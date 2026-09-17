using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.Protocol;

public static class MagicSkillRequirement
{
    public const int MasteryPointSlots = 9;

    private const int NationBuffFirstTree = 4000;
    private const int NationBuffEndTree = 10000;
    private const int FirstMasteryTree = 5;
    private const int LastMasteryTree = 8;

    public static int MasteryTreeOf(int skillTree)
    {
        if (skillTree is >= NationBuffFirstTree and < NationBuffEndTree)
            return 0;

        var digit = skillTree % 10;
        return digit is >= FirstMasteryTree and <= LastMasteryTree ? digit : 0;
    }

    public static bool IsMet(MagicData magic, int level, byte[] skillPoints)
    {
        if (magic.SkillLevel <= 0)
            return true;

        var tree = MasteryTreeOf(magic.Skill);
        if (tree == 0)
            return level >= magic.SkillLevel;

        return tree < skillPoints.Length && skillPoints[tree] >= magic.SkillLevel;
    }

    public static string Describe(MagicData magic)
    {
        var tree = MasteryTreeOf(magic.Skill);
        return tree == 0
            ? $"character level {magic.SkillLevel}"
            : $"{magic.SkillLevel} points in mastery {tree}";
    }
}
