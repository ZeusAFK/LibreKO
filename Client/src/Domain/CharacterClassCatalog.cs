namespace LibreKO.Domain;

public static class CharacterClassCatalog
{
    public const int TierUnknown = 0, TierBeginner = 1, TierNovice = 2, TierMaster = 3;

    public static int Family(int classCode)
    {
        int local = classCode % 100;
        return local switch
        {
            1 or 5 or 6 => 1,
            2 or 7 or 8 => 2,
            3 or 9 or 10 => 3,
            4 or 11 or 12 => 4,
            13 or 14 or 15 => 5,
            _ => 0,
        };
    }

    public static string DisplayName(int classCode) => Family(classCode) switch
    {
        1 => "Warrior",
        2 => "Rogue",
        3 => "Mage",
        4 => "Priest",
        5 => "Kurian",
        _ => $"Class {classCode}",
    };

    public static int Tier(int classCode) => (classCode % 100) switch
    {
        1 or 2 or 3 or 4 or 13 => TierBeginner,
        5 or 7 or 9 or 11 or 14 => TierNovice,
        6 or 8 or 10 or 12 or 15 => TierMaster,
        _ => TierUnknown,
    };

    public static string TierName(int classCode) => Tier(classCode) switch
    {
        TierBeginner => "Beginner",
        TierNovice => "Novice",
        TierMaster => "Master",
        _ => "Unknown",
    };

    public static string SpecializationName(int classCode) => classCode switch
    {
        101 => "Warrior",   105 => "Berserker", 106 => "Guardian",
        102 => "Rogue",     107 => "Hunter",    108 => "Penetrator",
        103 => "Wizard",    109 => "Sorcerer",  110 => "Necromancer",
        104 => "Priest",    111 => "Shaman",    112 => "Dark Priest",
        113 => "Kurian",    114 => "Novice Kurian", 115 => "Master Kurian",
        201 => "Warrior",   205 => "Blade",     206 => "Protector",
        202 => "Rogue",     207 => "Ranger",    208 => "Assassin",
        203 => "Wizard",    209 => "Mage",      210 => "Enchanter",
        204 => "Priest",    211 => "Cleric",    212 => "Druid",
        213 => "Porutu",    214 => "Novice Porutu", 215 => "Master Porutu",
        _ => DisplayName(classCode),
    };
}
