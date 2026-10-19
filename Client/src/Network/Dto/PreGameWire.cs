namespace LibreKO.Network;

public enum AllCharacterInfoOpcode : byte
{
    CharacterList = 1,
    NameChange = 2,
    ArrangeOpen = 3,
    ArrangeReceive = 4,
}

public enum GameServerState : byte
{
    Open = 0,
    PremiumOnly = 1,
    SecurityCheckFailed = 2,
    ClientOutOfDate = 3,
}

public enum GameStartSubOpcode : byte
{
    CharacterPicked = 1,
    Ready = 2,
}

public enum SelectCharacterResult : byte
{
    Failed = 0,
    Success = 1,
}

public enum SelectingCharacterNameChangeResult : byte
{
    Failed = 0,
    Success = 1,
    InvalidCharacters = 5,
}

public enum CreateCharacterResult : byte
{
    Success = 0,
    SlotFull = 1,
    NationRaceMismatch = 2,
    NameAlreadyExists = 3,
    ServerError = 4,
    InvalidName = 5,
    NameMissing = 6,
    InvalidRace = 7,
    UnsupportedRace = 8,
    InvalidClass = 9,
    PointsRemaining = 10,
    InvalidStats = 11,
    InvalidCharacters = 12,
}

public static class MyInfoWire
{
    public const int SlotBytes = 19;

    private const int TailBeforeRebirth = 4 + 4 + 6 + 2;

    public static void ReadTail(Packet p, out int manner, out int level, out int str, out int sta, out int dex, out int intel, out int mag,
            out long sealedExp)
    {
        manner = level = str = sta = dex = intel = mag = 0;
        sealedExp = 0;
        int reserved = InventoryConstants.MyInfoReservedItemRecords * SlotBytes;
        if (p.RemainingBytes < reserved + TailBeforeRebirth + 6) return;
        for (int i = 0; i < reserved; i++) p.ReadByte();
        p.ReadByte();
        if (p.ReadByte() != 0) return;
        p.ReadByte(); p.ReadByte();
        manner = p.ReadInt();
        for (int i = 0; i < 6; i++) p.ReadByte();
        p.ReadShort();
        level = p.ReadByte();
        str = p.ReadByte(); sta = p.ReadByte(); dex = p.ReadByte(); intel = p.ReadByte(); mag = p.ReadByte();
        if (p.RemainingBytes >= sizeof(long)) sealedExp = p.ReadLong();
    }
}
