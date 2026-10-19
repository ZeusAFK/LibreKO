#ifdef FX_DOUBLE_SIDED
#define FX_CULL cull_disabled
#else
#define FX_CULL cull_back
#endif

#if defined(FX_OPAQUE)
#define FX_BLEND_MODES depth_draw_opaque
#elif defined(FX_WRITES_DEPTH)
#define FX_BLEND_MODES blend_premul_alpha, depth_draw_always
#else
#define FX_BLEND_MODES blend_premul_alpha, depth_draw_never
#endif

#ifdef FX_NO_DEPTH_TEST
#define FX_DEPTH_TESTED false
render_mode unshaded, fog_disabled, shadows_disabled, FX_BLEND_MODES, FX_CULL, depth_test_disabled;
#else
#define FX_DEPTH_TESTED true
render_mode unshaded, fog_disabled, shadows_disabled, FX_BLEND_MODES, FX_CULL;
#endif

global uniform sampler2D fx_scene_depth;
global uniform mat4 fx_scene_view_projection;
global uniform bool fx_scene_depth_live;
const uint FX_LAYER = 131072u;
varying vec4 fx_clip;

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
