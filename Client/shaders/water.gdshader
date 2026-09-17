shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

uniform vec3 deep_color : source_color = vec3(0.04, 0.16, 0.22);
uniform vec3 shallow_color : source_color = vec3(0.13, 0.38, 0.44);
uniform vec3 foam_color : source_color = vec3(0.90, 0.95, 0.97);
uniform sampler2D wave_normal_a : hint_normal, repeat_enable, filter_linear_mipmap;
uniform sampler2D wave_normal_b : hint_normal, repeat_enable, filter_linear_mipmap;
uniform float wave_speed = 1.0;       // time multiplier
uniform float wave_amp = 0.18;        // gentle swell height multiplier
uniform float nmap_scale = 0.06;      // normal-map tiling (world units -> uv)
uniform vec2 scroll_a = vec2(0.045, 0.028);
uniform vec2 scroll_b = vec2(-0.032, 0.040);
uniform float normal_strength = 0.42; // ripple normal intensity
uniform float absorption = 0.10;      // Beer-Lambert: how fast color tints to deep with depth
uniform float foam_depth = 0.9;       // shoreline foam band width (world units)
uniform float foam_amount = 0.4;      // shoreline foam opacity (live-tunable)
uniform float refraction = 0.025;
uniform float rough_min = 0.12;       // base roughness looking straight down (live-tunable)
uniform float spec_amt = 0.4;         // specular intensity (live-tunable)
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform sampler2D depth_tex : hint_depth_texture, filter_linear_mipmap;

varying vec3 v_world;
varying vec3 v_wnorm;   // analytic Gerstner normal (world space)

const float GRAV = 9.8;

// Sum of 4 gentle Gerstner waves: outputs world displacement + analytic normal.
void gerstner(vec2 pos, float t, out vec3 disp, out vec3 nrm) {
    vec2 dir[4]; float len[4]; float amp[4]; float stp[4];
    dir[0] = normalize(vec2( 1.0,  0.6)); len[0] = 22.0; amp[0] = 0.50; stp[0] = 0.12;
    dir[1] = normalize(vec2(-0.7,  1.0)); len[1] = 14.0; amp[1] = 0.30; stp[1] = 0.11;
    dir[2] = normalize(vec2( 0.4, -1.0)); len[2] =  9.0; amp[2] = 0.16; stp[2] = 0.10;
    dir[3] = normalize(vec2(-1.0, -0.3)); len[3] =  6.5; amp[3] = 0.09; stp[3] = 0.10;
    disp = vec3(0.0);
    vec3 n = vec3(0.0, 1.0, 0.0);
    for (int i = 0; i < 4; i++) {
        float k = 6.2831853 / len[i];
        float a = amp[i] * wave_amp;
        float w = sqrt(GRAV * k);
        float f = k * dot(dir[i], pos) - w * t * wave_speed;
        float c = cos(f);
        float s = sin(f);
        disp.x += stp[i] * a * dir[i].x * c;
        disp.z += stp[i] * a * dir[i].y * c;
        disp.y += a * s;
        float wa = w * a;
        n.x -= dir[i].x * wa * c;
        n.z -= dir[i].y * wa * c;
        n.y -= stp[i] * wa * s;
    }
    nrm = normalize(n);
}

void vertex() {
    vec3 wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    vec3 disp; vec3 nrm;
    gerstner(wpos.xz, TIME, disp, nrm);
    VERTEX += disp;             // node transform is identity, so world disp == model disp
    v_world = wpos + disp;
    v_wnorm = nrm;
}

// Sample a tiling normal map (tangent space, z-up) and map it onto the horizontal surface:
// tangent x -> world X, tangent y -> world Z, tangent z -> world up (Y).
vec3 unpack_n(sampler2D tex, vec2 uv) {
    vec3 n = texture(tex, uv).xyz * 2.0 - 1.0;
    return vec3(n.x, n.z, n.y);
}

void fragment() {
    float t = TIME * wave_speed;
    // Evolving ripples: domain-warp the first normal layer by the second (which scrolls the
    // other way), so the combined pattern MORPHS over time instead of sliding as a rigid sheet.
    vec3 nb = unpack_n(wave_normal_b, v_world.xz * nmap_scale * 1.9 + scroll_b * t);
    vec2 warp = vec2(nb.x, nb.z) * 0.06;
    vec3 na = unpack_n(wave_normal_a, v_world.xz * nmap_scale + scroll_a * t + warp);
    vec3 ripple = normalize(na + nb);
    // Blend the ripple's horizontal tilt onto the (near-flat) Gerstner swell normal.
    vec3 wn = normalize(v_wnorm + vec3(ripple.x, 0.0, ripple.z) * normal_strength);
    NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);

    // Reconstruct the opaque scene depth behind the water -> the water column thickness we look
    // through. Godot 4 (Vulkan) depth is [0,1] already: NDC.xy = SCREEN_UV*2-1, NDC.z = raw depth.
    float draw = texture(depth_tex, SCREEN_UV).r;
    vec4 vp = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, draw, 1.0);
    vp.xyz /= vp.w;
    float water_depth = max(0.0, VERTEX.z - vp.z);   // both view-space (negative z); >=0

    // Beer-Lambert: shallow water shows the (refracted) BOTTOM; it only tints to deep blue as the
    // column gets thicker. This is the see-through that makes it read as water, not blue paint.
    float dt = 1.0 - exp(-water_depth * absorption);            // 0 shallow -> ~1 deep
    vec2 ruv = SCREEN_UV + NORMAL.xy * refraction * clamp(water_depth, 0.0, 3.0);
    vec3 refr = texture(screen_tex, ruv).rgb;
    vec3 body = mix(refr, deep_color, dt);                       // bottom -> deep
    body = mix(body, shallow_color, dt * (1.0 - dt) * 1.2);     // mid-depth teal band

    // Shoreline foam where water meets terrain (width = foam_depth, opacity = foam_amount).
    float foam = (1.0 - smoothstep(0.0, foam_depth, water_depth)) * foam_amount;
    vec3 col = mix(body, foam_color, foam);

    // Fresnel: see into the water looking straight down, sky reflection at grazing angles.
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 5.0);

    // Geometric specular anti-aliasing: the sun shattering on the fine ripples is specular
    // aliasing (bright firefly sparkles). Raise roughness where the normal changes fast within a
    // pixel, so those areas get a broad soft glitter, while calm water stays crisply reflective.
    vec3 ndx = dFdx(NORMAL);
    vec3 ndy = dFdy(NORMAL);
    float geo = dot(ndx, ndx) + dot(ndy, ndy);
    float base_rough = mix(rough_min, rough_min * 0.4, fres);

    ALBEDO = col;
    METALLIC = 0.0;
    ROUGHNESS = clamp(base_rough + clamp(geo * 0.8, 0.0, 0.35), 0.0, 1.0);
    SPECULAR = spec_amt;
    // Transparent at the shallow edges (shows ground), turning opaque with depth + grazing angle.
    ALPHA = clamp(0.45 + dt * 0.55 + fres * 0.3 + foam, 0.0, 1.0);
}
