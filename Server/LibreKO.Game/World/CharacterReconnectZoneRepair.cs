using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class CharacterReconnectZoneRepair
{
    public const short SafeReconnectZoneId = (short)ZoneId.Moradon;
    private const short UnsupportedTowerWarZoneId = 18;
    private const float SafeReconnectX = 818f;
    private const float SafeReconnectZ = 628f;

    public static bool TryRepair(Character character, AccountNation nation, IGameDataService gameData)
    {
        if (!RequiresRepair(character.MapId, gameData))
            return false;

        var (zoneId, posX, posZ, posY) = ResolveSafeSpawn(nation, gameData);
        character.MapId = (byte)zoneId;
        character.X = posX / 10f;
        character.Z = posZ / 10f;
        character.Y = posY / 10f;
        return true;
    }

    private static bool RequiresRepair(short zoneId, IGameDataService gameData) =>
        zoneId == UnsupportedTowerWarZoneId
        || (gameData.ZoneInfoTable is { Count: > 0 } zones && !zones.ContainsKey(zoneId));

    public static (short ZoneId, short PosX, short PosZ, short PosY) ResolveSafeSpawn(
        AccountNation nation,
        IGameDataService gameData)
    {
        _ = nation;
        _ = gameData;

        // Moradon's default start point is the central plaza, inside opposite-nation guard aggro range
        // with the current NPC spawn set, so reconnects land at a fixed inn-side point instead.
        return (SafeReconnectZoneId, (short)(SafeReconnectX * 10), (short)(SafeReconnectZ * 10), 0);
    }
}
