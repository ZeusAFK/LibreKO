using Godot;

namespace LibreKO;

public sealed partial class Flinch : SkeletonModifier3D
{
    private static readonly string[] LegRoots = { "LeftHip", "RightHip" };

    private bool[] _upper = System.Array.Empty<bool>();
    private int[] _bones = System.Array.Empty<int>();
    private Quaternion[] _target = System.Array.Empty<Quaternion>();
    private int _count;
    private int _clip = -1;
    private ulong _startMsec;
    private double _ramp;

    public static Flinch? Attach(Node3D body)
    {
        var skel = FindSkeleton(body);
        if (skel == null) return null;
        var upper = UpperMask(skel, out int room);
        if (upper == null) return null;
        var layer = new Flinch
        {
            Name = "flinch",
            Active = false,
            _upper = upper,
            _bones = new int[room],
            _target = new Quaternion[room],
        };
        skel.AddChild(layer);
        return layer;
    }

    public int PoseBones => _count;

    public float Weight
    {
        get
        {
            if (!Active || _ramp <= 0.0) return 0f;
            double t = Elapsed;
            return Mathf.Clamp((float)(t < _ramp ? t / _ramp : 2.0 - t / _ramp), 0f, 1f);
        }
    }

    private double Elapsed => (Time.GetTicksMsec() - _startMsec) / 1000.0;

    public void Play(int clipIndex, Animation clip, double blend)
    {
        if (Active && clipIndex == _clip) return;
        var skel = GetSkeleton();
        if (skel == null || clip.GetTrackCount() == 0) return;
        string node = clip.TrackGetPath(0).GetConcatenatedNames();

        _count = 0;
        for (int bone = 0; bone < _upper.Length && _count < _bones.Length; bone++)
        {
            if (!_upper[bone]) continue;
            int track = clip.FindTrack($"{node}:{skel.GetBoneName(bone)}", Animation.TrackType.Rotation3D);
            if (track < 0) continue;
            _bones[_count] = bone;
            _target[_count] = clip.RotationTrackInterpolate(track, 0.0).Normalized();
            _count++;
        }
        if (_count == 0) return;

        _clip = clipIndex;
        _startMsec = Time.GetTicksMsec();
        _ramp = ActionClip.Hold(0.0, blend) * 0.5;
        Active = true;
    }

    public void Stop()
    {
        Active = false;
        _clip = -1;
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        var skel = GetSkeleton();
        if (skel == null || _count == 0 || _ramp <= 0.0 || Elapsed >= _ramp * 2.0) { Stop(); return; }
        float w = Weight;
        if (w <= 0f) return;
        for (int i = 0; i < _count; i++)
            skel.SetBonePoseRotation(_bones[i], skel.GetBonePoseRotation(_bones[i]).Slerp(_target[i], w));
    }

    private static bool[]? UpperMask(Skeleton3D skel, out int count)
    {
        count = 0;
        int bones = skel.GetBoneCount();
        if (bones <= 1) return null;

        var lower = new bool[bones];
        bool legs = false;
        foreach (string root in LegRoots)
        {
            int leg = skel.FindBone(root);
            if (leg < 0) continue;
            legs = true;
            for (int b = 0; b < bones; b++)
                if (b == leg || Descends(skel, b, leg)) lower[b] = true;
        }
        if (!legs) return null;

        var upper = new bool[bones];
        for (int b = 0; b < bones; b++)
        {
            if (lower[b] || skel.GetBoneParent(b) < 0) continue;
            upper[b] = true;
            count++;
        }
        return count > 0 ? upper : null;
    }

    private static bool Descends(Skeleton3D skel, int bone, int root)
    {
        for (int p = skel.GetBoneParent(bone); p >= 0; p = skel.GetBoneParent(p))
            if (p == root) return true;
        return false;
    }

    private static Skeleton3D? FindSkeleton(Node n)
    {
        if (n is Skeleton3D s) return s;
        foreach (var c in n.GetChildren())
            if (FindSkeleton(c) is { } found) return found;
        return null;
    }
}
