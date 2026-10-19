using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public readonly record struct PlugPose(Vector3 Position, Quaternion Rotation, Vector3 Scale);

public readonly record struct HandWearer(int Race, bool Npc = false, int NpcId = 0, float NpcScale = 1f,
                                         int PlugSlots = NpcPlugSlots.Unlimited)
{
    public static readonly HandWearer TransformedLook = new(WeaponMount.NoRace);
}

public static class WeaponMount
{
    public const int NoRace = 0;
    public const int RaceBlockVersion = 2;
    public const float HandReferenceHeight = 1.8f;
    public const float GiantHandReferenceHeight = 4.5f;

    private const int DefaultBlock = 0;
    private const string RightWrist = "rightwrist";
    private const string LeftWrist = "leftwrist";
    private const string LeftForearm = "leftelbow";
    private const string RightArm = "rightarm";
    private const string LeftArm = "leftarm";

    private static readonly int[] GiantHandReferenceNpcIds = { 10620, 9839 };

    private static readonly int[][] RaceBlockRaces =
    {
        Array.Empty<int>(),
        new[] { PlayerRig.ArkTuarek },
        new[] { PlayerRig.Tuarek },
        new[] { PlayerRig.WrinkleTuarek },
        new[] { PlayerRig.PuriTuarek, PlayerRig.PuriTuarekMagician, PlayerRig.ElMoradWoman },
        new[] { PlayerRig.Barbarian },
        new[] { PlayerRig.ElMoradMan },
        new[] { PlayerRig.KarusKurian, PlayerRig.ElMoradKurian },
    };

    public static Transform3D PlugLocal(Vector3 position, Quaternion rotation, Vector3 scale) =>
        new(new Basis(rotation) * Basis.FromScale(scale), position * scale);

    public static Transform3D PlugLocal(PlugPose pose) => PlugLocal(pose.Position, pose.Rotation, pose.Scale);

    public static int RaceBlock(int race)
    {
        for (int block = 0; block < RaceBlockRaces.Length; block++)
            if (Array.IndexOf(RaceBlockRaces[block], race) >= 0) return block;
        return DefaultBlock;
    }

    public static float HandScale(HandWearer wearer, float bindHeight)
    {
        if (!wearer.Npc) return bindHeight / HandReferenceHeight;
        if (Array.IndexOf(GiantHandReferenceNpcIds, wearer.NpcId) >= 0) return bindHeight / GiantHandReferenceHeight;
        return Mathf.IsEqualApprox(wearer.NpcScale, 1f) ? 1f : bindHeight / HandReferenceHeight;
    }

    public static PlugPose HandPose(PlugPose plug, int version, IReadOnlyList<PlugPose> raceBlocks, bool right,
                                    HandWearer wearer, float bindHeight)
    {
        if (version >= RaceBlockVersion && raceBlocks.Count > 0)
        {
            int block = right ? DefaultBlock : RaceBlock(wearer.Race);
            return raceBlocks[block < raceBlocks.Count ? block : DefaultBlock];
        }
        float uniform = plug.Scale.Y * HandScale(wearer, bindHeight);
        return plug with { Scale = new Vector3(uniform, uniform, uniform) };
    }

    public static Transform3D HandPlugLocal(PlugPose plug, int version, IReadOnlyList<PlugPose> raceBlocks, bool right,
                                            HandWearer wearer, float bindHeight) =>
        PlugLocal(HandPose(plug, version, raceBlocks, right, wearer, bindHeight));

    public static float BindHeight(Skeleton3D skel)
    {
        var joints = new List<Vector3>(skel.GetBoneCount());
        for (int i = 0; i < skel.GetBoneCount(); i++)
            joints.Add(skel.GetBoneGlobalRest(i).Origin);
        return joints.Count == 0 ? HandReferenceHeight : PickBox.FromJoints(joints).End.Y;
    }

    public static int Bone(Skeleton3D skel, bool right, int itemId)
    {
        if (!right && ItemData.Get(itemId) is { Kind: ItemData.Kind.Shield })
        {
            int forearm = Find(skel, LeftForearm);
            if (forearm >= 0) return forearm;
        }
        int wrist = Find(skel, right ? RightWrist : LeftWrist);
        if (wrist < 0) return ArmChainEnd(skel, right ? RightArm : LeftArm);
        var kids = skel.GetBoneChildren(wrist);
        return kids.Length > 0 ? kids[0] : wrist;
    }

    private static int ArmChainEnd(Skeleton3D skel, string armKey)
    {
        int bone = Find(skel, armKey);
        if (bone < 0) return -1;
        while (DeepestChild(skel, bone) is var next and >= 0)
            bone = next;
        return bone;
    }

    private static int DeepestChild(Skeleton3D skel, int bone)
    {
        int best = -1, bestDepth = -1;
        foreach (int child in skel.GetBoneChildren(bone))
        {
            int depth = ChainDepth(skel, child);
            if (depth > bestDepth) { best = child; bestDepth = depth; }
        }
        return best;
    }

    private static int ChainDepth(Skeleton3D skel, int bone)
    {
        int deepest = 0;
        foreach (int child in skel.GetBoneChildren(bone))
            deepest = Mathf.Max(deepest, ChainDepth(skel, child));
        return deepest + 1;
    }

    private static int Find(Skeleton3D skel, string key)
    {
        for (int i = 0; i < skel.GetBoneCount(); i++)
            if (skel.GetBoneName(i).Replace(" ", "").ToLowerInvariant() == key) return i;
        return -1;
    }
}
