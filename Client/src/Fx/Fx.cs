using Godot;
using System.Collections.Generic;

namespace LibreKO;

public static class Fx
{
    private static readonly Dictionary<string, Godot.Collections.Dictionary> _cache = new();
    private static Dictionary<int, string>? _idNames;
    internal static bool ShuttingDown { get; private set; }

    public static void BeginShutdown(Node? root)
    {
        ShuttingDown = true;
        FxRegistry.Clear();
        if (root == null) return;
        StopFxUnder(root);
    }

    public static float AuthoredVelocity(string name)
    {
        var desc = LoadDescriptor(name);
        return desc != null && desc.ContainsKey("velocity") ? (float)desc["velocity"].AsDouble() : 0f;
    }

    public static float AuthoredCentreY(string name)
    {
        var desc = LoadDescriptor(name);
        return desc != null && desc.ContainsKey("centreY") ? (float)desc["centreY"].AsDouble() : 0f;
    }

    public static Node3D? Spawn(string name, Node parent, Vector3 pos, bool oneShot = false,
        float sizeScale = 1f, bool forceAdditive = false)
    {
        if (ShuttingDown) return null;
        return FxRegistry.Track(
            SpawnBaked(name, parent, pos, oneShot, sizeScale, forceAdditive), name, "baked");
    }

    private static Node3D? SpawnBaked(string name, Node parent, Vector3 pos, bool oneShot,
        float sizeScale, bool forceAdditive = false)
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
        foreach (var pv in desc["parts"].AsGodotArray())
        {
            partIndex++;
            var p = pv.AsGodotDictionary().Duplicate();
            p["bundleScale"] = sizeScale;
            if (forceAdditive) p["blend"] = "add";
            float partEnd = PartEnd(p);
            if (partEnd <= 0.001f)
            {
                float fps = ReadF(p, "texFPS");
                int frames = p.ContainsKey("frameCount") ? Mathf.Max(1, p["frameCount"].AsInt32()) : 1;
                partEnd = ReadF(p, "startTime") + (fps > 0.001f ? frames / fps : 0.05f);
            }
            oneShotLife = Mathf.Max(oneShotLife, partEnd);
            Node3D? node = null;
            try
            {
                node = p["type"].AsString() switch
                {
                    "Particles" => BuildParticles(p),
                    "BillBoard" => FxBillboard.Build(p),
                    "BottomBoard" => FxBillboard.Build(p),
                    "Mesh" => FxMesh.Build(p),
                    _ => null,
                };
            }
            catch (System.Exception e) { GD.PushWarning($"[fx] part build failed in '{name}': {e.Message}"); }
            if (node is GeometryInstance3D geometry
                && geometry.MaterialOverride is Material partMaterial)
            {
                partMaterial.RenderPriority = partIndex;
            }
            if (node != null) root.AddChild(node);
        }

