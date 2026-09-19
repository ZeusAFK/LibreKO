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
        5 => classCode >= 200 ? "Porutu" : "Kurian",
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

    public static bool IsBeastRace(int race) => race is 6 or 14;

    public static int[] ValidRacesForClass(int classCode, int nation)
    {
        bool isKarus = nation == 1 || classCode < 200;
        if (isKarus)
        {
            if (classCode is 113 or 114 or 115) return new[] { 6 }; // Kurian
            if (classCode is 101 or 105 or 106) return new[] { 1 }; // Ark Tuarek
            if (classCode is 102 or 107 or 108) return new[] { 2 }; // Tuarek
            if (classCode is 103 or 109 or 110) return new[] { 3, 4 }; // Wrinkle Tuarek, Pury Tuarek
            if (classCode is 104 or 111 or 112) return new[] { 2, 4 }; // Tuarek, Pury Tuarek
            return new[] { 1 };
        }
        else
        {
            if (classCode is 213 or 214 or 215) return new[] { 14 }; // Porutu
            if (classCode is 201 or 205 or 206) return new[] { 11, 12, 13 }; // Barbarian, El Morad Man, El Morad Woman
            if (classCode is 202 or 207 or 208 or 203 or 209 or 210 or 204 or 211 or 212)
                return new[] { 12, 13 }; // El Morad Man, El Morad Woman
            return new[] { 11 };
        }
    }

    public static int[] ValidClassesForRace(int race) => race switch
    {
        1 => new[] { 101, 105, 106 },
        2 => new[] { 102, 107, 108, 104, 111, 112 },
        3 => new[] { 103, 109, 110 },
        4 => new[] { 103, 109, 110, 104, 111, 112 },
        6 => new[] { 113, 114, 115 },
        11 => new[] { 201, 205, 206 },
        12 => new[] { 201, 205, 206, 202, 207, 208, 203, 209, 210, 204, 211, 212 },
        13 => new[] { 201, 205, 206, 202, 207, 208, 203, 209, 210, 204, 211, 212 },
        14 => new[] { 213, 214, 215 },
        _ => System.Array.Empty<int>(),
    };

    public static int NationForRace(int race) => race is 1 or 2 or 3 or 4 or 6 ? Nations.Karus : Nations.ElMorad;

    public static int NationForClass(int classCode) => classCode < 200 ? Nations.Karus : Nations.ElMorad;
}
