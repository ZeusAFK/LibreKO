using Godot;

namespace LibreKO.Presentation;

internal static class GateRig
{
    private const float LeverTiltAngle = 0.7853982f;
    internal const float GateMatchRadius = 4f;
    private const string GateWingPrefix = "gate_wing";
    private const float GateSwingRadiansPerSecond = 1f;
    private const float GateOpenAngle = 1.3962634f;
    private const float Gate2OpenAngle = 1.5707964f;

    internal sealed class GateCollider
    {
        public Vector2 MapXZ;
        public StaticBody3D? Body;
        public ObjectEventType EventType;
        public readonly List<Node3D> Wings = new();
        public Tween? Swing;
    }

    internal static void CollectGateFaces(Node node, List<Vector3> faces)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } mi)
        {
            var xform = mi.GlobalTransform;
            foreach (var v in mesh.GetFaces()) faces.Add(xform * v);
        }
        foreach (var child in node.GetChildren()) CollectGateFaces(child, faces);
    }

    internal static void CollectGateWings(Node node, List<Node3D> wings)
    {
        if (node is Node3D wing && wing.Name.ToString().StartsWith(GateWingPrefix, System.StringComparison.Ordinal))
            wings.Add(wing);
        foreach (var child in node.GetChildren()) CollectGateWings(child, wings);
    }

    internal static void SetGateClosed(GateCollider gate, bool closed)
    {
        if (gate.Body != null && GodotObject.IsInstanceValid(gate.Body))
            gate.Body.CollisionLayer = closed ? CollisionLayers.World : 0u;
        SwingGateWings(gate, closed);
    }

    private static void SwingGateWings(GateCollider gate, bool closed)
    {
        if (gate.Wings.Count == 0 || !GodotObject.IsInstanceValid(gate.Wings[0])) return;
        if (gate.Swing != null && gate.Swing.IsValid()) gate.Swing.Kill();
        gate.Swing = gate.Wings[0].CreateTween().SetParallel();
        for (int i = 0; i < gate.Wings.Count; i++)
        {
            var wing = gate.Wings[i];
            var target = GateWingRotation(wing.Rotation, gate.EventType, i, closed);
            float turn = (target - wing.Rotation).Length();
            if (turn <= 0f) continue;
            gate.Swing.TweenProperty(wing, "rotation", target, turn / GateSwingRadiansPerSecond);
        }
    }

    private static Vector3 GateWingRotation(Vector3 current, ObjectEventType eventType, int wingIndex, bool closed)
    {
        if (eventType is ObjectEventType.GateLever or ObjectEventType.LeverFlip)
        {
            float tilt = closed ? LeverTiltAngle : -LeverTiltAngle;
            return new Vector3(wingIndex % 2 == 0 ? tilt : -tilt, current.Y, current.Z);
        }
        float open = eventType == ObjectEventType.Gate2 ? Gate2OpenAngle : GateOpenAngle;
        float angle = closed ? 0f : wingIndex % 2 == 0 ? -open : open;
        return eventType == ObjectEventType.Gate2
            ? new Vector3(current.X, current.Y, angle)
            : new Vector3(current.X, angle, current.Z);
    }

    internal static void PoseGateWings(Node root, int eventType, bool closed)
    {
        var wings = new List<Node3D>();
        CollectGateWings(root, wings);
        wings.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        for (int i = 0; i < wings.Count; i++)
            wings[i].Rotation = GateWingRotation(wings[i].Rotation, (ObjectEventType)eventType, i, closed);
    }
}
