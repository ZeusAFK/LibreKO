namespace LibreKO.Network;

public enum ClanBattleResult : byte
{
    Challenged = 1,
    Offline = 2,
    Dead = 3,
    OtherNation = 4,
    NotLeader = 5,
    Declined = 6,
    NotClanMember = 7,
    WrongZone = 8,
    AlreadyAtWar = 9,
    NotAtWar = 16,
}

public enum ClanBattleEnd : byte
{
    TheySurrendered = 1,
    Cleared = 2,
    YouSurrendered = 3,
}

public static class ClanBattleWire
{
    public readonly record struct ResultReply(byte Result, int ChallengerId, string Challenger);
    public readonly record struct EndReply(ClanBattleEnd Kind, string Clan);

    public static ResultReply? ReadResult(Packet p)
    {
        if (p.RemainingBytes < 1) return null;
        var result = (ClanBattleResult)p.ReadByte();
        if (result != ClanBattleResult.Challenged || p.RemainingBytes < 6) return new ResultReply((byte)result, 0, "");
        int challenger = p.ReadInt();
        p.ReadUShort();
        return new ResultReply((byte)result, challenger, p.RemainingBytes >= 2 ? p.ReadString() : "");
    }

    public static EndReply? ReadEnd(Packet p)
    {
        if (p.RemainingBytes < 1) return null;
        var kind = (ClanBattleEnd)p.ReadByte();
        if (kind == ClanBattleEnd.Cleared || p.RemainingBytes < 6) return new EndReply(kind, "");
        p.ReadShort();
        p.ReadUShort();
        return new EndReply(kind, p.ReadString());
    }
}
