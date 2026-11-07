namespace LibreKO.Game.World;

public readonly record struct EventPostCalendar(DateTime DayStart, DateTime DayEnd, DateTime PeriodStart, DateTime PeriodEnd);

public static class EventPostRules
{
    public const byte LoveKnightOnline = 0;
    public const byte ContinuePlaying = 1;
    public const int RecentLimit = 100;
    public const int HistoryLimit = 100;

    public const int DailyVoucher = 811084000;
    public const int ThreeDayVoucher = 811085000;
    public const int SevenDayVoucher = 811086000;
    public const int FifteenDayVoucher = 811087000;
    public const int ThirtyDayVoucher = 811088000;
    public const int DecorativeVoucher = 811089000;

    private static readonly Dictionary<int, int[]> Milestones = new()
    {
        [3] = [ThreeDayVoucher],
        [7] = [SevenDayVoucher],
        [15] = [FifteenDayVoucher],
        [30] = [ThirtyDayVoucher, DecorativeVoucher],
    };

    public static bool IsEvent(uint eventId) => eventId is LoveKnightOnline or ContinuePlaying;

    public static string Phrase(byte eventId) =>
        eventId == ContinuePlaying ? "I Will Continue Playing Knight Online" : "I Love Knight Online";

    public static bool Matches(byte eventId, string text) =>
        string.Equals(text.Trim(), Phrase(eventId), StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<int> Rewards(int postsInPeriod) =>
        Milestones.TryGetValue(postsInPeriod, out var extra) ? [DailyVoucher, .. extra] : [DailyVoucher];

    public static EventPostCalendar Calendar(DateTime utcNow, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);
        var day = local.Date;
        var month = new DateTime(local.Year, local.Month, 1);
        return new EventPostCalendar(
            Utc(day, zone), Utc(day.AddDays(1), zone), Utc(month, zone), Utc(month.AddMonths(1), zone));
    }

    private static DateTime Utc(DateTime local, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
}
