namespace LibreKO.Network;

public enum ClassChangeSubOpcode : byte
{
    Eligibility = 1,
    JobChangeState = 2,
    StatReset = 3,
    SkillReset = 4,
    StatResetCost = 5,
    JobChangeResult = 6,
    RebirthStatChange = 7,
    RebirthStatReset = 8,
}

public enum ResetKind : byte
{
    Stat = 1,
    Skill = 2,
}

public enum ResetResult : byte
{
    Refused = 0,
    Success = 1,
    NothingToReset = 2,
    NeedsFirstJob = 3,
    InventoryNotEmpty = 4,
}

public enum JobChangeEligibility : byte
{
    Allowed = 1,
    LevelTooLow = 2,
    AlreadyDone = 3,
}

public enum JobChangeOutcome : byte
{
    Succeeded = 1,
    InvalidJob = 2,
    EquipmentWorn = 4,
    JobError = 5,
    NoTokenOrSameClass = 6,
}

public readonly record struct StatResetReply(bool Succeeded, int Money, int[] Stats, int MaxHp, int MaxMp, int Ap, int StatPoints);
