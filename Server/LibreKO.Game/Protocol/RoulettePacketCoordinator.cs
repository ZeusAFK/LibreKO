using System;
using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IRoulettePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class RoulettePacketCoordinator(
    SessionManager sessionManager,
    IUserNotificationService userNotification,
    ILogger<RoulettePacketCoordinator> logger) : IRoulettePacketCoordinator
{

    private const byte RouletteResultFail = RoulettePacketWriter.ResultFail;
    private const byte RouletteResultSpun = RoulettePacketWriter.ResultOk;

    private const int RouletteSpinCost = 1;          // coins per spin
    private const int RouletteStartingCoins = 10;    // seeded the first time a char checks status

    // One slice of the prize wheel. A slice is either an item (ItemId != 0) or gold (Gold != 0).
    private readonly struct RouletteSlice(int weight, int itemId, int gold)
    {
        public readonly int Weight = weight;
        public readonly int ItemId = itemId;
        public readonly int Gold = gold;
    }

    // Static weighted prize wheel (shared by all characters). Higher weight = more likely.
    private static readonly RouletteSlice[] RouletteWheel =
    {
        new(40, 0, 1_000),          // common: 1k gold
        new(25, 0, 5_000),          // 5k gold
        new(15, 379_022_000, 0),    // an item (HP potion id, illustrative)
        new(10, 0, 25_000),         // 25k gold
        new(7,  385_000_000, 0),    // another item
        new(3,  0, 100_000),        // 100k gold jackpot
    };

    private static readonly int RouletteWheelTotalWeight = ComputeTotalWeight();

    // charId -> remaining event coins (in-memory; resets on server restart).
    private static readonly Dictionary<int, int> rouletteCoins = new();
    // charId -> recent prize log (newest last); kept for future "history" sub-opcode / debugging.
    private static readonly Dictionary<int, List<RoulettePacketWriter.PrizeLogEntry>> roulettePrizeLog = new();

    private static readonly Random rouletteRng = new();
    private static readonly object rouletteLock = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = (EventBoardSubOpcode)packet.ReadByte();
        switch (sub)
        {
            case EventBoardSubOpcode.RouletteOpen:
                await SendRouletteStatusAsync(session);
                break;
            case EventBoardSubOpcode.RouletteSpin:
                await HandleRouletteSpinAsync(session);
                break;
            case EventBoardSubOpcode.RoulettePrizeList:
                await SendRoulettePrizeLogAsync(session);
                break;
        }
    }

    private async Task SendRouletteStatusAsync(UserSession session)
    {
        int coins = GetRouletteCoins(session.CharacterId);
        await session.Client.SendPacket(RoulettePacketWriter.Open(coins));
    }

    private async Task SendRoulettePrizeLogAsync(UserSession session)
    {
        List<RoulettePacketWriter.PrizeLogEntry> entries;
        lock (rouletteLock)
        {
            entries = roulettePrizeLog.TryGetValue(session.CharacterId, out var log)
                ? new List<RoulettePacketWriter.PrizeLogEntry>(log)
                : [];
        }

        entries.Reverse();
        await session.Client.SendPacket(RoulettePacketWriter.PrizeList(1, entries));
    }

    private async Task HandleRouletteSpinAsync(UserSession session)
    {
        int prizeItemId = 0;
        int prizeGold = 0;
        byte rouletteResult;

        lock (rouletteLock)
        {
            int coins = GetRouletteCoinsUnlocked(session.CharacterId);
            if (coins < RouletteSpinCost)
            {
                rouletteResult = RouletteResultFail;
            }
            else
            {
                rouletteCoins[session.CharacterId] = coins - RouletteSpinCost;
                var slice = RollRouletteSlice();
                prizeItemId = slice.ItemId;
                // Deterministic spin prize gold per character (the wheel's item slices still drive the
                // prize log / future item grants; the gold payout itself is fixed so it's reproducible).
                prizeGold = ComputeRoulettePrizeGold(session.CharacterId);
                AppendRoulettePrizeLog(session.CharacterId, prizeItemId, prizeGold);
                rouletteResult = RouletteResultSpun;
            }
        }

        // Grant the real gold once, only on a successful spin (the coin-deduction guard above ran exactly once).
        if (rouletteResult == RouletteResultSpun)
        {
            session.Money += prizeGold;
            await userNotification.SendGoldGainAsync(session, prizeGold);   // grants gold + GS_GOLD_CHANGE
            logger.LogDebug("{Name} spun roulette -> item {Item} gold {Gold}", session.Name, prizeItemId, prizeGold);
        }

        await session.Client.SendPacket(
            RoulettePacketWriter.Spin(rouletteResult, prizeItemId, prizeGold));
    }

    // Deterministic spin prize gold: 5000 + (CharacterId % 20) * 2500  ->  5,000 .. 52,500 gold.
    private static int ComputeRoulettePrizeGold(int charId) =>
        5_000 + (charId % 20) * 2_500;

    private static RouletteSlice RollRouletteSlice()
    {
        int roll = rouletteRng.Next(RouletteWheelTotalWeight);
        int acc = 0;
        foreach (var slice in RouletteWheel)
        {
            acc += slice.Weight;
            if (roll < acc)
                return slice;
        }
        return RouletteWheel[^1];
    }

    private static int ComputeTotalWeight()
    {
        int total = 0;
        foreach (var slice in RouletteWheel)
            total += slice.Weight;
        return total < 1 ? 1 : total;
    }

    private static int GetRouletteCoins(int charId)
    {
        lock (rouletteLock)
            return GetRouletteCoinsUnlocked(charId);
    }

    private static int GetRouletteCoinsUnlocked(int charId)
    {
        if (!rouletteCoins.TryGetValue(charId, out var coins))
        {
            coins = RouletteStartingCoins;
            rouletteCoins[charId] = coins;
        }
        return coins;
    }

    private static void AppendRoulettePrizeLog(int charId, int itemId, int gold)
    {
        if (!roulettePrizeLog.TryGetValue(charId, out var log))
        {
            log = [];
            roulettePrizeLog[charId] = log;
        }

        var quantity = itemId != 0 ? 1 : gold;
        log.Add(new RoulettePacketWriter.PrizeLogEntry(
            itemId, quantity, (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

        if (log.Count > RoulettePacketWriter.PrizeListMaxEntries)
            log.RemoveAt(0);
    }
}
