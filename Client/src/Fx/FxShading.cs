using Godot;
using LibreKO.Domain;

namespace LibreKO;

internal static class FxShading
{
    internal const uint LayerBit = 1u << 17;

    internal const string SceneDepthParam = "fx_scene_depth";
    internal const string SceneViewProjectionParam = "fx_scene_view_projection";
    internal const string SceneDepthLiveParam = "fx_scene_depth_live";

    internal const int RfAlphaBlending = 0x001;

    private static bool _globalsReady;
    private static ImageTexture? _farDepth;

    internal static Texture2D FarDepth
    {
        get
        {
            EnsureGlobals();
            return _farDepth!;
        }
    }

    internal static void EnsureGlobals()
    {
        if (_globalsReady) return;
        _globalsReady = true;
        using var image = Image.CreateEmpty(1, 1, false, Image.Format.Rf);
        image.SetPixel(0, 0, new Color(float.MaxValue, 0f, 0f));
        _farDepth = ImageTexture.CreateFromImage(image);
        RenderingServer.GlobalShaderParameterAdd(SceneDepthParam, RenderingServer.GlobalShaderParameterType.Sampler2D, _farDepth);
        RenderingServer.GlobalShaderParameterAdd(SceneViewProjectionParam, RenderingServer.GlobalShaderParameterType.Mat4, new Projection(Transform3D.Identity));
        RenderingServer.GlobalShaderParameterAdd(SceneDepthLiveParam, RenderingServer.GlobalShaderParameterType.Bool, false);
    }

    internal static int Packed(Godot.Collections.Dictionary part, int layers = 1) =>
        FxBlendMath.Pack(Fx.SrcBlend(part), Fx.DestBlend(part), Blended(part), layers);

    internal static bool Blended(Godot.Collections.Dictionary part) =>
        (RenderFlags(part) & RfAlphaBlending) != 0;

    internal static int RenderFlags(Godot.Collections.Dictionary part) =>
        part.ContainsKey("renderFlags") ? part["renderFlags"].AsInt32()
            : RfAlphaBlending | Fx.RfNotZWrite | Fx.RfDoubleSided;

    internal static bool WritesDepth(int renderFlags) => (renderFlags & Fx.RfNotZWrite) == 0;

    internal static bool DoubleSided(int renderFlags) => (renderFlags & Fx.RfDoubleSided) != 0;

    internal static bool NoDepthTest(int renderFlags) => (renderFlags & Fx.RfNotZBuffer) != 0;

    internal static int Priority(int renderFlags) => WritesDepth(renderFlags) ? Fx.FxSiblingWinnerPriority : 0;

    internal static bool Commutative(int packed)
    {
        var (_, dest, blended, _) = FxBlendMath.Unpack(packed);
        return blended && dest == FxBlendMath.One;
    }

    internal static string RenderModes(bool doubleSided, bool noDepthTest, bool writesDepth = false) =>
        "unshaded, fog_disabled, shadows_disabled, blend_premul_alpha"
        + (writesDepth ? ", depth_draw_always" : ", depth_draw_never")
        + (doubleSided ? ", cull_disabled" : ", cull_back")
        + (noDepthTest ? ", depth_test_disabled" : "");

    internal const string ClipVertex =
        "    fx_clip = fx_scene_view_projection * (INV_VIEW_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)));\n";

    internal static string DepthTested(bool noDepthTest) => noDepthTest ? "false" : "true";

    internal static string Header()
    {
        EnsureGlobals();
        return $"global uniform sampler2D {SceneDepthParam};\n"
             + $"global uniform mat4 {SceneViewProjectionParam};\n"
             + $"global uniform bool {SceneDepthLiveParam};\n"
             + $"const uint FX_LAYER = {LayerBit}u;\n"
             + "varying vec4 fx_clip;\n"
             + Functions;
    }

