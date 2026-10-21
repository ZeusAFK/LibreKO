namespace LibreKO.Network;

public enum DrakiEnterResult : byte
{
    LevelTooLow = 0,
    WrongZone = 1,
    AlreadyInInstance = 2,
    ShowBoard = 3,
    FloorNotReached = 4,
    NoLimit = 5,
    RoomCreationFailed = 6,
    InvalidItem = 7,
    Dead = 9,
    ServerIndexMismatch = 10,
    PacketIndexMismatch = 11,
    NoUserInfo = 12,
    CannotEnterNow = 13,
    SilentRefusal = 14,
    LadderPoints = 15,
    TowerFull = 100,
}

public readonly record struct DrakiRankRow(int Rank, string Name, int FinishSeconds, int Stage);

public sealed record DrakiRankList(IReadOnlyList<DrakiRankRow> Top, DrakiRankRow Mine, int MaxStage, int EntrancesLeft);

public readonly record struct DrakiStageEnd(int Stage, int SubStage, int ElapsedSeconds, bool GaveUp);

public static class DrakiWire
{
    public const int TopRows = 5;

    private const int StageEndHeaderBytes = 2;
    private const int StageEndBytes = StageEndHeaderBytes + 2 + 2 + 4 + 1;

    public static DrakiRankList ReadRankList(Packet p)
    {
        var top = new List<DrakiRankRow>(TopRows);
        for (int i = 0; i < TopRows; i++)
            top.Add(ReadRow(p));
        var mine = ReadRow(p);
        int maxStage = p.RemainingBytes >= 4 ? (ushort)p.ReadUInt() : 0;
        int entrancesLeft = p.RemainingBytes >= 4 ? (int)p.ReadUInt() : 0;
        return new DrakiRankList(top, mine, maxStage, entrancesLeft);
    }

    public static DrakiStageEnd? ReadStageEnd(Packet p)
    {
        if (p.RemainingBytes < StageEndBytes) return null;
        p.Skip(StageEndHeaderBytes);
        int stage = p.ReadUShort();
        int subStage = p.ReadUShort();
        int elapsed = (int)p.ReadUInt();
        bool gaveUp = p.ReadByte() != 0;
        return new DrakiStageEnd(stage, subStage, elapsed, gaveUp);
    }

    public static Packet Enter(int certificateItem, int chapter)
    {
        var p = new Packet(GameOpcodes.GS_EVENT);
        p.WriteByte((byte)TempleSubOpcode.DrakiEnter);
        p.WriteUInt((uint)certificateItem);
        p.WriteByte((byte)chapter);
        return p;
    }

    private static DrakiRankRow ReadRow(Packet p)
    {
        if (p.RemainingBytes < 2) return default;
        int rank = p.ReadByte();
        string name = p.ReadSByteString();
        int finish = p.RemainingBytes >= 4 ? (int)p.ReadUInt() : 0;
        int stage = p.RemainingBytes >= 4 ? (ushort)p.ReadUInt() : 0;
        return new DrakiRankRow(rank, name, finish, stage);
    }
}
