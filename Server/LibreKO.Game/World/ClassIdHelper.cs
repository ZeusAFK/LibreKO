using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class ClassIdHelper
{
    public static int GetSubtype(short classId) => Math.Abs(classId) % 100;

    public static AccountNation GetNation(short classId) =>
        (AccountNation)(Math.Abs(classId) / 100);

    public static bool IsWarrior(short classId) => GetSubtype(classId) is 1 or 5 or 6;

    public static int GroupOf(short classId) => IsWarrior(classId) ? Quests.Binding.QuestVocabulary.ClassGroupWarrior
        : IsRogue(classId) ? Quests.Binding.QuestVocabulary.ClassGroupRogue
        : IsMage(classId) ? Quests.Binding.QuestVocabulary.ClassGroupMage
        : IsPriest(classId) ? Quests.Binding.QuestVocabulary.ClassGroupPriest
        : IsPortuKurian(classId) ? Quests.Binding.QuestVocabulary.ClassGroupKurian
        : 0;
    public static bool IsRogue(short classId) => GetSubtype(classId) is 2 or 7 or 8;
    public static bool IsMage(short classId) => GetSubtype(classId) is 3 or 9 or 10;
    public static bool IsPriest(short classId) => GetSubtype(classId) is 4 or 11 or 12;
    public static bool IsPortuKurian(short classId) => GetSubtype(classId) is 13 or 14 or 15;

    public static bool IsBeginner(short classId) => GetSubtype(classId) is 1 or 2 or 3 or 4 or 13;

    public static bool IsNovice(short classId) => GetSubtype(classId) is 5 or 7 or 9 or 11 or 14;

    public static bool IsMastered(short classId) => GetSubtype(classId) is 6 or 8 or 10 or 12 or 15;

    public static bool IsSameJobGroup(short classId, byte newJob) => newJob switch
    {
        1 => IsWarrior(classId),
        2 => IsRogue(classId),
        3 => IsMage(classId),
        4 => IsPriest(classId),
        5 => IsPortuKurian(classId),
        _ => false,
    };

    public const short JobGroupWarrior = 1;
    public const short JobGroupRogue = 2;
    public const short JobGroupMage = 3;
    public const short JobGroupPriest = 4;
    public const short JobGroupPortuKurian = 13;

    public static bool MatchesJobGroup(short classId, short jobGroup) => jobGroup switch
    {
        JobGroupWarrior => IsWarrior(classId),
        JobGroupRogue => IsRogue(classId),
        JobGroupMage => IsMage(classId),
        JobGroupPriest => IsPriest(classId),
        JobGroupPortuKurian => IsPortuKurian(classId),
        > 100 => classId == jobGroup,
        _ => GetSubtype(classId) == jobGroup,
    };
}
