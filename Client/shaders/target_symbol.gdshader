shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;

uniform sampler2D symbol : source_color, filter_linear_mipmap_anisotropic;
uniform vec4 tint : source_color = vec4(1.0);

void fragment() {
    vec3 t = texture(symbol, UV).rgb;
    ALBEDO = tint.rgb;
    ALPHA = max(t.r, max(t.g, t.b)) * tint.a;
}
