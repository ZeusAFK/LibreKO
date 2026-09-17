using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IMiningPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
    Task StopGatheringAsync(UserSession session);
}

public class MiningPacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IPlayerProgressionService playerProgressionService,
    IUserNotificationService userNotificationService,
    ILogger<MiningPacketCoordinator> logger) : IMiningPacketCoordinator
{
    private const byte MiningStart = 1;
    private const byte MiningAttempt = 2;
    private const byte MiningStop = 3;
    private const byte BettingGame = 5;
    private const byte FishingStart = 6;
    private const byte FishingAttempt = 7;
    private const byte FishingStop = 8;

    private const byte KindPickaxe = 61;
    private const byte KindFishingRod = 63;

    private const int GoldenMattock = 389135000;
    private const int GoldenFishingRod = 191347000;
    private const int Rainworm = 508226000;

    private const double GatherDelaySeconds = 5;

    private const ushort ResultError = 0;
    private const ushort ResultSuccess = 1;
    private const ushort ResultAlready = 2;
    private const ushort ResultNotArea = 3;
    private const ushort ResultPreparing = 4;
    private const ushort ResultNoTool = 5;
    private const ushort ResultNothingFound = 6;
    private const ushort ResultNoEarthworm = 7;

    private const ushort EffectMiningItem = 13081;
    private const ushort EffectGatherExp = 13082;
    private const ushort EffectFishingItem = 30730;

    private const int WearMin = 2;
    private const int WearMax = 5;

    private const int BettingStake = 5000;
    private const int BettingPrize = 10000;
    private const byte BettingRollMin = 1;
    private const byte BettingRollMax = 5;
    private const ushort BettingWon = 1;
    private const ushort BettingTied = 2;
    private const ushort BettingLost = 3;
    private const ushort BettingNoCoins = 4;

    private static readonly int[] WeaponSlots =
        [InventoryConstants.RightHand, InventoryConstants.LeftHand];

    private readonly record struct EquippedTool(int Slot, int ItemId, GatherTool Tool);

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var subOpcode = packet.ReadByte();
        switch (subOpcode)
        {
            case MiningStart:
                await StartAsync(session, GatherType.Mining);
                break;

            case MiningAttempt:
                await AttemptAsync(session, GatherType.Mining);
                break;

            case MiningStop:
                await StopAsync(session, GatherType.Mining);
                break;

            case BettingGame:
                await BettingGameAsync(session);
                break;

            case FishingStart:
                await StartAsync(session, GatherType.Fishing);
                break;

            case FishingAttempt:
                await AttemptAsync(session, GatherType.Fishing);
                break;

            case FishingStop:
                await StopAsync(session, GatherType.Fishing);
                break;
        }
    }

    public async Task StopGatheringAsync(UserSession session)
    {
        if (session.IsMining)
            await StopAsync(session, GatherType.Mining);
        if (session.IsFishing)
            await StopAsync(session, GatherType.Fishing);
    }

    private static byte StartOpcode(GatherType type) =>
        type == GatherType.Mining ? MiningStart : FishingStart;

    private static byte AttemptOpcode(GatherType type) =>
        type == GatherType.Mining ? MiningAttempt : FishingAttempt;

    private static byte StopOpcode(GatherType type) =>
        type == GatherType.Mining ? MiningStop : FishingStop;

    private static bool IsActive(UserSession session, GatherType type) =>
        type == GatherType.Mining ? session.IsMining : session.IsFishing;

    private static void SetActive(UserSession session, GatherType type, bool active)
    {
        if (type == GatherType.Mining)
            session.IsMining = active;
        else
            session.IsFishing = active;
    }

    private static bool IsInArea(UserSession session, GatherType type) =>
        type == GatherType.Mining
            ? GatherZones.IsMiningArea(session.ZoneId, session.X, session.Z)
            : GatherZones.IsFishingArea(session.ZoneId, session.X, session.Z);

    private async Task StartAsync(UserSession session, GatherType type)
    {
        if (session.Hp <= 0)
            return;

        if (session.Trade.IsTrading || session.Trade.IsMerchanting)
        {
            await SendResultAsync(session, StartOpcode(type), ResultError);
            return;
        }

        var other = type == GatherType.Mining ? GatherType.Fishing : GatherType.Mining;
        if (IsActive(session, other))
            await StopAsync(session, other);

        var resultCode = ResultSuccess;
        if (IsActive(session, type))
            resultCode = ResultAlready;

        if (!IsInArea(session, type))
            resultCode = ResultNotArea;

        var tool = FindTool(session, type);
        if (tool == null)
            resultCode = ResultNoTool;
        else if (type == GatherType.Fishing && !HasBait(session, tool.Value))
            resultCode = ResultNoEarthworm;

        if (resultCode != ResultSuccess)
        {
            await SendResultAsync(session, StartOpcode(type), resultCode);
            return;
        }

        SetActive(session, type, true);
        session.LastGatherAttempt = DateTime.UtcNow;
        logger.LogDebug("{Name} started {GatherType}", session.Name, type);

        var packet = MiningPacketWriter.Broadcast(
            StartOpcode(type), ResultSuccess, session.CharacterId);
        await sessionManager.Regions.SendToRegion(session, packet, excludeSender: false);
    }

    private async Task AttemptAsync(UserSession session, GatherType type)
    {
        if (!IsActive(session, type) || session.Hp <= 0)
            return;

        if (session.Trade.IsTrading || session.Trade.IsMerchanting)
        {
            await FailAsync(session, type, ResultError);
            return;
        }

        if ((DateTime.UtcNow - session.LastGatherAttempt).TotalSeconds < GatherDelaySeconds)
        {
            await SendResultAsync(session, AttemptOpcode(type), ResultPreparing);
            return;
        }

        session.LastGatherAttempt = DateTime.UtcNow;

        if (!IsInArea(session, type))
        {
            await FailAsync(session, type, ResultNotArea);
            return;
        }

        var tool = FindTool(session, type);
        if (tool == null)
        {
            await FailAsync(session, type, ResultNoTool);
            return;
        }

        if (type == GatherType.Fishing && !HasBait(session, tool.Value))
        {
            await FailAsync(session, type, ResultNoEarthworm);
            return;
        }

        var reward = RollReward(session, type, tool.Value.Tool);
        if (reward == null)
        {
            await SendResultAsync(session, AttemptOpcode(type), ResultNothingFound);
            return;
        }

        ushort effect;
        if (reward.GiveItemNum == MiningFishingItemData.ExpRewardItemNum)
        {
            var exp = GetGatherExp(session.Level);
            if (exp > 0)
                await playerProgressionService.AwardExperienceAsync(session, exp);
            effect = EffectGatherExp;
        }
        else
        {
            var count = reward.GiveItemCount < 1 ? (ushort)1 : (ushort)reward.GiveItemCount;
            if (!await TryGiveItemAsync(session, reward.GiveItemNum, count))
            {
                await SendResultAsync(session, AttemptOpcode(type), ResultNothingFound);
                return;
            }

            effect = type == GatherType.Mining ? EffectMiningItem : EffectFishingItem;
        }

        await WearToolAsync(session);
        if (type == GatherType.Fishing && tool.Value.ItemId != GoldenFishingRod)
            await ConsumeBaitAsync(session);

        var packet = MiningPacketWriter.AttemptSucceeded(
            AttemptOpcode(type), ResultSuccess, session.CharacterId, effect);
        await sessionManager.Regions.SendToRegion(session, packet, excludeSender: false);
    }

    private async Task StopAsync(UserSession session, GatherType type)
    {
        if (!IsActive(session, type))
            return;

        SetActive(session, type, false);

        var broadcast = MiningPacketWriter.Broadcast(
            StopOpcode(type), ResultSuccess, session.CharacterId);
        await sessionManager.Regions.SendToRegion(session, broadcast, excludeSender: false);

        await session.Client.SendPacket(
            MiningPacketWriter.Result(StopOpcode(type), ResultAlready));
    }

    private async Task FailAsync(UserSession session, GatherType type, ushort resultCode)
    {
        await SendResultAsync(session, AttemptOpcode(type), resultCode);
        await StopAsync(session, type);
    }

    private async Task SendResultAsync(UserSession session, byte subOpcode, ushort resultCode)
    {
        await session.Client.SendPacket(MiningPacketWriter.Result(subOpcode, resultCode));
    }

    private EquippedTool? FindTool(UserSession session, GatherType type)
    {
        var wantedKind = type == GatherType.Mining ? KindPickaxe : KindFishingRod;
        var goldenId = type == GatherType.Mining ? GoldenMattock : GoldenFishingRod;

        foreach (var slotIndex in WeaponSlots)
        {
            var slot = session.Inventory[slotIndex];
            if (slot.IsEmpty || slot.Durability <= 0)
                continue;

            var itemData = gameDataService.GetItem(slot.ItemId);
            if (itemData == null || itemData.Kind != wantedKind)
                continue;

            var tool = slot.ItemId == goldenId ? GatherTool.Golden : GatherTool.Plain;
            return new EquippedTool(slotIndex, slot.ItemId, tool);
        }

        return null;
    }

    private static bool HasBait(UserSession session, EquippedTool tool) =>
        tool.ItemId == GoldenFishingRod || CountItem(session, Rainworm) > 0;

    private static int CountItem(UserSession session, int itemId)
    {
        int total = 0;
        for (int i = InventoryConstants.InventoryStart;
             i < InventoryConstants.InventoryStart + InventoryConstants.HaveMax; i++)
        {
            var slot = session.Inventory[i];
            if (slot.ItemId == itemId) total += slot.Count;
        }
        return total;
    }

    private async Task ConsumeBaitAsync(UserSession session)
    {
        for (int i = InventoryConstants.InventoryStart;
             i < InventoryConstants.InventoryStart + InventoryConstants.HaveMax; i++)
        {
            var slot = session.Inventory[i];
            if (slot.ItemId != Rainworm || slot.Count == 0)
                continue;

            slot.Count -= 1;
            var remaining = slot.Count;
            var durability = slot.Durability;
            if (remaining == 0) slot.Clear();

            await userNotificationService.SendStackChangeAsync(
                session, (byte)i, Rainworm, remaining, durability);
            await userNotificationService.SendWeightChangeAsync(session);
            return;
        }
    }

    private MiningFishingItemData? RollReward(UserSession session, GatherType type, GatherTool tool)
    {
        var pool = gameDataService
            .MiningFishingItemsByPool[(type, tool, GetWarStatus(session))]
            .ToArray();

        if (pool.Length == 0)
            return null;

        long total = 0;
        foreach (var row in pool)
            total += row.SuccessRate;

        if (total <= 0)
            return null;

        var roll = Random.Shared.NextInt64(total);
        long offset = 0;
        foreach (var row in pool)
        {
            offset += row.SuccessRate;
            if (roll < offset)
                return row;
        }

        return pool[^1];
    }

    private GatherWarStatus GetWarStatus(UserSession session)
    {
        var battle = sessionManager.Battle;
        if (battle.BattleOpen != BattleZoneManager.NATION_BATTLE)
            return GatherWarStatus.Peace;

        if (session.ZoneId is not BattleZoneManager.ZONE_KARUS and not BattleZoneManager.ZONE_ELMORAD)
            return GatherWarStatus.Peace;

        if (battle.Victory == 0)
            return GatherWarStatus.Peace;

        return battle.Victory == (byte)session.Nation
            ? GatherWarStatus.Winner
            : GatherWarStatus.Loser;
    }

    private static long GetGatherExp(byte level) => level switch
    {
        <= 34 => 50L,
        <= 59 => 100L,
        <= 69 => 200L,
        <= 83 => 300L,
        _ => 0L,
    };

    private async Task WearToolAsync(UserSession session)
    {
        var cost = (short)Random.Shared.Next(WearMin, WearMax + 1);
        var broke = false;

        foreach (var slotIndex in WeaponSlots)
        {
            var slot = session.Inventory[slotIndex];
            if (slot.IsEmpty || slot.Durability <= 0)
                continue;

            var itemData = gameDataService.GetItem(slot.ItemId);
            if (itemData == null || itemData.IsShield())
                continue;

            slot.Durability = (short)Math.Max(0, slot.Durability - cost);
            broke |= slot.Durability == 0;

            await session.Client.SendPacket(
                MiningPacketWriter.Durability((byte)slotIndex, slot.Durability));
        }

        if (!broke)
            return;

        session.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendStatUpdateAsync(session);
    }

    private async Task BettingGameAsync(UserSession session)
    {
        if (session.Hp <= 0)
            return;

        var result = BettingNoCoins;
        byte playerRoll = 0;
        byte npcRoll = 0;

        if (session.Money >= BettingStake)
        {
            session.Money -= BettingStake;
            await userNotificationService.SendGoldLossAsync(session, BettingStake);

            playerRoll = (byte)Random.Shared.Next(BettingRollMin, BettingRollMax + 1);
            npcRoll = (byte)Random.Shared.Next(BettingRollMin, BettingRollMax + 1);

            if (playerRoll > npcRoll)
            {
                result = BettingWon;
                session.Money += BettingPrize;
                await userNotificationService.SendGoldGainAsync(session, BettingPrize);
            }
            else if (playerRoll < npcRoll)
            {
                result = BettingLost;
            }
            else
            {
                result = BettingTied;
            }
        }

        var packet = MiningPacketWriter.BettingResult(
            BettingGame, result, session.CharacterId, playerRoll, npcRoll);
        await sessionManager.Regions.SendToRegion(session, packet, excludeSender: false);
    }

    private async Task<bool> TryGiveItemAsync(UserSession session, int itemId, ushort count)
    {
        var itemData = gameDataService.GetItem(itemId);
        if (itemData == null)
            return false;

        var addedWeight = (long)itemData.Weight * count;
        if (session.Stats.ItemWeight + addedWeight > session.Stats.MaxWeight)
            return false;

        var slotIndex = session.FindSlotForItem(itemId, gameDataService, count);
        if (slotIndex < 0)
            return false;

        var slot = session.Inventory[slotIndex];
        var isNewItem = slot.IsEmpty;
        slot.ItemId = itemId;
        slot.Count += count;
        if (isNewItem)
            slot.Durability = itemData.Duration;

        await userNotificationService.SendStackChangeAsync(
            session,
            (byte)slotIndex,
            itemId,
            slot.Count,
            slot.Durability,
            isNewItem);

        session.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendWeightChangeAsync(session);
        return true;
    }
}
