namespace LibreKO.Session;

public sealed class LotterySession
{
    public event Action<LotteryState>? LotteryStateEvent;

    internal void RaiseLotteryState(LotteryState lotteryState) => LotteryStateEvent?.Invoke(lotteryState);

    public event Action<LotteryJoinResult>? LotteryJoinEvent;

    internal void RaiseLotteryJoin(LotteryJoinResult lotteryJoinResult) => LotteryJoinEvent?.Invoke(lotteryJoinResult);

    public event Action<int>? LotteryProgressEvent;

    internal void RaiseLotteryProgress(int value) => LotteryProgressEvent?.Invoke(value);

    public event Action<string, List<LotteryWinner>>? LotteryEndedEvent;

    internal void RaiseLotteryEnded(string arg1, List<LotteryWinner> list) => LotteryEndedEvent?.Invoke(arg1, list);

    public event Action? LotteryCloseEvent;

    internal void RaiseLotteryClose() => LotteryCloseEvent?.Invoke();
}
