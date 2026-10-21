namespace LibreKO.Network;

public static class PartyRequest
{
    public const byte Create = 1, Permit = 2, Insert = 3, Remove = 4, Delete = 5, Promote = 28,
        CommanderAppoint = 30, CommanderMark = 31, CommanderOrder = 32, UserVote = 33, UserVoteAnswer = 34,
        InviteCancel = 68;
}
