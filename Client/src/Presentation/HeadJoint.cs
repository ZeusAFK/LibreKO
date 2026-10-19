using Godot;

namespace LibreKO.Presentation;

public sealed class HeadJoint
{
    public Skeleton3D? Skeleton;
    public int Bone = -1;
    public float RestHeight;
    public bool Sought;
    private ulong plateId;
    private bool plateIsRoot;

    public bool Follows => Bone >= 0;

    public bool IsPlateRoot(Node3D plates)
    {
        ulong id = plates.GetInstanceId();
        if (id == plateId) return plateIsRoot;
        plateId = id;
        plateIsRoot = plates.Name == ModelNodes.PlateRootName;
        return plateIsRoot;
    }

    public void Forget()
    {
        Skeleton = null;
        Bone = -1;
        Sought = false;
    }
}
