using Godot;

namespace LibreKO.Presentation;

internal static class PlateFollow
{
    private const string HeadBoneName = "Head";
    private const string HeadBoneSuffix = "head";
    private const float PlateFollowEpsilon = 0.005f;
    internal const string PlateBaseYMeta = "plate_base_y";
    internal const string WornPlateYMeta = "worn_plate_y";

    internal static void FollowHeadJoint(Node3D body, Node3D? plates, HeadJoint joint)
    {
        if (plates == null || !GodotObject.IsInstanceValid(plates) || plates.TopLevel
            || !joint.IsPlateRoot(plates)) return;
        if (!joint.Sought || (joint.Skeleton != null
            && (!GodotObject.IsInstanceValid(joint.Skeleton) || !joint.Skeleton.IsInsideTree())))
            SeekHeadJoint(body, joint);
        if (!joint.Follows) return;

        var skeleton = joint.Skeleton!;
        float lift = HeightInBody(body, skeleton, skeleton.GetBoneGlobalPose(joint.Bone)) - joint.RestHeight;
        lift = Mathf.Max(lift, -joint.RestHeight);
        var at = plates.Position;
        if (Mathf.Abs(at.Y - lift) < PlateFollowEpsilon) return;
        plates.Position = new Vector3(at.X, lift, at.Z);
    }

    internal static void SeekHeadJoint(Node3D body, HeadJoint joint)
    {
        joint.Forget();
        joint.Sought = true;
        var skeleton = NodeSearch.FindFirst<Skeleton3D>(body.GetNodeOrNull<Node3D>(WornLook.TransformNodeName) ?? body);
        if (skeleton == null) return;
        int bone = HeadBoneOf(skeleton);
        if (bone < 0) return;
        joint.Skeleton = skeleton;
        joint.Bone = bone;
        joint.RestHeight = HeightInBody(body, skeleton, skeleton.GetBoneGlobalRest(bone));
    }

    private static int HeadBoneOf(Skeleton3D skeleton)
    {
        int exact = skeleton.FindBone(HeadBoneName);
        if (exact >= 0) return exact;
        int found = -1;
        for (int i = 0; i < skeleton.GetBoneCount(); i++)
            if (skeleton.GetBoneName(i).ToLowerInvariant().EndsWith(HeadBoneSuffix)) found = i;
        return found;
    }

    private static float HeightInBody(Node3D body, Skeleton3D skeleton, Transform3D pose)
    {
        var chain = Transform3D.Identity;
        for (Node? node = skeleton; node != null && node != body; node = node.GetParent())
            if (node is Node3D spatial) chain = spatial.Transform * chain;
        return (chain * pose.Origin).Y;
    }

    internal static void AdoptWornPlate(Node3D worn)
    {
        if (worn.GetNodeOrNull<Node3D>(ModelNodes.PlateRootName) is not { } plates) return;
        if (NodeSearch.FindFirst<Label3D>(plates) is { } tag) worn.SetMeta(WornPlateYMeta, tag.Position.Y);
        worn.RemoveChild(plates);
        plates.QueueFree();
    }
}
