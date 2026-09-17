shader_type spatial;
render_mode cull_disabled, diffuse_burley;

uniform sampler2D base_tex : source_color, filter_linear_mipmap;
uniform sampler2D mark_tex : source_color, filter_linear_mipmap;
uniform float has_mark = 0.0;
uniform vec3 dye : source_color = vec3(1.0);

void fragment() {
    vec4 c = texture(base_tex, UV);
    if (has_mark > 0.5) {
        vec4 m = texture(mark_tex, UV);
        c.rgb = mix(c.rgb, m.rgb, m.a);
    }
    // Deliberately OPAQUE: the cloth textures are fully opaque, and writing ALPHA would push the
    // cape into the transparent pass, where it stops writing depth and sorts per-object — which
    // shows up as the wearer's body punching through folds. Opaque is also cheaper.
    ALBEDO = c.rgb * dye;
    ROUGHNESS = 0.82;
    SPECULAR = 0.18;
    if (!FRONT_FACING) {
        NORMAL = -NORMAL;
    }
}
