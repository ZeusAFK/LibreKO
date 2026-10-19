using Godot;

namespace LibreKO;

public partial class FxParticles : Node3D, IFxPooledPart, IFxPart
{
    private const uint EmitFlags = (uint)(GpuParticles3D.EmitFlags.Position | GpuParticles3D.EmitFlags.Velocity);
    private const uint OrbitEmitFlags = EmitFlags
        | (uint)(GpuParticles3D.EmitFlags.Color | GpuParticles3D.EmitFlags.Custom);
    private const float NoTravel = 1e-10f;

    private float age, start, life, hideTime, showTime;
    private Vector3 origin, velocity, acceleration;
    private Vector3 lastPosition = new(float.NaN, float.NaN, float.NaN);
    private Vector3 travel;
    private FxInstance? root;
    private bool suppressed, done;

    private FxSharedEmitter? shared;
    private double emitClock;
    private double[] expiry = System.Array.Empty<double>();
    private int expiryHead, alive;
    private float lodCarry;
    private bool burstDone;
    private static readonly System.Random Rng = new();

    public FxPartKey? PoolKey { get; set; }
    internal FxParticleTemplate? Template;
    private float Fade;

    internal static bool SkipPosition;
    internal static bool SkipEmission;

    internal void SetSuppressed(bool suppressed) => this.suppressed = suppressed;

    private Vector3 emitCentre;
    private Vector3[]? emitPosKeys, emitScaleKeys;
    private Quaternion[]? emitRotKeys;
    private float emitFps = 30f, emitPosRate, emitRotRate, emitScaleRate, emitWholeFrame;
    private bool hasEmitter;

    public void SetEmitter(Vector3 centre, Vector3[]? posKeys, Quaternion[]? rotKeys,
                           Vector3[]? scaleKeys, float fps, float posRate, float rotRate,
                           float scaleRate, float wholeFrame)
    {
        emitCentre = centre;
        emitPosKeys = posKeys;
        emitRotKeys = rotKeys;
        emitScaleKeys = scaleKeys;
        emitFps = fps;
        emitPosRate = posRate;
        emitRotRate = rotRate;
        emitScaleRate = scaleRate;
        emitWholeFrame = wholeFrame;
        hasEmitter = true;
    }

    private Vector3 EmitterOffset(float t)
    {
        if (!hasEmitter) return Vector3.Zero;
        var pos = Vector3.Zero;
        var rot = Quaternion.Identity;
        var scale = Vector3.One;
        if (emitWholeFrame > 0f)
        {
            float frame = Mathf.PosMod(t * emitFps, emitWholeFrame);
            if (emitPosKeys != null) pos = SampleV(emitPosKeys, frame * emitPosRate / 30f);
            if (emitScaleKeys != null) scale = SampleV(emitScaleKeys, frame * emitScaleRate / 30f);
            if (emitRotKeys != null) rot = SampleQ(emitRotKeys, frame * emitRotRate / 30f);
        }
        return pos + new Basis(rot) * (scale * emitCentre);
    }

    private static Vector3 SampleV(Vector3[] k, float kp)
    {
        int i = Mathf.Clamp((int)kp, 0, k.Length - 1);
        int j = Mathf.Min(i + 1, k.Length - 1);
        return k[i].Lerp(k[j], Mathf.Clamp(kp - i, 0f, 1f));
    }

    private static Quaternion SampleQ(Quaternion[] k, float kp)
    {
        int i = Mathf.Clamp((int)kp, 0, k.Length - 1);
        int j = Mathf.Min(i + 1, k.Length - 1);
        return k[i].Slerp(k[j], Mathf.Clamp(kp - i, 0f, 1f));
    }

    public void SetBlink(float hideTime, float showTime)
    {
        this.hideTime = hideTime;
        this.showTime = showTime;
    }

    public void Configure(float start, float life, Vector3 origin,
        Vector3 velocity, Vector3 acceleration, float fadeIn = 0f, float fadeOut = 0f)
    {
        this.start = Mathf.Max(0f, start);
        this.life = life > 0.001f ? fadeIn + life : life;
        this.origin = origin;
        this.velocity = velocity;
        this.acceleration = acceleration;
        Reset();
    }

    public void Reset()
    {
        Position = origin;
        lastPosition = origin;
        age = 0f;
        suppressed = false;
        emitClock = 0;
        alive = 0;
        expiryHead = 0;
        burstDone = false;
        Fade = 0f;
        Visible = true;
        done = false;
    }

    internal void Park()
    {
        FxEmitterPool.Release(shared);
        shared = null;
    }

    public override void _EnterTree()
    {
        root = GetParent() as FxInstance;
        if (root == null) return;
        root.AddPart(this);
        SetProcess(false);
    }

    public override void _Ready()
    {
        if (root != null) SetProcess(false);
    }

    public override void _ExitTree()
    {
        root?.RemovePart(this);
        root = null;
    }

    public override void _Process(double delta) => Tick(delta);

