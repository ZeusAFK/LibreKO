using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public readonly record struct DailyRankEntry(int Rank, string Name);

public sealed record DailyRankBoardData(int Board, int MyPlace, int Change, List<DailyRankEntry> Entries);

public sealed record DailyRankBoardInfo(int Id, string Title, string[] Lines);

public enum DailyRankChangeIcon { None, Up, Down, Same }

public static class DailyRank
{
    public const int BoardSlots = 9;
    public const int FirstBoard = 0;
    public const int AdonisBoard = 1;
    public const int DrakiBoard = 5;
    public const int ClanBoard = 8;
    public const int AdonisUnranked = 255;
    public const int MaxNames = 100;
    public const int RowsPerPage = 8;
    public const int PodiumPlaces = 3;
    public const int NewEntryChange = 50001;
    public const double RequestGapSeconds = 1.0;
    public const int DayKeyMonthFactor = 100;

    public const int TitleText = 43590;
    public const int MyRankText = 43591;
    public const int FooterText = 43592;
    public const int MyClanRankText = 43594;
    public const int TooSoonText = 16820;

    public static bool HasChange(int board) => board != DrakiBoard;

    public static bool AlwaysRequested(int board) => board == DrakiBoard;

    public static string PlaceShown(int place) => place <= 0 ? "-" : place.ToString();

    public static (string Text, DailyRankChangeIcon Icon) ChangeShown(int board, int change) =>
        !HasChange(board) ? ("", DailyRankChangeIcon.None)
        : change == NewEntryChange ? ("New", DailyRankChangeIcon.Same)
        : change == 0 ? ("-", DailyRankChangeIcon.Same)
        : change > 0 ? ($"+{change}", DailyRankChangeIcon.Up)
        : (change.ToString(), DailyRankChangeIcon.Down);

    public static int PageCount(int entries) => Math.Max(1, (entries + RowsPerPage - 1) / RowsPerPage);

    public static IEnumerable<DailyRankEntry> Page(IReadOnlyList<DailyRankEntry> entries, int page)
    {
        for (int i = page * RowsPerPage; i < entries.Count && i < (page + 1) * RowsPerPage; i++)
            yield return entries[i];
    }

    public static int DayKey(DateTime local) => local.Month * DayKeyMonthFactor + local.Day;

    public static bool ShouldBlink(int lastViewDay, DateTime local) => lastViewDay == 0 || lastViewDay != DayKey(local);
}
