using System.Collections.Generic;
using Godot;

namespace LibreKO;

internal static class FxMeshMaterial
{
    internal enum Variant { Blended, Opaque, LayerDepth }

    internal readonly record struct Look(
        ulong Sequence, int Frames, bool Loop, int BlendWord, bool DoubleSided, bool NoDepthTest,
        int Priority, Color Base);

    private static readonly Dictionary<(bool, bool, Variant), Shader> shaders = Shutdown.Track(new Dictionary<(bool, bool, Variant), Shader>());
    private static readonly Dictionary<(Look, Variant), ShaderMaterial> materials = Shutdown.Track(new Dictionary<(Look, Variant), ShaderMaterial>());
    private static Texture2DArray? white;
    private static StringName? fade, uvOffset, frame;

    internal static StringName FadeParam => fade ??= new StringName("fade");
    internal static StringName UvOffsetParam => uvOffset ??= new StringName("uv_offset");
    internal static StringName FrameParam => frame ??= new StringName("frame_raw");

    internal static ShaderMaterial For(Look look, Texture2DArray? frames, Variant variant)
    {
        var key = (look, variant);
        if (materials.TryGetValue(key, out var cached)) return cached;
        var material = new ShaderMaterial
        {
            Shader = ShaderFor(look.DoubleSided, look.NoDepthTest, variant),
            RenderPriority = look.Priority,
        };
        material.SetShaderParameter("frames", frames ?? White());
        material.SetShaderParameter("base_color", look.Base);
        material.SetShaderParameter("frame_count", (float)Mathf.Max(1, look.Frames));
        material.SetShaderParameter("frame_loop", look.Loop);
        material.SetShaderParameter(FxShading.BlendParam, (float)look.BlendWord);
        materials[key] = material;
        return material;
    }

    private static Texture2DArray White()
    {
        if (white != null) return white;
        using var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        white = new Texture2DArray();
        white.CreateFromImages(new Godot.Collections.Array<Image> { image });
        return white;
    }

    private static Shader ShaderFor(bool doubleSided, bool noDepthTest, Variant variant)
    {
        var key = (doubleSided, noDepthTest, variant);
        if (shaders.TryGetValue(key, out var shader)) return shader;
        shader = FxShading.VariantShader("fx_mesh", doubleSided, noDepthTest, variant switch
        {
            Variant.Opaque => "FX_OPAQUE",
            Variant.LayerDepth => "FX_WRITES_DEPTH",
            _ => null,
        });
        shaders[key] = shader;
        return shader;
    }
}
