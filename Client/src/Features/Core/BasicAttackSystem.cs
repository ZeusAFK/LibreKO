using Godot;

namespace LibreKO.Features.Core;

internal sealed class BasicAttackSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly AudioSystem audio;
    private readonly SelfAvatar avatar;
    private readonly BuffSystem buff;
    private readonly EntityActionSystem entityAction;
    private readonly EntityLooksSystem entityLooks;
    private readonly EntityStatesSystem entityStates;
    private readonly ICombatRequests requests;
    private readonly SelfSession self;
    private readonly SkillCastSystem skillCast;
    private readonly SoccerSystem soccer;

    public BasicAttackSystem(IWorldContext context, AudioSystem audio, SelfAvatar avatar, BuffSystem buff, EntityActionSystem entityAction, EntityLooksSystem entityLooks, EntityStatesSystem entityStates, ICombatRequests requests, SelfSession self, SkillCastSystem skillCast, SoccerSystem soccer)
    {
        this.context = context;
        this.audio = audio;
        this.avatar = avatar;
        this.buff = buff;
        this.entityAction = entityAction;
        this.entityLooks = entityLooks;
        this.entityStates = entityStates;
        this.requests = requests;
        this.self = self;
        this.skillCast = skillCast;
        this.soccer = soccer;
    }

    public void Build()
    {
        buff.SelfStunned += CancelSwing;
    }

    public void Dispose()
    {
        buff.SelfStunned -= CancelSwing;
    }

    internal void Tick(double now)
    {
        SelfMoveAttackTick(now);
        if (comboBuffered && !context.Self.Dead && now >= comboPlayingUntil) ComboHit(now);
        TickSwingCommit(now);
        TickAutoAttack(now);
        context.Hud.TouchActions?.SetAutoAttack(CanAutoAttack(), AutoAttack);
    }

    private const float SwingCommitFallback = 0.3f;

    internal bool AutoAttack { get; private set; }
    internal int AutoTargetId { get; private set; } = -1;
    private double nextSwing;
    private int swingTarget = -1;
    private double swingCommitAt;
    private double nextSwingIfCancelled;

    private const double ComboLen = 1.0;
    private const int ComboSteps = 3;
    private const double ComboGrace = 1.0;
    private double comboStepLen = ComboLen;
    private int comboStep;
    private double comboPlayingUntil;
    private double comboExpireAt;
    private bool comboBuffered;

    private bool CanAutoAttack() =>
        context.Selection.SelectedId >= 0
        && context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var target)
    && target.Attackable
    && !target.Dead;

    internal void ToggleAutoAttack()
    {
        if (soccer.SelfOnSoccerTeam) return;
        if (AutoAttack)
        {
            StopAutoAttack();
            return;
        }
        if (!CanAutoAttack())
        {
            if (context.Selection.SelectedId >= 0 && context.Entities.Views.ContainsKey(context.Selection.SelectedId))
            {
                context.Selection.FaceSelectedWhenStill();
                return;
            }
            context.Log.Notice("Select a target first.");
            return;
        }

        context.Selection.FaceSelectedWhenStill();
        StartAutoAttack(context.Selection.SelectedId);
    }

    internal void StartAutoAttack(int targetId)
    {
        if (soccer.SelfOnSoccerTeam && soccer.OnSoccerPitch()) return;
        avatar.AttackChain.Record(AttackChain.BasicAttack, FrameClock.Now());
        audio.EnterBattleMusic();
        AutoAttack = true;
        AutoTargetId = targetId;
        context.Movement.CancelClickMove();
        context.Log.Add(GameText.System(GameText.BeginAttack, "Beginning attack on %s", context.Entities.NameOf(targetId)), CombatLogKind.Status);
    }

    internal void StopAutoAttack()
    {
        if (AutoAttack) context.Log.Add(GameText.System(GameText.StopAttack, "Stop Attack"), CombatLogKind.Status);
        AutoAttack = false;
        AutoTargetId = -1;
        CancelSwing();
    }

    private void ArmSwing(int targetId, double now)
    {
        swingTarget = targetId;
        if (targetId < 0) return;
        float windUp = ActionAnimation.FirstStrikeTime(avatar.Anim);
        swingCommitAt = now + (windUp > 0f ? windUp : SwingCommitFallback);
    }

    private void TickSwingCommit(double now)
    {
        if (swingTarget < 0 || now < swingCommitAt) return;
        int target = swingTarget;
        swingTarget = -1;
        if (context.Self.Dead || self.Stunned(FrameClock.Now())) return;
        if (!context.Entities.Views.TryGetValue(target, out var t) || t.Dead || !t.Attackable) return;
        requests.Attack(target, SwingDelay(self.Inventory), chained: avatar.AttackChain.Chained);
    }

    internal static short SwingDelay(Inventory inventory)
    {
        int hand = inventory.HandWeapon(rightFirst: true);
        int delay = hand == 0 ? 0 : ItemData.Get(hand)?.Delay ?? 0;
        return (short)Mathf.Max(BareHandedSwingDelay, delay);
    }

    private const int BareHandedSwingDelay = 100;

    private float MeleeSwingInterval()
    {
        var gear = entityLooks.SelfGear();
        int right = gear.Length > InventoryConstants.VisRightHand ? gear[InventoryConstants.VisRightHand] : 0;
        int delay = right == 0 ? 0 : ItemData.Get(right)?.Delay ?? 0;
        int percent = right == 0 ? 0 : ItemData.ExtFor(right)?.AttackIntervalPercent ?? 0;
        return SwingTiming.IntervalSeconds(delay, percent, buff.AttackSpeedMultiplier());
    }

    private void CancelSwing()
    {
        comboBuffered = false;
        if (avatar.ActionRank == ModelAnimation.ActionRankBasic && FrameClock.Now() < avatar.ActionUntil)
        {
            avatar.ActionUntil = 0;
            avatar.Clip = null;
            avatar.StrikeTarget = SelfAvatar.NoStrikeTarget;
        }
        bool uncommitted = CancelBasicRangedShot();
        if (swingTarget >= 0)
        {
            uncommitted = true;
            swingTarget = -1;
        }
        if (!uncommitted) return;
        nextSwing = nextSwingIfCancelled;
        comboPlayingUntil = 0;
        comboExpireAt = 0;
        comboStep = 0;
    }

    private bool CancelBasicRangedShot()
    {
        if (context.Self.Dead || skillCast.PendingCasts.Count == 0) return false;
        if (skillCast.BasicRangedAttackSkill() is not { } ranged) return false;
        if (!skillCast.PendingCastPreEffect(ranged.Id)) return false;
        skillCast.CancelSelfCast(ranged.Id);
        return true;
    }

    private void TickAutoAttack(double now)
    {
        if (!AutoAttack)
            return;
        if (context.Self.Dead
            || context.Selection.SelectedId != AutoTargetId
            || !context.Entities.Views.TryGetValue(AutoTargetId, out var target)
            || !target.Attackable
            || target.Dead)
        {
            StopAutoAttack();
            return;
        }

        if (!InBasicAttackRange(AutoTargetId) || now < nextSwing)
            return;

        var rangedSkill = skillCast.BasicRangedAttackSkill();
        nextSwingIfCancelled = nextSwing;
        nextSwing = now + (rangedSkill != null ? rangedSkill.CastSeconds + RangedSwingPad : MeleeSwingInterval());
        BasicAttack();
    }

    internal float BasicAttackRange(EntityView? target)
    {
        int ranged = skillCast.RangedWeaponItem();
        if (ranged != 0)
            return (ItemData.Get(ranged)?.Range ?? 0) / Coord.TenthsPerUnit + (target?.Radius ?? 0f);
        float selfRadius = context.Entities.Views.TryGetValue(context.Self.CharId, out var me) ? me.Radius : SelfAvatar.DefaultBodyRadius;
        return SkillReach.BasicMelee(selfRadius, target?.Radius ?? 0f, MeleeWeaponReach(), context.Zone);
    }

    private float MeleeWeaponReach() => entityLooks.HandWeaponReach(rightFirst: true);
    internal const float ApproachFraction = 0.8f;

    internal bool SwingDue(double now) => now >= nextSwing;
    private const float RangedSwingPad = 0.15f;

    private void AimAttack(double now)
    {
        if (context.Selection.SelectedId >= 0 && context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var e))
            entityAction.FaceSelfToward(e.Body.Position);
        else if (context.Self.Facing.LengthSquared() >= 0.01f)
            entityAction.SnapSelfToFaceDir();
    }

    private void BasicAttack()
    {
        if (context.Self.Dead) return;
        if (skillCast.BasicRangedAttackSkill() is { } ranged)
        {
            if (!skillCast.HasPendingCast(ranged.Id)) skillCast.CastSkill(ranged.Id);
            return;
        }
        double now = FrameClock.Now();
        if (now < comboPlayingUntil) { comboBuffered = true; return; }
        ComboHit(now);
    }

    private void ComboHit(double now)
    {
        if (now > comboExpireAt) comboStep = 0;
        comboStep = (comboStep + 1) % ComboSteps;
        comboStepLen = ComboLen;

        bool movingSwing = SelfMoveAttackReady();
        if (!movingSwing) AimAttack(now);

        int target =
            context.Selection.SelectedId >= 0
            && context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var t)
            && t.Attackable
            && !t.Dead
            && InBasicAttackRange(context.Selection.SelectedId)
                ? context.Selection.SelectedId
                : -1;
        avatar.StrikeTarget = target;
        float lastStrike;
        if (movingSwing && SelfMoveAttack(target, now, out float moveStrike))
            lastStrike = moveStrike;
        else
        {
            entityAction.SelfAction(ActionAnimation.BasicAttackClips, ModelAnimation.ActionRankBasic);
            ArmSwing(target, now);
            lastStrike = ActionAnimation.LastStrikeTime(avatar.Anim);
        }
        nextSwing = Mathf.Max((float)nextSwing, (float)now + lastStrike + StrikeTailPad);
        comboPlayingUntil = now + comboStepLen;
        comboExpireAt = comboPlayingUntil + ComboGrace;
        comboBuffered = false;
    }

    internal bool InRange(int id, float radius) =>
        context.Entities.Views.TryGetValue(id, out var e) && e.Body.GlobalPosition.DistanceTo(context.Self.Body.GlobalPosition) <= radius;

    internal bool InBasicAttackRange(int id) =>
        context.Entities.Views.TryGetValue(id, out var e)
    && Coord.FlatDistance(e.Body.GlobalPosition, context.Self.Body.GlobalPosition) <= BasicAttackRange(e);

    private const int AttackResultMiss = 0;
    private const int AttackResultHit = 1;
    private const int AttackResultKill = 2;

    private const int AttackTypeBasic = 1;

    internal void OnAttack(AttackReply reply)
    {
        if (reply.TargetId == context.Self.CharId && reply.AttackerId != context.Self.CharId) skillCast.BreakCastByChance();
        if (reply.AttackerId == context.Self.CharId || reply.TargetId == context.Self.CharId) SelfInBattle?.Invoke(FrameClock.Now());
        if (reply.AttackType == AttackTypeBasic && reply.Result == AttackResultHit
            && (reply.AttackerId == context.Self.CharId ? avatar.AttackChain.Consume() : reply.Chained)
            && Fx.NameForId(AttackChain.Effect(GD.Randi())) is { } chainFx)
            context.Entities.SpawnFxOn(reply.AttackerId, chainFx, 0f);
        if (reply.Result == AttackResultHit) PlayStruck(reply.TargetId);
        if (reply.Result == AttackResultMiss && reply.AttackerId == context.Self.CharId && reply.TargetId != context.Self.CharId)
            context.Log.Add(GameText.System(GameText.Missed, "%s Missed.", context.Entities.NameOf(reply.TargetId)), CombatLogKind.Outgoing);
        if (reply.Result == AttackResultKill && context.Entities.Views.TryGetValue(reply.TargetId, out var victim)) victim.KillStriker = reply.AttackerId;
        if (reply.AttackerId != context.Self.CharId && context.Entities.Views.TryGetValue(reply.AttackerId, out var a))
        {
            a.StrikeTarget = reply.Result is AttackResultHit or AttackResultKill ? reply.TargetId : -1;
            if (!TryMoveAttack(a)) entityAction.PlayEntityAction(a, ActionAnimation.BasicAttackClips, ModelAnimation.ActionRankBasic);
        }
        entityAction.FaceToward(reply.AttackerId, reply.TargetId);
    }

    internal event Action<double>? SelfInBattle;

    private const float StrikeTailPad = 0.08f;

    private const uint NpcFlinchOneIn = 10;

    private void PlayStruck(int entityId)
    {
        double now = FrameClock.Now();
        bool monsterLook = entityAction.WearsMonsterLook(entityId);
        if (entityId == context.Self.CharId)
        {
            if (avatar.Flinch != null && !monsterLook)
            {
                if (SelfFlinchReady(now)) ActionAnimation.PlayFlinch(avatar.Flinch, avatar.Anim);
                return;
            }
            if (monsterLook && GD.Randi() % NpcFlinchOneIn != 0) return;
            entityAction.PlaySkillAction(entityId, ActionAnimation.StruckAnim(monsterLook), ActionAnimation.StruckClips, ModelAnimation.ActionRankStruck);
            return;
        }

        if (!context.Entities.Views.TryGetValue(entityId, out var e)) return;
        if (e.Flinch != null && !monsterLook)
        {
            if (ActionAnimation.EntityFlinchReady(e, now)) ActionAnimation.PlayFlinch(e.Flinch, e.Anim);
            return;
        }
        bool monsterClips = e.IsNpc || monsterLook;
        if (monsterClips && GD.Randi() % NpcFlinchOneIn != 0) return;
        entityAction.PlaySkillAction(entityId, ActionAnimation.StruckAnim(monsterClips), ActionAnimation.StruckClips, ModelAnimation.ActionRankStruck);
    }

    private const int MoveAttackWeaponAnim = 14;
    private const int MoveAttackBareAnim = 15;

    private bool TryMoveAttack(EntityView a)
    {
        if (a.IsNpc || a.Flinch == null || a.Anim == null || !EntityRig.EntityMoving(a)) return false;
        int pose = WeaponMounts.HandLoadout(a.Gear).Right != WeaponAnimation.NoItem ? MoveAttackWeaponAnim : MoveAttackBareAnim;
        string? name = ModelAnimation.AnimationNameAt(a.Anim, pose);
        if (name == null || !a.Anim.HasAnimation(name) || a.Anim.GetAnimation(name) is not { } clip) return false;
        var meta = ModelAnimation.AnimationMetaAt(a.Anim, pose);
        a.Flinch.PlayThrough(pose, clip, meta?.Blend ?? ActionClip.DefaultBlend);
        double now = FrameClock.Now();
        float swing = meta?.SoundTime(0) ?? -1f;
        float strike = meta?.StrikeTime(0) ?? -1f;
        a.MoveSwingAt = swing >= 0f ? now + swing : -1;
        a.MoveStrikeAt = strike >= 0f ? now + strike : -1;
        return true;
    }

    private double selfMoveSwingAt = -1;
    private double selfMoveStrikeAt = -1;

    private bool SelfMoveAttackReady() =>
        context.Self.Moving && avatar.Flinch != null && avatar.Anim != null && !entityStates.IsWearingTransformLook(context.Self.CharId);

    private bool SelfMoveAttack(int target, double now, out float lastStrike)
    {
        lastStrike = 0f;
        int pose = WeaponMounts.HandLoadout(entityLooks.SelfGear()).Right != WeaponAnimation.NoItem ? MoveAttackWeaponAnim : MoveAttackBareAnim;
        string? name = ModelAnimation.AnimationNameAt(avatar.Anim!, pose);
        if (name == null || !avatar.Anim!.HasAnimation(name) || avatar.Anim.GetAnimation(name) is not { } clip) return false;
        var meta = ModelAnimation.AnimationMetaAt(avatar.Anim, pose);
        avatar.Flinch!.PlayThrough(pose, clip, meta?.Blend ?? ActionClip.DefaultBlend);
        float swing = meta?.SoundTime(0) ?? -1f;
        float strike = meta?.StrikeTime(0) ?? -1f;
        selfMoveSwingAt = swing >= 0f ? now + swing : -1;
        selfMoveStrikeAt = strike >= 0f ? now + strike : -1;
        avatar.StrikeAnim = pose;
        swingTarget = target;
        if (target >= 0) swingCommitAt = now + (strike > 0f ? strike : SwingCommitFallback);
        lastStrike = strike > 0f ? strike : (float)clip.Length;
        return true;
    }

    private void SelfMoveAttackTick(double now)
    {
        if (selfMoveSwingAt >= 0 && now >= selfMoveSwingAt)
        {
            selfMoveSwingAt = -1;
            audio.SelfSwing();
        }
        if (selfMoveStrikeAt >= 0 && now >= selfMoveStrikeAt)
        {
            selfMoveStrikeAt = -1;
            audio.SelfStrike();
        }
    }

    private bool SelfFlinchReady(double now) =>
        !context.Self.Dead && !avatar.Sitting && !skillCast.SelfCasting(now)
    && !(avatar.ActionRank is ModelAnimation.ActionRankPosture or ModelAnimation.ActionRankDeath && now < avatar.ActionUntil);
}
