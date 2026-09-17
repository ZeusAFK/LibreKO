using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const string HandFxNodePrefix = "handfx_";

    private static readonly int[] HandFxSuppressedZones = { 29, 45, 57, 58, 59, 60, 85, 89 };

    private static class HandFxVariant
    {
        public const int Right = 0;
        public const int Left = 1;
        public const int RightKurian = 2;
        public const int LeftKurian = 3;
        public const int Count = 4;
    }

    private static readonly string[] HandFxVariantKeys = { "r", "l", "rk", "lk" };

    private readonly record struct HandFxPart(string Fx, int Bone, Vector3 Pos, float Scale);

    private static Dictionary<int, HandFxPart[][]>? _handFxIndex;

    internal static void AttachHandFx(Node3D body, int[]? gear, int race, int zone)
    {
        var skel = FindFirst<Skeleton3D>(body);
        if (skel == null) return;
        foreach (var old in skel.GetChildren())
            if (old is BoneAttachment3D ba
                && ba.Name.ToString().StartsWith(HandFxNodePrefix, System.StringComparison.Ordinal))
            {
                skel.RemoveChild(ba);
                ba.QueueFree();
            }

        if (gear == null || gear.Length <= InventoryConstants.VisCosGloveLeft) return;
        if (System.Array.IndexOf(HandFxSuppressedZones, zone) >= 0) return;

        _handFxIndex ??= LoadHandFxIndex();
        if (_handFxIndex.Count == 0) return;
        bool kurian = IsKurianRace(race);

        for (int hand = 0; hand < 2; hand++)
        {
            int itemId = gear[hand == 0
                ? InventoryConstants.VisCosGloveRight
                : InventoryConstants.VisCosGloveLeft];
            if (itemId <= 0 || !_handFxIndex.TryGetValue(itemId, out var variants)) continue;
            int variant = hand == 0
                ? (kurian ? HandFxVariant.RightKurian : HandFxVariant.Right)
                : (kurian ? HandFxVariant.LeftKurian : HandFxVariant.Left);

            foreach (var part in variants[variant])
            {
                if (part.Bone < 0 || part.Bone >= skel.GetBoneCount()) continue;
                var attach = new BoneAttachment3D { Name = $"{HandFxNodePrefix}{hand}_{part.Fx}" };
                skel.AddChild(attach);
                attach.BoneIdx = part.Bone;
                var spawned = Fx.Spawn(part.Fx, attach, part.Pos, oneShot: false,
                                       sizeScale: part.Scale);
                if (spawned != null) CullAttachedFx(spawned);
            }
        }
    }

    private static Dictionary<int, HandFxPart[][]> LoadHandFxIndex()
    {
        var map = new Dictionary<int, HandFxPart[][]>();
        using var file = Godot.FileAccess.Open("res://assets/handfx/index.json",
                                               Godot.FileAccess.ModeFlags.Read);
        if (file == null) return map;
        var parsed = Json.ParseString(file.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return map;
        foreach (var kv in parsed.AsGodotDictionary())
        {
            if (!int.TryParse(kv.Key.AsString(), out int itemId)) continue;
            if (kv.Value.VariantType != Variant.Type.Dictionary) continue;
            var entry = kv.Value.AsGodotDictionary();
            var variants = new HandFxPart[HandFxVariant.Count][];
            for (int v = 0; v < HandFxVariant.Count; v++)
                variants[v] = ReadHandFxParts(entry, HandFxVariantKeys[v]);
            map[itemId] = variants;
        }
        return map;
    }

    private static HandFxPart[] ReadHandFxParts(Godot.Collections.Dictionary entry, string key)
    {
        if (!entry.TryGetValue(key, out var lv) || lv.VariantType != Variant.Type.Array)
            return System.Array.Empty<HandFxPart>();
        var list = new List<HandFxPart>();
        foreach (var value in lv.AsGodotArray())
        {
            if (value.VariantType != Variant.Type.Dictionary) continue;
            var part = value.AsGodotDictionary();
            string fx = part.TryGetValue("fx", out var fv) ? fv.AsString() : "";
            if (fx.Length == 0) continue;
            var pos = Vector3.Zero;
            if (part.TryGetValue("p", out var pv) && pv.VariantType == Variant.Type.Array)
            {
                var a = pv.AsGodotArray();
                if (a.Count >= 3)
                    pos = new Vector3((float)a[0].AsDouble(), (float)a[1].AsDouble(),
                                      (float)a[2].AsDouble());
            }
            float scale = part.TryGetValue("scale", out var sv) ? (float)sv.AsDouble() : 1f;
            if (!(scale > 0.0001f)) scale = 1f;
            list.Add(new HandFxPart(fx, part.TryGetValue("bone", out var bv) ? bv.AsInt32() : -1,
                                    pos, scale));
        }
        return list.ToArray();
    }
}
