using Godot;
using System.Collections.Generic;

namespace LibreKO;

public partial class FxMesh : MeshInstance3D, IFxPooledPart, IFxPart
{
    private sealed class SurfaceState
    {
        public ShaderMaterial Transparent = null!;
        public ShaderMaterial? Opaque;
        public ShaderMaterial? LayerDepth;
        public int FrameCount;
        public bool WritesDepth;
        public Material? Current;
    }

    private const int TriangleCorners = 3;
    private const float ScaleStepSeconds = 1f / 90f;

    private float _lastFade = float.NaN;
    private Vector2 _lastUv = new(float.NaN, float.NaN);
    private int _lastTextureFrame = -1;
    private bool _animated;

    private readonly List<SurfaceState> _surfaces = new();
    private FxInstance? _root;
    private bool? _shown;
    private bool _done;
    private Vector3 _localOrigin;
    private static readonly Dictionary<ulong, bool> OpaqueTextures = new();
    private float _age;
    private Vector3 _initPos, _initVel, _accel, _basePos, _baseScale, _spin, _unitScale, _scaleVel, _scaleAccel;
    private Vector2 _textureMove;
    private Quaternion _baseRot = Quaternion.Identity;
    private float _start, _life, _fadeIn, _fadeOut, _meshFps, _texFps, _hideTime, _showTime, _sizeScale = 1f;
    private bool _textureLoop, _shapeLoop, _viewFix;
    private int _textureMoveDirection;

    private Vector3[]? _posKeys, _scaleKeys;
    private Quaternion[]? _rotKeys;
    private float _posRate, _rotRate, _scaleRate, _wholeFrame;

    public FxPartKey? PoolKey { get; set; }

    public void Reset()
    {
        _age = 0f;
        _shown = null;
        foreach (var surface in _surfaces) surface.Current = null;
        _lastFade = float.NaN;
        _lastUv = new Vector2(float.NaN, float.NaN);
        _lastTextureFrame = -1;
        Transparency = 0f;
        Visible = true;
        _done = false;
    }

    private static readonly Dictionary<FxPartKey, FxMesh> _prototypes = Shutdown.Track(new Dictionary<FxPartKey, FxMesh>());

    public static FxMesh? Build(Godot.Collections.Dictionary p, FxPartKey? key = null)
    {
        if (key is { } pooledKey)
        {
            if (Fx.TakePooledPart(pooledKey) is FxMesh pooled) return pooled;
            if (_prototypes.TryGetValue(pooledKey, out var prototype)) return prototype.Clone(pooledKey);
        }
        var built = BuildFresh(p, key);
        if (built != null && key is { } protoKey) _prototypes[protoKey] = built.Clone(protoKey);
        return built;
    }

    private FxMesh Clone(FxPartKey? key)
    {
        var c = new FxMesh
        {
            CastShadow = ShadowCastingSetting.Off,
            Layers = FxShading.LayerBit,
            Mesh = Mesh,
            _initPos = _initPos, _initVel = _initVel, _accel = _accel, _spin = _spin,
            _start = _start, _life = _life, _fadeIn = _fadeIn, _fadeOut = _fadeOut,
            _meshFps = _meshFps, _texFps = _texFps, _hideTime = _hideTime, _showTime = _showTime,
            _textureLoop = _textureLoop, _shapeLoop = _shapeLoop, _viewFix = _viewFix,
            _unitScale = _unitScale, _scaleVel = _scaleVel, _scaleAccel = _scaleAccel, _sizeScale = _sizeScale,
            _textureMoveDirection = _textureMoveDirection, _textureMove = _textureMove,
            _basePos = _basePos, _baseScale = _baseScale, _baseRot = _baseRot, _wholeFrame = _wholeFrame,
            _posKeys = _posKeys, _posRate = _posRate, _scaleKeys = _scaleKeys, _scaleRate = _scaleRate,
            _rotKeys = _rotKeys, _rotRate = _rotRate, _animated = _animated,
            PoolKey = key,
        };
        for (int i = 0; i < _surfaces.Count; i++)
        {
            var s = _surfaces[i];
            c._surfaces.Add(new SurfaceState
            {
                Transparent = s.Transparent, Opaque = s.Opaque, LayerDepth = s.LayerDepth,
                FrameCount = s.FrameCount, WritesDepth = s.WritesDepth, Current = s.Transparent,
            });
            c.SetSurfaceOverrideMaterial(i, s.Transparent);
        }
        return c;
    }

