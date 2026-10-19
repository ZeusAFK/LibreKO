using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NumericsVector3 = System.Numerics.Vector3;
using Godot;

namespace LibreKO;

public partial class Cape : MeshInstance3D, IFxTicked
{
    public int FxSlot { get; set; } = FxTickLane.Unlisted;

    private const float RefNeckY = 1.7346f;
    private const string AssetDir = "res://assets/capes/";

    public static bool Debug;

    public static bool DrawDebug;

    public static bool Enabled;

    private const float QuadHigh = 0.12f, QuadLow = 0.18f;
    private const int MaxColsHigh = 13, MaxRowsHigh = 17;
    private const int MaxColsLow = 9, MaxRowsLow = 13;
    private const int Iterations = 4;
    private const float StretchCompliance = 0.0000002f;
    private const float ShearCompliance = 0.000004f;
    private const float BendCompliance = 0.0008f;
    private const float Gravity = -9.81f;
    private const float AeroDrag = 0.65f;
    private const float AeroMaxAccel = 24f;
    private const float GustStrength = 0.3f;
    private const float ContactFriction = 0.18f;
    private const float SimDist = 40f;
    private const float ThrottleDist = 18f;

    private const float ThetaMax = 3.0f;
    private const float ClearMargin = 0.045f;
    private const float FlareOut = 0.055f;
    private const float CollarRadius = 0.085f;
    private const float CollarBlendDrop = 0.13f;
    private const float MaxDrapeRadius = 0.29f;
    private const int PleatCount = 3;
    private const float PleatDepth = 0.09f;
    private const float PleatFadeIn = 0.18f;
    private const float MaxPleat = 0.42f;
    private const float MaxGather = 1.40f;

    private static readonly (float T, float R)[] BodyProfile =
    {
        (0.00f, 0.070f),
        (0.12f, 0.170f),
        (0.30f, 0.150f),
        (0.46f, 0.200f),
        (0.66f, 0.175f),
        (1.00f, 0.120f),
    };

    public readonly record struct CapeDef(
        string Name, int Family, int Grade, int Ranking, int C, int M, int Price, int Points, string Desc = "", bool Castle = false);

    private sealed class GridMesh
    {
        public Vector3[] Pos = Array.Empty<Vector3>();
    }

    private static bool loaded;
    private static int cols = 5, rows = 9;
    private static readonly Dictionary<int, CapeDef> table = new();
    private static readonly Dictionary<string, GridMesh> grids = new();
    private static readonly Dictionary<string, Texture2D?> texCache = Shutdown.Track(new Dictionary<string, Texture2D?>());

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        using var f = Godot.FileAccess.Open(AssetDir + "capes.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PushWarning("[cape] capes.json missing — capes disabled"); return; }
        var doc = Json.ParseString(f.GetAsText()).AsGodotDictionary();
        if (doc.TryGetValue("grid", out var g))
        {
            var gd = g.AsGodotDictionary();
            cols = gd["cols"].AsInt32();
            rows = gd["rows"].AsInt32();
        }
        foreach (var k in doc["table"].AsGodotDictionary())
        {
            var o = k.Value.AsGodotDictionary();
            table[int.Parse(k.Key.AsString())] = new CapeDef(
                o["name"].AsString(), o["family"].AsInt32(), o["grade"].AsInt32(),
                o["ranking"].AsInt32(), o["c"].AsInt32(), o["m"].AsInt32(),
                o.TryGetValue("price", out var pr) ? pr.AsInt32() : 0,
                o.TryGetValue("points", out var pt) ? pt.AsInt32() : 0,
                o.TryGetValue("desc", out var desc) ? desc.AsString() : "",
                o.TryGetValue("castle", out var castle) && castle.AsBool());
        }
        foreach (var k in doc["meshes"].AsGodotDictionary())
        {
            var o = k.Value.AsGodotDictionary();
            var savedPositions = o["pos"].AsFloat32Array();
            var m = new GridMesh { Pos = new Vector3[savedPositions.Length / 3] };
            for (int i = 0; i < m.Pos.Length; i++)
                m.Pos[i] = new Vector3(savedPositions[i * 3], savedPositions[i * 3 + 1], savedPositions[i * 3 + 2]);
            grids[k.Key.AsString()] = m;
        }
        GD.Print($"[cape] {table.Count} capes, {grids.Count} fitted grids");
    }

    public static IReadOnlyDictionary<int, CapeDef> Catalogue
    {
        get { Load(); return table; }
    }

    public static bool TryGet(int capeId, out CapeDef def)
    {
        Load();
        return table.TryGetValue(capeId, out def);
    }

