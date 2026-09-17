using Godot;

namespace LibreKO;

public sealed class KoObjects
{
    public struct Placement
    {
        public string Name;
        public Vector3 KoPos;
        public Quaternion Rot;
        public Vector3 Scale;
        public int EventId;
        public int EventType;
        public int Belong;
        public int NpcId;
    }

    public string[] Names = System.Array.Empty<string>();
    public Vector3[] Positions = System.Array.Empty<Vector3>();
    public float[] Rotations = System.Array.Empty<float>();
    public Vector3[] Scales = System.Array.Empty<Vector3>();
    public int[] Events = System.Array.Empty<int>();

    public Placement[] Items
    {
        get
        {
            var items = new Placement[Names.Length];
            for (int i = 0; i < Names.Length; i++)
                items[i] = new Placement
                {
                    Name = Names[i],
                    KoPos = Positions[i],
                    Rot = new Quaternion(Rotations[i * 4], Rotations[i * 4 + 1], Rotations[i * 4 + 2], Rotations[i * 4 + 3]),
                    Scale = Scales[i],
                    EventId = Events[i * 4],
                    EventType = Events[i * 4 + 1],
                    Belong = Events[i * 4 + 2],
                    NpcId = Events[i * 4 + 3],
                };
            return items;
        }
    }

    public static KoObjects? LoadJson(string stem)
    {
        using var f = Godot.FileAccess.Open($"res://assets/terrain/{stem}/placements.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null)
            return null;
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Array)
            return null;
        var arr = parsed.AsGodotArray();
        int cnt = arr.Count;

        var names = new string[cnt];
        var positions = new Vector3[cnt];
        var rotations = new float[cnt * 4];
        var scales = new Vector3[cnt];
        var events = new int[cnt * 4];
        for (int i = 0; i < cnt; i++)
        {
            var o = arr[i].AsGodotDictionary();
            names[i] = o["name"].AsString();
            var p = o["p"].AsGodotArray();
            positions[i] = new Vector3(p[0].AsSingle(), p[1].AsSingle(), p[2].AsSingle());
            var r = o["r"].AsGodotArray();
            rotations[i * 4] = r[0].AsSingle(); rotations[i * 4 + 1] = r[1].AsSingle();
            rotations[i * 4 + 2] = r[2].AsSingle(); rotations[i * 4 + 3] = r[3].AsSingle();
            var s = o["s"].AsGodotArray();
            scales[i] = new Vector3(s[0].AsSingle(), s[1].AsSingle(), s[2].AsSingle());
            if (o.ContainsKey("e"))
            {
                events[i * 4] = o["e"].AsInt32();
                events[i * 4 + 1] = o["et"].AsInt32();
                events[i * 4 + 2] = o["b"].AsInt32();
                events[i * 4 + 3] = o["n"].AsInt32();
            }
        }
        return new KoObjects
        {
            Names = names, Positions = positions, Rotations = rotations, Scales = scales, Events = events,
        };
    }
}
