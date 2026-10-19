using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO.Domain;

public static class KnightRoyale
{
    public const int DuoMode = 2;
    public const int FirstGasRegion = 20051;
    public const int LastGasRegion = 20055;
    public const int LastArea = LastGasRegion - FirstGasRegion + 1;
    public const int GemsPerKillBase = 1;
    public const double ReturnBoxDelaySeconds = 3.0;
    public const int ReturnBoxSeconds = 5;
    public const double PingSeconds = 5.0;
    public const string RewardRule = "########################################";

    public const int DuoTitleText = 43149;
    public const int TitleText = 43150;
    public const int SurvivorsText = 43151;
    public const int PoisonAreaText = 43152;
    public const int AreaText = 43153;
    public const int AbandonedText = 43154;
    public const int GasReleasedText = 43155;
    public const int GasCountdownText = 43157;
    public const int NextGasText = 43158;
    public const int PathoistText = 43161;
    public const int ChestText = 43162;
    public const int RewardTitleText = 43163;
    public const int RewardRankText = 43164;
    public const int RewardKillsText = 43165;
    public const int RewardTotalText = 43166;
    public const int ReturnText = 43167;
    public const int LevelText = 43168;
    public const int DelegateText = 43169;
    public const int WinnerText = 43170;
    public const int AlreadyText = 17999;
    public const int RejectedText = 18000;
    public const int DuplicateIpText = 44422;
    public const int ChatBlockedText = 30303;

    public static readonly Color Yellow = new(1f, 1f, 0f);
    public static readonly Color Red = new(1f, 0f, 0f);
    public static readonly Color GreenYellow = new("adff2f");
    public static readonly Color Crimson = new("dc143c");
    public static readonly Color Refusal = new("ff3b3b");

    private static readonly int[] Zones = { 37, 38, 39, 76 };

    public static bool IsZone(int zone) => Array.IndexOf(Zones, zone) >= 0;

    public static int AreaOf(int region) =>
        region is >= FirstGasRegion and <= LastGasRegion ? region - FirstGasRegion + 1 : 0;

    public static int AreaCaption(int area, int releasedArea) => area > releasedArea ? AreaText : PoisonAreaText;

    public static int TitleFor(int mode) => mode == DuoMode ? DuoTitleText : TitleText;

    public static int SignUpText(KnightRoyaleSignUpResult result) => result switch
    {
        KnightRoyaleSignUpResult.Already => AlreadyText,
        KnightRoyaleSignUpResult.Rejected => RejectedText,
        KnightRoyaleSignUpResult.Level => LevelText,
        KnightRoyaleSignUpResult.DuplicateIp => DuplicateIpText,
        _ => 0,
    };

    public static bool ShowsCancel(KnightRoyaleSignUpResult result) => result == KnightRoyaleSignUpResult.Already;

    public static string Clock(int seconds)
    {
        seconds = Math.Max(0, seconds);
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    public static bool GasEnds(int nextArea) => nextArea is < 1 or > LastArea;

    public readonly record struct Reward(int Participants, int Rank, int Kills, int RankGems)
    {
        public int KillGems => Kills + GemsPerKillBase;
        public int TotalGems => RankGems + KillGems;
    }

    public static IReadOnlyList<string> RewardLines(Reward r, Func<int, string, string> text) => new[]
    {
        RewardRule,
        text(RewardTitleText, "Pathos Battlegrounds Reward"),
        TextTemplate.Fill(text(RewardRankText, "Total Participants: %d - My Rank: %d : %d Soul Gems"), r.Participants, r.Rank, r.RankGems),
        TextTemplate.Fill(text(RewardKillsText, "%d Kill(s) : %d Soul Gems"), r.Kills, r.KillGems),
        TextTemplate.Fill(text(RewardTotalText, "You have received %d Soul Gems."), r.TotalGems),
        RewardRule,
    };
}
