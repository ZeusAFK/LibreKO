using Godot;
using System.Collections.Generic;

namespace LibreKO;

internal static class FxParticleMaterial
{
    private static readonly Dictionary<(bool NoDepth, bool DoubleSided, bool Layers), Shader> variantShaders = Shutdown.Track(new Dictionary<(bool NoDepth, bool DoubleSided, bool Layers), Shader>());
    private static readonly Dictionary<string, Texture2DArray> Sequences = Shutdown.Track(new Dictionary<string, Texture2DArray>());

    internal static ShaderMaterial Build(Godot.Collections.Dictionary p, Texture2D first,
        float lifetime, bool hasColorRamp)
    {
        int flags = FxShading.RenderFlags(p);
        int count = Mathf.Max(1, p["frameCount"].AsInt32());
        bool layers = count > 1;
        bool noDepthTest = FxShading.NoDepthTest(flags);
        var key = (noDepthTest, FxShading.DoubleSided(flags), layers);
        if (!variantShaders.TryGetValue(key, out var shader))
        {
            shader = FxShading.VariantShader("fx_particle", key.Item2, noDepthTest, layers ? "FX_LAYERS" : null);
            variantShaders[key] = shader;
        }
        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("texture_albedo", layers ? Sequence(p, first) : first);
        mat.SetShaderParameter(FxShading.BlendParam, (float)FxShading.Packed(p));
        mat.SetShaderParameter("particle_lifetime", lifetime);
        mat.SetShaderParameter("fade_in", hasColorRamp ? 0f : Fx.ReadF(p, "fadeIn"));
        mat.SetShaderParameter("fade_out", hasColorRamp ? 0f : Fx.ReadF(p, "fadeOut"));
        mat.SetShaderParameter("roll_rate", Fx.ReadRollRate(p));
        var growth = p["sizeVel"].AsGodotArray();
        mat.SetShaderParameter("size_velocity", new Vector2((float)growth[0].AsDouble(), (float)growth[1].AsDouble()));
        bool fixedY = p.ContainsKey("velAlign") && p["velAlign"].AsBool();
        bool fixedRotation = p.ContainsKey("rotAbs") && p["rotAbs"].AsBool();
        mat.SetShaderParameter("orientation", fixedRotation ? 2 : fixedY ? 1 : 0);
        mat.SetShaderParameter("fixed_basis", Fx.FixedRotation(Fx.ReadVec3(p, "rot")));
        Vector2 grid = Vector2.One;
        if (p.ContainsKey("atlas"))
        {
            var a = p["atlas"].AsGodotArray();
            grid = new Vector2(Mathf.Max(1, a[0].AsInt32()), Mathf.Max(1, a[1].AsInt32()));
        }
        mat.SetShaderParameter("atlas_grid", grid);
        mat.SetShaderParameter("atlas_frames", p.ContainsKey("atlasFrames")
            ? Mathf.Clamp(p["atlasFrames"].AsInt32(), 1, (int)(grid.X * grid.Y)) : grid.X * grid.Y);
        mat.SetShaderParameter("frame_count", count);
        mat.SetShaderParameter("texture_fps", Fx.ReadF(p, "texFPS"));
        return mat;
    }

    internal static ShaderMaterial OrbitProcess(ParticleProcessMaterial basis, Texture2D? colorRamp, float orbitRate)
    {
        var material = Shaders.Material("fx_orbit_process");
        material.SetShaderParameter("lifetime_randomness", (float)basis.LifetimeRandomness);
        material.SetShaderParameter("scale_min", basis.ScaleMin);
        material.SetShaderParameter("scale_max", basis.ScaleMax);
        material.SetShaderParameter("gravity", basis.Gravity);
        material.SetShaderParameter("orbit_rate", orbitRate);
        material.SetShaderParameter("has_color_ramp", colorRamp != null);
        if (colorRamp != null) material.SetShaderParameter("color_ramp", colorRamp);
        return material;
    }

    private static Texture2DArray Sequence(Godot.Collections.Dictionary p, Texture2D first)
    {
        string key = p["tex"].ToString();
        if (Sequences.TryGetValue(key, out var cached)) return cached;
        var images = new Godot.Collections.Array<Image>();
        int count = Mathf.Max(1, p["frameCount"].AsInt32());
        for (int i = 0; i < count; i++)
        {
            var tex = Fx.FrameTexture(p, i) ?? first;
            var image = FxImages.Read(tex) ?? Image.CreateEmpty(first.GetWidth(), first.GetHeight(), false, Image.Format.Rgba8);
            if (image.IsCompressed()) image.Decompress();
            image.ClearMipmaps();
            image.Convert(Image.Format.Rgba8);
            if (image.GetWidth() != first.GetWidth() || image.GetHeight() != first.GetHeight())
                image.Resize(first.GetWidth(), first.GetHeight());
            image.GenerateMipmaps();
            images.Add(image);
        }
        var sequence = new Texture2DArray();
        sequence.CreateFromImages(images);
        foreach (var image in images) image.Dispose();
        Sequences[key] = sequence;
        return sequence;
    }
}
