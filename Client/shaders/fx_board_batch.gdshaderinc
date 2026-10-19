#include "res://shaders/fx_common.gdshaderinc"

uniform sampler2DArray frames : filter_linear_mipmap, repeat_enable;
uniform vec2 uv_scale = vec2(1.0, 1.0);
varying vec3 cell;
varying flat float blend_word;

void vertex() {
    cell = INSTANCE_CUSTOM.xyz;
    blend_word = INSTANCE_CUSTOM.w;
    fx_clip = fx_scene_view_projection * (INV_VIEW_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)));
}

void fragment() {
    vec4 fx_out = fx_shade(texture(frames, vec3(UV * uv_scale + cell.xy, cell.z)), vec4(1.0, 1.0, 1.0, COLOR.a), blend_word, fx_clip, CAMERA_VISIBLE_LAYERS, FX_DEPTH_TESTED);
    if (fx_out.a < 0.0) discard;
    float fx_fade = ALPHA * COLOR.r;
    ALBEDO = fx_out.rgb * fx_fade;
    ALPHA = fx_out.a * fx_fade;
}
