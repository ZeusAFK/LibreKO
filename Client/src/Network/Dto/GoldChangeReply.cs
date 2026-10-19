namespace LibreKO.Network;

public readonly record struct GoldChangeReply(GoldChangeKind Sub, int Amount, int Total, int Times = 0, int UnitId = -1)
{
    private const int JackpotTailBytes = 6;

    public bool ChangesBalance => Sub != GoldChangeKind.Multiplied;

    public static GoldChangeReply? Read(Packet p)
    {
        if (p.RemainingBytes < 9) return null;
        var sub = (GoldChangeKind)p.ReadByte();
        int amount = p.ReadInt();
        int total = p.ReadInt();
        if (sub != GoldChangeKind.Multiplied) return new GoldChangeReply(sub, amount, total);
        if (p.RemainingBytes < JackpotTailBytes) return null;
        int times = p.ReadUShort();
        return new GoldChangeReply(sub, amount, total, times, p.ReadInt());
    }
}
