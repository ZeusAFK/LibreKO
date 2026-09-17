using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NumericsVector3 = System.Numerics.Vector3;
using Godot;

namespace LibreKO;

public partial class Cape : MeshInstance3D
{
    public const int GmCapeId = 99;

    private const float RefNeckY = 1.7346f;
    private const string AssetDir = "res://assets/capes/";

    public static bool Debug;

    public static bool DrawDebug;

    public static bool Enabled;

    private const float QuadHigh = 0.12f, QuadLow = 0.18f;
    private const int MaxColsHigh = 13, MaxRowsHigh = 17;
    private const int MaxColsLow = 9, MaxRowsLow = 13;
    private const int Iterations = 4;
    private const float StretchCompliance = 0.0000002f;
    private const float ShearCompliance = 0.000004f;
    private const float BendCompliance = 0.0008f;
    private const float Gravity = -9.81f;
    private const float AeroDrag = 0.65f;
    private const float AeroMaxAccel = 24f;
    private const float GustStrength = 0.3f;
    private const float ContactFriction = 0.18f;
    private const float SimDist = 40f;
    private const float ThrottleDist = 18f;

    private const float ThetaMax = 3.0f;
    private const float ClearMargin = 0.045f;
    private const float FlareOut = 0.055f;
    private const float CollarRadius = 0.085f;
    private const float CollarBlendDrop = 0.13f;
    private const float MaxDrapeRadius = 0.29f;
    private const int PleatCount = 3;
    private const float PleatDepth = 0.09f;
    private const float PleatFadeIn = 0.18f;
    private const float MaxPleat = 0.42f;
    private const float MaxGather = 1.40f;

    private static readonly (float T, float R)[] BodyProfile =
    {
        (0.00f, 0.070f),
        (0.12f, 0.170f),
        (0.30f, 0.150f),
        (0.46f, 0.200f),
        (0.66f, 0.175f),
        (1.00f, 0.120f),
    };

    public readonly record struct CapeDef(
        string Name, int Family, int Grade, int Ranking, int C, int M, int Price, int Points);

    private sealed class GridMesh
    {
        public Vector3[] Pos = Array.Empty<Vector3>();
    }

    private static bool _loaded;
    private static int _cols = 5, _rows = 9;
    private static readonly Dictionary<int, CapeDef> _table = new();
    private static readonly Dictionary<string, GridMesh> _grids = new();
    private static readonly Dictionary<string, Texture2D?> _texCache = new();

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        using var f = Godot.FileAccess.Open(AssetDir + "capes.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PushWarning("[cape] capes.json missing — capes disabled"); return; }
        var doc = Json.ParseString(f.GetAsText()).AsGodotDictionary();
        if (doc.TryGetValue("grid", out var g))
        {
            var gd = g.AsGodotDictionary();
            _cols = gd["cols"].AsInt32();
            _rows = gd["rows"].AsInt32();
        }
        foreach (var k in doc["table"].AsGodotDictionary())
        {
            var o = k.Value.AsGodotDictionary();
            _table[int.Parse(k.Key.AsString())] = new CapeDef(
                o["name"].AsString(), o["family"].AsInt32(), o["grade"].AsInt32(),
                o["ranking"].AsInt32(), o["c"].AsInt32(), o["m"].AsInt32(),
                o.TryGetValue("price", out var pr) ? pr.AsInt32() : 0,
                o.TryGetValue("points", out var pt) ? pt.AsInt32() : 0);
        }
        foreach (var k in doc["meshes"].AsGodotDictionary())
        {
            var o = k.Value.AsGodotDictionary();
            var pos = o["pos"].AsFloat32Array();
            var m = new GridMesh { Pos = new Vector3[pos.Length / 3] };
            for (int i = 0; i < m.Pos.Length; i++)
                m.Pos[i] = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
            _grids[k.Key.AsString()] = m;
        }
        GD.Print($"[cape] {_table.Count} capes, {_grids.Count} fitted grids");
    }

    public static IReadOnlyDictionary<int, CapeDef> Catalogue
    {
        get { Load(); return _table; }
    }

    public static bool TryGet(int capeId, out CapeDef def)
    {
        Load();
        return _table.TryGetValue(capeId, out def);
    }

