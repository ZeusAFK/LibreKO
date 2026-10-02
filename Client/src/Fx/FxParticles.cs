using Godot;

namespace LibreKO;

public partial class FxParticles : Node3D, IFxPooledPart, IFxPart
{
    private const uint EmitFlags = (uint)(GpuParticles3D.EmitFlags.Position | GpuParticles3D.EmitFlags.Velocity);
    private const uint OrbitEmitFlags = EmitFlags
        | (uint)(GpuParticles3D.EmitFlags.Color | GpuParticles3D.EmitFlags.Custom);
    private const float NoTravel = 1e-10f;
    private const uint OrientedFlag = (uint)GpuParticles3D.EmitFlags.RotationScale;

    private float _age, _start, _life, _hideTime, _showTime;
    private Vector3 _origin, _velocity, _acceleration;
    private Vector3 _lastPosition = new(float.NaN, float.NaN, float.NaN);
    private Vector3 _travel;
    private FxInstance? _root;
    private bool _suppressed, _done;

    private FxSharedEmitter? _shared;
    private double _emitClock;
    private double[] _expiry = System.Array.Empty<double>();
    private int _expiryHead, _alive;
    private bool _burstDone;
    private static readonly System.Random Rng = new();

    public FxPartKey? PoolKey { get; set; }
    internal FxParticleTemplate? Template;
    internal float Fade;

    internal static bool SkipPosition;
    internal static bool SkipEmission;

    internal void SetSuppressed(bool suppressed) => _suppressed = suppressed;

    private Vector3 _emitCentre;
    private Vector3[]? _emitPosKeys, _emitScaleKeys;
    private Quaternion[]? _emitRotKeys;
    private float _emitFps = 30f, _emitPosRate, _emitRotRate, _emitScaleRate, _emitWholeFrame;
    private bool _hasEmitter;

    public void SetEmitter(Vector3 centre, Vector3[]? posKeys, Quaternion[]? rotKeys,
                           Vector3[]? scaleKeys, float fps, float posRate, float rotRate,
                           float scaleRate, float wholeFrame)
    {
        _emitCentre = centre;
        _emitPosKeys = posKeys;
        _emitRotKeys = rotKeys;
        _emitScaleKeys = scaleKeys;
        _emitFps = fps;
        _emitPosRate = posRate;
        _emitRotRate = rotRate;
        _emitScaleRate = scaleRate;
        _emitWholeFrame = wholeFrame;
        _hasEmitter = true;
    }

    private Vector3 EmitterOffset(float t)
    {
        if (!_hasEmitter) return Vector3.Zero;
        var pos = Vector3.Zero;
        var rot = Quaternion.Identity;
        var scale = Vector3.One;
        if (_emitWholeFrame > 0f)
        {
            float frame = Mathf.PosMod(t * _emitFps, _emitWholeFrame);
            if (_emitPosKeys != null) pos = SampleV(_emitPosKeys, frame * _emitPosRate / 30f);
            if (_emitScaleKeys != null) scale = SampleV(_emitScaleKeys, frame * _emitScaleRate / 30f);
            if (_emitRotKeys != null) rot = SampleQ(_emitRotKeys, frame * _emitRotRate / 30f);
        }
        return pos + new Basis(rot) * (scale * _emitCentre);
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
        _hideTime = hideTime;
        _showTime = showTime;
    }

    public void Configure(float start, float life, Vector3 origin,
        Vector3 velocity, Vector3 acceleration, float fadeIn = 0f, float fadeOut = 0f)
    {
        _start = Mathf.Max(0f, start);
        _life = life > 0.001f ? fadeIn + life : life;
        _origin = origin;
        _velocity = velocity;
        _acceleration = acceleration;
        Reset();
    }

    public void Reset()
    {
        Position = _origin;
        _lastPosition = _origin;
        _age = 0f;
        _suppressed = false;
        _emitClock = 0;
        _alive = 0;
        _expiryHead = 0;
        _burstDone = false;
        Fade = 0f;
        Visible = true;
        _done = false;
    }

