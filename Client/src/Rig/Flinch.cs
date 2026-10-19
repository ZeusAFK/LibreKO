using Godot;

namespace LibreKO;

public sealed partial class Flinch : SkeletonModifier3D
{
    private static readonly string[] LegRoots = { "LeftHip", "RightHip" };

    private bool[] upper = System.Array.Empty<bool>();
    private int[] bones = System.Array.Empty<int>();
    private Quaternion[] target = System.Array.Empty<Quaternion>();
    private int[] tracks = System.Array.Empty<int>();
    private Animation? motion;
    private double length;
    private int count;
    private int clip = -1;
    private ulong startMsec;
    private double ramp;
    private Skeleton3D skeleton = null!;
    private Node holder = null!;
    private bool parkQueued;

    public static Flinch? Attach(Node3D body)
    {
        var skel = FindSkeleton(body);
        if (skel == null) return null;
        var upper = UpperMask(skel, out int room);
        if (upper == null) return null;
        var holder = new Node { Name = "flinch_idle" };
        skel.AddChild(holder);
        var layer = new Flinch
        {
            Name = "flinch",
            Active = false,
            upper = upper,
            bones = new int[room],
            target = new Quaternion[room],
            tracks = new int[room],
            skeleton = skel,
            holder = holder,
        };
        holder.AddChild(layer);
        return layer;
    }

    public int PoseBones => count;

    public float Weight
    {
        get
        {
            if (!Active || ramp <= 0.0) return 0f;
            double t = Elapsed;
            if (motion != null)
                return Mathf.Clamp((float)System.Math.Min(t / ramp, (length - t) / ramp), 0f, 1f);
            return Mathf.Clamp((float)(t < ramp ? t / ramp : 2.0 - t / ramp), 0f, 1f);
        }
    }

    private double Duration => motion != null ? length : ramp * 2.0;

    private double Elapsed => (Time.GetTicksMsec() - startMsec) / 1000.0;

    public void Play(int clipIndex, Animation animation, double blend)
    {
        if (Active && clipIndex == clip) return;
        var skel = GodotObject.IsInstanceValid(skeleton) ? skeleton : null;
        if (skel == null || !BindTracks(skel, animation)) return;
        for (int i = 0; i < count; i++)
            target[i] = animation.RotationTrackInterpolate(tracks[i], 0.0).Normalized();

        motion = null;
        Start(skel, clipIndex, ActionClip.Hold(0.0, blend) * 0.5);
    }

    public void PlayThrough(int clipIndex, Animation animation, double blend)
    {
        var skel = GodotObject.IsInstanceValid(skeleton) ? skeleton : null;
        if (skel == null || animation.Length <= 0f || !BindTracks(skel, animation)) return;

        motion = animation;
        length = animation.Length;
        Start(skel, clipIndex, System.Math.Min(System.Math.Max(blend, ActionClip.DefaultBlend), length * 0.5));
    }

    private bool BindTracks(Skeleton3D skel, Animation animation)
    {
        if (animation.GetTrackCount() == 0) return false;
        string node = animation.TrackGetPath(0).GetConcatenatedNames();

        count = 0;
        for (int bone = 0; bone < upper.Length && count < bones.Length; bone++)
        {
            if (!upper[bone]) continue;
            int track = animation.FindTrack($"{node}:{skel.GetBoneName(bone)}", Animation.TrackType.Rotation3D);
            if (track < 0) continue;
            bones[count] = bone;
            tracks[count] = track;
            count++;
        }
        return count > 0;
    }

    private void Start(Skeleton3D skel, int clipIndex, double ramp)
    {
        clip = clipIndex;
        startMsec = Time.GetTicksMsec();
        this.ramp = ramp;
        Active = true;
        Engage(skel);
    }

    private void Engage(Skeleton3D skel)
    {
        parkQueued = false;
        if (GetParent() == skel) return;
        GetParent()?.RemoveChild(this);
        skel.AddChild(this);
    }

    private void Park()
    {
        if (!parkQueued) return;
        parkQueued = false;
        if (Active || !GodotObject.IsInstanceValid(skeleton) || !GodotObject.IsInstanceValid(holder)) return;
        if (GetParent() != skeleton) return;
        skeleton.RemoveChild(this);
        holder.AddChild(this);
    }

    public void Stop()
    {
        Active = false;
        clip = -1;
        motion = null;
        if (parkQueued || !GodotObject.IsInstanceValid(skeleton) || GetParent() != skeleton) return;
        parkQueued = true;
        Callable.From(Park).CallDeferred();
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        using var scope = Perf.Measure(Perf.Section.Flinch);
        var skel = GetSkeleton();
        double t = Elapsed;
        if (skel == null || count == 0 || ramp <= 0.0 || t >= Duration) { Stop(); return; }
        float w = Weight;
        if (w <= 0f) return;
        for (int i = 0; i < count; i++)
        {
            var targetRotation = motion != null ? motion.RotationTrackInterpolate(tracks[i], t).Normalized() : target[i];
            skel.SetBonePoseRotation(bones[i], skel.GetBonePoseRotation(bones[i]).Slerp(targetRotation, w));
        }
    }

    private static bool[]? UpperMask(Skeleton3D skel, out int count)
    {
        count = 0;
        int boneCount = skel.GetBoneCount();
        if (boneCount <= 1) return null;

        var lower = new bool[boneCount];
        bool legs = false;
        foreach (string root in LegRoots)
        {
            int leg = skel.FindBone(root);
            if (leg < 0) continue;
            legs = true;
            for (int b = 0; b < boneCount; b++)
                if (b == leg || Descends(skel, b, leg)) lower[b] = true;
        }
        if (!legs) return null;

        var upper = new bool[boneCount];
        for (int b = 0; b < boneCount; b++)
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