    private const string Functions = """
float fx_weight(int f, float channel, float a) {
    if (f == 1) return 0.0;
    if (f == 2) return 1.0;
    if (f == 3) return channel;
    if (f == 4) return 1.0 - channel;
    if (f == 5) return a;
    if (f == 6) return 1.0 - a;
    return 1.0;
}

float fx_keep(int f, vec3 s, float a) {
    if (f == 1) return 0.0;
    if (f == 2) return 1.0;
    if (f == 3) return (s.r + s.g + s.b) / 3.0;
    if (f == 4) return 1.0 - (s.r + s.g + s.b) / 3.0;
    if (f == 5) return a;
    if (f == 6) return 1.0 - a;
    return 0.0;
}

bool fx_behind_scene(vec4 clip) {
    if (!fx_scene_depth_live || clip.w <= 0.0001) return false;
    vec2 uv = clip.xy / clip.w * 0.5 + 0.5;
    if (any(lessThan(uv, vec2(0.0))) || any(greaterThanEqual(uv, vec2(1.0)))) return false;
    float scene = texelFetch(fx_scene_depth, ivec2(uv * vec2(textureSize(fx_scene_depth, 0))), 0).r;
    return clip.w > scene * 1.002 + 0.02;
}

vec3 fx_srgb_to_linear(vec3 c) {
    c = clamp(c, 0.0, 1.0);
    return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045)));
}

vec4 fx_shade(vec4 texel, vec4 modulate, float packed_blend, vec4 clip, uint camera_layers, bool depth_tested) {
    int packed = int(packed_blend + 0.5);
    int src = packed & 7;
    int dst = (packed >> 3) & 7;
    bool blended = (packed & 64) == 0;
    float layers = float(max(1, (packed >> 7) & 15));
    vec3 s = texel.rgb * modulate.rgb;
    float a = texel.a * modulate.a;
    if (a * 255.0 < 0.5) return vec4(-1.0);
    bool fx_camera = (camera_layers & ~FX_LAYER) == 0u;
    if (fx_camera && depth_tested && fx_behind_scene(clip)) return vec4(-1.0);
    vec4 o;
    if (!blended) {
        o = vec4(s, 1.0);
    } else {
        vec3 col = s * vec3(fx_weight(src, s.r, a), fx_weight(src, s.g, a), fx_weight(src, s.b, a));
        float keep = clamp(fx_keep(dst, s, a), 0.0, 1.0);
        if (layers > 1.0) {
            float cover = 1.0 - keep;
            float keep_all = pow(keep, layers);
            col *= cover > 0.0001 ? (1.0 - keep_all) / cover : layers;
            keep = keep_all;
        }
        o = vec4(col, 1.0 - keep);
    }
    if (!fx_camera) o.rgb = fx_srgb_to_linear(o.rgb);
    return o;
}
""";

    private static readonly System.Collections.Generic.Dictionary<(bool, bool), Shader> SurfaceShaders = Shutdown.Track(new System.Collections.Generic.Dictionary<(bool, bool), Shader>());

    internal static ShaderMaterial SurfaceMaterial(int renderFlags, int blendWord, Texture? texture)
    {
        bool doubleSided = DoubleSided(renderFlags);
        bool noDepthTest = NoDepthTest(renderFlags);
        var key = (doubleSided, noDepthTest);
        if (!SurfaceShaders.TryGetValue(key, out var shader))
        {
            shader = new Shader
            {
                Code = "shader_type spatial;\n"
                     + $"render_mode {RenderModes(doubleSided, noDepthTest)};\n"
                     + "uniform sampler2D tex : filter_linear_mipmap, repeat_enable;\n"
                     + "uniform vec4 tint = vec4(1.0);\n"
                     + "uniform vec2 uv_scale = vec2(1.0);\n"
                     + "uniform vec2 uv_offset = vec2(0.0);\n"
                     + "uniform float fx_blend;\n"
                     + Header()
                     + "void vertex() {\n" + ClipVertex + "}\n"
                     + "void fragment() {\n"
                     + "    vec4 fx_out = fx_shade(texture(tex, UV * uv_scale + uv_offset), COLOR * tint, fx_blend, "
                     + $"fx_clip, CAMERA_VISIBLE_LAYERS, {DepthTested(noDepthTest)});\n"
                     + Apply()
                     + "}\n",
            };
            SurfaceShaders[key] = shader;
        }
        var material = new ShaderMaterial { Shader = shader, RenderPriority = Priority(renderFlags) };
        material.SetShaderParameter(BlendParam, (float)blendWord);
        if (texture != null) material.SetShaderParameter(TextureParam, texture);
        return material;
    }

    internal static readonly StringName BlendParam = "fx_blend";
    internal static readonly StringName TextureParam = "tex";
    internal static readonly StringName TintParam = "tint";
    internal static readonly StringName UvScaleParam = "uv_scale";
    internal static readonly StringName UvOffsetParam = "uv_offset";

    internal static string Apply(string fade = "1.0") =>
        "    if (fx_out.a < 0.0) discard;\n"
        + $"    float fx_fade = ALPHA * ({fade});\n"
        + "    ALBEDO = fx_out.rgb * fx_fade;\n"
        + "    ALPHA = fx_out.a * fx_fade;\n";
}