    public static Texture2D? ClothTexture(int c) => Tex($"c_{c:00}.png");

    public static Texture2D? MarkTexture(int m) => m > 0 ? Tex($"m_{m:00}.png") : null;

    public static bool IsRenderable(int capeId)
    {
        Load();
        return capeId > 0 && _table.ContainsKey(capeId);
    }

    private static Texture2D? _blank;

    private static Texture2D Blank
    {
        get
        {
            if (_blank != null) return _blank;
            var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            img.SetPixel(0, 0, Colors.White);
            return _blank = ImageTexture.CreateFromImage(img);
        }
    }

    private static Texture2D? Tex(string file)
    {
        if (_texCache.TryGetValue(file, out var t)) return t;
        string path = AssetDir + "tex/" + file;
        t = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        _texCache[file] = t;
        return t;
    }

    private static GridMesh? GridFor(int family, int race)
    {
        if (_grids.TryGetValue($"{family}:{race}", out var m)) return m;
        int baseRace = race >= 10 ? 11 : 1;
        for (int r = baseRace; r < baseRace + 6; r++)
            if (_grids.TryGetValue($"{family}:{r}", out m)) return m;
        foreach (var kv in _grids)
            if (kv.Key.StartsWith($"{family}:")) return kv.Value;
        foreach (var kv in _grids) return kv.Value;
        return null;
    }

    private Skeleton3D _skel = null!;
    private int _rideBone = -1;
    private Transform3D _anchorLocal;
    private int _hipBone = -1, _neckBone = -1;
    private int _upLegL = -1, _loLegL = -1, _ftLegL = -1;
    private int _upLegR = -1, _loLegR = -1, _ftLegR = -1;
    private int _upArmL = -1, _loArmL = -1, _handL = -1;
    private int _upArmR = -1, _loArmR = -1, _handR = -1;

    private int _nc, _nr;
    private Vector3[] _rest = Array.Empty<Vector3>();
    private Vector3[] _pos = Array.Empty<Vector3>();
    private Vector3[] _targets = Array.Empty<Vector3>();
    private Vector3[] _previous = Array.Empty<Vector3>();
    private Vector3[] _renderPos = Array.Empty<Vector3>();
    private Vector3[] _vel = Array.Empty<Vector3>();
    private Vector3[] _pred = Array.Empty<Vector3>();
    private Vector3[] _nrm = Array.Empty<Vector3>();
    private Vector3[] _surfacePos = Array.Empty<Vector3>();
    private int[] _surfaceIdx = Array.Empty<int>();
    private int _surfaceCols, _surfaceRows;
    private int[] _surfaceSamples = Array.Empty<int>();
    private float[] _surfaceWeights = Array.Empty<float>();
    private int[] _idx = Array.Empty<int>();
    private float[] _w = Array.Empty<float>();
    private bool[] _contact = Array.Empty<bool>();
    private float[] _tether = Array.Empty<float>();
    private float[] _areaW = Array.Empty<float>();
    private Vector3 _bodyFwd = Vector3.Forward;

    private struct Link
    {
        public int A, B;
        public float Length, Compliance, Lambda;
    }

    private Link[] _links = Array.Empty<Link>();

    private ArrayMesh _mesh = null!;
    private ShaderMaterial _mat = null!;
    private byte[] _vertexBytes = Array.Empty<byte>();
    private int _normalOffset, _normalStride;
    private Basis _restBasis;
    private Transform3D _lastFrameAnchor, _stepAnchor, _pinPrevious, _pinCurrent;
    private Node3D _bodyRoot = null!;
    private VisibleOnScreenNotifier3D _visibility = null!;
    private int _race, _family;
    private bool _highDetail;
    private float _lastStep;
    private Color _dye;
    private Vector3 _renderOrigin;
    private bool _primed, _frozen;
    private float _rigScale = 1f;
    private double _phase, _accum;
    private Vector3 _anchorVel;

    private int _capeId;

    public int CapeId => _capeId;
    internal ReadOnlySpan<Vector3> ClothVertices => _surfacePos;
    internal ReadOnlySpan<int> ClothTriangles => _surfaceIdx;
    internal int ClothColumns => _surfaceCols;
    internal bool Dormant => _frozen;
    internal bool Finite => Array.TrueForAll(_pos, p => p.IsFinite()) && Array.TrueForAll(_surfacePos, p => p.IsFinite());

