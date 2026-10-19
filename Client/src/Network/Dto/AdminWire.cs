namespace LibreKO.Network;

public enum AdminRequestSubOpcode : byte
{
    State = 1,
    Coins = 2,
    Stats = 3,
    GiveItem = 4,
    SetClass = 5,
    Zone = 6,
    ItemSearch = 7,
    CollectionRaces = 8,
    CollectionRaceStart = 9,
    CollectionRaceClose = 10,
    SetLevel = 11,
    SetSkill = 12,
    SetLook = 13,
    Find = 14,
    Go = 15,
    SpawnRow = 16,
    SpawnSet = 17,
    SpawnPersist = 18,
    Cash = 19,
    Commands = 20,
}

public enum AdminReplySubOpcode : byte
{
    State = 0x10,
    Result = 0x11,
    Grant = 0x12,
    GmFx = 0x13,
    CollectionRaces = 0x14,
    Find = 0x15,
    SpawnRow = 0x16,
    Commands = 0x17,
}

public enum AdminProgressMode : byte
{
    Keep = 0,
    Reset = 1,
}

public enum AdminFindKind : byte
{
    Npcs = 0,
    Monsters = 1,
    Players = 2,
}

public static class AdminWire
{
    public static (List<LibreKO.Domain.GmCommandInfo> Commands, bool Open) ReadCommands(Packet p)
    {
        var commands = new List<LibreKO.Domain.GmCommandInfo>();
        if (p.RemainingBytes < 3) return (commands, false);
        bool open = p.ReadByte() != 0;
        int count = p.ReadShort();
        for (int i = 0; i < count && p.RemainingBytes >= 6; i++)
            commands.Add(new LibreKO.Domain.GmCommandInfo(p.ReadString(), p.ReadString(), p.ReadString()));
        return (commands, open);
    }
}

public class AdminCollectionRace
{
    public int Id;
    public string Name = string.Empty;
    public byte ZoneId;
    public byte MinLevel;
    public byte MaxLevel;
    public int DurationMinutes;
    public bool AutoStart;
    public bool Active;
    public int RemainingSeconds;
    public int Completions;
    public int MaxWinners;
    public string Schedule = string.Empty;
    public string Objectives = string.Empty;
}

public class AdminFindHit
{
    public int Id;
    public int SpawnRow;
    public string Name = string.Empty;
    public int Level;
    public int Zone;
    public int X;
    public int Z;
    public bool Monster;
    public bool Bot;
}

public class AdminSpawnRow
{
    public bool CanPersist;
    public int Index;
    public int NpcId;
    public string Name = string.Empty;
    public int Zone;
    public bool Monster;
    public int X;
    public int Z;
    public float Y;
    public int Direction;
    public int Count;
    public int RespawnSeconds;
    public int SpawnRange;
    public int Alive;
}

public enum AdminPanelGrant : byte
{
    None = 0,
    GameMaster = 1,
    PublicDemo = 2,
}
