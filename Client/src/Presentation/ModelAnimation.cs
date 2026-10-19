using Godot;

namespace LibreKO.Presentation;

internal static class ModelAnimation
{
    internal const int ActionRankStruck = 0;
    internal const int ActionRankBasic = 1;
    internal const int ActionRankPosture = 2;
    internal const int ActionRankSkill = 3;
    internal const int ActionRankDeath = 4;

    internal const string ModelNodeName = "Model";

    private static readonly Dictionary<string, (Dictionary<int, ModelNodes.AnimMeta> ByIndex, Dictionary<string, ModelNodes.AnimMeta> ByName)>
        AnimationMetaByPath = new();

    private static readonly HashSet<string> DoubleSidedScenes = new();

    internal static void ForceDoubleSidedOnce(Node inst, string scenePath)
    {
        if (DoubleSidedScenes.Add(scenePath)) ModelNodes.ForceDoubleSided(inst);
    }

    private const float ClipLengthTolerance = 0.01f;

    private static void MatchClipLength(Animation? clip, ModelNodes.AnimMeta meta)
    {
        if (clip == null || meta.End <= meta.Start) return;
        float expected = (meta.End - meta.Start) / meta.Fps;
        if (Mathf.Abs(clip.Length - expected) > ClipLengthTolerance) clip.Length = expected;
    }

    internal static void RegisterAnimationMetadata(AnimationPlayer anim, string path)
    {
        if (AnimationMetaByPath.TryGetValue(path, out var shared))
        {
            ModelNodes.AnimationMetaByIndex[anim.GetInstanceId()] = shared.ByIndex;
            ModelNodes.AnimationMetaByName[anim.GetInstanceId()] = shared.ByName;
            return;
        }
        var map = new System.Collections.Generic.Dictionary<int, ModelNodes.AnimMeta>();
        var names = new System.Collections.Generic.Dictionary<string, ModelNodes.AnimMeta>(
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
                    var meta = new ModelNodes.AnimMeta
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
                    MatchClipLength(anim.GetAnimation(name), meta);
                }
        }
        ModelNodes.AnimationMetaByIndex[anim.GetInstanceId()] = map;
        ModelNodes.AnimationMetaByName[anim.GetInstanceId()] = names;
        AnimationMetaByPath[path] = (map, names);
    }

    internal static string? AnimationNameAt(AnimationPlayer anim, int sourceIndex)
    {
        if (ModelNodes.AnimationMetaByIndex.TryGetValue(anim.GetInstanceId(), out var map))
            return map.TryGetValue(sourceIndex, out var exact) ? exact.Name : null;
        var list = anim.GetAnimationList();
        return sourceIndex > 0 && sourceIndex < list.Length ? list[sourceIndex].ToString() : null;
    }

    internal static ModelNodes.AnimMeta? AnimationMetaAt(AnimationPlayer anim, int sourceIndex) =>
        ModelNodes.AnimationMetaByIndex.TryGetValue(anim.GetInstanceId(), out var map)
        && map.TryGetValue(sourceIndex, out var meta) ? meta : null;

    internal static ModelNodes.AnimMeta? AnimationMetaFor(AnimationPlayer anim, string name) =>
        ModelNodes.AnimationMetaByName.TryGetValue(anim.GetInstanceId(), out var map)
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

    internal static string[] StanceIdleClips(int[]? gear)
    {
        var (kind, leftKind, _) = WeaponMounts.HandLoadout(gear);
        if (WeaponAnimation.IsOneHandBlade(kind) && WeaponAnimation.IsOneHandBlade(leftKind))
            return ModelNodes.DualIdleClips;
        if (kind == WeaponAnimation.Crossbow) return ModelNodes.CrossbowIdleClips;
        if (leftKind is WeaponAnimation.Bow or WeaponAnimation.LongBow
            || kind is WeaponAnimation.Bow or WeaponAnimation.LongBow) return ModelNodes.BowIdleClips;
        return kind switch
        {
            WeaponAnimation.NoItem or 0 => ModelNodes.UnarmedIdleClips,
            11 => ModelNodes.DaggerIdleClips,
            22 => ModelNodes.TwoHandIdleClips,
            21 => ModelNodes.SwordIdleClips,
            31 or 32 => ModelNodes.AxeIdleClips,
            41 or 181 => ModelNodes.BluntIdleClips,
            42 => ModelNodes.TwoBluntIdleClips,
            51 => ModelNodes.SpearIdleClips,
            52 => ModelNodes.PolearmIdleClips,
            110 => ModelNodes.StaffIdleClips,
            140 => ModelNodes.JamadarIdleClips,
            _ => ModelNodes.IdleClips,
        };
    }

    private static string[] ClipsFor(string state) =>
        state == "run" ? ModelNodes.RunClips
        : state == "walk" ? ModelNodes.WalkClips
        : state == "walk_reverse" ? ModelNodes.WalkReverseClips
        : state == "sit" ? ModelNodes.SitClips
        : ModelNodes.IdleClips;

    internal static void PlayClip(EntityView e, string state)
    {
        if (state == "sit" && e.Hover != null)
        {
            PlayClipOn(e.Anim, ref e.Clip, WingRig.WingSitLocomotion, e.Hover.Loop);
            return;
        }
        bool stance = !e.IsNpc && state == "idle" && (e.CombatStance || FrameClock.Now() < e.CombatStanceUntil);
        PlayClipOn(
            e.Anim,
            ref e.Clip,
            state,
            stance ? StanceIdleClips(e.Gear) : null,
            stance ? StanceIdleSlot(e.Gear) : -1);
    }

    internal static void PlayClipOn(
        AnimationPlayer? anim, ref string? clip, string state, string[]? idleOverride = null,
        int idleSlot = -1)
    {
        if (anim == null || clip == state) return;
        string[] requested = idleOverride ?? ClipsFor(state);
        string? name = null;
        if (idleOverride != null)
        {
            if (idleSlot >= 0) name = AnimationNameAt(anim, idleSlot);
            name ??= Pick(anim, requested);
        }
        if (name == null && LocomotionSlot(state) is var slot && slot >= 0)
            name = AnimationNameAt(anim, slot);
        name ??= Pick(anim, requested) ?? AnimationNameAt(anim, 0) ?? Pick(anim, idleOverride ?? ModelNodes.IdleClips);
        if (name == null || !anim.HasAnimation(name)) return;
        if (anim.CurrentAnimation == name && anim.IsPlaying() && BlendHold.Remaining(anim) <= 0)
        {
            clip = state;
            return;
        }

        var res = anim.GetAnimation(name);
        if (res != null) res.LoopMode = Animation.LoopModeEnum.Linear;
        double blend = AnimationMetaFor(anim, name)?.Blend ?? ModelNodes.AnimBlend;
        BlendHold.Play(anim, name, blend, hold: false);
        CrowdAnimator.NotifyPlay(anim, name, blend, hold: false);
        clip = state;
    }

    internal static string? Pick(AnimationPlayer ap, string[] names)
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

    internal static int StanceIdleSlot(int[]? gear)
    {
        var (right, left, weight) = WeaponMounts.HandLoadout(gear);
        return WeaponAnimation.BreathBase(right, left, weight);
    }
}
