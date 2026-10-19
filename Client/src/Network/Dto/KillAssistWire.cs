namespace LibreKO.Network;

public enum KillAssistSubOpcode : byte
{
    Kill = 1,
    Assist = 2,
}

public enum KillStreakKind : byte
{
    OwnStreak = 1,
    OtherStreak = 2,
    Announcement = 3,
}

public readonly record struct KillStreakReply(KillStreakKind Kind, int CharacterId, string Name, byte Nation, int Count);

public readonly record struct AssistReply(int CharacterId, int Count, byte Grade);

public static class KillAssistReplies
{
    public static KillStreakReply? ReadKill(Packet p)
    {
        if (p.RemainingBytes < 1 + 4 + 2) return null;
        var kind = (KillStreakKind)p.ReadByte();
        int id = p.ReadInt();
        string name = p.ReadString();
        if (p.RemainingBytes < 1 + 2) return null;
        byte nation = p.ReadByte();
        return new KillStreakReply(kind, id, name, nation, p.ReadUShort());
    }

    public static AssistReply? ReadAssist(Packet p)
    {
        if (p.RemainingBytes < 4 + 2 + 1) return null;
        return new AssistReply(p.ReadInt(), p.ReadUShort(), p.ReadByte());
    }
}
