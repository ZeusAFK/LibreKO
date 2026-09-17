using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

using LibreKO.Game.Protocol.Writers;

using LibreKO.Common.Enums;

namespace LibreKO.Game.Protocol;

public interface IWorldObjectEventService
{
    Task HandleObjectEventAsync(IClient client, Packet packet);
}

public class WorldObjectEventService(
    SessionManager sessionManager,
    IWorldMovementService worldMovementService,
    ILogger<WorldObjectEventService> logger) : IWorldObjectEventService
{
    private const byte ObjectEventFailed = 0;
    private const byte ObjectEventSucceeded = 1;

    private const float MaxObjectRangeSq = 100.0f;

    private const byte ObjectBind = 0;
    private const byte ObjectGate = 1;
    private const byte ObjectGateLever = 2;
    private const byte ObjectFlagLever = 3;
    private const byte ObjectWarpGate = 5;
    private const byte ObjectRemoveBind = 7;
    private const byte ObjectAnvil = 8;

    public async Task HandleObjectEventAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0 || packet.RemainingBytes < 6)
            return;

        var objectIndex = packet.ReadShort();
        var npcId = packet.ReadInt();
        var objectEvent = sessionManager.Maps?.GetObjectEvent(session.ZoneId, objectIndex);

        var success = false;
        if (objectEvent == null)
        {
            logger.LogDebug(
                "Object event {Index} not found in zone {Zone} for {Name}",
                objectIndex, session.ZoneId, session.Name);
        }
        else
        {
            var dx = session.X - objectEvent.PosX;
            var dz = session.Z - objectEvent.PosZ;
            if (dx * dx + dz * dz > MaxObjectRangeSq)
            {
                logger.LogDebug(
                    "Object event {Index} type {Type} out of range for {Name}: player=({PlayerX},{PlayerZ}) object=({ObjectX},{ObjectZ})",
                    objectIndex, objectEvent.Type, session.Name, session.X, session.Z, objectEvent.PosX, objectEvent.PosZ);
            }
            else
            {
                switch (objectEvent.Type)
                {
                    case ObjectBind:
                    case ObjectRemoveBind:
                        success = await HandleBindObjectEventAsync(session, objectEvent);
                        break;

                    case ObjectGate:
                    {
                        var gateNpc = sessionManager.Regions.GetNpc(npcId);
                        if (gateNpc != null && (byte)gateNpc.Nation == (byte)session.Nation)
                        {
                            gateNpc.GateOpen = !gateNpc.GateOpen;
                            await BroadcastGateFlagAsync(gateNpc);
                            success = true;
                        }
                        break;
                    }

                    case ObjectGateLever:
                    case ObjectFlagLever:
                    {
                        if (objectEvent.Belong != 0 && objectEvent.Belong != (int)session.Nation)
                            break;

                        var gateNpc = sessionManager.Regions.GetNpcByProtoId(session.ZoneId, objectEvent.ControlNpcId);
                        if (gateNpc != null)
                        {
                            gateNpc.GateOpen = !gateNpc.GateOpen;
                            await BroadcastGateFlagAsync(gateNpc);
                            success = true;
                        }
                        break;
                    }

                    case ObjectWarpGate:
                        success = await HandleWarpGateObjectEventAsync(session, objectEvent);
                        if (success)
                            return;
                        break;

                    case ObjectAnvil:
                        await SendAnvilRequestAsync(session, objectEvent.Index);
                        return;
                }
            }
        }

        if (success)
            return;

        await client.SendPacket(MiscPacketWriter.ObjectEventResult(
            objectEvent != null ? (byte)objectEvent.Type : (byte)0, ObjectEventFailed));
    }

    private async Task<bool> HandleBindObjectEventAsync(UserSession session, ObjectEvent objectEvent)
    {
        if (objectEvent.Belong != 0 && objectEvent.Belong != (int)session.Nation)
            return false;

        session.Quest.BindPoint = objectEvent.Index;
        logger.LogDebug("{Name} bind point changed to {BindPoint} in zone {Zone}", session.Name, objectEvent.Index, session.ZoneId);

        await session.Client.SendPacket(MiscPacketWriter.ObjectEventResult(
            (byte)objectEvent.Type, ObjectEventSucceeded));
        return true;
    }

    private async Task<bool> HandleWarpGateObjectEventAsync(UserSession session, ObjectEvent objectEvent)
    {
        if (objectEvent.Belong != 0 && objectEvent.Belong != (int)session.Nation)
            return false;

        if (session.ZoneId != (byte)session.Nation && session.ZoneId <= 2)
            return false;

        var warps = sessionManager.Maps?.GetWarpGateList(session.ZoneId, objectEvent);
        if (warps == null || warps.Count == 0)
            return false;

        var entries = warps
            .Select(warp => new WarpListEntry(
                warp.WarpId,
                warp.Name,
                warp.Announce,
                warp.Zone,
                0,
                (int)warp.Fee))
            .ToList();

        await worldMovementService.SendWarpListAsync(session, entries);
        return true;
    }

    private async Task BroadcastGateFlagAsync(NpcInstance npc)
    {
        var packet = NpcSpawnPacketWriter.GateFlag(
            NpcSpawnPacketWriter.InOutIn, npc.UniqueId, (short)npc.NpcId, npc.NpcType,
            npc.MaxHp, npc.Hp, npc.GateOpen);
        await sessionManager.Regions.BroadcastFromNpc(npc, packet);
    }

    private static async Task SendAnvilRequestAsync(UserSession session, int anvilId)
    {
        var result = ItemUpgradePacketWriter.AnvilOpen(anvilId);
        await session.Client.SendPacket(result);
    }
}