        root.BundleLife = oneShot && life <= 0.001f ? Mathf.Max(0.05f, oneShotLife) : life;
        Diag.Slow($"fx spawn {name}", watch);
        return root;
    }

    private static void StopFxUnder(Node node)
    {
        if (node is GpuParticles3D particles)
            particles.Emitting = false;
        if (node.Name.ToString().StartsWith("fx_") && GodotObject.IsInstanceValid(node))
        {
            node.QueueFree();
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

    private static GpuParticles3D? BuildParticles(Godot.Collections.Dictionary p)
    {
        var tex = FirstTexture(p);
        if (tex == null) return null;
        float lifeMin = ReadF(p, "lifeMin");
        float lifeMax = Mathf.Max(0.01f, ReadF(p, "lifeMax", 1f));
        float bundleScale = ReadF(p, "bundleScale", 1f);
        float speed = ReadF(p, "speed") * bundleScale;
        float emitInterval = ReadF(p, "emitInterval");
        float fadeIn = ReadF(p, "fadeIn");
        float fadeOut = ReadF(p, "fadeOut");
        var lut = ColorRamp(p);
        float particleLife = ParticleLife(p);
        float explosiveness = particleLife > 0.01f
            ? Mathf.Clamp(emitInterval / particleLife, 0f, 0.85f) : 0f;
        int pool = Mathf.Max(1, p.ContainsKey("numParticles") ? p["numParticles"].AsInt32() : 16);
        int numCreate = Mathf.Max(1, p.ContainsKey("numCreate") ? p["numCreate"].AsInt32() : 1);
        float emitterLife = ReadF(p, "life");
        bool singleBurst = emitInterval > 0.001f && emitterLife > 0.001f && emitInterval >= emitterLife;
        int amount = singleBurst
            ? Mathf.Clamp(numCreate, 1, pool)
            : emitInterval > 0.001f
                ? Mathf.Clamp(Mathf.RoundToInt(particleLife * numCreate / emitInterval), 1, pool)
                : pool;
        var gp = new FxParticles
        {
            Amount = amount,
            Lifetime = particleLife,
            OneShot = singleBurst,
            Explosiveness = explosiveness,
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = ParticleBounds(p, particleLife, speed, bundleScale),
        };
        Vector3 boxExtent = ReadVec3(p, "emitBoxExtent") * bundleScale;
        Vector3 boxCentre = ReadVec3(p, "emitBoxCentre") * bundleScale;
        bool gather = p.ContainsKey("emitType") && p["emitType"].AsInt32() == 2;
        Vector3 gatherPoint = gather ? ReadVec3(p, "gatherPoint") : Vector3.Zero;
        gp.Configure(
            ReadF(p, "startTime"), ReadF(p, "life"),
            ReadVec3(p, "initPos") + gatherPoint,
            ReadVec3(p, "initVel"), ReadVec3(p, "accel"),
            ReadF(p, "fadeIn"), ReadF(p, "fadeOut"));
        gp.SetBlink(ReadF(p, "hideTime"), ReadF(p, "showTime"));
        AttachEmitterShape(gp, p);

        Vector3 emitDir = ReadVec3(p, "emitDir");
        float sizeOff = ReadF(p, "sizeOffset");
        float sizeMin = (ReadF(p, "sizeMin", 1f) + sizeOff) * bundleScale;
        float sizeMax2 = (ReadF(p, "sizeMax", 1f) + sizeOff) * bundleScale;
        var pm = new ParticleProcessMaterial
        {
            Direction = emitDir.LengthSquared() > 1e-6f ? emitDir.Normalized() : Vector3.Up,
            Spread = ReadF(p, "spread"),
            InitialVelocityMin = gather ? 0f : speed,
            InitialVelocityMax = gather ? 0f : speed,
            RadialVelocityMin = gather ? -speed : 0f,
            RadialVelocityMax = gather ? -speed : 0f,
            Gravity = ReadVec3(p, "gravity"),
            ScaleMin = sizeMin,
            ScaleMax = sizeMax2,
            AngularVelocityMin = Mathf.RadToDeg(ReadF(p, "rollRate")),
            AngularVelocityMax = Mathf.RadToDeg(ReadF(p, "rollRate")),
            LifetimeRandomness = Mathf.Clamp((lifeMax - lifeMin) / particleLife, 0f, 1f),
        };
        if (boxExtent.LengthSquared() > 1e-8f || gather)
        {
            pm.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
            pm.EmissionBoxExtents = boxExtent;
            pm.EmissionShapeOffset = boxCentre - gatherPoint;
        }
        else if (boxCentre.LengthSquared() > 1e-8f)
        {
            pm.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
            pm.EmissionBoxExtents = Vector3.Zero;
            pm.EmissionShapeOffset = boxCentre;
        }
        if (lut != null) pm.ColorRamp = lut;

        gp.ProcessMaterial = pm;
        gp.DrawPass1 = new QuadMesh { Size = Vector2.One };
        gp.MaterialOverride = FxParticleMaterial.Build(p, tex, particleLife, lut != null);
        return gp;
    }

    internal const int RfDoubleSided = 0x004;
    internal const int RfNotZWrite   = 0x100;
    internal const int RfNotZBuffer  = 0x400;

    private const int FxSiblingWinnerPriority = 1;

    internal static void ApplyRenderFlags(StandardMaterial3D mat, Godot.Collections.Dictionary p)
    {
        int rf = p.ContainsKey("renderFlags") ? p["renderFlags"].AsInt32() : RfNotZWrite | RfDoubleSided;
        bool writesZ = (rf & RfNotZWrite) == 0;
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        // Measured: DepthDrawModeEnum.Always punches a black hole through the world — FX_CONTINUATION_HANDOFF §4r.
        mat.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
        mat.RenderPriority = writesZ ? FxSiblingWinnerPriority : 0;
        mat.NoDepthTest = (rf & RfNotZBuffer) != 0;
        mat.CullMode = (rf & RfDoubleSided) != 0
            ? BaseMaterial3D.CullModeEnum.Disabled
            : BaseMaterial3D.CullModeEnum.Back;
    }

    internal static StandardMaterial3D MakeMaterial(Godot.Collections.Dictionary p,
        BaseMaterial3D.BillboardModeEnum billboard, Texture2D? tex)
    {
        bool add = p["blend"].AsString() == "add" || IsSrcColorOverInv(SrcBlend(p), DestBlend(p));
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = add ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
            DisableFog = add,
            VertexColorUseAsAlbedo = true,
            BillboardMode = billboard,
            BillboardKeepScale = true,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            AlbedoColor = Colors.White,
        };
        ApplyRenderFlags(mat, p);
        if (tex != null) mat.AlbedoTexture = TextureForBlend(tex, add, SrcBlend(p), DestBlend(p));
        return mat;
    }

    private static void AttachEmitterShape(FxParticles gp, Godot.Collections.Dictionary p)
    {
        if (!p.ContainsKey("emitterRef") || p["emitterRef"].VariantType == Variant.Type.Nil) return;
        if (!p.ContainsKey("emitCentre") || p["emitCentre"].VariantType == Variant.Type.Nil) return;
        var shape = FxMesh.LoadShapeJson(p["emitterRef"].AsString());
        if (shape == null) return;
        var c = p["emitCentre"].AsGodotArray();
        if (c.Count != 3) return;
        gp.SetEmitter(
            new Vector3((float)c[0].AsDouble(), (float)c[1].AsDouble(), (float)c[2].AsDouble()),
            FxMesh.VecKeys(shape, "posKeys"),
            FxMesh.QuatKeys(shape, "rotKeys"),
            FxMesh.VecKeys(shape, "scaleKeys"),
            ReadF(p, "emitterFps", 30f),
            FxMesh.Rate(shape, "posKeys", "posRate"),
            FxMesh.Rate(shape, "rotKeys", "rotRate"),
            FxMesh.Rate(shape, "scaleKeys", "scaleRate"),
            (float)shape["wholeFrame"].AsDouble());
    }

    private static readonly Dictionary<ulong, Texture2D> _glowCache = new();

    private const float GlowAlphaGain = 2.0f;
    private const float GlowRgbGain = 1.5f;

    internal static Texture2D GlowAlpha(Texture2D src)
    {
        ulong key = src.GetRid().Id;
        if (_glowCache.TryGetValue(key, out var cached)) return cached;
        var img = src.GetImage();
        if (img == null) { _glowCache[key] = src; return src; }
        if (img.IsCompressed()) img.Decompress();
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        int w = img.GetWidth(), h = img.GetHeight();

        int aMin = 255;
        for (int y = 0; y < h && aMin > 250; y++)
            for (int x = 0; x < w; x++)
            {
                int av = (int)(img.GetPixel(x, y).A * 255f);
                if (av < aMin) { aMin = av; if (aMin <= 250) break; }
            }
        Texture2D result;
        if (aMin <= 250)
        {
            bool changed = false;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = img.GetPixel(x, y);
                    if (c.A >= 0.999f) continue;
                    img.SetPixel(x, y, new Color(c.R, c.G, c.B, Mathf.Min(1f, c.A * GlowAlphaGain)));
                    changed = true;
                }
            result = changed ? ImageTexture.CreateFromImage(img) : src;
        }
        else
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = img.GetPixel(x, y);
                    float lum = Mathf.Max(c.R, Mathf.Max(c.G, c.B));
                    img.SetPixel(x, y, new Color(
                        Mathf.Min(1f, c.R * GlowRgbGain),
                        Mathf.Min(1f, c.G * GlowRgbGain),
                        Mathf.Min(1f, c.B * GlowRgbGain),
                        Mathf.Min(1f, lum * GlowAlphaGain)));
                }
            result = ImageTexture.CreateFromImage(img);
        }
        _glowCache[key] = result;
        return result;
    }

    private static readonly Dictionary<ulong, Texture2D> _srcColorCache = new();

    internal static Texture2D SrcColorSquared(Texture2D src)
    {
        ulong key = src.GetRid().Id;
        if (_srcColorCache.TryGetValue(key, out var cached)) return cached;
        var img = src.GetImage();
        if (img == null) { _srcColorCache[key] = src; return src; }
        if (img.IsCompressed()) img.Decompress();
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        int w = img.GetWidth(), h = img.GetHeight();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = img.GetPixel(x, y);
                float r = c.R * c.A, g = c.G * c.A, b = c.B * c.A;
                img.SetPixel(x, y, new Color(r * r, g * g, b * b, 1f));
            }
        var result = ImageTexture.CreateFromImage(img);
        _srcColorCache[key] = result;
        return result;
    }

    internal static bool IsSrcColorOverInv(int srcBlend, int destBlend) => srcBlend == 3 && destBlend == 4;

    internal static Texture2D TextureForBlend(Texture2D src, bool additive, int srcBlend = 5,
        int destBlend = 6) =>
        IsSrcColorOverInv(srcBlend, destBlend) ? GlowAlpha(src)
        : !additive ? src
        : srcBlend == 2 ? src
        : srcBlend == 3 ? SrcColorSquared(src)
        : src;

    internal static int SrcBlend(Godot.Collections.Dictionary p) =>
        p.ContainsKey("srcBlend") ? p["srcBlend"].AsInt32() : 5;

    internal static int DestBlend(Godot.Collections.Dictionary p) =>
        p.ContainsKey("destBlend") ? p["destBlend"].AsInt32() : 6;

    internal static Texture2D? FirstTexture(Godot.Collections.Dictionary p) => FrameTexture(p, 0);

    internal static Texture2D? FrameTexture(Godot.Collections.Dictionary p, int frame)
    {
        var arr = p["tex"].AsGodotArray();
        if (arr.Count == 0) return null;
        string path = $"res://assets/fx/tex/{arr[Mathf.Clamp(frame, 0, arr.Count - 1)].AsString()}.png";
        return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
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
