using Godot;
using LibreKO.Domain;

namespace LibreKO;

public static class WeaponMount
{
    private const int ShieldKind = 60;
    private const string RightWrist = "rightwrist";
    private const string LeftWrist = "leftwrist";
    private const string LeftForearm = "leftelbow";

    public static int Bone(Skeleton3D skel, bool right, int itemId)
    {
        if (!right && ItemData.Get(itemId) is { Kind: ShieldKind })
        {
            int forearm = Find(skel, LeftForearm);
            if (forearm >= 0) return forearm;
        }
        int wrist = Find(skel, right ? RightWrist : LeftWrist);
        if (wrist < 0) return -1;
        var kids = skel.GetBoneChildren(wrist);
        return kids.Length > 0 ? kids[0] : wrist;
    }

    private static int Find(Skeleton3D skel, string key)
    {
        for (int i = 0; i < skel.GetBoneCount(); i++)
            if (skel.GetBoneName(i).Replace(" ", "").ToLowerInvariant() == key) return i;
        return -1;
    }
}
