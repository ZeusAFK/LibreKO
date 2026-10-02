using System.Collections.Generic;
using System.Text;
using Godot;

namespace LibreKO;

internal static class OutfitMaterial
{
    internal const int MaxSlots = 8;

    internal readonly record struct Slot(
        Texture2D? Texture, Color Color, float Roughness, float Metallic, float Specular, float Scissor,
        int ShineLevel, int ShinePart);

    private static readonly Dictionary<string, ShaderMaterial> _materials = Shutdown.Track(new Dictionary<string, ShaderMaterial>());
    private static Shader? _shader, _shineShader;
    private static ImageTexture? _white;

    internal static bool TryDescribe(Material? material, int shineLevel, int shinePart, out Slot slot)
    {
        slot = default;
        if (material is not StandardMaterial3D m) return false;
        if (m.CullMode != BaseMaterial3D.CullModeEnum.Disabled) return false;
        if (m.ShadingMode != BaseMaterial3D.ShadingModeEnum.PerPixel) return false;
        if (m.BillboardMode != BaseMaterial3D.BillboardModeEnum.Disabled) return false;
        if (m.Transparency is not (BaseMaterial3D.TransparencyEnum.Disabled or BaseMaterial3D.TransparencyEnum.AlphaScissor)) return false;
        if (m.TextureFilter != BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps || !m.TextureRepeat) return false;
        if (m.Uv1Scale != Vector3.One || m.Uv1Offset != Vector3.Zero || m.Uv1Triplanar) return false;
        if (m.NormalEnabled || m.VertexColorUseAsAlbedo || m.DetailEnabled || m.RimEnabled
            || m.ClearcoatEnabled || m.AOEnabled || m.HeightmapEnabled || m.SubsurfScatterEnabled
            || m.BacklightEnabled || m.RefractionEnabled || m.AnisotropyEnabled) return false;
        if (m.RoughnessTexture != null || m.MetallicTexture != null) return false;
        if (m.EmissionEnabled && (m.EmissionTexture != null || m.Emission.R + m.Emission.G + m.Emission.B > 0f)) return false;
        if (m.NextPass != null && shineLevel <= 0) return false;
        slot = new Slot(m.AlbedoTexture, m.AlbedoColor, m.Roughness, m.Metallic, m.MetallicSpecular,
            m.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor ? m.AlphaScissorThreshold : 0f,
            shineLevel, shinePart);
        return true;
    }

    internal static ShaderMaterial For(IReadOnlyList<Slot> slots)
    {
        var key = new StringBuilder();
        foreach (var s in slots)
            key.Append(s.Texture?.GetRid().Id ?? 0).Append('/').Append(s.Color.ToHtml()).Append('/')
               .Append(s.Roughness).Append('/').Append(s.Metallic).Append('/').Append(s.Specular).Append('/')
               .Append(s.Scissor).Append('/').Append(s.ShineLevel).Append('/').Append(s.ShinePart).Append(';');
        string id = key.ToString();
        if (_materials.TryGetValue(id, out var cached)) return cached;

        var colors = new Vector4[MaxSlots];
        var surfaces = new Vector4[MaxSlots];
        var shineA = new Vector4[MaxSlots];
        var shineB = new Vector4[MaxSlots];
        var material = new ShaderMaterial { Shader = _shader ??= new Shader { Code = BodyCode() } };
        ShaderMaterial? shine = null;
        for (int i = 0; i < MaxSlots; i++)
        {
            var s = i < slots.Count ? slots[i] : default;
            var linear = s.Color.SrgbToLinear();
            colors[i] = new Vector4(linear.R, linear.G, linear.B, s.Color.A);
            surfaces[i] = new Vector4(s.Roughness, s.Metallic, s.Specular, s.Scissor);
            material.SetShaderParameter($"slot_texture_{i}", s.Texture ?? White());
            if (i < slots.Count && s.ShineLevel > 0)
            {
                ItemShine.PackShine(s.ShineLevel, s.ShinePart, out shineA[i], out shineB[i]);
                shineB[i].Z = s.Texture != null && s.Scissor > 0f ? 1f : 0f;
                shineB[i].W = s.Scissor > 0f ? s.Scissor : ItemShine.DefaultAlphaCutoff;
                shine ??= new ShaderMaterial { Shader = _shineShader ??= new Shader { Code = ShineCode() } };
            }
        }
        material.SetShaderParameter("slot_color", colors);
        material.SetShaderParameter("slot_surface", surfaces);
        if (shine != null)
        {
            for (int i = 0; i < MaxSlots; i++)
                shine.SetShaderParameter($"slot_texture_{i}", (i < slots.Count ? slots[i].Texture : null) ?? White());
            shine.SetShaderParameter("slot_color", colors);
            shine.SetShaderParameter("slot_shine_a", shineA);
            shine.SetShaderParameter("slot_shine_b", shineB);
            ItemShine.ApplyTiming(shine);
            material.NextPass = shine;
        }
        _materials[id] = material;
        return material;
    }

    private static ImageTexture White()
    {
        if (_white != null) return _white;
        using var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        return _white = ImageTexture.CreateFromImage(image);
    }

