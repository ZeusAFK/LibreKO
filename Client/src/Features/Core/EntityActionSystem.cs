using Godot;

namespace LibreKO.Features.Core;

internal sealed class EntityActionSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly GenieSession genie;
    private readonly SelfAvatar avatar;
    private readonly EntityLooksSystem entityLooks;
    private readonly ScenerySystem scenery;

    public EntityActionSystem(IWorldContext context, GenieSession genie, SelfAvatar avatar, EntityLooksSystem entityLooks, ScenerySystem scenery)
    {
        this.context = context;
        this.genie = genie;
        this.avatar = avatar;
        this.entityLooks = entityLooks;
        this.scenery = scenery;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    private IGenieCombo? genieCombo;

    internal void AttachGenieCombo(IGenieCombo genieCombo) => this.genieCombo = genieCombo;

    internal void DetachGenieCombo() => genieCombo = null;

    private ILootBoxes? lootBoxes;

    internal void AttachLootBoxes(ILootBoxes lootBoxes) => this.lootBoxes = lootBoxes;

    internal void DetachLootBoxes() => lootBoxes = null;

    internal void ReleaseCorpse(EntityView e) => e.CorpseRemoveAt = context.Now + CorpseLinger;

    internal event Action<int>? DeathCry;

    internal void LatchStrikeTarget(int actorId, int targetId)
    {
        if (actorId == context.Self.CharId) avatar.StrikeTarget = targetId;
        else if (context.Entities.Views.TryGetValue(actorId, out var a)) a.StrikeTarget = targetId;
    }

    private int[]? GearOf(int entityId) =>
        entityId == context.Self.CharId ? entityLooks.SelfGear() : context.Entities.Views.TryGetValue(entityId, out var e) ? e.Gear : null;

    private const int TransformedSkillAnim = 4;
    private const double CombatStanceLingerSeconds = 3.0;
    private const float DeathActionMinSeconds = 3f;

    internal int SkillAnim(int entityId, SkillData.Skill s, bool release)
    {
        int[]? gear = GearOf(entityId);
        int anim = SkillAnimation.Resolve(s, SkillAnimation.HeldItemClass(gear, InventoryConstants.VisRightHand), SkillAnimation.HeldItemClass(gear, InventoryConstants.VisLeftHand), release);
        return anim != SkillAnimation.None && WearsMonsterLook(entityId) ? TransformedSkillAnim : anim;
    }

    internal bool WearsMonsterLook(int entityId) =>
        context.Entities.BodyOf(entityId)?.GetNodeOrNull<Node3D>(StateLooks.TransformNodeName) is { } worn
        && !worn.GetMeta(WornLook.WornAccessoriesMeta, false).AsBool();

    private bool CanPlaySelfAction(int rank, double now) =>
        ActionAnimation.CanPlayAction(rank, avatar.ActionRank, avatar.ActionUntil, now);

    internal void BeginSelfAction(double len, int rank, double now, int anim = ActionAnimation.NoActionAnim)
    {
        avatar.ActionUntil = now + Mathf.Min((float)len, ActionAnimation.ActionClipCap);
        avatar.ActionRank = rank;
        avatar.ActionAnim = anim;
        avatar.Clip = null;
    }

    internal void PlaySkillAction(int entityId, int action, string[] fallback, int rank, bool restart = false)
    {
        if (action < 0) return;
        double now = FrameClock.Now();
        if (entityId == context.Self.CharId)
        {
            if (context.Self.Dead || avatar.Anim == null) return;
            if (now < avatar.ActionUntil && action == avatar.ActionAnim) return;
            if (!CanPlaySelfAction(rank, now)) return;
            double len = ActionAnimation.PlayActionOn(avatar.Anim, action, fallback);
            if (len > 0 && rank == ModelAnimation.ActionRankSkill && genie.GenieRunning && genieCombo?.ComboOn == true) len *= GenieClipScale();
            if (len > 0)
            {
                BeginSelfAction(len, rank, now, action);
                avatar.StrikeAnim = action;
            }
        }
        else if (context.Entities.Views.TryGetValue(entityId, out var e) && e.Anim != null && !e.Dead)
        {
            if (!restart && now < e.ActionUntil && action == e.ActionAnim) return;
            if (!ActionAnimation.CanPlayEntityAction(e, rank, now)) return;
            double len = ActionAnimation.PlayActionOn(e.Anim, action, fallback);
            if (len > 0)
            {
                ActionAnimation.BeginEntityAction(e, len, rank, now, action);
                e.StrikeAnim = action;
            }
        }
    }

    private double GenieClipScale()
    {
        var gear = entityLooks.SelfGear();
        int right = gear.Length > InventoryConstants.VisRightHand ? ItemData.Get(gear[InventoryConstants.VisRightHand])?.Kind ?? WeaponAnimation.None : WeaponAnimation.None;
        int left = gear.Length > InventoryConstants.VisLeftHand ? ItemData.Get(gear[InventoryConstants.VisLeftHand])?.Kind ?? WeaponAnimation.None : WeaponAnimation.None;
        return GenieClip.Scale(right, left);
    }

    internal void SelfAction(string[] candidates, int rank)
    {
        if (context.Self.Dead) return;
        if (avatar.Anim == null) return;
        double now = FrameClock.Now();
        if (!CanPlaySelfAction(rank, now)) return;
        bool basicAttack = candidates == ActionAnimation.BasicAttackClips;
        double len;
        int strikeAnim = ActionAnimation.NoActionAnim;
        if (basicAttack)
        {
            var gear = entityLooks.SelfGear();
            strikeAnim = ActionAnimation.AttackSlot(gear);
            len = ActionAnimation.PlayActionOn(avatar.Anim, strikeAnim, ActionAnimation.AttackVariant(ActionAnimation.AttackClips(gear)));
        }
        else len = ActionAnimation.PlayActionOn(avatar.Anim, candidates);
        if (len > 0)
        {
            BeginSelfAction(len, rank, now);
            avatar.StrikeAnim = strikeAnim;
        }
    }

    internal void PlayEntityAction(EntityView e, string[] candidates, int rank)
    {
        if (e.Anim == null || e.Dead) return;
        double now = FrameClock.Now();
        if (!ActionAnimation.CanPlayEntityAction(e, rank, now)) return;
        bool playerSwing = candidates == ActionAnimation.BasicAttackClips && !e.IsNpc;
        int strikeAnim = playerSwing ? ActionAnimation.AttackSlot(e.Gear) : ActionAnimation.NoActionAnim;
        double len = playerSwing
            ? ActionAnimation.PlayActionOn(e.Anim, strikeAnim, ActionAnimation.AttackVariant(ActionAnimation.AttackClips(e.Gear)))
            : ActionAnimation.PlayActionOn(e.Anim, candidates == ActionAnimation.BasicAttackClips ? ActionAnimation.AttackVariant(candidates) : candidates);
        if (len <= 0) return;
        ActionAnimation.BeginEntityAction(e, len, rank, now);
        e.StrikeAnim = strikeAnim;
        if (candidates == ActionAnimation.BasicAttackClips && !e.IsNpc)
            e.CombatStanceUntil = e.ActionUntil + CombatStanceLingerSeconds;
    }

    internal void FaceToward(int attackerId, int targetId, bool instant = false)
    {
        Vector3? tgt = context.Entities.WorldPosOf(targetId);
        if (tgt == null) return;
        if (attackerId == context.Self.CharId && context.Self.Body != null)
        {
            if (instant) FaceSelfToward(tgt.Value);
            else TurnSelfToward(tgt.Value);
        }
        else if (context.Entities.Views.TryGetValue(attackerId, out var a)) FaceEntityToward(a, tgt.Value, instant);
    }

    private float? selfYawGoal;

    internal void FaceSelfToward(Vector3 target)
    {
        if (FlatDirectionTo(target) is not { } dir) return;
        avatar.Facing = dir;
        SnapSelfToFaceDir();
    }

    internal void TurnSelfToward(Vector3 target)
    {
        if (FlatDirectionTo(target) is not { } dir) return;
        avatar.Facing = dir;
        selfYawGoal = ActionAnimation.SelfYawOf(dir);
    }

    internal void SnapSelfToFaceDir()
    {
        selfYawGoal = null;
        context.Self.Body.RotationDegrees = new Vector3(0, ActionAnimation.SelfYawOf(context.Self.Facing), 0);
    }

    internal void TickSelfFacing(float dt)
    {
        if (selfYawGoal is not { } goal || context.Self.Body == null) return;
        float yaw = Turning.Toward(context.Self.Body.RotationDegrees.Y, goal, dt);
        context.Self.Body.RotationDegrees = new Vector3(0, yaw, 0);
        if (yaw == goal) selfYawGoal = null;
    }

    private Vector3? FlatDirectionTo(Vector3 target)
    {
        var d = target - context.Self.Body.Position;
        d.Y = 0;
        return d.LengthSquared() < 0.01f ? null : d.Normalized();
    }

    private void FaceEntityToward(EntityView e, Vector3 to, bool instant = false)
    {
        if (YawToward(e.Body.Position, to) is { } yaw) EntityRig.FaceEntity(e, yaw, instant);
    }

    internal void FaceNodeToward(Node3D node, Vector3 from, Vector3 to)
    {
        if (YawToward(from, to) is { } yaw) node.RotationDegrees = new Vector3(0, yaw, 0);
    }

    internal void FaceTowardPoint(int entityId, Vector3 point)
    {
        if (entityId == context.Self.CharId && context.Self.Body != null) TurnSelfToward(point);
        else if (context.Entities.Views.TryGetValue(entityId, out var e)) FaceEntityToward(e, point);
    }

    internal float? YawToward(Vector3 from, Vector3 to)
    {
        var (fx, fz) = scenery.WorldToMap(from);
        var (tx, tz) = scenery.WorldToMap(to);
        float dx = tx - fx, dz = tz - fz;
        if (dx * dx + dz * dz < 0.01f) return null;
        return 180f - Coord.MapHeading(dx, dz);
    }

    private const double KillingBlowGrace = 0.15;

    private const double KillingBlowCap = 1.5;

    internal void BeginDying(EntityView e)
    {
        e.Dead = true;
        double now = FrameClock.Now();
        e.DeathCheckAt = now + KillingBlowGrace;
        e.DeathDueAt = now + KillingBlowCap;
    }

    internal void TickDying(EntityView e, double now)
    {
        if (now < e.DeathCheckAt) return;
        if (now < e.DeathDueAt && KillingBlowPending(e.Id)) return;
        FinishDying(e);
    }

    internal void FinishDying(EntityView e)
    {
        if (e.DeathDueAt <= 0) return;
        e.DeathDueAt = 0;
        var pose = e.KillStriker >= 0 ? DeathPoses.ForKillerKind(SkillAnimation.HeldItemClass(GearOf(e.KillStriker), InventoryConstants.VisRightHand)) : DeathPose.KeepPosition;
        var killer = e.KillStriker == context.Self.CharId ? context.Self.Body : context.Entities.Views.TryGetValue(e.KillStriker, out var k) ? k.Body : null;
        e.KillStriker = -1;
        float cry = LayOutCorpse(e, settled: false, pose);
        double now = FrameClock.Now();
        if (cry > 0f) e.DeathCryAt = now + cry;
        else DeathCry?.Invoke(e.Id);
        if (killer == null || !GodotObject.IsInstanceValid(killer) || !DeathPoses.Pushes(pose, e.ModelId)) return;
        var away = e.Body.GlobalPosition - killer.GlobalPosition;
        away.Y = 0f;
        if (away.LengthSquared() <= 0f) return;
        e.PushDir = away.Normalized();
        e.PushStart = now;
    }

    internal void TickCorpse(EntityView e, double now, double delta)
    {
        if (e.DeathCryAt > 0 && now >= e.DeathCryAt)
        {
            e.DeathCryAt = 0;
            DeathCry?.Invoke(e.Id);
        }
        if (e.PushStart < 0) return;
        float seconds = (float)(now - e.PushStart);
        float step = DeathPoses.PushStep(seconds, e.Radius * e.Body.Scale.Y, (float)delta);
        if (step <= 0f)
        {
            if (seconds >= DeathPoses.PushEndSeconds) e.PushStart = -1;
            return;
        }
        var moved = e.Body.Position + e.PushDir * step;
        var (mapX, mapZ) = scenery.WorldToMap(moved);
        e.Body.Position = scenery.GroundPosFor(e.IsNpc, mapX, mapZ, e.MapY, e.Lift);
    }

    internal static bool Dying(EntityView e) => e.DeathDueAt > 0;

    private bool KillingBlowPending(int victimId)
    {
        if (avatar.StrikeTarget == victimId && StrikeStillAhead(avatar.Anim)) return true;
        foreach (var attacker in context.Entities.Views.Values)
            if (attacker.StrikeTarget == victimId && !attacker.Dead && StrikeStillAhead(attacker.Anim)) return true;
        return false;
    }

    private static bool StrikeStillAhead(AnimationPlayer? anim)
    {
        if (anim == null) return false;
        if (!CrowdAnimator.Current(anim, out string name, out double pos))
        {
            name = anim.CurrentAnimation.ToString();
            pos = name.Length == 0 ? 0 : anim.CurrentAnimationPosition;
        }
        if (name.Length == 0 || ModelAnimation.AnimationMetaFor(anim, name) is not { } meta) return false;
        for (int slot = 0; slot < SoundCues.SoundSlots; slot++)
        {
            float at = meta.StrikeTime(slot);
            if (at >= 0f && at > pos) return true;
        }
        return false;
    }

    private const double CorpseLinger = 5.0;

    internal float LayOutCorpse(EntityView e, bool settled = true, DeathPose pose = DeathPose.KeepPosition)
    {
        e.Dead = true;
        e.DeathDueAt = 0;
        e.HasTarget = false;
        e.Speed = 0f;
        e.CorpseRemoveAt = e.IsNpc && lootBoxes?.HoldsBox(e.Id) != true ? context.Now + CorpseLinger : 0;
        e.Flinch?.Stop();
        if (settled) e.Hp = 0;
        NpcGatesFeature.RefreshGateBlocker(e);
        int clip = DeathPoses.ClipIndex(pose, e.IsNpc || WearsMonsterLook(e.Id));
        double len = ActionAnimation.PlayActionOn(e.Anim, clip, ActionAnimation.DeathClips);
        e.DeathClipEnd = settled ? context.Now : context.Now + len;
        e.ActionUntil = context.Now + Mathf.Max((float)len, DeathActionMinSeconds);
        e.ActionRank = ModelAnimation.ActionRankDeath;
        e.ActionClip = "dead";
        if (settled && e.Anim != null && len > 0)
        {
            BlendHold.Release(e.Anim);
            e.Anim.Advance(len);
        }
        return e.Anim != null && ModelAnimation.AnimationMetaAt(e.Anim, clip) is { } meta ? meta.SoundTime(0) : -1f;
    }
}
