#include "res://shaders/fx_common.gdshaderinc"

uniform sampler2D tex : filter_linear_mipmap, repeat_enable;
uniform vec4 tint = vec4(1.0);
uniform vec2 uv_scale = vec2(1.0);
uniform vec2 uv_offset = vec2(0.0);
uniform float fx_blend;

void vertex() {
    fx_clip = fx_scene_view_projection * (INV_VIEW_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)));
}

void fragment() {
    vec4 fx_out = fx_shade(texture(tex, UV * uv_scale + uv_offset), COLOR * tint, fx_blend, fx_clip, CAMERA_VISIBLE_LAYERS, FX_DEPTH_TESTED);
    if (fx_out.a < 0.0) discard;
    float fx_fade = ALPHA;
    ALBEDO = fx_out.rgb * fx_fade;
    ALPHA = fx_out.a * fx_fade;
}