    private static FxMesh? BuildFresh(Godot.Collections.Dictionary p, FxPartKey? key)
    {
        if (!p.ContainsKey("meshRef") || p["meshRef"].VariantType == Variant.Type.Nil) return null;
        var shape = LoadShape(p["meshRef"].AsString());
        if (shape == null) return null;

        var n = new FxMesh
        {
            CastShadow = ShadowCastingSetting.Off,
            Layers = FxShading.LayerBit,
            _initPos = Fx.ReadVec3(p, "initPos"),
            _initVel = Fx.ReadVec3(p, "initVel"),
            _accel = Fx.ReadVec3(p, "accel"),
            _spin = Fx.ReadVec3(p, "rotVel"),
            _start = Fx.ReadF(p, "startTime"),
            _life = Fx.ReadF(p, "life"),
            _fadeIn = Fx.ReadF(p, "fadeIn"),
            _fadeOut = Fx.ReadF(p, "fadeOut"),
            _meshFps = Mathf.Max(0.001f, Fx.ReadF(p, "meshFps", 30f)),
            _texFps = Fx.ReadF(p, "texFPS"),
            _hideTime = Fx.ReadF(p, "hideTime"),
            _showTime = Fx.ReadF(p, "showTime"),
            _textureLoop = ReadBool(p, "textureLoop"),
            _shapeLoop = ReadBool(p, "shapeLoop"),
            _viewFix = ReadBool(p, "viewFix"),
            _unitScale = ReadVec3Or(p, "unitScale", Vector3.One),
            _sizeScale = Fx.ReadF(p, "bundleScale", 1f),
            _scaleVel = ReadVec3Or(p, "scaleVel", Vector3.Zero),
            _scaleAccel = ReadVec3Or(p, "scaleAccel", Vector3.Zero),
            _textureMoveDirection = p.ContainsKey("textureMoveDirection") ? p["textureMoveDirection"].AsInt32() : 0,
            _textureMove = ReadVec2Or(p, "textureMove", Vector2.Zero),
            PoolKey = key,
        };

        var basev = shape["base"].AsGodotDictionary();
        n._basePos = n._initPos;
        n._baseScale = ReadVec3Or(basev, "scale", Vector3.One);
        var rq = basev["rot"].AsGodotArray();
        if (rq.Count == 4)
            n._baseRot = new Quaternion((float)rq[0].AsDouble(), (float)rq[1].AsDouble(),
                                        (float)rq[2].AsDouble(), (float)rq[3].AsDouble()).Normalized();
        n._wholeFrame = (float)shape["wholeFrame"].AsDouble();
        n._posKeys = ReadVecKeys(shape, "posKeys"); n._posRate = KeyRate(shape, "posRate");
        n._scaleKeys = ReadVecKeys(shape, "scaleKeys"); n._scaleRate = KeyRate(shape, "scaleRate");
        n._rotKeys = ReadQuatKeys(shape, "rotKeys"); n._rotRate = KeyRate(shape, "rotRate");

        var (mesh, surfaceParts) = SharedShapeMesh(p["meshRef"].AsString(), shape);
        if (mesh == null || surfaceParts.Count == 0) return null;
        n.Mesh = mesh;
        for (int surf = 0; surf < surfaceParts.Count; surf++)
        {
            var surfState = MakeMeshMaterial(surfaceParts[surf], p, n._textureLoop);
            n.SetSurfaceOverrideMaterial(surf, surfState.Transparent);
            surfState.Current = surfState.Transparent;
            n._surfaces.Add(surfState);
            if (surfState.FrameCount > 1) n._animated = true;
        }
        return n;
    }

