using System.Collections.Generic;
using Godot;

namespace LibreKO;

internal static class FxMeshMaterial
{
    internal enum Blend { Mix, Add, Premultiplied }

    internal readonly record struct Look(
        ulong Sequence, int Frames, bool Loop, Blend Blending, bool DoubleSided, bool NoDepthTest,
        bool WritesDepth, int Priority, Color Base);

    private static readonly Dictionary<(Blend, bool, bool, bool, bool), Shader> _shaders = new();
    private static readonly Dictionary<(Look, bool Opaque), ShaderMaterial> _materials = new();
    private static Texture2DArray? _white;
    private static StringName? _fade, _uvOffset, _frame;

    internal static StringName FadeParam => _fade ??= new StringName("fade");
    internal static StringName UvOffsetParam => _uvOffset ??= new StringName("uv_offset");
    internal static StringName FrameParam => _frame ??= new StringName("frame_raw");

    internal static ShaderMaterial For(Look look, Texture2DArray? frames, bool opaque)
    {
        var key = (look, opaque);
        if (_materials.TryGetValue(key, out var cached)) return cached;
        var material = new ShaderMaterial
        {
            Shader = ShaderFor(look.Blending, look.DoubleSided, look.NoDepthTest, opaque, look.WritesDepth),
            RenderPriority = look.Priority,
        };
        material.SetShaderParameter("frames", frames ?? White());
        material.SetShaderParameter("base_color", look.Base);
        material.SetShaderParameter("frame_count", (float)Mathf.Max(1, look.Frames));
        material.SetShaderParameter("frame_loop", look.Loop);
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

    private static Shader ShaderFor(Blend blend, bool doubleSided, bool noDepthTest, bool opaque, bool writesDepth)
    {
        var key = (blend, doubleSided, noDepthTest, opaque, writesDepth);
        if (_shaders.TryGetValue(key, out var shader)) return shader;
        string blendMode = opaque ? "blend_mix" : blend switch
        {
            Blend.Add => "blend_add",
            Blend.Premultiplied => "blend_premul_alpha",
            _ => "blend_mix",
        };
        string modes = "unshaded, " + blendMode
                       + (opaque && writesDepth ? ", depth_draw_opaque" : ", depth_draw_never")
                       + (doubleSided ? ", cull_disabled" : ", cull_back")
                       + (noDepthTest ? ", depth_test_disabled" : "")
                       + (blend != Blend.Mix ? ", fog_disabled" : "");
        shader = new Shader
        {
            Code = "shader_type spatial;\n"
                   + $"render_mode {modes};\n"
                   + "uniform sampler2DArray frames : source_color, filter_linear, repeat_enable;\n"
                   + "uniform vec4 base_color : source_color = vec4(1.0);\n"
                   + "uniform float frame_count = 1.0;\n"
                   + "uniform bool frame_loop = true;\n"
                   + "instance uniform float fade = 1.0;\n"
                   + "instance uniform vec2 uv_offset = vec2(0.0);\n"
                   + "instance uniform float frame_raw = 0.0;\n"
                   + "void vertex() { UV += uv_offset; }\n"
                   + "void fragment() {\n"
                   + "    float layer = frame_loop ? mod(frame_raw, frame_count) : min(frame_raw, frame_count - 1.0);\n"
                   + "    vec4 tex = texture(frames, vec3(UV, layer));\n"
                   + "    ALBEDO = base_color.rgb * tex.rgb;\n"
                   + (opaque ? "" : "    ALPHA = base_color.a * tex.a * fade;\n")
                   + "}\n",
        };
        _shaders[key] = shader;
        return shader;
    }
}
