using System.Collections.Generic;
using Godot;

namespace LibreKO;

internal static class FxMeshMaterial
{
    internal enum Variant { Blended, Opaque, LayerDepth }

    internal readonly record struct Look(
        ulong Sequence, int Frames, bool Loop, int BlendWord, bool DoubleSided, bool NoDepthTest,
        int Priority, Color Base);

    private static readonly Dictionary<(bool, bool, Variant), Shader> _shaders = Shutdown.Track(new Dictionary<(bool, bool, Variant), Shader>());
    private static readonly Dictionary<(Look, Variant), ShaderMaterial> _materials = Shutdown.Track(new Dictionary<(Look, Variant), ShaderMaterial>());
    private static Texture2DArray? _white;
    private static StringName? _fade, _uvOffset, _frame;

    internal static StringName FadeParam => _fade ??= new StringName("fade");
    internal static StringName UvOffsetParam => _uvOffset ??= new StringName("uv_offset");
    internal static StringName FrameParam => _frame ??= new StringName("frame_raw");

    internal static ShaderMaterial For(Look look, Texture2DArray? frames, Variant variant)
    {
        var key = (look, variant);
        if (_materials.TryGetValue(key, out var cached)) return cached;
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
        _materials[key] = material;
        return material;
    }

    private static Texture2DArray White()
    {
        if (_white != null) return _white;
        using var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        _white = new Texture2DArray();
        _white.CreateFromImages(new Godot.Collections.Array<Image> { image });
        return _white;
    }

    private static Shader ShaderFor(bool doubleSided, bool noDepthTest, Variant variant)
    {
        var key = (doubleSided, noDepthTest, variant);
        if (_shaders.TryGetValue(key, out var shader)) return shader;
        bool opaque = variant == Variant.Opaque;
        string modes = opaque
            ? "unshaded, fog_disabled, shadows_disabled, depth_draw_opaque"
              + (doubleSided ? ", cull_disabled" : ", cull_back")
              + (noDepthTest ? ", depth_test_disabled" : "")
            : FxShading.RenderModes(doubleSided, noDepthTest, variant == Variant.LayerDepth);
        shader = new Shader
        {
            Code = "shader_type spatial;\n"
                   + $"render_mode {modes};\n"
                   + "uniform sampler2DArray frames : filter_linear, repeat_enable;\n"
                   + "uniform vec4 base_color = vec4(1.0);\n"
                   + "uniform float frame_count = 1.0;\n"
                   + "uniform bool frame_loop = true;\n"
                   + "uniform float fx_blend;\n"
                   + "instance uniform float fade = 1.0;\n"
                   + "instance uniform vec2 uv_offset = vec2(0.0);\n"
                   + "instance uniform float frame_raw = 0.0;\n"
                   + FxShading.Header()
                   + "void vertex() {\n    UV += uv_offset;\n" + FxShading.ClipVertex + "}\n"
                   + "void fragment() {\n"
                   + "    float layer = frame_loop ? mod(frame_raw, frame_count) : min(frame_raw, frame_count - 1.0);\n"
                   + "    vec4 fx_out = fx_shade(texture(frames, vec3(UV, layer)), base_color * vec4(1.0, 1.0, 1.0, fade), fx_blend, "
                   + $"fx_clip, CAMERA_VISIBLE_LAYERS, {FxShading.DepthTested(noDepthTest)});\n"
                   + (opaque ? "    if (fx_out.a < 0.0) discard;\n    ALBEDO = fx_out.rgb;\n" : FxShading.Apply())
                   + "}\n",
        };
        _shaders[key] = shader;
        return shader;
    }
}
