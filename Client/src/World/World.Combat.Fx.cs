using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private void SpawnHitImpact(Ent victim, Vector3 strikeDir, int weaponItemId)
    {
        Vector3 hitPoint = RandomHitPoint(victim);
        string element = WeaponElement(weaponItemId, targetEffect: true) ?? "";
        string targetFx = element.Length > 0 ? $"{element}_sword_target0_1" : "damage0_1";
        var impact = Fx.Spawn(targetFx, this, hitPoint, oneShot: true);
        if (impact != null && element.Length > 0)
            impact.Scale = Vector3.One * 0.22f;
    }

    private static Aabb EntityLocalBox(Ent e)
    {
        if (e.HitFxBox is { } cached) return cached;
        bool first = true;
        var box = new Aabb();
        var inv = e.Body.GlobalTransform.AffineInverse();
        foreach (var mi in FindAll<MeshInstance3D>(e.Body))
        {
            if (mi.Mesh == null || !mi.Visible) continue;
            if (e.HpBar != null && e.HpBar.IsAncestorOf(mi)) continue;
            var b = (inv * mi.GlobalTransform) * mi.GetAabb();
            box = first ? b : box.Merge(b);
            first = false;
        }
        if (first || box.Size.Y < 0.2f)
            box = new Aabb(new Vector3(-e.Radius * 0.7f, 0.2f, -e.Radius * 0.7f),
                           new Vector3(e.Radius * 1.4f, 1.6f, e.Radius * 1.4f));
        e.HitFxBox = box;
        return box;
    }

    private Vector3 RandomHitPoint(Ent e)
    {
        var box = EntityLocalBox(e);
        var p = box.Position + new Vector3(
            box.Size.X * (0.5f + (GD.Randf() - 0.5f) * 0.55f),
            box.Size.Y * Mathf.Lerp(0.30f, 0.85f, GD.Randf()),
            box.Size.Z * (0.5f + (GD.Randf() - 0.5f) * 0.55f));
        return e.Body.GlobalTransform * p;
    }

    private void SpawnFxOn(int id, string fxName, float y)
    {
        Node3D? parent = id == _myId ? _self : (_ents.TryGetValue(id, out var e) ? e.Body : null);
        if (parent == null) return;
        Fx.Spawn(fxName, parent, new Vector3(0, y, 0), oneShot: true);
    }

    private static float ProjectileSpeedFor(string? fxName) =>
        Mathf.Max(fxName != null ? Fx.AuthoredVelocity(fxName) : 0f, ProjectileFxSpeed);

    private void LaunchFlight(int casterId, int targetId, SkillData.Skill s, short[] data, bool own)
    {
        StopSkillFx(casterId, s.Id, 1);
        if (s.IsMelee || s.SelfAnim2 != 0)
            PlaySkillAction(casterId, SkillAnim(casterId, s, true), ClipsForCast(s), ActionRankSkill);
        var from = WorldPosOf(casterId);
        Vector3? impact = targetId < 0 ? AreaImpactPoint(data) : null;
        var aim = impact ?? WorldPosOf(targetId);
        if (own) ClearPendingCast(s.Id);
        if (from == null || aim == null)
        {
            if (own) EndCast(s.Id);
            return;
        }
        if (s.FlyingFx != null) AudioFxAt(s.FlyingFxId, casterId);

        bool area = targetId < 0;
        var offsets = Volley.Offsets(s.NeedsFlying ? s.NeedArrow : 1);
        var reach = new Vector3(aim.Value.X - from.Value.X, 0f, aim.Value.Z - from.Value.Z);
        float rise = aim.Value.Y - from.Value.Y;
        for (int k = 0; k < offsets.Length; k++)
        {
            bool guided = k == 0 && !area && s.FlightHomes;
            Vector3 end = area || guided ? aim.Value : from.Value + reach.Rotated(Vector3.Up, offsets[k]) + new Vector3(0f, rise, 0f);
            if (!area && !guided && own && WorldPosOf(FirstHostileAlong(from.Value, end)) is { } struck) end = struck;
            if (s.FlyingFx != null)
                SpawnFxProjectile(casterId, guided ? targetId : AreaImpactTarget, s.FlyingFx, 0f, guided ? null : end);
            if (!own) continue;
            double travel = ProjectileTravelTime(casterId, guided ? targetId : AreaImpactTarget, s.FlyingFx, guided ? null : end);
            _pendingCasts.Add(new PendingCast
            {
                EffectTime = Now() + travel + k * VolleyHitGapSeconds,
                SkillId = s.Id,
                TargetId = area || guided ? targetId : AreaImpactTarget,
                Stage = PendingEffecting,
                Data = area ? data : null,
                Straight = !area && !guided,
                From = from.Value,
                To = end,
            });
        }
    }

    private const int AreaImpactTarget = SkillFxTarget.AreaImpactTarget;
    private const float StraightArrowReachSlack = 0.5f;

    private int FirstHostileAlong(Vector3 from, Vector3 to)
    {
        var candidates = new List<VolleyCandidate>();
        foreach (var (id, e) in _ents)
            if (e.Attackable && !e.Dead)
                candidates.Add(new VolleyCandidate(id, e.Body.Position.X, e.Body.Position.Z, e.Radius + StraightArrowReachSlack));
        return Volley.FirstAlong(from.X, from.Z, to.X, to.Z, candidates);
    }

    private Vector3? AreaImpactPoint(short[] data) =>
        data.Length > 2 ? GroundPos(data[0], data[2], 0f, 0f) : null;

    private double ProjectileTravelTime(int casterId, int targetId, string? fxName, Vector3? impact = null)
    {
        var from = WorldPosOf(casterId);
        var to = impact ?? WorldPosOf(targetId);
        if (from == null || to == null) return 0.2;
        return Mathf.Clamp(from.Value.DistanceTo(to.Value) / ProjectileSpeedFor(fxName), 0.08f, 1.5f);
    }

    private void SpawnFxProjectile(int casterId, int targetId, string fxName, float lateral = 0f, Vector3? impact = null)
    {
        var from = WorldPosOf(casterId);
        var to = impact ?? WorldPosOf(targetId);
        if (from == null || to == null)
        {
            SpawnFxOn(targetId == 0 ? casterId : targetId, fxName, 1.2f);
            return;
        }

        var start = from.Value + new Vector3(0, ProjectileFxHeight, 0);
        var end = to.Value + new Vector3(0, ProjectileFxHeight, 0);
        if (lateral != 0f)
        {
            var side = (end - start).Cross(Vector3.Up);
            if (side.LengthSquared() > 0.0001f) start += side.Normalized() * lateral;
        }
        var node = Fx.Spawn(fxName, this, start);
        if (node is not FxInstance flight)
        {
            if (node != null) node.QueueFree();
            return;
        }
        flight.HomingTarget = _ents.TryGetValue(targetId, out var te) ? te.Body : null;
        flight.HomingPoint = end;
        flight.HomingHeight = ProjectileFxHeight;
        flight.FlightSpeed = ProjectileSpeedFor(fxName);
        flight.GlobalBasis = FxInstance.AimBasis(end - start);
    }

    private bool SpawnFxAtImpact(int casterId, int targetId, string fxName, int targetPart, short[] data,
        bool areaCast)
    {
        int entityId = targetId == 0 ? casterId : targetId;
        switch (SkillFxTarget.Placement(targetPart, targetId, areaCast))
        {
            case ImpactFxPlacement.UnderEntity:
                Node3D? body = entityId == _myId ? _self : (_ents.TryGetValue(entityId, out var e) ? e.Body : null);
                if (body == null) return false;
                Fx.Spawn(fxName, this, body.GlobalPosition + new Vector3(0, 0.15f, 0), oneShot: true);
                return true;
            case ImpactFxPlacement.OnEntity:
                SpawnOwnedFx(entityId, 0, 3, fxName, targetPart, oneShot: true);
                return true;
            case ImpactFxPlacement.AtImpactPoint when AreaImpactPoint(data) is { } point:
                Fx.Spawn(fxName, this, point + new Vector3(0, 0.15f, 0), oneShot: true);
                return true;
            default:
                return false;
        }
    }

    private void SpawnOwnedFx(int entityId, int skillId, int phase, string fxName, int encodedPart,
        bool oneShot = false)
    {
        int value = Mathf.Abs(encodedPart);
        int first = value % 1000;
        int second = value / 1000;
        SpawnOwnedFxPart(entityId, skillId, phase, fxName, first, encodedPart < 0, oneShot);
        if (second > 0)
            SpawnOwnedFxPart(entityId, skillId, phase, fxName, second, encodedPart < 0, oneShot);
    }

    private void SpawnOwnedFxPart(int entityId, int skillId, int phase, string fxName, int part,
        bool worldSentinel, bool oneShot)
    {
        Node3D? body = entityId == _myId ? _self : (_ents.TryGetValue(entityId, out var e) ? e.Body : null);
        if (body == null) return;
        Node3D owner = body;
        Skeleton3D? ownerSkeleton = null;
        if (!worldSentinel && FindFirst<Skeleton3D>(body) is { } skel && part >= 0 && part < skel.GetBoneCount())
        {
            var attach = new BoneAttachment3D { Name = $"skill_fx_{skillId}_{phase}_{part}", BoneIdx = part };
            skel.AddChild(attach);
            owner = attach;
            ownerSkeleton = skel;
        }
        Vector3 offset = Vector3.Zero;
        var node = Fx.Spawn(fxName, owner, offset, oneShot);
        if (node == null)
        {
            if (owner != body) owner.QueueFree();
            return;
        }
        var facing = ownerSkeleton ?? FindFirst<Skeleton3D>(body);
        if (facing != null && node is FxInstance pinned)
        {
            pinned.Pin(owner, facing, offset, Vector3.Back);
        }
        if (oneShot)
        {
            if (owner != body)
                node.TreeExited += () =>
                {
                    if (GodotObject.IsInstanceValid(owner) && !owner.IsQueuedForDeletion())
                        owner.QueueFree();
                };
            return;
        }

        var tracked = owner == body ? node : owner;
        var key = (entityId, skillId, phase);
        if (!_skillFx.TryGetValue(key, out var list)) _skillFx[key] = list = new List<Node3D>();
        list.Add(tracked);
    }

    private void StopSkillFx(int casterId, int skillId, int? phase = null)
    {
        var keys = new List<(int Caster, int Skill, int Phase)>();
        foreach (var key in _skillFx.Keys)
            if (key.Caster == casterId && key.Skill == skillId && (!phase.HasValue || key.Phase == phase.Value))
                keys.Add(key);
        foreach (var key in keys)
        {
            foreach (var node in _skillFx[key])
                if (GodotObject.IsInstanceValid(node)) node.QueueFree();
            _skillFx.Remove(key);
        }
    }
}
