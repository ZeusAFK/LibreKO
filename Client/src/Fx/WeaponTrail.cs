using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class WeaponTrail : Node3D, IFxTicked
{
    public int FxSlot { get; set; } = FxTickLane.Unlisted;

    public const int Steps = 30;
    private const float FrameStep = 0.111f;
    private const float TeleportJump = 6f;
    private const int Raw = 24;
    private const float InnerEdgeShade = 0.25f;

    private Skeleton3D skel = null!;
    private int bone;
    private Transform3D plug;
    private float tr0, tr1;
    private Color rgb;
    private float alpha;
    private bool textured;
    private bool live;
    private float fade;
    private AnimationPlayer? anim;

    private MeshInstance3D meshInstance = null!;
    private ImmediateMesh mesh = null!;
    private ShaderMaterial mat = null!;

    private readonly Vector3[] innerEdge = new Vector3[Raw];
    private readonly Vector3[] outerEdge = new Vector3[Raw];
    private readonly double[] times = new double[Raw];
    private readonly Vector3[] lastA = new Vector3[Steps];
    private readonly Vector3[] lastB = new Vector3[Steps];
    private int count;
    private string clip = "";

    public static WeaponTrail? Create(Node3D body, AnimationPlayer? anim, Skeleton3D skel, int bone,
                                      Transform3D plug, float tr0, float tr1, uint traceColor, int element)
    {
        if (tr1 <= tr0) return null;
        var tex = Texture(element);

        var t = new WeaponTrail
        {
            Name = "weapon_trail",
            skel = skel,
            bone = bone,
            plug = plug,
            tr0 = tr0,
            tr1 = tr1,
            rgb = new Color(((traceColor >> 16) & 0xFF) / 255f, ((traceColor >> 8) & 0xFF) / 255f,
                             (traceColor & 0xFF) / 255f),
            alpha = ((traceColor >> 24) & 0xFF) / 255f,
            textured = tex != null,
            anim = anim,
        };
        t.mat = FxShading.SurfaceMaterial(FxShading.RfAlphaBlending | Fx.RfDoubleSided | Fx.RfNotZWrite,
            FxBlendMath.Pack(FxBlendMath.SrcAlpha, FxBlendMath.One, true, 1), tex);
        t.mesh = new ImmediateMesh();
        t.meshInstance = new MeshInstance3D
        {
            Mesh = t.mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = FxShading.LayerBit,
        };
        t.AddChild(t.meshInstance);
        t.Visible = false;
        body.AddChild(t);
        return t;
    }

    private bool shown;

    private void SetShown(bool shown)
    {
        if (this.shown == shown) return;
        this.shown = shown;
        Visible = shown;
        FxLayer.Users += shown ? 1 : -1;
    }

    public override void _EnterTree() => FxDriver.Lane(this, FxLane.Gear)?.Join(this);

    internal bool BladeSegment(out Vector3 from, out Vector3 to)
    {
        from = to = Vector3.Zero;
        if (!GodotObject.IsInstanceValid(skel) || !skel.IsInsideTree()) return false;
        var edge = skel.GlobalTransform * skel.GetBoneGlobalPose(bone) * plug;
        from = edge * new Vector3(0f, tr0, 0f);
        to = edge * new Vector3(0f, tr1, 0f);
        return true;
    }

    public override void _ExitTree()
    {
        FxDriver.Lane(this, FxLane.Gear)?.Leave(this);
        SetShown(false);
    }

    private const float TraceLeadFrames = 2f;

    public void FxTick(double delta)
    {
        using var scope = Perf.Measure(Perf.Section.Gear);
        if (anim == null || !GodotObject.IsInstanceValid(anim)
            || !GodotObject.IsInstanceValid(skel) || !skel.IsInsideTree() || !anim.Active)
        {
            live = false;
            fade = 0f;
            SetShown(false);
            return;
        }

        if (!CrowdAnimator.Current(anim, out string clip, out double pos))
        {
            using var current = anim.CurrentAnimation;
            clip = current.ToString();
            pos = clip.Length == 0 ? 0 : anim.CurrentAnimationPosition;
        }
        if (clip.Length == 0)
        {
            // CurrentAnimationPosition errors outright when nothing is playing, which happens
            // for the frames between an entity spawning and its first clip starting.
            this.clip = "";
            count = 0;
            live = false;
            Fade(delta);
            return;
        }
        if (clip != this.clip) { this.clip = clip; count = 0; live = false; }

        if (!ModelAnimation.TraceWindow(anim, clip, out float t0, out float t1, out float fps)
            || pos < t0 - TraceLeadFrames / fps || pos > t1)
        {
            if (live && textured && pos > t1) fade = 1f;
            live = false;
            Fade(delta);
            return;
        }
        var edge = skel.GlobalTransform * skel.GetBoneGlobalPose(bone) * plug;
        Push(pos, edge * new Vector3(0f, tr0, 0f), edge * new Vector3(0f, tr1, 0f));
        if (pos < t0 || count < 2)
        {
            Fade(delta);
            return;
        }

        live = true;
        fade = 0f;
        Rebuild(pos, FrameStep / fps);
        SetShown(true);
    }

    private void Fade(double delta)
    {
        if (fade > 0f) fade = Mathf.Max(0f, fade - (float)delta * WeaponTrailRule.AfterimageFadePerSecond);
        if (fade <= 0f)
        {
            SetShown(false);
            return;
        }
        Emit(fade);
        SetShown(true);
    }

    private void Push(double at, Vector3 inner, Vector3 outer)
    {
        float jump = (tr1 - tr0) * TeleportJump;
        if (count > 0 && (innerEdge[count - 1].DistanceSquaredTo(inner) > jump * jump || at < times[count - 1])) count = 0;
        if (count == Raw)
        {
            System.Array.Copy(innerEdge, 1, innerEdge, 0, Raw - 1);
            System.Array.Copy(outerEdge, 1, outerEdge, 0, Raw - 1);
            System.Array.Copy(times, 1, times, 0, Raw - 1);
            count--;
        }
        innerEdge[count] = inner; outerEdge[count] = outer; times[count] = at; count++;
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
        int i = count - 1;
        while (i > 0 && times[i - 1] > at) i--;
        if (i <= 0) { a = innerEdge[0]; b = outerEdge[0]; return; }

        double span = times[i] - times[i - 1];
        float s = span > 1e-6 ? Mathf.Clamp((float)((at - times[i - 1]) / span), 0f, 1f) : 0f;
        int p0 = Mathf.Max(i - 2, 0), p3 = Mathf.Min(i + 1, count - 1);
        a = CatmullRom(innerEdge[p0], innerEdge[i - 1], innerEdge[i], innerEdge[p3], s);
        b = CatmullRom(outerEdge[p0], outerEdge[i - 1], outerEdge[i], outerEdge[p3], s);
    }

    private void Rebuild(double pos, double stepSeconds)
    {
        var inv = GlobalTransform.AffineInverse();
        for (int i = 0; i < Steps; i++)
        {
            Sample(Mathf.Max(pos - i * stepSeconds, 0.0), out var a, out var b);
            lastA[i] = inv * a;
            lastB[i] = inv * b;
        }
        Emit(1f);
    }

    private void Emit(float alpha)
    {
        mesh.ClearSurfaces();
        mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, mat);
        for (int i = 0; i < Steps; i++)
        {
            StepColours(i, alpha, out var inner, out var outer);
            float u = i / (float)Steps;

            mesh.SurfaceSetColor(inner);
            mesh.SurfaceSetUV(new Vector2(u, 0f));
            mesh.SurfaceAddVertex(lastA[i]);
            mesh.SurfaceSetColor(outer);
            mesh.SurfaceSetUV(new Vector2(u, 1f));
            mesh.SurfaceAddVertex(lastB[i]);
        }
        mesh.SurfaceEnd();
    }

    private void StepColours(int i, float alpha, out Color inner, out Color outer)
    {
        if (textured)
        {
            inner = outer = new Color(1f, 1f, 1f, alpha);
            return;
        }
        float k = (Steps - i) / (float)Steps;
        outer = new Color(rgb.R * k, rgb.G * k, rgb.B * k, this.alpha);
        inner = new Color(outer.R * InnerEdgeShade, outer.G * InnerEdgeShade, outer.B * InnerEdgeShade, this.alpha);
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
