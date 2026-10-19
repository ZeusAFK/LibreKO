using Godot;

namespace LibreKO.Presentation;

internal static class EntityRig
{
    private const float RunThreshold = MoveSpeeds.Walk * 2f;

    internal static float RunThresholdOf(EntityView e) => e.IsNpc ? RunThreshold * e.BoundRadius : RunThreshold;

    internal const float MoveArriveEps = 0.02f;

    internal static EntityInfo EntityInfoOf(int id, EntityView e) => new(id, e.NpcId, e.NpcType, e.IsNpc, e.IsMonster, e.Attackable, e.Dead, e.Statue, e.Nation, e.Name, e.Body.Position);

    internal static void SyncEntityPlates(EntityView e, bool near)
    {
        if (e.PlateRoot == null || near == e.PlateNear) return;
        e.PlateNear = near;
        e.PlateRoot.TopLevel = !near;
        if (near) e.PlateRoot.Transform = Transform3D.Identity;
    }

    internal static void CancelEntityPosture(EntityView e, double now)
    {
        if (e.ActionRank != ModelAnimation.ActionRankPosture || now >= e.ActionUntil) return;
        e.ActionUntil = 0;
        e.ActionClip = null;
        e.Clip = null;
    }

    internal static void FaceEntity(EntityView e, float yawDegrees, bool immediate)
    {
        e.TargetYaw = yawDegrees;
        if (immediate) SetEntityYaw(e, yawDegrees);
    }

    private static void SetEntityYaw(EntityView e, float yawDegrees)
    {
        e.Yaw = yawDegrees;
        e.Body.RotationDegrees = new Vector3(0, yawDegrees, 0);
    }

    internal static void TickEntityFacing(EntityView e, float dt)
    {
        float current = e.Yaw ?? e.Body.RotationDegrees.Y;
        if (current == e.TargetYaw) { e.Yaw = current; return; }
        SetEntityYaw(e, Turning.Toward(current, e.TargetYaw, dt));
    }

    internal static bool EntityMoving(EntityView e) => EntityMoving(e, e.Body.Position);

    internal static bool EntityMoving(EntityView e, Vector3 pos) =>
        !e.Dead && e.HasTarget && e.Speed > 0.1f
        && pos.DistanceTo(e.Target) > MoveArriveEps;

    internal const float EntityRenderDist = 160f;
    private const float EntityRenderFade = 30f;

    internal static void ApplyEntityRenderCost(Node3D body)
    {
        foreach (var mi in ModelNodes.FindAll<MeshInstance3D>(body))
        {
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mi.VisibilityRangeEnd = EntityRenderDist * Config.ViewDistance;
            mi.VisibilityRangeEndMargin = EntityRenderFade;
            mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            SkinShare.FixBounds(mi);
        }
    }

    internal static string DressKey(int race, int face, int[]? gear, int hair, bool hideHelmet, bool hideCostume) =>
        $"{race}:{face}:{hair}:{hideHelmet}:{hideCostume}:{(gear == null ? "" : string.Join(',', gear))}";

    internal static int GearIndexForLookSlot(int lookSlot)
        => System.Array.IndexOf(InventoryConstants.VisualSlots, lookSlot);

    internal static string TitleTextOf(int titleId, int zone) =>
        titleId != 0 && StatePlates.TitleShownIn(zone) && AchievementData.TitleOf(titleId) is { } title ? title.Name : "";

    internal static (float Footprint, float Bound) BodyRadii(Node3D body)
    {
        Aabb? merged = null;

        void Walk(Node n)
        {
            if (n is Cape or BoneAttachment3D) return;
            if (n is MeshInstance3D { Mesh: not null } mi)
            {
                var a = mi.GlobalTransform * mi.GetAabb();
                if (a.Size != Vector3.Zero)
                    merged = merged.HasValue ? merged.Value.Merge(a) : a;
            }
            foreach (var child in n.GetChildren()) Walk(child);
        }

        Walk(body);
        if (!merged.HasValue) return (0.9f, 1.0f);
        var s = merged.Value.Size;
        return (Mathf.Clamp(Mathf.Max(s.X, s.Z) * 0.5f, 0.45f, 6f),
                Mathf.Clamp(s.Length() * 0.5f, 0.7f, 10f));
    }

    internal static void CollectFaces(Node node, List<Vector3> faces)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } mi)
        {
            var xform = mi.GlobalTransform;
            foreach (var v in mesh.GetFaces()) faces.Add(xform * v);
        }
        foreach (var child in node.GetChildren()) CollectFaces(child, faces);
    }
}
