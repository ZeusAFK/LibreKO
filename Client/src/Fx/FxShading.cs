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

    private static bool globalsReady;
    private static ImageTexture? farDepth;

    internal static Texture2D FarDepth
    {
        get
        {
            EnsureGlobals();
            return farDepth!;
        }
    }

    internal static void EnsureGlobals()
    {
        if (globalsReady) return;
        globalsReady = true;
        using var image = Image.CreateEmpty(1, 1, false, Image.Format.Rf);
        image.SetPixel(0, 0, new Color(float.MaxValue, 0f, 0f));
        farDepth = ImageTexture.CreateFromImage(image);
        RenderingServer.GlobalShaderParameterSet(SceneDepthParam, farDepth);
    }

    internal static Shader VariantShader(string include, bool doubleSided, bool noDepthTest, string? define = null)
    {
        EnsureGlobals();
        var code = new System.Text.StringBuilder("shader_type spatial;\n");
        if (doubleSided) code.Append("#define FX_DOUBLE_SIDED\n");
        if (noDepthTest) code.Append("#define FX_NO_DEPTH_TEST\n");
        if (define != null) code.Append("#define ").Append(define).Append('\n');
        code.Append("#include \"res://shaders/").Append(include).Append(".gdshaderinc\"\n");
        return new Shader { Code = code.ToString() };
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

    private static readonly System.Collections.Generic.Dictionary<(bool, bool), Shader> SurfaceShaders = Shutdown.Track(new System.Collections.Generic.Dictionary<(bool, bool), Shader>());

    internal static ShaderMaterial SurfaceMaterial(int renderFlags, int blendWord, Texture? texture)
    {
        bool doubleSided = DoubleSided(renderFlags);
        bool noDepthTest = NoDepthTest(renderFlags);
        var key = (doubleSided, noDepthTest);
        if (!SurfaceShaders.TryGetValue(key, out var shader))
        {
            shader = VariantShader("fx_surface", doubleSided, noDepthTest);
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
}
