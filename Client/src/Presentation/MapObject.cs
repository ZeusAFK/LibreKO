using Godot;

namespace LibreKO.Presentation;

internal sealed class MapObject
{
    public MeshInstance3D? Mi;
    public Mesh? InstMesh;
    public Transform3D InstXform;
    public bool Built;
    public string Name = "";
    public Vector3 MapPos;
    public Quaternion MapRot;
    public Vector3 Scale;
    public Vector3 Origin;
    public int EventId;
    public ObjectEventType EventType;
    public int Belong;
    public int NpcId;

    public Mesh? HitMesh => Mi?.Mesh ?? InstMesh;
    public Transform3D HitXform => Mi != null ? Mi.GlobalTransform : InstXform;
}
