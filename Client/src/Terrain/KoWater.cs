using Godot;

namespace LibreKO;

public sealed class KoWater
{
    public struct Patch
    {
        public string Tex;
        public int Cols;
        public int Rows;
        public float LevelY;
        public Vector3[] Verts;
    }

    public Patch[] Patches = System.Array.Empty<Patch>();

    public static KoWater? LoadJson(string stem)
    {
        using var f = Godot.FileAccess.Open($"res://assets/terrain/{stem}/water.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null)
            return null;
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Array)
            return null;
        var arr = parsed.AsGodotArray();

        var patches = new System.Collections.Generic.List<Patch>(arr.Count);
        foreach (var entry in arr)
        {
            var o = entry.AsGodotDictionary();
            int cols = o["cols"].AsInt32();
            int rows = o["rows"].AsInt32();
            var vArr = o["verts"].AsGodotArray();
            if (cols < 2 || rows < 2 || vArr.Count != cols * rows)
                continue;

            var verts = new Vector3[vArr.Count];
            for (int i = 0; i < vArr.Count; i++)
            {
                var v = vArr[i].AsGodotArray();
                verts[i] = new Vector3(v[0].AsSingle(), v[1].AsSingle(), v[2].AsSingle());
            }
            patches.Add(new Patch
            {
                Tex = o["tex"].AsString(),
                Cols = cols,
                Rows = rows,
                LevelY = o["level_y"].AsSingle(),
                Verts = verts,
            });
        }
        return patches.Count > 0 ? new KoWater { Patches = patches.ToArray() } : null;
    }
}
