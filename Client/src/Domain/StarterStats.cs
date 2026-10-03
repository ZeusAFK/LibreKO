using System.Collections.Generic;

namespace LibreKO.Domain;

public static class StarterStats
{
    public readonly record struct Roll(int Str, int Sta, int Dex, int Int, int Mag, int Bonus)
    {
        public int Total => Str + Sta + Dex + Int + Mag;
    }

    public const int StatFloor = 50;
    public const int RequiredTotal = 300;

    private static readonly Dictionary<int, Roll> Table = new()
    {
        [10101] = new(65, 65, 60, 50, 50, 10),
        [20102] = new(60, 60, 70, 50, 50, 10),
        [20104] = new(50, 60, 60, 70, 50, 10),
        [30103] = new(50, 50, 70, 70, 50, 10),
        [40103] = new(50, 50, 70, 70, 50, 10),
        [40104] = new(50, 60, 60, 70, 50, 10),
        [60113] = new(65, 65, 60, 50, 50, 10),
        [110201] = new(65, 65, 60, 50, 50, 10),
        [120201] = new(65, 65, 60, 50, 50, 10),
        [120202] = new(60, 60, 70, 50, 50, 10),
        [120203] = new(50, 50, 70, 70, 50, 10),
        [120204] = new(50, 60, 60, 70, 50, 10),
        [130201] = new(65, 65, 60, 50, 50, 10),
        [130202] = new(60, 60, 70, 50, 50, 10),
        [130203] = new(50, 50, 70, 70, 50, 10),
        [130204] = new(50, 60, 60, 70, 50, 10),
        [140213] = new(65, 65, 60, 50, 50, 10),
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
        1 => Localization.Loc.Tr("Ark Tuarek"),
        2 => Localization.Loc.Tr("Tuarek"),
        3 => Localization.Loc.Tr("Wrinkle Tuarek"),
        4 => Localization.Loc.Tr("Pury Tuarek"),
        6 or 14 => Localization.Loc.Tr("Kurian"),
        11 => Localization.Loc.Tr("Barbarian"),
        12 => Localization.Loc.Tr("El Morad Man"),
        13 => Localization.Loc.Tr("El Morad Woman"),
        _ => $"{Localization.Loc.Tr("Race")} {race}",
    };

    public static string ClassName(int cls) => (cls % 100) switch
    {
        1 => Localization.Loc.Tr("Warrior"),
        2 => Localization.Loc.Tr("Rogue"),
        3 => Localization.Loc.Tr("Mage"),
        4 => Localization.Loc.Tr("Priest"),
        13 => Localization.Loc.Tr("Kurian"),
        _ => $"{Localization.Loc.Tr("Class")} {cls}",
    };

    public static string Blurb(int cls) => (cls % 100) switch
    {
        1 => Localization.Loc.Tr("Front-line fighter. Becomes a Blade for critical damage, or a Protector who shields the party's casters."),
        2 => Localization.Loc.Tr("Ranged specialist. Becomes a Hunter with the bow, or an Assassin who strikes from stealth."),
        3 => Localization.Loc.Tr("Elemental caster. Becomes a Mage of raw destruction, or an Enchanter who weakens and controls the enemy."),
        4 => Localization.Loc.Tr("Support caster. Becomes a Priest who heals and resurrects, or a Pikeman who fights with the spear."),
        13 => Localization.Loc.Tr("Close-quarters summoner. Fights with clawed gauntlets and calls on the spirits that bind them."),
        _ => "",
    };
}
