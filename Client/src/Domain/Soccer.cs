using Godot;

namespace LibreKO.Domain;

public sealed record SoccerGoal(int KickerId, int KickerTeam, int GoalTeam, int Blue, int Red);

public sealed record SoccerEnd(int Winner, int Blue, int Red);

public static class Soccer
{
    public const int TeamNone = 0;
    public const int TeamBlue = 1;
    public const int TeamRed = 2;
    public const int StateChangeTeam = 11;
    public const int FirstProvokeEmote = 11;
    public const int ProvokeEmotes = 3;

    public const int PitchRegion = 192;
    public const int BallProto = 3201;
    public const float BallMoveSeconds = 1.5f;
    public const float BallMoveDamp = 0.85f;
    public const float BallTravelSeconds = BallMoveSeconds / BallMoveDamp;
    public const int BlueFx = 70132;
    public const int RedFx = 70131;
    public const float KickDistanceScale = 10f;
    public const float KickInset = 0.5f;
    public const float ClockWrapSeconds = 3600f;
    public const int SecondsPerMinute = 60;

    public const int GoalText = 25000;
    public const int OwnGoalText = 25001;
    public const int ChooseTeamText = 25002;
    public const int WinText = 25003;
    public const int DrawText = 25004;
    public const int BlueText = 25005;
    public const int RedText = 25006;

    public static int FxFor(int team) => team switch
    {
        TeamBlue => BlueFx,
        TeamRed => RedFx,
        _ => 0,
    };

    public static string Clock(double seconds)
    {
        int left = (int)(Mathf.Max(0f, (float)seconds) % ClockWrapSeconds);
        return $"{left / SecondsPerMinute:00}:{left % SecondsPerMinute:00}";
    }

    public static bool IsOwnGoal(int kickerTeam, int goalTeam) => kickerTeam == goalTeam;

    public static Vector3 Landing(Vector3 ball, Vector3 kicker, int tableDistance)
    {
        var dir = ball - kicker;
        dir = dir.LengthSquared() > 0f ? dir.Normalized() : Vector3.Zero;
        return ball + dir * (tableDistance / KickDistanceScale - KickInset);
    }

    public static (int TextId, bool BlueFirst) Result(int winner) => winner switch
    {
        TeamBlue => (WinText, true),
        TeamRed => (WinText, false),
        _ => (DrawText, true),
    };

    public static bool SkillAllowed(bool soccerSkill, bool onPitch, bool hasTeam, bool speedBuff) =>
        soccerSkill ? onPitch && hasTeam : !onPitch || speedBuff;
}
