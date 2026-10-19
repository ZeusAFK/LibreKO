using Godot;

namespace LibreKO.Presentation;

internal static class ActionAnimation
{
    internal const float ActionClipCap = 2.5f;
    internal const float ProjectileFxSpeed = 18f;

    private const short ArrowMissedMarker = MagicFail.ProjectileKilled;
    private const int MissMarkerSlot = 3;

    internal static short[] ArrowMissedData()
    {
        var data = new short[MagicData.Length];
        data[MissMarkerSlot] = ArrowMissedMarker;
        return data;
    }

    internal static int BuffSeconds(SkillData.Skill s, short[] data) =>
        s.Type1 is MagicType.Buff or MagicType.Transform or MagicType.Stealth
        && data.Length > 3 && data[3] > 0
            ? data[3]
            : s.Duration;

    internal static readonly string[] StruckClips = { "struck0", "struck", "struck1", "struck2" };

    internal static readonly string[] DeathClips = { "dead2", "dead0", "dead3", "dead1", "dead4", "dead" };

    internal static readonly string[] BasicAttackClips =
        { "attack_sword0_A", "attack_sword1_A", "attack0", "attack1", "attack",
          "attack_blunt0_A", "attack_Axe0_A", "attack_dual0_A", "attack_TwoHand0_A", "skill_sword01_A" };

    private static readonly string[] UnarmedAttackClips =
        { "attack_Punch0_A", "attack_Kick0_A", "attack_Punch0_B", "attack_Kick0_B", "attack0", "attack" };

    private static readonly string[] DaggerAttackClips =
        { "attack_Dagger0_A", "attack_Dagger1_A", "attack0", "attack" };

    private static readonly string[] DualAttackClips =
        { "attack_dual0_A", "attack_dual1_A", "attack0", "attack" };

    private static readonly string[] TwoHandAttackClips =
        { "attack_TwoHand0_A", "attack_TwoHand1_A", "attack0", "attack" };

    private static readonly string[] BluntAttackClips =
        { "attack_blunt0_A", "attack_blunt1_A", "attack0", "attack" };

    private static readonly string[] TwoBluntAttackClips =
        { "attack_Twoblunt0_A", "attack_Twoblunt1_A", "attack0", "attack" };

    private static readonly string[] AxeAttackClips =
        { "attack_Axe0_A", "attack_Axe1_A", "attack0", "attack" };

    private static readonly string[] SpearAttackClips =
        { "attack_Spear0_A", "attack0", "attack" };

    private static readonly string[] PolearmAttackClips =
        { "attack_Polearm0_A", "attack0", "attack" };

    private static readonly string[] StaffAttackClips =
        { "attack_Bash0_A", "magic_attack", "attack0", "attack" };

    private static readonly string[] JamadarAttackClips =
        { "attack_jamadar_A", "attack_Kick0_A", "attack0", "attack" };

    private static readonly string[] BowAttackClips =
        { "Shoot_Arrow_A", "Shoot_Arrow_B",
          "shoot_arrow_a", "shoot_arrow_b", "attack_Bow0_A", "attack_bow0_A", "attack0", "attack" };

    private static readonly string[] CrossbowAttackClips =
        { "Shoot_Quarrel_A", "Shoot_Quarrel_B", "attack_Bow0_A", "Shoot_Arrow_A", "attack0", "attack" };

    private static readonly string[] MeleeSkillClips =
        { "skill_sword01_A", "skill_sword02_A", "attack_sword0_A", "attack_sword1_A", "attack0", "attack",
          "attack_TwoHand0_A", "attack_blunt0_A", "attack_Axe0_A", "attack_dual0_A" };

    private static readonly string[] BuffClips =
        { "warcry0", "warcry1", "magic_ability", "magic_abil", "magic0", "greeting1", "breath", "basic" };

    private static readonly string[] MagicCastClips =
        { "magic_attack", "magic0", "magic1", "magic_heal", "breath", "basic" };

    internal static double PlayActionOn(AnimationPlayer? anim, int action, string[] fallback)
    {
        if (anim == null) return 0;
        string? indexed = ModelAnimation.AnimationNameAt(anim, action);
        if (indexed != null && anim.HasAnimation(indexed))
            return StartAction(anim, indexed, ModelAnimation.AnimationMetaAt(anim, action)?.Blend ?? ActionClip.DefaultBlend);
        return PlayActionOn(anim, fallback);
    }

    internal static int AttackSlot(int[]? gear)
    {
        var (right, left, weight) = WeaponMounts.HandLoadout(gear);
        return WeaponAnimation.AttackAnim(right, left, weight, n => (int)(GD.Randi() % (uint)n));
    }

