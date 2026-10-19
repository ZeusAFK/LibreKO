shader_type spatial;
render_mode unshaded, blend_mul, depth_draw_never, cull_back, shadows_disabled, fog_disabled;
uniform vec4 tint : source_color = vec4(1.0);

void fragment() {
    ALBEDO = tint.rgb;
}
