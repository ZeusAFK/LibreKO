using Godot;

namespace LibreKO.Features.Core;

internal sealed class CombatFxSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly EntityActionSystem entityAction;
    private readonly EntitySystem entities;
    private readonly AudioSystem audio;
    private readonly EntityLooksSystem entityLooks;
    private readonly EntityStatesSystem entityStates;
    private readonly ScenerySystem scenery;

    public CombatFxSystem(IWorldContext context, EntityActionSystem entityAction, EntitySystem entities, AudioSystem audio, EntityLooksSystem entityLooks, EntityStatesSystem entityStates, ScenerySystem scenery)
    {
        this.context = context;
        this.entityAction = entityAction;
        this.entities = entities;
        this.audio = audio;
        this.entityLooks = entityLooks;
        this.entityStates = entityStates;
        this.scenery = scenery;
    }

    public void Build()
    {
        entityStates.GenieAuraChanged += ShowGenieAura;
        entities.EntityRemoved += ForgetSkillFx;
    }

    public void Dispose()
    {
        entityStates.GenieAuraChanged -= ShowGenieAura;
        entities.EntityRemoved -= ForgetSkillFx;
    }

    internal const float ProjectileFxHeight = 1.2f;
    private const int GenieAuraFxId = 30745;
    private const int GenieAuraKey = -GenieAuraFxId;
    private const int GenieAuraOnBody = -1;

    private void ShowGenieAura(int entityId, bool on)
    {
        if (on == ShowsSkillFx(entityId, GenieAuraKey, SkillFxPhase.Aura)) return;
        StopSkillFx(entityId, GenieAuraKey);
        if (on && Fx.NameForId(GenieAuraFxId) is { } fxName)
            SpawnOwnedFxPart(entityId, GenieAuraKey, SkillFxPhase.Aura, fxName, GenieAuraOnBody, worldSentinel: false, oneShot: false);
    }

    internal readonly record struct FlightArrival(double Travel, int TargetId, bool Area, bool Straight, Vector3 From, Vector3 To);

    internal List<FlightArrival>? LaunchFlight(int casterId, int targetId, SkillData.Skill s, short[] data, bool own)
    {
        StopSkillFx(casterId, s.Id, MagicSub.Casting);
        if (s.IsMelee || s.SelfAnim2 != 0)
            entityAction.PlaySkillAction(casterId, entityAction.SkillAnim(casterId, s, true), ActionAnimation.ClipsForCast(s), ModelAnimation.ActionRankSkill);
        var from = entities.WorldPosOf(casterId);
        Vector3? impact = targetId < 0 ? AreaImpactPoint(data, casterId) : null;
        var aim = impact ?? entities.WorldPosOf(targetId);
        if (from == null || aim == null) return null;
        if (s.FlyingFx != null) audio.FxAt(s.FlyingFxId, casterId);

        bool area = targetId < 0;
        var offsets = Volley.Offsets(s.NeedsFlying ? s.NeedArrow : 1);
        var arrivals = new List<FlightArrival>();
        var reach = new Vector3(aim.Value.X - from.Value.X, 0f, aim.Value.Z - from.Value.Z);
        float rise = aim.Value.Y - from.Value.Y;
        for (int k = 0; k < offsets.Length; k++)
        {
            bool guided = k == 0 && !area && s.FlightHomes;
            Vector3 end = area || guided ? aim.Value : from.Value + StraightArrowSpan(reach, s, own).Rotated(Vector3.Up, offsets[k]) + new Vector3(0f, rise, 0f);
            if (!area && !guided && own && entities.WorldPosOf(FirstHostileAlong(from.Value, end)) is { } struck) end = struck;
            if (s.FlyingFx != null)
                SpawnFxProjectile(casterId, guided ? targetId : AreaImpactTarget, s.FlyingFx, 0f, guided ? null : end);
            if (!own) continue;
            double travel = ProjectileTravelTime(casterId, guided ? targetId : AreaImpactTarget, s.FlyingFx, guided ? null : end);
            arrivals.Add(new FlightArrival(travel, area || guided ? targetId : AreaImpactTarget, area, !area && !guided, from.Value, end));
        }
        return arrivals;
    }

    private Vector3 StraightArrowSpan(Vector3 reach, SkillData.Skill s, bool own)
    {
        float distance = reach.Length();
        if (!own || distance <= 0f) return reach;
        float range = s.Range > 0 ? s.Range : entityLooks.HandWeaponReach(rightFirst: false) * Mathf.Max(s.AddRange, 0) / ArrowRangePercent;
        return distance >= range ? reach : reach / distance * range;
    }

    private const float ArrowRangePercent = 100f;
    internal const int AreaImpactTarget = SkillFxTarget.AreaImpactTarget;
    private const float StraightArrowReachSlack = 0.5f;

    internal int FirstHostileAlong(Vector3 from, Vector3 to)
    {
        var candidates = new List<VolleyCandidate>();
        foreach (var (id, e) in context.Entities.Views)
            if (e.Attackable && !e.Dead)
                candidates.Add(new VolleyCandidate(id, e.Body.Position.X, e.Body.Position.Z, e.Radius + StraightArrowReachSlack));
        return Volley.FirstAlong(from.X, from.Z, to.X, to.Z, candidates);
    }

    private Vector3? AreaImpactPoint(short[] data, int casterId)
    {
        if (data.Length <= 2) return null;
        var caster = entities.WorldPosOf(casterId);
        if (caster == null && (data[0] == 0 || data[2] == 0)) return null;
        var (casterX, casterZ) = caster is { } at ? scenery.WorldToMap(at) : (0f, 0f);
        float mapX = data[0] != 0 ? data[0] : casterX;
        float mapZ = data[2] != 0 ? data[2] : casterZ;
        float nearY = data[1] != 0 ? data[1] : caster?.Y ?? ScenerySystem.NoServerHeight;
        return scenery.FloorGroundPos(mapX, mapZ, nearY, 0f, login: false);
    }

    private double ProjectileTravelTime(int casterId, int targetId, string? fxName, Vector3? impact = null)
    {
        var from = entities.WorldPosOf(casterId);
        var to = impact ?? entities.WorldPosOf(targetId);
        if (from == null || to == null) return 0.2;
        return Mathf.Clamp(from.Value.DistanceTo(to.Value) / SkillFxRig.ProjectileSpeedFor(fxName), 0.08f, 1.5f);
    }

    private void SpawnFxProjectile(int casterId, int targetId, string fxName, float lateral = 0f, Vector3? impact = null)
    {
        var from = entities.WorldPosOf(casterId);
        var to = impact ?? entities.WorldPosOf(targetId);
        if (from == null || to == null)
        {
            entities.SpawnFxOn(targetId == 0 ? casterId : targetId, fxName, 1.2f);
            return;
        }

        var start = from.Value + new Vector3(0, ProjectileFxHeight, 0);
        var end = to.Value + new Vector3(0, ProjectileFxHeight, 0);
        if (lateral != 0f)
        {
            var side = (end - start).Cross(Vector3.Up);
            if (side.LengthSquared() > 0.0001f) start += side.Normalized() * lateral;
        }
        var node = Fx.Spawn(fxName, context.Root, start, deferParts: true);
        if (node is not FxInstance flight)
        {
            Fx.Free(node);
            return;
        }
        flight.HomingTarget = context.Entities.Views.TryGetValue(targetId, out var te) ? te.Body : null;
        flight.HomingPoint = end;
        flight.HomingHeight = ProjectileFxHeight;
        flight.FlightSpeed = SkillFxRig.ProjectileSpeedFor(fxName);
        flight.GlobalBasis = FxInstance.AimBasis(end - start);
    }

    internal bool SpawnFxAtImpact(int casterId, int targetId, string fxName, int targetPart, short[] data,
        bool areaCast, bool melee)
    {
        int entityId = targetId == 0 ? casterId : targetId;
        switch (SkillFxTarget.Placement(targetPart, targetId, areaCast, melee))
        {
            case ImpactFxPlacement.UnderEntity:
                Node3D? body = entityId == context.Self.CharId ? context.Self.Body : (context.Entities.Views.TryGetValue(entityId, out var e) ? e.Body : null);
                if (body == null) return false;
                Fx.Spawn(fxName, context.Root, body.GlobalPosition + new Vector3(0, 0.15f, 0), oneShot: true,
                    sizeScale: TargetFxWorldSize(fxName, casterId, entityId), deferParts: true);
                return true;
            case ImpactFxPlacement.OnEntity:
                SpawnOwnedFx(entityId, 0, MagicSub.Effecting, fxName, targetPart, oneShot: true,
                    sizeScale: TargetFxPinnedSize(fxName, casterId, entityId));
                return true;
            case ImpactFxPlacement.AtImpactPoint when AreaImpactPoint(data, casterId) is { } point:
                Fx.Spawn(fxName, context.Root, point + new Vector3(0, 0.15f, 0), oneShot: true, deferParts: true);
                return true;
            default:
                return false;
        }
    }

    internal void SpawnOwnedFx(int entityId, int skillId, int phase, string fxName, int encodedPart,
        bool oneShot = false, float sizeScale = 1f)
    {
        int value = Mathf.Abs(encodedPart);
        int first = value % 1000;
        int second = value / 1000;
        SpawnOwnedFxPart(entityId, skillId, phase, fxName, first, encodedPart < 0, oneShot, sizeScale);
        if (second > 0)
            SpawnOwnedFxPart(entityId, skillId, phase, fxName, second, encodedPart < 0, oneShot, sizeScale);
    }

    private float TargetFxWorldSize(string fxName, int casterId, int targetId)
    {
        if (targetId == casterId || !Fx.UsesTargetScale(fxName)) return 1f;
        if (targetId == context.Self.CharId || !context.Entities.Views.TryGetValue(targetId, out var e))
            return FxTargetScale.WorldSize(FxTargetScale.PlayerWidth, FxTargetScale.PlayerWidth, FxTargetScale.PlayerHeight);
        var size = SkillFxRig.EntityLocalBox(e).Size * e.Body.Scale;
        return FxTargetScale.WorldSize(size.X, size.Z, size.Y);
    }

    private float TargetFxPinnedSize(string fxName, int casterId, int targetId)
    {
        float worldSize = TargetFxWorldSize(fxName, casterId, targetId);
        bool scaled = targetId != casterId && targetId != context.Self.CharId && Fx.UsesTargetScale(fxName);
        return scaled && context.Entities.Views.TryGetValue(targetId, out var e) ? FxTargetScale.ForPinned(worldSize, e.Body.Scale.Y) : worldSize;
    }

    internal void SpawnOwnedFxPart(int entityId, int skillId, int phase, string fxName, int part,
        bool worldSentinel, bool oneShot, float sizeScale = 1f)
    {
        Node3D? body = entityId == context.Self.CharId ? context.Self.Body : (context.Entities.Views.TryGetValue(entityId, out var e) ? e.Body : null);
        if (body == null) return;
        Node3D owner = body;
        Skeleton3D? ownerSkeleton = null;
        if (!worldSentinel && NodeSearch.FindFirst<Skeleton3D>(body) is { } skel && part >= 0 && part < skel.GetBoneCount())
        {
            var attach = new BoneAttachment3D { Name = $"skill_fx_{skillId}_{phase}_{part}", BoneIdx = part };
            skel.AddChild(attach);
            owner = attach;
            ownerSkeleton = skel;
        }
        Vector3 offset = Vector3.Zero;
        var node = Fx.Spawn(fxName, owner, offset, oneShot, sizeScale: sizeScale, deferParts: true);
        if (node == null)
        {
            if (owner != body) owner.QueueFree();
            return;
        }
        var facing = ownerSkeleton ?? NodeSearch.FindFirst<Skeleton3D>(body);
        if (facing != null && node is FxInstance pinned)
            pinned.Pin(owner, facing, offset, Vector3.Back);
        if (owner != body)
            node.TreeExited += () =>
            {
                if (GodotObject.IsInstanceValid(owner) && !owner.IsQueuedForDeletion())
                    owner.QueueFree();
            };
        if (oneShot) return;

        var key = (entityId, skillId, phase);
        if (!skillFx.TryGetValue(key, out var list)) skillFx[key] = list = new List<Node3D>();
        list.Add(node);
    }

    private readonly Dictionary<(int Caster, int Skill, int Phase), List<Node3D>> skillFx = new();

    internal bool ShowsSkillFx(int casterId, int skillId, int phase) =>
        skillFx.TryGetValue((casterId, skillId, phase), out var nodes) && nodes.Exists(GodotObject.IsInstanceValid);

    private const double SkillFxSweepSeconds = 10.0;
    private double skillFxSweepAt;

    internal void StopSkillFx(int casterId, int skillId, int? phase = null)
    {
        if (phase is int one)
        {
            StopSkillFxKey((casterId, skillId, one));
            return;
        }
        foreach (int each in SkillFxPhase.All) StopSkillFxKey((casterId, skillId, each));
    }

    private void StopSkillFxKey((int Caster, int Skill, int Phase) key)
    {
        if (!skillFx.Remove(key, out var list)) return;
        foreach (var node in list) Fx.Free(node);
    }

    internal void SweepSkillFx(double now)
    {
        if (now < skillFxSweepAt) return;
        skillFxSweepAt = now + SkillFxSweepSeconds;
        List<(int Caster, int Skill, int Phase)>? empty = null;
        foreach (var (key, list) in skillFx)
        {
            list.RemoveAll(node => !GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion());
            if (list.Count == 0) (empty ??= new()).Add(key);
        }
        if (empty == null) return;
        foreach (var key in empty) skillFx.Remove(key);
    }

    internal void ForgetSkillFx(int casterId)
    {
        var keys = new List<(int Caster, int Skill, int Phase)>();
        foreach (var key in skillFx.Keys)
            if (key.Caster == casterId) keys.Add(key);
        foreach (var key in keys) skillFx.Remove(key);
    }
}
