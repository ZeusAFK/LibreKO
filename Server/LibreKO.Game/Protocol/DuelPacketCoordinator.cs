using System.Collections.Generic;
using System.Linq;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IDuelPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class DuelPacketCoordinator(
    SessionManager sessionManager,
    ILogger<DuelPacketCoordinator> logger) : IDuelPacketCoordinator
{
    private const byte DuelSubList = 1;
    private const byte DuelSubCreate = 2;
    private const byte DuelSubJoin = 3;
    private const byte DuelSubLeave = 4;

    private const int DuelMinStake = 0;
    private const int DuelMaxStake = 100_000_000;

    private sealed class Duel
    {
        public int DuelId;
        public int DuelCreatorId;
        public string DuelCreatorName = "";
        public int DuelJoinerId;          // 0 = open slot (no joiner yet)
        public int DuelStake;
    }

    // The lobby (in-memory; resets on server restart). Guarded by DuelGate for the list/join/leave races.
    private static readonly List<Duel> DuelLobby = new();
    private static readonly object DuelGate = new();
    private static int DuelNextId = 1;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case DuelSubList:
                await SendDuelListAsync(session);
                break;
            case DuelSubCreate:
                await HandleDuelCreateAsync(session, packet);
                break;
            case DuelSubJoin:
                await HandleDuelJoinAsync(session, packet);
                break;
            case DuelSubLeave:
                await HandleDuelLeaveAsync(session, packet);
                break;
        }
    }

    private async Task SendDuelListAsync(UserSession session)
    {
        Duel[] snapshot;
        lock (DuelGate)
            snapshot = DuelLobby.ToArray();

        var listings = new List<DuelPacketWriter.Listing>(snapshot.Length);
        foreach (var d in snapshot)
        {
            listings.Add(new DuelPacketWriter.Listing(
                d.DuelId, d.DuelCreatorName, d.DuelStake, d.DuelJoinerId != 0));
        }

        await session.Client.SendPacket(DuelPacketWriter.DuelList(DuelSubList, listings));
    }

    private async Task HandleDuelCreateAsync(UserSession session, Packet packet)
    {
        int stake = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;

        if (stake < DuelMinStake || stake > DuelMaxStake)
        {
            await session.Client.SendPacket(DuelPacketWriter.Result(
                DuelSubCreate, DuelPacketWriter.Failed, DuelPacketWriter.NoDuelId));
            return;
        }

        int newId;
        Packet result;
        lock (DuelGate)
        {
            // One open duel per creator: if they already host one, refuse a duplicate.
            if (DuelLobby.Any(d => d.DuelCreatorId == session.CharacterId))
            {
                result = DuelPacketWriter.Result(
                    DuelSubCreate, DuelPacketWriter.Failed, DuelPacketWriter.NoDuelId);
            }
            else
            {
                newId = DuelNextId++;
                DuelLobby.Add(new Duel
                {
                    DuelId = newId,
                    DuelCreatorId = session.CharacterId,
                    DuelCreatorName = session.Name,
                    DuelJoinerId = 0,
                    DuelStake = stake,
                });
                logger.LogDebug("{Name} created duel {Id} for stake {Stake}", session.Name, newId, stake);
                result = DuelPacketWriter.Result(
                    DuelSubCreate, DuelPacketWriter.Succeeded, newId);
            }
        }

        await session.Client.SendPacket(result);
        await SendDuelListAsync(session);
    }

    private async Task HandleDuelJoinAsync(UserSession session, Packet packet)
    {
        int duelId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;


        bool ok;
        lock (DuelGate)
        {
            var duel = DuelLobby.FirstOrDefault(d => d.DuelId == duelId);
            // Can't join: no such duel, already full, or your own duel.
            if (duel == null || duel.DuelJoinerId != 0 || duel.DuelCreatorId == session.CharacterId)
            {
                ok = false;
            }
            else
            {
                duel.DuelJoinerId = session.CharacterId;
                ok = true;
                logger.LogDebug("{Name} joined duel {Id}", session.Name, duelId);
            }
        }

        await session.Client.SendPacket(DuelPacketWriter.Result(
            DuelSubJoin, ok ? DuelPacketWriter.Succeeded : DuelPacketWriter.Failed, duelId));
        await SendDuelListAsync(session);
    }

    private async Task HandleDuelLeaveAsync(UserSession session, Packet packet)
    {
        int duelId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;


        bool ok = false;
        lock (DuelGate)
        {
            var duel = DuelLobby.FirstOrDefault(d => d.DuelId == duelId);
            if (duel != null &&
                (duel.DuelCreatorId == session.CharacterId || duel.DuelJoinerId == session.CharacterId))
            {
                if (duel.DuelCreatorId == session.CharacterId)
                    DuelLobby.Remove(duel);          // creator leaving disbands the duel
                else
                    duel.DuelJoinerId = 0;           // joiner leaving reopens the slot
                ok = true;
                logger.LogDebug("{Name} left duel {Id}", session.Name, duelId);
            }
        }

        await session.Client.SendPacket(DuelPacketWriter.Result(
            DuelSubLeave, ok ? DuelPacketWriter.Succeeded : DuelPacketWriter.Failed, duelId));
        await SendDuelListAsync(session);
    }
}
