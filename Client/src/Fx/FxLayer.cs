using Godot;

namespace LibreKO;

public partial class FxLayer : Node
{
    private const double IdleLingerSeconds = 2.0;
    private const int CompositeCanvasLayer = -1;

    internal static int Users;
    private static Viewport? _drawingHost;

    internal static bool DrawsEffectsIn(Viewport? viewport) => viewport != null && ReferenceEquals(viewport, _drawingHost);

    private Viewport _host = null!;
    private Camera3D? _hostCamera;
    private SubViewport _view = null!;
    private Camera3D _camera = null!;
    private CanvasLayer _canvas = null!;
    private TextureRect _composite = null!;
    private readonly FxSceneDepth _depth = new();
    private Vector2I _requestedSize;
    private double _lastWanted = double.NegativeInfinity;
    private bool _rendering;

    internal static bool Supported =>
        DisplayServer.GetName() != "headless" && RenderingServer.GetRenderingDevice() != null
        && RenderingServer.GetCurrentRenderingMethod() == ForwardPlus;

    private const string ForwardPlus = "forward_plus";

    public static FxLayer? Attach(Viewport host)
    {
        if (!Supported) return null;
        var layer = new FxLayer { Name = "FxLayer", _host = host };
        host.CallDeferred(Node.MethodName.AddChild, layer);
        return layer;
    }

    public override void _Ready()
    {
        FxShading.EnsureGlobals();
        _view = new SubViewport
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
        AddChild(_view);
        _camera = new Camera3D
        {
            Name = "FxCamera",
            CullMask = FxShading.LayerBit,
            Environment = PlainEnvironment(),
            Current = true,
        };
        _view.AddChild(_camera);

        _canvas = new CanvasLayer { Name = "FxComposite", Layer = CompositeCanvasLayer };
        AddChild(_canvas);
        var material = new ShaderMaterial { Shader = new Shader { Code = CompositeCode } };
        material.SetShaderParameter("layer", _view.GetTexture());
        _composite = new TextureRect
        {
            Texture = _view.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = material,
            Visible = false,
        };
        _composite.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _canvas.AddChild(_composite);
        _depth.Enabled = false;

        RenderingServer.FramePreDraw += Sync;
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePreDraw -= Sync;
        Unbind();
        _depth.Enabled = false;
        var depth = _depth;
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

    private Vector2I HostSize() => _host switch
    {
        Window window => window.Size,
        SubViewport sub => sub.Size,
        _ => Vector2I.One,
    };

    private void Sync()
    {
        if (!GodotObject.IsInstanceValid(_host)) return;
        var camera = _host.GetCamera3D();
        if (camera != _hostCamera)
        {
            Unbind();
            if (camera != null && Config.FxLayer) Bind(camera);
        }
        if (!Config.FxLayer)
        {
            if (_hostCamera != null) Unbind();
            SetRendering(false);
            return;
        }
        if (_hostCamera == null) { SetRendering(false); return; }

        double now = Time.GetTicksMsec() / 1000.0;
        if (Users > 0) _lastWanted = now;
        bool wanted = now - _lastWanted < IdleLingerSeconds;

        var target = _depth.TargetWanted;
        if (target is { X: > 0, Y: > 0 } && target != _requestedSize)
        {
            _requestedSize = target;
            var depth = _depth;
            RenderingServer.CallOnRenderThread(Callable.From(() => depth.Resize(target)));
        }

        SetRendering(wanted);
        if (!wanted) return;
        var size = HostSize();
        if (size.X > 0 && size.Y > 0 && _view.Size != size) _view.Size = size;
        if (!Mathf.IsEqualApprox(_view.Scaling3DScale, _host.Scaling3DScale)) _view.Scaling3DScale = _host.Scaling3DScale;
        _camera.GlobalTransform = _hostCamera.GlobalTransform;
        _camera.Projection = _hostCamera.Projection;
        _camera.Fov = _hostCamera.Fov;
        _camera.Size = _hostCamera.Size;
        _camera.Near = _hostCamera.Near;
        _camera.Far = _hostCamera.Far;
        _camera.KeepAspect = _hostCamera.KeepAspect;
        _camera.HOffset = _hostCamera.HOffset;
        _camera.VOffset = _hostCamera.VOffset;
        _camera.FrustumOffset = _hostCamera.FrustumOffset;
    }

    private void SetRendering(bool on)
    {
        if (_rendering == on) return;
        _rendering = on;
        _view.RenderTargetUpdateMode = on ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        _composite.Visible = on;
        _depth.Enabled = on;
        if (!on) RenderingServer.GlobalShaderParameterSet(FxShading.SceneDepthLiveParam, false);
    }

    private void Bind(Camera3D camera)
    {
        _hostCamera = camera;
        _drawingHost = _host;
        camera.CullMask &= ~FxShading.LayerBit;
        var compositor = camera.Compositor ?? new Compositor();
        var effects = compositor.CompositorEffects;
        if (!effects.Contains(_depth)) effects.Add(_depth);
        compositor.CompositorEffects = effects;
        camera.Compositor = compositor;
    }

    private void Unbind()
    {
        var camera = _hostCamera;
        _hostCamera = null;
        _drawingHost = null;
        RenderingServer.GlobalShaderParameterSet(FxShading.SceneDepthLiveParam, false);
        if (camera == null || !GodotObject.IsInstanceValid(camera)) return;
        camera.CullMask |= FxShading.LayerBit;
        if (camera.Compositor is { } compositor)
        {
            var effects = compositor.CompositorEffects;
            effects.Remove(_depth);
            compositor.CompositorEffects = effects;
        }
    }

    private const string CompositeCode = """
shader_type canvas_item;
render_mode blend_premul_alpha, unshaded;
uniform sampler2D layer : filter_nearest;

void fragment() {
    vec4 c = texture(layer, UV);
    vec3 raw = mix(pow((c.rgb + 0.055) / 1.055, vec3(2.4)), c.rgb / 12.92, lessThan(c.rgb, vec3(0.04045)));
    COLOR = vec4(raw, c.a);
}
""";
}

public partial class FxSceneDepth : CompositorEffect
{
    internal Vector2I TargetWanted;

