using Godot;
using System.Collections.Generic;

namespace LibreKO;

public partial class FxBillboard : MeshInstance3D, IFxPooledPart, IFxPart
{
    private ShaderMaterial _mat = null!;
    private Texture2D?[] _frames = System.Array.Empty<Texture2D?>();
    private float _age;
    private Vector3 _initPos, _initVel, _accel;
    private float _start, _life, _fadeIn, _fadeOut, _hideTime, _showTime;
    private float _w, _h, _wVel, _hVel, _wAcc, _hAcc;
    private bool _spins, _explicitRot, _flat;
    private Vector3 _rotDeg;
    private Basis _matrixBasis = Basis.Identity;
    private float _spinRate;
    private Color _lastColor = new Color(-1, -1, -1, -1);
    private float _gap;
    private float _blendWord;
    private float _texFps; private int _frameCount; private bool _wrap;
    private int _atlasCols = 1, _atlasRows = 1;
    private int _atlasFrames = 1;
    private ArrayMesh? _groundFan;
    private bool _groundFanBuilt;
    private bool _mirroredQuarterUv;
    private float _fanW, _fanH, _fanRot;
    private FxInstance? _root;
    private bool? _shown;
    private bool _done;
    private int _lastFrame = -1, _lastCell = -1;
    private FxBoardBatch? _batcher;
    private FxBoardBatch.Batch? _batch;
    private FxBoardBatch.Key _key;
    private Texture2DArray? _sequence;
    private int _layers = 1;

    internal bool Batched { get; private set; }
    public FxPartKey? PoolKey { get; set; }
    internal float Fade;
    internal Vector2 AtlasOffset;

    public static System.Func<float, float, float?>? GroundHeight;

    private static readonly Dictionary<FxPartKey, FxBillboard> _prototypes = Shutdown.Track(new Dictionary<FxPartKey, FxBillboard>());

    public static FxBillboard? Build(Godot.Collections.Dictionary p, FxPartKey? key = null)
    {
        if (key is { } pooledKey)
        {
            if (Fx.TakePooledPart(pooledKey) is FxBillboard pooled) return pooled;
            if (_prototypes.TryGetValue(pooledKey, out var prototype)) return prototype.Clone(pooledKey);
        }
        var built = BuildFresh(p, key);
        if (built != null && key is { } protoKey) _prototypes[protoKey] = built.Clone(protoKey);
        return built;
    }

    private FxBillboard Clone(FxPartKey? key)
    {
        var c = new FxBillboard
        {
            CastShadow = ShadowCastingSetting.Off,
            _frames = _frames,
            _initPos = _initPos, _initVel = _initVel, _accel = _accel,
            _start = _start, _life = _life, _fadeIn = _fadeIn, _fadeOut = _fadeOut,
            _hideTime = _hideTime, _showTime = _showTime,
            _w = _w, _h = _h, _wVel = _wVel, _hVel = _hVel, _wAcc = _wAcc, _hAcc = _hAcc,
            _spins = _spins, _explicitRot = _explicitRot, _flat = _flat,
            _rotDeg = _rotDeg, _matrixBasis = _matrixBasis, _spinRate = _spinRate,
            _gap = _gap, _blendWord = _blendWord,
            _texFps = _texFps, _frameCount = _frameCount, _wrap = _wrap,
            _atlasCols = _atlasCols, _atlasRows = _atlasRows, _atlasFrames = _atlasFrames,
            _mirroredQuarterUv = _mirroredQuarterUv,
            _key = _key, _sequence = _sequence, _layers = _layers,
            PoolKey = key,
        };
        if (_flat && _mat != null)
        {
            c._mat = (ShaderMaterial)_mat.Duplicate();
            c.MaterialOverride = c._mat;
            c.Layers = Layers;
        }
        if (Batched)
        {
            c.Batched = true;
            c.SetNotifyTransform(false);
        }
        return c;
    }

