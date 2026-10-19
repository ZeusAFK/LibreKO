namespace LibreKO.Session;

public sealed class DailyRankSession
{
    public event Action<DailyRankBoardData>? DailyRankBoardEvent;

    internal void RaiseDailyRankBoard(DailyRankBoardData dailyRankBoardData) => DailyRankBoardEvent?.Invoke(dailyRankBoardData);

    public event Action? DailyRankResetEvent;

    internal void RaiseDailyRankReset() => DailyRankResetEvent?.Invoke();
}