    private static readonly Dictionary<string, (ArrayMesh? Mesh, List<Godot.Collections.Dictionary> Parts)> _meshCache = Shutdown.Track(new Dictionary<string, (ArrayMesh? Mesh, List<Godot.Collections.Dictionary> Parts)>());

    private static (ArrayMesh? Mesh, List<Godot.Collections.Dictionary> Parts) SharedShapeMesh(
        string meshRef, Godot.Collections.Dictionary shape)
    {
        if (_meshCache.TryGetValue(meshRef, out var cached)) return cached;
        using var scope = Perf.Measure(Perf.Section.FxBuildShape);
        var mesh = new ArrayMesh();
        var parts = new List<Godot.Collections.Dictionary>();
        foreach (var pv in shape["parts"].AsGodotArray())
        {
            var sp = pv.AsGodotDictionary();
            var posArr = sp["pos"].AsGodotArray();
            var uvArr = sp["uv"].AsGodotArray();
            var idxArr = sp["idx"].AsGodotArray();
            int vcount = posArr.Count / 3;
            if (vcount < 3 || idxArr.Count < 3) continue;
            var verts = new Vector3[vcount];
            var uvs = new Vector2[vcount];
            var pivot = Fx.ReadVec3(sp, "pivot");
            for (int i = 0; i < vcount; i++)
            {
                verts[i] = new Vector3((float)posArr[i * 3].AsDouble(), (float)posArr[i * 3 + 1].AsDouble(),
                                       (float)posArr[i * 3 + 2].AsDouble()) + pivot;
                uvs[i] = new Vector2((float)uvArr[i * 2].AsDouble(), (float)uvArr[i * 2 + 1].AsDouble());
            }
            var idx = new int[idxArr.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = idxArr[i].AsInt32();
            for (int i = 0; i + TriangleCorners - 1 < idx.Length; i += TriangleCorners)
                (idx[i + 1], idx[i + 2]) = (idx[i + 2], idx[i + 1]);

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Index] = idx;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            parts.Add(sp);
        }
        var entry = (parts.Count > 0 ? mesh : null, parts);
        _meshCache[meshRef] = entry;
        return entry;
    }

    private static SurfaceState MakeMeshMaterial(Godot.Collections.Dictionary sp,
                                                 Godot.Collections.Dictionary part, bool loop)
    {
        Color baseColor = ReadColor(sp);
        int blendWord = FxShading.Packed(part);
        var (srcBlend, destBlend, blended, _) = LibreKO.Domain.FxBlendMath.Unpack(blendWord);
        int rf = FxShading.RenderFlags(part);
        bool writesDepth = !part.ContainsKey("renderFlags") || FxShading.WritesDepth(rf);
        var frames = LoadTextures(sp);
        Texture2DArray? sequence = null;
        ulong sequenceId = 0;
        if (frames.Length > 0)
        {
            var (array, id, _) = FxBoardBatch.SequenceFor(frames);
            sequence = array;
            sequenceId = id;
        }
        bool opaque = baseColor.A >= 1f;
        if (blended)
        {
            opaque &= srcBlend == LibreKO.Domain.FxBlendMath.SrcAlpha && destBlend == LibreKO.Domain.FxBlendMath.InvSrcAlpha;
            foreach (var frame in frames)
                opaque &= frame != null && IsOpaque(frame);
        }
        var look = new FxMeshMaterial.Look(
            sequenceId, sequence != null ? frames.Length : 0, loop, blendWord,
            FxShading.DoubleSided(rf), FxShading.NoDepthTest(rf), FxShading.Priority(rf), baseColor);
        return new SurfaceState
        {
            Transparent = FxMeshMaterial.For(look, sequence, FxMeshMaterial.Variant.Blended),
            Opaque = opaque ? FxMeshMaterial.For(look, sequence, FxMeshMaterial.Variant.Opaque) : null,
            LayerDepth = writesDepth ? FxMeshMaterial.For(look, sequence, FxMeshMaterial.Variant.LayerDepth) : null,
            FrameCount = look.Frames,
            WritesDepth = writesDepth,
        };
    }

