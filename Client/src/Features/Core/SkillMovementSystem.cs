using Godot;

namespace LibreKO.Features.Core;

internal sealed class SkillMovementSystem : IFeature, IMovementSkills
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly ScenerySystem scenery;
    private readonly EntityActionSystem entityAction;
    private readonly CombatFxSystem combatFx;
    private readonly SkillCastSystem skillCast;

    public SkillMovementSystem(IWorldContext context, SelfAvatar avatar, ScenerySystem scenery, EntityActionSystem entityAction, CombatFxSystem combatFx, SkillCastSystem skillCast)
    {
        this.context = context;
        this.avatar = avatar;
        this.scenery = scenery;
        this.entityAction = entityAction;
        this.combatFx = combatFx;
        this.skillCast = skillCast;
    }

    public void Build()
    {
        skillCast.AttachMovementSkills(this);
    }

    public void Dispose()
    {
        skillCast.DetachMovementSkills();
    }

    private const double DashSeconds = 0.3;
    private const double PullReachSeconds = 0.2;
    private const float DashStopDefault = 1.124f;
    private const float DashStopMin = 1.1f;
    private const float DashStopMax = 1.2f;
    private const float DashRayLift = 1.0f;
    private const int DashXSlot = 0;
    private const int DashResultSlot = 1;
    private const int DashZSlot = 2;
    private const short DashSucceeded = 1;
    private const int PullFailedFx = 30731;
    private const int TextSkillFailed = 4404;

    private Vector3 selfDashFrom;
    private Vector3 selfDashTo;
    private double selfDashStart = double.NegativeInfinity;

    private static int DashWarpType(SkillData.Skill s)
    {
        int warp = s.WarpKind;
        if (warp is WarpType.Rush or WarpType.Pull) return warp;
        return SkillReach.SpotKind(s.Id) switch
        {
            SkillReach.SpotRush => WarpType.Rush,
            SkillReach.SpotPull => WarpType.Pull,
            _ => 0,
        };
    }

    void IMovementSkills.CastBlink(SkillData.Skill s) => CastBlink(s);

    short[]? IMovementSkills.DashRequestData(int skillId, int targetId) => DashRequestData(skillId, targetId);

    bool IMovementSkills.OnDashEffecting(SkillData.Skill s, int casterId, int targetId, short[] data) => OnDashEffecting(s, casterId, targetId, data);

    internal short[]? DashRequestData(int skillId, int targetId)
    {
        if (SkillData.Get(skillId) is not { } s) return null;
        int warp = DashWarpType(s);
        if (warp == 0 || !context.Entities.Views.TryGetValue(targetId, out var target)) return null;
        float radius = context.Entities.Views.TryGetValue(context.Self.CharId, out var me) ? me.Radius : SelfAvatar.DefaultBodyRadius;
        var mine = context.Self.Body.GlobalPosition;
        var theirs = target.Body.GlobalPosition;
        var dest = warp == WarpType.Rush ? DashDestination(mine, theirs, radius) : DashDestination(theirs, mine, radius);
        var (mapX, mapZ) = scenery.WorldToMap(dest);
        var data = new short[MagicData.Length];
        data[DashXSlot] = Tenths(mapX);
        data[DashZSlot] = Tenths(mapZ);
        return data;
    }

    private static short Tenths(float units) =>
        unchecked((short)(ushort)Mathf.Clamp(Mathf.RoundToInt(units * Coord.TenthsPerUnit), 0, ushort.MaxValue));

    private static float FromTenths(short wire) => unchecked((ushort)wire) / Coord.TenthsPerUnit;

    private Vector3 DashDestination(Vector3 from, Vector3 to, float radius)
    {
        float stop = radius is >= DashStopMin and <= DashStopMax ? radius : DashStopDefault;
        var span = new Vector3(to.X - from.X, 0f, to.Z - from.Z);
        float length = span.Length();
        if (length <= stop) return from;
        var dir = span / length;
        var goal = to - dir * stop;

        var query = PhysicsRayQueryParameters3D.Create(
            from + Vector3.Up * DashRayLift, goal + Vector3.Up * DashRayLift, CollisionLayers.World);
        query.Exclude = skillCast.SightExclusions();
        var hit = context.Root.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
        {
            var at = (Vector3)hit["position"];
            float clear = new Vector2(at.X - from.X, at.Z - from.Z).Length();
            goal = from + dir * Mathf.Max(0f, clear - Mathf.Min(stop, clear));
        }
        goal.Y = scenery.HeightNear(goal);
        return goal;
    }

    internal bool OnDashEffecting(SkillData.Skill s, int casterId, int targetId, short[] data)
    {
        int warp = DashWarpType(s);
        if (warp == 0) return false;

        entityAction.PlaySkillAction(casterId, entityAction.SkillAnim(casterId, s, false), ActionAnimation.ClipsForCast(s), ModelAnimation.ActionRankSkill, restart: true);
        if (targetId >= 0) entityAction.FaceToward(casterId, targetId, instant: true);

        bool succeeded = data.Length > DashZSlot && data[DashResultSlot] == DashSucceeded;
        if (!succeeded)
        {
            if (warp == WarpType.Pull && Fx.NameForId(PullFailedFx) is { } failed)
                context.Entities.SpawnFxOn(casterId, failed, 0f);
            if (casterId == context.Self.CharId) context.Log.Notice(GameText.System(TextSkillFailed, "Failed %s.", s.Name));
            return true;
        }

        float mapX = FromTenths(data[DashXSlot]), mapZ = FromTenths(data[DashZSlot]);
        if (warp == WarpType.Rush)
        {
            if (s.SelfFx1 != null) combatFx.SpawnOwnedFx(casterId, s.Id, MagicSub.Casting, s.SelfFx1, s.SelfPart1, oneShot: true);
            DashEntity(casterId, mapX, mapZ);
            return true;
        }

        if (s.SelfFx1 != null) combatFx.SpawnOwnedFx(casterId, s.Id, MagicSub.Casting, s.SelfFx1, s.SelfPart1, oneShot: true);
        context.Root.After(PullReachSeconds, () =>
        {
            if (s.TargetFx != null) combatFx.SpawnOwnedFx(targetId, s.Id, MagicSub.Effecting, s.TargetFx, s.TargetPart, oneShot: true);
            DashEntity(targetId, mapX, mapZ);
        });
        return true;
    }

    private void DashEntity(int id, float mapX, float mapZ)
    {
        if (id == context.Self.CharId)
        {
            if (context.Self.Body == null || !GodotObject.IsInstanceValid(context.Self.Body)) return;
            selfDashFrom = context.Self.Body.Position;
            selfDashTo = scenery.EntityGroundPos(mapX, mapZ, ScenerySystem.NoServerHeight, context.Self.Lift);
            selfDashStart = FrameClock.Now();
            return;
        }
        if (!context.Entities.Views.TryGetValue(id, out var e)) return;
        var dest = scenery.EntityGroundPos(mapX, mapZ, ScenerySystem.NoServerHeight, e.Lift);
        float distance = new Vector2(dest.X - e.Body.Position.X, dest.Z - e.Body.Position.Z).Length();
        e.MapX = mapX;
        e.MapZ = mapZ;
        e.Target = dest;
        e.HasTarget = true;
        e.Speed = distance / (float)DashSeconds;
    }

    internal bool TickSelfDash()
    {
        if (double.IsNegativeInfinity(selfDashStart) || context.Self.Body == null) return false;
        double t = (FrameClock.Now() - selfDashStart) / DashSeconds;
        var pos = selfDashFrom.Lerp(selfDashTo, (float)Mathf.Min(t, 1.0));
        var (mapX, mapZ) = scenery.WorldToMap(pos);
        pos = context.Terrain.GroundPos(mapX, mapZ, pos.Y - context.Self.Lift, context.Self.Lift);
        context.Self.Body.Position = pos;
        avatar.LastFreePosition = pos;
        avatar.MapX = mapX;
        avatar.MapZ = mapZ;
        avatar.MapY = pos.Y - context.Self.Lift;
        if (avatar.Body != null) avatar.Body.Velocity = Vector3.Zero;
        if (t >= 1.0) selfDashStart = double.NegativeInfinity;
        return true;
    }

    private const float BlinkProbeLift = 1f;
    private const float BlinkWallMargin = 2.1f;
    private const float BlinkTerrainStep = 0.5f;
    private const double BlinkWarpWindow = 3.0;

    internal void CastBlink(SkillData.Skill s)
    {
        if (context.Self.Body == null) return;
        var stop = BlinkDestination(s.Radius);
        var (mapX, mapZ) = scenery.WorldToMap(stop);
        var data = BlinkPath.Data(mapX, stop.Y, mapZ);

        bool skipCooldown = skillCast.SpendInstantMagic(s);
        avatar.QuietWarpsUntil = FrameClock.Now() + BlinkWarpWindow;
        skillCast.SendCastingStage(s, context.Self.CharId, data);
        skillCast.QueuePendingStage(s.Id, context.Self.CharId, SkillCastSystem.PendingEffecting, SkillCastSystem.CastDelay(s), data);
        skillCast.BeginLocalCast(s, skipCooldown);
    }

    private Vector3 BlinkDestination(float reach)
    {
        var start = context.Self.Body!.GlobalPosition;
        var (dx, dz) = BlinkPath.MapDirection(context.Self.Body.RotationDegrees.Y);
        var dir = Coord.DirToGodot(dx, 0f, dz);
        var end = start + dir * reach;
        var from = new Vector3(start.X, start.Y - context.Self.Lift + BlinkProbeLift, start.Z);
        var to = new Vector3(end.X, scenery.HeightNear(end) + BlinkProbeLift, end.Z);

        float clear = BlinkClearDistance(from, to, reach);
        if (clear < reach) clear = Mathf.Max(0f, clear - BlinkWallMargin);
        var stop = start + dir * clear;
        stop.Y = scenery.HeightNear(stop);
        return stop;
    }

    private float BlinkClearDistance(Vector3 from, Vector3 to, float reach)
    {
        float clear = reach;
        var q = PhysicsRayQueryParameters3D.Create(from, to, CollisionLayers.World);
        if (avatar.Body != null) q.Exclude = new Godot.Collections.Array<Rid> { avatar.Body.GetRid() };
        var hit = context.Root.GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count > 0)
        {
            var at = (Vector3)hit["position"];
            clear = new Vector2(at.X - from.X, at.Z - from.Z).Length();
        }

        for (float d = BlinkTerrainStep; d < clear; d += BlinkTerrainStep)
        {
            var p = from.Lerp(to, d / reach);
            if (scenery.HeightNear(p) > p.Y) return d;
        }
        return clear;
    }
}
