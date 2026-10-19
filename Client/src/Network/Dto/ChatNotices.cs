namespace LibreKO.Network;

public readonly record struct MonumentNotice(MonumentNoticeKind Kind, int Nation, string Name, bool Done, int Value)
{
    public static MonumentNotice? Read(Packet p)
    {
        if (p.RemainingBytes < 1) return null;
        var kind = (MonumentNoticeKind)p.ReadByte();
        switch (kind)
        {
            case MonumentNoticeKind.TopSecretCaptured or MonumentNoticeKind.MonumentCaptured or MonumentNoticeKind.SpyRescue:
            {
                if (p.RemainingBytes < 3) return null;
                int nation = p.ReadByte();
                string name = p.ReadString();
                if (name.Length == 0) return null;
                bool done = kind == MonumentNoticeKind.SpyRescue && p.RemainingBytes >= 1 && p.ReadByte() != 0;
                return new MonumentNotice(kind, nation, name, done, 0);
            }
            case MonumentNoticeKind.QuestFailed:
                return p.RemainingBytes >= sizeof(int) ? new MonumentNotice(kind, 0, "", false, p.ReadInt()) : null;
            case MonumentNoticeKind.TopSecretExpired:
                return new MonumentNotice(kind, 0, "", false, 0);
            case MonumentNoticeKind.Alert:
                return p.RemainingBytes >= 1 ? new MonumentNotice(kind, 0, "", false, p.ReadByte()) : null;
            default:
                return null;
        }
    }
}

public readonly record struct ChaosStoneNotice(string Breaker, int Nation)
{
    public static ChaosStoneNotice? Read(Packet p)
    {
        if (p.RemainingBytes < 2) return null;
        string breaker = p.ReadString();
        return p.RemainingBytes >= 1 ? new ChaosStoneNotice(breaker, p.ReadByte()) : null;
    }
}

public readonly record struct BoxItemNotice(string Name, int BoxItem, int Item)
{
    public static BoxItemNotice? Read(Packet p)
    {
        if (p.RemainingBytes < 1) return null;
        string name = p.ReadSByteString();
        if (p.RemainingBytes < 2 * sizeof(int)) return null;
        return new BoxItemNotice(name, p.ReadInt(), p.ReadInt());
    }
}

public enum MonumentNoticeKind : byte
{
    TopSecretCaptured = 1,
    QuestFailed = 2,
    TopSecretExpired = 3,
    MonumentCaptured = 4,
    SpyRescue = 5,
    Alert = 7,
}