    internal bool SurfaceOpaque(int index) =>
        index < _surfaces.Count && _surfaces[index].Opaque is { } opaque && ReferenceEquals(_surfaces[index].Current, opaque);

    internal bool SurfaceWritesDepth(int index) => index < _surfaces.Count && _surfaces[index].WritesDepth;

    private static bool IsOpaque(Texture2D texture)
    {
        ulong id = texture.GetRid().Id;
        if (OpaqueTextures.TryGetValue(id, out bool opaque)) return opaque;
        using var scope = Perf.Measure(Perf.Section.SpawnOpacity);
        using var image = FxImages.Read(texture);
        if (image == null) return false;
        if (image.IsCompressed()) image.Decompress();
        opaque = image.DetectAlpha() == Image.AlphaMode.None;
        OpaqueTextures[id] = opaque;
        return opaque;
    }

    private static Color ReadColor(Godot.Collections.Dictionary sp)
    {
        if (!sp.ContainsKey("color") || sp["color"].VariantType != Variant.Type.Array)
            return Colors.White;
        var c = sp["color"].AsGodotArray();
        return c.Count == 4
            ? new Color((float)c[0].AsDouble(), (float)c[1].AsDouble(),
                        (float)c[2].AsDouble(), (float)c[3].AsDouble())
            : Colors.White;
    }

    private static Texture2D?[] LoadTextures(Godot.Collections.Dictionary sp)
    {
        var stems = new List<string>();
        if (sp.ContainsKey("texs") && sp["texs"].VariantType == Variant.Type.Array)
        {
            foreach (var v in sp["texs"].AsGodotArray())
                if (v.VariantType != Variant.Type.Nil) stems.Add(v.AsString());
        }
        else if (sp.ContainsKey("tex") && sp["tex"].VariantType != Variant.Type.Nil)
        {
            stems.Add(sp["tex"].AsString());
        }

        var frames = new Texture2D?[stems.Count];
        for (int i = 0; i < stems.Count; i++)
        {
            string path = $"res://assets/fx/tex/{stems[i]}.png";
            if (ResourceLoader.Exists(path))
            {
                using var scope = Perf.Measure(Perf.Section.FxLoadTexture);
                frames[i] = ResourceLoader.Load<Texture2D>(path);
            }
        }
        return frames;
    }

    private Viewport? _viewport;

    public override void _EnterTree()
    {
        _viewport = GetViewport();
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
        Visible = shown;
    }

    internal void ResetShown() => _shown = null;

    public override void _Process(double delta) => Tick(delta);

