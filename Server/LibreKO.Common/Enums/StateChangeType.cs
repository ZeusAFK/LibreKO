namespace LibreKO.Common.Enums;

public enum StateChangeType : byte
{
    Pose = 1,
    NeedParty = 2,
    Abnormal = 3,
    Action = 4,
    Visibility = 5,
    Stealth = 7,
    CombatStance = 8,
    Transformation = 19,
}

public enum CombatStanceState : byte
{
    Relaxed = 0,
    Ready = 1,
}

public enum UserPoseState : byte
{
    Standing = 1,
    Sitting = 2,
    Dead = 3,
    Blinking = 4,
    Monument = 6,
    Mining = 7,
    Fishing = 8,
}

public enum NpcPoseState : byte
{
    Asleep = 4,
    Awake = 5,
}
