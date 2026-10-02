using Godot;
using LibreKO.Domain;
using System.Collections.Generic;

namespace LibreKO;

public static class Fx
{
    private static readonly Dictionary<string, Godot.Collections.Dictionary> _cache = new();
    private static Dictionary<int, string>? _idNames;
    internal static bool ShuttingDown { get; private set; }

    private static ulong _cameraFrame = ulong.MaxValue;
    private static Viewport? _cameraViewport;
    private static Camera3D? _camera;
    private static Transform3D _cameraTransform;

    internal static bool FrameCamera(Node node, out Transform3D camera)
    {
        ulong frame = Engine.GetProcessFrames();
        var viewport = node.GetViewport();
        if (frame != _cameraFrame || viewport != _cameraViewport)
        {
            _cameraFrame = frame;
            _cameraViewport = viewport;
            _camera = viewport?.GetCamera3D();
            if (_camera != null) _cameraTransform = _camera.GlobalTransform;
        }
        camera = _cameraTransform;
        return _camera != null;
    }

    internal static void PrimeFrameCamera(Viewport viewport, Camera3D? camera, in Transform3D xf)
    {
        _cameraFrame = Engine.GetProcessFrames();
        _cameraViewport = viewport;
        _camera = camera;
        _cameraTransform = xf;
    }

    public static void BeginShutdown(Node? root)
    {
        ShuttingDown = true;
        FxRegistry.Clear();
        ClearPartPool();
        FxEmitterPool.Clear();
        if (root == null) return;
        StopFxUnder(root);
    }

    public static float AuthoredVelocity(string name)
    {
        var desc = LoadDescriptor(name);
        return desc != null && desc.ContainsKey("velocity") ? (float)desc["velocity"].AsDouble() : 0f;
    }

    public static bool LoopsForever(string name)
    {
        var desc = LoadDescriptor(name);
        if (desc == null) return false;
        foreach (var pv in desc["parts"].AsGodotArray())
            if (PartEnd(pv.AsGodotDictionary()) <= 0.001f) return true;
        return false;
    }

    public static float AuthoredCentreY(string name)
    {
        var desc = LoadDescriptor(name);
        return desc != null && desc.ContainsKey("centreY") ? (float)desc["centreY"].AsDouble() : 0f;
    }

    public static Node3D? Spawn(string name, Node parent, Vector3 pos, bool oneShot = false,
        float sizeScale = 1f, bool forceAdditive = false, bool deferParts = false)
    {
        if (ShuttingDown || Perf.SkipFx) return null;
        using var scope = Perf.Measure(Perf.Section.FxSpawn);
        Perf.Tally(Perf.Counter.FxSpawned);
        return FxRegistry.Track(
            SpawnBaked(name, parent, pos, oneShot, sizeScale, forceAdditive, deferParts), name, "baked");
    }

    private const double PartBudgetMs = 4.0;
    private const double MaxPartDelaySeconds = 0.3;

    private readonly record struct PendingPart(FxInstance Root, Godot.Collections.Dictionary Part, FxPartKey Key,
        int Index, long QueuedAt);

    private static readonly Queue<PendingPart> _pendingParts = new();
    private static ulong _budgetFrame = ulong.MaxValue;
    private static long _budgetSpent;

    internal static int PendingParts => _pendingParts.Count;

