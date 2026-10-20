using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Game.World;

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
    private const int TreesPerClass = 10;
    private const int SkillIdsPerClass = 1000;
    private const int RamLook = 30040;
    private const int ClassPrefixesPerNation = 100;

    public const int NoNationRole = 0;
    public const int NationRoleFirstBand = 1;

    private static readonly HashSet<int> CommandForms = [20007, 20008, 31501, 31502, 31503, 31504, 31505, 31506, 31507];

    private static readonly HashSet<int> CorpseGestures = [490049, 490065, 490066, 490067];
    private static readonly HashSet<int> CoinHeals = [490076, 490084];
    private static readonly HashSet<int> CommanderOrders = [492065, 492066, 492067, 492068, 492072, 492073];
    private static readonly HashSet<int> CommanderShouts = [130013, 130014, 230013, 230014];
    private static readonly HashSet<int> RamStrikes = [120011, 220011];

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

    public static bool IsClassPassive(MagicData magic) =>
        magic.PrimaryType == MagicSkillType.None && IsClassTree(magic.Skill);

    public static short ClassOf(int skillId) => (short)(skillId / SkillIdsPerClass);

    public static bool IsClassSkill(int skillId) => IsPlayerClass(ClassOf(skillId));

    // A row outside the class trees needs its item, an event tree, or a place on the client's own skill bars.
    public static bool IsCastableBy(MagicData magic, int classId, int transformId, AccountNation nation)
    {
        var prefix = ClassOf(magic.Id);
        if (IsPlayerClass(prefix))
            return prefix == classId;

        if (magic.UseItem != 0 || ChaosRules.IsEventSkillTree(magic.Skill))
            return true;

        if (RamStrikes.Contains(magic.Id))
            return transformId == RamLook && IsOfNation(prefix, nation);

        if (CommanderShouts.Contains(magic.Id))
            return IsOfNation(prefix, nation);

        return CorpseGestures.Contains(magic.Id) || CoinHeals.Contains(magic.Id) || CommanderOrders.Contains(magic.Id);
    }

    public static bool IsQuietBarRow(int skillId) =>
        CoinHeals.Contains(skillId) || CommanderOrders.Contains(skillId) || CommanderShouts.Contains(skillId);

    private static bool IsOfNation(short prefix, AccountNation nation) => prefix / ClassPrefixesPerNation == (int)nation;

    private static bool IsClassTree(int skillTree) => IsPlayerClass((short)(skillTree / TreesPerClass));

    private static bool IsPlayerClass(short classId) =>
        ClassIdHelper.GetNation(classId) is AccountNation.Karus or AccountNation.ElMorad
        && ClassIdHelper.GetSubtype(classId) is >= (int)ClassSubtype.WarriorBeginner and <= (int)ClassSubtype.KurianMastered;

    public static string Describe(MagicData magic)
    {
        var tree = MasteryTreeOf(magic.Skill);
        return tree == 0
            ? $"character level {magic.SkillLevel}"
            : $"{magic.SkillLevel} points in mastery {tree}";
    }
}
