using System.Collections.Generic;
using Godot;

namespace LibreKO;

public sealed class CrowdAnimator
{
    private const float BakeFps = 30f;

    private sealed class Track
    {
        public int Bone;
        public Quaternion[]? Rot;
        public Vector3[]? Pos;
    }

    private sealed class BakedClip
    {
        public float Length;
        public int Frames;
        public Track[] Tracks = System.Array.Empty<Track>();
    }

    private sealed class ClipBank
    {
        public readonly Dictionary<string, BakedClip?> Clips = new();
    }

    private static readonly Dictionary<string, ClipBank> banks = new();
    private static readonly Dictionary<ulong, CrowdAnimator> byPlayer = new();

    private readonly AnimationPlayer anim;
    private readonly Skeleton3D skel;
    private readonly ClipBank bank;
    private BakedClip? clip, previous;
    private string name = "";
    private bool loop;
    private float length;
    private double time, previousTime, blendLeft, blendTotal;
    private bool holdBlend;

    public bool Driving { get; private set; }

    public static int Steps { get; private set; }
    private static int stepsAccum;
    private static ulong stepsFrame = ulong.MaxValue;

    private CrowdAnimator(AnimationPlayer anim, Skeleton3D skel, ClipBank bank)
    {
        this.anim = anim;
        this.skel = skel;
        this.bank = bank;
    }

    public static CrowdAnimator? Create(AnimationPlayer? anim, Node3D body, string rigPath)
    {
        if (anim == null || FindSkeleton(body) is not { } skel) return null;
        if (!banks.TryGetValue(rigPath, out var bank)) banks[rigPath] = bank = new ClipBank();
        var animator = new CrowdAnimator(anim, skel, bank);
        byPlayer[anim.GetInstanceId()] = animator;
        return animator;
    }

    public void Release()
    {
        if (GodotObject.IsInstanceValid(anim)) byPlayer.Remove(anim.GetInstanceId());
    }

    public static bool Current(AnimationPlayer anim, out string clipName, out double position)
    {
        if (byPlayer.TryGetValue(anim.GetInstanceId(), out var animator) && animator.Driving && animator.clip != null)
        {
            clipName = animator.name;
            position = animator.time;
            return true;
        }
        clipName = "";
        position = 0;
        return false;
    }

    public static void NotifyPlay(AnimationPlayer anim, string name, double blend, bool hold = true)
    {
        if (byPlayer.TryGetValue(anim.GetInstanceId(), out var animator)) animator.Play(name, blend, hold);
    }

    private void Play(string name, double blend, bool hold)
    {
        holdBlend = hold;
        var clip = Bake(name);
        if (clip == null) { this.clip = null; return; }
        if (this.clip != null && blend > 0)
        {
            previous = this.clip;
            previousTime = time;
            blendTotal = blendLeft = blend;
        }
        else blendLeft = 0;
        this.clip = clip;
        Describe(name);
        time = 0;
    }

    private void Describe(string name)
    {
        this.name = name;
        var resource = anim.GetAnimation(name);
        loop = resource != null && resource.LoopMode != Animation.LoopModeEnum.None;
        length = resource != null ? (float)resource.Length : clip?.Length ?? 0f;
    }

    public void Resume()
    {
        if (!Driving) return;
        Driving = false;
        if (clip != null && GodotObject.IsInstanceValid(anim))
            using (Perf.Measure(Perf.Section.StepSeek)) anim.Seek(time, false);
    }

    public void TakeOver()
    {
        Driving = true;
        string name = anim.CurrentAnimation.ToString();
        if (name.Length == 0) { clip = null; return; }
        clip = Bake(name);
        Describe(name);
        time = anim.CurrentAnimationPosition;
        blendLeft = 0;
    }

    public void Step(double delta, bool apply = true)
    {
        if (clip == null || !GodotObject.IsInstanceValid(skel)) return;
        CountStep();
        float weight = 0f;
        bool blending = blendLeft > 0 && previous != null;
        if (blending)
        {
            blendLeft -= delta;
            if (!holdBlend) previousTime += delta;
            weight = blendTotal > 0 ? (float)Mathf.Clamp(blendLeft / blendTotal, 0.0, 1.0) : 0f;
        }
        if (!blending || !holdBlend)
        {
            time += delta;
            if (loop) { if (length > 0f) time = Mathf.PosMod(time, length); }
            else if (time > length) time = length;
        }
        if (apply) Apply(clip, (float)time, weight > 0f ? previous : null, (float)previousTime, weight);
    }

