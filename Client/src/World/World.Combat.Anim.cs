using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private static int AttackSlot(int[]? gear)
    {
        var (right, left, weight) = HandLoadout(gear);
        return WeaponAnimation.AttackAnim(right, left, weight, n => (int)(GD.Randi() % (uint)n));
    }

    private static int StanceIdleSlot(int[]? gear)
    {
        var (right, left, weight) = HandLoadout(gear);
        return WeaponAnimation.BreathBase(right, left, weight);
    }

    private static string[] AttackClips(int[]? gear)
    {
        var (kind, leftKind, _) = HandLoadout(gear);
        if (WeaponAnimation.IsOneHandBlade(kind) && WeaponAnimation.IsOneHandBlade(leftKind))
            return DualAttackClips;
        if (kind == WeaponAnimation.Crossbow) return CrossbowAttackClips;
        if (leftKind is WeaponAnimation.Bow or WeaponAnimation.LongBow
            || kind is WeaponAnimation.Bow or WeaponAnimation.LongBow) return BowAttackClips;
        return kind switch
        {
            WeaponAnimation.NoItem or 0 => UnarmedAttackClips,
            11 => DaggerAttackClips,
            21 => BasicAttackClips,
            22 => TwoHandAttackClips,
            31 or 32 => AxeAttackClips,
            41 or 181 => BluntAttackClips,
            42 => TwoBluntAttackClips,
            51 => SpearAttackClips,
            52 => PolearmAttackClips,
            110 => StaffAttackClips,
            140 => JamadarAttackClips,
            _ => BasicAttackClips,
        };
    }

    private static string[] ClipsForCast(SkillData.Skill? s) => s == null ? MeleeSkillClips : s.Type1 switch
    {
        1 or 2 => MeleeSkillClips,
        3 => MagicCastClips,
        _ => BuffClips,
    };

    private int SkillAnim(int entityId, SkillData.Skill s, bool release)
    {
        int[]? gear = GearOf(entityId);
        return SkillAnimation.Resolve(s, HeldItemClass(gear, 6), HeldItemClass(gear, 7), release);
    }

    private const float StrikeTailPad = 0.08f;

    private static float LastStrikeTime(AnimationPlayer? anim)
    {
        if (anim == null) return 0f;
        string name = anim.CurrentAnimation.ToString();
        if (name.Length == 0 || AnimationMetaFor(anim, name) is not { } meta) return 0f;
        float last = 0f;
        for (int slot = 0; slot < 2; slot++)
            last = Mathf.Max(last, meta.StrikeTime(slot));
        return last;
    }

    private static float FirstStrikeTime(AnimationPlayer? anim)
    {
        if (anim == null) return 0f;
        string name = anim.CurrentAnimation.ToString();
        if (name.Length == 0 || AnimationMetaFor(anim, name) is not { } meta) return 0f;
        float first = 0f;
        for (int slot = 0; slot < 2; slot++)
        {
            float at = meta.StrikeTime(slot);
            if (at > 0f && (first <= 0f || at < first)) first = at;
        }
        return first;
    }

    private static double ActionLength(Animation? clip, double blend) =>
        clip == null ? ActionClip.MissingClip : ActionClip.Hold(clip.Length, blend);

    private static double PlayActionOn(AnimationPlayer? anim, string[] candidates)
    {
        if (anim == null) return 0;
        string? name = Pick(anim, candidates);
        if (name == null) return 0;
        return StartAction(anim, name, AnimationMetaFor(anim, name)?.Blend ?? ActionClip.DefaultBlend);
    }

    internal static double StartAction(AnimationPlayer anim, string name, double blend)
    {
        var res = anim.GetAnimation(name);
        if (res != null)
        {
            res.LoopMode = Animation.LoopModeEnum.None;
            if (ActionClip.IsStaticPose(res.Length)) res.Length = (float)ActionClip.Hold(res.Length, blend);
        }
        anim.Play(name, blend);
        return ActionLength(res, blend);
    }

    private const int ActionRankStruck = 0;
    private const int ActionRankBasic = 1;
    private const int ActionRankPosture = 2;
    private const int ActionRankSkill = 3;
    private const int ActionRankDeath = 4;

    private static bool CanPlayAction(int rank, int current, double until, double now)
    {
        if (now >= until) return true;
        if (current == ActionRankDeath) return false;
        return rank >= ActionRankBasic;
    }

    private bool CanPlaySelfAction(int rank, double now) =>
        CanPlayAction(rank, _selfActionRank, _selfActionUntil, now);

    private static bool CanPlayEntityAction(Ent e, int rank, double now) =>
        CanPlayAction(rank, e.ActionRank, e.ActionUntil, now);

    private const int NoActionAnim = int.MinValue;

    private void BeginSelfAction(double len, int rank, double now, int anim = NoActionAnim)
    {
        _selfActionUntil = now + Mathf.Min((float)len, ActionClipCap);
        _selfActionRank = rank;
        _selfActionAnim = anim;
        _selfClip = null;
    }

    private static void BeginEntityAction(Ent e, double len, int rank, double now, int anim = NoActionAnim)
    {
        e.ActionUntil = now + Mathf.Min((float)len, ActionClipCap);
        e.ActionRank = rank;
        e.ActionAnim = anim;
        e.ActionClip = "action";
        e.Clip = null;
    }

    private void PlaySkillAction(int entityId, int action, string[] fallback, int rank)
    {
        if (action < 0) return;
        double now = Now();
        if (entityId == _myId)
        {
            if (_selfDead || _selfAnim == null) return;
            if (now < _selfActionUntil && action == _selfActionAnim) return;
            if (!CanPlaySelfAction(rank, now)) return;
            double len = PlayActionOn(_selfAnim, action, fallback);
            if (len > 0) BeginSelfAction(len, rank, now, action);
        }
        else if (_ents.TryGetValue(entityId, out var e) && e.Anim != null && !e.Dead)
        {
            if (now < e.ActionUntil && action == e.ActionAnim) return;
            if (!CanPlayEntityAction(e, rank, now)) return;
            double len = PlayActionOn(e.Anim, action, fallback);
            if (len > 0) BeginEntityAction(e, len, rank, now, action);
        }
    }

    private void SelfAction(string[] candidates, int rank)
    {
        if (_selfDead) return;
        if (_selfAnim == null) return;
        double now = Now();
        if (!CanPlaySelfAction(rank, now)) return;
        bool basicAttack = candidates == BasicAttackClips;
        double len;
        if (basicAttack)
        {
            var gear = SelfGear();
            len = PlayActionOn(_selfAnim, AttackSlot(gear), AttackVariant(AttackClips(gear)));
        }
        else len = PlayActionOn(_selfAnim, candidates);
        if (len > 0) BeginSelfAction(len, rank, now);
    }

    private void PlayEntityAction(Ent e, string[] candidates, int rank)
    {
        if (e.Anim == null || e.Dead) return;
        double now = Now();
        if (!CanPlayEntityAction(e, rank, now)) return;
        bool playerSwing = candidates == BasicAttackClips && !e.IsNpc;
        double len = playerSwing
            ? PlayActionOn(e.Anim, AttackSlot(e.Gear), AttackVariant(AttackClips(e.Gear)))
            : PlayActionOn(e.Anim, candidates == BasicAttackClips ? AttackVariant(candidates) : candidates);
        if (len <= 0) return;
        BeginEntityAction(e, len, rank, now);
        if (candidates == BasicAttackClips && !e.IsNpc)
            e.CombatStanceUntil = e.ActionUntil + 3.0;
    }

    private static int StruckAnim(bool npc) =>
        (npc ? SkillAnimation.NpcStruck0 : SkillAnimation.Struck0)
        + (int)(GD.Randi() % SkillAnimation.StruckPoses);

    private const uint NpcFlinchOneIn = 10;

    private void PlayStruck(int entityId)
    {
        double now = Now();
        if (entityId == _myId)
        {
            if (_selfFlinch != null)
            {
                if (SelfFlinchReady(now)) PlayFlinch(_selfFlinch, _selfAnim);
                return;
            }
            PlaySkillAction(entityId, StruckAnim(npc: false), StruckClips, ActionRankStruck);
            return;
        }

        if (!_ents.TryGetValue(entityId, out var e)) return;
        if (e.Flinch != null)
        {
            if (EntityFlinchReady(e, now)) PlayFlinch(e.Flinch, e.Anim);
            return;
        }
        if (e.IsNpc && GD.Randi() % NpcFlinchOneIn != 0) return;
        PlaySkillAction(entityId, StruckAnim(e.IsNpc), StruckClips, ActionRankStruck);
    }

    private static void PlayFlinch(Flinch layer, AnimationPlayer? anim)
    {
        if (anim == null) return;
        int pose = StruckAnim(npc: false);
        string? name = AnimationNameAt(anim, pose);
        if (name == null || !anim.HasAnimation(name)) return;
        if (anim.GetAnimation(name) is not { } clip) return;
        layer.Play(pose, clip, AnimationMetaAt(anim, pose)?.Blend ?? ActionClip.DefaultBlend);
    }

    private bool SelfFlinchReady(double now) =>
        !_selfDead && !_selfSitting && !SelfCasting(now)
        && !(_selfActionRank is ActionRankPosture or ActionRankDeath && now < _selfActionUntil);

    private static bool EntityFlinchReady(Ent e, double now) =>
        !e.Dead && !e.Sitting && !e.AnimPaused
        && !(e.ActionRank is ActionRankSkill or ActionRankPosture or ActionRankDeath && now < e.ActionUntil);

    private bool _selfCombatStance;

    private bool SelfCombatStanceReady() =>
        !_selfDead
        && _selectedId >= 0
        && _ents.TryGetValue(_selectedId, out var target)
        && target.Attackable
        && !target.Dead;

    private void CombatStanceTick()
    {
        bool ready = SelfCombatStanceReady();
        if (ready == _selfCombatStance) return;
        _selfCombatStance = ready;
        Net.I.SendCombatStance(ready);
    }

    private static string[] AttackVariant(string[] clips)
    {
        if (clips.Length < 2 || (GD.Randi() & 1) == 0) return clips;
        var varied = (string[])clips.Clone();
        (varied[0], varied[1]) = (varied[1], varied[0]);
        return varied;
    }

    private void FaceToward(int attackerId, int targetId)
    {
        Vector3? tgt = WorldPosOf(targetId);
        if (tgt == null) return;
        if (attackerId == _myId && _self != null) FaceSelfToward(tgt.Value);
        else if (_ents.TryGetValue(attackerId, out var a)) FaceNodeToward(a.Body, a.Body.Position, tgt.Value);
    }

    private void FaceTowardImpact(int casterId, short[] data)
    {
        if (data.Length <= 2 || (data[0] == 0 && data[2] == 0)) return;
        Vector3 impact = GroundPos(data[0], data[2], 0f, 0f);
        if (casterId == _myId && _self != null) FaceSelfToward(impact);
        else if (_ents.TryGetValue(casterId, out var a)) FaceNodeToward(a.Body, a.Body.Position, impact);
    }

    private void FaceSelfToward(Vector3 target)
    {
        var d = target - _self.Position;
        d.Y = 0;
        if (d.LengthSquared() < 0.01f) return;
        _faceDir = d.Normalized();
        _self.RotationDegrees = new Vector3(0, 180f - Coord.KoHeading(-_faceDir.X, _faceDir.Z), 0);
    }

    private void FaceNodeToward(Node3D node, Vector3 from, Vector3 to)
    {
        var (fx, fz) = WorldToKo(from);
        var (tx, tz) = WorldToKo(to);
        float dx = tx - fx, dz = tz - fz;
        if (dx * dx + dz * dz < 0.01f) return;
        node.RotationDegrees = new Vector3(0, 180f - Coord.KoHeading(dx, dz), 0);
    }

    private Vector3? WorldPosOf(int id) =>
        id == _myId ? _self?.Position :
        _ents.TryGetValue(id, out var e) ? e.Body.Position : null;
}
