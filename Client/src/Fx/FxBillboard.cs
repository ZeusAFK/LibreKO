using Godot;
using System.Collections.Generic;

namespace LibreKO;

public partial class FxBillboard : MeshInstance3D
{
    private StandardMaterial3D _mat = null!;
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
    private float _addBoost = 1f;
    private bool _additive;
    private float _texFps; private int _frameCount; private bool _wrap;
    private int _atlasCols = 1, _atlasRows = 1;
    private int _atlasFrames = 1;
    private ArrayMesh? _groundFan;
    private bool _groundFanBuilt;
    private bool _mirroredQuarterUv;
    private float _fanW, _fanH, _fanRot;

    public static System.Func<float, float, float?>? GroundHeight;

    public static FxBillboard? Build(Godot.Collections.Dictionary p)
    {
        var tex0 = Fx.FirstTexture(p);
        if (tex0 == null) return null;
        bool flat = p.ContainsKey("flat") && p["flat"].AsBool();
        var rv = Fx.ReadVec3(p, "rotVel");
        float spin = flat ? rv.Y : rv.X;
        bool autoSpin = p.ContainsKey("autoSpin") && p["autoSpin"].AsBool();
        if (autoSpin && Mathf.Abs(spin) < 1e-4f) spin = Mathf.DegToRad(50f);
        var bb = new FxBillboard
        {
            Mesh = new QuadMesh { Size = Vector2.One },
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
            _w = Mathf.Max(0.01f, Fx.ReadF(p, "sizeW", 1f) * Fx.ReadF(p, "bundleScale", 1f)),
            _h = Mathf.Max(0.01f, Fx.ReadF(p, "sizeH", 1f) * Fx.ReadF(p, "bundleScale", 1f)),
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
        };
        int num = p.ContainsKey("num") ? Mathf.Max(1, p["num"].AsInt32()) : 1;
        bb._addBoost = p["blend"].AsString() == "add" ? Mathf.Min(num, 4) : 1f;
        var sv = p.ContainsKey("sizeVel") ? p["sizeVel"].AsGodotArray() : new Godot.Collections.Array();
        var sa = p.ContainsKey("sizeAccel") ? p["sizeAccel"].AsGodotArray() : new Godot.Collections.Array();
        if (sv.Count == 2) { bb._wVel = (float)sv[0].AsDouble(); bb._hVel = (float)sv[1].AsDouble(); }
        if (sa.Count == 2) { bb._wAcc = (float)sa[0].AsDouble(); bb._hAcc = (float)sa[1].AsDouble(); }

        bb._mat = Fx.MakeMaterial(p, BaseMaterial3D.BillboardModeEnum.Disabled, tex0);
        bb.MaterialOverride = bb._mat;

        if (p.ContainsKey("atlas"))
        {
            var g = p["atlas"].AsGodotArray();
            bb._atlasCols = Mathf.Max(1, g[0].AsInt32());
            bb._atlasRows = Mathf.Max(1, g[1].AsInt32());
            bb._atlasFrames = p.ContainsKey("atlasFrames")
                ? Mathf.Clamp(p["atlasFrames"].AsInt32(), 1, bb._atlasCols * bb._atlasRows)
                : bb._atlasCols * bb._atlasRows;
            bb._mat.Uv1Scale = new Vector3(1f / bb._atlasCols, 1f / bb._atlasRows, 1f);
        }

        bool add = p["blend"].AsString() == "add";
        bb._additive = add;
        int srcBlend = Fx.SrcBlend(p), destBlend = Fx.DestBlend(p);
        bb._frames = new Texture2D?[bb._frameCount];
        for (int i = 0; i < bb._frameCount; i++)
        {
            var f = Fx.FrameTexture(p, i);
            bb._frames[i] = f != null ? Fx.TextureForBlend(f, add, srcBlend, destBlend) : null;
        }
        return bb;
    }