    public static Texture2D? ClothTexture(int c) => Tex($"c_{c:00}.png");

    public static Texture2D? MarkTexture(int m) => m > 0 ? Tex($"m_{m:00}.png") : null;

    public static bool IsRenderable(int capeId)
    {
        Load();
        return capeId > 0 && table.ContainsKey(capeId);
    }

    private static Texture2D? blank;

    private static Texture2D Blank
    {
        get
        {
            if (blank != null) return blank;
            var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            img.SetPixel(0, 0, Colors.White);
            return blank = ImageTexture.CreateFromImage(img);
        }
    }

    private static Texture2D? Tex(string file)
    {
        if (texCache.TryGetValue(file, out var t)) return t;
        string path = AssetDir + "tex/" + file;
        t = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        texCache[file] = t;
        return t;
    }

    private static GridMesh? GridFor(int family, int race)
    {
        if (grids.TryGetValue($"{family}:{race}", out var m)) return m;
        int baseRace = race >= 10 ? 11 : 1;
        for (int r = baseRace; r < baseRace + 6; r++)
            if (grids.TryGetValue($"{family}:{r}", out m)) return m;
        foreach (var kv in grids)
            if (kv.Key.StartsWith($"{family}:")) return kv.Value;
        foreach (var kv in grids) return kv.Value;
        return null;
    }

    private Skeleton3D skel = null!;
    private int rideBone = -1;
    private Transform3D anchorLocal;
    private int hipBone = -1, neckBone = -1;
    private int upLegL = -1, loLegL = -1, ftLegL = -1;
    private int upLegR = -1, loLegR = -1, ftLegR = -1;
    private int upArmL = -1, loArmL = -1, handL = -1;
    private int upArmR = -1, loArmR = -1, handR = -1;

    private int gridColumns, gridRows;
    private Vector3[] rest = Array.Empty<Vector3>();
    private Vector3[] pos = Array.Empty<Vector3>();
    private Vector3[] targets = Array.Empty<Vector3>();
    private Vector3[] previous = Array.Empty<Vector3>();
    private Vector3[] renderPos = Array.Empty<Vector3>();
    private Vector3[] vel = Array.Empty<Vector3>();
    private Vector3[] pred = Array.Empty<Vector3>();
    private Vector3[] nrm = Array.Empty<Vector3>();
    private Vector3[] surfacePos = Array.Empty<Vector3>();
    private int[] surfaceIdx = Array.Empty<int>();
    private int surfaceCols, surfaceRows;
    private int[] surfaceSamples = Array.Empty<int>();
    private float[] surfaceWeights = Array.Empty<float>();
    private int[] idx = Array.Empty<int>();
    private float[] inverseMass = Array.Empty<float>();
    private bool[] contact = Array.Empty<bool>();
    private float[] tether = Array.Empty<float>();
    private float[] areaW = Array.Empty<float>();
    private Vector3 bodyFwd = Vector3.Forward;

    private struct Link
    {
        public int A, B;
        public float Length, Compliance, Lambda;
    }

    private Link[] links = Array.Empty<Link>();

    private ArrayMesh mesh = null!;
    private ShaderMaterial mat = null!;
    private static readonly Dictionary<(long Format, int Vertices), byte[]> VertexTemplates = Shutdown.Track(new Dictionary<(long Format, int Vertices), byte[]>());
    private byte[] vertexBytes = Array.Empty<byte>();
    private int normalOffset, normalStride;
    private Basis restBasis;
    private Transform3D lastFrameAnchor, stepAnchor, pinPrevious, pinCurrent;
    private Node3D bodyRoot = null!;
    private VisibleOnScreenNotifier3D visibility = null!;
    private int race, family;
    private bool highDetail;
    private float lastStep;
    private Color dye;
    private Vector3 renderOrigin;
    private bool primed, frozen;
    private float rigScale = 1f;
    private double phase, accum;
    private Vector3 anchorVel;

    private int capeId;

    public int CapeId => capeId;
    internal ReadOnlySpan<Vector3> ClothVertices => surfacePos;
    internal ReadOnlySpan<int> ClothTriangles => surfaceIdx;
    internal int ClothColumns => surfaceCols;
    internal bool Dormant => frozen;
    internal bool Finite => Array.TrueForAll(pos, p => p.IsFinite()) && Array.TrueForAll(surfacePos, p => p.IsFinite());

    public string Resolution => $"{gridColumns}x{gridRows}";

