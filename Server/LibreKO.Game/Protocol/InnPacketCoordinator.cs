using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IInnPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class InnPacketCoordinator(
    SessionManager sessionManager,
    ILogger<InnPacketCoordinator> logger) : IInnPacketCoordinator
{
    private const byte InnSubStatus  = 1;
    private const byte InnSubSetHome = 2;
    private const byte InnSubRest    = 3;

    private const byte InnResultOk  = 1;
    private const byte InnResultBad = 0;

    // charId -> saved inn zone id (the town bound as recall point). In-memory; resets on restart.
    private static readonly Dictionary<int, ushort> InnSavedZone = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case InnSubStatus:
                await InnSendStatusAsync(session);
                break;
            case InnSubSetHome:
                await InnHandleSetHomeAsync(session);
                break;
            case InnSubRest:
                await InnHandleRestAsync(session);
                break;
        }
    }

    private async Task InnSendStatusAsync(UserSession session)
    {
        bool saved = InnSavedZone.TryGetValue(session.CharacterId, out var zone);
        await session.Client.SendPacket(InnPacketWriter.HomeZone(
            InnSubStatus,
            saved ? InnResultOk : InnResultBad,
            saved ? zone : (ushort)0));
    }

    private async Task InnHandleSetHomeAsync(UserSession session)
    {

        // Bind the town the player is currently standing in (their live zone) as the recall point.
        ushort zone = session.ZoneId;
        if (zone == 0)
        {
            await session.Client.SendPacket(InnPacketWriter.HomeZone(
                InnSubSetHome, InnResultBad, 0));
            return;
        }

        InnSavedZone[session.CharacterId] = zone;
        logger.LogDebug("{Name} set inn home zone {Zone}", session.Name, zone);
        await session.Client.SendPacket(InnPacketWriter.HomeZone(
            InnSubSetHome, InnResultOk, zone));
    }

    private async Task InnHandleRestAsync(UserSession session)
    {
        // The server has no HP/MP-over-time tick for this convenience feature; it simply acks the request
        // and the client plays out the local recovery animation/regen. We reject only if somehow out of world.
        await session.Client.SendPacket(InnPacketWriter.Result(
            InnSubRest, session.CharacterId != 0 ? InnResultOk : InnResultBad));
    }
}
