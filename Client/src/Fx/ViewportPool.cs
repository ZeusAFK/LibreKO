using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreKO;

internal sealed class ViewportPool<TPart, TItem> where TPart : notnull where TItem : class
{
    private readonly Dictionary<(ulong Viewport, TPart Part), List<TItem>> shelves = new();

    internal int Live { get; private set; }

    internal IEnumerable<TItem> Items => shelves.Values.SelectMany(shelf => shelf);

    internal IReadOnlyList<TItem> Shelf(ulong viewport, TPart part) =>
        shelves.TryGetValue((viewport, part), out var shelf) ? shelf : Array.Empty<TItem>();

    internal void Add(ulong viewport, TPart part, TItem item)
    {
        if (!shelves.TryGetValue((viewport, part), out var shelf)) shelves[(viewport, part)] = shelf = new List<TItem>();
        shelf.Add(item);
        Live++;
    }

    internal bool Forget(ulong viewport, TPart part, TItem item)
    {
        if (!shelves.TryGetValue((viewport, part), out var shelf) || !shelf.Remove(item)) return false;
        Live--;
        if (shelf.Count == 0) shelves.Remove((viewport, part));
        return true;
    }

    internal void Clear()
    {
        shelves.Clear();
        Live = 0;
    }
}
