using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class WeaponTrail : Node3D
{
    public const int Steps = 30;
    private const float FrameStep = 0.111f;
    private const float TeleportJump = 6f;
    private const int Raw = 24;
    private const float InnerEdgeShade = 0.25f;

    private Skeleton3D _skel = null!;
    private int _bone;
    private Transform3D _plug;
    private float _tr0, _tr1;
    private Color _rgb;
    private float _alpha;
    private bool _textured;
    private bool _live;
    private float _fade;
    private AnimationPlayer? _anim;

    private MeshInstance3D _mi = null!;
    private ImmediateMesh _mesh = null!;
    private ShaderMaterial _mat = null!;

    private readonly Vector3[] _a = new Vector3[Raw];
    private readonly Vector3[] _b = new Vector3[Raw];
    private readonly double[] _t = new double[Raw];
    private readonly Vector3[] _lastA = new Vector3[Steps];
    private readonly Vector3[] _lastB = new Vector3[Steps];
    private int _n;
    private string _clip = "";

    public static WeaponTrail? Create(Node3D body, AnimationPlayer? anim, Skeleton3D skel, int bone,
                                      Transform3D plug, float tr0, float tr1, uint traceColor, int element)
    {
        if (tr1 <= tr0) return null;
        var tex = Texture(element);

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
            _textured = tex != null,
            _anim = anim,
        };
        t._mat = FxShading.SurfaceMaterial(FxShading.RfAlphaBlending | Fx.RfDoubleSided | Fx.RfNotZWrite,
            FxBlendMath.Pack(FxBlendMath.SrcAlpha, FxBlendMath.One, true, 1), tex);
        t._mesh = new ImmediateMesh();
        t._mi = new MeshInstance3D
        {
            Mesh = t._mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = FxShading.LayerBit,
        };
        t.AddChild(t._mi);
        t.Visible = false;
        body.AddChild(t);
        return t;
    }

    private bool _shown;

    private void SetShown(bool shown)
    {
        if (_shown == shown) return;
        _shown = shown;
        Visible = shown;
        FxLayer.Users += shown ? 1 : -1;
    }

    public override void _ExitTree() => SetShown(false);

    private const float TraceLeadFrames = 2f;

    public override void _Process(double delta)
    {
        using var scope = Perf.Measure(Perf.Section.Gear);
        if (_anim == null || !GodotObject.IsInstanceValid(_anim)
            || !GodotObject.IsInstanceValid(_skel) || !_skel.IsInsideTree() || !_anim.Active)
        {
            _live = false;
            _fade = 0f;
            SetShown(false);
            return;
        }

        if (!CrowdAnimator.Current(_anim, out string clip, out double pos))
        {
            clip = _anim.CurrentAnimation.ToString();
            pos = clip.Length == 0 ? 0 : _anim.CurrentAnimationPosition;
        }
        if (clip.Length == 0)
        {
            // CurrentAnimationPosition errors outright when nothing is playing, which happens
            // for the frames between an entity spawning and its first clip starting.
            _clip = "";
            _n = 0;
            _live = false;
            Fade(delta);
            return;
        }
        if (clip != _clip) { _clip = clip; _n = 0; _live = false; }

        if (!World.TraceWindow(_anim, clip, out float t0, out float t1, out float fps)
            || pos < t0 - TraceLeadFrames / fps || pos > t1)
        {
            if (_live && _textured && pos > t1) _fade = 1f;
            _live = false;
            Fade(delta);
            return;
        }
        var edge = _skel.GlobalTransform * _skel.GetBoneGlobalPose(_bone) * _plug;
        Push(pos, edge * new Vector3(0f, _tr0, 0f), edge * new Vector3(0f, _tr1, 0f));
        if (pos < t0 || _n < 2)
        {
            Fade(delta);
            return;
        }

        _live = true;
        _fade = 0f;
        Rebuild(pos, FrameStep / fps);
        SetShown(true);
    }

    private void Fade(double delta)
    {
        if (_fade > 0f) _fade = Mathf.Max(0f, _fade - (float)delta * WeaponTrailRule.AfterimageFadePerSecond);
        if (_fade <= 0f)
        {
            SetShown(false);
            return;
        }
        Emit(_fade);
        SetShown(true);
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
        for (int i = 0; i < Steps; i++)
        {
            Sample(Mathf.Max(pos - i * stepSeconds, 0.0), out var a, out var b);
            _lastA[i] = inv * a;
            _lastB[i] = inv * b;
        }
        Emit(1f);
    }

    private void Emit(float alpha)
    {
        _mesh.ClearSurfaces();
        _mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, _mat);
        for (int i = 0; i < Steps; i++)
        {
            StepColours(i, alpha, out var inner, out var outer);
            float u = i / (float)Steps;

            _mesh.SurfaceSetColor(inner);
            _mesh.SurfaceSetUV(new Vector2(u, 0f));
            _mesh.SurfaceAddVertex(_lastA[i]);
            _mesh.SurfaceSetColor(outer);
            _mesh.SurfaceSetUV(new Vector2(u, 1f));
            _mesh.SurfaceAddVertex(_lastB[i]);
        }
        _mesh.SurfaceEnd();
    }

    private void StepColours(int i, float alpha, out Color inner, out Color outer)
    {
        if (_textured)
        {
            inner = outer = new Color(1f, 1f, 1f, alpha);
            return;
        }
        float k = (Steps - i) / (float)Steps;
        outer = new Color(_rgb.R * k, _rgb.G * k, _rgb.B * k, _alpha);
        inner = new Color(outer.R * InnerEdgeShade, outer.G * InnerEdgeShade, outer.B * InnerEdgeShade, _alpha);
    }

    private static readonly string[] TexturePaths =
    {
        "res://assets/fx/tex/weapon_trail.png",
        "res://assets/fx/tex/weapon_trail_fire.png",
        "res://assets/fx/tex/weapon_trail_ice.png",
        "res://assets/fx/tex/weapon_trail_lightning.png",
        "res://assets/fx/tex/weapon_trail_poison.png",
    };
    private static readonly Texture2D?[] Textures = new Texture2D?[WeaponTrailRule.Count];
    private static readonly bool[] TexturesTried = new bool[WeaponTrailRule.Count];

    private static Texture2D? Texture(int element)
    {
        int i = element is >= 0 and < WeaponTrailRule.Count ? element : WeaponTrailRule.Normal;
        if (TexturesTried[i]) return Textures[i];
        TexturesTried[i] = true;
        if (ResourceLoader.Exists(TexturePaths[i])) Textures[i] = ResourceLoader.Load<Texture2D>(TexturePaths[i]);
        return Textures[i];
    }
}
