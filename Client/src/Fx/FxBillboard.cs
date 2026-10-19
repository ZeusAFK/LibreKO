using Godot;
using System.Collections.Generic;

namespace LibreKO;

public partial class FxBillboard : MeshInstance3D, IFxPooledPart, IFxPart
{
    private ShaderMaterial mat = null!;
    private Texture2D?[] frames = System.Array.Empty<Texture2D?>();
    private float age;
    private Vector3 initPos, initVel, accel;
    private float start, life, fadeIn, fadeOut, hideTime, showTime;
    private float width, height, widthVelocity, heightVelocity, widthAcceleration, heightAcceleration;
    private bool spins, explicitRot, flat;
    private Basis fixedBasis = Basis.Identity;
    private Basis matrixBasis = Basis.Identity;
    private float spinRate;
    private Color lastColor = new Color(-1, -1, -1, -1);
    private float gap;
    private float blendWord;
    private float texFps; private int frameCount; private bool wrap;
    private int atlasCols = 1, atlasRows = 1;
    private int atlasFrames = 1;
    private ArrayMesh? groundFan;
    private bool groundFanBuilt;
    private bool mirroredQuarterUv;
    private float fanW, fanH, fanRot;
    private FxInstance? root;
    private bool? shown;
    private bool done;
    private int lastFrame = -1, lastCell = -1;
    private FxBoardBatch? batcher;
    private FxBoardBatch.Batch? batch;
    private FxBoardBatch.Key key;
    private Texture2DArray? sequence;
    private int layers = 1;

    internal bool Batched { get; private set; }
    public FxPartKey? PoolKey { get; set; }
    internal float Fade;
    internal Vector2 AtlasOffset;

    public static System.Func<float, float, float?>? GroundHeight;

    private static readonly Dictionary<FxPartKey, FxBillboard> prototypes = Shutdown.Track(new Dictionary<FxPartKey, FxBillboard>());

    public static FxBillboard? Build(Godot.Collections.Dictionary p, FxPartKey? partKey = null)
    {
        if (partKey is { } pooledKey)
        {
            if (Fx.TakePooledPart(pooledKey) is FxBillboard pooled) return pooled;
            if (prototypes.TryGetValue(pooledKey, out var prototype)) return prototype.Clone(pooledKey);
        }
        var built = BuildFresh(p, partKey);
        if (built != null && partKey is { } protoKey) prototypes[protoKey] = built.Clone(protoKey);
        return built;
    }

    private FxBillboard Clone(FxPartKey? partKey)
    {
        var c = new FxBillboard
        {
            CastShadow = ShadowCastingSetting.Off,
            frames = frames,
            initPos = initPos, initVel = initVel, accel = accel,
            start = start, life = life, fadeIn = fadeIn, fadeOut = fadeOut,
            hideTime = hideTime, showTime = showTime,
            width = width, height = height, widthVelocity = widthVelocity, heightVelocity = heightVelocity, widthAcceleration = widthAcceleration, heightAcceleration = heightAcceleration,
            spins = spins, explicitRot = explicitRot, flat = flat,
            fixedBasis = fixedBasis, matrixBasis = matrixBasis, spinRate = spinRate,
            gap = gap, blendWord = blendWord,
            texFps = texFps, frameCount = frameCount, wrap = wrap,
            atlasCols = atlasCols, atlasRows = atlasRows, atlasFrames = atlasFrames,
            mirroredQuarterUv = mirroredQuarterUv,
            key = key, sequence = sequence, layers = layers,
            PoolKey = partKey,
        };
        if (flat && mat != null)
        {
            c.mat = (ShaderMaterial)mat.Duplicate();
            c.MaterialOverride = c.mat;
            c.Layers = Layers;
        }
        if (Batched)
        {
            c.Batched = true;
            c.SetNotifyTransform(false);
        }
        return c;
    }

