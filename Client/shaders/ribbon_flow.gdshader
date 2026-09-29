shader_type canvas_item;
render_mode blend_premul_alpha;

uniform vec2 quad_top_left;
uniform vec2 quad_top_right;
uniform vec2 quad_bottom_right;
uniform vec2 quad_bottom_left;

const int STREAKS = 18;
const int SPARKLES = 76;
const int PULSE_EVERY = 13;
const float WIDE_HALF = 0.8375;
const float WIDE_FEATHER = 1.25;
const float THIN_HALF = 0.175;
const float THIN_FEATHER = 0.4375;

varying vec2 local;

void vertex() {
    local = VERTEX;
}

float hash01(int value) {
    uint x = uint(value);
    x ^= x >> 16u;
    x *= 2146121005u;
    x ^= x >> 15u;
    x *= 2221713035u;
    x ^= x >> 16u;
    return float(x & 16777215u) / 16777215.0;
}

vec2 at(float u, float v) {
    return mix(mix(quad_top_left, quad_top_right, u), mix(quad_bottom_left, quad_bottom_right, u), v);
}

float line_cover(vec2 p, vec2 from, vec2 to, float half_width, float feather) {
    vec2 d = to - from;
    float len = length(d);
    vec2 dir = d / len;
    vec2 r = p - from;
    float along = dot(r, dir);
    float across = abs(dot(r, vec2(-dir.y, dir.x)));
    float x = max(max(-along, along - len), 0.0);
    float y = max(across - half_width, 0.0);
    return clamp(1.0 - max(x, y) / feather, 0.0, 1.0);
}

void over(inout vec4 acc, vec3 rgb, float a) {
    acc = vec4(rgb * a, a) + acc * (1.0 - a);
}

void fragment() {
    float t = TIME;
    vec4 acc = vec4(0.0);
    for (int i = 0; i < STREAKS; i++) {
        float speed = 0.010 + hash01(i * 73 + 9) * 0.012;
        float u = fract(hash01(i * 17 + 3) + t * speed);
        float v = hash01(i * 29 + 11);
        vec2 p = at(u, 0.20 + v * 0.60);
        float len = 4.0 + hash01(i * 43 + 7) * 10.0;
        len = min(len, max(0.0, quad_top_right.x - p.x - 1.5));
        if (len < 0.5) continue;
        vec2 to = p + vec2(len, 0.15);
        over(acc, vec3(1.0, 1.0, 0.88), 0.025 * line_cover(local, p, to, WIDE_HALF, WIDE_FEATHER));
        over(acc, vec3(1.0, 1.0, 0.90), 0.075 * line_cover(local, p, to, THIN_HALF, THIN_FEATHER));
    }
    for (int i = 0; i < SPARKLES; i++) {
        float speed = 0.006 + hash01(i * 53 + 19) * 0.016;
        float u = fract(hash01(i * 31 + 5) + t * speed);
        float v = 0.13 + hash01(i * 47 + 13) * 0.74;
        vec2 p = at(u, v);
        float radius = 0.22 + hash01(i * 67 + 23) * 0.52;
        float dist = distance(local, p);
        if (dist <= radius) over(acc, vec3(1.0, 1.0, 0.88), 0.08 + radius * 0.10);
        if (i % PULSE_EVERY == 0) {
            float pulse = 0.5 + 0.5 * sin(t * 1.15 + float(i) * 1.7);
            if (dist <= 1.8 + pulse * 0.8) over(acc, vec3(1.0, 1.0, 0.90), 0.025 + pulse * 0.025);
            if (dist <= 0.45 + pulse * 0.30) over(acc, vec3(1.0, 1.0, 0.92), 0.22 + pulse * 0.20);
        }
    }
    COLOR = vec4(acc.rgb * COLOR.rgb, acc.a) * COLOR.a;
}
