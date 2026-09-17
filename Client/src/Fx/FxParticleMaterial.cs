using Godot;
using System.Collections.Generic;

namespace LibreKO;

internal static class FxParticleMaterial
{
    private static readonly Dictionary<(bool Add, bool NoDepth, bool DoubleSided, bool Layers), Shader> Shaders = new();
    private static readonly Dictionary<string, Texture2DArray> Sequences = new();

    internal static ShaderMaterial Build(Godot.Collections.Dictionary p, Texture2D first,
        float lifetime, bool hasColorRamp)
    {
        bool add = p["blend"].AsString() == "add" || Fx.IsSrcColorOverInv(Fx.SrcBlend(p), Fx.DestBlend(p));
        int flags = p.ContainsKey("renderFlags") ? p["renderFlags"].AsInt32() : Fx.RfNotZWrite | Fx.RfDoubleSided;
        int count = Mathf.Max(1, p["frameCount"].AsInt32());
        bool layers = count > 1;
        var key = (add, (flags & Fx.RfNotZBuffer) != 0, (flags & Fx.RfDoubleSided) != 0, layers);
        if (!Shaders.TryGetValue(key, out var shader))
        {
            string modes = $"unshaded, depth_draw_never, {(add ? "blend_add, fog_disabled" : "blend_mix")}, "
                + (key.Item3 ? "cull_disabled" : "cull_back") + (key.Item2 ? ", depth_test_disabled" : "");
            shader = new Shader { Code = "shader_type spatial;\nrender_mode " + modes + ";\n"
                + (layers ? "uniform sampler2DArray texture_albedo" : "uniform sampler2D texture_albedo")
                + " : source_color, filter_linear, repeat_enable;\n" + VertexCode
                + "\nvoid fragment() {\n    vec4 tex = "
                + (layers ? "texture(texture_albedo, vec3(UV, texture_layer))" : "texture(texture_albedo, UV)")
                + ";\n    ALBEDO = tex.rgb * COLOR.rgb;\n    ALPHA = tex.a * COLOR.a;\n}\n" };
            Shaders[key] = shader;
        }
        var mat = new ShaderMaterial { Shader = shader };
        if (layers)
            mat.SetShaderParameter("texture_albedo", Sequence(p, first, add));
        else
            mat.SetShaderParameter("texture_albedo", Fx.TextureForBlend(first, add, Fx.SrcBlend(p), Fx.DestBlend(p)));
        mat.SetShaderParameter("particle_lifetime", lifetime);
        mat.SetShaderParameter("fade_in", hasColorRamp ? 0f : Fx.ReadF(p, "fadeIn"));
        mat.SetShaderParameter("fade_out", hasColorRamp ? 0f : Fx.ReadF(p, "fadeOut"));
        mat.SetShaderParameter("roll_rate", Fx.ReadF(p, "rollRate"));
        var growth = p["sizeVel"].AsGodotArray();
        mat.SetShaderParameter("size_velocity", new Vector2((float)growth[0].AsDouble(), (float)growth[1].AsDouble()));
        bool fixedY = p.ContainsKey("velAlign") && p["velAlign"].AsBool();
        bool fixedRotation = p.ContainsKey("rotAbs") && p["rotAbs"].AsBool();
        mat.SetShaderParameter("orientation", fixedRotation ? 2 : fixedY ? 1 : 0);
        Vector3 rot = Fx.ReadVec3(p, "rot") * (Mathf.Pi / 180f);
        Quaternion q = Basis.FromEuler(rot).GetRotationQuaternion();
        mat.SetShaderParameter("fixed_basis", new Basis(new Quaternion(q.X, -q.Y, -q.Z, q.W)));
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

    private static Texture2DArray Sequence(Godot.Collections.Dictionary p, Texture2D first, bool add)
    {
        string key = p["tex"].ToString() + $"/{add}/{Fx.SrcBlend(p)}/{Fx.DestBlend(p)}";
        if (Sequences.TryGetValue(key, out var cached)) return cached;
        var images = new Godot.Collections.Array<Image>();
        int count = Mathf.Max(1, p["frameCount"].AsInt32());
        for (int i = 0; i < count; i++)
        {
            var tex = Fx.TextureForBlend(Fx.FrameTexture(p, i) ?? first, add, Fx.SrcBlend(p), Fx.DestBlend(p));
            var image = tex.GetImage();
            if (image.IsCompressed()) image.Decompress();
            image.ClearMipmaps();
            image.Convert(Image.Format.Rgba8);
            if (image.GetWidth() != first.GetWidth() || image.GetHeight() != first.GetHeight())
                image.Resize(first.GetWidth(), first.GetHeight());
            images.Add(image);
        }
        var sequence = new Texture2DArray();
        sequence.CreateFromImages(images);
        foreach (var image in images) image.Dispose();
        Sequences[key] = sequence;
        return sequence;
    }

    private const string VertexCode = """
uniform float particle_lifetime;
uniform float fade_in;
uniform float fade_out;
uniform float roll_rate;
uniform vec2 size_velocity;
uniform int orientation;
uniform mat3 fixed_basis;
uniform vec2 atlas_grid = vec2(1.0);
uniform float atlas_frames = 1.0;
uniform float frame_count = 1.0;
uniform float texture_fps;
varying flat float texture_layer;

void vertex() {
    float age = INSTANCE_CUSTOM.y * particle_lifetime;
    float life = INSTANCE_CUSTOM.w * particle_lifetime;
    vec3 birth_scale = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
    vec2 size = max(vec2(0.0), birth_scale.xy + size_velocity * age);
    vec3 point = vec3(VERTEX.xy * size, VERTEX.z * birth_scale.z);
    float angle = age * roll_rate;
    point.xy = mat2(vec2(cos(angle), sin(angle)), vec2(-sin(angle), cos(angle))) * point.xy;
    mat3 facing = mat3(INV_VIEW_MATRIX);
    if (orientation == 1) {
        vec3 right = cross(vec3(0.0, 1.0, 0.0), INV_VIEW_MATRIX[2].xyz);
        right = length(right) > 0.0001 ? normalize(right) : vec3(1.0, 0.0, 0.0);
        facing = mat3(right, vec3(0.0, 1.0, 0.0), cross(right, vec3(0.0, 1.0, 0.0)));
    } else if (orientation == 2) {
        facing = mat3(MODEL_MATRIX[0].xyz / max(birth_scale.x, 0.0001),
                      MODEL_MATRIX[1].xyz / max(birth_scale.y, 0.0001),
                      MODEL_MATRIX[2].xyz / max(birth_scale.z, 0.0001)) * fixed_basis;
    }
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(facing[0], 0.0), vec4(facing[1], 0.0), vec4(facing[2], 0.0), MODEL_MATRIX[3]);
    VERTEX = point;
    float frame = floor(age * texture_fps);
    float cell = mod(frame, atlas_frames);
    UV = (UV + vec2(mod(cell, atlas_grid.x), floor(cell / atlas_grid.x))) / atlas_grid;
    texture_layer = mod(frame, frame_count);
    if (fade_in > 0.0) COLOR.a *= clamp(age / fade_in, 0.0, 1.0);
    if (fade_out > 0.0) COLOR.a *= clamp((life - age) / fade_out, 0.0, 1.0);
}
""";
}
