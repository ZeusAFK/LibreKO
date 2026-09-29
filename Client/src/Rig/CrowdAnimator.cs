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

    private static readonly Dictionary<string, ClipBank> _banks = new();
    private static readonly Dictionary<ulong, CrowdAnimator> _byPlayer = new();

    private readonly AnimationPlayer _anim;
    private readonly Skeleton3D _skel;
    private readonly ClipBank _bank;
    private BakedClip? _clip, _previous;
    private string _name = "";
    private bool _loop;
    private float _length;
    private double _time, _previousTime, _blendLeft, _blendTotal;

    public bool Driving { get; private set; }

    public static int Steps { get; private set; }
    private static int _stepsAccum;
    private static ulong _stepsFrame = ulong.MaxValue;

    private CrowdAnimator(AnimationPlayer anim, Skeleton3D skel, ClipBank bank)
    {
        _anim = anim;
        _skel = skel;
        _bank = bank;
    }

    public static CrowdAnimator? Create(AnimationPlayer? anim, Node3D body, string rigPath)
    {
        if (anim == null || FindSkeleton(body) is not { } skel) return null;
        if (!_banks.TryGetValue(rigPath, out var bank)) _banks[rigPath] = bank = new ClipBank();
        var animator = new CrowdAnimator(anim, skel, bank);
        _byPlayer[anim.GetInstanceId()] = animator;
        return animator;
    }

    public void Release()
    {
        if (GodotObject.IsInstanceValid(_anim)) _byPlayer.Remove(_anim.GetInstanceId());
    }

    public static bool Current(AnimationPlayer anim, out string clip, out double position)
    {
        if (_byPlayer.TryGetValue(anim.GetInstanceId(), out var animator) && animator.Driving && animator._clip != null)
        {
            clip = animator._name;
            position = animator._time;
            return true;
        }
        clip = "";
        position = 0;
        return false;
    }

    public static void NotifyPlay(AnimationPlayer anim, string name, double blend)
    {
        if (_byPlayer.TryGetValue(anim.GetInstanceId(), out var animator)) animator.Play(name, blend);
    }

    private void Play(string name, double blend)
    {
        var clip = Bake(name);
        if (clip == null) { _clip = null; return; }
        if (_clip != null && blend > 0)
        {
            _previous = _clip;
            _previousTime = _time;
            _blendTotal = _blendLeft = blend;
        }
        else _blendLeft = 0;
        _clip = clip;
        Describe(name);
        _time = 0;
    }

    private void Describe(string name)
    {
        _name = name;
        var resource = _anim.GetAnimation(name);
        _loop = resource != null && resource.LoopMode != Animation.LoopModeEnum.None;
        _length = resource != null ? (float)resource.Length : _clip?.Length ?? 0f;
    }

    public void Resume()
    {
        if (!Driving) return;
        Driving = false;
        if (_clip != null && GodotObject.IsInstanceValid(_anim))
            using (Perf.Measure(Perf.Section.StepSeek)) _anim.Seek(_time, false);
    }

    public void TakeOver()
    {
        Driving = true;
        string name = _anim.CurrentAnimation.ToString();
        if (name.Length == 0) { _clip = null; return; }
        _clip = Bake(name);
        Describe(name);
        _time = _anim.CurrentAnimationPosition;
        _blendLeft = 0;
    }

    public void Step(double delta, bool apply = true)
    {
        if (_clip == null || !GodotObject.IsInstanceValid(_skel)) return;
        CountStep();
        _time += delta;
        if (_loop) { if (_length > 0f) _time = Mathf.PosMod(_time, _length); }
        else if (_time > _length) _time = _length;

        float weight = 0f;
        if (_blendLeft > 0 && _previous != null)
        {
            _blendLeft -= delta;
            _previousTime += delta;
            weight = _blendTotal > 0 ? (float)Mathf.Clamp(_blendLeft / _blendTotal, 0.0, 1.0) : 0f;
        }
        if (apply) Apply(_clip, (float)_time, weight > 0f ? _previous : null, (float)_previousTime, weight);
    }

    private static void CountStep()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame != _stepsFrame)
        {
            Steps = _stepsAccum;
            _stepsAccum = 0;
            _stepsFrame = frame;
        }
        _stepsAccum++;
    }

    private Quaternion[] _rotOut = new Quaternion[64];
    private Vector3[] _posOut = new Vector3[64];
    private bool[] _hasRot = new bool[64], _hasPos = new bool[64];

    private void Apply(BakedClip clip, float time, BakedClip? previous, float previousTime, float previousWeight)
    {
        int n = clip.Tracks.Length;
        if (_rotOut.Length < n)
        {
            _rotOut = new Quaternion[n];
            _posOut = new Vector3[n];
            _hasRot = new bool[n];
            _hasPos = new bool[n];
        }
        using (Perf.Measure(Perf.Section.StepSample))
        {
            for (int i = 0; i < n; i++)
            {
                var track = clip.Tracks[i];
                _hasRot[i] = track.Rot != null;
                _hasPos[i] = track.Pos != null;
                if (track.Rot != null)
                {
                    var q = Sample(track.Rot, time, clip);
                    if (previous != null && FindTrack(previous, track.Bone) is { Rot: { } prevRot })
                        q = Sample(prevRot, previousTime, previous).Slerp(q, 1f - previousWeight);
                    _rotOut[i] = q;
                }
                if (track.Pos != null)
                {
                    var p = Sample(track.Pos, time, clip);
                    if (previous != null && FindTrack(previous, track.Bone) is { Pos: { } prevPos })
                        p = Sample(prevPos, previousTime, previous).Lerp(p, 1f - previousWeight);
                    _posOut[i] = p;
                }
            }
        }
        using (Perf.Measure(Perf.Section.StepPose))
        {
            for (int i = 0; i < n; i++)
            {
                int bone = clip.Tracks[i].Bone;
                if (_hasRot[i]) _skel.SetBonePoseRotation(bone, _rotOut[i]);
                if (_hasPos[i]) _skel.SetBonePosePosition(bone, _posOut[i]);
            }
        }
    }

    private static Track? FindTrack(BakedClip clip, int bone)
    {
        foreach (var t in clip.Tracks) if (t.Bone == bone) return t;
        return null;
    }

    private static Quaternion Sample(Quaternion[] frames, float time, BakedClip clip)
    {
        float f = Mathf.Clamp(time * BakeFps, 0f, clip.Frames - 1);
        int i = (int)f;
        int j = Mathf.Min(i + 1, clip.Frames - 1);
        return i == j ? frames[i] : frames[i].Slerp(frames[j], f - i);
    }

    private static Vector3 Sample(Vector3[] frames, float time, BakedClip clip)
    {
        float f = Mathf.Clamp(time * BakeFps, 0f, clip.Frames - 1);
        int i = (int)f;
        int j = Mathf.Min(i + 1, clip.Frames - 1);
        return i == j ? frames[i] : frames[i].Lerp(frames[j], f - i);
    }

    private BakedClip? Bake(string name)
    {
        if (_bank.Clips.TryGetValue(name, out var cached)) return cached;
        BakedClip? baked = null;
        var res = _anim.GetAnimation(name);
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
                int bone = _skel.FindBone(path.GetSubName(0));
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
        _bank.Clips[name] = baked;
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
