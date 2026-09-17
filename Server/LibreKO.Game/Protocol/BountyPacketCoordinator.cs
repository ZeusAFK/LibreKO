using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IBountyPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class BountyPacketCoordinator(
    SessionManager sessionManager,
    IUserNotificationService userNotification,
    ILogger<BountyPacketCoordinator> logger) : IBountyPacketCoordinator
{
    private const byte BountySubList = 1;
    private const byte BountySubPost = 2;
    private const byte BountySubClaim = 3;

    private const int BountyMinReward = 1;
    private const int BountyMaxReward = 100_000_000;

    private sealed class Bounty
    {
        public int BountyId;
        public string BountyTarget = "";
        public string BountyPoster = "";
        public int BountyReward;
    }

    // The open board (in-memory; resets on server restart). Guarded by BountyGate for the list/claim race.
    private static readonly List<Bounty> BountyBoard = new();
    private static readonly object BountyGate = new();
    private static int BountyNextId = 1;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case BountySubList:
                await SendBountyListAsync(session);
                break;
            case BountySubPost:
                await HandleBountyPostAsync(session, packet);
                break;
            case BountySubClaim:
                await HandleBountyClaimAsync(session, packet);
                break;
        }
    }

    private async Task SendBountyListAsync(UserSession session)
    {
        Bounty[] snapshot;
        lock (BountyGate)
            snapshot = BountyBoard.ToArray();

        var entries = snapshot
            .Select(b => new BountyPacketWriter.Entry(
                b.BountyId, b.BountyTarget, b.BountyPoster, b.BountyReward))
            .ToList();

        await session.Client.SendPacket(BountyPacketWriter.BountyList(BountySubList, entries));
    }

    private async Task HandleBountyPostAsync(UserSession session, Packet packet)
    {
        string target = packet.RemainingBytes >= 1 ? packet.ReadSByteString() : "";
        int reward = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;


        target = target.Trim();
        if (target.Length == 0 || reward < BountyMinReward || reward > BountyMaxReward)
        {
            await session.Client.SendPacket(BountyPacketWriter.Result(
                BountySubPost, BountyPacketWriter.Failed));
            return;
        }

        lock (BountyGate)
        {
            BountyBoard.Add(new Bounty
            {
                BountyId = BountyNextId++,
                BountyTarget = target,
                BountyPoster = session.Name,
                BountyReward = reward,
            });
        }

        logger.LogDebug("{Name} posted bounty on {Target} for {Reward}", session.Name, target, reward);
        await session.Client.SendPacket(BountyPacketWriter.Result(
            BountySubPost, BountyPacketWriter.Succeeded));

        // Push the refreshed board so the poster (and form) sees their new entry immediately.
        await SendBountyListAsync(session);
    }

    private async Task HandleBountyClaimAsync(UserSession session, Packet packet)
    {
        int bountyId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;


        Bounty? claimed = null;
        lock (BountyGate)
        {
            var entry = BountyBoard.FirstOrDefault(b => b.BountyId == bountyId);
            if (entry != null)
            {
                BountyBoard.Remove(entry);
                claimed = entry;
            }
        }

        if (claimed == null)
        {
            await session.Client.SendPacket(BountyPacketWriter.BountyResult(
                BountySubClaim, BountyPacketWriter.Failed, bountyId));
            return;
        }

        // Pay the bounty reward to the claimer. Guarded one-shot: only the thread that removed this
        // bounty from the board under BountyGate reaches here, so the reward is granted exactly once.
        session.Money += claimed.BountyReward;
        await userNotification.SendGoldGainAsync(session, claimed.BountyReward);   // grants gold + GS_GOLD_CHANGE

        logger.LogDebug("{Name} claimed bounty {Id} on {Target} for {Reward}",
            session.Name, claimed.BountyId, claimed.BountyTarget, claimed.BountyReward);
        await session.Client.SendPacket(BountyPacketWriter.BountyResult(
            BountySubClaim, BountyPacketWriter.Succeeded, bountyId));

        // Refresh the board for the claimer so the claimed entry disappears.
        await SendBountyListAsync(session);
    }
}