    private static string SlotDeclarations()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < MaxSlots; i++)
            sb.Append($"uniform sampler2D slot_texture_{i} : source_color, filter_linear_mipmap, repeat_enable;\n");
        sb.Append($"uniform vec4 slot_color[{MaxSlots}];\n");
        sb.Append("varying flat int slot;\n");
        sb.Append("vec4 slot_sample(vec2 uv, vec2 ddx, vec2 ddy) {\n");
        for (int i = 0; i < MaxSlots - 1; i++)
            sb.Append($"    if (slot == {i}) return textureGrad(slot_texture_{i}, uv, ddx, ddy);\n");
        sb.Append($"    return textureGrad(slot_texture_{MaxSlots - 1}, uv, ddx, ddy);\n}}\n");
        sb.Append("vec2 slot_size() {\n");
        for (int i = 0; i < MaxSlots - 1; i++)
            sb.Append($"    if (slot == {i}) return vec2(textureSize(slot_texture_{i}, 0));\n");
        sb.Append($"    return vec2(textureSize(slot_texture_{MaxSlots - 1}, 0));\n}}\n");
        sb.Append("float slot_alpha_lod(vec2 uv, float lod) {\n");
        for (int i = 0; i < MaxSlots - 1; i++)
            sb.Append($"    if (slot == {i}) return textureLod(slot_texture_{i}, uv, lod).a;\n");
        sb.Append($"    return textureLod(slot_texture_{MaxSlots - 1}, uv, lod).a;\n}}\n");
        return sb.ToString();
    }

    private static string BodyCode() =>
        "shader_type spatial;\n"
        + "render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;\n"
        + SlotDeclarations()
        + $"uniform vec4 slot_surface[{MaxSlots}];\n"
        + "void vertex() { slot = int(UV2.x + 0.5); }\n"
        + "void fragment() {\n"
        + "    vec4 tex = slot_sample(UV, dFdx(UV), dFdy(UV)) * slot_color[slot];\n"
        + "    vec4 surface = slot_surface[slot];\n"
        + "    ALBEDO = tex.rgb;\n"
        + "    ROUGHNESS = surface.x;\n"
        + "    METALLIC = surface.y;\n"
        + "    SPECULAR = surface.z;\n"
        + "    ALPHA = surface.w > 0.0 ? tex.a : 1.0;\n"
        + "    ALPHA_SCISSOR_THRESHOLD = surface.w;\n"
        + "}\n";

    private static string ShineCode() =>
        "shader_type spatial;\n"
        + "render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;\n"
        + SlotDeclarations()
        + $"uniform vec4 slot_shine_a[{MaxSlots}];\n"
        + $"uniform vec4 slot_shine_b[{MaxSlots}];\n"
        + "uniform float pulse_top = 1.0;\n"
        + "uniform float rise_seconds = 1.0;\n"
        + "uniform float hold_seconds = 1.0;\n"
        + "uniform float fall_seconds = 1.0;\n"
        + "uniform float cycle_seconds = 1.0;\n"
        + "varying vec3 surface_position;\n"
        + "void vertex() { slot = int(UV2.x + 0.5); surface_position = VERTEX; }\n"
        + "float envelope(vec4 a, vec4 b) {\n"
        + "    if (b.x < 0.5) return a.y;\n"
        + "    float cycle = cycle_seconds / a.z;\n"
        + "    float rise = rise_seconds / a.z;\n"
        + "    float hold = hold_seconds / a.z;\n"
        + "    float fall = fall_seconds / a.z;\n"
        + "    float t = mod(TIME + b.y, cycle);\n"
        + "    float v = t < rise ? mix(a.w, pulse_top, t / rise)\n"
        + "        : t < rise + hold ? pulse_top\n"
        + "        : t < rise + hold + fall ? mix(pulse_top, a.w, (t - rise - hold) / fall)\n"
        + "        : a.w;\n"
        + "    return v * a.y;\n"
        + "}\n"
        + "void fragment() {\n"
        + "    vec4 a = slot_shine_a[slot];\n"
        + "    vec4 b = slot_shine_b[slot];\n"
        + "    if (a.x <= 0.0) discard;\n"
        + "    float strength = a.x * envelope(a, b);\n"
        + "    vec2 uv = UV;\n"
        + "    vec4 texel = slot_color[slot] * slot_sample(uv, dFdx(uv), dFdy(uv));\n"
        + "    bool use_alpha = b.z > 0.5;\n"
        + "    float coverage = use_alpha ? texel.a * step(b.w, texel.a) : 1.0;\n"
        + "    float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.8);\n"
        + "    float edge = 0.0;\n"
        + "    float halo = 0.0;\n"
        + "    if (use_alpha) {\n"
        + "        vec2 size = slot_size();\n"
        + "        float footprint = max(length(dFdx(uv) * size), length(dFdy(uv) * size));\n"
        + "        float soft_alpha = slot_alpha_lod(uv, max(3.0, log2(max(footprint, 1.0)) + 1.5));\n"
        + "        edge = max(0.0, coverage - soft_alpha);\n"
        + "        halo = max(0.0, soft_alpha - coverage);\n"
        + "        rim *= 0.5;\n"
        + "    }\n"
        + "    if (coverage + halo < 0.005) discard;\n"
        + "    float detail = smoothstep(0.08, 0.65, dot(texel.rgb, vec3(0.2126, 0.7152, 0.0722)));\n"
        + "    float flow = sin(surface_position.y * 13.0 - TIME * 1.8\n"
        + "        + sin(surface_position.x * 8.0 + surface_position.z * 6.0 + TIME * 0.7) * 1.6);\n"
        + "    float filament = pow(max(flow, 0.0), 18.0);\n"
        + "    float pulse = 0.9 + 0.1 * sin(TIME * 1.4 + surface_position.y * 3.0);\n"
        + "    float energy = ((rim * (0.6 + filament * 0.3) + detail * (0.055 + filament * 0.2)) * coverage\n"
        + "        + edge * (1.1 + filament * 0.6) + halo * 0.7) * strength;\n"
        + "    vec3 gold = mix(vec3(1.0, 0.65, 0.28), vec3(1.0, 0.94, 0.78), smoothstep(0.2, 1.2, energy));\n"
        + "    ALBEDO = gold * energy * pulse;\n"
        + "}\n";
}
