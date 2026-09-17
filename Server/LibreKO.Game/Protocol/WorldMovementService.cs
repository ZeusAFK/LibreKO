using System.Linq;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public readonly record struct WarpListEntry(
    short WarpId,
    string Name,
    string Announce,
    short ZoneId,
    short MaxUsers,
    int Fee);

public interface IWorldMovementService
{
    Task HandleMoveAsync(IClient client, Packet packet);
    Task HandleRotateAsync(IClient client, Packet packet);
    Task HandleStateChangeAsync(IClient client, Packet packet);
    Task HandleHomeAsync(IClient client);
    Task WarpAsync(UserSession session, ushort posX, ushort posZ);
    Task HandleRecvWarpAsync(IClient client, Packet packet);
    Task HandleWarpListAsync(IClient client, Packet packet);
    Task HandleZoneChangeAsync(IClient client, Packet packet);
    Task HandleStealthAsync(IClient client, Packet packet);
    Task HandleSpeedHackCheckAsync(IClient client, Packet packet);
    Task SendWarpListAsync(UserSession session, IReadOnlyCollection<WarpListEntry> warps);
}

public class WorldMovementService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IZoneTransitionService zoneTransitionService,
    IUserNotificationService userNotificationService,
    ICombatNotificationService combatNotificationService,
    ICombatLifecycleService combatLifecycleService,
    IWorldVisibilityService worldVisibilityService,
    IMiningPacketCoordinator miningPacketCoordinator,
    IStealthService stealthService,
    ILogger<WorldMovementService> logger) : IWorldMovementService
{
    private const byte MoveEchoFinish = 0;
    private const byte MoveEchoStart = 1;
    private const byte MoveEchoMove = 3;

    private const float MoveSpeedScale = 100f;
    private const float PositionScale = 10f;

    private const short DamageZoneHp = 10;

    public async Task HandleMoveAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.IsWarping || session.Hp <= 0 || packet.RemainingBytes < 9)
            return;

        var willX = packet.ReadUShort();
        var willZ = packet.ReadUShort();
        var willY = packet.ReadUShort();
        var speed = packet.ReadShort();
        var echo = packet.ReadByte();

        ushort curX = willX, curZ = willZ, curY = willY;
        if (packet.RemainingBytes >= 6)
        {
            curX = packet.ReadUShort();
            curZ = packet.ReadUShort();
            curY = packet.ReadUShort();
        }

        if (echo is not MoveEchoFinish and not MoveEchoStart and not MoveEchoMove)
            return;

        if (speed != 0 && echo != MoveEchoFinish)
            (willX, willZ) = LeadDestination(willX, willZ, curX, curZ, speed);

        if (speed is > 90 or < -90)
        {
            logger.LogWarning("Speed hack detected for {Name}: speed={Speed}", session.Name, speed);
            return;
        }

        var newX = willX / 10.0f;
        var newZ = willZ / 10.0f;
        if (sessionManager.Maps != null && !sessionManager.Maps.IsValidPosition(session.ZoneId, newX, newZ))
            return;

        if (willX != session.MoveOldWillX || willZ != session.MoveOldWillZ)
            await stealthService.RevealAsync(session, InvisibilityType.DispelOnMove);

        session.X = newX;
        session.Y = willY / 10.0f;
        session.Z = newZ;
        session.MoveOldEcho = echo;
        session.MoveOldSpeed = speed;
        session.MoveOldWillX = willX;
        session.MoveOldWillY = willY;
        session.MoveOldWillZ = willZ;

        await zoneTransitionService.RefreshArenaAsync(session);

        if (session.IsGathering)
            await miningPacketCoordinator.StopGatheringAsync(session);

        var oldRegionX = session.RegionX;
        var oldRegionZ = session.RegionZ;
        var regionChanged = sessionManager.Regions.UpdateRegion(session);

        // Server-side position/anti-cheat state is already updated above. Defer the
        // GS_MOVE broadcast: mark the player dirty and let MovementBroadcastService fan
        // out the latest position at a fixed cadence, instead of one broadcast per
        // received move packet (the dominant send-volume cost at scale). The corrected
        // will-position/speed/echo are in MoveOld* for the broadcaster to rebuild from.
        session.MovePending = true;

        if (regionChanged)
        {
            await worldVisibilityService.BroadcastRegionTransitionAsync(session, oldRegionX, oldRegionZ);
            await worldVisibilityService.SendRegionUserListAsync(session);
            await worldVisibilityService.SendNpcRegionListAsync(session);
        }

        if (sessionManager.Maps == null)
            return;

        var gameEvent = sessionManager.Maps.CheckEvent(session.ZoneId, newX, newZ);
        if (gameEvent == null)
            return;

        switch (gameEvent.Type)
        {
            case 1:
                await zoneTransitionService.ChangeZoneAsync(session, (byte)gameEvent.Exec1, gameEvent.Exec2, gameEvent.Exec3);
                break;

            case 3:
                session.Hp -= (short)Math.Min(DamageZoneHp, session.Hp);
                await combatNotificationService.SendHpChangeAsync(session);
                if (session.Hp <= 0)
                    await combatLifecycleService.HandlePlayerDeathAsync(session, killer: null);
                break;
        }
    }

    private static (ushort X, ushort Z) LeadDestination(
        ushort willX, ushort willZ, ushort curX, ushort curZ, short speed)
    {
        var stepX = (willX - curX) / PositionScale;
        var stepZ = (willZ - curZ) / PositionScale;
        var length = MathF.Sqrt(stepX * stepX + stepZ * stepZ);
        if (length <= 0f)
            return (willX, willZ);

        var lead = speed / MoveSpeedScale;
        var leadX = willX + stepX / length * lead * PositionScale;
        var leadZ = willZ + stepZ / length * lead * PositionScale;

        return ((ushort)Math.Clamp(leadX, 0f, ushort.MaxValue),
                (ushort)Math.Clamp(leadZ, 0f, ushort.MaxValue));
    }

    public async Task HandleRotateAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 2)
            return;

        session.Direction = packet.ReadShort();

        var result = MovementPacketWriter.Rotate(session.CharacterId, session.Direction);
        await sessionManager.Regions.SendToRegion(session, result);
    }

    public async Task HandleStateChangeAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 2)
            return;

        var type = packet.ReadByte();
        var value = packet.RemainingBytes >= 4 ? packet.ReadInt() : packet.ReadByte();

        if (type == (byte)StateChangeType.Stealth)
            return;

        if (type == (byte)StateChangeType.Pose)
            session.IsSitting = value == (byte)UserPoseState.Sitting;

        if (type == (byte)StateChangeType.CombatStance)
            session.InCombatStance = value != (byte)CombatStanceState.Relaxed;

        var result = MovementPacketWriter.StateChange(session.CharacterId, type, value);
        await sessionManager.Regions.SendToRegion(session, result, excludeSender: false);
    }

    public async Task HandleHomeAsync(IClient client)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0 || session.Hp < session.MaxHp / 2)
            return;

        var startPos = gameDataService.GetStartPosition(session.ZoneId);
        if (startPos == null)
            return;

        var (x, z) = startPos.RandomSpawn(session.Nation);

        await WarpAsync(session, (ushort)(x * 10), (ushort)(z * 10));
    }

    public async Task WarpAsync(UserSession session, ushort posX, ushort posZ)
    {
        var realX = posX / 10.0f;
        var realZ = posZ / 10.0f;

        await session.Client.SendPacket(MovementPacketWriter.Warp(posX, posZ));

        await worldVisibilityService.BroadcastUserInOutAsync(session, InOutType.Out);
        sessionManager.Regions.DropAggroOn(session.CharacterId);

        session.X = realX;
        session.Y = ResolveTargetHeight(session.ZoneId, realX, realZ);
        session.Z = realZ;
        session.SpeedLastX = 0f;
        session.SpeedLastZ = 0f;
        sessionManager.Regions.UpdateRegion(session);
        await zoneTransitionService.RefreshArenaAsync(session);

        await worldVisibilityService.BroadcastUserInOutAsync(session, InOutType.Warp);
        await worldVisibilityService.SendNpcRegionListAsync(session);
        await worldVisibilityService.SendRegionUserListAsync(session);
    }

    public async Task HandleRecvWarpAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 4)
            return;

        await WarpAsync(session, packet.ReadUShort(), packet.ReadUShort());
    }

    private const byte CommandCaptainFame = 100;

    public async Task HandleSpeedHackCheckAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null) return;
        if (session.IsGM) return;

        var baseClass = ClassIdHelper.GetSubtype(session.Class);
        bool isRogue = baseClass is 2 or 7 or 8;
        bool isCaptain = session.Fame == CommandCaptainFame;
        float maxSpeed = (isRogue || isCaptain ? 90f : 67f) + 17f;

        var lastX = session.SpeedLastX;
        var lastZ = session.SpeedLastZ;

        if (lastX == 0f && lastZ == 0f)
        {
            session.SpeedLastX = session.X;
            session.SpeedLastZ = session.Z;
            return;
        }

        var dx = session.X - lastX;
        var dz = session.Z - lastZ;
        var range = (dx * dx + dz * dz) / 100f;

        if (range >= maxSpeed)
        {
            logger.LogWarning("Speed hack from {Name}: range={Range:F1} > limit={Limit:F1}, warping back to ({X:F0},{Z:F0})",
                session.Name, range, maxSpeed, lastX, lastZ);
            await WarpAsync(session, (ushort)((ushort)lastX * 10), (ushort)((ushort)lastZ * 10));
        }
        else
        {
            session.SpeedLastX = session.X;
            session.SpeedLastZ = session.Z;
        }
    }

    public async Task HandleWarpListAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 2)
            return;

        var sourceId = packet.ReadShort();

        if (packet.RemainingBytes < 2)
        {
            var npcWarpEntries = GetNpcWarpListEntries(session, sourceId);
            if (npcWarpEntries.Count == 0)
                return;

            await SendWarpListAsync(session, npcWarpEntries);
            return;
        }

        var warpId = packet.ReadShort();

        var mapWarp = sessionManager.Maps?.GetWarp(session.ZoneId, warpId);
        if (mapWarp != null)
        {
            await HandleMapWarpSelectionAsync(session, mapWarp);
            return;
        }

        var npcWarps = GetNpcWarps(session, sourceId);
        var selectedWarp = npcWarps.FirstOrDefault(warp => warp.WarpId == warpId);
        if (selectedWarp == null)
            return;

        if (session.Money < (int)selectedWarp.Fee)
        {
            var fail = WarpListPacketWriter.Result(WarpListPacketWriter.ResultNotQualified);
            await client.SendPacket(fail);
            return;
        }

        if (selectedWarp.Fee > 0)
        {
            session.Money -= (int)selectedWarp.Fee;
            await userNotificationService.SendGoldLossAsync(session, (int)selectedWarp.Fee);
        }

        if (selectedWarp.Zone == session.ZoneId)
        {
            logger.LogDebug("Same-zone warp {Name} via '{WarpName}': X={X} Z={Z}",
                session.Name, selectedWarp.Name, selectedWarp.X, selectedWarp.Z);
            await SendSameZoneWarpSuccessAsync(session);
            await WarpAsync(session, (ushort)(selectedWarp.X * 10), (ushort)(selectedWarp.Z * 10));
            return;
        }

        var destinationZoneId = ResolveWarpDestinationZone(session.ZoneId, selectedWarp.Zone);
        if (destinationZoneId == session.ZoneId)
        {
            await SendSameZoneWarpSuccessAsync(session);
            await WarpAsync(session, (ushort)(selectedWarp.X * 10), (ushort)(selectedWarp.Z * 10));
            return;
        }

        var (npcWarpX, npcWarpZ) = ResolveWarpArrival(session.ZoneId, destinationZoneId, selectedWarp);
        logger.LogDebug("Warp {Name} to zone {Zone} via warp '{WarpName}': X={X} Z={Z}",
            session.Name, destinationZoneId, selectedWarp.Name, npcWarpX, npcWarpZ);
        await zoneTransitionService.ChangeZoneAsync(session, (byte)destinationZoneId, npcWarpX, npcWarpZ);
    }

    public async Task HandleZoneChangeAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var opcode = packet.ReadByte();
        if (opcode == 1)
        {
            await worldVisibilityService.SendNearbyUsersToClientAsync(session);

            await client.SendPacket(ZoneChangePacketWriter.Ready());
            return;
        }

        if (opcode == 2)
        {
            session.IsWarping = false;
            await worldVisibilityService.BroadcastUserInOutAsync(session, InOutType.Warp);
        }
    }

    public async Task HandleStealthAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        if (packet.ReadByte() != 0)
            return;

        await stealthService.RevealAsync(session, InvisibilityType.None);
    }

    public async Task SendWarpListAsync(UserSession session, IReadOnlyCollection<WarpListEntry> warps)
    {
        var entries = warps
            .Select(warp => new WarpListPacketWriter.Entry(
                warp.WarpId, warp.Name, warp.Announce, warp.ZoneId, warp.MaxUsers,
                (uint)Math.Max(0, warp.Fee)))
            .ToList();

        await session.Client.SendPacket(WarpListPacketWriter.Menu(entries));
    }

    private IReadOnlyList<WarpInfo> GetNpcWarps(UserSession session, short npcId)
    {
        var npc = gameDataService.GetNpc(npcId, isMonster: false);
        if (npc == null || !npc.IsNpc)
            return [];

        var npcInstance = sessionManager.Regions.GetNpc(session.Quest.EventNpcUniqueId);
        if (npcInstance == null || npcInstance.NpcId != npcId || !IsInNpcRange(session, npcInstance))
            return [];

        return sessionManager.Maps?.GetWarpList(session.ZoneId, npc.Group) ?? [];
    }

    private List<WarpListEntry> GetNpcWarpListEntries(UserSession session, short npcId)
    {
        return [.. GetNpcWarps(session, npcId)
            .Select(warp => new WarpListEntry(
                warp.WarpId,
                warp.Name,
                string.Empty,
                warp.Zone,
                0,
                (int)warp.Fee))];
    }

    private async Task HandleMapWarpSelectionAsync(UserSession session, WarpInfo selectedWarp)
    {
        if ((selectedWarp.Nation != (short)EntityNation.All && selectedWarp.Nation != (short)session.Nation)
            || session.Money < selectedWarp.Fee)
            return;

        if (selectedWarp.Fee > 0)
        {
            session.Money -= (int)selectedWarp.Fee;
            await userNotificationService.SendGoldLossAsync(session, (int)selectedWarp.Fee);
        }

        var destinationZoneId = ResolveWarpDestinationZone(session.ZoneId, selectedWarp.Zone);
        var (targetX, targetZ) = ResolveWarpArrival(session.ZoneId, destinationZoneId, selectedWarp);

        if (destinationZoneId == session.ZoneId)
        {
            await SendSameZoneWarpSuccessAsync(session);
            await WarpAsync(session, (ushort)(targetX * 10), (ushort)(targetZ * 10));
            return;
        }

        await zoneTransitionService.ChangeZoneAsync(session, (byte)destinationZoneId, targetX, targetZ);
    }

    private static (float X, float Z) ApplyWarpRadius(float x, float z, float radius)
    {
        if (radius <= 0)
            return (x, z);

        var offsetX = Random.Shared.NextSingle() * radius * 2;
        if (offsetX < radius)
            offsetX = -offsetX;

        var offsetZ = Random.Shared.NextSingle() * radius * 2;
        if (offsetZ < radius)
            offsetZ = -offsetZ;

        return (x + offsetX, z + offsetZ);
    }

    private static async Task SendSameZoneWarpSuccessAsync(UserSession session)
    {
        var result = WarpListPacketWriter.Arrived();
        await session.Client.SendPacket(result);
    }

    private static bool IsInNpcRange(UserSession session, NpcInstance npc)
    {
        var dx = session.X - npc.X;
        var dz = session.Z - npc.Z;
        return dx * dx + dz * dz <= GameConstants.MaxNpcInteractionRangeSq;
    }

    private short ResolveWarpDestinationZone(short currentZoneId, short targetZoneId)
    {
        if (targetZoneId == currentZoneId || !SharesMapFile(currentZoneId, targetZoneId))
            return targetZoneId;

        if (!gameDataService.ZoneInfoTable.TryGetValue(currentZoneId, out var currentZone)
            || !gameDataService.ZoneInfoTable.TryGetValue(targetZoneId, out var targetZone))
            return targetZoneId;

        if (!string.Equals(
                NormalizeMapFamily(currentZone.MapName),
                NormalizeMapFamily(targetZone.MapName),
                StringComparison.OrdinalIgnoreCase))
            return targetZoneId;

        return currentZoneId;
    }

    private bool SharesMapFile(short zoneId, short otherZoneId)
    {
        if (zoneId == otherZoneId)
            return true;

        if (!gameDataService.ZoneInfoTable.TryGetValue(zoneId, out var zone)
            || !gameDataService.ZoneInfoTable.TryGetValue(otherZoneId, out var otherZone))
            return false;

        var smdName = zone.SmdName?.Trim();
        return !string.IsNullOrEmpty(smdName)
            && string.Equals(smdName, otherZone.SmdName?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private (float X, float Z) ResolveWarpArrival(short fromZoneId, short destinationZoneId, WarpInfo warp)
    {
        if (!SharesMapFile(fromZoneId, destinationZoneId)
            && SharesMapFile(destinationZoneId, BattleZoneManager.ZONE_MORADON))
            return (0f, 0f);

        return warp.X == 0f && warp.Z == 0f
            ? (0f, 0f)
            : ApplyWarpRadius(warp.X, warp.Z, warp.Radius);
    }

    private float ResolveTargetHeight(short zoneId, float x, float z)
    {
        var height = sessionManager.Maps?.GetHeight(zoneId, x, z) ?? 0f;
        if (height == float.MinValue || height < 0f)
            return 0f;
        return height;
    }

    private static string NormalizeMapFamily(string mapName)
    {
        var normalized = (mapName ?? string.Empty).Trim();
        foreach (var suffix in SharedMapVariantSuffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return normalized[..^suffix.Length].TrimEnd();
        }

        return normalized;
    }

    private static readonly string[] SharedMapVariantSuffixes =
    [
        " VIII",
        " VII",
        " III",
        " II",
        " IV",
        " VI",
        " IX",
        " V",
        " X",
        " I"
    ];
}
