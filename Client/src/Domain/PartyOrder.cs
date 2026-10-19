using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public sealed class PartyOrder
{
    public const int Up = -1;
    public const int Down = 1;

    private readonly List<int> ids = new();

    public IReadOnlyList<int> Ids => ids;

    public List<T> Arrange<T>(IReadOnlyList<T> members, Func<T, int> idOf)
    {
        var present = new HashSet<int>();
        foreach (var member in members) present.Add(idOf(member));
        ids.RemoveAll(id => !present.Contains(id));
        foreach (var member in members)
            if (!ids.Contains(idOf(member))) ids.Add(idOf(member));
        var arranged = new List<T>(members);
        arranged.Sort((a, b) => ids.IndexOf(idOf(a)).CompareTo(ids.IndexOf(idOf(b))));
        return arranged;
    }

    public bool Move(int id, int step)
    {
        int count = ids.Count;
        int from = ids.IndexOf(id);
        if (from < 0 || count < 2) return false;
        int to = ((from + step) % count + count) % count;
        (ids[from], ids[to]) = (ids[to], ids[from]);
        return true;
    }

    public bool MoveTo(int id, int index)
    {
        int from = ids.IndexOf(id);
        if (from < 0) return false;
        int to = Math.Clamp(index, 0, ids.Count - 1);
        if (to == from) return false;
        ids.RemoveAt(from);
        ids.Insert(to, id);
        return true;
    }

    public static int DropIndex(IReadOnlyList<float> otherCentres, float centre)
    {
        int index = 0;
        foreach (float other in otherCentres)
            if (other < centre) index++;
        return index;
    }
}
