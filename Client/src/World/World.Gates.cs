using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private const int ObjectEventGate = 1;
    private const float GateMatchRadius = 4f;

    private sealed class GateCollider
    {
        public Vector2 KoXZ;
        public StaticBody3D Body = null!;
    }

    private readonly List<GateCollider> _gateColliders = new();
    private readonly List<(Vector2 KoXZ, bool Open)> _gateStates = new();

    private void AddGateCollider(ObjInfo o, Node3D placed)
    {
        if (o.EventType != ObjectEventGate || _objRoot == null) return;
        var faces = new List<Vector3>();
        CollectGateFaces(placed, faces);
        if (faces.Count == 0) return;

        var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
        shape.SetFaces(faces.ToArray());
        var body = new StaticBody3D { Name = $"Gate{o.EventId}", CollisionLayer = 0, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = shape });
        _objRoot.AddChild(body);

        var gate = new GateCollider { KoXZ = new Vector2(o.KoPos.X, o.KoPos.Z), Body = body };
        _gateColliders.Add(gate);
        foreach (var (at, open) in _gateStates)
            if (at.DistanceTo(gate.KoXZ) <= GateMatchRadius) SetGateClosed(gate, !open);
    }

    private static void CollectGateFaces(Node node, List<Vector3> faces)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } mi)
        {
            var xform = mi.GlobalTransform;
            foreach (var v in mesh.GetFaces()) faces.Add(xform * v);
        }
        foreach (var child in node.GetChildren()) CollectGateFaces(child, faces);
    }

    private void NoteGateState(float koX, float koZ, bool open)
    {
        var at = new Vector2(koX, koZ);
        _gateStates.RemoveAll(s => s.KoXZ.DistanceTo(at) <= GateMatchRadius);
        _gateStates.Add((at, open));
        foreach (var gate in _gateColliders)
            if (gate.KoXZ.DistanceTo(at) <= GateMatchRadius) SetGateClosed(gate, !open);
    }

    private static void SetGateClosed(GateCollider gate, bool closed)
    {
        if (GodotObject.IsInstanceValid(gate.Body))
            gate.Body.CollisionLayer = closed ? WorldCollisionLayer : 0u;
    }
}
