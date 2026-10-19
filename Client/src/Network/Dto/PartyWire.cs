namespace LibreKO.Network;

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
}

public enum PartyBbsSubOpcode : byte
{
    Register = 0x01,
    Delete = 0x02,
    Wanted = 0x04,
    Invite = 8,
    InviteAccept = 9,
    List = 0x0B,
}

public enum PartyBbsEntryType : byte
{
    Seeker = 2,
    WantedParty = 3,
}

public enum UserVoteResultKind : byte
{
    Voided = 0,
    WarningGiven = 1,
    WarningCount = 2,
}

public static class PartyWire
{
    public const int MaxMembers = 8;
    public const short StatusMemberRecord = 1;
}

public enum PartyStatusIcon : byte
{
    None = 0,
    OverTimeDamage = 1,
    Poison = 2,
    Slow = 3,
    GreyHealth = 5,
}

public enum SeekingPartyState : byte
{
    NotSeeking = 1,
    SeekingBand = 2,
    Seeking = 3,
}

public enum PartyBoardType : byte
{
    Normal = 0,
    FullMoon = 2,
}

public enum CommanderOrder : byte
{
    None = 0,
    Forward = 1,
    Back = 2,
    BackHome = 3,
    Right = 5,
    Left = 6,
}

public readonly record struct PartyMemberVitals(int CharId, int MaxHp, int Hp, int MaxMp, int Mp);
