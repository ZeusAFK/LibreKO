shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;
#include "res://shaders/outfit_slots.gdshaderinc"
uniform vec4 slot_surface[8];

void vertex() {
    slot = int(UV2.x + 0.5);
}

void fragment() {
    vec4 tex = slot_sample(UV, dFdx(UV), dFdy(UV)) * slot_color[slot];
    vec4 surface = slot_surface[slot];
    ALBEDO = tex.rgb;
    ROUGHNESS = surface.x;
    METALLIC = surface.y;
    SPECULAR = surface.z;
    ALPHA = surface.w > 0.0 ? tex.a : 1.0;
    ALPHA_SCISSOR_THRESHOLD = surface.w;
}
