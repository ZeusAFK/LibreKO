using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public interface IZoneTransitionService
{
    Task ChangeZoneAsync(UserSession session, byte newZone, float x, float z);
    Task SendZoneAbilityAsync(UserSession session);
    Task RefreshArenaAsync(UserSession session);
}

public class ZoneTransitionService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    TimeWeatherBroadcastService timeWeather,
    ILogger<ZoneTransitionService> logger) : IZoneTransitionService
{
    private const byte ZoneAbilityUpdate = 1;
    private const byte DefaultTariff = 10;

    private const float MoradonTownX = 816f;
    private const float MoradonTownZ = 532f;

    public async Task ChangeZoneAsync(UserSession session, byte newZone, float x, float z)
    {
        if (session.IsWarping)
            return;

        session.IsWarping = true;

        if (x == 0f && z == 0f)
            (x, z) = ResolveZeroCoords(newZone, session.Nation);

        var outPacket = VisibilityPacketWriter.UserOut(session.CharacterId);
        await sessionManager.Regions.SendToRegion(session, outPacket);

        sessionManager.Regions.RemoveFromRegion(session);
        sessionManager.Regions.DropAggroOn(session.CharacterId);

        session.ZoneId = newZone;
        session.X = x;
        session.Z = z;
        session.Y = ResolveTargetHeight(newZone, x, z);
        session.SpeedLastX = 0f;
        session.SpeedLastZ = 0f;
        session.Quest.BindPoint = -1;

        logger.LogDebug("ZoneChange for {Name}: zone={Zone} X={X} Z={Z} Y={Y} PosX={PosX} PosZ={PosZ} PosY={PosY}",
            session.Name, newZone, x, z, session.Y, session.GetPosX, session.GetPosZ, session.GetPosY);

        sessionManager.Regions.AddToRegion(session);

        await session.Client.SendPacket(ZoneChangePacketWriter.Teleport(
            (short)newZone,
            (ushort)session.GetPosX, (ushort)session.GetPosZ, (ushort)session.GetPosY,
            (byte)session.Nation));

        await SendZoneAbilityAsync(session);
        await session.Client.SendPacket(timeWeather.BuildWeatherPacketFor(session.ZoneId));
    }

    public Task RefreshArenaAsync(UserSession session)
        => ArenaZones.GetArenaId(session.ZoneId, session.X, session.Z) == session.ArenaId
            ? Task.CompletedTask
            : SendZoneAbilityAsync(session);

    public async Task SendZoneAbilityAsync(UserSession session)
    {
        session.ArenaId = ArenaZones.GetArenaId(session.ZoneId, session.X, session.Z);
        var rule = ZoneRules.For(session.ZoneId);
        var zoneType = GetZoneAbilityType(session.ZoneId, session.ArenaId);
        var kingData = gameDataService.KingSystemTable.TryGetValue((byte)session.Nation, out var data) ? data : null;
        var tariff = (ushort)(kingData?.TerritoryTariff ?? DefaultTariff);

        await session.Client.SendPacket(ZoneChangePacketWriter.ZoneAbility(
            ZoneAbilityUpdate,
            rule.Flags.HasFlag(ZoneFlags.TradeOtherNation),
            (byte)zoneType,
            rule.Flags.HasFlag(ZoneFlags.TalkOtherNation),
            tariff));
    }

    private float ResolveTargetHeight(short zoneId, float x, float z)
    {
        var height = sessionManager.Maps?.GetHeight(zoneId, x, z) ?? 0f;
        return height == float.MinValue ? 0f : height;
    }

    private (float X, float Z) ResolveZeroCoords(byte zoneId, AccountNation nation)
    {
        var startPosition = gameDataService.GetStartPosition(zoneId);
        if (startPosition != null)
        {
            if (startPosition.BaseX(nation) != 0 || startPosition.BaseZ(nation) != 0)
            {
                var (spawnX, spawnZ) = startPosition.RandomSpawn(nation);
                logger.LogDebug("ResolveZeroCoords: zone {Zone} → start_position ({X}, {Z})",
                    zoneId, spawnX, spawnZ);
                return (spawnX, spawnZ);
            }
        }

        if (gameDataService.ZoneInfoTable.TryGetValue(zoneId, out var zone)
            && (zone.InitX != 0 || zone.InitZ != 0))
        {
            return (zone.InitX, zone.InitZ);
        }

        logger.LogWarning("ResolveZeroCoords: no coords for zone {Zone} — using Moradon fallback", zoneId);
        return (MoradonTownX, MoradonTownZ);
    }

    public static ZoneAbilityType GetZoneAbilityType(byte zoneId, byte arenaId)
        => arenaId != ArenaZones.NoArena
            ? ZoneAbilityType.FreeForAll
            : ZoneRules.For(zoneId).Ability;
}
