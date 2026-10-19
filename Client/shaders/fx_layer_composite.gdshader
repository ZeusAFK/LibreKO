shader_type canvas_item;
render_mode blend_premul_alpha, unshaded;
uniform sampler2D layer : filter_nearest;

void fragment() {
    vec4 c = texture(layer, UV);
    vec3 raw = mix(pow((c.rgb + 0.055) / 1.055, vec3(2.4)), c.rgb / 12.92, lessThan(c.rgb, vec3(0.04045)));
    COLOR = vec4(raw, c.a);
}
