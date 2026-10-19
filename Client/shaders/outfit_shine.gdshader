shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
#include "res://shaders/outfit_slots.gdshaderinc"
uniform vec4 slot_shine_a[8];
uniform vec4 slot_shine_b[8];
uniform float pulse_top = 1.0;
uniform float rise_seconds = 1.0;
uniform float hold_seconds = 1.0;
uniform float fall_seconds = 1.0;
uniform float cycle_seconds = 1.0;
global uniform float item_shine_classic;
const float classic_gain = 0.6;
varying vec3 surface_position;

void vertex() {
    slot = int(UV2.x + 0.5);
    surface_position = VERTEX;
}

float envelope(vec4 a, vec4 b) {
    if (b.x < 0.5) return a.y;
    float cycle = cycle_seconds / a.z;
    float rise = rise_seconds / a.z;
    float hold = hold_seconds / a.z;
    float fall = fall_seconds / a.z;
    float t = mod(TIME + b.y, cycle);
    float v = t < rise ? mix(a.w, pulse_top, t / rise)
        : t < rise + hold ? pulse_top
        : t < rise + hold + fall ? mix(pulse_top, a.w, (t - rise - hold) / fall)
        : a.w;
    return v * a.y;
}

void fragment() {
    vec4 a = slot_shine_a[slot];
    vec4 b = slot_shine_b[slot];
    if (a.x <= 0.0) discard;
    float strength = a.x * envelope(a, b);
    vec2 uv = UV;
    vec4 texel = slot_color[slot] * slot_sample(uv, dFdx(uv), dFdy(uv));
    bool use_alpha = b.z > 0.5;
    float coverage = use_alpha ? texel.a * step(b.w, texel.a) : 1.0;
    if (item_shine_classic > 0.5) {
        if (coverage < 0.005) discard;
        ALBEDO = texel.rgb * coverage * envelope(a, b) * classic_gain;
    } else {
        float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.8);
        float edge = 0.0;
        float halo = 0.0;
        if (use_alpha) {
            vec2 size = slot_size();
            float footprint = max(length(dFdx(uv) * size), length(dFdy(uv) * size));
            float soft_alpha = slot_alpha_lod(uv, max(3.0, log2(max(footprint, 1.0)) + 1.5));
            edge = max(0.0, coverage - soft_alpha);
            halo = max(0.0, soft_alpha - coverage);
            rim *= 0.5;
        }
        if (coverage + halo < 0.005) discard;
        float detail = smoothstep(0.08, 0.65, dot(texel.rgb, vec3(0.2126, 0.7152, 0.0722)));
        float flow = sin(surface_position.y * 13.0 - TIME * 1.8
            + sin(surface_position.x * 8.0 + surface_position.z * 6.0 + TIME * 0.7) * 1.6);
        float filament = pow(max(flow, 0.0), 18.0);
        float pulse = 0.9 + 0.1 * sin(TIME * 1.4 + surface_position.y * 3.0);
        float energy = ((rim * (0.6 + filament * 0.3) + detail * (0.055 + filament * 0.2)) * coverage
            + edge * (1.1 + filament * 0.6) + halo * 0.7) * strength;
        vec3 gold = mix(vec3(1.0, 0.65, 0.28), vec3(1.0, 0.94, 0.78), smoothstep(0.2, 1.2, energy));
        ALBEDO = gold * energy * pulse;
    }
}
