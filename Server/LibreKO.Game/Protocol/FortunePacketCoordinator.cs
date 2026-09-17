using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IFortunePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class FortunePacketCoordinator(
    SessionManager sessionManager,
    IUserNotificationService userNotification,
    ILogger<FortunePacketCoordinator> logger) : IFortunePacketCoordinator
{
    private const byte FortuneSubStatus = 1;
    private const byte FortuneSubDraw = 2;

    // Reward pool the server rolls from. (itemId, gold). itemId 0 = a pure-gold prize.
    private static readonly (int FortuneItemId, int FortuneGold)[] FortuneRewards =
    {
        (0, 1000),            // consolation gold
        (0, 5000),            // gold
        (379022000, 0),       // a scroll / potion stack (sample item id)
        (700001000, 0),       // a misc reward item (sample item id)
        (0, 25000),           // jackpot gold
    };

    // charId -> has drawn since server start (in-memory; the per-day reset is approximated by restart).
    private static readonly HashSet<int> fortuneDrawn = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case FortuneSubStatus:
                await SendFortuneStatusAsync(session);
                break;
            case FortuneSubDraw:
                await HandleFortuneDrawAsync(session);
                break;
        }
    }

    private static async Task SendFortuneStatusAsync(UserSession session)
    {
        bool canDraw = !fortuneDrawn.Contains(session.CharacterId);
        await session.Client.SendPacket(
            FortunePacketWriter.Status(FortuneSubStatus, canDraw));
    }

    private async Task HandleFortuneDrawAsync(UserSession session)
    {

        if (fortuneDrawn.Contains(session.CharacterId))
        {
            await session.Client.SendPacket(FortunePacketWriter.DrawResult(
                FortuneSubDraw, FortunePacketWriter.Failed,
                FortunePacketWriter.NoReward, FortunePacketWriter.NoReward));
            return;
        }

        fortuneDrawn.Add(session.CharacterId);

        // Roll a reward. Deterministic-ish per character so the same restart session is stable.
        int idx = (int)((uint)(session.CharacterId * 2654435761u) % (uint)FortuneRewards.Length);
        var (rewardItemId, rewardGold) = FortuneRewards[idx];

        // Daily fortune payout. Math.Random is unavailable here, so derive a "random-ish" but
        // per-character-stable gold prize from the character id, and add it to any rolled gold prize.
        int fortuneGold = 10000 + (session.CharacterId % 10) * 5000;
        rewardGold += fortuneGold;

        // Grant the gold once (this branch runs only on the first draw, behind the fortuneDrawn guard).
        session.Money += rewardGold;
        await userNotification.SendGoldGainAsync(session, rewardGold);   // adds gold + sends GS_GOLD_CHANGE

        logger.LogDebug("{Name} drew fortune item={Item} gold={Gold}", session.Name, rewardItemId, rewardGold);

        await session.Client.SendPacket(FortunePacketWriter.DrawResult(
            FortuneSubDraw, FortunePacketWriter.Succeeded, rewardItemId, rewardGold));
    }
}
