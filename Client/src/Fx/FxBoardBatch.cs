using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class FxBoardBatch : Node3D
{
    internal readonly record struct Key(
        ulong Sequence, bool Sorted, bool DoubleSided, bool NoDepthTest, int Priority, float UvScaleX, float UvScaleY);

    internal sealed class Batch
    {
        public MultiMeshInstance3D Instance = null!;
        public MultiMesh Multimesh = null!;
        public float[] Data = new float[InstanceStride * InitialCapacity];
        public float[] Sorted = new float[InstanceStride * InitialCapacity];
        public float[] Depth = new float[InitialCapacity];
        public int[] Order = new int[InitialCapacity];
        public int Count;
        public int Uploaded;
        public int IdleFrames;
        public bool Shown = true;
        public bool Pruned;
        public bool SortByDepth;
        public Vector3 Min, Max;
        public float Extent;
    }

    private const int InstanceStride = 20;
    private const int InitialCapacity = 64;
    private const int BatcherProcessPriority = 900;
    private const int PruneIdleFrames = 600;
    private const int ShrinkRatio = 4;

    private static readonly Dictionary<ulong, FxBoardBatch> _perViewport = new();
    private static readonly Dictionary<string, (Texture2DArray Array, ulong Id)> _sequences = Shutdown.Track(new Dictionary<string, (Texture2DArray Array, ulong Id)>());
    private static readonly Dictionary<(bool DoubleSided, bool NoDepthTest), Shader> _shaders = Shutdown.Track(new Dictionary<(bool DoubleSided, bool NoDepthTest), Shader>());
    private static readonly StringName FramesParam = "frames";
    private static readonly StringName UvScaleParam = "uv_scale";
    private static QuadMesh? _quad;
    private static ulong _nextSequenceId = 1;

    private readonly Dictionary<Key, Batch> _batches = new();
    private readonly List<Key> _prune = new();

    internal static FxBoardBatch? For(Node part)
    {
        var viewport = part.GetViewport();
        if (viewport == null) return null;
        ulong id = viewport.GetInstanceId();
        if (_perViewport.TryGetValue(id, out var batcher) && GodotObject.IsInstanceValid(batcher)) return batcher;
        batcher = new FxBoardBatch { Name = "FxBoards", ProcessPriority = BatcherProcessPriority };
        _perViewport[id] = batcher;
        viewport.CallDeferred(Node.MethodName.AddChild, batcher);
        return batcher;
    }

    internal static (Texture2DArray Array, ulong Id, int Layers) SequenceFor(Texture2D?[] frames)
    {
        Texture2D? first = null;
        foreach (var f in frames) if (f != null) { first = f; break; }
        if (first == null) return (null!, 0, 0);
        var keyBuilder = new System.Text.StringBuilder();
        foreach (var f in frames) keyBuilder.Append((f ?? first).GetRid().Id).Append(',');
        string key = keyBuilder.ToString();
        if (_sequences.TryGetValue(key, out var cached)) return (cached.Array, cached.Id, frames.Length);
        using var scope = Perf.Measure(Perf.Section.SpawnSequence);

        var images = new Godot.Collections.Array<Image>();
        foreach (var f in frames)
        {
            var image = FxImages.Read(f ?? first) ?? Image.CreateEmpty(first.GetWidth(), first.GetHeight(), false, Image.Format.Rgba8);
            if (image.IsCompressed()) image.Decompress();
            image.ClearMipmaps();
            image.Convert(Image.Format.Rgba8);
            if (image.GetWidth() != first.GetWidth() || image.GetHeight() != first.GetHeight())
                image.Resize(first.GetWidth(), first.GetHeight());
            images.Add(image);
        }
        var array = new Texture2DArray();
        array.CreateFromImages(images);
        foreach (var image in images) image.Dispose();
        var entry = (array, _nextSequenceId++);
        _sequences[key] = entry;
        return (entry.array, entry.Item2, frames.Length);
    }

    internal Batch Resolve(Key key, Texture2DArray sequence)
    {
        if (_batches.TryGetValue(key, out var batch)) return batch;
        var material = new ShaderMaterial
        {
            Shader = ShaderFor(key.DoubleSided, key.NoDepthTest),
            RenderPriority = key.Priority,
        };
        material.SetShaderParameter(FramesParam, sequence);
        material.SetShaderParameter(UvScaleParam, new Vector2(key.UvScaleX, key.UvScaleY));
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = _quad ??= new QuadMesh { Size = Vector2.One },
            InstanceCount = InitialCapacity,
            VisibleInstanceCount = 0,
        };
        var instance = new MultiMeshInstance3D
        {
            Multimesh = multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            IgnoreOcclusionCulling = true,
            Layers = FxShading.LayerBit,
        };
        AddChild(instance);
        batch = new Batch { Instance = instance, Multimesh = multimesh, SortByDepth = key.Sorted };
        _batches[key] = batch;
        return batch;
    }

    private static Shader ShaderFor(bool doubleSided, bool noDepthTest)
    {
        var key = (doubleSided, noDepthTest);
        if (_shaders.TryGetValue(key, out var shader)) return shader;
        shader = new Shader
        {
            Code = "shader_type spatial;\n"
                   + $"render_mode {FxShading.RenderModes(doubleSided, noDepthTest)};\n"
                   + "uniform sampler2DArray frames : filter_linear, repeat_enable;\n"
                   + "uniform vec2 uv_scale = vec2(1.0, 1.0);\n"
                   + "varying vec3 cell;\n"
                   + "varying flat float blend_word;\n"
                   + FxShading.Header()
                   + "void vertex() {\n    cell = INSTANCE_CUSTOM.xyz;\n    blend_word = INSTANCE_CUSTOM.w;\n"
                   + FxShading.ClipVertex + "}\n"
                   + "void fragment() {\n"
                   + "    vec4 fx_out = fx_shade(texture(frames, vec3(UV * uv_scale + cell.xy, cell.z)), vec4(1.0, 1.0, 1.0, COLOR.a), "
                   + $"blend_word, fx_clip, CAMERA_VISIBLE_LAYERS, {FxShading.DepthTested(noDepthTest)});\n"
                   + FxShading.Apply("COLOR.r")
                   + "}\n",
        };
        _shaders[key] = shader;
        return shader;
    }

    internal static void Write(Batch b, in Transform3D xf, Color color, Vector2 uvOffset, float frame,
        float blendWord, float depth, float extent)
    {
        int capacity = b.Depth.Length;
        if (b.Count == capacity)
        {
            int grown = capacity * 2;
            System.Array.Resize(ref b.Data, grown * InstanceStride);
            System.Array.Resize(ref b.Sorted, grown * InstanceStride);
            System.Array.Resize(ref b.Depth, grown);
            System.Array.Resize(ref b.Order, grown);
        }
        int i = b.Count * InstanceStride;
        var d = b.Data;
        var basis = xf.Basis;
        var origin = xf.Origin;
        d[i] = basis.Row0.X; d[i + 1] = basis.Row0.Y; d[i + 2] = basis.Row0.Z; d[i + 3] = origin.X;
        d[i + 4] = basis.Row1.X; d[i + 5] = basis.Row1.Y; d[i + 6] = basis.Row1.Z; d[i + 7] = origin.Y;
        d[i + 8] = basis.Row2.X; d[i + 9] = basis.Row2.Y; d[i + 10] = basis.Row2.Z; d[i + 11] = origin.Z;
        d[i + 12] = color.R; d[i + 13] = color.G; d[i + 14] = color.B; d[i + 15] = color.A;
        d[i + 16] = uvOffset.X; d[i + 17] = uvOffset.Y; d[i + 18] = frame; d[i + 19] = blendWord;
        b.Depth[b.Count] = depth;
        if (b.Count == 0) { b.Min = origin; b.Max = origin; b.Extent = extent; }
        else
        {
            b.Min = new Vector3(Mathf.Min(b.Min.X, origin.X), Mathf.Min(b.Min.Y, origin.Y), Mathf.Min(b.Min.Z, origin.Z));
            b.Max = new Vector3(Mathf.Max(b.Max.X, origin.X), Mathf.Max(b.Max.Y, origin.Y), Mathf.Max(b.Max.Z, origin.Z));
            if (extent > b.Extent) b.Extent = extent;
        }
        b.Count++;
    }

    public override void _Process(double delta)
    {
        using var scope = Perf.Measure(Perf.Section.FxBatch);
        if (Fx.ShuttingDown) return;
        foreach (var (key, b) in _batches)
        {
            if (b.Count == 0)
            {
                if (b.Uploaded != 0) { b.Multimesh.VisibleInstanceCount = 0; b.Uploaded = 0; }
                if (b.Shown) { b.Shown = false; b.Instance.Visible = false; }
                if (++b.IdleFrames >= PruneIdleFrames) _prune.Add(key);
                continue;
            }
            b.IdleFrames = 0;
            if (!b.Shown) { b.Shown = true; b.Instance.Visible = true; }
            ShrinkIfOversized(b);
            if (b.SortByDepth) SortBackToFront(b);
            int capacity = b.Depth.Length;
            if (b.Multimesh.InstanceCount != capacity) b.Multimesh.InstanceCount = capacity;
            b.Multimesh.Buffer = b.Data;
            b.Multimesh.VisibleInstanceCount = b.Count;
            var pad = Vector3.One * b.Extent;
            b.Instance.CustomAabb = new Aabb(b.Min - pad, b.Max - b.Min + pad * 2f);
            b.Uploaded = b.Count;
            b.Count = 0;
        }
        if (_prune.Count == 0) return;
        foreach (var key in _prune)
        {
            var b = _batches[key];
            b.Pruned = true;
            b.Instance.QueueFree();
            _batches.Remove(key);
        }
        _prune.Clear();
    }

    private static void ShrinkIfOversized(Batch b)
    {
        int capacity = b.Depth.Length;
        if (capacity <= InitialCapacity || b.Count * ShrinkRatio > capacity) return;
        int shrunk = capacity / 2;
        System.Array.Resize(ref b.Data, shrunk * InstanceStride);
        System.Array.Resize(ref b.Sorted, shrunk * InstanceStride);
        System.Array.Resize(ref b.Depth, shrunk);
        System.Array.Resize(ref b.Order, shrunk);
    }

    private static void SortBackToFront(Batch b)
    {
        int n = b.Count;
        for (int i = 0; i < n; i++) b.Order[i] = i;
        System.Array.Sort(b.Depth, b.Order, 0, n);
        for (int k = 0; k < n; k++)
            System.Array.Copy(b.Data, b.Order[n - 1 - k] * InstanceStride, b.Sorted, k * InstanceStride, InstanceStride);
        (b.Data, b.Sorted) = (b.Sorted, b.Data);
    }

    internal int BatchCount => _batches.Count;

    internal static int LiveBatches
    {
        get
        {
            int n = 0;
            foreach (var batcher in _perViewport.Values)
                if (GodotObject.IsInstanceValid(batcher)) n += batcher._batches.Count;
            return n;
        }
    }
}
