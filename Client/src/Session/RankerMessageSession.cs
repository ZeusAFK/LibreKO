namespace LibreKO.Session;

public sealed class RankerMessageSession
{
    public event Action<string, string>? RankerPaperEvent;

    internal void RaiseRankerPaper(string arg1, string arg2) => RankerPaperEvent?.Invoke(arg1, arg2);

    public event Action<RankerMessageSub, short>? RankerResultEvent;

    internal void RaiseRankerResult(RankerMessageSub arg1, short arg2) => RankerResultEvent?.Invoke(arg1, arg2);
}
