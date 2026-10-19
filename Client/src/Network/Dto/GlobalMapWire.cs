namespace LibreKO.Network;

public enum GlobalMapSubOpcode : byte
{
    List = 1,
}

public struct GlobalMapZone
{
    public int ZoneId;
    public string Name;
    public int Population;
    public byte OwnerNation;
}
