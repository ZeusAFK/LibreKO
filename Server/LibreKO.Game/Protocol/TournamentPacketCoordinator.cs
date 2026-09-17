using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface ITournamentPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class TournamentPacketCoordinator(
    SessionManager sessionManager,
    ILogger<TournamentPacketCoordinator> logger) : ITournamentPacketCoordinator
{
    private const byte TournamentSubStatus = 1;
    private const byte TournamentSubRegister = 2;
    private const byte TournamentSubUnregister = 3;

    // charIds currently registered for the arena tournament (in-memory; resets on server restart).
    private static readonly HashSet<int> TournamentRegistrants = new();

    // Current bracket round (1-based). Static so every participant sees the same round.
    private static byte _tournamentRound = 1;

    private static readonly object TournamentLock = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case TournamentSubStatus:
                await SendTournamentStatusAsync(session);
                break;
            case TournamentSubRegister:
                await HandleTournamentRegisterAsync(session, register: true);
                break;
            case TournamentSubUnregister:
                await HandleTournamentRegisterAsync(session, register: false);
                break;
        }
    }

    private async Task SendTournamentStatusAsync(UserSession session)
    {
        bool registered;
        ushort participantCount;
        byte round;
        lock (TournamentLock)
        {
            registered = TournamentRegistrants.Contains(session.CharacterId);
            participantCount = (ushort)TournamentRegistrants.Count;
            round = _tournamentRound;
        }

        var result = TournamentPacketWriter.Status(
            TournamentSubStatus, registered, participantCount, round);
        await session.Client.SendPacket(result);
    }

    private async Task HandleTournamentRegisterAsync(UserSession session, bool register)
    {
        byte resultFlag;
        lock (TournamentLock)
        {
            if (register)
                TournamentRegistrants.Add(session.CharacterId);
            else
                TournamentRegistrants.Remove(session.CharacterId);
            resultFlag = TournamentRegistrants.Contains(session.CharacterId) ? (byte)1 : (byte)0;
        }

        logger.LogDebug("{Name} tournament {Action} -> registered={Flag}",
            session.Name, register ? "register" : "unregister", resultFlag);

        var result = TournamentPacketWriter.Result(
            register ? TournamentSubRegister : TournamentSubUnregister, resultFlag);
        await session.Client.SendPacket(result);
    }
}