    public override void _Process(double delta)
    {
        if (Fx.ShuttingDown || IsQueuedForDeletion()) return;
        _age += (float)delta;
        float localT = _age - _start;
        if (localT < 0f) { Visible = false; return; }
        Visible = true;
        if (!Fx.PartAge(localT, _life, _fadeIn, _fadeOut, out float t, out float a))
        {
            Visible = false;
            SetProcess(false);
            QueueFree();
            return;
        }
        if (Fx.PartHidden(localT, _hideTime, _showTime)) { Visible = false; return; }

        Position = _initPos + _initVel * t + 0.5f * _accel * (t * t);
        if (_flat && GroundHeight != null)
        {
            var parent = GetParentOrNull<Node3D>();
            Vector3 world = parent != null ? parent.GlobalTransform * Position : Position;
            world.Y = GroundY(world.X, world.Z, world.Y) + _gap;
            Position = parent != null ? parent.ToLocal(world) : world;
        }
        else if (_flat)
            Position += new Vector3(0f, _gap, 0f);

        float w = Mathf.Max(0.001f, _w + _wVel * t + 0.5f * _wAcc * t * t);
        float h = Mathf.Max(0.001f, _h + _hVel * t + 0.5f * _hAcc * t * t);

        if (_flat)
        {
            Transform = new Transform3D(Basis.Identity, Position);
            EnsureGroundFan(w, h, _spinRate * t);
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
                var cam = GetViewport()?.GetCamera3D();
                if (cam != null)
                {
                    // View-plane, not look-at-position: FX_CAPTURE_HANDOFF.md measures 963u-apart
                    // quads sharing one normal to 0.00 deg.
                    Basis camBasis = cam.GlobalTransform.Basis.Orthonormalized();
                    Basis worldBasis = camBasis * _matrixBasis;
                    if (_spins) worldBasis = worldBasis.Rotated(camBasis.Z, _spinRate * localT);
                    var parent = GetParentOrNull<Node3D>();
                    Basis parentBasis = parent?.GlobalTransform.Basis.Orthonormalized() ?? Basis.Identity;
                    basis = parentBasis.Inverse() * worldBasis;
                }
                else basis = Basis.Identity;
            }
            // NOT Basis.Scaled — it scales the rows, so a yawed quad's width lands on world Z and the
            // disc collapses to a 1-unit lens at yaw 90.
            Transform = new Transform3D(basis * Basis.FromScale(new Vector3(w, h, 1f)), Position);
        }

        float rgb = _additive ? _addBoost * a : _addBoost;
        var want = new Color(rgb, rgb, rgb, a);
        if (want != _lastColor) { _mat.AlbedoColor = want; _lastColor = want; }

        if (_atlasCols * _atlasRows > 1 && _texFps > 0f)
        {
            int frame = Mathf.Max(0, (int)(_texFps * t));
            int cell = _wrap ? frame % _atlasFrames : Mathf.Min(frame, _atlasFrames - 1);
            _mat.Uv1Offset = new Vector3((cell % _atlasCols) / (float)_atlasCols,
                                         (cell / _atlasCols) / (float)_atlasRows, 0f);
        }
        else if (_frameCount > 1 && _texFps > 0f)
        {
            int f = (int)(_texFps * t);
            f = _wrap ? ((f % _frameCount) + _frameCount) % _frameCount : Mathf.Min(f, _frameCount - 1);
            if (_frames[f] != null) _mat.AlbedoTexture = _frames[f];
        }
    }

    private const int GroundFanSegments = 24;
    private const int GroundFanRings = 4;

    private void EnsureGroundFan(float w, float h, float rotation)
    {
        if (!IsInsideTree()) return;
        if (_groundFanBuilt && Mathf.IsEqualApprox(w, _fanW) && Mathf.IsEqualApprox(h, _fanH)
            && Mathf.IsEqualApprox(rotation, _fanRot))
            return;
        _groundFanBuilt = true;
        _fanW = w; _fanH = h; _fanRot = rotation;
        _groundFan ??= new ArrayMesh();
        _groundFan.ClearSurfaces();
        if (Mesh != _groundFan) Mesh = _groundFan;

        Vector3 gp = GlobalPosition;
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
                verts[v] = new Vector3(off.X, GroundY(gp.X + off.X, gp.Z + off.Z, gp.Y - _gap) + _gap - gp.Y, off.Z);
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
