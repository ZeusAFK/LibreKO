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

    private static readonly Dictionary<string, ShaderMaterial> materials = Shutdown.Track(new Dictionary<string, ShaderMaterial>());
    private static ImageTexture? white;

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
        if (materials.TryGetValue(id, out var cached)) return cached;

        var colors = new Vector4[MaxSlots];
        var surfaces = new Vector4[MaxSlots];
        var shineA = new Vector4[MaxSlots];
        var shineB = new Vector4[MaxSlots];
        var material = Shaders.Material("outfit_body");
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
                ItemShine.EnsureGlobals();
                shine ??= Shaders.Material("outfit_shine");
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
        materials[id] = material;
        return material;
    }

    private static ImageTexture White()
    {
        if (white != null) return white;
        using var image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        return white = ImageTexture.CreateFromImage(image);
    }
}
