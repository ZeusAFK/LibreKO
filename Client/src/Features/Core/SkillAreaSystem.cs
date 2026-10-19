using Godot;

namespace LibreKO.Features.Core;

internal sealed class SkillAreaSystem : IFeature, IAreaCasts, IEscapable
{
    private readonly IWorldContext context;
    private readonly SelfSession self;
    private readonly ScenerySystem scenery;
    private readonly EntityActionSystem entityAction;
    private readonly PlayerSystem player;
    private readonly SkillCastSystem skillCast;

    public SkillAreaSystem(IWorldContext context, SelfSession self, ScenerySystem scenery, EntityActionSystem entityAction, PlayerSystem player, SkillCastSystem skillCast)
    {
        this.context = context;
        this.self = self;
        this.scenery = scenery;
        this.entityAction = entityAction;
        this.player = player;
        this.skillCast = skillCast;
    }

    public void Build()
    {
        skillCast.AttachAreaCasts(this);
    }

    bool IEscapable.CloseOnEscape()
    {
        if (AimingAreaSkill) CancelAreaCast();
        else return false;
        return true;
    }

    public void Dispose()
    {
        skillCast.DetachAreaCasts();
    }

    private const int AreaRingPointers = 8;
    private const float AreaRingGrowRate = 60f;
    private const float AreaRingSpinDeg = 50f;
    private const float AreaRingInset = 1f;
    private const double AreaAimRepickInterval = 1.0 / 30.0;
    private const string AreaRingFx = "zone_pointer";
    private const string AreaMarkerFxKarus = "region_target_ka_wizard";
    private const string AreaMarkerFxElMorad = "region_target_el_wizard";

    private sealed class AreaRing
    {
        public readonly List<Node3D> Pointers = new(AreaRingPointers);
        public Node3D? Marker;
        public Vector3 Centre;
        public float Radius;
        public float Grown;
        public float Spin;
    }

    private int areaSkillId = -1;
    private AreaRing? areaAim;
    private Vector3 areaAimPoint;
    private bool areaAimValid;
    private double areaRepickAccum;

    internal bool AimingAreaSkill => areaSkillId >= 0;

    bool IAreaCasts.BeginAreaCast(SkillData.Skill s) => BeginAreaCast(s);

    void IAreaCasts.SendAreaCast(SkillData.Skill s, Vector3 impact) => SendAreaCast(s, impact);

    internal bool BeginAreaCast(SkillData.Skill s)
    {
        if (context.Self.Body == null || context.Self.Dead) return false;

        CancelAreaCast();
        areaSkillId = s.Id;
        areaRepickAccum = AreaAimRepickInterval;
        areaAimPoint = context.Self.Body.GlobalPosition;
        areaAimValid = false;
        areaAim = NewAreaRing(areaAimPoint, AreaRingRadius(s), marker: true);
        UpdateAreaAim(context.Root.GetViewport().GetMousePosition());
        return true;
    }

    internal void CancelAreaCast()
    {
        if (areaSkillId < 0 && areaAim == null) return;
        areaSkillId = -1;
        FreeAreaRing(areaAim);
        areaAim = null;
        areaAimValid = false;
    }

    private void ConfirmAreaCast(Vector2 screenPosition)
    {
        var s = SkillData.Get(areaSkillId);
        UpdateAreaAim(screenPosition);
        Vector3 impact = areaAimPoint;
        bool valid = areaAimValid;
        CancelAreaCast();
        if (s == null || context.Self.Body == null) return;

        if (!valid) return;
        if (!skillCast.AnnounceReach(skillCast.SkillReachToPoint(s, impact, 0f, targetIsPlayer: false))) return;

        SendAreaCast(s, impact);
    }

    internal void SendAreaCast(SkillData.Skill s, Vector3 impact)
    {
        if (context.Self.Body == null) return;
        var (mapX, mapZ) = scenery.WorldToMap(impact);
        var data = new short[MagicData.Length];
        data[0] = (short)Mathf.RoundToInt(mapX);
        data[1] = (short)Mathf.RoundToInt(impact.Y);
        data[2] = (short)Mathf.RoundToInt(mapZ);

        entityAction.FaceNodeToward(context.Self.Body, context.Self.Body.Position, impact);
        bool skipCooldown = skillCast.SpendInstantMagic(s);
        skillCast.SendCastingStage(s, -1, data);
        skillCast.QueuePendingStage(s.Id, -1, s.HasFlyingStage ? SkillCastSystem.PendingFlying : SkillCastSystem.PendingEffecting, SkillCastSystem.CastDelay(s), data);
        skillCast.BeginLocalCast(s, skipCooldown);
    }

    private static float AreaRingRadius(SkillData.Skill s) => Mathf.Max(1f, s.Radius - AreaRingInset);

    internal void AreaCastTick(double delta, double now)
    {
        if (AimingAreaSkill)
        {
            if (context.Self.Dead) CancelAreaCast();
            else
            {
                areaRepickAccum += delta;
                if (areaRepickAccum >= AreaAimRepickInterval)
                {
                    areaRepickAccum = 0;
                    if (context.Root.GetViewport().GuiGetHoveredControl() == null)
                        UpdateAreaAim(context.Root.GetViewport().GetMousePosition());
                }
            }
        }

        if (areaAim != null) AnimateAreaRing(areaAim, delta);
    }