    internal void Park()
    {
        FxEmitterPool.Release(_shared);
        _shared = null;
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

    public override void _Process(double delta) => Tick(delta);

    public void Tick(double delta)
    {
        using var scope = Perf.Measure(Perf.Section.FxPart);
        if (_done || Fx.ShuttingDown) return;
        _age += (float)delta;
        float raw = _age - _start;
        if (raw < 0f) return;

        float t = raw;
        if (_life > 0.001f && raw >= _life)
        {
            if (raw >= _life + (Template?.Lifetime ?? 0f))
            {
                _done = true;
                if (_root == null) SetProcess(false);
                Fx.RecyclePart(this);
            }
            return;
        }
        if (Fx.PartHidden(raw, _hideTime, _showTime)) return;
        Vector3 position = _origin + _velocity * t + 0.5f * _acceleration * t * t + EmitterOffset(t);
        _travel = _hasEmitter ? position - _lastPosition : _velocity + _acceleration * t;
        if (position != _lastPosition)
        {
            _lastPosition = position;
            if (!SkipPosition) Position = position;
        }
        if (_suppressed || SkipEmission || Template == null) return;
        Emit(Template, delta);
    }

    private void Emit(FxParticleTemplate template, double delta)
    {
        if (_expiry.Length < template.Capacity) _expiry = new double[template.Capacity];
        int capacity = _expiry.Length;
        while (_alive > 0 && _expiry[(_expiryHead - _alive + capacity) % capacity] <= _age) _alive--;

        int want = 0;
        if (template.SingleBurst)
        {
            if (!_burstDone) { want = template.NumCreate; _burstDone = true; }
        }
        else if (template.EmitInterval <= 0.001f)
        {
            _emitClock += delta * template.Capacity / Mathf.Max(0.01f, template.Lifetime);
            want = (int)_emitClock;
            _emitClock -= want;
        }
        else
        {
            _emitClock += delta;
            while (_emitClock >= template.EmitInterval)
            {
                _emitClock -= template.EmitInterval;
                want += template.NumCreate;
            }
        }
        want = System.Math.Min(want, template.Capacity - _alive);
        if (want <= 0) return;

        _shared ??= FxEmitterPool.Acquire(this, PoolKey ?? default, template);
        if (_shared == null) return;

        Transform3D world;
        if (_root != null)
        {
            _root.EnsureFrame();
            world = _root.FrameXf * new Transform3D(Basis.Identity, _lastPosition);
        }
        else world = GlobalTransform;

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
        uint oriented = 0;
        if (template.FixedOrientation)
        {
            xform.Basis = world.Basis.Orthonormalized();
            oriented = OrientedFlag;
        }
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
                _shared.EmitParticle(xform, velocity, centre, spin, OrbitEmitFlags | oriented);
            }
            else
                _shared.EmitParticle(xform, velocity, Colors.White, Colors.White, EmitFlags | oriented);
            _expiry[_expiryHead] = _age + template.LifeMax;
            _expiryHead = (_expiryHead + 1) % capacity;
            _alive++;
        }
    }

    private static float Span(float extent) => extent <= 0f ? 0f : (float)(Rng.NextDouble() * 2.0 - 1.0) * extent;

    private Vector3 EmitAxis(Vector3 emitAxis)
    {
        if (emitAxis == Vector3.Zero || _travel.LengthSquared() <= NoTravel) return emitAxis;
        return FxSpawnBox.Orientation(_travel) * emitAxis;
    }

    private static Vector3 ConeDirection(float halfAngleDegrees)
    {
        float tilt = Mathf.DegToRad(halfAngleDegrees) * (float)(Rng.NextDouble() * 2.0 - 1.0);
        float roll = (float)(Rng.NextDouble() * Mathf.Tau);
        float sinTilt = Mathf.Sin(tilt);
        return new Vector3(sinTilt * Mathf.Cos(roll), sinTilt * Mathf.Sin(roll), Mathf.Cos(tilt));
    }
}