    private Rid _target;
    private Vector2I _targetSize;
    private Texture2Drd? _targetTexture;
    private Rid _shader, _pipeline, _sampler;
    private bool _broken;

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
        var old = _target;
        _target = rid;
        _targetSize = size;
        _targetTexture ??= new Texture2Drd();
        _targetTexture.TextureRdRid = rid;
        RenderingServer.GlobalShaderParameterSet(FxShading.SceneDepthLiveParam, false);
        RenderingServer.GlobalShaderParameterSet(FxShading.SceneDepthParam, _targetTexture);
        if (old.IsValid) rd.FreeRid(old);
    }

    internal void Release()
    {
        RenderingServer.GlobalShaderParameterSet(FxShading.SceneDepthLiveParam, false);
        RenderingServer.GlobalShaderParameterSet(FxShading.SceneDepthParam, FxShading.FarDepth);
        var rd = RenderingServer.GetRenderingDevice();
        if (rd == null) return;
        if (_targetTexture != null) _targetTexture.TextureRdRid = default;
        if (_target.IsValid) rd.FreeRid(_target);
        if (_sampler.IsValid) rd.FreeRid(_sampler);
        if (_shader.IsValid) rd.FreeRid(_shader);
        _target = _sampler = _shader = _pipeline = default;
        _targetSize = Vector2I.Zero;
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
        if (!_target.IsValid || _targetSize != size || size.X <= 0 || size.Y <= 0 || !Prepare(rd)) return;

        var scene = renderData.GetRenderSceneData();
        var projection = scene.GetCamProjection();
        var view = new Projection(scene.GetCamTransform().AffineInverse());

        var depthUniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 };
        depthUniform.AddId(_sampler);
        depthUniform.AddId(buffers.GetDepthLayer(0, false));
        var targetUniform = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
        targetUniform.AddId(_target);
        var set = UniformSetCacheRD.GetCache(_shader, 0, new Godot.Collections.Array<RDUniform> { depthUniform, targetUniform });

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
        rd.ComputeListBindComputePipeline(list, _pipeline);
        rd.ComputeListBindUniformSet(list, set, 0);
        rd.ComputeListSetPushConstant(list, bytes, (uint)bytes.Length);
        rd.ComputeListDispatch(list, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
        rd.ComputeListEnd();

        RenderingServer.GlobalShaderParameterSet(FxShading.SceneViewProjectionParam, projection * view);
        RenderingServer.GlobalShaderParameterSet(FxShading.SceneDepthLiveParam, true);
    }

    private bool Prepare(RenderingDevice rd)
    {
        if (_pipeline.IsValid) return true;
        if (_broken) return false;
        var spirv = rd.ShaderCompileSpirVFromSource(new RDShaderSource
        {
            Language = RenderingDevice.ShaderLanguage.Glsl,
            SourceCompute = ComputeCode,
        });
        string error = spirv.GetStageCompileError(RenderingDevice.ShaderStage.Compute);
        if (!string.IsNullOrEmpty(error))
        {
            GD.PushWarning($"[fx] scene depth copy shader failed: {error}");
            _broken = true;
            return false;
        }
        _shader = rd.ShaderCreateFromSpirV(spirv);
        _pipeline = rd.ComputePipelineCreate(_shader);
        _sampler = rd.SamplerCreate(new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Nearest,
            MagFilter = RenderingDevice.SamplerFilter.Nearest,
        });
        return _pipeline.IsValid;
    }

    private const string ComputeCode = """
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
layout(set = 0, binding = 0) uniform sampler2D depth_buffer;
layout(r32f, set = 0, binding = 1) uniform restrict writeonly image2D view_distance;
layout(push_constant, std430) uniform Params {
    mat4 inverse_projection;
    vec2 size;
    vec2 unused;
} params;

void main() {
    ivec2 texel = ivec2(gl_GlobalInvocationID.xy);
    if (texel.x >= int(params.size.x) || texel.y >= int(params.size.y)) return;
    float depth = texelFetch(depth_buffer, texel, 0).r;
    vec2 ndc = (vec2(texel) + 0.5) / params.size * 2.0 - 1.0;
    vec4 view = params.inverse_projection * vec4(ndc, depth, 1.0);
    imageStore(view_distance, texel, vec4(-view.z / view.w, 0.0, 0.0, 0.0));
}
""";
}