    private static FxBillboard? BuildFresh(Godot.Collections.Dictionary p, FxPartKey? key)
    {
        var tex0 = Fx.FirstTexture(p);
        if (tex0 == null) return null;
        bool flat = p.ContainsKey("flat") && p["flat"].AsBool();
        var rv = Fx.ReadVec3(p, "rotVel");
        float spin = flat ? rv.Y : rv.X;
        bool autoSpin = p.ContainsKey("autoSpin") && p["autoSpin"].AsBool();
        if (autoSpin && Mathf.Abs(spin) < 1e-4f) spin = Mathf.DegToRad(50f);
        float bundleScale = Fx.ReadF(p, "bundleScale", 1f);
        var bb = new FxBillboard
        {
            CastShadow = ShadowCastingSetting.Off,
            _initPos = Fx.ReadVec3(p, "initPos"),
            _initVel = Fx.ReadVec3(p, "initVel"),
            _accel = Fx.ReadVec3(p, "accel"),
            _start = Fx.ReadF(p, "startTime"),
            _life = Fx.ReadF(p, "life"),
            _fadeIn = Fx.ReadF(p, "fadeIn"),
            _fadeOut = Fx.ReadF(p, "fadeOut"),
            _hideTime = Fx.ReadF(p, "hideTime"),
            _showTime = Fx.ReadF(p, "showTime"),
            _w = Mathf.Max(0.01f, Fx.ReadF(p, "sizeW", 1f) * bundleScale),
            _h = Mathf.Max(0.01f, Fx.ReadF(p, "sizeH", 1f) * bundleScale),
            _spins = Mathf.Abs(spin) > 1e-4f,
            _spinRate = spin,
            _explicitRot = p.ContainsKey("rotEnable") && p["rotEnable"].AsBool(),
            _flat = flat,
            _mirroredQuarterUv = p.ContainsKey("mirroredQuarterUv") && p["mirroredQuarterUv"].AsBool(),
            _gap = Fx.ReadF(p, "gap"),
            _rotDeg = Fx.ReadVec3(p, "rot"),
            _matrixBasis = Fx.ReadKoMatrixBasis(p),
            _texFps = Fx.ReadF(p, "texFPS"),
            _frameCount = Mathf.Max(1, p["frameCount"].AsInt32()),
            _wrap = p.ContainsKey("frameWrap") && p["frameWrap"].AsBool(),
            PoolKey = key,
        };
        int num = p.ContainsKey("num") ? Mathf.Max(1, p["num"].AsInt32()) : 1;
        int blendWord = FxShading.Packed(p, num);
        int rf = FxShading.RenderFlags(p);
        bb._blendWord = blendWord;
        var sv = p.ContainsKey("sizeVel") ? p["sizeVel"].AsGodotArray() : new Godot.Collections.Array();
        var sa = p.ContainsKey("sizeAccel") ? p["sizeAccel"].AsGodotArray() : new Godot.Collections.Array();
        if (sv.Count == 2) { bb._wVel = (float)sv[0].AsDouble() * bundleScale; bb._hVel = (float)sv[1].AsDouble() * bundleScale; }
        if (sa.Count == 2) { bb._wAcc = (float)sa[0].AsDouble() * bundleScale; bb._hAcc = (float)sa[1].AsDouble() * bundleScale; }

        if (flat)
        {
            bb._mat = FxShading.SurfaceMaterial(rf, blendWord, tex0);
            bb.MaterialOverride = bb._mat;
            bb.Layers = FxShading.LayerBit;
        }

        if (p.ContainsKey("atlas"))
        {
            var g = p["atlas"].AsGodotArray();
            bb._atlasCols = Mathf.Max(1, g[0].AsInt32());
            bb._atlasRows = Mathf.Max(1, g[1].AsInt32());
            bb._atlasFrames = p.ContainsKey("atlasFrames")
                ? Mathf.Clamp(p["atlasFrames"].AsInt32(), 1, bb._atlasCols * bb._atlasRows)
                : bb._atlasCols * bb._atlasRows;
            if (flat) bb._mat.SetShaderParameter(FxShading.UvScaleParam, new Vector2(1f / bb._atlasCols, 1f / bb._atlasRows));
        }

        bb._frames = new Texture2D?[bb._frameCount];
        for (int i = 0; i < bb._frameCount; i++) bb._frames[i] = Fx.FrameTexture(p, i);
        if (!flat)
        {
            var (sequence, sequenceId, layers) = FxBoardBatch.SequenceFor(bb._frames);
            if (sequence == null) return null;
            bb._sequence = sequence;
            bb._layers = layers;
            bb._key = new FxBoardBatch.Key(sequenceId, !FxShading.Commutative(blendWord),
                FxShading.DoubleSided(rf), FxShading.NoDepthTest(rf), FxShading.Priority(rf),
                1f / bb._atlasCols, 1f / bb._atlasRows);
            bb.Batched = true;
            bb.SetNotifyTransform(false);
        }
        return bb;
    }

