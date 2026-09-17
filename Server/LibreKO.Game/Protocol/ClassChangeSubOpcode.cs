namespace LibreKO.Game.Protocol;

public enum ClassChangeSubOpcode : byte
{
    Eligibility = 1,
    StatReset = 2,
    SkillReset = 3,
    StatResetCost = 4,
    JobChangeState = 5,
    JobChangeResult = 6,
    RebirthStatChange = 7,
    RebirthStatReset = 8,
}
