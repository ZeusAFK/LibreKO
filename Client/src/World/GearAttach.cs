using Godot;

namespace LibreKO;

public static class GearAttach
{
    public static int Bone(int plugJoint, int boneCount, int fallbackBone) =>
        (uint)plugJoint < (uint)boneCount ? plugJoint : fallbackBone;

    public static int WeaponBone(Skeleton3D skeleton, int plugJoint, bool right) =>
        Bone(plugJoint, skeleton.GetBoneCount(), HandBone(skeleton, right));

    public static int HandBone(Skeleton3D skeleton, bool right)
    {
        string wristKey = right ? "rightwrist" : "leftwrist";
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
        {
            if (skeleton.GetBoneName(i).Replace(" ", "").ToLower() != wristKey) continue;
            var kids = skeleton.GetBoneChildren(i);
            return kids.Length > 0 ? kids[0] : i;
        }
        return -1;
    }

    public static Vector3 LimbEnd(Skeleton3D skeleton, int bone)
    {
        if ((uint)bone >= (uint)skeleton.GetBoneCount()) return Vector3.Zero;
        int child = -1;
        foreach (int kid in skeleton.GetBoneChildren(bone))
        {
            string name = skeleton.GetBoneName(kid).ToString().Replace(" ", "").ToLowerInvariant();
            if (name.Contains("wrist")) return skeleton.GetBoneRest(kid).Origin;
            if (child < 0 && !name.StartsWith("end")) child = kid;
        }
        return child < 0 ? Vector3.Zero : skeleton.GetBoneRest(child).Origin;
    }

    public static Vector3 SeatOnLimb(Vector3 plugPosition, Vector3 limbEnd)
    {
        float limbLengthSquared = limbEnd.LengthSquared();
        if (limbLengthSquared < 1e-8f) return plugPosition;
        float limbLength = Mathf.Sqrt(limbLengthSquared);
        float projection = plugPosition.Dot(limbEnd) / limbLength;
        if (projection <= limbLength) return plugPosition;
        return plugPosition - limbEnd * ((projection - limbLength) / limbLength);
    }
}