    public override void _EnterTree()
    {
        _root = GetParent() as FxInstance;
        if (_root == null) return;
        _root.AddPart(this);
        SetProcess(false);
    }

    public override void _Ready()
    {
        if (_root != null) SetProcess(false);
    }

    public override void _ExitTree()
    {
        _root?.RemovePart(this);
        _root = null;
    }

    private void SetShown(bool shown)
    {
        if (_shown == shown) return;
        _shown = shown;
        if (!Batched) Visible = shown;
    }

    internal void ResetShown() => _shown = null;

    public void Reset()
    {
        _age = 0f;
        _shown = null;
        _lastColor = new Color(-1, -1, -1, -1);
        _lastFrame = _lastCell = -1;
        Fade = 0f;
        AtlasOffset = Vector2.Zero;
        _batcher = null;
        _batch = null;
        Transparency = 0f;
        Visible = true;
        _done = false;
    }

    public override void _Process(double delta) => Tick(delta);

    public void Tick(double delta)
    {
        using var scope = Perf.Measure(Perf.Section.FxBoard);
        if (_done || Fx.ShuttingDown) return;
        _age += (float)delta;
        float localT = _age - _start;
        if (localT < 0f) { SetShown(false); return; }
        if (!Fx.PartAge(localT, _life, _fadeIn, _fadeOut, out float t, out float a))
        {
            SetShown(false);
            _done = true;
            if (_root == null) SetProcess(false);
            Fx.RecyclePart(this);
            return;
        }
        if (Fx.PartHidden(localT, _hideTime, _showTime)) { SetShown(false); return; }
        SetShown(true);

        Vector3 pos = _initPos + _initVel * t + 0.5f * _accel * (t * t);
        if (_flat && GroundHeight != null)
        {
            if (_root != null)
            {
                _root.EnsureFrame();
                Vector3 world = _root.FrameXf * pos;
                world.Y = GroundY(world.X, world.Z, world.Y) + _gap;
                pos = _root.FrameXfInverse * world;
            }
            else
            {
                var parent = GetParentOrNull<Node3D>();
                Vector3 world = parent != null ? parent.GlobalTransform * pos : pos;
                world.Y = GroundY(world.X, world.Z, world.Y) + _gap;
                pos = parent != null ? parent.ToLocal(world) : world;
            }
        }
        else if (_flat)
            pos += new Vector3(0f, _gap, 0f);

        float w = Mathf.Max(0.001f, _w + _wVel * t + 0.5f * _wAcc * t * t);
        float h = Mathf.Max(0.001f, _h + _hVel * t + 0.5f * _hAcc * t * t);

        if (_flat)
        {
            float spin = _spinRate * t;
            EnsureGroundFan(w, h, spin, pos);
            var fanScale = _fanW > 0f && _fanH > 0f ? new Vector3(w / _fanW, 1f, h / _fanH) : Vector3.One;
            Transform = new Transform3D(new Basis(Vector3.Up, spin - _fanRot) * Basis.FromScale(fanScale), pos);
        }
        else
        {
            Basis basis;
            if (_explicitRot)
            {
                var koBasis = Basis.FromEuler(new Vector3(
                    Mathf.DegToRad(_rotDeg.X), Mathf.DegToRad(_rotDeg.Y), Mathf.DegToRad(_rotDeg.Z)));
                var koQ = koBasis.GetRotationQuaternion();
                var godotQ = new Quaternion(koQ.X, -koQ.Y, -koQ.Z, koQ.W);
                basis = _matrixBasis * new Basis(godotQ);
            }
            else
            {
                bool hasCam;
                Transform3D camXf;
                Basis parentInverse;
                if (_root != null)
                {
                    _root.EnsureFrame();
                    hasCam = _root.HasCam;
                    camXf = _root.CamXf;
                    parentInverse = _root.FrameInverse;
                }
                else
                {
                    hasCam = Fx.FrameCamera(this, out camXf);
                    var parent = GetParentOrNull<Node3D>();
                    parentInverse = (parent?.GlobalTransform.Basis.Orthonormalized() ?? Basis.Identity).Inverse();
                }
                if (hasCam)
                {
                    // View-plane, not look-at-position: FX_CAPTURE_HANDOFF.md measures 963u-apart
                    // quads sharing one normal to 0.00 deg.
                    Basis camBasis = camXf.Basis.Orthonormalized();
                    Basis worldBasis = camBasis * _matrixBasis;
                    if (_spins) worldBasis = worldBasis.Rotated(camBasis.Z, _spinRate * localT);
                    basis = parentInverse * worldBasis;
                }
                else basis = Basis.Identity;
            }
            // NOT Basis.Scaled — it scales the rows, so a yawed quad's width lands on world Z and the
            // disc collapses to a 1-unit lens at yaw 90.
            var local = new Transform3D(basis * Basis.FromScale(new Vector3(w, h, 1f)), pos);
            if (Batched)
            {
                WriteBatched(local, t, a, Mathf.Max(w, h));
                return;
            }
            Transform = local;
        }

        var want = new Color(1f, 1f, 1f, a);
        if (want != _lastColor) { _mat.SetShaderParameter(FxShading.TintParam, want); _lastColor = want; }

        if (_atlasCols * _atlasRows > 1 && _texFps > 0f)
        {
            int cell = AtlasCell(t);
            if (cell != _lastCell)
            {
                _lastCell = cell;
                AtlasOffset = AtlasOffsetFor(cell);
                _mat.SetShaderParameter(FxShading.UvOffsetParam, AtlasOffset);
            }
        }
        else if (_frameCount > 1 && _texFps > 0f)
        {
            int f = FrameIndex(t);
            if (f == _lastFrame) return;
            _lastFrame = f;
            if (_frames[f] is { } texture) _mat.SetShaderParameter(FxShading.TextureParam, texture);
        }
    }