    public string Resolution => $"{_nc}x{_nr}";

    public static Cape? Attach(Node3D body, int capeId, Color dye, int race, bool highDetail = false)
    {
        Load();
        if (!IsRenderable(capeId)) return null;
        var skel = FindSkeleton(body);
        if (skel == null) return null;

        var def = _table[capeId];
        var grid = GridFor(def.Family, race);
        if (grid == null) return null;

        int ride = FirstBone(skel, "UpperChest", "Chest2", "Chest", "Spine", "Hips");
        int neck = FirstBone(skel, "Neck", "Head", "UpperChest", "Chest2");
        if (ride < 0 || neck < 0) return null;

        var cape = new Cape
        {
            Name = "Cape",
            _skel = skel,
            _bodyRoot = body,
            _race = race,
            _highDetail = highDetail,
            _rideBone = ride,
            _neckBone = neck,
            _hipBone = FirstBone(skel, "Hips", "Pelvis", "Root"),
            _upLegL = FirstBone(skel, "UpperLeg_L", "LeftHip"),
            _loLegL = FirstBone(skel, "LowerLeg_L", "LeftKnee"),
            _ftLegL = FirstBone(skel, "Foot_L", "LeftAnkle"),
            _upLegR = FirstBone(skel, "UpperLeg_R", "RightHip"),
            _loLegR = FirstBone(skel, "LowerLeg_R", "RightKnee"),
            _ftLegR = FirstBone(skel, "Foot_R", "RightAnkle"),
            _upArmL = FirstBone(skel, "UpperArm_L", "LeftShoulder"),
            _loArmL = FirstBone(skel, "LowerArm_L", "LeftElbow"),
            _handL = FirstBone(skel, "Hand_L", "LeftWrist"),
            _upArmR = FirstBone(skel, "UpperArm_R", "RightShoulder"),
            _loArmR = FirstBone(skel, "LowerArm_R", "RightElbow"),
            _handR = FirstBone(skel, "Hand_R", "RightWrist"),
            TopLevel = true,
            CastShadow = ShadowCastingSetting.Off,
            ProcessPriority = 500,
        };
        body.AddChild(cape);
        cape.Build(grid, capeId, dye, highDetail, body);
        return cape;
    }

    public void SetCape(int capeId, Color dye)
    {
        Load();
        if (!_table.TryGetValue(capeId, out var def)) { QueueFree(); return; }
        if (_family != def.Family && GridFor(def.Family, _race) is { } grid)
        {
            Build(grid, capeId, dye, _highDetail, _bodyRoot);
            return;
        }
        _capeId = capeId;
        _dye = dye;
        ApplyDye(dye);
        var mark = def.M > 0 ? Tex($"m_{def.M:00}.png") : null;
        _mat.SetShaderParameter("base_tex", Tex($"c_{def.C:00}.png") ?? Blank);
        _mat.SetShaderParameter("mark_tex", mark ?? Blank);
        _mat.SetShaderParameter("has_mark", mark != null ? 1.0f : 0.0f);
    }

    private void ApplyDye(Color dye)
    {
        bool dyed = dye.R > 0.001f || dye.G > 0.001f || dye.B > 0.001f;
        var c = dyed ? dye : Colors.White;
        _mat.SetShaderParameter("dye", new Vector3(c.R, c.G, c.B));
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(_skel) || _rest.Length == 0 || delta <= 0) return;
        var anchor = AnchorWorld();
        _pinCurrent = AttachmentWorld();
        var cam = GetViewport().GetCamera3D();
        float distance2 = cam == null ? 0f : cam.GlobalPosition.DistanceSquaredTo(anchor.Origin);
        bool dormant = !IsVisibleInTree() || distance2 > SimDist * SimDist
            || (cam != null && !_visibility.IsOnScreen());
        if (dormant)
        {
            if (!_frozen)
            {
                _renderOrigin = Vector3.Zero;
                SmoothSurface(_rest);
                Upload(_surfacePos);
                _frozen = true;
            }
            GlobalTransform = _pinCurrent;
            _lastFrameAnchor = anchor;
            _pinPrevious = _pinCurrent;
            _primed = false;
            if (_dbg != null) _dbg.Visible = false;
            return;
        }
        UpdatePins();
        if (!_primed || _frozen || delta > 0.2 || anchor.Origin.DistanceSquaredTo(_lastFrameAnchor.Origin) > 9f)
            Reset(anchor);
        _frozen = false;

