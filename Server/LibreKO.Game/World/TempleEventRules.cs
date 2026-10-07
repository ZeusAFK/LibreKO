using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class TempleEventRules
{
    public const int JoinWindowSeconds = 600;
    public const byte DefaultCountdownMinutes = 10;
    public const int ChaosDurationSeconds = 1200;
    public const int BorderDefenseWarDurationSeconds = 1800;
    public const int JuraidMountainDurationSeconds = 2700;
    public const int UnderTheCastleDurationSeconds = 3600;
    public const byte JuraidMountainDefaultMinLevel = 40;
    public const byte JuraidMountainDefaultMaxLevel = 83;
    public const byte BorderDefenseWarDefaultMinLevel = 20;
    public const byte BorderDefenseWarDefaultMaxLevel = 83;
    public const byte UnderTheCastleDefaultMinLevel = 70;
    public const byte UnderTheCastleDefaultMaxLevel = 83;

    public const int ChaosPlayersPerRoom = 18;
    public const int StartMinuteOfHour = 0;

    public static byte ZoneFor(TempleEvent contest) => contest switch
    {
        TempleEvent.Chaos => (byte)ZoneId.ChaosDungeon,
        TempleEvent.BorderDefenseWar => (byte)ZoneId.BorderDefenseWar,
        TempleEvent.JuraidMountain => (byte)ZoneId.JuradMountain,
        TempleEvent.UnderTheCastle => (byte)ZoneId.UnderCastle,
        _ => 0,
    };

    public static int DurationSecondsFor(TempleEvent contest) => contest switch
    {
        TempleEvent.Chaos => ChaosDurationSeconds,
        TempleEvent.BorderDefenseWar => BorderDefenseWarDurationSeconds,
        TempleEvent.JuraidMountain => JuraidMountainDurationSeconds,
        TempleEvent.UnderTheCastle => UnderTheCastleDurationSeconds,
        _ => 0,
    };

    public static string NameFor(TempleEvent contest) => contest switch
    {
        TempleEvent.Chaos => "Chaos Dungeon",
        TempleEvent.BorderDefenseWar => "Border Defense War",
        TempleEvent.JuraidMountain => "Juraid Mountain",
        TempleEvent.UnderTheCastle => "Under The Castle",
        _ => "Event",
    };
}
