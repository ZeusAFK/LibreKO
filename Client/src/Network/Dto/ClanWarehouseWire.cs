namespace LibreKO.Network;

public enum ClanWarehouseResult : byte
{
    Failed = 0,
    Succeeded = 1,
    NotInClan = 2,
    NoRights = 3,
    NotStorable = 4,
}

public static class ClanWarehouseWire
{
    public const int NetSlots = 192;
}
