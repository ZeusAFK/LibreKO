namespace LibreKO.Network;

public enum DropBand : byte
{
    Common = 1,
    Uncommon = 2,
    Rare = 3,
    VeryRare = 4,
}

public readonly record struct DropLine(int ItemId, DropBand Band);

public sealed record DropListReply(int TargetId, bool Shown, int CoinMin, int CoinMax, int Experience,
    IReadOnlyList<DropLine> Items, int Total)
{
    private const byte DropListShown = 1;
    private const int HeaderBytes = 4 + 1;
    private const int SummaryBytes = 4 + 4 + 4 + 1;
    private const int LineBytes = 4 + 1;
    private const int TotalBytes = 2;

    public static DropListReply? Read(Packet p)
    {
        if (p.RemainingBytes < HeaderBytes) return null;
        int target = p.ReadInt();
        if (p.ReadByte() != DropListShown || p.RemainingBytes < SummaryBytes)
            return new DropListReply(target, false, 0, 0, 0, System.Array.Empty<DropLine>(), 0);
        int coinMin = p.ReadInt();
        int coinMax = p.ReadInt();
        int exp = p.ReadInt();
        int count = p.ReadByte();
        var items = new List<DropLine>(count);
        for (int i = 0; i < count && p.RemainingBytes >= LineBytes; i++)
            items.Add(new DropLine(p.ReadInt(), (DropBand)p.ReadByte()));
        int total = p.RemainingBytes >= TotalBytes ? p.ReadUShort() : items.Count;
        return new DropListReply(target, true, coinMin, coinMax, exp, items, Math.Max(total, items.Count));
    }
}