    public void Tick(double delta)
    {
        using var scope = Perf.Measure(Perf.Section.FxMesh);
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
        Vector3 sPos = _basePos, sScale = _baseScale;
        Quaternion sRot = _baseRot;

        if (_wholeFrame > 0.001f && (_posKeys != null || _rotKeys != null || _scaleKeys != null))
        {
            float frame = ShapeFrame(t * _meshFps);
            if (_posKeys != null) sPos = SampleVec(_posKeys, _posRate, frame);
            if (_scaleKeys != null) sScale = SampleVec(_scaleKeys, _scaleRate, frame);
            if (_rotKeys != null) sRot = SampleQuat(_rotKeys, _rotRate, frame);
        }

        float swell = t * t * (t + ScaleStepSeconds) / (2f * ScaleStepSeconds);
        Vector3 runtimeScale = (_unitScale + _scaleVel * t + _scaleAccel * swell) * _sizeScale;
        runtimeScale = new Vector3(Mathf.Max(0f, runtimeScale.X), Mathf.Max(0f, runtimeScale.Y),
                                   Mathf.Max(0f, runtimeScale.Z));
        Basis orient = Basis.FromEuler(_spin * t);
        if (_viewFix)
        {
            bool hasCam;
            Transform3D camXf;
            Basis parentInverse;
            Vector3 worldPos;
            if (_root != null)
            {
                _root.EnsureFrame();
                hasCam = _root.HasCam;
                camXf = _root.CamXf;
                parentInverse = _root.FrameInverse;
                worldPos = _root.FrameXf * _localOrigin;
            }
            else
            {
                hasCam = Fx.FrameCamera(this, out camXf);
                var parent = GetParentOrNull<Node3D>();
                parentInverse = (parent?.GlobalTransform.Basis.Orthonormalized() ?? Basis.Identity).Inverse();
                worldPos = GlobalPosition;
            }
            if (hasCam)
            {
                Vector3 toCam = camXf.Origin - worldPos;
                toCam.Y = 0f;
                if (toCam.LengthSquared() > 1e-6f)
                {
                    // -Z at the camera, not +Z: the KO card's front face is the one seen looking
                    // ALONG its +Z, so pointing +Z at us showed the back and mirrored every glyph.
                    var yaw = new Basis(Vector3.Up, Mathf.Atan2(-toCam.X, -toCam.Z));
                    orient = parentInverse * yaw * orient;
                }
            }
        }
        var partBasis = orient * Basis.FromScale(runtimeScale);
        var shapeBasis = new Basis(sRot) * Basis.FromScale(sScale);
        _localOrigin = pos + partBasis * sPos;
        Transform = new Transform3D(partBasis * shapeBasis, _localOrigin);

        if (a != _lastFade)
        {
            _lastFade = a;
            SetInstanceShaderParameter(FxMeshMaterial.FadeParam, a);
        }
        if (_textureMoveDirection != 0)
        {
            var uv = new Vector2(_textureMove.X * t, _textureMove.Y * t);
            if (uv != _lastUv)
            {
                _lastUv = uv;
                SetInstanceShaderParameter(FxMeshMaterial.UvOffsetParam, uv);
            }
        }
        if (_animated && _texFps > 0f)
        {
            int textureFrame = Mathf.Max(0, (int)(t * _texFps));
            if (textureFrame != _lastTextureFrame)
            {
                _lastTextureFrame = textureFrame;
                SetInstanceShaderParameter(FxMeshMaterial.FrameParam, (float)textureFrame);
            }
        }
        for (int i = 0; i < _surfaces.Count; i++)
        {
            var s = _surfaces[i];
            Material want = s.LayerDepth != null && FxLayer.DrawsEffectsIn(_viewport) ? s.LayerDepth
                : s.Opaque != null && a >= 1f ? s.Opaque
                : s.Transparent;
            if (ReferenceEquals(s.Current, want)) continue;
            s.Current = want;
            SetSurfaceOverrideMaterial(i, want);
        }
    }

    private float ShapeFrame(float frame)
    {
        if (_wholeFrame <= 1.0f) return 0f;
        float span = _wholeFrame - 1.0f;
        if (_shapeLoop)
            frame = Mathf.PosMod(frame, span);
        else
        {
            frame = Mathf.Min(frame, span);
        }
        return Mathf.Max(0f, frame);
    }

    private static Vector3 SampleVec(Vector3[] keys, float rate, float frame)
    {
        float kp = frame * rate / 30f;
        int i = Mathf.Clamp((int)kp, 0, keys.Length - 1);
        int j = Mathf.Min(i + 1, keys.Length - 1);
        return keys[i].Lerp(keys[j], Mathf.Clamp(kp - i, 0f, 1f));
    }

    private static Quaternion SampleQuat(Quaternion[] keys, float rate, float frame)
    {
        float kp = frame * rate / 30f;
        int i = Mathf.Clamp((int)kp, 0, keys.Length - 1);
        int j = Mathf.Min(i + 1, keys.Length - 1);
        return keys[i].Slerp(keys[j], Mathf.Clamp(kp - i, 0f, 1f));
    }

