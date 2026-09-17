using System;
using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IGeniePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class GeniePacketCoordinator(
    SessionManager sessionManager,
    IUserNotificationService userNotification,
    ILogger<GeniePacketCoordinator> logger) : IGeniePacketCoordinator
{
    private const byte GenieSubStatus = 1;
    private const byte GenieSubClaim  = 2;

    private const int GenieRewardGold = 20000;    // flat daily genie gift

    // Contextual tips by ascending level threshold; the highest threshold <= my level wins.
    private static readonly (int MinLevel, string Tip)[] GenieTips =
    {
        (1,  "Welcome! Talk to town NPCs to pick up your first quests."),
        (10, "Slot skills onto your hotkey bar (1-8) and press R to auto-attack."),
        (20, "Repair your gear at a blacksmith before it breaks in the field."),
        (30, "Join a party (P) to share experience and clear tougher monsters."),
        (40, "Visit the merchant to sell loot and stock up on potions."),
        (50, "High-grade armor and enchanted weapons make a real difference now."),
        (60, "Claim your daily genie reward every day — the gold adds up!"),
        (70, "You're elite — chase rare drops and help your nation in the field."),
    };

    // charId -> last UTC day index on which the daily reward was claimed.
    private static readonly Dictionary<int, int> lastClaimDay = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case GenieSubStatus:
                await SendGenieStatusAsync(session);
                break;
            case GenieSubClaim:
                await HandleGenieClaimAsync(session);
                break;
        }
    }

    private async Task SendGenieStatusAsync(UserSession session)
    {
        string tip = PickGenieTip(session.Level);
        bool rewardAvail = !GenieClaimedToday(session.CharacterId);

        await session.Client.SendPacket(
            GeniePacketWriter.Status(GenieSubStatus, tip, rewardAvail));
    }

    private async Task HandleGenieClaimAsync(UserSession session)
    {

        if (GenieClaimedToday(session.CharacterId))
        {
            await session.Client.SendPacket(GeniePacketWriter.ClaimResult(
                GenieSubClaim, GeniePacketWriter.Failed, GeniePacketWriter.NoReward));
            return;
        }

        lastClaimDay[session.CharacterId] = GenieTodayIndex();
        session.Money += GenieRewardGold;
        await userNotification.SendGoldGainAsync(session, GenieRewardGold);   // grants gold + GS_GOLD_CHANGE
        logger.LogDebug("{Name} claimed daily genie reward ({Gold} gold)", session.Name, GenieRewardGold);

        await session.Client.SendPacket(GeniePacketWriter.ClaimResult(
            GenieSubClaim, GeniePacketWriter.Succeeded, GenieRewardGold));
    }

    private static string PickGenieTip(byte level)
    {
        string tip = GenieTips[0].Tip;
        foreach (var (minLevel, text) in GenieTips)
        {
            if (level >= minLevel) tip = text;
            else break;
        }
        return tip;
    }

    private static bool GenieClaimedToday(int charId)
        => lastClaimDay.TryGetValue(charId, out var day) && day == GenieTodayIndex();

    private static int GenieTodayIndex()
        => (int)(DateTime.UtcNow.Date - DateTime.UnixEpoch).TotalDays;
}
