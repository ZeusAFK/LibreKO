namespace LibreKO.Features.Core;

internal interface IMovementSkills
{
    void CastBlink(SkillData.Skill s);

    short[]? DashRequestData(int skillId, int targetId);

    bool OnDashEffecting(SkillData.Skill s, int casterId, int targetId, short[] data);
}