    private static void CountStep()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame != stepsFrame)
        {
            Steps = stepsAccum;
            stepsAccum = 0;
            stepsFrame = frame;
        }
        stepsAccum++;
    }

    private Quaternion[] rotOut = new Quaternion[64];
    private Vector3[] posOut = new Vector3[64];
    private bool[] hasRot = new bool[64], hasPos = new bool[64];

    private void Apply(BakedClip clip, float clipTime, BakedClip? previous, float previousClipTime, float previousWeight)
    {
        int n = clip.Tracks.Length;
        if (rotOut.Length < n)
        {
            rotOut = new Quaternion[n];
            posOut = new Vector3[n];
            hasRot = new bool[n];
            hasPos = new bool[n];
        }
        using (Perf.Measure(Perf.Section.StepSample))
        {
            for (int i = 0; i < n; i++)
            {
                var track = clip.Tracks[i];
                hasRot[i] = track.Rot != null;
                hasPos[i] = track.Pos != null;
                if (track.Rot != null)
                {
                    var q = Sample(track.Rot, clipTime, clip);
                    if (previous != null && FindTrack(previous, track.Bone) is { Rot: { } prevRot })
                        q = Sample(prevRot, previousClipTime, previous).Slerp(q, 1f - previousWeight);
                    rotOut[i] = q;
                }
                if (track.Pos != null)
                {
                    var p = Sample(track.Pos, clipTime, clip);
                    if (previous != null && FindTrack(previous, track.Bone) is { Pos: { } prevPos })
                        p = Sample(prevPos, previousClipTime, previous).Lerp(p, 1f - previousWeight);
                    posOut[i] = p;
                }
            }
        }
        using (Perf.Measure(Perf.Section.StepPose))
        {
            for (int i = 0; i < n; i++)
            {
                int bone = clip.Tracks[i].Bone;
                if (hasRot[i]) skel.SetBonePoseRotation(bone, rotOut[i]);
                if (hasPos[i]) skel.SetBonePosePosition(bone, posOut[i]);
            }
        }
    }

    private static Track? FindTrack(BakedClip clip, int bone)
    {
        foreach (var t in clip.Tracks) if (t.Bone == bone) return t;
        return null;
    }

    private static Quaternion Sample(Quaternion[] frames, float clipTime, BakedClip clip)
    {
        float f = Mathf.Clamp(clipTime * BakeFps, 0f, clip.Frames - 1);
        int i = (int)f;
        int j = Mathf.Min(i + 1, clip.Frames - 1);
        return i == j ? frames[i] : frames[i].Slerp(frames[j], f - i);
    }

    private static Vector3 Sample(Vector3[] frames, float clipTime, BakedClip clip)
    {
        float f = Mathf.Clamp(clipTime * BakeFps, 0f, clip.Frames - 1);
        int i = (int)f;
        int j = Mathf.Min(i + 1, clip.Frames - 1);
        return i == j ? frames[i] : frames[i].Lerp(frames[j], f - i);
    }

    private BakedClip? Bake(string name)
    {
        if (bank.Clips.TryGetValue(name, out var cached)) return cached;
        BakedClip? baked = null;
        var res = anim.GetAnimation(name);
        if (res != null)
        {
            int frames = Mathf.Max(2, Mathf.CeilToInt((float)res.Length * BakeFps) + 1);
            var tracks = new List<Track>();
            for (int i = 0; i < res.GetTrackCount(); i++)
            {
                var type = res.TrackGetType(i);
                if (type != Animation.TrackType.Rotation3D && type != Animation.TrackType.Position3D) continue;
                var path = res.TrackGetPath(i);
                if (path.GetSubNameCount() == 0) continue;
                int bone = skel.FindBone(path.GetSubName(0));
                if (bone < 0) continue;
                var track = tracks.Find(t => t.Bone == bone);
                if (track == null) { track = new Track { Bone = bone }; tracks.Add(track); }
                if (type == Animation.TrackType.Rotation3D)
                {
                    track.Rot = new Quaternion[frames];
                    for (int k = 0; k < frames; k++)
                        track.Rot[k] = res.RotationTrackInterpolate(i, Mathf.Min(k / BakeFps, (float)res.Length)).Normalized();
                }
                else
                {
                    track.Pos = new Vector3[frames];
                    for (int k = 0; k < frames; k++)
                        track.Pos[k] = res.PositionTrackInterpolate(i, Mathf.Min(k / BakeFps, (float)res.Length));
                }
            }
            if (tracks.Count > 0)
                baked = new BakedClip { Length = (float)res.Length, Frames = frames, Tracks = tracks.ToArray() };
        }
        bank.Clips[name] = baked;
        return baked;
    }

    private static Skeleton3D? FindSkeleton(Node node)
    {
        if (node is Skeleton3D skel) return skel;
        foreach (var child in node.GetChildren())
            if (FindSkeleton(child) is { } found) return found;
        return null;
    }
}
