namespace LibreKO.Session;

public sealed class EventPostSession
{
    public EventPostReply? Board { get; private set; }
    public IReadOnlyList<EventPostHistoryEntry> History { get; private set; } = [];

    public event Action<EventPostReply>? BoardEvent;
    public event Action<EventPostReply>? RegisteredEvent;
    public event Action<IReadOnlyList<EventPostHistoryEntry>>? HistoryEvent;

    internal void ApplyBoard(EventPostReply reply)
    {
        Board = reply;
        BoardEvent?.Invoke(reply);
    }

    internal void ApplyRegistered(EventPostReply reply)
    {
        if (reply.Board != null) Board = reply;
        RegisteredEvent?.Invoke(reply);
    }

    internal void ApplyHistory(IReadOnlyList<EventPostHistoryEntry> history)
    {
        History = history;
        HistoryEvent?.Invoke(history);
    }
}