    public void Tick(double delta)
    {
        using var scope = Perf.Measure(Perf.Section.FxPart);
        if (done || Fx.ShuttingDown) return;
        age += (float)delta;
        float raw = age - start;
        if (raw < 0f) return;

        float t = raw;
        if (life > 0.001f && raw >= life)
        {
            if (raw >= life + (Template?.Lifetime ?? 0f))
            {
                done = true;
                if (root == null) SetProcess(false);
                Fx.RecyclePart(this);
            }
            return;
        }
        if (Fx.PartHidden(raw, hideTime, showTime)) return;
        Vector3 position = origin + velocity * t + 0.5f * acceleration * t * t + EmitterOffset(t);
        travel = hasEmitter ? position - lastPosition : velocity + acceleration * t;
        if (position != lastPosition)
        {
            lastPosition = position;
            if (!SkipPosition) Position = position;
        }
        if (suppressed || SkipEmission || Template == null) return;
        Emit(Template, delta);
    }

    private void Emit(FxParticleTemplate template, double delta)
    {
        if (expiry.Length < template.Capacity) expiry = new double[template.Capacity];
        int capacity = expiry.Length;
        while (alive > 0 && expiry[(expiryHead - alive + capacity) % capacity] <= age) alive--;

        int want = 0;
        if (template.SingleBurst)
        {
            if (!burstDone) { want = template.NumCreate; burstDone = true; }
        }
        else if (template.EmitInterval <= 0.001f)
        {
            emitClock += delta * template.Capacity / Mathf.Max(0.01f, template.Lifetime);
            want = (int)emitClock;
            emitClock -= want;
        }
        else
        {
            emitClock += delta;
            while (emitClock >= template.EmitInterval)
            {
                emitClock -= template.EmitInterval;
                want += template.NumCreate;
            }
        }
        want = System.Math.Min(want, template.Capacity - alive);
        if (want <= 0) return;

        Transform3D world;
        if (root != null)
        {
            root.EnsureFrame();
            world = root.FrameXf * new Transform3D(Basis.Identity, lastPosition);
        }
        else world = GlobalTransform;

        want = FxLod.Thin(want, FxLod.Ratio(world.Origin), ref lodCarry, template.SingleBurst);
        if (want <= 0) return;

        shared ??= FxEmitterPool.Acquire(this, PoolKey ?? default, template);
        if (shared == null) return;

        Vector3 axis = EmitAxis(template.EmitAxis);
        bool aimed = axis.LengthSquared() > NoTravel;
        Basis arc = aimed ? FxSpawnBox.Orientation(axis) : Basis.Identity;
        Vector3 gather = arc * template.GatherPoint;
        bool orbit = template.OrbitRate != 0f;
        Color centre = default, spin = default;
        if (orbit)
        {
            Vector3 worldAxis = aimed ? (world.Basis * axis).Normalized() : world.Basis.Z.Normalized();
            centre = new Color(world.Origin.X, world.Origin.Y, world.Origin.Z, 0f);
            spin = new Color(worldAxis.X, worldAxis.Y, worldAxis.Z, 0f);
        }

        var xform = Transform3D.Identity;
        for (int i = 0; i < want; i++)
        {
            Vector3 local = arc * (template.BoxOffset + new Vector3(
                Span(template.BoxExtent.X), Span(template.BoxExtent.Y), Span(template.BoxExtent.Z)));
            Vector3 velocity;
            if (template.Gather)
            {
                Vector3 toward = gather - local;
                velocity = toward.LengthSquared() > NoTravel
                    ? world.Basis * (toward.Normalized() * template.Speed)
                    : Vector3.Zero;
            }
            else
                velocity = aimed ? world.Basis * (arc * ConeDirection(template.Spread) * template.Speed) : Vector3.Zero;
            xform.Origin = world * local;
            if (orbit)
            {
                if (!template.SingleBurst) spin.A = (float)(Rng.NextDouble() * Mathf.Tau);
                shared.EmitParticle(xform, velocity, centre, spin, OrbitEmitFlags);
            }
            else
                shared.EmitParticle(xform, velocity, Colors.White, Colors.White, EmitFlags);
            expiry[expiryHead] = age + template.LifeMax;
            expiryHead = (expiryHead + 1) % capacity;
            alive++;
        }
    }

    private static float Span(float extent) => extent <= 0f ? 0f : (float)(Rng.NextDouble() * 2.0 - 1.0) * extent;

    private Vector3 EmitAxis(Vector3 emitAxis)
    {
        if (emitAxis == Vector3.Zero || travel.LengthSquared() <= NoTravel) return emitAxis;
        return FxSpawnBox.Orientation(travel) * emitAxis;
    }

    private static Vector3 ConeDirection(float halfAngleDegrees)
    {
        float tilt = Mathf.DegToRad(halfAngleDegrees) * (float)(Rng.NextDouble() * 2.0 - 1.0);
        float roll = (float)(Rng.NextDouble() * Mathf.Tau);
        float sinTilt = Mathf.Sin(tilt);
        return new Vector3(sinTilt * Mathf.Cos(roll), sinTilt * Mathf.Sin(roll), Mathf.Cos(tilt));
    }
}