    public static Cape? Attach(Node3D body, int capeId, Color dye, int race, bool highDetail = false)
    {
        Load();
        if (!IsRenderable(capeId)) return null;
        var skel = FindSkeleton(body);
        if (skel == null) return null;

        var def = table[capeId];
        var grid = GridFor(def.Family, race);
        if (grid == null) return null;

        int ride = FirstBone(skel, "UpperChest", "Chest2", "Chest", "Spine", "Hips");
        int neck = FirstBone(skel, "Neck", "Head", "UpperChest", "Chest2");
        if (ride < 0 || neck < 0) return null;

        var cape = new Cape
        {
            Name = "Cape",
            skel = skel,
            bodyRoot = body,
            race = race,
            highDetail = highDetail,
            rideBone = ride,
            neckBone = neck,
            hipBone = FirstBone(skel, "Hips", "Pelvis", "Root"),
            upLegL = FirstBone(skel, "UpperLeg_L", "LeftHip"),
            loLegL = FirstBone(skel, "LowerLeg_L", "LeftKnee"),
            ftLegL = FirstBone(skel, "Foot_L", "LeftAnkle"),
            upLegR = FirstBone(skel, "UpperLeg_R", "RightHip"),
            loLegR = FirstBone(skel, "LowerLeg_R", "RightKnee"),
            ftLegR = FirstBone(skel, "Foot_R", "RightAnkle"),
            upArmL = FirstBone(skel, "UpperArm_L", "LeftShoulder"),
            loArmL = FirstBone(skel, "LowerArm_L", "LeftElbow"),
            handL = FirstBone(skel, "Hand_L", "LeftWrist"),
            upArmR = FirstBone(skel, "UpperArm_R", "RightShoulder"),
            loArmR = FirstBone(skel, "LowerArm_R", "RightElbow"),
            handR = FirstBone(skel, "Hand_R", "RightWrist"),
            TopLevel = true,
            CastShadow = ShadowCastingSetting.Off,
        };
        body.AddChild(cape);
        cape.Build(grid, capeId, dye, highDetail, body);
        return cape;
    }

    public void SetCape(int capeId, Color dye)
    {
        Load();
        if (!table.TryGetValue(capeId, out var def)) { QueueFree(); return; }
        if (family != def.Family && GridFor(def.Family, race) is { } grid)
        {
            Build(grid, capeId, dye, highDetail, bodyRoot);
            return;
        }
        this.capeId = capeId;
        this.dye = dye;
        ApplyDye(dye);
        var mark = def.M > 0 ? Tex($"m_{def.M:00}.png") : null;
        mat.SetShaderParameter("base_tex", Tex($"c_{def.C:00}.png") ?? Blank);
        mat.SetShaderParameter("mark_tex", mark ?? Blank);
        mat.SetShaderParameter("has_mark", mark != null ? 1.0f : 0.0f);
    }

    private void ApplyDye(Color dye)
    {
        bool dyed = dye.R > 0.001f || dye.G > 0.001f || dye.B > 0.001f;
        var c = dyed ? dye : Colors.White;
        mat.SetShaderParameter("dye", new Vector3(c.R, c.G, c.B));
    }

    public override void _EnterTree()
    {
        var lane = FxDriver.Lane(this, FxLane.Late);
        if (lane == null) return;
        lane.Join(this);
        lane.AfterTick = FlushBatch;
    }

    private static readonly List<Cape> Batch = new();
    private float stepH, stepDt;
    private Transform3D stepTarget;
    private bool stepOk;

    internal static void FlushBatch()
    {
        if (Batch.Count == 0) return;
        using var scope = Perf.Measure(Perf.Section.Cape);
        foreach (var cape in Batch)
        {
            if (!IsInstanceValid(cape)) continue;
            cape.RunStep();
            cape.FinishStep();
        }
        Batch.Clear();
    }

    internal void TickNow(double delta)
    {
        FxTick(delta);
        FlushBatch();
    }

    private void RunStep()
    {
        try
        {
            Step();
            stepOk = true;
        }
        catch (Exception)
        {
            stepOk = false;
        }
    }

    private void Step()
    {
        float h = stepH, dt = stepDt;
        var anchor = stepTarget;
        while (accum + 1e-8 >= h)
        {
            float blend = Mathf.Clamp(1f - ((float)accum - h) / dt, 0f, 1f);
            var step = lastFrameAnchor.InterpolateWith(anchor, blend);
            Simulate(h, step, blend);
            accum -= h;
        }
        lastFrameAnchor = anchor;
        renderOrigin = anchor.Origin;
        float alpha = Mathf.Clamp((float)accum / h, 0, 1);
        for (int i = 0; i < pos.Length; i++)
            renderPos[i] = inverseMass[i] == 0 ? pins[i] : previous[i].Lerp(pos[i], alpha);
        SmoothSurface(renderPos);
        CollideRender();
        Pack(surfacePos);
    }

