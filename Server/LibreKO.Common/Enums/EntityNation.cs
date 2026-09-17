namespace LibreKO.Common.Enums;

public enum EntityNation : byte
{
    All = 0,
    Karus = 1,
    ElMorad = 2,
    None = 3,
}

public static class EntityNationRules
{
    public static bool IsOpenTo(byte ownerNation, AccountNation player) =>
        ownerNation is (byte)EntityNation.All or (byte)EntityNation.None
        || ownerNation == (byte)player;
}
