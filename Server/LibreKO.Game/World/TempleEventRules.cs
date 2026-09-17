using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public enum TempleEvent : byte
{
    None = 0,
    Chaos = 1,
    BorderDefenseWar = 2,
    JuraidMountain = 3,
}

public static class TempleEventRules
{
    public const int JoinWindowSeconds = 600;
    public const int ChaosDurationSeconds = 1200;
    public const int BorderDefenseWarDurationSeconds = 1800;
    public const int JuraidMountainDurationSeconds = 2700;

    public const int ChaosPlayersPerRoom = 18;
    public const int StartMinuteOfHour = 0;

    public static byte ZoneFor(TempleEvent contest) => contest switch
    {
        TempleEvent.Chaos => (byte)ZoneId.ChaosDungeon,
        TempleEvent.BorderDefenseWar => (byte)ZoneId.BorderDefenseWar,
        TempleEvent.JuraidMountain => (byte)ZoneId.JuradMountain,
        _ => 0,
    };

    public static int DurationSecondsFor(TempleEvent contest) => contest switch
    {
        TempleEvent.Chaos => ChaosDurationSeconds,
        TempleEvent.BorderDefenseWar => BorderDefenseWarDurationSeconds,
        TempleEvent.JuraidMountain => JuraidMountainDurationSeconds,
        _ => 0,
    };
}
