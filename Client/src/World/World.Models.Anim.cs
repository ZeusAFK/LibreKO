using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private const string ModelNodeName = "Model";

    private (Node3D body, AnimationPlayer? anim) MakeAnimatedEntity(PackedScene scene, string name, float scale)
    {
        if (scale <= 0f) scale = 1f;
        var body = new Node3D();

        var visual = new Node3D { Name = ModelNodeName, Scale = new Vector3(scale, scale, scale) };
        body.AddChild(visual);
        var inst = scene.Instantiate<Node3D>();
        inst.RotationDegrees = new Vector3(0, 180, 0);
        visual.AddChild(inst);
        ForceDoubleSided(inst);

        var anim = FindFirst<AnimationPlayer>(inst);
        if (anim != null)
            RegisterAnimationMetadata(anim, scene.ResourcePath.GetBaseName() + ".anim.json");

        body.AddChild(NameLabel(name, ModelTopY(inst) * scale + 0.35f));
        return (body, anim);
    }

    private static void RegisterAnimationMetadata(AnimationPlayer anim, string path)
    {
        var map = new System.Collections.Generic.Dictionary<int, AnimMeta>();
        var names = new System.Collections.Generic.Dictionary<string, AnimMeta>(
            System.StringComparer.OrdinalIgnoreCase);
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f != null)
        {
            var root = Json.ParseString(f.GetAsText()).AsGodotDictionary();
            if (root.TryGetValue("animations", out var av) && av.VariantType == Variant.Type.Array)
                foreach (var v in av.AsGodotArray())
                {
                    var e = v.AsGodotDictionary();
                    if (!e.TryGetValue("index", out var iv) || !e.TryGetValue("exported", out var nv)
                        || nv.VariantType != Variant.Type.String) continue;
                    string name = nv.AsString();
                    if (name.Length == 0 || !anim.HasAnimation(name)) continue;
                    var meta = new AnimMeta
                    {
                        Name = name,
                        Start = MetaFloat(e, "start"),
                        End = MetaFloat(e, "end"),
                        Fps = Mathf.Max(0.001f, MetaFloat(e, "fps", 30f)),
                        Blend = Mathf.Clamp(MetaFloat(e, "blend"), 0f, 1f),
                        Strike0 = MetaFloat(e, "strike0"),
                        Strike1 = MetaFloat(e, "strike1"),
                        Sound0 = MetaFloat(e, "sound0"),
                        Sound1 = MetaFloat(e, "sound1"),
                        TraceStart = MetaFloat(e, "plugTraceStart"),
                        TraceEnd = MetaFloat(e, "plugTraceEnd"),
                    };
                    map[iv.AsInt32()] = meta;
                    names[name] = meta;
                }
        }
        AnimationMetaByIndex[anim.GetInstanceId()] = map;
        AnimationMetaByName[anim.GetInstanceId()] = names;
    }

    private static string? AnimationNameAt(AnimationPlayer anim, int sourceIndex)
    {
        if (AnimationMetaByIndex.TryGetValue(anim.GetInstanceId(), out var map))
            return map.TryGetValue(sourceIndex, out var exact) ? exact.Name : null;
        var list = anim.GetAnimationList();
        return sourceIndex > 0 && sourceIndex < list.Length ? list[sourceIndex].ToString() : null;
    }

    private static AnimMeta? AnimationMetaAt(AnimationPlayer anim, int sourceIndex) =>
        AnimationMetaByIndex.TryGetValue(anim.GetInstanceId(), out var map)
        && map.TryGetValue(sourceIndex, out var meta) ? meta : null;

    private static AnimMeta? AnimationMetaFor(AnimationPlayer anim, string name) =>
        AnimationMetaByName.TryGetValue(anim.GetInstanceId(), out var map)
        && map.TryGetValue(name, out var meta) ? meta : null;

    internal static bool TraceWindow(AnimationPlayer anim, string clip,
                                     out float t0, out float t1, out float fps)
    {
        t0 = t1 = -1f;
        fps = 30f;
        if (AnimationMetaFor(anim, clip) is not { } meta) return false;
        t0 = meta.TraceStartTime;
        t1 = meta.TraceEndTime;
        fps = meta.Fps;
        return t0 >= 0f && t1 > t0 && fps > 0f;
    }

    private static float MetaFloat(Godot.Collections.Dictionary d, string key, float fallback = 0f) =>
        d.TryGetValue(key, out var value) && value.VariantType != Variant.Type.Nil
            ? (float)value.AsDouble() : fallback;

    private static string[] StanceIdleClips(int[]? gear)
    {
        var (kind, leftKind, _) = HandLoadout(gear);
        if (WeaponAnimation.IsOneHandBlade(kind) && WeaponAnimation.IsOneHandBlade(leftKind))
            return DualIdleClips;
        if (kind == WeaponAnimation.Crossbow) return CrossbowIdleClips;
        if (leftKind is WeaponAnimation.Bow or WeaponAnimation.LongBow
            || kind is WeaponAnimation.Bow or WeaponAnimation.LongBow) return BowIdleClips;
        return kind switch
        {
            WeaponAnimation.NoItem or 0 => UnarmedIdleClips,
            11 => DaggerIdleClips,
            22 => TwoHandIdleClips,
            21 => SwordIdleClips,
            31 or 32 => AxeIdleClips,
            41 or 181 => BluntIdleClips,
            42 => TwoBluntIdleClips,
            51 => SpearIdleClips,
            52 => PolearmIdleClips,
            110 => StaffIdleClips,
            140 => JamadarIdleClips,
            _ => IdleClips,
        };
    }

    private static string[] ClipsFor(string state) =>
        state == "run" ? RunClips
        : state == "walk" ? WalkClips
        : state == "walk_reverse" ? WalkReverseClips
        : state == "sit" ? SitClips
        : IdleClips;

    private static void PlayClip(Ent e, string state)
    {
        bool stance = !e.IsNpc && state == "idle" && (e.CombatStance || Now() < e.CombatStanceUntil);
        PlayClipOn(
            e.Anim,
            ref e.Clip,
            state,
            stance ? StanceIdleClips(e.Gear) : null,
            stance ? StanceIdleSlot(e.Gear) : -1);
    }

    private static void PlayClipOn(
        AnimationPlayer? anim, ref string? clip, string state, string[]? idleOverride = null,
        int idleSlot = -1)
    {
        if (anim == null || clip == state) return;
        string[] requested = state == "idle" && idleOverride != null ? idleOverride : ClipsFor(state);
        string? name = null;
        if (idleOverride != null)
        {
            if (idleSlot >= 0) name = AnimationNameAt(anim, idleSlot);
            name ??= Pick(anim, requested);
        }
        if (name == null && LocomotionSlot(state) is var slot && slot >= 0)
            name = AnimationNameAt(anim, slot);
        name ??= Pick(anim, requested) ?? AnimationNameAt(anim, 0) ?? Pick(anim, idleOverride ?? IdleClips);
        if (name == null || !anim.HasAnimation(name)) return;

        var res = anim.GetAnimation(name);
        if (res != null) res.LoopMode = Animation.LoopModeEnum.Linear;
        double blend = AnimationMetaFor(anim, name)?.Blend ?? AnimBlend;
        anim.Play(name, blend);
        clip = state;
    }

    private static string? Pick(AnimationPlayer ap, string[] names)
    {
        foreach (var n in names) if (ap.HasAnimation(n)) return n;
        return null;
    }

    private static int LocomotionSlot(string state) => state switch
    {
        "idle" => 0,
        "walk" => 1,
        "run" => 2,
        "walk_reverse" => 3,
        _ => -1,
    };
}
