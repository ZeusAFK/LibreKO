shader_type spatial;
render_mode cull_disabled;
uniform sampler2DArray tiles : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float cell_size = 4.0;
varying flat float v_l1;
varying flat float v_l2;
varying flat float v_has2;
varying vec2 v_grid;

vec3 linear_to_srgb(vec3 c) {
    vec3 lo = c * 12.92;
    vec3 hi = 1.055 * pow(max(c, vec3(0.0)), vec3(1.0 / 2.4)) - 0.055;
    return mix(lo, hi, step(vec3(0.0031308), c));
}

vec3 srgb_to_linear(vec3 c) {
    vec3 lo = c / 12.92;
    vec3 hi = pow(max((c + 0.055) / 1.055, vec3(0.0)), vec3(2.4));
    return mix(lo, hi, step(vec3(0.04045), c));
}

void vertex() {
    v_l1 = CUSTOM0.r;
    v_l2 = CUSTOM0.g;
    v_has2 = CUSTOM0.b;
    v_grid = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz / cell_size;
}
// Cell UV is the grid fraction under a .gtd dir flip: tile's orientation, grid's scale (handoff §8).
vec2 tile_grad(vec2 duv, float len) {
    return duv * (len / max(length(duv), 1e-8));
}

void fragment() {
    float lx = length(dFdx(v_grid)), ly = length(dFdy(v_grid));
    vec3 c1 = textureGrad(tiles, vec3(UV, v_l1),
                          tile_grad(dFdx(UV), lx), tile_grad(dFdy(UV), ly)).rgb;
    if (v_has2 > 0.5) {
        vec3 c2 = textureGrad(tiles, vec3(UV2, v_l2),
                              tile_grad(dFdx(UV2), lx), tile_grad(dFdy(UV2), ly)).rgb;
        vec3 fixed_function_add = min(linear_to_srgb(c1) + linear_to_srgb(c2), vec3(1.0));
        ALBEDO = srgb_to_linear(fixed_function_add);
    } else {
        ALBEDO = c1;
    }
    ROUGHNESS = 1.0;
}