    private void FinishStep()
    {
        if (!stepOk)
        {
            primed = false;
            return;
        }
        GlobalTransform = new Transform3D(Basis.Identity, renderOrigin);
        mesh.SurfaceUpdateVertexRegion(0, 0, vertexBytes);
        CommitColliders();
        pinPrevious = pinCurrent;
        Array.Copy(pins, oldPins, gridColumns);
        if (DrawDebug) DrawDebugOverlay();
        else if (dbg != null) dbg.Visible = false;
    }

    public override void _ExitTree() => FxDriver.Lane(this, FxLane.Late)?.Leave(this);

    private Transform3D parkedPin;
    private bool parkedWritten;

    public void FxTick(double delta)
    {
        using var scope = Perf.Measure(Perf.Section.Cape);
        if (!IsInstanceValid(skel) || rest.Length == 0 || delta <= 0) return;
        var anchor = AnchorWorld();
        pinCurrent = AttachmentWorld();
        var cam = GetViewport().GetCamera3D();
        float distance2 = cam == null ? 0f : cam.GlobalPosition.DistanceSquaredTo(anchor.Origin);
        bool dormant = !IsVisibleInTree() || distance2 > SimDist * SimDist
            || (cam != null && !visibility.IsOnScreen());
        if (dormant)
        {
            if (!frozen)
            {
                renderOrigin = Vector3.Zero;
                SmoothSurface(rest);
                Upload(surfacePos);
                frozen = true;
            }
            if (!parkedWritten || !pinCurrent.IsEqualApprox(parkedPin))
            {
                GlobalTransform = pinCurrent;
                parkedPin = pinCurrent;
                parkedWritten = true;
            }
            lastFrameAnchor = anchor;
            pinPrevious = pinCurrent;
            primed = false;
            if (dbg != null) dbg.Visible = false;
            return;
        }
        parkedWritten = false;
        UpdatePins();
        if (!primed || frozen || delta > 0.2 || anchor.Origin.DistanceSquaredTo(lastFrameAnchor.Origin) > 9f)
            Reset(anchor);
        frozen = false;

        float h = distance2 > ThrottleDist * ThrottleDist ? 1f / 30f
            : highDetail ? 1f / 120f : 1f / 60f;
        if (delta > 0.035) h = Mathf.Max(h, 1f / 60f);
        if (lastStep != h) { accum = 0; lastStep = h; }
        float dt = Mathf.Min((float)delta, h * 4);
        accum += dt;
        UpdateColliders();
        stepH = h;
        stepDt = dt;
        stepTarget = anchor;
        Batch.Add(this);
    }

    private void Reset(Transform3D anchor)
    {
        pinPrevious = pinCurrent;
        Array.Copy(pins, oldPins, gridColumns);
        Array.Copy(pins, stepPins, gridColumns);
        BuildTargets(anchor, pinCurrent);
        for (int i = 0; i < rest.Length; i++)
        {
            pos[i] = previous[i] = pred[i] = targets[i];
            vel[i] = Vector3.Zero;
        }
        stepAnchor = lastFrameAnchor = anchor;
        anchorVel = Vector3.Zero;
        accum = 0;
        primed = true;
        collidersPrimed = false;
    }

    public static void RefreshBody(Node3D body)
    {
        if (FindFirst<Cape>(body) is { } cape && !cape.IsQueuedForDeletion()
            && GridFor(cape.family, cape.race) is { } grid)
            cape.Build(grid, cape.capeId, cape.dye, cape.highDetail, cape.bodyRoot);
    }

    private float aspect = 1f;
    private readonly (Vector3 A, Vector3 B, float Ra, float Rb, float Aspect)[] segs
        = new (Vector3, Vector3, float, float, float)[20];

