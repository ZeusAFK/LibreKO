using Godot;

namespace LibreKO;

public partial class FxLayer : Node
{
    private const double IdleLingerSeconds = 2.0;
    private const int CompositeCanvasLayer = -1;

    internal static int Users;
    private static Viewport? drawingHost;

    internal static bool DrawsEffectsIn(Viewport? viewport) => viewport != null && ReferenceEquals(viewport, drawingHost);

    private Viewport host = null!;
    private Camera3D? hostCamera;
    private SubViewport view = null!;
    private Camera3D camera = null!;
    private CanvasLayer canvas = null!;
    private TextureRect composite = null!;
    private readonly FxSceneDepth depth = new();
    private Vector2I requestedSize;
    private double lastWanted = double.NegativeInfinity;
    private bool rendering;

    internal static bool Supported =>
        DisplayServer.GetName() != "headless" && RenderingServer.GetRenderingDevice() != null
        && RenderingServer.GetCurrentRenderingMethod() == ForwardPlus;

    private const string ForwardPlus = "forward_plus";

    public static FxLayer? Attach(Viewport host)
    {
        if (!Supported) return null;
        var layer = new FxLayer { Name = "FxLayer", host = host };
        host.CallDeferred(Node.MethodName.AddChild, layer);
        return layer;
    }

    public override void _Ready()
    {
        FxShading.EnsureGlobals();
        view = new SubViewport
        {
            Name = "FxView",
            TransparentBg = true,
            Msaa3D = Viewport.Msaa.Disabled,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
            UseTaa = false,
            UseDebanding = false,
            UseOcclusionCulling = false,
            PositionalShadowAtlasSize = 0,
            HandleInputLocally = false,
            GuiDisableInput = true,
            AudioListenerEnable3D = false,
            PhysicsObjectPicking = false,
            Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            Size = HostSize(),
        };
        AddChild(view);
        camera = new Camera3D
        {
            Name = "FxCamera",
            CullMask = FxShading.LayerBit,
            Environment = PlainEnvironment(),
            Current = true,
        };
        view.AddChild(camera);

        canvas = new CanvasLayer { Name = "FxComposite", Layer = CompositeCanvasLayer };
        AddChild(canvas);
        var material = Shaders.Material("fx_layer_composite");
        material.SetShaderParameter("layer", view.GetTexture());
        composite = new TextureRect
        {
            Texture = view.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = material,
            Visible = false,
        };
        composite.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(composite);
        depth.Enabled = false;

        RenderingServer.FramePreDraw += Sync;
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePreDraw -= Sync;
        Unbind();
        this.depth.Enabled = false;
        var depth = this.depth;
        RenderingServer.CallOnRenderThread(Callable.From(() => depth.Release()));
    }

    private static Godot.Environment PlainEnvironment() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = new Color(0f, 0f, 0f, 0f),
        AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
        ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
        TonemapMode = Godot.Environment.ToneMapper.Linear,
        TonemapExposure = 1f,
        GlowEnabled = false,
        FogEnabled = false,
        VolumetricFogEnabled = false,
        SsaoEnabled = false,
        SsilEnabled = false,
        SsrEnabled = false,
        SdfgiEnabled = false,
        AdjustmentEnabled = false,
    };

    private Vector2I HostSize() => host switch
    {
        Window window => window.Size,
        SubViewport sub => sub.Size,
        _ => Vector2I.One,
    };

    private void Sync()
    {
        if (!GodotObject.IsInstanceValid(host)) return;
        var camera = host.GetCamera3D();
        if (camera != hostCamera)
        {
            Unbind();
            if (camera != null && Config.FxLayer) Bind(camera);
        }
        if (!Config.FxLayer)
        {
            if (hostCamera != null) Unbind();
            SetRendering(false);
            return;
        }
        if (hostCamera == null) { SetRendering(false); return; }

        double now = FrameClock.Now();
        if (Users > 0) lastWanted = now;
        bool wanted = now - lastWanted < IdleLingerSeconds;

        var target = depth.TargetWanted;
        if (target is { X: > 0, Y: > 0 } && target != requestedSize)
        {
            requestedSize = target;
            var depth = this.depth;
            RenderingServer.CallOnRenderThread(Callable.From(() => depth.Resize(target)));
        }

        SetRendering(wanted);
        if (!wanted) return;
        var size = HostSize();
        if (size.X > 0 && size.Y > 0 && view.Size != size) view.Size = size;
        if (!Mathf.IsEqualApprox(view.Scaling3DScale, host.Scaling3DScale)) view.Scaling3DScale = host.Scaling3DScale;
        this.camera.GlobalTransform = hostCamera.GlobalTransform;
        this.camera.Projection = hostCamera.Projection;
        this.camera.Fov = hostCamera.Fov;
        this.camera.Size = hostCamera.Size;
        this.camera.Near = hostCamera.Near;
        this.camera.Far = hostCamera.Far;
        this.camera.KeepAspect = hostCamera.KeepAspect;
        this.camera.HOffset = hostCamera.HOffset;
        this.camera.VOffset = hostCamera.VOffset;
        this.camera.FrustumOffset = hostCamera.FrustumOffset;
    }

    private void SetRendering(bool on)
    {
        if (rendering == on) return;
        rendering = on;
        view.RenderTargetUpdateMode = on ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        composite.Visible = on;
        depth.Enabled = on;
        if (!on) RenderingServer.GlobalShaderParameterSet(FxShading.SceneDepthLiveParam, false);
    }

    private void Bind(Camera3D camera)
    {
        hostCamera = camera;
        drawingHost = host;
        camera.CullMask &= ~FxShading.LayerBit;
        var compositor = camera.Compositor ?? new Compositor();
        var effects = compositor.CompositorEffects;
        if (!effects.Contains(depth)) effects.Add(depth);
        compositor.CompositorEffects = effects;
        camera.Compositor = compositor;
    }

    private void Unbind()
    {
        var camera = hostCamera;
        hostCamera = null;
        drawingHost = null;
        RenderingServer.GlobalShaderParameterSet(FxShading.SceneDepthLiveParam, false);
        if (camera == null || !GodotObject.IsInstanceValid(camera)) return;
        camera.CullMask |= FxShading.LayerBit;
        if (camera.Compositor is { } compositor)
        {
            var effects = compositor.CompositorEffects;
            effects.Remove(depth);
            compositor.CompositorEffects = effects;
        }
    }
}

