using System.Collections.Generic;

namespace LibreKO.Domain;

public readonly record struct SplashDigit(int Digit, float X);

public static class KillSplash
{
    public const int MaxShown = 999;
    public const int AnnouncementStep = 10;
    public const float HoldSeconds = 3f;
    public const float TopY = 100f;
    public const int ResetText = 44052;
    public const int AssistResetText = 44053;
    public const int KarusNameText = 44042;
    public const int ElmoradNameText = 44043;
    public const int StreakCountText = 44044;
    public const int AchievedText = 44045;
    public const string NarrationVoice = "m1";

    private const float OneDigitX = 130f;
    private const float TwoTensX = 120f;
    private const float TwoOnesX = 137f;
    private const float ThreeHundredsX = 114f;
    private const float ThreeTensX = 130f;
    private const float ThreeOnesX = 145f;
    private const float AssistTensX = 110f;
    private const float AssistOnesX = 126f;
    private const int LastNumberedGem = 10;
    private const int ManyKillsGem = 12;

    public static string BackFor(int count) => count switch
    {
        <= 2 => "back_0",
        <= 9 => "back_3",
        10 => "back_4",
        _ when count % AnnouncementStep == 0 => "back_6",
        _ => "back_5",
    };

    public static string GemFor(int count) =>
        $"gem_{(count - 1 > LastNumberedGem ? ManyKillsGem : count - 1)}";

    public static string WordFor(int count, int nation) => count switch
    {
        1 => "word_first_kill",
        2 => "word_double_kill",
        3 => "word_multi_kill",
        4 => "word_diamond_kill",
        5 => "word_emerald_kill",
        6 => "word_dominating",
        7 => "word_ruby_kill",
        8 => "word_olvine_kill",
        9 => "word_sapphire_kill",
        10 => "word_legend_kill",
        20 => nation == Nations.Karus ? "word_karus_hero" : "word_elmorad_hero",
        50 => "word_legendary_hero",
        >= 100 => "word_unstoppable",
        _ => "word_legendary",
    };

    public static int NarrationFor(int count, int nation) => count switch
    {
        >= 1 and <= 10 => count,
        20 => nation == Nations.Karus ? 12 : 11,
        50 or >= 100 => 0,
        _ => 10,
    };

    public static List<SplashDigit> Digits(int count)
    {
        int shown = System.Math.Clamp(count, 0, MaxShown);
        int hundreds = shown / 100, tens = shown % 100 / 10, ones = shown % 10;
        if (hundreds > 0)
            return [new(hundreds, ThreeHundredsX), new(tens, ThreeTensX), new(ones, ThreeOnesX)];
        if (tens > 0)
            return [new(tens, TwoTensX), new(ones, TwoOnesX)];
        return ones > 0 ? [new(ones, OneDigitX)] : [];
    }

    public static List<SplashDigit> AssistDigits(int count)
    {
        int shown = System.Math.Clamp(count, 0, 99);
        int tens = shown / 10, ones = shown % 10;
        return tens > 0 ? [new(tens, AssistTensX), new(ones, AssistOnesX)] : [new(ones, AssistOnesX)];
    }

    public static string GradeFor(int grade) => $"grade_{System.Math.Clamp(grade - 1, 0, 3)}";

    public static int AnnouncementWordText(int count, int nation) => count switch
    {
        10 => 44046,
        20 => nation == Nations.Karus ? 44048 : 44047,
        30 or 40 or 60 or 70 or 80 or 90 => 44049,
        50 => 44050,
        _ => 44051,
    };
}
