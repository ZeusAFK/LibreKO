namespace LibreKO.Network;

public struct FriendEntry
{
    public string Name;
    public int CharId;
    public byte Status;

    public byte Level;
    public short Class;
    public byte Nation;
    public byte ZoneId;

    public readonly bool IsOnline => Status != 0 && CharId >= 0;
    public readonly bool InParty => Status == 3;
}

public readonly record struct FriendDetails(string Name, byte Level, short Class, byte Nation, byte ZoneId);
