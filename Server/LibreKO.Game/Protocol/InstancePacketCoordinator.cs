using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IInstancePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class InstancePacketCoordinator(
    SessionManager sessionManager,
    ILogger<InstancePacketCoordinator> logger) : IInstancePacketCoordinator
{
    private const byte InstanceSubList = 1;
    private const byte InstanceSubEnter = 2;
    private const byte InstanceSubLeave = 3;

    private const byte InstanceResultFail = 0;
    private const byte InstanceResultOk = 1;

    // Static catalog of instance dungeons: id, display name, minimum level, recommended party size.
    private static readonly InstanceDef[] InstanceCatalog =
    {
        new(1, "Forgotten Temple", 10, 3),
        new(2, "Draky's Nest", 30, 4),
        new(3, "Juraid Mountain", 50, 6),
        new(4, "Ronark Land Lair", 60, 8),
        new(5, "Bifrost Sanctuary", 70, 8),
    };

    // charId -> current instanceId (0 = not inside any instance). In-memory; resets on server restart.
    private static readonly Dictionary<int, int> instanceCurrent = new();

    private readonly record struct InstanceDef(int Id, string Name, byte MinLevel, ushort PartySize);

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case InstanceSubList:
                await SendInstanceListAsync(session);
                break;
            case InstanceSubEnter:
                await HandleInstanceEnterAsync(session, packet);
                break;
            case InstanceSubLeave:
                await HandleInstanceLeaveAsync(session);
                break;
        }
    }

    private static async Task SendInstanceListAsync(UserSession session)
    {
        var entries = InstanceCatalog
            .Select(def => new InstancePacketWriter.Entry(
                def.Id, def.Name, def.MinLevel, def.PartySize))
            .ToList();

        await session.Client.SendPacket(
            InstancePacketWriter.InstanceList(InstanceSubList, entries));
    }

    private async Task HandleInstanceEnterAsync(UserSession session, Packet packet)
    {
        int instanceId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        var def = System.Array.Find(InstanceCatalog, d => d.Id == instanceId);

        bool ok = def.Id != 0
                  && session.Level >= def.MinLevel
                  && GetCurrentInstance(session.CharacterId) == 0;
        if (ok)
        {
            instanceCurrent[session.CharacterId] = instanceId;
            logger.LogDebug("{Name} entered instance {Instance}", session.Name, instanceId);
        }

        await session.Client.SendPacket(InstancePacketWriter.Result(
            InstanceSubEnter, ok ? InstanceResultOk : InstanceResultFail, instanceId));
    }

    private async Task HandleInstanceLeaveAsync(UserSession session)
    {
        int current = GetCurrentInstance(session.CharacterId);
        bool ok = current != 0;
        if (ok)
        {
            instanceCurrent[session.CharacterId] = 0;
            logger.LogDebug("{Name} left instance {Instance}", session.Name, current);
        }

        await session.Client.SendPacket(InstancePacketWriter.Result(
            InstanceSubLeave, ok ? InstanceResultOk : InstanceResultFail, current));
    }

    private static int GetCurrentInstance(int charId)
        => instanceCurrent.TryGetValue(charId, out var id) ? id : 0;
}
