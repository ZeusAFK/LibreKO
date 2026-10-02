using Godot;
using System.Collections.Generic;

namespace LibreKO;

internal static class FxParticleMaterial
{
    private static readonly Dictionary<(bool NoDepth, bool DoubleSided, bool Layers), Shader> Shaders = Shutdown.Track(new Dictionary<(bool NoDepth, bool DoubleSided, bool Layers), Shader>());
    private static readonly Dictionary<string, Texture2DArray> Sequences = Shutdown.Track(new Dictionary<string, Texture2DArray>());

    internal static ShaderMaterial Build(Godot.Collections.Dictionary p, Texture2D first,
        float lifetime, bool hasColorRamp)
    {
        int flags = FxShading.RenderFlags(p);
        int count = Mathf.Max(1, p["frameCount"].AsInt32());
        bool layers = count > 1;
        bool noDepthTest = FxShading.NoDepthTest(flags);
        var key = (noDepthTest, FxShading.DoubleSided(flags), layers);
        if (!Shaders.TryGetValue(key, out var shader))
        {
            shader = new Shader { Code = "shader_type spatial;\n"
                + $"render_mode {FxShading.RenderModes(key.Item2, noDepthTest)};\n"
                + (layers ? "uniform sampler2DArray texture_albedo" : "uniform sampler2D texture_albedo")
                + " : filter_linear, repeat_enable;\nuniform float fx_blend;\n" + FxShading.Header() + VertexCode
                + "\nvoid fragment() {\n    vec4 fx_out = fx_shade("
                + (layers ? "texture(texture_albedo, vec3(UV, texture_layer))" : "texture(texture_albedo, UV)")
                + $", COLOR, fx_blend, fx_clip, CAMERA_VISIBLE_LAYERS, {FxShading.DepthTested(noDepthTest)});\n"
                + FxShading.Apply() + "}\n" };
            Shaders[key] = shader;
        }
        var mat = new ShaderMaterial { Shader = shader };
        mat.SetShaderParameter("texture_albedo", layers ? Sequence(p, first) : first);
        mat.SetShaderParameter(FxShading.BlendParam, (float)FxShading.Packed(p));
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

    private static Shader? _orbitShader;

    internal static ShaderMaterial OrbitProcess(ParticleProcessMaterial basis, Texture2D? colorRamp, float orbitRate)
    {
        _orbitShader ??= new Shader { Code = OrbitProcessCode };
        var material = new ShaderMaterial { Shader = _orbitShader };
        material.SetShaderParameter("lifetime_randomness", (float)basis.LifetimeRandomness);
        material.SetShaderParameter("scale_min", basis.ScaleMin);
        material.SetShaderParameter("scale_max", basis.ScaleMax);
        material.SetShaderParameter("gravity", basis.Gravity);
        material.SetShaderParameter("orbit_rate", orbitRate);
        material.SetShaderParameter("has_color_ramp", colorRamp != null);
        if (colorRamp != null) material.SetShaderParameter("color_ramp", colorRamp);
        return material;
    }

    private const string OrbitProcessCode = """
shader_type particles;
render_mode disable_velocity, disable_force, keep_data;

uniform float lifetime_randomness = 0.0;
uniform float scale_min = 1.0;
uniform float scale_max = 1.0;
uniform vec3 gravity = vec3(0.0);
uniform float orbit_rate = 0.0;
uniform bool has_color_ramp = false;
uniform sampler2D color_ramp : repeat_disable, filter_linear;

float orbit_hash(uint x) {
    x = (x ^ 61u) ^ (x >> 16u);
    x *= 9u;
    x = x ^ (x >> 4u);
    x *= 668265261u;
    x = x ^ (x >> 15u);
    return float(x) / 4294967295.0;
}

vec3 orbit_turn(vec3 v, vec3 k, float a) {
    float c = cos(a);
    float s = sin(a);
    return v * c + cross(k, v) * s + k * dot(k, v) * (1.0 - c);
}

void start() {
    vec3 spawn = TRANSFORM[3].xyz;
    USERDATA1 = vec4(COLOR.rgb, 0.0);
    USERDATA2 = CUSTOM;
    USERDATA3 = vec4(spawn - COLOR.rgb, 0.0);
    USERDATA4 = vec4(VELOCITY, 0.0);
    uint seed = NUMBER * 1973u + RANDOM_SEED * 9277u;
    float s = mix(scale_min, scale_max, orbit_hash(seed));
    TRANSFORM = mat4(vec4(normalize(TRANSFORM[0].xyz) * s, 0.0), vec4(normalize(TRANSFORM[1].xyz) * s, 0.0),
                     vec4(normalize(TRANSFORM[2].xyz) * s, 0.0), vec4(spawn, 1.0));
    CUSTOM = vec4(0.0, 0.0, 0.0, 1.0 - lifetime_randomness * orbit_hash(seed + 7u));
    COLOR = vec4(1.0);
}

void process() {
    CUSTOM.y += DELTA / LIFETIME;
    if (CUSTOM.y > CUSTOM.w) {
        ACTIVE = false;
    }
    float age = CUSTOM.y * LIFETIME;
    vec3 travel = USERDATA3.xyz + USERDATA4.xyz * age;
    vec3 turned = orbit_turn(travel, USERDATA2.xyz, USERDATA2.w + orbit_rate * age);
    TRANSFORM[3].xyz = USERDATA1.xyz + turned + 0.5 * gravity * age * age;
    COLOR = has_color_ramp ? texture(color_ramp, vec2(clamp(CUSTOM.y / CUSTOM.w, 0.0, 1.0), 0.0)) : vec4(1.0);
}
""";

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
    fx_clip = fx_scene_view_projection * (INV_VIEW_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)));
}
""";
}
