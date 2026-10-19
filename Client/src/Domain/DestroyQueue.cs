using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public sealed class DestroyQueue
{
    public readonly record struct Entry(int Slot, int ItemId);

    private readonly List<Entry> entries = new();

    public IReadOnlyList<Entry> Entries => entries;
    public bool Running { get; private set; }
    public int Count => entries.Count;

    public bool Add(int slot, int itemId)
    {
        if (Running || entries.Exists(e => e.Slot == slot)) return false;
        entries.Add(new Entry(slot, itemId));
        return true;
    }

    public void Remove(int slot)
    {
        if (!Running) entries.RemoveAll(e => e.Slot == slot);
    }

    public void Clear()
    {
        entries.Clear();
        Running = false;
    }

    public void DropStale(Func<int, int, bool> stillThere)
    {
        if (!Running) entries.RemoveAll(e => !stillThere(e.Slot, e.ItemId));
    }

    public Entry? Start()
    {
        if (Running || entries.Count == 0) return null;
        Running = true;
        return entries[0];
    }

    public Entry? Advance(bool ok)
    {
        if (!Running || entries.Count == 0) return null;
        entries.RemoveAt(0);
        if (ok && entries.Count > 0) return entries[0];
        Clear();
        return null;
    }
}
