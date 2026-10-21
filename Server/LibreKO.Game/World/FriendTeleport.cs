using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class FriendTeleport
{
    public static bool ZoneAllows(byte casterZone, AccountNation nation, byte targetZone, bool reborn) =>
        !CharacterReconnectZoneRepair.IsEventZone(casterZone)
        && !CharacterReconnectZoneRepair.IsEventZone(targetZone)
        && !ZoneRules.Allows(targetZone, ZoneFlags.WarZone)
        && !(targetZone <= (byte)ZoneId.ElMoradCamp1 && targetZone != (byte)nation)
        && !(reborn && ZoneRules.BarsReborn(targetZone));
}
