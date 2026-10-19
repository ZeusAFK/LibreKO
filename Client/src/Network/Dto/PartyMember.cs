namespace LibreKO.Network;

public readonly struct PartyMember
{
    public readonly int CharId;
    public readonly byte SuccessCode;
    public readonly string Name;
    public readonly int MaxHp, Hp;
    public readonly int Level;
    public readonly int Class;
    public readonly int MaxMp, Mp;

    public PartyMember(int charId, byte successCode, string name,
        int maxHp, int hp, int level, int cls, int maxMp, int mp)
    {
        CharId = charId; SuccessCode = successCode; Name = name;
        MaxHp = maxHp; Hp = hp; Level = level; Class = cls; MaxMp = maxMp; Mp = mp;
    }

    public bool BecameLeader => SuccessCode == 100;
}

public readonly record struct PartyMemberBuffs(int CharId, IReadOnlyList<int> SkillIds)
{
    private const int HeaderBytes = sizeof(int) + sizeof(byte);

    public static PartyMemberBuffs? Read(Packet p)
    {
        if (p.RemainingBytes < HeaderBytes) return null;
        int charId = p.ReadInt();
        int count = p.ReadByte();
        if (p.RemainingBytes < count * sizeof(int)) return null;
        var skillIds = new int[count];
        for (int i = 0; i < count; i++) skillIds[i] = p.ReadInt();
        return new PartyMemberBuffs(charId, skillIds);
    }
}

public readonly record struct PartySignalProbe(uint Token)
{
    public static PartySignalProbe? Read(Packet p) =>
        p.RemainingBytes < sizeof(uint) ? null : new PartySignalProbe(p.ReadUInt());

    public Packet Echo()
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteByte((byte)PartySubOpcode.SignalProbe);
        p.WriteUInt(Token);
        return p;
    }
}

public readonly record struct PartySignalEntry(int CharId, int Ms);

public static class PartySignalTable
{
    private const int EntryBytes = sizeof(int) + sizeof(ushort);

    public static IReadOnlyList<PartySignalEntry>? Read(Packet p)
    {
        if (p.RemainingBytes < sizeof(byte)) return null;
        int count = p.ReadByte();
        if (p.RemainingBytes < count * EntryBytes) return null;
        var entries = new PartySignalEntry[count];
        for (int i = 0; i < count; i++) entries[i] = new PartySignalEntry(p.ReadInt(), p.ReadUShort());
        return entries;
    }
}

public readonly record struct PartyLevelProgress(int CharId, int Permille)
{
    private const int Bytes = sizeof(int) + sizeof(ushort);

    public static PartyLevelProgress? Read(Packet p) =>
        p.RemainingBytes < Bytes ? null : new PartyLevelProgress(p.ReadInt(), p.ReadUShort());
}
