using Godot;

namespace LibreKO;

public sealed class KoFxPlacements
{
    public struct Placement
    {
        public string Fx;
        public Vector3 KoPos;
        public Quaternion Rot;
        public float Scale;
    }

    public Placement[] Items = System.Array.Empty<Placement>();

    public static KoFxPlacements? LoadJson(string stem)
    {
        using var f = Godot.FileAccess.Open($"res://assets/terrain/{stem}/fx_placements.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null)
            return null;
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Array)
            return null;
        var arr = parsed.AsGodotArray();
        var items = new Placement[arr.Count];
        for (int i = 0; i < arr.Count; i++)
        {
            var o = arr[i].AsGodotDictionary();
            var p = o["p"].AsGodotArray();
            var q = o["q"].AsGodotArray();
            items[i] = new Placement
            {
                Fx = o["fx"].AsString(),
                KoPos = new Vector3(p[0].AsSingle(), p[1].AsSingle(), p[2].AsSingle()),
                Rot = new Quaternion(q[0].AsSingle(), q[1].AsSingle(), q[2].AsSingle(), q[3].AsSingle()),
                Scale = o.ContainsKey("s") ? o["s"].AsSingle() : 1f,
            };
        }
        return new KoFxPlacements { Items = items };
    }
}