    private static FxBillboard? BuildFresh(Godot.Collections.Dictionary p, FxPartKey? partKey)
    {
        var tex0 = Fx.FirstTexture(p);
        if (tex0 == null) return null;
        bool flat = p.ContainsKey("flat") && p["flat"].AsBool();
        var rv = Fx.ReadSpinRate(p);
        float spin = flat ? rv.Y : rv.X;
        bool autoSpin = p.ContainsKey("autoSpin") && p["autoSpin"].AsBool();
        if (autoSpin && Mathf.Abs(spin) < 1e-4f) spin = Mathf.DegToRad(50f);
        float bundleScale = Fx.ReadF(p, "bundleScale", 1f);
        var bb = new FxBillboard
        {
            CastShadow = ShadowCastingSetting.Off,
            initPos = Fx.ReadVec3(p, "initPos"),
            initVel = Fx.ReadVec3(p, "initVel"),
            accel = Fx.ReadVec3(p, "accel"),
            start = Fx.ReadF(p, "startTime"),
            life = Fx.ReadF(p, "life"),
            fadeIn = Fx.ReadF(p, "fadeIn"),
            fadeOut = Fx.ReadF(p, "fadeOut"),
            hideTime = Fx.ReadF(p, "hideTime"),
            showTime = Fx.ReadF(p, "showTime"),
            width = Mathf.Max(0.01f, Fx.ReadF(p, "sizeW", 1f) * bundleScale),
            height = Mathf.Max(0.01f, Fx.ReadF(p, "sizeH", 1f) * bundleScale),
            spins = Mathf.Abs(spin) > 1e-4f,
            spinRate = spin,
            explicitRot = p.ContainsKey("rotEnable") && p["rotEnable"].AsBool(),
            flat = flat,
            mirroredQuarterUv = p.ContainsKey("mirroredQuarterUv") && p["mirroredQuarterUv"].AsBool(),
            gap = Fx.ReadF(p, "gap"),
            fixedBasis = Fx.FixedRotation(Fx.ReadVec3(p, "rot")),
            matrixBasis = Fx.ReadMapMatrixBasis(p),
            texFps = Fx.ReadF(p, "texFPS"),
            frameCount = Mathf.Max(1, p["frameCount"].AsInt32()),
            wrap = p.ContainsKey("frameWrap") && p["frameWrap"].AsBool(),
            PoolKey = partKey,
        };
        int num = p.ContainsKey("num") ? Mathf.Max(1, p["num"].AsInt32()) : 1;
        int packedBlend = FxShading.Packed(p, num);
        int rf = FxShading.RenderFlags(p);
        bb.blendWord = packedBlend;
        var sv = p.ContainsKey("sizeVel") ? p["sizeVel"].AsGodotArray() : new Godot.Collections.Array();
        var sa = p.ContainsKey("sizeAccel") ? p["sizeAccel"].AsGodotArray() : new Godot.Collections.Array();
        if (sv.Count == 2) { bb.widthVelocity = (float)sv[0].AsDouble() * bundleScale; bb.heightVelocity = (float)sv[1].AsDouble() * bundleScale; }
        if (sa.Count == 2) { bb.widthAcceleration = (float)sa[0].AsDouble() * bundleScale; bb.heightAcceleration = (float)sa[1].AsDouble() * bundleScale; }

        if (flat)
        {
            bb.mat = FxShading.SurfaceMaterial(rf, packedBlend, tex0);
            bb.MaterialOverride = bb.mat;
            bb.Layers = FxShading.LayerBit;
        }

        if (p.ContainsKey("atlas"))
        {
            var g = p["atlas"].AsGodotArray();
            bb.atlasCols = Mathf.Max(1, g[0].AsInt32());
            bb.atlasRows = Mathf.Max(1, g[1].AsInt32());
            bb.atlasFrames = p.ContainsKey("atlasFrames")
                ? Mathf.Clamp(p["atlasFrames"].AsInt32(), 1, bb.atlasCols * bb.atlasRows)
                : bb.atlasCols * bb.atlasRows;
            if (flat) bb.mat.SetShaderParameter(FxShading.UvScaleParam, new Vector2(1f / bb.atlasCols, 1f / bb.atlasRows));
        }

        bb.frames = new Texture2D?[bb.frameCount];
        for (int i = 0; i < bb.frameCount; i++) bb.frames[i] = Fx.FrameTexture(p, i);
        if (!flat)
        {
            var (sequence, sequenceId, layers) = FxBoardBatch.SequenceFor(bb.frames);
            if (sequence == null) return null;
            bb.sequence = sequence;
            bb.layers = layers;
            bb.key = new FxBoardBatch.Key(sequenceId, !FxShading.Commutative(packedBlend),
                FxShading.DoubleSided(rf), FxShading.NoDepthTest(rf), FxShading.Priority(rf),
                1f / bb.atlasCols, 1f / bb.atlasRows);
            bb.Batched = true;
            bb.SetNotifyTransform(false);
        }
        return bb;
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

    private void SetShown(bool show)
    {
        if (shown == show) return;
        shown = show;
        if (!Batched) Visible = show;
    }

    internal void ResetShown() => shown = null;

    public void Reset()
    {
        age = 0f;
        shown = null;
        lastColor = new Color(-1, -1, -1, -1);
        lastFrame = lastCell = -1;
        Fade = 0f;
        AtlasOffset = Vector2.Zero;
        batcher = null;
        batch = null;
        Transparency = 0f;
        Visible = true;
        done = false;
    }

    public override void _Process(double delta) => Tick(delta);

    public void Tick(double delta)
    {
        using var scope = Perf.Measure(Perf.Section.FxBoard);
        if (done || Fx.ShuttingDown) return;
        age += (float)delta;
        float localT = age - start;
        if (localT < 0f) { SetShown(false); return; }
        if (!Fx.PartAge(localT, life, fadeIn, fadeOut, out float t, out float a))
        {
            SetShown(false);
            done = true;
            if (root == null) SetProcess(false);
            Fx.RecyclePart(this);
            return;
        }
        if (Fx.PartHidden(localT, hideTime, showTime)) { SetShown(false); return; }
        SetShown(true);

        Vector3 pos = initPos + initVel * t + 0.5f * accel * (t * t);
        float w = Mathf.Max(0.001f, width + widthVelocity * t + 0.5f * widthAcceleration * t * t);
        float h = Mathf.Max(0.001f, height + heightVelocity * t + 0.5f * heightAcceleration * t * t);

        if (flat) PlaceOnGround(pos, w, h, spinRate * t);
        else
        {
            bool hasCam;
            Transform3D camXf;
            Basis parentInverse;
            if (root != null)
            {
                root.EnsureFrame();
                hasCam = root.HasCam;
                camXf = root.CamXf;
                parentInverse = root.FrameInverse;
            }
            else
            {
                hasCam = Fx.FrameCamera(this, out camXf);
                var parent = GetParentOrNull<Node3D>();
                parentInverse = (parent?.GlobalTransform.Basis.Orthonormalized() ?? Basis.Identity).Inverse();
            }
            Basis basis;
            if (explicitRot)
            {
                Basis worldBasis = fixedBasis;
                if (spins) worldBasis *= new Basis(Vector3.Back, spinRate * localT);
                basis = parentInverse * worldBasis * matrixBasis;
            }
            else if (hasCam)
            {
                // View-plane, not look-at-position: FX_CAPTURE_HANDOFF.md measures 963u-apart
                // quads sharing one normal to 0.00 deg.
                Basis camBasis = camXf.Basis.Orthonormalized();
                Basis worldBasis = camBasis * matrixBasis;
                if (spins) worldBasis = worldBasis.Rotated(camBasis.Z, spinRate * localT);
                basis = parentInverse * worldBasis;
            }
            else basis = Basis.Identity;
            // NOT Basis.Scaled — it scales the rows, so a yawed quad's width lands on world Z and the
            // disc collapses to a 1-unit lens at yaw 90.
            var local = new Transform3D(basis * Basis.FromScale(new Vector3(w, h, 1f)), pos);
            if (Batched)
            {
                WriteBatched(local, t, a, Mathf.Max(w, h));
                return;
            }
            Transform = local;
        }

        if (FlipbookEnded(t)) a = 0f;
        var want = new Color(1f, 1f, 1f, a);
        if (want != lastColor) { mat.SetShaderParameter(FxShading.TintParam, want); lastColor = want; }

        if (atlasCols * atlasRows > 1 && texFps > 0f)
        {
            int cell = AtlasCell(t);
            if (cell != lastCell)
            {
                lastCell = cell;
                AtlasOffset = AtlasOffsetFor(cell);
                mat.SetShaderParameter(FxShading.UvOffsetParam, AtlasOffset);
            }
        }
        else if (frameCount > 1 && texFps > 0f)
        {
            int f = FrameIndex(t);
            if (f == lastFrame) return;
            lastFrame = f;
            if (frames[f] is { } texture) mat.SetShaderParameter(FxShading.TextureParam, texture);
        }
    }

    private bool FlipbookEnded(float t) =>
        life > 0f && !wrap && texFps > 0f && (int)(texFps * t) >= Mathf.Max(frameCount, atlasFrames);

    private int AtlasCell(float t)
    {
        int frame = Mathf.Max(0, (int)(texFps * t));
        return wrap ? frame % atlasFrames : Mathf.Min(frame, atlasFrames - 1);
    }

    private Vector2 AtlasOffsetFor(int cell) =>
        new((cell % atlasCols) / (float)atlasCols, (cell / atlasCols) / (float)atlasRows);

    private int FrameIndex(float t)
    {
        int f = (int)(texFps * t);
        return wrap ? ((f % frameCount) + frameCount) % frameCount : Mathf.Min(f, frameCount - 1);
    }

    private void WriteBatched(in Transform3D local, float t, float a, float extent)
    {
        Transform3D world;
        Vector3 camPos;
        bool visible;
        if (root != null)
        {
            world = root.FrameXf * local;
            camPos = root.HasCam ? root.CamXf.Origin : world.Origin;
            visible = root.VisibleInTree;
        }
        else
        {
            var parent = GetParentOrNull<Node3D>();
            world = parent != null ? parent.GlobalTransform * local : local;
            camPos = Fx.FrameCamera(this, out var camXf) ? camXf.Origin : world.Origin;
            visible = IsVisibleInTree();
        }
        float keep = 1f - Fade;
        Vector2 uvOffset = Vector2.Zero;
        float frame = 0f;
        if (atlasCols * atlasRows > 1 && texFps > 0f)
        {
            AtlasOffset = AtlasOffsetFor(AtlasCell(t));
            uvOffset = AtlasOffset;
        }
        else if (layers > 1 && texFps > 0f)
            frame = FrameIndex(t);
        if (!visible || a <= 0f || keep <= 0f || FlipbookEnded(t)) return;
        batcher ??= FxBoardBatch.For(this);
        if (batcher == null) return;
        if (batch == null || batch.Pruned) batch = batcher.Resolve(key, sequence!);
        FxBoardBatch.Write(batch, world, new Color(keep, keep, keep, a), uvOffset, frame, blendWord,
            camPos.DistanceSquaredTo(world.Origin), extent);
    }

    private const int GroundFanSegments = 24;
    private const int GroundFanRings = 4;
    private const float GroundFanRebakeScale = 1.5f;
    private const float GroundFanRebakeAngle = Mathf.Pi / 6f;
    private const float GroundFanRebakeMove = 0.5f;
    private const double GroundFanRebakeInterval = 0.1;
    private Vector3 fanAt;
    private double fanBakedAt;

    private bool GroundFanStale(float w, float h, float rotation, Vector3 centre)
    {
        if (!groundFanBuilt) return true;
        double now = Time.GetTicksMsec() * 0.001;
        if (now - fanBakedAt < GroundFanRebakeInterval) return false;
        float sw = w / fanW, sh = h / fanH;
        return sw > GroundFanRebakeScale || sw < 1f / GroundFanRebakeScale
            || sh > GroundFanRebakeScale || sh < 1f / GroundFanRebakeScale
            || Mathf.Abs(Mathf.AngleDifference(rotation, fanRot)) > GroundFanRebakeAngle
            || centre.DistanceSquaredTo(fanAt) > GroundFanRebakeMove * GroundFanRebakeMove;
    }

    private void PlaceOnGround(Vector3 pos, float w, float h, float spin)
    {
        if (!IsInsideTree()) return;
        Transform3D frame, toLocal;
        if (root != null)
        {
            root.EnsureFrame();
            frame = root.FrameXf;
            toLocal = root.FrameXfInverse;
        }
        else
        {
            frame = GetParentOrNull<Node3D>()?.GlobalTransform ?? Transform3D.Identity;
            toLocal = frame.AffineInverse();
        }
        Vector3 centre = frame * pos;
        centre.Y = GroundY(centre.X, centre.Z, centre.Y) + gap;
        Vector3 frameScale = frame.Basis.Scale;
        w *= Mathf.Abs(frameScale.X);
        h *= Mathf.Abs(frameScale.Z);
        EnsureGroundFan(w, h, spin, centre);
        var fanScale = new Vector3(w / fanW, 1f, h / fanH);
        Transform = toLocal * new Transform3D(new Basis(Vector3.Up, spin - fanRot) * Basis.FromScale(fanScale), centre);
    }

    private void EnsureGroundFan(float w, float h, float rotation, Vector3 gp)
    {
        if (!GroundFanStale(w, h, rotation, gp)) return;
        groundFanBuilt = true;
        fanW = w; fanH = h; fanRot = rotation; fanAt = gp;
        fanBakedAt = Time.GetTicksMsec() * 0.001;
        groundFan ??= new ArrayMesh();
        groundFan.ClearSurfaces();
        if (Mesh != groundFan) Mesh = groundFan;
        float c = Mathf.Cos(rotation), s = Mathf.Sin(rotation);
        Vector3 right = new(c, 0f, -s);
        Vector3 forward = new(s, 0f, c);

        int rim = GroundFanSegments;
        var verts = new Vector3[1 + GroundFanRings * rim];
        var uvs = new Vector2[verts.Length];
        verts[0] = new Vector3(0f, GroundY(gp.X, gp.Z, gp.Y - gap) + gap - gp.Y, 0f);
        uvs[0] = GroundUv(0f, 0f, mirroredQuarterUv);
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
                verts[v] = new Vector3(off.X, GroundY(gp.X + off.X, gp.Z + off.Z, gp.Y - gap) + gap - gp.Y, off.Z);
                uvs[v] = GroundUv(cx, cz, mirroredQuarterUv);
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
        groundFan.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
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
