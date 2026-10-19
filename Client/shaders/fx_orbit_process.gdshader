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
