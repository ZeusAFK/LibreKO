namespace LibreKO.Game.World;

public enum AuctionPhase
{
    Open,
    Settlement,
    Preparation,
}

public readonly record struct AuctionMoment(int Serial, AuctionPhase Phase, int SecondsLeft)
{
    public int Day => SpecialAuctionSchedule.DayOf(Serial);
}

public static class SpecialAuctionSchedule
{
    public const int DaysPerGroup = 13;
    public const int RowsPerGroup = 14;

    private static readonly TimeSpan Close = new(22, 55, 0);
    private static readonly TimeSpan Settled = new(23, 0, 0);
    private static readonly TimeSpan Reopen = new(23, 5, 0);
    private static readonly DateOnly Epoch = new(2000, 1, 1);

    public static int SerialOf(DateOnly date) => date.DayNumber - Epoch.DayNumber;

    public static int DayOf(int serial) => serial % DaysPerGroup + 1;

    public static int Row(int group, int day) => RowsPerGroup * (group - 1) + day;

    public static AuctionMoment At(DateTime utc)
    {
        var date = DateOnly.FromDateTime(utc);
        var time = utc.TimeOfDay;
        if (time >= Reopen)
        {
            var closes = utc.Date.AddDays(1) + Close;
            return new AuctionMoment(SerialOf(date.AddDays(1)), AuctionPhase.Open, (int)(closes - utc).TotalSeconds);
        }
        if (time < Close)
            return new AuctionMoment(SerialOf(date), AuctionPhase.Open, (int)(utc.Date + Close - utc).TotalSeconds);
        return new AuctionMoment(SerialOf(date), time < Settled ? AuctionPhase.Settlement : AuctionPhase.Preparation, 0);
    }
}
