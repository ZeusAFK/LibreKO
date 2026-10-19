namespace LibreKO.Network;

public readonly record struct UserVoteAsk(string Target, byte Reason, string Text);

public readonly record struct UserVoteOutcome(bool Warned, short Warnings, short Limit, bool Counted);

public static class UserVoteReply
{
    private const short Done = 1;

    public static (short Result, UserVoteAsk? Ask) ReadStart(Packet p)
    {
        if (p.RemainingBytes < 2) return (0, null);
        short result = p.ReadShort();
        if (result != Done || p.RemainingBytes < 3) return (result, null);
        string target = p.ReadString();
        byte reason = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
        string text = reason == Domain.UserVote.ReasonOther && p.RemainingBytes >= 2 ? p.ReadString() : "";
        return (result, new UserVoteAsk(target, reason, text));
    }

    public static UserVoteOutcome ReadResult(Packet p)
    {
        if (p.RemainingBytes < 3) return new UserVoteOutcome(false, 0, 0, false);
        short result = p.ReadShort();
        var kind = (UserVoteResultKind)p.ReadByte();
        if (result != Done) return new UserVoteOutcome(false, 0, 0, false);
        if (kind == UserVoteResultKind.WarningCount && p.RemainingBytes >= 4) return new UserVoteOutcome(true, p.ReadShort(), p.ReadShort(), true);
        return new UserVoteOutcome(kind == UserVoteResultKind.WarningGiven, 0, 0, false);
    }
}

public enum UserVoteAnswer : byte
{
    No = 0,
    Yes = 1,
    Draw = 2,
}