    private static readonly Dictionary<string, Godot.Collections.Dictionary?> _shapeCache = new();

    internal static Godot.Collections.Dictionary? LoadShapeJson(string meshRef) => LoadShape(meshRef);

    internal static Vector3[]? VecKeys(Godot.Collections.Dictionary shape, string key) =>
        ReadVecKeys(shape, key);

    internal static Quaternion[]? QuatKeys(Godot.Collections.Dictionary shape, string key) =>
        ReadQuatKeys(shape, key);

    internal static float Rate(Godot.Collections.Dictionary shape, string trackKey, string rateKey) =>
        shape.ContainsKey(trackKey) && shape[trackKey].VariantType != Variant.Type.Nil
            ? KeyRate(shape, rateKey)
            : 30f;

    private static Godot.Collections.Dictionary? LoadShape(string meshRef)
    {
        if (_shapeCache.TryGetValue(meshRef, out var cached)) return cached;
        using var scope = Perf.Measure(Perf.Section.FxLoadShape);
        string path = $"res://assets/fx/mesh/{meshRef}.json";
        Godot.Collections.Dictionary? dict = null;
        if (Godot.FileAccess.FileExists(path))
        {
            using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (f != null)
            {
                var parsed = Json.ParseString(f.GetAsText());
                if (parsed.VariantType == Variant.Type.Dictionary) dict = parsed.AsGodotDictionary();
            }
        }
        _shapeCache[meshRef] = dict;
        return dict;
    }

    private static Vector3 ReadVec3Or(Godot.Collections.Dictionary d, string key, Vector3 def)
    {
        if (!d.ContainsKey(key)) return def;
        var a = d[key].AsGodotArray();
        return a.Count == 3 ? new Vector3((float)a[0].AsDouble(), (float)a[1].AsDouble(), (float)a[2].AsDouble()) : def;
    }

    private static Vector2 ReadVec2Or(Godot.Collections.Dictionary d, string key, Vector2 def)
    {
        if (!d.ContainsKey(key)) return def;
        var a = d[key].AsGodotArray();
        return a.Count == 2 ? new Vector2((float)a[0].AsDouble(), (float)a[1].AsDouble()) : def;
    }

    private static float KeyRate(Godot.Collections.Dictionary d, string key) =>
        d.ContainsKey(key) && d[key].VariantType != Variant.Type.Nil ? (float)d[key].AsDouble() : 30f;

    private static bool ReadBool(Godot.Collections.Dictionary d, string key) =>
        d.ContainsKey(key) && d[key].VariantType != Variant.Type.Nil && d[key].AsBool();

    private static Vector3[]? ReadVecKeys(Godot.Collections.Dictionary d, string key)
    {
        if (!d.ContainsKey(key) || d[key].VariantType == Variant.Type.Nil) return null;
        var arr = d[key].AsGodotArray();
        if (arr.Count == 0) return null;
        var outv = new Vector3[arr.Count];
        for (int i = 0; i < arr.Count; i++)
        {
            var v = arr[i].AsGodotArray();
            outv[i] = new Vector3((float)v[0].AsDouble(), (float)v[1].AsDouble(), (float)v[2].AsDouble());
        }
        return outv;
    }

    private static Quaternion[]? ReadQuatKeys(Godot.Collections.Dictionary d, string key)
    {
        if (!d.ContainsKey(key) || d[key].VariantType == Variant.Type.Nil) return null;
        var arr = d[key].AsGodotArray();
        if (arr.Count == 0) return null;
        var outq = new Quaternion[arr.Count];
        for (int i = 0; i < arr.Count; i++)
        {
            var v = arr[i].AsGodotArray();
            outq[i] = new Quaternion((float)v[0].AsDouble(), (float)v[1].AsDouble(),
                                     (float)v[2].AsDouble(), (float)v[3].AsDouble()).Normalized();
        }
        return outq;
    }
}
