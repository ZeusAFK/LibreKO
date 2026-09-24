using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.Protocol;

public static class MagicSkillRequirement
{
    public const int MasteryPointSlots = 9;

    private const int NationBuffFirstTree = 4000;
    private const int NationBuffEndTree = 10000;
    private const int FirstMasteryTree = 5;
    private const int LastMasteryTree = 8;
    private const int CommandTreeDigit = 9;
    private const int NationBuffBand = 1000;
    private const int FirstNationBuffLine = 1;
    private const int LastNationBuffLine = 5;

    public const int NoNationRole = 0;
    public const int NationRoleFirstBand = 1;

    private static readonly HashSet<int> CommandForms = [20007, 20008, 31501, 31502, 31503, 31504, 31505, 31506, 31507];

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

    public static bool IsGranted(MagicData magic, int transformId, int nationRole = NoNationRole, int nationLine = 0)
    {
        var tree = magic.Skill;
        if (tree is >= NationBuffFirstTree and < NationBuffEndTree)
        {
            if (nationRole == NoNationRole)
                return false;
            if (nationRole == NationRoleFirstBand)
                return tree < NationBuffFirstTree + NationBuffBand;
            if (nationLine is < FirstNationBuffLine or > LastNationBuffLine)
                return true;
            var band = NationBuffFirstTree + nationLine * NationBuffBand;
            return tree >= band && tree < band + NationBuffBand;
        }

        return tree % 10 != CommandTreeDigit || CommandForms.Contains(transformId);
    }

    public static string Describe(MagicData magic)
    {
        var tree = MasteryTreeOf(magic.Skill);
        return tree == 0
            ? $"character level {magic.SkillLevel}"
            : $"{magic.SkillLevel} points in mastery {tree}";
    }
}
