using Godot;

namespace LibreKO;

public partial class World
{
    private const string HeadBoneName = "Head";
    private const string HeadBoneSuffix = "head";
    private const float PlateFollowEpsilon = 0.005f;
    private const string PlateBaseYMeta = "plate_base_y";
    private const string WornPlateYMeta = "worn_plate_y";

    private sealed class HeadJoint
    {
        public Skeleton3D? Skeleton;
        public int Bone = -1;
        public float RestHeight;
        public bool Sought;
        private ulong _plateId;
        private bool _plateIsRoot;

        public bool Follows => Bone >= 0;

        public bool IsPlateRoot(Node3D plates)
        {
            ulong id = plates.GetInstanceId();
            if (id == _plateId) return _plateIsRoot;
            _plateId = id;
            _plateIsRoot = plates.Name == PlateRootName;
            return _plateIsRoot;
        }

        public void Forget()
        {
            Skeleton = null;
            Bone = -1;
            Sought = false;
        }
    }

    private readonly HeadJoint _selfHead = new();

    private static void FollowHeadJoint(Node3D body, Node3D? plates, HeadJoint joint)
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

    private static void SeekHeadJoint(Node3D body, HeadJoint joint)
    {
        joint.Forget();
        joint.Sought = true;
        var skeleton = FindFirst<Skeleton3D>(body.GetNodeOrNull<Node3D>(TransformNodeName) ?? body);
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

    private static void AdoptWornPlate(Node3D worn)
    {
        if (worn.GetNodeOrNull<Node3D>(PlateRootName) is not { } plates) return;
        if (FindFirst<Label3D>(plates) is { } tag) worn.SetMeta(WornPlateYMeta, tag.Position.Y);
        worn.RemoveChild(plates);
        plates.QueueFree();
    }

    private void FitPlateToLook(int charId)
    {
        Label3D? tag;
        HeadJoint joint;
        if (charId == _myId)
        {
            SelfPlateRoot();
            tag = _selfNameTag;
            joint = _selfHead;
        }
        else if (_ents.TryGetValue(charId, out var e))
        {
            tag = e.NameTag;
            joint = e.Head;
        }
        else return;
        if (TransformHost(charId) is not { } host || tag == null || !GodotObject.IsInstanceValid(tag)) return;
        if (!tag.HasMeta(PlateBaseYMeta)) tag.SetMeta(PlateBaseYMeta, tag.Position.Y);
        float y = tag.GetMeta(PlateBaseYMeta).AsSingle();
        if (host.GetNodeOrNull<Node3D>(TransformNodeName) is { } worn && worn.HasMeta(WornPlateYMeta))
            y = worn.GetMeta(WornPlateYMeta).AsSingle();
        var at = new Vector3(tag.Position.X, y, tag.Position.Z);
        if (PlateOf(charId) is { } plate && plate.Name == tag) plate.MoveTo(at);
        else tag.Position = at;
        joint.Forget();
    }

    private Node3D? SelfPlateRoot()
    {
        if (_selfNameTag == null || !GodotObject.IsInstanceValid(_selfNameTag))
        {
            _selfNameTagFound = true;
            _selfNameTag = _self != null ? FindFirst<Label3D>(_self) : null;
        }
        return _selfNameTag?.GetParent() as Node3D;
    }
}
