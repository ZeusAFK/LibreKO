using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const string WingNodePrefix = "wing_";
    private const float WingKurianScale = 1.1f;
    private const float WingRunSpeedScale = 1.35f;
    private const int WingDefaultBone = 2;
    private const int WingKurianBoneDefault = 44;
    private const int WingSlotCount = 4;

    private static readonly int[] WingVisualSlots =
    {
        InventoryConstants.VisCosWing, InventoryConstants.VisCosFairy,
        InventoryConstants.VisCosTalisman, InventoryConstants.VisCosEmblem,
    };

    private static readonly int[] WingSuppressedZones =
        { 37, 38, 39, 45, 57, 58, 59, 60, 76, 85, 86, 89 };

    private static class WingAnimSlot
    {
        public const int Run = 0;
        public const int Breath = 1;
        public const int Hit = 2;
        public const int Attack = 3;
        public const int Sit = 4;
        public const int Die = 5;
    }

    internal static class WingState
    {
        public const string Run = "run";
        public const string Breath = "breath";
        public const string Attack = "attack";
        public const string Sit = "sit";
        public const string Die = "die";
    }

    private const string WingSitLocomotion = "wing_sit";
    private const string WornWingNode = WingNodePrefix + "0";
    private const int WingSitStartAnim = 145;
    private const int WingSitLoopAnim = 146;
    private const int WingSitEndAnim = 147;
    private static readonly string[] WingSitStartNames = { "wing_S", "wing_s" };
    private static readonly string[] WingSitLoopNames = { "wing_B", "wing_Breath", "wing_breath" };
    private static readonly string[] WingSitEndNames = { "wing_E", "wing_e" };

    private sealed class HoverClips
    {
        public string[] Start = System.Array.Empty<string>();
        public string[] Loop = System.Array.Empty<string>();
        public string[] End = System.Array.Empty<string>();
    }

    private static HoverClips? ResolveHoverClips(Node3D? body, AnimationPlayer? anim)
    {
        if (body == null || anim == null || FindFirst<Skeleton3D>(body)?.GetNodeOrNull(WornWingNode) == null)
            return null;
        string? start = HoverClip(anim, WingSitStartAnim, WingSitStartNames);
        string? loop = HoverClip(anim, WingSitLoopAnim, WingSitLoopNames);
        string? end = HoverClip(anim, WingSitEndAnim, WingSitEndNames);
        if (start == null || loop == null || end == null) return null;
        return new HoverClips { Start = new[] { start }, Loop = new[] { loop }, End = new[] { end } };
    }

    private static string? HoverClip(AnimationPlayer anim, int animIndex, string[] names)
    {
        if (AnimationMetaAt(anim, animIndex)?.Name is { Length: > 0 } byIndex && anim.HasAnimation(byIndex))
            return byIndex;
        return Pick(anim, names);
    }

    private static bool HoverPosture(HoverClips? hover, bool sitting, int actionRank, double actionUntil) =>
        hover != null && (sitting || actionRank == ActionRankPosture && Now() < actionUntil);

    private readonly record struct WingPart(string Stem, int Bone, int Slot);

    private static Dictionary<int, WingPart>? _wingIndex;
    private static int _wingKurianBone = WingKurianBoneDefault;
    private AnimationPlayer?[] _selfWingAnims = new AnimationPlayer?[WingSlotCount];
    private HoverClips? _selfHover;
    private readonly string?[] _selfWingClips = new string?[WingSlotCount];

    private static bool IsKurianRace(int race) => PlayerRig.IsKurian(race);

    internal static AnimationPlayer?[] AttachWings(Node3D body, int[]? gear, int race, int zone,
                                                  bool enableShine = true, bool shineShadow = false)
    {
        var anims = new AnimationPlayer?[WingSlotCount];
        var skel = FindFirst<Skeleton3D>(body);
        if (skel == null) return anims;
        var worn = new Dictionary<string, BoneAttachment3D>();
        foreach (var old in skel.GetChildren())
            if (old is BoneAttachment3D ba
                && ba.Name.ToString().StartsWith(WingNodePrefix, System.StringComparison.Ordinal))
                worn[ba.Name.ToString()] = ba;

        if (gear == null || System.Array.IndexOf(WingSuppressedZones, zone) >= 0)
        {
            DropWings(skel, worn.Values);
            if (enableShine) ItemShineLight.Refresh(body, shineShadow);
            return anims;
        }

        _wingIndex ??= LoadWingIndex();
        bool kurian = IsKurianRace(race);
        foreach (int i in WingVisualSlots)
        {
            if (i >= gear.Length || gear[i] <= 0 || !TryWingPart(gear[i], out var part)) continue;
            if (part.Slot < 0 || part.Slot >= WingSlotCount || anims[part.Slot] != null) continue;

            string resPath = $"res://assets/wings/{part.Stem}.glb";
            if (!ResourceLoader.Exists(resPath)
                || ResourceLoader.Load(resPath) is not PackedScene scene) continue;
            int bone = kurian ? _wingKurianBone : part.Bone;
            if (bone < 0 || bone >= skel.GetBoneCount()) bone = part.Bone;
            if (bone < 0 || bone >= skel.GetBoneCount()) continue;

            string name = WingNodePrefix + part.Slot;
            string look = $"{part.Stem}/{bone}/{kurian}";
            if (worn.Remove(name, out var kept))
            {
                if (kept.GetMeta(WingLookMeta, "").AsString() == look && kept.GetChildOrNull<Node3D>(0) is { } keptInst)
                {
                    if (enableShine && part.Slot == 0)
                        ItemShine.Apply(keptInst, gear[InventoryConstants.VisBreast], 0);
                    anims[part.Slot] = FindFirst<AnimationPlayer>(keptInst);
                    continue;
                }
                DropWings(skel, new[] { kept });
            }

            var attach = new BoneAttachment3D { Name = name };
            attach.SetMeta(WingLookMeta, look);
            skel.AddChild(attach);
            attach.BoneIdx = bone;

            var inst = scene.Instantiate<Node3D>();
            if (kurian) inst.Scale = new Vector3(WingKurianScale, WingKurianScale, WingKurianScale);
            attach.AddChild(inst);
            ForceDoubleSided(inst);
            if (enableShine && part.Slot == 0)
                ItemShine.Apply(inst, gear[InventoryConstants.VisBreast], 0);
            AttachFxPlugs(inst, $"res://assets/wings/{part.Stem}.fxplug.json");

            var anim = FindFirst<AnimationPlayer>(inst);
            if (anim != null)
                RegisterAnimationMetadata(anim, $"res://assets/wings/{part.Stem}.anim.json");
            anims[part.Slot] = anim;
        }
        DropWings(skel, worn.Values);
        if (enableShine) ItemShineLight.Refresh(body, shineShadow);
        return anims;
    }

    private const string WingLookMeta = "wing_look";

    private static void DropWings(Skeleton3D skel, IEnumerable<BoneAttachment3D> wings)
    {
        foreach (var ba in wings)
        {
            skel.RemoveChild(ba);
            ba.QueueFree();
        }
    }

    private static bool TryWingPart(int itemId, out WingPart part) =>
        _wingIndex!.TryGetValue(itemId, out part) || _wingIndex.TryGetValue(ItemData.BaseId(itemId), out part);

    private static Dictionary<int, WingPart> LoadWingIndex()
    {
        var map = new Dictionary<int, WingPart>();
        using var file = Godot.FileAccess.Open("res://assets/wings/index.json",
                                               Godot.FileAccess.ModeFlags.Read);
        if (file == null) return map;
        var parsed = Json.ParseString(file.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return map;
        foreach (var kv in parsed.AsGodotDictionary())
        {
            string key = kv.Key.AsString();
            if (key == "_kurianBone")
            {
                _wingKurianBone = kv.Value.AsInt32();
                continue;
            }
            if (!int.TryParse(key, out int itemId)) continue;
            if (kv.Value.VariantType != Variant.Type.Dictionary) continue;
            var e = kv.Value.AsGodotDictionary();
            string stem = e.TryGetValue("model", out var mv) ? mv.AsString() : "";
            if (stem.Length == 0) continue;
            map[itemId] = new WingPart(
                stem,
                e.TryGetValue("bone", out var bv) ? bv.AsInt32() : WingDefaultBone,
                e.TryGetValue("slot", out var sv) ? sv.AsInt32() : 0);
        }
        return map;
    }

    private void WingTick()
    {
        foreach (var e in _ents.Values)
        {
            if (e.WingAnims == null) continue;
            if (e.AnimPaused) continue;
            string state = EntityWingState(e);
            bool active = !e.AnimThrottled;
            for (int s = 0; s < e.WingAnims.Length; s++)
            {
                if (e.WingAnims[s] is not { } anim) continue;
                if (anim.Active != active) anim.Active = active;
                PlayWingClip(anim, ref e.WingClips[s], state);
            }
        }

        string selfState = SelfWingState();
        for (int s = 0; s < _selfWingAnims.Length; s++)
            if (_selfWingAnims[s] is { } anim)
                PlayWingClip(anim, ref _selfWingClips[s], selfState);
    }

    private static string EntityWingState(Ent e)
    {
        if (e.Dead) return WingState.Die;
        if (HoverPosture(e.Hover, e.Sitting, e.ActionRank, e.ActionUntil)) return WingState.Sit;
        if (e.ActionClip != null || Now() < e.ActionUntil) return WingState.Attack;
        return e.Clip is "run" or "walk" or "walk_reverse" ? WingState.Run : WingState.Breath;
    }

    private string SelfWingState()
    {
        if (_selfDead) return WingState.Die;
        if (HoverPosture(_selfHover, _selfSitting, _selfActionRank, _selfActionUntil)) return WingState.Sit;
        if (Now() < _selfActionUntil) return WingState.Attack;
        return _selfClip is "run" or "walk" or "walk_reverse" ? WingState.Run : WingState.Breath;
    }

    internal static void PlayWingClip(AnimationPlayer anim, ref string? clip, string state)
    {
        if (clip == state) return;
        int slot = state switch
        {
            WingState.Run => WingAnimSlot.Run,
            WingState.Attack => WingAnimSlot.Attack,
            WingState.Sit => WingAnimSlot.Sit,
            WingState.Die => WingAnimSlot.Die,
            _ => WingAnimSlot.Breath,
        };
        string? name = AnimationNameAt(anim, slot) ?? AnimationNameAt(anim, WingAnimSlot.Breath);
        if (name == null || !anim.HasAnimation(name)) return;
        var res = anim.GetAnimation(name);
        if (res != null)
            res.LoopMode = slot == WingAnimSlot.Die
                ? Animation.LoopModeEnum.None
                : Animation.LoopModeEnum.Linear;
        anim.SpeedScale = slot == WingAnimSlot.Run ? WingRunSpeedScale : 1f;
        anim.Play(name, AnimationMetaFor(anim, name)?.Blend ?? AnimBlend);
        clip = state;
    }
}