public partial class FxSceneDepth : CompositorEffect
{
    internal Vector2I TargetWanted;

    private Rid target;
    private Vector2I targetSize;
    private Texture2Drd? targetTexture;
    private Rid shader, pipeline, sampler;
    private bool broken;

    public FxSceneDepth()
    {
        EffectCallbackType = EffectCallbackTypeEnum.PostOpaque;
        AccessResolvedDepth = true;
    }

    internal void Resize(Vector2I size)
    {
        var rd = RenderingServer.GetRenderingDevice();
        if (rd == null) return;
        var rid = rd.TextureCreate(new RDTextureFormat
        {
            Format = RenderingDevice.DataFormat.R32Sfloat,
            Width = (uint)size.X,
            Height = (uint)size.Y,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit,
        }, new RDTextureView());
        if (!rid.IsValid) return;
        var old = target;
        target = rid;
        targetSize = size;
        targetTexture ??= new Texture2Drd();
        targetTexture.TextureRdRid = rid;
        RenderingServer.GlobalShaderParameterSet(FxShading.SceneDepthLiveParam, false);
        RenderingServer.GlobalShaderParameterSet(FxShading.SceneDepthParam, targetTexture);
        if (old.IsValid) rd.FreeRid(old);
    }

    internal void Release()
    {
        RenderingServer.GlobalShaderParameterSet(FxShading.SceneDepthLiveParam, false);
        RenderingServer.GlobalShaderParameterSet(FxShading.SceneDepthParam, FxShading.FarDepth);
        var rd = RenderingServer.GetRenderingDevice();
        if (rd == null) return;
        if (targetTexture != null) targetTexture.TextureRdRid = default;
        if (target.IsValid) rd.FreeRid(target);
        if (sampler.IsValid) rd.FreeRid(sampler);
        if (shader.IsValid) rd.FreeRid(shader);
        target = sampler = shader = pipeline = default;
        targetSize = Vector2I.Zero;
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        if (effectCallbackType != (int)EffectCallbackTypeEnum.PostOpaque) return;
        var rd = RenderingServer.GetRenderingDevice();
        if (rd == null) return;
        using var sceneBuffers = renderData.GetRenderSceneBuffers();
        if (sceneBuffers is not RenderSceneBuffersRD buffers) return;
        var size = buffers.GetInternalSize();
        TargetWanted = size;
        if (!target.IsValid || targetSize != size || size.X <= 0 || size.Y <= 0 || !Prepare(rd)) return;

        var scene = renderData.GetRenderSceneData();
        var projection = scene.GetCamProjection();
        var view = new Projection(scene.GetCamTransform().AffineInverse());

        var depthUniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 };
        depthUniform.AddId(sampler);
        depthUniform.AddId(buffers.GetDepthLayer(0, false));
        var targetUniform = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
        targetUniform.AddId(target);
        var set = UniformSetCacheRD.GetCache(shader, 0, new Godot.Collections.Array<RDUniform> { depthUniform, targetUniform });

        var inverse = projection.Inverse();
        var push = new float[20];
        for (int c = 0; c < 4; c++)
        {
            var column = inverse[c];
            push[c * 4] = column.X;
            push[c * 4 + 1] = column.Y;
            push[c * 4 + 2] = column.Z;
            push[c * 4 + 3] = column.W;
        }
        push[16] = size.X;
        push[17] = size.Y;
        var bytes = new byte[push.Length * sizeof(float)];
        System.Buffer.BlockCopy(push, 0, bytes, 0, bytes.Length);

        long list = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(list, pipeline);
        rd.ComputeListBindUniformSet(list, set, 0);
        rd.ComputeListSetPushConstant(list, bytes, (uint)bytes.Length);
        rd.ComputeListDispatch(list, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
        rd.ComputeListEnd();

        RenderingServer.GlobalShaderParameterSet(FxShading.SceneViewProjectionParam, projection * view);
        RenderingServer.GlobalShaderParameterSet(FxShading.SceneDepthLiveParam, true);
    }

    private bool Prepare(RenderingDevice rd)
    {
        if (pipeline.IsValid) return true;
        if (broken) return false;
        var spirv = rd.ShaderCompileSpirVFromSource(new RDShaderSource
        {
            Language = RenderingDevice.ShaderLanguage.Glsl,
            SourceCompute = Godot.FileAccess.GetFileAsString(ComputePath),
        });
        string error = spirv.GetStageCompileError(RenderingDevice.ShaderStage.Compute);
        if (!string.IsNullOrEmpty(error))
        {
            GD.PushWarning($"[fx] scene depth copy shader failed: {error}");
            broken = true;
            return false;
        }
        shader = rd.ShaderCreateFromSpirV(spirv);
        pipeline = rd.ComputePipelineCreate(shader);
        sampler = rd.SamplerCreate(new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Nearest,
            MagFilter = RenderingDevice.SamplerFilter.Nearest,
        });
        return pipeline.IsValid;
    }

    private const string ComputePath = "res://shaders/fx_scene_depth.comp";
}
