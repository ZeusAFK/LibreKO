namespace LibreKO.Domain;

public static class TempleJoin
{
    public const int MapOfChaosCentreText = 18800;
    public const int ChaosSignUpEventId = 24;
    public const string LevelTooHighText = "Your level is too high to apply.";

    public static bool SignedUp(TempleJoinResult result) => result is TempleJoinResult.Joined or TempleJoinResult.AlreadyJoined;

    public static int SignUpEventId(TempleEventType type) =>
        type == TempleEventType.Chaos ? ChaosSignUpEventId : (int)type;

    public static int TextId(TempleJoinResult result) => result switch
    {
        TempleJoinResult.AlreadyJoined => 17999,
        TempleJoinResult.WrongPoints => 18002,
        TempleJoinResult.NeedsMapOfChaos => 18801,
        TempleJoinResult.LadderPoints or TempleJoinResult.ApplyLadderPoints => 41005,
        TempleJoinResult.OtherDungeon => 44542,
        TempleJoinResult.LevelTooLow => 44543,
        TempleJoinResult.DuringWar => 44544,
        TempleJoinResult.DuplicateIp => 44422,
        TempleJoinResult.NotOpen => 44545,
        _ => 0,
    };

    public static string CountLine(bool chaos, int karus, int elMorad) =>
        chaos ? $"Signed up: {karus + elMorad}" : $"Karus {karus}  ·  El Morad {elMorad}";
}
