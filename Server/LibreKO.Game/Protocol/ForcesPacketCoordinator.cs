using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IForcesPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class ForcesPacketCoordinator(
    SessionManager sessionManager,
    ILogger<ForcesPacketCoordinator> logger) : IForcesPacketCoordinator
{
    private const byte ForcesSubStatus = 1;
    private const byte ForcesSubJoin = 2;
    private const byte ForcesSubLeave = 3;

    // Rank thresholds (force points needed to reach each rank). Rank 0 = "Recruit" / not yet ranked.
    private static readonly int[] ForcesRankPoints = { 0, 100, 300, 700, 1500, 3000 };

    // Per-character force membership state (in-memory; resets on server restart).
    private sealed class ForcesState
    {
        public bool Joined;
        public int Points;
        public byte AngerPct;
    }

    private static readonly Dictionary<int, ForcesState> ForcesMembers = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case ForcesSubStatus:
                await ForcesSendStatusAsync(session);
                break;
            case ForcesSubJoin:
                await ForcesHandleJoinAsync(session);
                break;
            case ForcesSubLeave:
                await ForcesHandleLeaveAsync(session);
                break;
        }
    }

    private async Task ForcesHandleJoinAsync(UserSession session)
    {
        var state = ForcesGetState(session.CharacterId);
        byte result;
        if (state.Joined)
        {
            result = 0; // already a member
        }
        else
        {
            state.Joined = true;
            // Seed a starting bounty of force points so a fresh member has a visible rank/gauge.
            state.Points = 100;
            state.AngerPct = 20;
            result = 1;
            logger.LogDebug("{Name} joined the nation force", session.Name);
        }

        await session.Client.SendPacket(ForcesPacketWriter.Result(ForcesSubJoin, result));

        await ForcesSendStatusAsync(session);
    }

    private async Task ForcesHandleLeaveAsync(UserSession session)
    {
        var state = ForcesGetState(session.CharacterId);
        byte result;
        if (!state.Joined)
        {
            result = 0; // not a member
        }
        else
        {
            state.Joined = false;
            state.Points = 0;
            state.AngerPct = 0;
            result = 1;
            logger.LogDebug("{Name} left the nation force", session.Name);
        }

        await session.Client.SendPacket(ForcesPacketWriter.Result(ForcesSubLeave, result));

        await ForcesSendStatusAsync(session);
    }

    private async Task ForcesSendStatusAsync(UserSession session)
    {
        var state = ForcesGetState(session.CharacterId);
        byte rank = ForcesRankFor(state.Points);

        await session.Client.SendPacket(ForcesPacketWriter.Status(
            ForcesSubStatus, state.Joined, state.Points, rank, state.AngerPct));
    }

    private static byte ForcesRankFor(int points)
    {
        byte rank = 0;
        for (byte i = 0; i < ForcesRankPoints.Length; i++)
        {
            if (points >= ForcesRankPoints[i])
                rank = i;
        }
        return rank;
    }

    private static ForcesState ForcesGetState(int charId)
    {
        if (!ForcesMembers.TryGetValue(charId, out var state))
        {
            state = new ForcesState();
            ForcesMembers[charId] = state;
        }
        return state;
    }
}