    internal static string[] AttackClips(int[]? gear)
    {
        var (kind, leftKind, _) = WeaponMounts.HandLoadout(gear);
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

    internal static string[] ClipsForCast(SkillData.Skill? s) => s == null ? MeleeSkillClips : s.Type1 switch
    {
        1 or 2 => MeleeSkillClips,
        3 => MagicCastClips,
        _ => BuffClips,
    };

    internal static float LastStrikeTime(AnimationPlayer? anim)
    {
        if (anim == null) return 0f;
        string name = anim.CurrentAnimation.ToString();
        if (name.Length == 0 || ModelAnimation.AnimationMetaFor(anim, name) is not { } meta) return 0f;
        float last = 0f;
        for (int slot = 0; slot < 2; slot++)
            last = Mathf.Max(last, meta.StrikeTime(slot));
        return last + (float)BlendHold.Remaining(anim);
    }

    internal static float FirstStrikeTime(AnimationPlayer? anim)
    {
        if (anim == null) return 0f;
        string name = anim.CurrentAnimation.ToString();
        if (name.Length == 0 || ModelAnimation.AnimationMetaFor(anim, name) is not { } meta) return 0f;
        float first = 0f;
        for (int slot = 0; slot < 2; slot++)
        {
            float at = meta.StrikeTime(slot);
            if (at > 0f && (first <= 0f || at < first)) first = at;
        }
        return first > 0f ? first + (float)BlendHold.Remaining(anim) : first;
    }

    private static double ActionLength(Animation? clip, double blend) =>
        clip == null ? ActionClip.MissingClip : ActionClip.Duration(clip.Length, blend);

    internal static double PlayActionOn(AnimationPlayer? anim, string[] candidates)
    {
        if (anim == null) return 0;
        string? name = ModelAnimation.Pick(anim, candidates);
        if (name == null) return 0;
        return StartAction(anim, name, ModelAnimation.AnimationMetaFor(anim, name)?.Blend ?? ActionClip.DefaultBlend);
    }

    internal static double StartAction(AnimationPlayer anim, string name, double blend)
    {
        var res = anim.GetAnimation(name);
        if (res != null)
        {
            res.LoopMode = Animation.LoopModeEnum.None;
            if (ActionClip.IsStaticPose(res.Length)) res.Length = (float)ActionClip.Hold(res.Length, blend);
        }
        BlendHold.Play(anim, name, blend);
        CrowdAnimator.NotifyPlay(anim, name, blend);
        return ActionLength(res, blend);
    }

    internal static bool CanPlayAction(int rank, int current, double until, double now)
    {
        if (now >= until) return true;
        if (current == ModelAnimation.ActionRankDeath) return false;
        return rank >= ModelAnimation.ActionRankBasic;
    }

    internal static bool CanPlayEntityAction(EntityView e, int rank, double now) =>
        CanPlayAction(rank, e.ActionRank, e.ActionUntil, now);

    internal const int NoActionAnim = EntityView.NoActionAnim;

    internal static void BeginEntityAction(EntityView e, double len, int rank, double now, int anim = NoActionAnim)
    {
        ResetEntityActionScale(e);
        e.ActionUntil = now + Mathf.Min((float)len, ActionClipCap);
        e.ActionRank = rank;
        e.ActionAnim = anim;
        e.ActionClip = "action";
        e.Clip = null;
    }

    internal static void ResetEntityActionScale(EntityView e)
    {
        if (e.ActionTimeScale == 1f) return;
        e.ActionTimeScale = 1f;
        if (e.Anim != null && e.Anim.SpeedScale != 1f) e.Anim.SpeedScale = 1f;
    }

    internal static int StruckAnim(bool npc) =>
        (npc ? SkillAnimation.NpcStruck0 : SkillAnimation.Struck0)
        + (int)(GD.Randi() % SkillAnimation.StruckPoses);

    internal static void PlayFlinch(Flinch layer, AnimationPlayer? anim)
    {
        if (anim == null) return;
        int pose = StruckAnim(npc: false);
        string? name = ModelAnimation.AnimationNameAt(anim, pose);
        if (name == null || !anim.HasAnimation(name)) return;
        if (anim.GetAnimation(name) is not { } clip) return;
        layer.Play(pose, clip, ModelAnimation.AnimationMetaAt(anim, pose)?.Blend ?? ActionClip.DefaultBlend);
    }

    internal static bool EntityFlinchReady(EntityView e, double now) =>
        !e.Dead && !e.Sitting && !e.AnimPaused
        && !(e.ActionRank is ModelAnimation.ActionRankSkill or ModelAnimation.ActionRankPosture or ModelAnimation.ActionRankDeath && now < e.ActionUntil);

    internal static string[] AttackVariant(string[] clips)
    {
        if (clips.Length < 2 || (GD.Randi() & 1) == 0) return clips;
        var varied = (string[])clips.Clone();
        (varied[0], varied[1]) = (varied[1], varied[0]);
        return varied;
    }

    internal static float SelfYawOf(Vector3 dir) => 180f - Coord.MapHeading(-dir.X, dir.Z);
}
