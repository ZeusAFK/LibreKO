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
    TargetNumber = 31,
    Alert = 32,
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
    TargetNumber = 31,
    Alert = 32,
}
