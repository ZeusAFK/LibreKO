namespace LibreKO.Network;

public enum EventPostEvent : uint
{
    LoveKnightOnline = 0,
    ContinuePlaying = 1,
}

public enum EventPostResult : uint
{
    Failed = 0,
    Accepted = 1,
    TryLater = 5,
    WrongText = 101,
    AlreadyPosted = 102,
}

public sealed record EventPostEntry(string Server, string Name, string Text, uint Time);

public sealed record EventPostBoard(uint Today, uint InPeriod, uint PeriodStart, uint PeriodEnd, IReadOnlyList<EventPostEntry> Posts);

public sealed record EventPostReply(EventPostEvent Event, EventPostResult Result, EventPostBoard? Board);

public readonly record struct EventPostHistoryEntry(EventPostEvent Event, uint Time);

public static class EventPostWire
{
    public const int RecentMax = 100;
    public const int HistoryMax = 100;
    private const uint HistoryShown = 1;
    private const int ReplyBytes = 8;
    private const int CountsBytes = 18;
    private const int HistoryEntryBytes = 5;
    private const int HistoryHeadBytes = 6;

    public static IReadOnlyList<EventPostHistoryEntry>? ReadHistory(Packet p)
    {
        if (p.RemainingBytes < HistoryHeadBytes || p.ReadUInt() != HistoryShown) return null;
        int count = Math.Clamp((int)p.ReadShort(), 0, HistoryMax);
        var entries = new List<EventPostHistoryEntry>(count);
        for (int i = 0; i < count && p.RemainingBytes >= HistoryEntryBytes; i++)
            entries.Add(new EventPostHistoryEntry((EventPostEvent)p.ReadByte(), p.ReadUInt()));
        return entries;
    }

    public static EventPostReply? ReadReply(Packet p)
    {
        if (p.RemainingBytes < ReplyBytes) return null;
        var eventId = (EventPostEvent)p.ReadUInt();
        var result = (EventPostResult)p.ReadUInt();
        return new EventPostReply(eventId, result, result == EventPostResult.Accepted ? ReadBoard(p) : null);
    }

    private static EventPostBoard? ReadBoard(Packet p)
    {
        if (p.RemainingBytes < CountsBytes) return null;
        uint today = p.ReadUInt();
        uint inPeriod = p.ReadUInt();
        uint start = p.ReadUInt();
        uint end = p.ReadUInt();
        int count = Math.Clamp((int)p.ReadShort(), 0, RecentMax);
        var posts = new List<EventPostEntry>(count);
        for (int i = 0; i < count && p.RemainingBytes > 0; i++)
        {
            string server = p.ReadSByteString();
            string name = p.ReadSByteString();
            string text = p.ReadSByteString();
            if (p.RemainingBytes < sizeof(uint)) break;
            posts.Add(new EventPostEntry(server, name, text, p.ReadUInt()));
        }
        return new EventPostBoard(today, inPeriod, start, end, posts);
    }
}
