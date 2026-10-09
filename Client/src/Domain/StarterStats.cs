using System.Collections.Generic;

namespace LibreKO.Domain;

public static class StarterStats
{
    public readonly record struct Roll(int Str, int Sta, int Dex, int Int, int Mag, int Bonus)
    {
        public int Total => Str + Sta + Dex + Int + Mag;

        public int StatAtRow(int row) => row switch
        {
            0 => Str, 1 => Sta, 2 => Dex, 3 => Int, 4 => Mag, _ => 0,
        };
    }

    public const int StatFloor = 50;
    public const int RequiredTotal = 300;

    public static Roll BaseForClass(int cls) => CharacterClassCatalog.Family(cls) switch
    {
        CharacterClassCatalog.FamilyWarrior or CharacterClassCatalog.FamilyKurian => new(65, 65, 60, 50, 50, 10),
        CharacterClassCatalog.FamilyRogue => new(60, 60, 70, 50, 50, 10),
        CharacterClassCatalog.FamilyMage => new(50, 50, 70, 70, 50, 10),
        CharacterClassCatalog.FamilyPriest => new(50, 60, 60, 70, 50, 10),
        _ => new(58, 58, 58, 58, 58, 10),
    };

    private static readonly Dictionary<int, Roll> Table = new()
    {
        [10101] = BaseForClass(101),
        [20102] = BaseForClass(102),
        [20104] = BaseForClass(104),
        [30103] = BaseForClass(103),
        [40103] = BaseForClass(103),
        [40104] = BaseForClass(104),
        [60113] = BaseForClass(113),
        [110201] = BaseForClass(201),
        [120201] = BaseForClass(201),
        [120202] = BaseForClass(202),
        [120203] = BaseForClass(203),
        [120204] = BaseForClass(204),
        [130201] = BaseForClass(201),
        [130202] = BaseForClass(202),
        [130203] = BaseForClass(203),
        [130204] = BaseForClass(204),
        [140213] = BaseForClass(213),
    };

    private static readonly Dictionary<int, int[]> RaceClasses = new()
    {
        [1] = new[] { 101 },
        [2] = new[] { 102, 104 },
        [3] = new[] { 103 },
        [4] = new[] { 103, 104 },
        [6] = new[] { 113 },
        [11] = new[] { 201 },
        [12] = new[] { 201, 202, 203, 204 },
        [13] = new[] { 201, 202, 203, 204 },
        [14] = new[] { 213 },
    };

    public static int[] RacesFor(int nation) =>
        nation == Nations.Karus ? new[] { 1, 2, 3, 4, 6 } : new[] { 11, 12, 13, 14 };

    public static int[] ClassesFor(int race) =>
        RaceClasses.TryGetValue(race, out var c) ? c : System.Array.Empty<int>();

    public static Roll? For(int race, int cls) =>
        Table.TryGetValue(race * 10000 + cls, out var r) ? r : null;

    public static string RaceName(int race) => race switch
    {
        1 => "Ark Tuarek",
        2 => "Tuarek",
        3 => "Wrinkle Tuarek",
        4 => "Pury Tuarek",
        6 or 14 => "Kurian",
        11 => "Barbarian",
        12 => "El Morad Man",
        13 => "El Morad Woman",
        _ => $"Race {race}",
    };

    public static string ClassName(int cls) => (cls % 100) switch
    {
        1 => "Warrior",
        2 => "Rogue",
        3 => "Mage",
        4 => "Priest",
        13 => "Kurian",
        _ => $"Class {cls}",
    };

    public static string Blurb(int cls) => (cls % 100) switch
    {
        1 => "Front-line fighter. Becomes a Blade for critical damage, or a Protector "
             + "who shields the party's casters.",
        2 => "Ranged specialist. Becomes a Hunter with the bow, or an Assassin who "
             + "strikes from stealth.",
        3 => "Elemental caster. Becomes a Mage of raw destruction, or an Enchanter who "
             + "weakens and controls the enemy.",
        4 => "Support caster. Becomes a Priest who heals and resurrects, or a Pikeman "
             + "who fights with the spear.",
        13 => "Close-quarters summoner. Fights with clawed gauntlets and calls on the "
              + "spirits that bind them.",
        _ => "",
    };
}