    private static T? FindFirst<T>(Node n) where T : class
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren())
            if (FindFirst<T>(c) is { } found) return found;
        return null;
    }
    private const float UpperArmRadius = 0.090f, ForeArmRadius = 0.075f, HandRadius = 0.070f;
    private const float ThighRadius = 0.115f, CalfRadius = 0.085f;
    private float rNeck, rChest, rHips, inflate;

    private const int ProfileBins = 14;
    private const float CollideInflate = 0.02f;
    private float[]? fitted;

    private MeshInstance3D? dbg;

    private void SmoothSurface(Vector3[] source)
    {
        var input = MemoryMarshal.Cast<Vector3, NumericsVector3>(source.AsSpan());
        var output = MemoryMarshal.Cast<Vector3, NumericsVector3>(surfacePos.AsSpan());
        for (int i = 0; i < output.Length; i++)
        {
            var position = NumericsVector3.Zero;
            int start = i * 16;
            for (int j = start; j < start + 16; j++)
                position += input[surfaceSamples[j]] * surfaceWeights[j];
            output[i] = position;
        }
    }

    private void Upload(Vector3[] positions)
    {
        Pack(positions);
        mesh.SurfaceUpdateVertexRegion(0, 0, vertexBytes);
    }

    private void Pack(Vector3[] positions)
    {
        var points = MemoryMarshal.Cast<Vector3, NumericsVector3>(positions.AsSpan());
        var normals = MemoryMarshal.Cast<Vector3, NumericsVector3>(nrm.AsSpan());
        normals.Clear();
        for (int t = 0; t < surfaceIdx.Length; t += 3)
        {
            int a = surfaceIdx[t], b = surfaceIdx[t + 1], c = surfaceIdx[t + 2];
            var normal = NumericsVector3.Cross(points[b] - points[a], points[c] - points[a]);
            normals[a] += normal; normals[b] += normal; normals[c] += normal;
        }
        var vertices = MemoryMarshal.Cast<byte, Vector3>(vertexBytes.AsSpan(0, positions.Length * 12));
        for (int i = 0; i < positions.Length; i++)
        {
            vertices[i] = positions[i] - renderOrigin;
            var normal = nrm[i].LengthSquared() > 1e-12f ? nrm[i].Normalized() : Vector3.Back;
            var oct = normal.OctahedronEncode();
            int offset = normalOffset + i * normalStride;
            BinaryPrimitives.WriteUInt16LittleEndian(vertexBytes.AsSpan(offset), (ushort)Mathf.Clamp(oct.X * 65535, 0, 65535));
            BinaryPrimitives.WriteUInt16LittleEndian(vertexBytes.AsSpan(offset + 2), (ushort)Mathf.Clamp(oct.Y * 65535, 0, 65535));
        }
    }

    private static Skeleton3D? FindSkeleton(Node n)
    {
        if (n is Skeleton3D s) return s;
        foreach (var c in n.GetChildren())
            if (FindSkeleton(c) is { } found) return found;
        return null;
    }

    private static Basis BodyBasis(Skeleton3D skel)
    {
        var feet = Vector3.Zero;
        foreach (int foot in new[] { FirstBone(skel, "Foot_L", "LeftAnkle"), FirstBone(skel, "Foot_R", "RightAnkle") })
        {
            if (foot < 0) continue;
            var children = skel.GetBoneChildren(foot);
            if (children.Length > 0) feet += BoneDelta(skel, foot, children[0]);
        }
        var fwd = Flat(feet);
        if (fwd == Vector3.Zero) fwd = Flat(BoneDelta(skel, FootBone(skel), ToeBone(skel)));
        if (fwd == Vector3.Zero)
            fwd = Flat(BoneDelta(skel, FirstBone(skel, "Neck"), FirstBone(skel, "Head")));
        if (fwd == Vector3.Zero) return Basis.Identity;
        var up = Vector3.Up;
        return new Basis(up.Cross(fwd), up, fwd);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.Y = 0f;
        return v.LengthSquared() < 1e-8f ? Vector3.Zero : v.Normalized();
    }

    private static Vector3 BoneDelta(Skeleton3D skel, int from, int to)
        => from < 0 || to < 0 ? Vector3.Zero
         : skel.GetBoneGlobalRest(to).Origin - skel.GetBoneGlobalRest(from).Origin;

    private static int FootBone(Skeleton3D skel) => FirstBone(skel, "Foot_R", "RightAnkle", "Foot_L", "LeftAnkle");

    private static int ToeBone(Skeleton3D skel)
    {
        int named = FirstBone(skel, "Toe_R", "Toe_L");
        if (named >= 0) return named;
        int foot = FootBone(skel);
        if (foot < 0) return -1;
        var kids = skel.GetBoneChildren(foot);
        return kids.Length > 0 ? kids[0] : -1;
    }

    private static int FirstBone(Skeleton3D skel, params string[] names)
    {
        foreach (var want in names)
        {
            int direct = skel.FindBone(want);
            if (direct >= 0) return direct;
            string key = want.Replace(" ", "").ToLowerInvariant();
            for (int i = 0; i < skel.GetBoneCount(); i++)
                if (skel.GetBoneName(i).Replace(" ", "").ToLowerInvariant() == key) return i;
        }
        return -1;
    }

    private static Shader ClothShader => Shaders.Get("cloth");
}

