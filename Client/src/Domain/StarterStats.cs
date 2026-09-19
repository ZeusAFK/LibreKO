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
        1 => "Ark Tuarek",
        2 => "Tuarek",
        3 => "Wrinkle Tuarek",
        4 => "Pury Tuarek",
        6 => "Kurian",
        14 => "Porutu",
        11 => "Barbarian",
        12 => "El Morad Man",
        13 => "El Morad Woman",
        _ => $"Race {race}",
    };

    public static string RaceGender(int race) => race switch
    {
        1 or 2 or 3 or 11 or 12 => "Male",
        4 or 13 => "Female",
        6 or 14 => "Beast",
        _ => "Unknown",
    };

    public static string ClassName(int cls) => cls switch
    {
        213 or 214 or 215 => "Porutu",
        _ => (cls % 100) switch
        {
            1 => "Warrior",
            2 => "Rogue",
            3 => "Mage",
            4 => "Priest",
            13 => "Kurian",
            _ => $"Class {cls}",
        }
    };

    public static string Blurb(int cls) => cls switch
    {
        213 or 214 or 215 => "Close-quarters summoner of El Morad. Fights with clawed gauntlets and calls on the "
              + "spirits that bind them.",
        _ => (cls % 100) switch
        {
            1 => "Front-line fighter. Becomes a Blade for critical damage, or a Protector "
                 + "who shields the party's casters.",
            2 => "Ranged specialist. Becomes a Hunter with the bow, or an Assassin who "
                 + "strikes from stealth.",
            3 => "Elemental caster. Becomes a Mage of raw destruction, or an Enchanter who "
                 + "weakens and controls the enemy.",
            4 => "Support caster. Becomes a Priest who heals and resurrects, or a Pikeman "
                 + "who fights with the spear.",
            13 => "Close-quarters summoner of Karus. Fights with clawed gauntlets and calls on the "
                  + "spirits that bind them.",
            _ => "",
        }
    };
}