    private static bool WithinPartBudget()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame != _budgetFrame)
        {
            _budgetFrame = frame;
            _budgetSpent = 0;
        }
        return _budgetSpent < PartBudgetMs * System.Diagnostics.Stopwatch.Frequency / 1000.0;
    }

    internal static void BuildPendingParts()
    {
        long overdue = (long)(MaxPartDelaySeconds * System.Diagnostics.Stopwatch.Frequency);
        while (_pendingParts.Count > 0)
        {
            var next = _pendingParts.Peek();
            bool late = System.Diagnostics.Stopwatch.GetTimestamp() - next.QueuedAt > overdue;
            if (!late && !WithinPartBudget()) break;
            _pendingParts.Dequeue();
            if (!GodotObject.IsInstanceValid(next.Root) || next.Root.Released) continue;
            next.Root.PendingParts--;
            using var scope = Perf.Measure(Perf.Section.FxSpawn);
            BuildPart(next.Root, next.Part, next.Key, next.Index);
        }
    }

    private static void BuildPart(FxInstance root, Godot.Collections.Dictionary p, FxPartKey key, int partIndex)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        Node3D? node = null;
        try
        {
            string type = p["type"].AsString();
            if (type == "Particles" && Perf.SkipEmitters) { }
            else if (type == "Particles")
            {
                using var build = Perf.Measure(Perf.Section.SpawnParticles);
                node = BuildParticles(p, key);
            }
            else if (type is "BillBoard" or "BottomBoard")
            {
                using var build = Perf.Measure(Perf.Section.SpawnBoard);
                node = FxBillboard.Build(p, key);
            }
            else if (type == "Mesh")
            {
                using var build = Perf.Measure(Perf.Section.SpawnMesh);
                node = FxMesh.Build(p, key);
            }
        }
        catch (System.Exception e) { GD.PushWarning($"[fx] part build failed in '{key.Name}': {e.Message}"); }
        if (node is GeometryInstance3D geometry
            && geometry.MaterialOverride is Material partMaterial)
        {
            partMaterial.RenderPriority = partIndex;
        }
        if (node != null) root.AddChild(node);
        WithinPartBudget();
        _budgetSpent += System.Diagnostics.Stopwatch.GetTimestamp() - started;
    }

    private static Node3D? SpawnBaked(string name, Node parent, Vector3 pos, bool oneShot,
        float sizeScale, bool forceAdditive = false, bool deferParts = false)
    {
        var watch = Diag.Watch();
        var desc = LoadDescriptor(name);
        if (desc == null)
        {
            GD.PushWarning($"[fx] no baked descriptor for '{name}'");
            return null;
        }

        var root = new FxInstance { Name = $"fx_{name}", Position = pos };
        parent.AddChild(root);
        float life = (float)desc["life"].AsDouble();
        float oneShotLife = life;
        root.AuthoredVelocity = desc.ContainsKey("velocity") ? (float)desc["velocity"].AsDouble() : 0f;

        int partIndex = -1;
        foreach (var (p, partEnd) in PreparedParts(name, desc, sizeScale, forceAdditive))
        {
            partIndex++;
            oneShotLife = Mathf.Max(oneShotLife, partEnd);
            var key = new FxPartKey(name, partIndex, sizeScale, forceAdditive);
            if (deferParts && (_pendingParts.Count > 0 || !WithinPartBudget()))
            {
                _pendingParts.Enqueue(new PendingPart(root, p, key, partIndex, System.Diagnostics.Stopwatch.GetTimestamp()));
                root.PendingParts++;
            }
            else BuildPart(root, p, key, partIndex);
        }

        root.BundleLife = oneShot && life <= 0.001f ? Mathf.Max(0.05f, oneShotLife) : life;
        Diag.Slow($"fx spawn {name}", watch);
        return root;
    }

    private static readonly Dictionary<(string Name, float Scale, bool Additive), List<(Godot.Collections.Dictionary Part, float End)>> _preparedParts = new();

    private static List<(Godot.Collections.Dictionary Part, float End)> PreparedParts(
        string name, Godot.Collections.Dictionary desc, float sizeScale, bool forceAdditive)
    {
        var key = (name, sizeScale, forceAdditive);
        if (_preparedParts.TryGetValue(key, out var prepared)) return prepared;
        prepared = new List<(Godot.Collections.Dictionary, float)>();
        foreach (var pv in desc["parts"].AsGodotArray())
        {
            var p = pv.AsGodotDictionary().Duplicate();
            p["bundleScale"] = sizeScale;
            if (forceAdditive)
            {
                p["blend"] = "add";
                p["srcBlend"] = FxBlendMath.SrcAlpha;
                p["destBlend"] = FxBlendMath.One;
                p["renderFlags"] = FxShading.RenderFlags(p) | FxShading.RfAlphaBlending;
            }
            float partEnd = PartEnd(p);
            if (partEnd <= 0.001f)
            {
                float fps = ReadF(p, "texFPS");
                int frames = p.ContainsKey("frameCount") ? Mathf.Max(1, p["frameCount"].AsInt32()) : 1;
                partEnd = ReadF(p, "startTime") + (fps > 0.001f ? frames / fps : 0.05f);
            }
            prepared.Add((p, partEnd));
        }
        _preparedParts[key] = prepared;
        return prepared;
    }

    private static void StopFxUnder(Node node)
    {
        if (node is FxParticles particles)
            particles.SetSuppressed(true);
        if (node.Name.ToString().StartsWith("fx_") && GodotObject.IsInstanceValid(node))
        {
            Free(node);
            return;
        }
        foreach (var child in node.GetChildren())
            StopFxUnder(child);
    }

    private static Aabb ParticleBounds(Godot.Collections.Dictionary p, float lifeMax, float speed,
                                       float bundleScale)
    {
        Vector3 boxCentre = ReadVec3(p, "emitBoxCentre") * bundleScale;
        Vector3 boxExtent = ReadVec3(p, "emitBoxExtent") * bundleScale;
        float grav = ReadVec3(p, "gravity").Length();
        float sizeMax = (Mathf.Max(ReadF(p, "sizeMax"), ReadF(p, "sizeMin")) + ReadF(p, "sizeOffset"))
                          * bundleScale
                      + lifeMax * Mathf.Max(0f, MaxGrowth(p));
        float travel = speed * lifeMax + 0.5f * grav * lifeMax * lifeMax;
        float reach = Mathf.Clamp(travel + sizeMax * 0.5f + 1f, 1f, 400f);
        Vector3 half = boxExtent + new Vector3(reach, reach, reach);
        return new Aabb(boxCentre - half, half * 2f);
    }

    private static float MaxGrowth(Godot.Collections.Dictionary p)
    {
        if (!p.ContainsKey("sizeVel")) return 0f;
        var v = p["sizeVel"].AsGodotArray();
        if (v.Count != 2) return 0f;
        return Mathf.Max((float)v[0].AsDouble(), (float)v[1].AsDouble());
    }

    internal static float ParticleLife(Godot.Collections.Dictionary p) =>
        Mathf.Max(0.01f, ReadF(p, "lifeMax", 1f))
        + (p.ContainsKey("colorLUT") && p["colorLUT"].VariantType == Variant.Type.Array
            && p["colorLUT"].AsGodotArray().Count > 0 ? 0f : ReadF(p, "fadeIn") + ReadF(p, "fadeOut"));

    internal static float PartEnd(Godot.Collections.Dictionary p)
    {
        float life = ReadF(p, "life");
        if (life <= 0.001f) return 0f;
        float end = ReadF(p, "startTime") + ReadF(p, "fadeIn") + life;
        return end + (p["type"].AsString() == "Particles" ? ParticleLife(p) : ReadF(p, "fadeOut"));
    }

    private const int PartPoolPerKey = 256;
    private static readonly Dictionary<FxPartKey, FxParticleTemplate> _particleTemplates =
        Shutdown.Track(new Dictionary<FxPartKey, FxParticleTemplate>(), template => (template.Process, template.Material));
    private static readonly Dictionary<FxPartKey, Stack<Node3D>> _partPool = new();

    internal static int PooledParts { get; private set; }

    internal static void RecyclePart(Node3D part)
    {
        if (ShuttingDown || part.IsQueuedForDeletion() || part is not IFxPooledPart { PoolKey: { } key })
        {
            part.QueueFree();
            return;
        }
        if (!_partPool.TryGetValue(key, out var stack)) _partPool[key] = stack = new Stack<Node3D>();
        if (stack.Count >= PartPoolPerKey) { part.QueueFree(); return; }
        part.GetParent()?.RemoveChild(part);
        if (part is FxParticles particles) particles.Park();
        stack.Push(part);
        PooledParts++;
    }

    internal static Node3D? TakePooledPart(FxPartKey key)
    {
        if (!_partPool.TryGetValue(key, out var stack) || stack.Count == 0) return null;
        var part = stack.Pop();
        PooledParts--;
        if (!GodotObject.IsInstanceValid(part)) return null;
        ((IFxPooledPart)part).Reset();
        return part;
    }

    private static void ClearPartPool()
    {
        foreach (var stack in _partPool.Values)
            foreach (var part in stack)
                if (GodotObject.IsInstanceValid(part)) part.QueueFree();
        _partPool.Clear();
        PooledParts = 0;
    }

    internal static void Free(Node? node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node)) return;
        if (node is FxInstance root) root.Release();
        else node.QueueFree();
    }

    private static Node3D? BuildParticles(Godot.Collections.Dictionary p, FxPartKey key)
    {
        if (TakePooledPart(key) is FxParticles pooled) return pooled;
        var tex = FirstTexture(p);
        if (tex == null) return null;
        if (!_particleTemplates.TryGetValue(key, out var template))
            _particleTemplates[key] = template = BuildParticleTemplate(p, key, tex);
        var gp = new FxParticles { PoolKey = key, Template = template };
        gp.Configure(template.Start, template.Life, template.Origin, template.Velocity, template.Acceleration,
            template.FadeIn, template.FadeOut);
        gp.SetBlink(template.HideTime, template.ShowTime);
        if (template.Emitter is { } em)
            gp.SetEmitter(em.Centre, em.Pos, em.Rot, em.Scale, em.Fps, em.PosRate, em.RotRate, em.ScaleRate, em.WholeFrame);
        return gp;
    }

    private const int GatherEmitType = 2;
    private const float OrbitRateEpsilon = 1e-4f;
    private const float EmitAxisEpsilon = 1e-6f;

    private static FxParticleTemplate BuildParticleTemplate(Godot.Collections.Dictionary p, FxPartKey key, Texture2D tex)
    {
        float lifeMin = ReadF(p, "lifeMin");
        float lifeMax = Mathf.Max(0.01f, ReadF(p, "lifeMax", 1f));
        float bundleScale = ReadF(p, "bundleScale", 1f);
        float speed = ReadF(p, "speed") * bundleScale;
        float emitInterval = ReadF(p, "emitInterval");
        float particleLife = ParticleLife(p);
        int capacity = Mathf.Max(1, p.ContainsKey("numParticles") ? p["numParticles"].AsInt32() : 16);
        int numCreate = Mathf.Max(1, p.ContainsKey("numCreate") ? p["numCreate"].AsInt32() : 1);
        float emitterLife = ReadF(p, "life");
        bool singleBurst = emitInterval > 0.001f && emitterLife > 0.001f && emitInterval >= emitterLife;
        var lut = ColorRamp(p);
        Vector3 boxExtent = ReadVec3(p, "emitBoxExtent") * bundleScale;
        Vector3 boxCentre = ReadVec3(p, "emitBoxCentre") * bundleScale;
        int emitType = p.ContainsKey("emitType") ? p["emitType"].AsInt32() : 0;
        bool gather = emitType == GatherEmitType;
        var emitter = EmitterKeysFor(p);
        Vector3 gatherPoint = gather ? ReadVec3(p, "gatherPoint") : Vector3.Zero;
        Vector3 emitDir = ReadVec3(p, "emitDir");
        float sizeOff = ReadF(p, "sizeOffset");
        float sizeMin = (ReadF(p, "sizeMin", 1f) + sizeOff) * bundleScale;
        float sizeMax = (ReadF(p, "sizeMax", 1f) + sizeOff) * bundleScale;
        var pm = new ParticleProcessMaterial
        {
            Gravity = ReadVec3(p, "gravity"),
            ScaleMin = sizeMin,
            ScaleMax = sizeMax,
            AngularVelocityMin = Mathf.RadToDeg(ReadF(p, "rollRate")),
            AngularVelocityMax = Mathf.RadToDeg(ReadF(p, "rollRate")),
            LifetimeRandomness = Mathf.Clamp((lifeMax - lifeMin) / particleLife, 0f, 1f),
        };
        if (lut != null) pm.ColorRamp = lut;
        var material = FxParticleMaterial.Build(p, tex, particleLife, lut != null);
        material.RenderPriority = key.Index;
        float orbitRate = -ReadF(p, "ptRotVel");
        return new FxParticleTemplate
        {
            Process = Mathf.Abs(orbitRate) > OrbitRateEpsilon
                ? FxParticleMaterial.OrbitProcess(pm, lut, orbitRate)
                : pm,
            Material = material,
            Emitter = emitter,
            Capacity = capacity,
            NumCreate = singleBurst ? Mathf.Clamp(numCreate, 1, capacity) : numCreate,
            EmitInterval = emitInterval,
            Lifetime = particleLife,
            LifeMax = lifeMax,
            Speed = speed,
            Spread = ReadF(p, "spread"),
            Gather = gather,
            SingleBurst = singleBurst,
            Commutative = FxShading.Commutative(FxShading.Packed(p)),
            EmitAxis = emitDir.LengthSquared() > EmitAxisEpsilon ? emitDir.Normalized() : Vector3.Zero,
            BoxOffset = boxCentre,
            BoxExtent = boxExtent,
            GatherPoint = gatherPoint,
            OrbitRate = orbitRate,
            FixedOrientation = p.ContainsKey("rotAbs") && p["rotAbs"].AsBool(),
            Start = ReadF(p, "startTime"),
            Life = ReadF(p, "life"),
            Origin = ReadVec3(p, "initPos"),
            Velocity = ReadVec3(p, "initVel"),
            Acceleration = ReadVec3(p, "accel"),
            FadeIn = ReadF(p, "fadeIn"),
            FadeOut = ReadF(p, "fadeOut"),
            HideTime = ReadF(p, "hideTime"),
            ShowTime = ReadF(p, "showTime"),
        };
    }

    internal const int RfDoubleSided = 0x004;
    internal const int RfNotZWrite   = 0x100;
    internal const int RfNotZBuffer  = 0x400;

    internal const int FxSiblingWinnerPriority = 1;

    internal static void SetTransparency(GeometryInstance3D gi, float transparency)
    {
        if (gi is FxBillboard { Batched: true } board) board.Fade = transparency;
        else gi.Transparency = transparency;
    }

    internal static void SetShown(Node node, bool shown)
    {
        switch (node)
        {
            case FxParticles particles:
                particles.SetSuppressed(!shown);
                break;
            case FxMesh mesh:
                mesh.Visible = shown;
                mesh.ResetShown();
                break;
            case FxBillboard board:
                board.Visible = shown;
                board.ResetShown();
                break;
            case FxLampLight:
                break;
            case VisualInstance3D visual:
                visual.Visible = shown;
                break;
        }
        foreach (var child in node.GetChildren()) SetShown(child, shown);
    }

    private static FxEmitterKeys? EmitterKeysFor(Godot.Collections.Dictionary p)
    {
        if (!p.ContainsKey("emitterRef") || p["emitterRef"].VariantType == Variant.Type.Nil) return null;
        if (!p.ContainsKey("emitCentre") || p["emitCentre"].VariantType == Variant.Type.Nil) return null;
        var shape = FxMesh.LoadShapeJson(p["emitterRef"].AsString());
        if (shape == null) return null;
        var c = p["emitCentre"].AsGodotArray();
        if (c.Count != 3) return null;
        return new FxEmitterKeys
        {
            Centre = new Vector3((float)c[0].AsDouble(), (float)c[1].AsDouble(), (float)c[2].AsDouble()),
            Pos = FxMesh.VecKeys(shape, "posKeys"),
            Rot = FxMesh.QuatKeys(shape, "rotKeys"),
            Scale = FxMesh.VecKeys(shape, "scaleKeys"),
            Fps = ReadF(p, "emitterFps", 30f),
            PosRate = FxMesh.Rate(shape, "posKeys", "posRate"),
            RotRate = FxMesh.Rate(shape, "rotKeys", "rotRate"),
            ScaleRate = FxMesh.Rate(shape, "scaleKeys", "scaleRate"),
            WholeFrame = (float)shape["wholeFrame"].AsDouble(),
        };
    }

    internal static int SrcBlend(Godot.Collections.Dictionary p) =>
        p.ContainsKey("srcBlend") ? p["srcBlend"].AsInt32() : 5;

    internal static int DestBlend(Godot.Collections.Dictionary p) =>
        p.ContainsKey("destBlend") ? p["destBlend"].AsInt32() : 6;

    internal static Texture2D? FirstTexture(Godot.Collections.Dictionary p) => FrameTexture(p, 0);

    private static readonly Dictionary<string, Texture2D?> _frameTextures = Shutdown.Track(new Dictionary<string, Texture2D?>());

    internal static Texture2D? FrameTexture(Godot.Collections.Dictionary p, int frame)
    {
        var arr = p["tex"].AsGodotArray();
        if (arr.Count == 0) return null;
        string stem = arr[Mathf.Clamp(frame, 0, arr.Count - 1)].AsString();
        if (_frameTextures.TryGetValue(stem, out var cached)) return cached;
        using var scope = Perf.Measure(Perf.Section.FxLoadTexture);
        string path = $"res://assets/fx/tex/{stem}.png";
        var texture = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        _frameTextures[stem] = texture;
        return texture;
    }

    private static GradientTexture1D? ColorRamp(Godot.Collections.Dictionary p)
    {
        if (p["colorLUT"].VariantType == Variant.Type.Nil) return null;
        var lut = p["colorLUT"].AsGodotArray();
        int n = lut.Count;
        if (n == 0) return null;
        int steps = n;
        var offsets = new float[steps];
        var colors = new Color[steps];
        for (int i = 0; i < steps; i++)
        {
            float t = steps == 1 ? 0f : i / (float)(steps - 1);
            var c = lut[Mathf.Min(n - 1, Mathf.RoundToInt(t * (n - 1)))].AsGodotArray();
            offsets[i] = t;
            colors[i] = new Color((float)c[0].AsDouble(), (float)c[1].AsDouble(),
                                  (float)c[2].AsDouble(), (float)c[3].AsDouble());
        }
        return new GradientTexture1D { Gradient = new Gradient { Offsets = offsets, Colors = colors } };
    }

    internal static Vector3 ReadVec3(Godot.Collections.Dictionary p, string key)
    {
        if (!p.ContainsKey(key)) return Vector3.Zero;
        var a = p[key].AsGodotArray();
        return a.Count == 3
            ? new Vector3((float)a[0].AsDouble(), (float)a[1].AsDouble(), (float)a[2].AsDouble())
            : Vector3.Zero;
    }

    internal static float ReadF(Godot.Collections.Dictionary p, string key, float def = 0f)
        => p.ContainsKey(key) && p[key].VariantType != Variant.Type.Nil ? (float)p[key].AsDouble() : def;

    internal static Basis ReadKoMatrixBasis(Godot.Collections.Dictionary p)
    {
        if (!p.ContainsKey("matrix") || p["matrix"].VariantType != Variant.Type.Array)
            return Basis.Identity;
        var m = p["matrix"].AsGodotArray();
        if (m.Count != 16) return Basis.Identity;
        float R(int row, int col)
        {
            float v = (float)m[row * 4 + col].AsDouble();
            return (row == 0) != (col == 0) ? -v : v;
        }
        return new Basis(
            new Vector3(R(0, 0), R(1, 0), R(2, 0)),
            new Vector3(R(0, 1), R(1, 1), R(2, 1)),
            new Vector3(R(0, 2), R(1, 2), R(2, 2)));
    }

    internal static bool PartAge(float rawAge, float life, float fadeIn, float fadeOut,
        out float age, out float alpha)
    {
        age = rawAge;
        alpha = 1f;
        float total = life > 0.001f ? fadeIn + life + fadeOut : 0f;
        if (total > 0.001f)
        {
            if (rawAge >= total)
            {
                age = total;
                alpha = 0f;
                return false;
            }
            if (fadeIn > 0.001f && age < fadeIn)
                alpha = Mathf.Clamp(age / fadeIn, 0f, 1f);
            else if (fadeOut > 0.001f && age > fadeIn + life)
                alpha = Mathf.Clamp((total - age) / fadeOut, 0f, 1f);
        }
        else if (fadeIn > 0.001f)
        {
            alpha = Mathf.Clamp(age / fadeIn, 0f, 1f);
        }
        return true;
    }

    internal static bool PartHidden(float rawAge, float hideTime, float showTime)
    {
        if (hideTime <= 0.001f || showTime <= 0.001f) return false;
        float period = showTime + hideTime;
        return Mathf.PosMod(rawAge, period) >= showTime;
    }

    public static bool Has(string name) => LoadDescriptor(name) != null;

    public static string? NameForId(int fxId)
    {
        if (_idNames == null)
        {
            _idNames = new Dictionary<int, string>();
            const string path = "res://assets/fx/ids.json";
            if (Godot.FileAccess.FileExists(path))
            {
                using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
                if (f != null)
                {
                    var parsed = Json.ParseString(f.GetAsText());
                    if (parsed.VariantType == Variant.Type.Dictionary)
                    {
                        foreach (var kv in parsed.AsGodotDictionary())
                        {
                            if (int.TryParse(kv.Key.AsString(), out int id))
                                _idNames[id] = kv.Value.AsString();
                        }
                    }
                }
            }
        }
        return _idNames.TryGetValue(fxId, out var name) ? name : null;
    }

    internal static Godot.Collections.Dictionary? LoadDescriptor(string name)
    {
        string stem = name.ToLowerInvariant();
        if (_cache.TryGetValue(stem, out var cached)) return cached;
        using var scope = Perf.Measure(Perf.Section.FxLoadDescriptor);
        string path = $"res://assets/fx/{stem}.json";
        if (!Godot.FileAccess.FileExists(path)) { _cache[stem] = null!; return null; }
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null) { _cache[stem] = null!; return null; }
        var parsed = Json.ParseString(f.GetAsText());
        var dict = parsed.VariantType == Variant.Type.Dictionary ? parsed.AsGodotDictionary() : null;
        _cache[stem] = dict!;
        return dict;
    }
}