    private int AtlasCell(float t)
    {
        int frame = Mathf.Max(0, (int)(_texFps * t));
        return _wrap ? frame % _atlasFrames : Mathf.Min(frame, _atlasFrames - 1);
    }

    private Vector2 AtlasOffsetFor(int cell) =>
        new((cell % _atlasCols) / (float)_atlasCols, (cell / _atlasCols) / (float)_atlasRows);

    private int FrameIndex(float t)
    {
        int f = (int)(_texFps * t);
        return _wrap ? ((f % _frameCount) + _frameCount) % _frameCount : Mathf.Min(f, _frameCount - 1);
    }

    private void WriteBatched(in Transform3D local, float t, float a, float extent)
    {
        Transform3D world;
        Vector3 camPos;
        bool visible;
        if (_root != null)
        {
            world = _root.FrameXf * local;
            camPos = _root.HasCam ? _root.CamXf.Origin : world.Origin;
            visible = _root.VisibleInTree;
        }
        else
        {
            var parent = GetParentOrNull<Node3D>();
            world = parent != null ? parent.GlobalTransform * local : local;
            camPos = Fx.FrameCamera(this, out var camXf) ? camXf.Origin : world.Origin;
            visible = IsVisibleInTree();
        }
        float keep = 1f - Fade;
        Vector2 uvOffset = Vector2.Zero;
        float frame = 0f;
        if (_atlasCols * _atlasRows > 1 && _texFps > 0f)
        {
            AtlasOffset = AtlasOffsetFor(AtlasCell(t));
            uvOffset = AtlasOffset;
        }
        else if (_layers > 1 && _texFps > 0f)
            frame = FrameIndex(t);
        if (!visible || a <= 0f || keep <= 0f) return;
        _batcher ??= FxBoardBatch.For(this);
        if (_batcher == null) return;
        if (_batch == null || _batch.Pruned) _batch = _batcher.Resolve(_key, _sequence!);
        FxBoardBatch.Write(_batch, world, new Color(keep, keep, keep, a), uvOffset, frame, _blendWord,
            camPos.DistanceSquaredTo(world.Origin), extent);
    }

    private const int GroundFanSegments = 24;
    private const int GroundFanRings = 4;
    private const float GroundFanRebakeScale = 1.5f;
    private const float GroundFanRebakeAngle = Mathf.Pi / 6f;
    private const float GroundFanRebakeMove = 0.5f;
    private const double GroundFanRebakeInterval = 0.1;
    private Vector3 _fanAt;
    private float _fanYaw;
    private double _fanBakedAt;

