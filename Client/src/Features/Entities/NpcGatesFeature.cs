using Godot;

namespace LibreKO.Features.Entities;

internal sealed class NpcGatesFeature : IFeature
{
    private readonly IWorldContext context;

    public NpcGatesFeature(IWorldContext context)
    {
        this.context = context;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    private const float GateMinThickness = 1f;

    internal static bool IsBreakableGate(EntitySnapshot info) =>
        info.IsNpc && info.IsMonster && info.NpcType == NpcTypes.Gate;

    internal static StaticBody3D? AttachGateBlocker(Node3D body)
    {
        if (LocalMeshBounds(body, body) is not { } bounds) return null;

        var world = body.GlobalTransform;
        var scale = world.Basis.Scale;
        var size = bounds.Size * scale;
        size = new Vector3(Mathf.Max(size.X, GateMinThickness), size.Y, Mathf.Max(size.Z, GateMinThickness));

        var blocker = new StaticBody3D { TopLevel = true, CollisionLayer = CollisionLayers.Blocker, CollisionMask = 0 };
        blocker.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(blocker);
        blocker.GlobalTransform = new Transform3D(world.Basis.Orthonormalized(), world * bounds.GetCenter());
        return blocker;
    }

    internal static Aabb? LocalMeshBounds(Node node, Node3D root)
    {
        Aabb? merged = null;
        if (node is MeshInstance3D { Mesh: not null } mi)
        {
            var box = root.GlobalTransform.AffineInverse() * mi.GlobalTransform * mi.GetAabb();
            merged = box;
        }
        foreach (var child in node.GetChildren())
        {
            if (LocalMeshBounds(child, root) is not { } childBox) continue;
            merged = merged is { } m ? m.Merge(childBox) : childBox;
        }
        return merged;
    }

    internal static void RefreshGateBlocker(EntityView e)
    {
        if (e.GateBlocker == null || !GodotObject.IsInstanceValid(e.GateBlocker)) return;
        e.GateBlocker.CollisionLayer = e.Dead ? 0u : CollisionLayers.Blocker;
        if (GodotObject.IsInstanceValid(e.Body)) e.Body.Visible = !e.Dead;
    }
}
