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
        public const string Die = "die";
    }

    private readonly record struct WingPart(string Stem, int Bone, int Slot);

    private static Dictionary<int, WingPart>? _wingIndex;
    private static int _wingKurianBone = WingKurianBoneDefault;
    private AnimationPlayer?[] _selfWingAnims = new AnimationPlayer?[WingSlotCount];
    private readonly string?[] _selfWingClips = new string?[WingSlotCount];

    private static bool IsKurianRace(int race) => race == 6 || race == 14;

    internal static AnimationPlayer?[] AttachWings(Node3D body, int[]? gear, int race, int zone,
                                                  bool enableShine = true)
    {
        var anims = new AnimationPlayer?[WingSlotCount];
        var skel = FindFirst<Skeleton3D>(body);
        if (skel == null) return anims;
        foreach (var old in skel.GetChildren())
            if (old is BoneAttachment3D ba
                && ba.Name.ToString().StartsWith(WingNodePrefix, System.StringComparison.Ordinal))
            {
                skel.RemoveChild(ba);
                ba.QueueFree();
            }

        if (gear == null || gear.Length <= InventoryConstants.VisCosWing
            || System.Array.IndexOf(WingSuppressedZones, zone) >= 0)
        {
            if (enableShine) ItemShineLight.Refresh(body);
            return anims;
        }

        _wingIndex ??= LoadWingIndex();
        bool kurian = IsKurianRace(race);
        for (int i = InventoryConstants.VisCosWing;
             i <= InventoryConstants.VisCosEmblem && i < gear.Length; i++)
        {
            if (gear[i] <= 0 || !_wingIndex.TryGetValue(gear[i], out var part)) continue;
            if (part.Slot < 0 || part.Slot >= WingSlotCount || anims[part.Slot] != null) continue;

            string resPath = $"res://assets/wings/{part.Stem}.glb";
            if (!ResourceLoader.Exists(resPath)
                || ResourceLoader.Load(resPath) is not PackedScene scene) continue;
            int bone = kurian ? _wingKurianBone : part.Bone;
            if (bone < 0 || bone >= skel.GetBoneCount()) bone = part.Bone;
            if (bone < 0 || bone >= skel.GetBoneCount()) continue;

            var attach = new BoneAttachment3D { Name = WingNodePrefix + part.Slot };
            skel.AddChild(attach);
            attach.BoneIdx = bone;

            var inst = scene.Instantiate<Node3D>();
            if (kurian) inst.Scale = new Vector3(WingKurianScale, WingKurianScale, WingKurianScale);
            attach.AddChild(inst);
            ForceDoubleSided(inst);
            if (enableShine && part.Slot == 0)
                ItemShine.Apply(inst, gear[InventoryConstants.VisBreast], 0);

            var anim = FindFirst<AnimationPlayer>(inst);
            if (anim != null)
                RegisterAnimationMetadata(anim, $"res://assets/wings/{part.Stem}.anim.json");
            anims[part.Slot] = anim;
        }
        if (enableShine) ItemShineLight.Refresh(body);
        return anims;
    }

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
            string state = EntityWingState(e);
            bool active = !e.AnimPaused;
            for (int s = 0; s < e.WingAnims.Length; s++)
            {
                if (e.WingAnims[s] is not { } anim) continue;
                if (anim.Active != active) anim.Active = active;
                if (active) PlayWingClip(anim, ref e.WingClips[s], state);
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
        if (e.ActionClip != null || Now() < e.ActionUntil) return WingState.Attack;
        return e.Clip is "run" or "walk" or "walk_reverse" ? WingState.Run : WingState.Breath;
    }

    private string SelfWingState()
    {
        if (_selfDead) return WingState.Die;
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
