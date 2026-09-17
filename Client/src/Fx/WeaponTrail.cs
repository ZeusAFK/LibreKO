using Godot;

namespace LibreKO;

public partial class WeaponTrail : Node3D
{
    public const int Steps = 30;
    private const float FrameStep = 0.111f;
    private const float TeleportJump = 6f;
    private const int Raw = 24;

    private Skeleton3D _skel = null!;
    private int _bone;
    private Transform3D _plug;
    private float _tr0, _tr1;
    private Color _rgb;
    private float _alpha;
    private AnimationPlayer? _anim;

    private MeshInstance3D _mi = null!;
    private ImmediateMesh _mesh = null!;
    private StandardMaterial3D _mat = null!;

    private readonly Vector3[] _a = new Vector3[Raw];
    private readonly Vector3[] _b = new Vector3[Raw];
    private readonly double[] _t = new double[Raw];
    private int _n;
    private string _clip = "";

    public static WeaponTrail? Create(Node3D body, AnimationPlayer? anim, Skeleton3D skel, int bone,
                                      Transform3D plug, float tr0, float tr1, uint traceColor)
    {
        if (tr1 <= tr0) return null;
        var tex = Texture();
        if (tex == null) return null;

        var t = new WeaponTrail
        {
            Name = "weapon_trail",
            _skel = skel,
            _bone = bone,
            _plug = plug,
            _tr0 = tr0,
            _tr1 = tr1,
            _rgb = new Color(((traceColor >> 16) & 0xFF) / 255f, ((traceColor >> 8) & 0xFF) / 255f,
                             (traceColor & 0xFF) / 255f),
            _alpha = ((traceColor >> 24) & 0xFF) / 255f,
            _anim = anim,
        };
        t._mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = tex,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            DisableReceiveShadows = true,
        };
        t._mesh = new ImmediateMesh();
        t._mi = new MeshInstance3D { Mesh = t._mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        t.AddChild(t._mi);
        t.Visible = false;
        body.AddChild(t);
        return t;
    }

    public override void _Process(double delta)
    {
        if (_anim == null || !GodotObject.IsInstanceValid(_anim)
            || !GodotObject.IsInstanceValid(_skel) || !_skel.IsInsideTree())
        {
            Visible = false;
            return;
        }

        string clip = _anim.CurrentAnimation.ToString();
        if (clip.Length == 0)
        {
            // CurrentAnimationPosition errors outright when nothing is playing, which happens
            // for the frames between an entity spawning and its first clip starting.
            _clip = "";
            _n = 0;
            Visible = false;
            return;
        }
        if (clip != _clip) { _clip = clip; _n = 0; }

        double pos = _anim.CurrentAnimationPosition;
        var edge = _skel.GlobalTransform * _skel.GetBoneGlobalPose(_bone) * _plug;
        Push(pos, edge * new Vector3(0f, _tr0, 0f), edge * new Vector3(0f, _tr1, 0f));

        if (!World.TraceWindow(_anim, clip, out float t0, out float t1, out float fps)
            || pos < t0 || pos > t1 || _n < 2)
        {
            Visible = false;
            return;
        }

        Rebuild(pos, FrameStep / fps);
        Visible = true;
    }

    private void Push(double at, Vector3 a, Vector3 b)
    {
        float jump = (_tr1 - _tr0) * TeleportJump;
        if (_n > 0 && (_a[_n - 1].DistanceSquaredTo(a) > jump * jump || at < _t[_n - 1])) _n = 0;
        if (_n == Raw)
        {
            System.Array.Copy(_a, 1, _a, 0, Raw - 1);
            System.Array.Copy(_b, 1, _b, 0, Raw - 1);
            System.Array.Copy(_t, 1, _t, 0, Raw - 1);
            _n--;
        }
        _a[_n] = a; _b[_n] = b; _t[_n] = at; _n++;
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float s)
    {
        float s2 = s * s, s3 = s2 * s;
        return 0.5f * ((2f * p1) + (-p0 + p2) * s
             + (2f * p0 - 5f * p1 + 4f * p2 - p3) * s2
             + (-p0 + 3f * p1 - 3f * p2 + p3) * s3);
    }

    private void Sample(double at, out Vector3 a, out Vector3 b)
    {
        int i = _n - 1;
        while (i > 0 && _t[i - 1] > at) i--;
        if (i <= 0) { a = _a[0]; b = _b[0]; return; }

        double span = _t[i] - _t[i - 1];
        float s = span > 1e-6 ? Mathf.Clamp((float)((at - _t[i - 1]) / span), 0f, 1f) : 0f;
        int p0 = Mathf.Max(i - 2, 0), p3 = Mathf.Min(i + 1, _n - 1);
        a = CatmullRom(_a[p0], _a[i - 1], _a[i], _a[p3], s);
        b = CatmullRom(_b[p0], _b[i - 1], _b[i], _b[p3], s);
    }

    private void Rebuild(double pos, double stepSeconds)
    {
        var inv = GlobalTransform.AffineInverse();
        _mesh.ClearSurfaces();
        _mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, _mat);
        for (int i = 0; i < Steps; i++)
        {
            Sample(Mathf.Max(pos - i * stepSeconds, 0.0), out var a, out var b);
            float k = (Steps - i) / (float)Steps;
            var outer = new Color(_rgb.R * k, _rgb.G * k, _rgb.B * k, _alpha);
            var inner = new Color(outer.R * 0.25f, outer.G * 0.25f, outer.B * 0.25f, _alpha);
            float u = i / (float)Steps;

            _mesh.SurfaceSetColor(inner);
            _mesh.SurfaceSetUV(new Vector2(u, 0f));
            _mesh.SurfaceAddVertex(inv * a);
            _mesh.SurfaceSetColor(outer);
            _mesh.SurfaceSetUV(new Vector2(u, 1f));
            _mesh.SurfaceAddVertex(inv * b);
        }
        _mesh.SurfaceEnd();
    }

    private static Texture2D? _tex;
    private static bool _texTried;
    private static Texture2D? Texture()
    {
        if (_texTried) return _tex;
        _texTried = true;
        const string path = "res://assets/fx/tex/weapon_trail.png";
        if (ResourceLoader.Exists(path)) _tex = ResourceLoader.Load<Texture2D>(path);
        return _tex;
    }
}
