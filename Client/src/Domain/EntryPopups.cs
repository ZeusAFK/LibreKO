using System.Collections.Generic;

namespace LibreKO.Domain;

public enum EntryPopup
{
    TimedItems,
    ZoneGuide,
    ZoneCaption,
}

public sealed class EntryPopupQueue
{
    private readonly SortedSet<EntryPopup> pending = new();

    public bool Released { get; private set; }

    public bool Showing { get; private set; }

    public int Count => pending.Count;

    public void Add(EntryPopup popup) => pending.Add(popup);

    public void Release() => Released = true;

    public EntryPopup? TakeNext()
    {
        if (!Released || Showing || pending.Count == 0) return null;
        var next = pending.Min;
        pending.Remove(next);
        Showing = true;
        return next;
    }

    public void Closed() => Showing = false;
}