        float h = distance2 > ThrottleDist * ThrottleDist ? 1f / 30f
            : _highDetail ? 1f / 120f : 1f / 60f;
        if (delta > 0.035) h = Mathf.Max(h, 1f / 60f);
        if (_lastStep != h) { _accum = 0; _lastStep = h; }
        float dt = Mathf.Min((float)delta, h * 4);
        _accum += dt;
        UpdateColliders();
        while (_accum + 1e-8 >= h)
        {
            float blend = Mathf.Clamp(1f - ((float)_accum - h) / dt, 0f, 1f);
            var step = _lastFrameAnchor.InterpolateWith(anchor, blend);
            Simulate(h, step, blend);
            _accum -= h;
        }
        _lastFrameAnchor = anchor;
        _renderOrigin = anchor.Origin;
        GlobalTransform = new Transform3D(Basis.Identity, _renderOrigin);
        float alpha = Mathf.Clamp((float)_accum / h, 0, 1);
        for (int i = 0; i < _pos.Length; i++)
            _renderPos[i] = _w[i] == 0 ? _pins[i] : _previous[i].Lerp(_pos[i], alpha);
        SmoothSurface(_renderPos);
        CollideRender();
        Upload(_surfacePos);
        CommitColliders();
        _pinPrevious = _pinCurrent;
        Array.Copy(_pins, _oldPins, _nc);
        if (DrawDebug) DrawDebugOverlay();
        else if (_dbg != null) _dbg.Visible = false;
    }

    private void Reset(Transform3D anchor)
    {
        _pinPrevious = _pinCurrent;
        Array.Copy(_pins, _oldPins, _nc);
        Array.Copy(_pins, _stepPins, _nc);
        BuildTargets(anchor, _pinCurrent);
        for (int i = 0; i < _rest.Length; i++)
        {
            _pos[i] = _previous[i] = _pred[i] = _targets[i];
            _vel[i] = Vector3.Zero;
        }
        _stepAnchor = _lastFrameAnchor = anchor;
        _anchorVel = Vector3.Zero;
        _accum = 0;
        _primed = true;
        _collidersPrimed = false;
    }

    public static void RefreshBody(Node3D body)
    {
        if (FindFirst<Cape>(body) is { } cape && !cape.IsQueuedForDeletion()
            && GridFor(cape._family, cape._race) is { } grid)
            cape.Build(grid, cape._capeId, cape._dye, cape._highDetail, cape._bodyRoot);
    }

    private float _aspect = 1f;
    private readonly (Vector3 A, Vector3 B, float Ra, float Rb, float Aspect)[] _segs
        = new (Vector3, Vector3, float, float, float)[20];

    private static T? FindFirst<T>(Node n) where T : class
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren())
            if (FindFirst<T>(c) is { } found) return found;
        return null;
    }
    private const float UpperArmRadius = 0.090f, ForeArmRadius = 0.075f, HandRadius = 0.070f;
    private const float ThighRadius = 0.115f, CalfRadius = 0.085f;
    private float _rNeck, _rChest, _rHips, _inflate;

    private const int ProfileBins = 14;
    private const float CollideInflate = 0.02f;
    private float[]? _fitted;

    private MeshInstance3D? _dbg;

    private void SmoothSurface(Vector3[] source)
    {
        var input = MemoryMarshal.Cast<Vector3, NumericsVector3>(source.AsSpan());
        var output = MemoryMarshal.Cast<Vector3, NumericsVector3>(_surfacePos.AsSpan());
        for (int i = 0; i < output.Length; i++)
        {
            var position = NumericsVector3.Zero;
            int start = i * 16;
            for (int j = start; j < start + 16; j++)
                position += input[_surfaceSamples[j]] * _surfaceWeights[j];
            output[i] = position;
        }
    }

    private void Upload(Vector3[] positions)
    {
        var points = MemoryMarshal.Cast<Vector3, NumericsVector3>(positions.AsSpan());
        var normals = MemoryMarshal.Cast<Vector3, NumericsVector3>(_nrm.AsSpan());
        normals.Clear();
        for (int t = 0; t < _surfaceIdx.Length; t += 3)
        {
            int a = _surfaceIdx[t], b = _surfaceIdx[t + 1], c = _surfaceIdx[t + 2];
            var normal = NumericsVector3.Cross(points[b] - points[a], points[c] - points[a]);
            normals[a] += normal; normals[b] += normal; normals[c] += normal;
        }
        var vertices = MemoryMarshal.Cast<byte, Vector3>(_vertexBytes.AsSpan(0, positions.Length * 12));
        for (int i = 0; i < positions.Length; i++)
        {
            vertices[i] = positions[i] - _renderOrigin;
            var normal = _nrm[i].LengthSquared() > 1e-12f ? _nrm[i].Normalized() : Vector3.Back;
            var oct = normal.OctahedronEncode();
            int offset = _normalOffset + i * _normalStride;
            BinaryPrimitives.WriteUInt16LittleEndian(_vertexBytes.AsSpan(offset), (ushort)Mathf.Clamp(oct.X * 65535, 0, 65535));
            BinaryPrimitives.WriteUInt16LittleEndian(_vertexBytes.AsSpan(offset + 2), (ushort)Mathf.Clamp(oct.Y * 65535, 0, 65535));
        }
        _mesh.SurfaceUpdateVertexRegion(0, 0, _vertexBytes);
    }

    private static Skeleton3D? FindSkeleton(Node n)
    {
        if (n is Skeleton3D s) return s;
        foreach (var c in n.GetChildren())
            if (FindSkeleton(c) is { } found) return found;
        return null;
    }

    private static Basis BodyBasis(Skeleton3D skel)
    {
        var feet = Vector3.Zero;
        foreach (int foot in new[] { FirstBone(skel, "Foot_L", "LeftAnkle"), FirstBone(skel, "Foot_R", "RightAnkle") })
        {
            if (foot < 0) continue;
            var children = skel.GetBoneChildren(foot);
            if (children.Length > 0) feet += BoneDelta(skel, foot, children[0]);
        }
        var fwd = Flat(feet);
        if (fwd == Vector3.Zero) fwd = Flat(BoneDelta(skel, FootBone(skel), ToeBone(skel)));
        if (fwd == Vector3.Zero)
            fwd = Flat(BoneDelta(skel, FirstBone(skel, "Neck"), FirstBone(skel, "Head")));
        if (fwd == Vector3.Zero) return Basis.Identity;
        var up = Vector3.Up;
        return new Basis(up.Cross(fwd), up, fwd);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.Y = 0f;
        return v.LengthSquared() < 1e-8f ? Vector3.Zero : v.Normalized();
    }

    private static Vector3 BoneDelta(Skeleton3D skel, int from, int to)
        => from < 0 || to < 0 ? Vector3.Zero
         : skel.GetBoneGlobalRest(to).Origin - skel.GetBoneGlobalRest(from).Origin;

    private static int FootBone(Skeleton3D skel) => FirstBone(skel, "Foot_R", "RightAnkle", "Foot_L", "LeftAnkle");

    private static int ToeBone(Skeleton3D skel)
    {
        int named = FirstBone(skel, "Toe_R", "Toe_L");
        if (named >= 0) return named;
        int foot = FootBone(skel);
        if (foot < 0) return -1;
        var kids = skel.GetBoneChildren(foot);
        return kids.Length > 0 ? kids[0] : -1;
    }

    private static int FirstBone(Skeleton3D skel, params string[] names)
    {
        foreach (var want in names)
        {
            int direct = skel.FindBone(want);
            if (direct >= 0) return direct;
            string key = want.Replace(" ", "").ToLowerInvariant();
            for (int i = 0; i < skel.GetBoneCount(); i++)
                if (skel.GetBoneName(i).Replace(" ", "").ToLowerInvariant() == key) return i;
        }
        return -1;
    }

    private static Shader ClothShader => Shaders.Get("cloth");
}

