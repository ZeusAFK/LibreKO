using Godot;
using FileAccess = Godot.FileAccess;

namespace LibreKO.Presentation;

public readonly record struct ZoneArea(int Id, float MinX, float MaxX, float MinZ, float MaxZ)
{
    public bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
}

public static class ZoneAreas
{
    private const string AreaFile = "res://assets/terrain/{0}/areas.json";
    private static readonly List<ZoneArea> areas = new();
    private static int loadedZone = -1;

    public static IReadOnlyList<ZoneArea> For(int zone)
    {
        if (zone != loadedZone) Load(zone);
        return areas;
    }

    private static void Load(int zone)
    {
        loadedZone = zone;
        areas.Clear();
        if (ZoneCatalog.Stem(zone) is not { } stem) return;
        string path = string.Format(AreaFile, stem);
        if (!FileAccess.FileExists(path)) return;
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null || Json.ParseString(file.GetAsText()).AsGodotArray() is not { } rows) return;
        foreach (var row in rows)
        {
            var d = row.AsGodotDictionary();
            areas.Add(new ZoneArea(d["id"].AsInt32(), d["minX"].AsSingle(), d["maxX"].AsSingle(),
                d["minZ"].AsSingle(), d["maxZ"].AsSingle()));
        }
    }
}
