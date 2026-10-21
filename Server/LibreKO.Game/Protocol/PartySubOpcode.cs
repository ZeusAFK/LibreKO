namespace LibreKO.Game.Protocol;

public enum PartySubOpcode : byte
{
    Invite = 2,
    MemberInfo = 3,
    MemberLeft = 4,
    Disband = 5,
    VitalsChange = 6,
    LevelChange = 7,
    ClassChange = 8,
    StatusEffect = 9,
    Commander = 30,
    CommanderMark = 31,
    CommanderOrder = 32,
    UserVote = 33,
    UserVoteResult = 35,
    WarBoard = 38,
    RejoinForget = 39,
    MemberBuffs = 64,
    SignalProbe = 65,
    SignalTable = 66,
    LevelProgress = 67,
    InviteSent = 68,
    InviteClosed = 69,
    InviteWithdrawn = 70,
}

public enum PartyRequest : byte
{
    Create = 1,
    Permit = 2,
    Insert = 3,
    Remove = 4,
    Delete = 5,
    Promote = 28,
    CommandPromote = 30,
    CommanderMark = 31,
    CommanderOrder = 32,
    UserVote = 33,
    UserVoteAnswer = 34,
    WarBoard = 38,
    SignalEcho = 65,
    InviteCancel = 68,
}
