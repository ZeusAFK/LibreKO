using Godot;

namespace LibreKO.Features.Core;

internal interface IAreaCasts
{
    bool BeginAreaCast(SkillData.Skill s);

    void SendAreaCast(SkillData.Skill s, Vector3 impact);
}
