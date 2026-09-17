using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private const string ClanGauntletNode = "clan_gauntlet";
    private const string ClanGauntletFx = "clan_rank_1";
    private const int ClanGradeBest = 1;
    private const int ClanGradeWorst = 5;

    private readonly struct ClanGauntlet
    {
        public string Stem { get; init; }
        public int Joint { get; init; }
        public Vector3 Pos { get; init; }
        public Quaternion Quat { get; init; }
        public Vector3 Scale { get; init; }
    }

    private Dictionary<string, ClanGauntlet>? _clanGauntletIndex;

    private void AttachClanGauntlet(Node3D body, int race, int clanGrade)
    {
        var skel = FindFirst<Skeleton3D>(body);
        if (skel == null) return;

        foreach (var old in skel.GetChildren())
            if (old is BoneAttachment3D ba && ba.Name.ToString() == ClanGauntletNode)
            { skel.RemoveChild(ba); ba.QueueFree(); }

        if (clanGrade is < ClanGradeBest or > ClanGradeWorst) return;

        _clanGauntletIndex ??= LoadClanGauntletIndex();
        if (!_clanGauntletIndex.TryGetValue($"{race}_{clanGrade}", out var g)) return;

        string resPath = $"res://assets/items/clanaddon/{g.Stem}.glb";
        if (!ResourceLoader.Exists(resPath) || ResourceLoader.Load(resPath) is not PackedScene scene)
            return;

        if (g.Joint < 0 || g.Joint >= skel.GetBoneCount()) return;

        var attach = new BoneAttachment3D { Name = ClanGauntletNode };
        skel.AddChild(attach);
        attach.BoneIdx = g.Joint;

        var mesh = scene.Instantiate<Node3D>();
        mesh.Transform = new Transform3D(new Basis(g.Quat).Scaled(g.Scale), g.Pos);
        ForceDoubleSided(mesh);
        attach.AddChild(mesh);

        var glow = Fx.Spawn(ClanGauntletFx, attach, g.Pos, oneShot: false);
        if (glow != null) CullAttachedFx(glow);
    }

    private static Dictionary<string, ClanGauntlet> LoadClanGauntletIndex()
    {
        var map = new Dictionary<string, ClanGauntlet>();
        using var f = Godot.FileAccess.Open(
            "res://assets/items/clanaddon/index.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) return map;
        if (Json.ParseString(f.GetAsText()).AsGodotDictionary() is not { } d) return map;

        foreach (var k in d.Keys)
        {
            var e = d[k].AsGodotDictionary();
            var pos = e["pos"].AsGodotArray();
            var q = e["quat"].AsGodotArray();
            var sc = e["scale"].AsGodotArray();
            map[k.AsString()] = new ClanGauntlet
            {
                Stem = e["stem"].AsString(),
                Joint = e["joint"].AsInt32(),
                Pos = new Vector3((float)pos[0], (float)pos[1], (float)pos[2]),
                Quat = new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]),
                Scale = new Vector3((float)sc[0], (float)sc[1], (float)sc[2]),
            };
        }

        GD.Print($"[clan] {map.Count} gauntlet plugs");
        return map;
    }
}