    private bool GroundFanStale(float w, float h, float rotation, Vector3 centre, float frameYaw)
    {
        if (!_groundFanBuilt) return true;
        double now = Time.GetTicksMsec() * 0.001;
        if (now - _fanBakedAt < GroundFanRebakeInterval) return false;
        float sw = w / _fanW, sh = h / _fanH;
        return sw > GroundFanRebakeScale || sw < 1f / GroundFanRebakeScale
            || sh > GroundFanRebakeScale || sh < 1f / GroundFanRebakeScale
            || Mathf.Abs(Mathf.AngleDifference(rotation, _fanRot)) > GroundFanRebakeAngle
            || Mathf.Abs(Mathf.AngleDifference(frameYaw, _fanYaw)) > GroundFanRebakeAngle
            || centre.DistanceSquaredTo(_fanAt) > GroundFanRebakeMove * GroundFanRebakeMove;
    }

    private void EnsureGroundFan(float w, float h, float rotation, Vector3 pos)
    {
        if (!IsInsideTree()) return;
        _root?.EnsureFrame();
        Transform3D frame = _root != null ? _root.FrameXf
            : GetParentOrNull<Node3D>() is { } parent ? parent.GlobalTransform : Transform3D.Identity;
        Vector3 gp = frame * pos;
        float frameYaw = frame.Basis.Orthonormalized().GetEuler().Y;
        if (!GroundFanStale(w, h, rotation, gp, frameYaw)) return;
        _groundFanBuilt = true;
        _fanW = w; _fanH = h; _fanRot = rotation; _fanAt = gp; _fanYaw = frameYaw;
        _fanBakedAt = Time.GetTicksMsec() * 0.001;
        _groundFan ??= new ArrayMesh();
        _groundFan.ClearSurfaces();
        if (Mesh != _groundFan) Mesh = _groundFan;
        var toWorld = new Basis(Vector3.Up, frameYaw);
        float c = Mathf.Cos(rotation), s = Mathf.Sin(rotation);
        Vector3 right = new(c, 0f, -s);
        Vector3 forward = new(s, 0f, c);

        int rim = GroundFanSegments;
        var verts = new Vector3[1 + GroundFanRings * rim];
        var uvs = new Vector2[verts.Length];
        verts[0] = new Vector3(0f, GroundY(gp.X, gp.Z, gp.Y - _gap) + _gap - gp.Y, 0f);
        uvs[0] = GroundUv(0f, 0f, _mirroredQuarterUv);
        for (int ring = 1; ring <= GroundFanRings; ring++)
        {
            float r = ring / (float)GroundFanRings;
            for (int i = 0; i < rim; i++)
            {
                float ang = i / (float)rim * Mathf.Tau;
                float cx = Mathf.Cos(ang), cz = Mathf.Sin(ang);
                float extent = Mathf.Max(Mathf.Abs(cx), Mathf.Abs(cz));
                cx *= r / extent;
                cz *= r / extent;
                Vector3 off = right * (cx * w * 0.5f) + forward * (cz * h * 0.5f);
                int v = 1 + (ring - 1) * rim + i;
                Vector3 sample = toWorld * off;
                verts[v] = new Vector3(off.X, GroundY(gp.X + sample.X, gp.Z + sample.Z, gp.Y - _gap) + _gap - gp.Y, off.Z);
                uvs[v] = GroundUv(cx, cz, _mirroredQuarterUv);
            }
        }

        var idx = new System.Collections.Generic.List<int>(GroundFanRings * rim * 6);
        for (int i = 0; i < rim; i++)
        {
            idx.Add(0); idx.Add(1 + i); idx.Add(1 + (i + 1) % rim);
        }
        for (int ring = 1; ring < GroundFanRings; ring++)
        {
            int inner = 1 + (ring - 1) * rim, outer = 1 + ring * rim;
            for (int i = 0; i < rim; i++)
            {
                int j = (i + 1) % rim;
                idx.Add(inner + i); idx.Add(outer + i); idx.Add(outer + j);
                idx.Add(inner + i); idx.Add(outer + j); idx.Add(inner + j);
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        _groundFan.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }

    internal static Vector2 GroundUv(float x, float z, bool mirroredQuarter)
        => mirroredQuarter
            ? new Vector2(0.98f - 0.96f * Mathf.Abs(x), 0.98f - 0.96f * Mathf.Abs(z))
            : new Vector2(0.5f - 0.5f * x, 0.5f - 0.5f * z);

    private static float GroundY(float wx, float wz, float fallback)
    {
        var hgt = GroundHeight?.Invoke(wx, wz);
        return hgt ?? fallback;
    }
}
