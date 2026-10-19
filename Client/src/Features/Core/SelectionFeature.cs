using Godot;

namespace LibreKO.Features.Core;

internal sealed partial class SelectionFeature : IFeature, ISelection
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly PartySession party;
    private readonly SelfSession self;
    private readonly EntitiesSession entitiesSession;
    private readonly ScenerySystem scenery;
    private readonly BuffSystem buff;
    private readonly MapInteractablesSystem mapInteractables;
    private readonly EntityActionSystem entityAction;
    private readonly PlayerSystem player;
    private readonly SkillCastSystem skillCast;
    private readonly BasicAttackSystem basicAttack;

    public SelectionFeature(IWorldContext context, PartySession party, SelfSession self, EntitiesSession entitiesSession, SelfAvatar avatar, ScenerySystem scenery, BuffSystem buff, MapInteractablesSystem mapInteractables, EntityActionSystem entityAction, PlayerSystem player, SkillCastSystem skillCast, BasicAttackSystem basicAttack)
    {
        this.context = context;
        this.party = party;
        this.self = self;
        this.entitiesSession = entitiesSession;
        this.avatar = avatar;
        this.scenery = scenery;
        this.buff = buff;
        this.mapInteractables = mapInteractables;
        this.entityAction = entityAction;
        this.player = player;
        this.skillCast = skillCast;
        this.basicAttack = basicAttack;
    }

    public void Build()
    {
        scenery.ObjectsReset += OnObjectsReset;
        scenery.MapFxReset += ClearPickCandidates;
    }

    internal void Attach() => entitiesSession.EntityHpSyncEvent += OnEntityHpSync;

    bool ISelection.Selectable(EntityView e) => Selectable(e);
    EntityView? ISelection.PickAt(Vector2 screen, out int id) => PickAt(screen, out id);
    int ISelection.SelectedId => SelectedId;
    void ISelection.Select(int id) => Select(id);
    void ISelection.Deselect() => Deselect();
    void ISelection.FaceSelectedWhenStill() => FaceSelectedWhenStill();
    void ISelection.WithSelected(int id, Action action) => WithSelected(id, action);

    public void Dispose()
    {
        entitiesSession.EntityHpSyncEvent -= OnEntityHpSync;
        scenery.ObjectsReset -= OnObjectsReset;
        scenery.MapFxReset -= ClearPickCandidates;
    }

    internal int SelectedId { get; private set; } = -1;

    internal event Action<int>? TalkRequested;

    private IDebugOverlay? debugOverlay;

    internal void AttachDebugOverlay(IDebugOverlay debugOverlay) => this.debugOverlay = debugOverlay;

    internal void DetachDebugOverlay() => debugOverlay = null;

    private MeshInstance3D selRing = null!;
    private ShaderMaterial selRingMat = null!;
    internal const float ClickPickRadius = 48f;
    private const float BodyPickPadPx = 3f;
    private const float BodyPickMinPx = 14f;
    private const float BodyPickDist = 40f;

    internal void BuildRing()
    {
        selRingMat = TargetSymbol.Material();
        selRing = new MeshInstance3D
        {
            Mesh = TargetSymbol.Mesh(),
            MaterialOverride = selRingMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        context.Entities.Layer.AddChild(selRing);
    }

    private MeshInstance3D objHighlight = null!;
    private StandardMaterial3D objHighlightMat = null!;
    private static readonly Color SelectedBoxColor = new(1f, 0.15f, 0.12f);

    internal void BuildObjectHighlight()
    {
        objHighlightMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = SelectedBoxColor,
            NoDepthTest = true,
        };
        objHighlight = new MeshInstance3D
        {
            Mesh = PickShapes.BuildWireBoxMesh(),
            Visible = false,
            MaterialOverride = objHighlightMat,
        };
        context.Root.AddChild(objHighlight);
    }

    internal void RefreshObjectHighlight()
    {
        if (objHighlight == null) return;
        if (debugOverlay?.IsOpen == true && PickMode == PickKind.Object && scenery.PickedObject?.HitMesh is { } hitMesh)
        {
            var aabb = hitMesh.GetAabb();
            var local = new Transform3D(Basis.Identity.Scaled(aabb.Size), aabb.GetCenter());
            objHighlight.GlobalTransform = scenery.PickedObject.HitXform * local;
            objHighlight.Visible = true;
        }
        else if (debugOverlay?.IsOpen == true && PickMode == PickKind.Effect && scenery.PickedFx is { } fx
                 && GodotObject.IsInstanceValid(fx.Node))
        {
            var box = PickShapes.FxBounds(fx);
            objHighlight.GlobalTransform = new Transform3D(Basis.Identity.Scaled(box.Size), box.GetCenter());
            objHighlight.Visible = true;
        }
        else
            objHighlight.Visible = false;
        RefreshPickMarkers();
    }

    internal bool Selectable(EntityView e) => (!e.Dead || !e.IsNpc) && !StealthLook.HiddenFromMe(e) && !buff.OthersHidden;

    internal void UpdateRing()
    {
        if (SelectedId >= 0 && context.Entities.Views.TryGetValue(SelectedId, out var e) && Selectable(e))
        {
            var p = e.Body.Position;
            var feet = new Vector3(p.X, p.Y - e.Lift, p.Z);
            TargetSymbol.Place(selRing, feet, e.BoundRadius, scenery.GroundNormalAt(feet));
        }
        else
        {
            selRing.Visible = false;
            SelectedId = -1;
        }
    }

    internal EntityView? PickAt(Vector2 screen, out int id) => PickEntityAt(screen, ClickPickRadius, out id, out _);

    internal EntityView? PickEntityAt(Vector2 mouse, float radius, out int id, out float pixels)
    {
        if (RayPickEntity(mouse, out id) is { } hit)
        {
            pixels = 0f;
            return hit;
        }
        if (Platform.PointerUi)
        {
            pixels = radius;
            return null;
        }
        EntityView? best = null;
        EntityView? boxed = null;
        int boxedId = -1;
        float boxedDepth = float.MaxValue;
        id = -1;
        pixels = radius;
        if (context.Camera == null) return null;
        var eye = context.Camera.GlobalPosition;
        float boxPick2 = BodyPickDist * BodyPickDist;
        foreach (var kv in context.Entities.Views)
        {
            var e = kv.Value;
            if (!Selectable(e)) continue;
            var worldPosition = e.Body.GlobalPosition + Vector3.Up;
            if (context.Camera.IsPositionBehind(worldPosition)) continue;

            float depth = eye.DistanceSquaredTo(worldPosition);
            if (depth <= boxPick2 && BodyScreenRect(e) is { } rect && rect.HasPoint(mouse))
            {
                if (depth < boxedDepth) { boxedDepth = depth; boxed = e; boxedId = kv.Key; }
            }

            float d = context.Camera.UnprojectPosition(worldPosition).DistanceTo(mouse);
            if (d < pixels) { pixels = d; best = e; id = kv.Key; }
        }
        if (boxed == null) return best;
        id = boxedId;
        pixels = 0f;
        return boxed;
    }

    private Rect2? BodyScreenRect(EntityView e)
    {
        var box = SkillFxRig.EntityLocalBox(e);
        var t = e.Body.GlobalTransform;
        var lo = new Vector2(float.MaxValue, float.MaxValue);
        var hi = new Vector2(float.MinValue, float.MinValue);
        for (int c = 0; c < 8; c++)
        {
            var corner = t * (box.Position + new Vector3(
                (c & 1) != 0 ? box.Size.X : 0f,
                (c & 2) != 0 ? box.Size.Y : 0f,
                (c & 4) != 0 ? box.Size.Z : 0f));
            if (context.Camera!.IsPositionBehind(corner)) return null;
            var p = context.Camera.UnprojectPosition(corner);
            lo = new Vector2(Mathf.Min(lo.X, p.X), Mathf.Min(lo.Y, p.Y));
            hi = new Vector2(Mathf.Max(hi.X, p.X), Mathf.Max(hi.Y, p.Y));
        }
        var size = hi - lo;
        float gx = Mathf.Max(BodyPickPadPx, (BodyPickMinPx - size.X) * 0.5f);
        float gy = Mathf.Max(BodyPickPadPx, (BodyPickMinPx - size.Y) * 0.5f);
        return new Rect2(lo - new Vector2(gx, gy), size + new Vector2(gx, gy) * 2f);
    }

    internal bool TryPickAt(Vector2 mouse)
    {
        var best = PickEntityAt(mouse, ClickPickRadius, out int bestId, out float bestD);
        if (best != null && debugOverlay?.IsOpen == true && PickMode == PickKind.Effect && NearestFxPixels(mouse) < bestD)
            best = null;
        if (best != null)
        {
            bool retarget = bestId != SelectedId;
            Select(bestId, best);
            if (!retarget && !best.Attackable) TalkRequested?.Invoke(bestId);
            return true;
        }
        if (mapInteractables.TrySelectWarpGate(mouse)) return true;
        if (mapInteractables.TrySelectAnvil(mouse)) return true;
        Deselect();
        PickAt(mouse);
        return false;
    }

    internal enum PickKind { Object, Effect }

    internal PickKind PickMode { get; private set; } = PickKind.Object;

    private readonly List<MapObject> objCands = new();
    private readonly List<FxRegistry.Entry> fxCands = new();
    internal int CandIdx { get; private set; }
    private Vector2 candPixel = new(-9999f, -9999f);
    private Transform3D candCam;
    private const float CandSamePixel = 6f;

    internal int CandCount => PickMode == PickKind.Object ? objCands.Count : fxCands.Count;

    internal void SetPickKind(PickKind kind)
    {
        if (PickMode == kind) return;
        PickMode = kind;
        scenery.ClearPick();
        ClearPickCandidates();
        RefreshObjectHighlight();
        debugOverlay?.Invalidate();
    }

    private void ClearPickCandidates()
    {
        objCands.Clear();
        fxCands.Clear();
        CandIdx = 0;
        candPixel = new Vector2(-9999f, -9999f);
    }

    internal void PickAt(Vector2 mouse)
    {
        if (context.Camera == null) return;
        bool sameSpot = CandCount > 0
                        && mouse.DistanceTo(candPixel) <= CandSamePixel
                        && context.Camera.GlobalTransform.Origin.DistanceSquaredTo(candCam.Origin) < 0.0025f
                        && context.Camera.GlobalBasis.Z.DistanceSquaredTo(candCam.Basis.Z) < 1e-6f;
        if (sameSpot)
        {
            CandIdx = (CandIdx + 1) % CandCount;
        }
        else
        {
            objCands.Clear();
            fxCands.Clear();
            CandIdx = 0;
            if (PickMode == PickKind.Object) GatherObjectCandidates(mouse, objCands);
            else GatherFxCandidates(mouse, fxCands);
            candPixel = mouse;
            candCam = context.Camera.GlobalTransform;
        }
        ApplyPickCandidate();
    }

    internal void CyclePick(int step)
    {
        int n = CandCount;
        if (n == 0) return;
        CandIdx = ((CandIdx + step) % n + n) % n;
        ApplyPickCandidate();
    }

    private void ApplyPickCandidate()
    {
        if (PickMode == PickKind.Object)
        {
            scenery.PickObject(CandIdx < objCands.Count ? objCands[CandIdx] : null);
        }
        else
        {
            scenery.PickFx(CandIdx < fxCands.Count ? fxCands[CandIdx] : null);
        }
        RefreshObjectHighlight();
        debugOverlay?.Invalidate();
    }

    private void GatherObjectCandidates(Vector2 mouse, List<MapObject> into)
    {
        var from = context.Camera!.ProjectRayOrigin(mouse);
        var dir = context.Camera.ProjectRayNormal(mouse);
        var hits = new List<(MapObject Obj, float Dist, bool Exact)>();
        foreach (var o in scenery.Objects)
        {
            if (o.HitMesh is not { } mesh) continue;
            var xform = o.HitXform;
            var inv = xform.AffineInverse();
            Vector3 lo = inv * from;
            Vector3 ld = inv.Basis * dir;
            if (!PickGeometry.RayAabbEntry(lo, ld, mesh.GetAabb(), out float t)) continue;
            bool exact = RayMeshHit(mesh, lo, ld, out float tTri);
            if (exact) t = tTri;
            Vector3 worldHit = xform * (lo + ld * t);
            hits.Add((o, (worldHit - from).Length(), exact));
        }
        hits.Sort((a, b) => a.Exact != b.Exact
            ? (a.Exact ? -1 : 1)
            : a.Dist.CompareTo(b.Dist));
        foreach (var h in hits) into.Add(h.Obj);
    }

    private readonly Dictionary<Mesh, (Vector3[] Verts, int[] Idx)> pickTris = new();
    private const int PickTriCacheMax = 96;

    private bool RayMeshHit(Mesh mesh, Vector3 o, Vector3 d, out float t)
    {
        t = 0f;
        if (!pickTris.TryGetValue(mesh, out var soup))
        {
            if (pickTris.Count >= PickTriCacheMax) pickTris.Clear();
            soup = PickShapes.BuildTriSoup(mesh);
            pickTris[mesh] = soup;
        }
        if (soup.Idx.Length < 3) return false;
        float best = float.MaxValue;
        for (int i = 0; i + 2 < soup.Idx.Length; i += 3)
            if (PickShapes.RayTriangle(o, d, soup.Verts[soup.Idx[i]], soup.Verts[soup.Idx[i + 1]], soup.Verts[soup.Idx[i + 2]],
                    out float ti) && ti < best)
                best = ti;
        if (best == float.MaxValue) return false;
        t = best;
        return true;
    }

    private const float FxPickRadius = 34f;
    private const float FxPickMaxDist = 400f;

    private void GatherFxCandidates(Vector2 mouse, List<FxRegistry.Entry> into)
    {
        var cam = context.Camera!.GlobalPosition;
        var hits = new List<(FxRegistry.Entry Fx, float Dist, float Px)>();
        foreach (var fx in FxRegistry.Live)
        {
            if (fx.Node == scenery.FxPreview || fx.Node == scenery.FxDebug) continue;
            var box = PickShapes.FxBounds(fx);
            float dist = cam.DistanceTo(box.GetCenter());
            if (dist > FxPickMaxDist) continue;
            float px = FxScreenDist(box, mouse);
            if (px > FxPickRadius) continue;
            hits.Add((fx, dist, px));
        }
        hits.Sort((a, b) => Mathf.Abs(a.Dist - b.Dist) > 0.5f ? a.Dist.CompareTo(b.Dist) : a.Px.CompareTo(b.Px));
        foreach (var h in hits) into.Add(h.Fx);
    }

    private float NearestFxPixels(Vector2 mouse)
    {
        if (context.Camera == null) return float.MaxValue;
        var cam = context.Camera.GlobalPosition;
        float best = float.MaxValue;
        foreach (var fx in FxRegistry.Live)
        {
            if (fx.Node == scenery.FxPreview || fx.Node == scenery.FxDebug) continue;
            var box = PickShapes.FxBounds(fx);
            if (cam.DistanceTo(box.GetCenter()) > FxPickMaxDist) continue;
            float px = FxScreenDist(box, mouse);
            if (px <= FxPickRadius && px < best) best = px;
        }
        return best;
    }

    private float FxScreenDist(Aabb box, Vector2 mouse)
    {
        var lo = box.Position;
        var hi = box.Position + box.Size;
        Rect2? rect = null;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) != 0 ? hi.X : lo.X,
                (i & 2) != 0 ? hi.Y : lo.Y,
                (i & 4) != 0 ? hi.Z : lo.Z);
            if (context.Camera!.IsPositionBehind(corner)) continue;
            var p = context.Camera.UnprojectPosition(corner);
            rect = rect.HasValue ? rect.Value.Expand(p) : new Rect2(p, Vector2.Zero);
        }
        if (!rect.HasValue) return float.MaxValue;
        var r = rect.Value;
        float dx = Mathf.Max(Mathf.Max(r.Position.X - mouse.X, mouse.X - r.End.X), 0f);
        float dy = Mathf.Max(Mathf.Max(r.Position.Y - mouse.Y, mouse.Y - r.End.Y), 0f);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    internal void SelectNearest(bool hostile)
    {
        EntityView? best = null; int bestId = -1; float bestD = float.MaxValue;
        foreach (var kv in context.Entities.Views)
        {
            var e = kv.Value;
            if (e.Dead || e.Attackable != hostile || !Selectable(e)) continue;
            if (!hostile && !e.IsNpc) continue;
            float d = e.Body.Position.DistanceTo(context.Self.Body.Position);
            if (d < bestD) { bestD = d; best = e; bestId = kv.Key; }
        }
        if (best != null) Select(bestId, best);
    }

    internal void SelectNearestPartyMember()
    {
        EntityView? best = null; int bestId = -1; float bestD = float.MaxValue;
        foreach (var member in party.Members)
        {
            if (member.CharId == context.Self.CharId || !context.Entities.Views.TryGetValue(member.CharId, out var e) || !Selectable(e)) continue;
            float d = e.Body.Position.DistanceTo(context.Self.Body.Position);
            if (d < bestD) { bestD = d; best = e; bestId = member.CharId; }
        }
        if (best == null) return;
        Select(bestId, best);
        FaceSelectedWhenStill();
    }

    internal void FaceSelectedWhenStill()
    {
        if (context.Self.Body == null || context.Self.Dead || context.Self.Moving || skillCast.IsRootedByCast()) return;
        if (SelectedId < 0 || !context.Entities.Views.TryGetValue(SelectedId, out var e)) return;
        entityAction.TurnSelfToward(e.Body.Position);
        player.SendHeadingIfChanged(context.Self.MapXZ.X, context.Self.MapXZ.Z);
    }

    internal void Select(int id, EntityView e)
    {
        if (basicAttack.AutoAttack && basicAttack.AutoTargetId != id)
            basicAttack.StopAutoAttack();
        SelectedId = id;
        avatar.Clip = null;
        scenery.ClearPick();
        mapInteractables.WarpGates.Select(null);
        mapInteractables.Anvils.Select(null);
        ClearPickCandidates();
        RefreshObjectHighlight();
        debugOverlay?.Invalidate();
        TargetSymbol.Tint(selRingMat, e.Attackable
            ? new Color(0.95f, 0.2f, 0.2f)
            : new Color(0.25f, 0.9f, 0.3f));
        UpdateRing();
    }

    internal void Select(int id)
    {
        if (context.Entities.Views.TryGetValue(id, out var e)) Select(id, e);
    }

    internal void WithSelected(int id, Action action)
    {
        int previous = SelectedId;
        SelectedId = id;
        try { action(); }
        finally { SelectedId = previous; }
    }

    internal void Deselect()
    {
        SelectedId = -1;
        avatar.Clip = null;
        mapInteractables.WarpGates.Select(null);
        mapInteractables.Anvils.Select(null);
        basicAttack.StopAutoAttack();
    }

    internal void OnEntityHpSync(int id, int hp, int maxHp)
    {
        if (!context.Entities.Views.TryGetValue(id, out var e)) return;
        e.Hp = hp;
        e.MaxHp = maxHp;
    }

    private const string RestrictionPath = "res://assets/npcs/restriction.json";

    private static NpcRestriction? npcRestriction;

    internal static NpcRestriction Restriction => npcRestriction ??= LoadRestriction();

    private static NpcRestriction LoadRestriction()
    {
        var restriction = new NpcRestriction();
        using var f = Godot.FileAccess.Open(RestrictionPath, Godot.FileAccess.ModeFlags.Read);
        if (f == null) return restriction;
        var root = Json.ParseString(f.GetAsText()).AsGodotDictionary();
        Fill(root, "touch", restriction.Touch);
        Fill(root, "unpickable", restriction.Unpickable);
        Fill(root, "pickTransformed", restriction.PickTransformed);
        return restriction;

        static void Fill(Godot.Collections.Dictionary root, string key, HashSet<int> into)
        {
            if (root.TryGetValue(key, out var ids) && ids.VariantType == Variant.Type.Array)
                foreach (var id in ids.AsGodotArray()) into.Add(id.AsInt32());
        }
    }

    private EntityView? RayPickEntity(Vector2 mouse, out int id)
    {
        id = -1;
        if (context.Camera == null) return null;
        var from = context.Camera.ProjectRayOrigin(mouse);
        var dir = context.Camera.ProjectRayNormal(mouse);
        bool transformed = self.TransformSkill != 0;
        EntityView? best = null;
        float bestDistance = float.MaxValue;
        foreach (var (eid, e) in context.Entities.Views)
        {
            if (!Selectable(e) || !GodotObject.IsInstanceValid(e.Body)) continue;
            if (e.IsNpc && (PickBox.SkippedType(e.NpcType) || !Restriction.Pickable(e.NpcId, transformed))) continue;
            float distance = from.DistanceSquaredTo(e.Body.GlobalPosition);
            if (distance >= bestDistance) continue;
            var inverse = e.Body.GlobalTransform.AffineInverse();
            if (PickBox.Hit(inverse * from, inverse.Basis * dir, EntityPickBox(e)) == null) continue;
            best = e;
            id = eid;
            bestDistance = distance;
        }
        return best;
    }

    private const float ShapeMatchDistance = 4f;

    private Dictionary<int, List<MapObject>>? objectsByNpc;

    private void OnObjectsReset()
    {
        objectsByNpc = null;
        ClearPickCandidates();
    }

    private Aabb EntityPickBox(EntityView e)
    {
        if (e.ObjectType == NpcTypes.ObjectType.MapObject && ShapeBox(e) is { } shape) return shape;
        if (e.PickBox is { } cached) return cached;
        var box = JointBox(e) ?? SkillFxRig.EntityLocalBox(e);
        e.PickBox = box;
        return box;
    }

    private Aabb? ShapeBox(EntityView e)
    {
        if (objectsByNpc == null)
        {
            objectsByNpc = new Dictionary<int, List<MapObject>>();
            foreach (var o in scenery.Objects)
            {
                if (o.NpcId == 0) continue;
                if (!objectsByNpc.TryGetValue(o.NpcId, out var list)) objectsByNpc[o.NpcId] = list = new List<MapObject>();
                list.Add(o);
            }
        }
        if (!objectsByNpc.TryGetValue(e.NpcId, out var shapes)) return null;
        var at = new Vector2(e.MapX, e.MapZ);
        foreach (var o in shapes)
        {
            if (o.Mi == null || !GodotObject.IsInstanceValid(o.Mi)) continue;
            if (new Vector2(o.MapPos.X, o.MapPos.Z).DistanceTo(at) > ShapeMatchDistance) continue;
            return (e.Body.GlobalTransform.AffineInverse() * o.Mi.GlobalTransform) * o.Mi.GetAabb();
        }
        return null;
    }

    private static Aabb? JointBox(EntityView e)
    {
        if (NodeSearch.FindFirst<Skeleton3D>(e.Body) is not { } skeleton || skeleton.GetBoneCount() == 0) return null;
        var toBody = e.Body.GlobalTransform.AffineInverse() * skeleton.GlobalTransform;
        var joints = new List<Vector3>(skeleton.GetBoneCount());
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
            joints.Add(toBody * skeleton.GetBoneGlobalRest(i).Origin);
        return PickBox.FromJoints(joints);
    }

    private Node3D? pickMarkerRoot;

    private readonly List<(Label3D Tag, MeshInstance3D Box)> pickMarkers = new();

    private ArrayMesh? pickBoxMesh;
    private StandardMaterial3D? pickBoxMat;
    private double pickMarkerAccum;

    internal void PickMarkerTick(double delta)
    {
        pickMarkerAccum += delta;
        if (pickMarkerAccum < PickMarkerInterval) return;
        pickMarkerAccum = 0;
        RefreshPickMarkers();
    }

    internal const double PickMarkerInterval = 0.15;
    private const int MaxPickMarkers = 64;
    private const float PickMarkerDist = 90f;
    private const float PickMarkerMargin = 60f;

    private static readonly Color ObjMarkerColor = new(1f, 0.88f, 0.45f);
    private static readonly Color FxMarkerColor = new(0.45f, 0.92f, 1f);
    private static readonly Color PickedMarkerColor = new(1f, 0.35f, 0.3f);

    private readonly List<(float Dist, Vector3 At, Transform3D Box, string Text, bool Picked)> markerScratch = new();

    internal void RefreshPickMarkers()
    {
        if (context.Camera == null) return;
        markerScratch.Clear();
        if (debugOverlay?.IsOpen == true)
        {
            markerRect = context.Root.GetViewport().GetVisibleRect().Grow(PickMarkerMargin);
            if (PickMode == PickKind.Object) CollectObjectMarkers();
            else CollectFxMarkers();
            markerScratch.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        }
        if (markerScratch.Count == 0 && pickMarkerRoot == null) return;

        EnsurePickMarkerRoot();
        var tint = PickMode == PickKind.Object ? ObjMarkerColor : FxMarkerColor;
        if (pickBoxMat != null) pickBoxMat.AlbedoColor = new Color(tint, 0.45f);

        int n = Mathf.Min(markerScratch.Count, MaxPickMarkers);
        for (int i = 0; i < n; i++)
        {
            var (_, at, box, text, picked) = markerScratch[i];
            var (tag, mesh) = PickMarker(i);
            tag.Text = text;
            tag.Modulate = picked ? PickedMarkerColor : tint;
            tag.GlobalPosition = at;
            tag.Visible = true;
            mesh.GlobalTransform = box;
            mesh.Visible = !picked;
        }
        for (int i = n; i < pickMarkers.Count; i++)
        {
            pickMarkers[i].Tag.Visible = false;
            pickMarkers[i].Box.Visible = false;
        }
    }

    private void CollectObjectMarkers()
    {
        var cam = context.Camera!.GlobalPosition;
        for (int i = 0; i < scenery.Objects.Count; i++)
        {
            var o = scenery.Objects[i];
            if (o.HitMesh is not { } mesh) continue;
            var xf = o.HitXform;
            var aabb = mesh.GetAabb();
            var centre = xf * aabb.GetCenter();
            float dist = cam.DistanceTo(centre);
            if (dist > PickMarkerDist || !OnScreen(centre)) continue;
            float half = Mathf.Abs((xf.Basis * new Vector3(0, aabb.Size.Y * 0.5f, 0)).Y);
            markerScratch.Add((
                dist,
                centre + Vector3.Up * (half + 0.6f),
                xf * new Transform3D(Basis.Identity.Scaled(aabb.Size), aabb.GetCenter()),
                $"#{i} {ModelNodes.SafeModelName(o.Name)}",
                o == scenery.PickedObject));
        }
    }

    private void CollectFxMarkers()
    {
        var cam = context.Camera!.GlobalPosition;
        BuildFxOwnerLookup();
        foreach (var fx in FxRegistry.Live)
        {
            if (fx.Node == scenery.FxPreview || fx.Node == scenery.FxDebug) continue;
            var box = PickShapes.FxBounds(fx);
            var centre = box.GetCenter();
            float dist = cam.DistanceTo(centre);
            if (dist > PickMarkerDist || !OnScreen(centre)) continue;
            markerScratch.Add((
                dist,
                new Vector3(centre.X, box.Position.Y + box.Size.Y + 0.35f, centre.Z),
                new Transform3D(Basis.Identity.Scaled(box.Size), centre),
                FxLabel(fx),
                fx.Node == scenery.PickedFx?.Node));
        }
    }

    private readonly Dictionary<Node3D, string> fxOwners = new();

    internal void BuildFxOwnerLookup()
    {
        fxOwners.Clear();
        foreach (var kv in context.Entities.Views)
            if (kv.Value.Body != null)
                fxOwners[kv.Value.Body] = kv.Value.Name.Length > 0 ? kv.Value.Name : $"entity {kv.Key}";
        if (context.Self.Body != null) fxOwners[context.Self.Body] = "you";
    }

    private string FxLabel(FxRegistry.Entry fx)
    {
        if (scenery.MapFxIndex.TryGetValue(fx.Node, out int idx)) return $"#{idx} {fx.Name}";
        string? owner = FxOwnerName(fx.Node);
        return owner != null ? $"{fx.Name} → {owner}" : fx.Name;
    }

    internal string? FxOwnerName(Node3D node)
    {
        for (Node? n = node; n != null && n != context.Root; n = n.GetParent())
            if (n is Node3D n3 && fxOwners.TryGetValue(n3, out string? name)) return name;
        return null;
    }

    private Rect2 markerRect;

    private bool OnScreen(Vector3 worldPosition) =>
        !context.Camera!.IsPositionBehind(worldPosition) && markerRect.HasPoint(context.Camera.UnprojectPosition(worldPosition));

    private void EnsurePickMarkerRoot()
    {
        if (pickMarkerRoot != null) return;
        pickMarkerRoot = new Node3D { Name = "PickMarkers" };
        context.Root.AddChild(pickMarkerRoot);
        pickBoxMesh = PickShapes.BuildWireBoxMesh();
        pickBoxMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(ObjMarkerColor, 0.45f),
        };
    }

    private (Label3D Tag, MeshInstance3D Box) PickMarker(int i)
    {
        while (pickMarkers.Count <= i)
        {
            var tag = new Label3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 48,
                PixelSize = 0.0032f,
                OutlineSize = 10,
                NoDepthTest = true,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
                Visible = false,
            };
            pickMarkerRoot!.AddChild(tag);
            var box = new MeshInstance3D
            {
                Mesh = pickBoxMesh,
                MaterialOverride = pickBoxMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            pickMarkerRoot.AddChild(box);
            pickMarkers.Add((tag, box));
        }
        return pickMarkers[i];
    }

}
