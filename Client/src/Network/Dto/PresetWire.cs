namespace LibreKO.Network;

public enum PresetSubOpcode : byte
{
    Stat = 1,
    Skill = 2,
}

public enum PresetResult : byte
{
    Applied = 1,
    NeedsRedistribution = 2,
    StatApplyFailed = 3,
    SkillApplyFailed = 5,
    ClassError = 7,
    PointsMismatch = 8,
    LevelTooLow = 9,
    NeedsFirstJobChange = 10,
    NeedsSecondJobChange = 11,
    MasterFailed = 12,
}
