using System;
using System.Globalization;

namespace LibreKO.Domain;

public enum SiegeRateKind
{
    Moradon = 1,
    Delos = 2,
    DungeonFee = 3,
}

public static class SiegeWarfare
{
    public const byte CastleWarType = 2;
    public const int RateMin = 0;
    public const int RateMax = 5;
    public const int FeeStep = 10_000;
    public const int FeeLimit = 10_000;
    private const int DayTextBase = 10200;
    private const int FirstWeekday = 1;
    private const int LastWeekday = 7;
    private const int RegistrationEndHour = 24;
    private const int RegistrationEndMinute = 0;

    public const int CastleWarText = 10101;
    public const int ChallengerWarText = 10102;
    public const int SignedUpText = 10214;
    public const int ChosenText = 10215;
    public const int PeriodText = 10216;
    public const int FeeText = 10217;
    public const int MoradonPromptText = 10222;
    public const int MoradonConfirmText = 10223;
    public const int DelosPromptText = 10224;
    public const int DelosConfirmText = 10225;
    public const int FeePromptText = 10226;
    public const int FeeConfirmText = 10227;
    public const int CollectableText = 10228;
    public const int EmptyPocketText = 10229;
    public const int RateNotAllowedText = 10230;
    public const int CollectedText = 10231;
    public const int MoradonChangedText = 10232;
    public const int DelosChangedText = 10233;
    public const int FeeChangedText = 10234;
    public const int NothingCollectedText = 10235;
    public const int CancelChallengeText = 10236;
    public const int KarusText = 3102;
    public const int ElMoradText = 3101;
    public const int NoAuthorityText = 6514;
    public const int CoinText = 4533;

    private const int NotKnightsText = 10210;
    private const int ClosedText = 10211;
    private const int NotLeaderText = 10212;
    private const int OccupierText = 10213;
    private const int NoCoinsText = 1702;

    public static int ListErrorText(short result, bool coins) => result switch
    {
        -1 => NotKnightsText,
        -2 => ClosedText,
        -3 => NotLeaderText,
        -4 => OccupierText,
        -5 when coins => NoCoinsText,
        _ => 0,
    };

    public static int CollectErrorText(short result) => result switch
    {
        -5 => EmptyPocketText,
        -4 or -3 => NoAuthorityText,
        _ => 0,
    };

    public static int TaxErrorText(short result) => result switch
    {
        -5 => RateNotAllowedText,
        -4 or -3 => NoAuthorityText,
        _ => 0,
    };

    public static int WarText(byte warType) => warType == CastleWarType ? CastleWarText : ChallengerWarText;

    public static int DayText(int weekday) => DayTextBase + weekday;

    public static int NationText(int nation) => nation switch
    {
        Nations.Karus => KarusText,
        Nations.ElMorad => ElMoradText,
        _ => 0,
    };

    public static string Clock(int hour, int minute) =>
        $"{hour.ToString("00", CultureInfo.InvariantCulture)}:{minute.ToString("00", CultureInfo.InvariantCulture)}";

    public static string RegistrationPeriod(string template, string startDay, int hour, int minute, string endDay) =>
        TextTemplate.Fill(template.Replace("%.2d", "%s"), startDay, Two(hour), Two(minute), endDay,
            Two(RegistrationEndHour), Two(RegistrationEndMinute));

    private static string Two(int value) => value.ToString("00", CultureInfo.InvariantCulture);

    public static bool IsOwnClan(string clan, string mine) =>
        mine.Length > 0 && string.Equals(clan, mine, StringComparison.Ordinal);

    public static int PromptText(SiegeRateKind kind) => kind switch
    {
        SiegeRateKind.Moradon => MoradonPromptText,
        SiegeRateKind.Delos => DelosPromptText,
        _ => FeePromptText,
    };

    public static int ConfirmText(SiegeRateKind kind) => kind switch
    {
        SiegeRateKind.Moradon => MoradonConfirmText,
        SiegeRateKind.Delos => DelosConfirmText,
        _ => FeeConfirmText,
    };

    public static SiegeOfficeSubOpcode WireSub(SiegeRateKind kind) => kind switch
    {
        SiegeRateKind.Moradon => SiegeOfficeSubOpcode.MoradonRate,
        SiegeRateKind.Delos => SiegeOfficeSubOpcode.DelosRate,
        _ => SiegeOfficeSubOpcode.DungeonFee,
    };

    public static int ChangedText(SiegeOfficeSubOpcode sub) => sub switch
    {
        SiegeOfficeSubOpcode.MoradonRate => MoradonChangedText,
        SiegeOfficeSubOpcode.DelosRate => DelosChangedText,
        _ => FeeChangedText,
    };

    public static int Step(SiegeRateKind kind, int value, bool up)
    {
        if (kind != SiegeRateKind.DungeonFee) return Math.Clamp(value + (up ? 1 : -1), RateMin, RateMax);
        int next = value + (up ? FeeStep : -FeeStep);
        return up ? Math.Min(next, FeeLimit) : Math.Max(next, FeeLimit);
    }
}