    private void UpdateAreaAim(Vector2 screenPosition)
    {
        if (!AimingAreaSkill) return;
        if (!player.TryPickTerrainPoint(screenPosition, 0f, out Vector3 point)) { areaAimValid = false; return; }
        if (PickAreaFloor(screenPosition, point) is { } floor) point = floor;
        areaAimPoint = point;
        areaAimValid = true;
        if (areaAim != null) areaAim.Centre = point;
    }

    private void AnimateAreaRing(AreaRing ring, double delta)
    {
        if (ring.Grown < ring.Radius)
            ring.Grown = Mathf.Min(ring.Radius, ring.Grown + AreaRingGrowRate * (float)delta);
        ring.Spin += Mathf.DegToRad(AreaRingSpinDeg) * (float)delta;

        if (ring.Marker != null && GodotObject.IsInstanceValid(ring.Marker))
            ring.Marker.GlobalPosition = ring.Centre + Vector3.Up * 0.05f;

        for (int i = 0; i < ring.Pointers.Count; i++)
        {
            var node = ring.Pointers[i];
            if (!GodotObject.IsInstanceValid(node)) continue;
            float angle = ring.Spin + Mathf.Tau * i / ring.Pointers.Count;
            var at = ring.Centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ring.Grown;
            node.GlobalPosition = SnapToFloor(at, ring.Centre.Y) + Vector3.Up * 0.05f;
        }
    }

    private const float AreaFloorRayMargin = 0.5f;
    private const float AreaFloorProbeUp = 2f;

    private Vector3? PickAreaFloor(Vector2 screenPosition, Vector3 terrainPoint)
    {
        if (context.Camera == null) return null;
        var from = context.Camera.ProjectRayOrigin(screenPosition);
        var direction = context.Camera.ProjectRayNormal(screenPosition).Normalized();
        var to = from + direction * (from.DistanceTo(terrainPoint) + AreaFloorRayMargin);
        var query = PhysicsRayQueryParameters3D.Create(from, to, CollisionLayers.World);
        if (scenery.SelfProbeExclude() is { } exclude) query.Exclude = exclude;
        var hit = context.Root.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || ((Vector3)hit["normal"]).Y < ScenerySystem.FloorNormalMin) return null;
        return (Vector3)hit["position"];
    }

    private Vector3 SnapToGround(Vector3 worldPosition)
    {
        var (mapX, mapZ) = scenery.WorldToMap(worldPosition);
        return context.Terrain.GroundPos(mapX, mapZ, worldPosition.Y, 0f);
    }

    private Vector3 SnapToFloor(Vector3 worldPosition, float nearY)
    {
        var onTerrain = SnapToGround(worldPosition);
        if (scenery.Terrain == null) return onTerrain;
        var floors = scenery.ObjectFloorsAt(onTerrain.X, onTerrain.Z,
            Mathf.Max(onTerrain.Y, nearY) + AreaFloorProbeUp, onTerrain.Y - ScenerySystem.EntityFloorProbeDown);
        return new Vector3(onTerrain.X, FloorChoice.Nearest(onTerrain.Y, floors, nearY), onTerrain.Z);
    }

    private AreaRing? NewAreaRing(Vector3 centre, float radius, bool marker)
    {
        var ring = new AreaRing { Centre = centre, Radius = radius };
        for (int i = 0; i < AreaRingPointers; i++)
        {
            var node = Fx.Spawn(AreaRingFx, context.Root, centre);
            if (node == null) break;
            node.Name = $"AreaRingPointer{i}";
            ring.Pointers.Add(node);
        }
        if (marker)
        {
            ring.Marker = Fx.Spawn(AreaMarkerFx(), context.Root, centre + Vector3.Up * 0.05f);
            if (ring.Marker != null) ring.Marker.Name = "AreaCastMarker";
        }
        return ring.Pointers.Count > 0 || ring.Marker != null ? ring : null;
    }

    private static void FreeAreaRing(AreaRing? ring)
    {
        if (ring == null) return;
        foreach (var node in ring.Pointers)
            if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        ring.Pointers.Clear();
        if (ring.Marker != null && GodotObject.IsInstanceValid(ring.Marker)) ring.Marker.QueueFree();
        ring.Marker = null;
    }

    private string AreaMarkerFx() =>
        self.Class / 100 == 1 ? AreaMarkerFxKarus : AreaMarkerFxElMorad;

    internal bool AreaCastPointer(InputEvent inputEvent, bool overUi)
    {
        if (!AimingAreaSkill) return false;

        if (inputEvent is InputEventMouseMotion motion)
        {
            if (!overUi) UpdateAreaAim(motion.Position);
            return false;
        }

        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left } click && !overUi)
        {
            if (click.Pressed) ConfirmAreaCast(click.Position);
            return true;
        }

        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && !overUi)
        {
            CancelAreaCast();
            return true;
        }

        return false;
    }
}
