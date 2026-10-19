using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreKO.Domain;

public sealed class PowerUpStoreCart
{
    public const int LineCountMax = 999;

    public sealed class Line
    {
        public required PowerUpStoreEntry Entry { get; set; }
        public int Count { get; internal set; }
        public long Total => (long)Entry.Price * Count;
    }

    private readonly List<Line> lines = [];

    public IReadOnlyList<Line> Lines => lines;
    public bool IsEmpty => lines.Count == 0;
    public int Units => lines.Sum(l => l.Count);
    public long Total => lines.Sum(l => l.Total);

    public bool Affordable(long balance) => Total <= balance;

    public void Add(PowerUpStoreEntry entry, int count = 1)
    {
        if (Find(entry.Id) is { } line)
            line.Count = Math.Min(LineCountMax, line.Count + Math.Max(1, count));
        else
            lines.Add(new Line { Entry = entry, Count = Math.Clamp(count, 1, LineCountMax) });
    }

    public bool CanIncrease(int entryId) => Find(entryId) is { } line && line.Count < LineCountMax;

    public void Increase(int entryId)
    {
        if (Find(entryId) is { } line && line.Count < LineCountMax) line.Count++;
    }

    public void Decrease(int entryId)
    {
        if (Find(entryId) is { } line && line.Count > 1) line.Count--;
    }

    public void Remove(int entryId) => lines.RemoveAll(l => l.Entry.Id == entryId);

    public void Clear() => lines.Clear();

    public bool Reprice(IEnumerable<PowerUpStoreEntry> catalogue)
    {
        var byId = catalogue.ToDictionary(e => e.Id);
        var changed = lines.RemoveAll(l => !byId.ContainsKey(l.Entry.Id)) > 0;
        foreach (var line in lines)
        {
            var current = byId[line.Entry.Id];
            if (current.Price != line.Entry.Price) changed = true;
            line.Entry = current;
        }
        return changed;
    }

    private Line? Find(int entryId) => lines.FirstOrDefault(l => l.Entry.Id == entryId);
}
