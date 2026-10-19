using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreKO.Domain;

public sealed class ShopCatalogue
{
    public const int PageSize = 24;

    public readonly record struct Entry(int ItemId, int Page, int Cell);

    public static readonly ShopCatalogue Empty = new(Array.Empty<Entry>(), _ => "");

    private readonly Entry[] entries;
    private readonly Func<int, string> name;

    public ShopCatalogue(IEnumerable<Entry> offered, Func<int, string> name)
    {
        entries = offered
            .Where(e => e.ItemId != 0 && e.Cell >= 0 && e.Cell < PageSize)
            .OrderBy(e => e.Page).ThenBy(e => e.Cell)
            .ToArray();
        this.name = name;
        Pages = entries.Select(e => e.Page).Distinct().ToArray();
    }

    public IReadOnlyList<int> Pages { get; }

    public int Count => entries.Length;

    public int[] Page(int page)
    {
        var cells = new int[PageSize];
        foreach (var entry in entries)
            if (entry.Page == page) cells[entry.Cell] = entry.ItemId;
        return cells;
    }

    public IReadOnlyList<int> Search(string query)
    {
        var wanted = new ItemQuery(query);
        if (wanted.IsEmpty) return Array.Empty<int>();
        return entries.Where(e => wanted.Matches(e.ItemId, name)).Select(e => e.ItemId).ToArray();
    }

    public static int[] Slice(IReadOnlyList<int> items, int page)
    {
        var cells = new int[PageSize];
        for (int i = 0; i < PageSize && page * PageSize + i < items.Count; i++)
            cells[i] = items[page * PageSize + i];
        return cells;
    }

    public static int PagesFor(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);
}
