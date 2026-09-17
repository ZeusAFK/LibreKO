using Godot;
using System.Collections.Generic;

namespace LibreKO;

public partial class FxMesh : MeshInstance3D
{
    private sealed class SurfaceState
    {
        public StandardMaterial3D Mat = null!;
        public Texture2D?[] Frames = System.Array.Empty<Texture2D?>();
        public bool Add;
        public Color BaseColor = Colors.White;
        public bool Opaque, WritesDepth;
    }

    private readonly List<SurfaceState> _surfaces = new();
    private static readonly Dictionary<ulong, bool> OpaqueTextures = new();
    private float _age;
    private Vector3 _initPos, _initVel, _accel, _basePos, _baseScale, _spin, _unitScale, _scaleVel, _scaleAccel;
    private Vector2 _textureMove;
    private Quaternion _baseRot = Quaternion.Identity;
    private float _start, _life, _fadeIn, _fadeOut, _meshFps, _texFps, _hideTime, _showTime;
    private bool _textureLoop, _shapeLoop, _viewFix;
    private int _textureMoveDirection;

    private Vector3[]? _posKeys, _scaleKeys;
    private Quaternion[]? _rotKeys;
    private float _posRate, _rotRate, _scaleRate, _wholeFrame;

    public static FxMesh? Build(Godot.Collections.Dictionary p)
    {
        if (!p.ContainsKey("meshRef") || p["meshRef"].VariantType == Variant.Type.Nil) return null;
        var shape = LoadShape(p["meshRef"].AsString());
        if (shape == null) return null;

        var n = new FxMesh
        {
            CastShadow = ShadowCastingSetting.Off,
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
            _unitScale = ReadVec3Or(p, "unitScale", Vector3.One) * Fx.ReadF(p, "bundleScale", 1f),
            _scaleVel = ReadVec3Or(p, "scaleVel", Vector3.Zero),
            _scaleAccel = ReadVec3Or(p, "scaleAccel", Vector3.Zero),
            _textureMoveDirection = p.ContainsKey("textureMoveDirection") ? p["textureMoveDirection"].AsInt32() : 0,
            _textureMove = ReadVec2Or(p, "textureMove", Vector2.Zero),
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

        var mesh = new ArrayMesh();
        int surf = 0;
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

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Index] = idx;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            var surfState = MakeMeshMaterial(sp, p);
            var mat = surfState.Mat;
            mesh.SurfaceSetMaterial(surf, mat);
            n._surfaces.Add(surfState);
            surf++;
        }
        if (surf == 0) return null;
        n.Mesh = mesh;
        return n;
    }

    private static SurfaceState MakeMeshMaterial(Godot.Collections.Dictionary sp,
                                                 Godot.Collections.Dictionary part)
    {
        bool add = part.ContainsKey("blend")
            ? part["blend"].AsString() == "add"
            : sp["blend"].AsString() == "add";
        Color baseColor = ReadColor(sp);
        var blend = Fx.IsSrcColorOverInv(Fx.SrcBlend(part), Fx.DestBlend(part))
                  ? BaseMaterial3D.BlendModeEnum.PremultAlpha
                  : add ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix;
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = blend,
            DisableFog = blend != BaseMaterial3D.BlendModeEnum.Mix,
            VertexColorUseAsAlbedo = true,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            AlbedoColor = baseColor,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled,
        };
        Fx.ApplyRenderFlags(mat, part);
        var frames = LoadTextures(sp, add, Fx.SrcBlend(part), Fx.DestBlend(part));
        if (frames.Length > 0 && frames[0] != null)
            mat.AlbedoTexture = frames[0];
        bool opaque = blend == BaseMaterial3D.BlendModeEnum.Mix && baseColor.A >= 1f;
        foreach (var frame in frames)
            opaque &= frame != null && IsOpaque(frame);
        return new SurfaceState
        {
            Mat = mat, Frames = frames, Add = add, BaseColor = baseColor, Opaque = opaque,
            WritesDepth = !part.ContainsKey("renderFlags") || (part["renderFlags"].AsInt32() & Fx.RfNotZWrite) == 0,
        };
    }

    private static bool IsOpaque(Texture2D texture)
    {
        ulong id = texture.GetRid().Id;
        if (OpaqueTextures.TryGetValue(id, out bool opaque)) return opaque;
        using var image = texture.GetImage();
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

    private static Texture2D?[] LoadTextures(Godot.Collections.Dictionary sp, bool add, int srcBlend, int destBlend)
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
                var tex = ResourceLoader.Load<Texture2D>(path);
                frames[i] = Fx.TextureForBlend(tex, add, srcBlend, destBlend);
            }
        }
        return frames;
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

        Vector3 pos = _initPos + _initVel * t + 0.5f * _accel * (t * t);
        Vector3 sPos = _basePos, sScale = _baseScale;
        Quaternion sRot = _baseRot;

        if (Fx.PartHidden(localT, _hideTime, _showTime)) { Visible = false; return; }

        if (_wholeFrame > 0.001f && (_posKeys != null || _rotKeys != null || _scaleKeys != null))
        {
            float frame = ShapeFrame(t * _meshFps);
            if (_posKeys != null) sPos = SampleVec(_posKeys, _posRate, frame);
            if (_scaleKeys != null) sScale = SampleVec(_scaleKeys, _scaleRate, frame);
            if (_rotKeys != null) sRot = SampleQuat(_rotKeys, _rotRate, frame);
        }

        Vector3 runtimeScale = _unitScale + _scaleVel * t + 0.5f * _scaleAccel * (t * t);
        runtimeScale = new Vector3(Mathf.Max(0f, runtimeScale.X), Mathf.Max(0f, runtimeScale.Y),
                                   Mathf.Max(0f, runtimeScale.Z));
        Basis orient = Basis.FromEuler(_spin * t);
        if (_viewFix)
        {
            var cam = GetViewport()?.GetCamera3D();
            if (cam != null)
            {
                Vector3 toCam = cam.GlobalPosition - GlobalPosition;
                toCam.Y = 0f;
                if (toCam.LengthSquared() > 1e-6f)
                {
                    // -Z at the camera, not +Z: the KO card's front face is the one seen looking
                    // ALONG its +Z, so pointing +Z at us showed the back and mirrored every glyph.
                    var yaw = new Basis(Vector3.Up, Mathf.Atan2(-toCam.X, -toCam.Z));
                    var parent = GetParentOrNull<Node3D>();
                    Basis parentBasis = parent?.GlobalTransform.Basis.Orthonormalized() ?? Basis.Identity;
                    orient = parentBasis.Inverse() * yaw * orient;
                }
            }
        }
        var partBasis = orient * Basis.FromScale(runtimeScale);
        var shapeBasis = new Basis(sRot) * Basis.FromScale(sScale);
        Transform = new Transform3D(partBasis * shapeBasis, pos + partBasis * sPos);

        int textureFrame = _texFps > 0f ? Mathf.Max(0, (int)(t * _texFps)) : 0;
        foreach (var s in _surfaces)
        {
            bool opaque = s.Opaque && a >= 1f;
            s.Mat.Transparency = opaque ? BaseMaterial3D.TransparencyEnum.Disabled : BaseMaterial3D.TransparencyEnum.Alpha;
            s.Mat.DepthDrawMode = opaque && s.WritesDepth
                ? BaseMaterial3D.DepthDrawModeEnum.OpaqueOnly : BaseMaterial3D.DepthDrawModeEnum.Disabled;
            s.Mat.AlbedoColor = new Color(
                s.BaseColor.R, s.BaseColor.G, s.BaseColor.B, s.BaseColor.A * a);
            if (_textureMoveDirection != 0)
                s.Mat.Uv1Offset = new Vector3(_textureMove.X * t, _textureMove.Y * t, 0f);
            if (s.Frames.Length <= 1) continue;
            int frame = _textureLoop ? textureFrame % s.Frames.Length : Mathf.Min(textureFrame, s.Frames.Length - 1);
            if (s.Frames[frame] != null) s.Mat.AlbedoTexture = s.Frames[frame];
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
