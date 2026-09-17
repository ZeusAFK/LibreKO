using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private const string WeaponNodePrefix = "weapon_";

    private void AttachWeapons(Node3D body, int[]? gear, int npcType = 0, int npcId = 0)
    {
        if (gear == null) return;
        if (npcType == NpcTypes.FixedPose || NoWeaponNpcIds.Contains(npcId)) return;
        var skel = FindFirst<Skeleton3D>(body);
        if (skel == null) return;
        _weaponIndex ??= LoadWeaponIndex();
        if (_weaponIndex.Count == 0) return;
        foreach (var old in skel.GetChildren())
            if (old is BoneAttachment3D ba
                && ba.Name.ToString().StartsWith(WeaponNodePrefix, System.StringComparison.Ordinal))
            { skel.RemoveChild(ba); ba.QueueFree(); }
        foreach (var old in body.GetChildren())
            if (old is WeaponTrail wt) { body.RemoveChild(wt); wt.QueueFree(); }

        foreach (int slot in new[] { 6, 7 })
        {
            if (slot >= gear.Length || gear[slot] <= 0) continue;
            int baseItemId = ResolveWeaponBaseId(gear[slot]);
            if (!_weaponIndex.TryGetValue(baseItemId, out var w)) continue;
            string resPath = $"res://assets/items/weapon/{w.Stem}.glb";
            if (!ResourceLoader.Exists(resPath) || ResourceLoader.Load(resPath) is not PackedScene scene)
                continue;
            int bone = HandBone(skel, right: slot == 6);
            if (bone < 0) continue;
            var attach = new BoneAttachment3D { Name = $"{WeaponNodePrefix}{slot}" };
            skel.AddChild(attach);
            attach.BoneIdx = bone;
            var mesh = scene.Instantiate<Node3D>();
            mesh.Transform = new Transform3D(new Basis(w.Quat).Scaled(w.Scale), w.Pos);
            ForceDoubleSided(mesh);
            attach.AddChild(mesh);
            AttachWeaponGlow(mesh, gear[slot]);
            ItemShine.Apply(mesh, gear[slot], slot);
            if (slot == 6 && w.TraceSteps > 0)
                WeaponTrail.Create(body, BodyAnim(body), skel, bone,
                                   mesh.Transform, w.Trace0, w.Trace1, w.TraceColor);
        }
    }

    private void AttachWeaponGlow(Node3D weaponMesh, int itemId)
    {
        if (!_glowLoaded) { LoadWeaponGlow(); _glowLoaded = true; }
        bool hasTableGlow = TryResolveWeaponGlow(itemId, out int baseItemId, out var fxName, out var tailFx);
        if (!hasTableGlow)
        {
            baseItemId = ResolveWeaponBaseId(itemId);
            fxName = "";
            tailFx = "";
        }
        string? element = WeaponElement(itemId, targetEffect: false);
        if (element == null && !hasTableGlow)
            return;

        var mi = FindFirst<MeshInstance3D>(weaponMesh);
        Vector3 bladeAt; float radius;
        if (_weaponIndex != null && _weaponIndex.TryGetValue(baseItemId, out var wi) && wi.FxPos is { } fxp)
        {
            bladeAt = mi != null ? mi.Transform * fxp : fxp;
            radius = wi.FxRadius > 0.01f ? wi.FxRadius : 0.6f;
        }
        else if (mi?.Mesh != null)
        {
            var ab = mi.Mesh.GetAabb();
            Vector3 tip = mi.Transform * ab.GetCenter();
            float best = -1f;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = mi.Transform * (ab.Position + ab.Size * new Vector3(c & 1, (c >> 1) & 1, (c >> 2) & 1));
                float d = corner.LengthSquared();
                if (d > best) { best = d; tip = corner; }
            }
            bladeAt = (mi.Transform * ab.GetCenter()).Lerp(tip, 0.62f);
            radius = Mathf.Max(0.2f, (mi.Transform.Basis * ab.Size).Length() * 0.32f);
        }
        else { bladeAt = Vector3.Zero; radius = 0.6f; }

        {
            if (hasTableGlow || element is { Length: > 0 })
            {
                string mainFx = hasTableGlow && fxName.Length > 0 ? fxName : $"{element}_sword0_1";
                if (tailFx.Length == 0 && element is { Length: > 0 })
                    tailFx = $"{element}_sword_tail0_1";
                if (_weaponIndex != null
                    && _weaponIndex.TryGetValue(baseItemId, out var info)
                    && info.FxGuide.Length > 0
                    && FxWeaponGlow.Create(mainFx, tailFx, info.FxGuide) is { } guideGlow)
                {
                    weaponMesh.AddChild(guideGlow);
                    if (mi != null) guideGlow.Transform = mi.Transform;
                }
                else
                {
                    Fx.Spawn(mainFx, weaponMesh, bladeAt);
                    Aabb bounds = mi?.Mesh != null ? mi.Mesh.GetAabb() : new Aabb(bladeAt, Vector3.Zero);
                    if (FxWeaponGlow.CreateTailOnly(tailFx, bounds) is { } scatter)
                    {
                        weaponMesh.AddChild(scatter);
                        if (mi != null) scatter.Transform = mi.Transform;
                    }
                    else Fx.Spawn(tailFx, weaponMesh, bladeAt);
                }
            }
            return;
        }

    }

    private static string? WeaponElement(int itemId, bool targetEffect)
    {
        if (itemId <= 0 || ItemData.ExtFor(itemId) is not { } ext) return null;
        bool Eligible(int damage) => damage >= 64 || (ext.MagicOrRare == 4 && damage > 0);
        if (Eligible(ext.FireDamage)) return "fire";
        if (Eligible(ext.IceDamage)) return "ice";
        if (targetEffect)
        {
            if (Eligible(ext.PoisonDamage)) return "poison";
            if (Eligible(ext.LightningDamage)) return "lighting";
        }
        else
        {
            if (Eligible(ext.LightningDamage)) return "lighting";
            if (Eligible(ext.PoisonDamage)) return "poison";
        }
        return null;
    }

    private int ResolveWeaponBaseId(int itemId)
    {
        if (_weaponIndex != null && _weaponIndex.ContainsKey(itemId)) return itemId;

        int rounded = itemId / 1000 * 1000;
        if (_weaponIndex != null && _weaponIndex.ContainsKey(rounded)) return rounded;
        if (_weaponCat != null && _weaponCat.ContainsKey(rounded)) return rounded;

        int bestBase = 0, bestExt = int.MaxValue;
        if (_weaponIndex != null)
        {
            foreach (var baseId in _weaponIndex.Keys)
            {
                int ext = itemId - baseId;
                if (ext < 0 || ext > 9999 || ext >= bestExt) continue;
                bestBase = baseId;
                bestExt = ext;
            }
        }
        if (bestBase != 0) return bestBase;

        if (_weaponCat != null)
        {
            foreach (var baseId in _weaponCat.Keys)
            {
                int ext = itemId - baseId;
                if (ext < 0 || ext > 9999 || ext >= bestExt) continue;
                bestBase = baseId;
                bestExt = ext;
            }
        }
        return bestBase != 0 ? bestBase : rounded;
    }

    private bool TryResolveWeaponGlow(int itemId, out int baseItemId, out string fxName)
        => TryResolveWeaponGlow(itemId, out baseItemId, out fxName, out _);

    private void LoadWeaponGlow()
    {
        _weaponCat = new System.Collections.Generic.Dictionary<int, int>();
        _glowCats = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<int, string>>();
        _glowTails = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<int, string>>();
        using var f = Godot.FileAccess.Open("res://assets/items/weapon/glow.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) return;
        if (Json.ParseString(f.GetAsText()).AsGodotDictionary() is not { } root) return;
        if (root.TryGetValue("weaponCat", out var wc) && wc.AsGodotDictionary() is { } wcd)
            foreach (var k in wcd.Keys)
                if (int.TryParse(k.AsString(), out int baseId)) _weaponCat[baseId] = wcd[k].AsInt32();
        LoadGlowSection(root, "cats", _glowCats);
        LoadGlowSection(root, "tails", _glowTails);
    }

    private static void LoadGlowSection(Godot.Collections.Dictionary root, string key,
        System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<int, string>> into)
    {
        if (!root.TryGetValue(key, out var cs) || cs.AsGodotDictionary() is not { } csd) return;
        foreach (var k in csd.Keys)
        {
            if (!int.TryParse(k.AsString(), out int cat) || csd[k].AsGodotDictionary() is not { } extd) continue;
            var map = new System.Collections.Generic.Dictionary<int, string>();
            foreach (var e in extd.Keys)
                if (int.TryParse(e.AsString(), out int ext)) map[ext] = extd[e].AsString();
            into[cat] = map;
        }
    }

    private static int HandBone(Skeleton3D skel, bool right)
    {
        string wristKey = right ? "rightwrist" : "leftwrist";
        for (int i = 0; i < skel.GetBoneCount(); i++)
        {
            if (skel.GetBoneName(i).Replace(" ", "").ToLower() != wristKey) continue;
            var kids = skel.GetBoneChildren(i);
            return kids.Length > 0 ? kids[0] : i;
        }
        return -1;
    }

    private static System.Collections.Generic.Dictionary<int, WeaponInfo> LoadWeaponIndex()
    {
        var map = new System.Collections.Generic.Dictionary<int, WeaponInfo>();
        using var f = Godot.FileAccess.Open("res://assets/items/weapon/index.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) return map;
        if (Json.ParseString(f.GetAsText()).AsGodotDictionary() is { } d)
            foreach (var k in d.Keys)
            {
                if (!int.TryParse(k.AsString(), out int id)) continue;
                var e = d[k].AsGodotDictionary();
                var pos = e["pos"].AsGodotArray();
                var q = e["quat"].AsGodotArray();
                var sc = e["scale"].AsGodotArray();
                var wi = new WeaponInfo
                {
                    Stem = e["stem"].AsString(),
                    Joint = e["joint"].AsInt32(),
                    Pos = new Vector3((float)pos[0], (float)pos[1], (float)pos[2]),
                    Quat = new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]),
                    Scale = new Vector3((float)sc[0], (float)sc[1], (float)sc[2]),
                };
                if (e.ContainsKey("fxp"))
                {
                    var fp = e["fxp"].AsGodotArray();
                    wi.FxPos = new Vector3((float)fp[0], (float)fp[1], (float)fp[2]);
                    wi.FxRadius = (float)e["fxr"].AsDouble();
                }
                if (e.ContainsKey("fxg"))
                    wi.FxGuide = e["fxg"].AsString();
                if (e.ContainsKey("trstep"))
                {
                    wi.TraceSteps = e["trstep"].AsInt32();
                    wi.TraceColor = (uint)e["trcol"].AsInt64();
                    wi.Trace0 = (float)e["tr0"].AsDouble();
                    wi.Trace1 = (float)e["tr1"].AsDouble();
                }
                map[id] = wi;
            }

        using (var aliasesFile = Godot.FileAccess.Open(
                   "res://assets/items/weapon/visual_aliases.json",
                   Godot.FileAccess.ModeFlags.Read))
            if (aliasesFile != null)
            {
                var parsed = Json.ParseString(aliasesFile.GetAsText());
                if (parsed.VariantType == Variant.Type.Dictionary)
                {
                    var aliases = parsed.AsGodotDictionary();
                    foreach (var key in aliases.Keys)
                        if (int.TryParse(key.AsString(), out int exactId)
                            && map.TryGetValue(aliases[key].AsInt32(), out var canonical))
                            map[exactId] = canonical;
                }
            }
        return map;
    }
}
