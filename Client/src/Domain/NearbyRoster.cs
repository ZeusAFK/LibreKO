using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public enum NearbyRelation { Enemy, Ally, Clan, Party }

public readonly record struct NearbyListed(string Name, int Nation, float X, float Z, int ClanId);

public readonly record struct NearbySeen(int Id, string Name, int Nation, int Level, int Class, int ClanId, float X, float Z, bool Gm);

public readonly record struct NearbyViewer(string Name, int Nation, int ClanId, float X, float Z, bool Gm);

public readonly record struct NearbyRow(string Name, int Id, int Nation, int Level, int Class, int ClanId, float Distance, float X, float Z, NearbyRelation Relation);

public static class NearbyRoster
{
    public const int MaxRows = 800;
    public const int NotInSight = -1;

    public static List<NearbyRow> Build(NearbyViewer me, IReadOnlyList<NearbyListed> listed,
        IReadOnlyList<NearbySeen> seen, IReadOnlySet<string> partyNames)
    {
        var rows = new Dictionary<string, NearbyRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in seen)
        {
            if (Same(s.Name, me.Name) || (s.Gm && !me.Gm)) continue;
            rows[s.Name] = new NearbyRow(s.Name, s.Id, s.Nation, s.Level, s.Class, s.ClanId,
                Distance(me, s.X, s.Z), s.X, s.Z, RelationOf(me, s.Name, s.Nation, s.ClanId, partyNames));
        }
        foreach (var l in listed)
        {
            if (Same(l.Name, me.Name) || rows.ContainsKey(l.Name)) continue;
            rows[l.Name] = new NearbyRow(l.Name, NotInSight, l.Nation, 0, 0, l.ClanId,
                Distance(me, l.X, l.Z), l.X, l.Z, RelationOf(me, l.Name, l.Nation, l.ClanId, partyNames));
        }

        var sorted = new List<NearbyRow>(rows.Values);
        sorted.Sort((a, b) =>
        {
            int byDistance = a.Distance.CompareTo(b.Distance);
            return byDistance != 0 ? byDistance : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        if (sorted.Count > MaxRows) sorted.RemoveRange(MaxRows, sorted.Count - MaxRows);
        return sorted;
    }

    public static NearbyRelation RelationOf(NearbyViewer me, string name, int nation, int clanId, IReadOnlySet<string> partyNames)
    {
        if (partyNames.Contains(name.ToLowerInvariant()) || partyNames.Contains(name)) return NearbyRelation.Party;
        if (clanId > 0 && clanId == me.ClanId) return NearbyRelation.Clan;
        return nation == me.Nation ? NearbyRelation.Ally : NearbyRelation.Enemy;
    }

    public static bool SameRows(IReadOnlyList<NearbyRow> a, IReadOnlyList<NearbyRow> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.Name != y.Name || x.Id != y.Id || x.Relation != y.Relation || x.Level != y.Level
                || (int)MathF.Round(x.Distance) != (int)MathF.Round(y.Distance))
                return false;
        }
        return true;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static float Distance(NearbyViewer me, float x, float z)
    {
        float dx = x - me.X, dz = z - me.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
