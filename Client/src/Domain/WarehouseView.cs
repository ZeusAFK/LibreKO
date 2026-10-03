using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public static class WarehouseView
{
    public const int Slots = 192;
    public const int UiPageSize = 48;
    public const int ServerPageSize = 24;
    public const int UiPages = Slots / UiPageSize;

    public static int UiPageOf(int abs) => abs / UiPageSize;

    public static byte ServerPage(int abs) => (byte)(abs / ServerPageSize);

    public static byte ServerCell(int abs) => (byte)(abs % ServerPageSize);

    public static HashSet<int> Matches(IReadOnlyList<int> itemIds, string query, Func<int, string> name)
    {
        var hits = new HashSet<int>();
        var wanted = new ItemQuery(query);
        if (wanted.IsEmpty) return hits;
        for (int i = 0; i < itemIds.Count; i++)
            if (wanted.Matches(itemIds[i], name)) hits.Add(i);
        return hits;
    }

    public static bool[] PagesWithHits(IReadOnlySet<int> hits)
    {
        var pages = new bool[UiPages];
        foreach (int abs in hits)
            if (abs >= 0 && abs < Slots) pages[UiPageOf(abs)] = true;
        return pages;
    }
}
