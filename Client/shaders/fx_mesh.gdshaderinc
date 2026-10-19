#include "res://shaders/fx_common.gdshaderinc"

uniform sampler2DArray frames : filter_linear_mipmap, repeat_enable;
uniform vec4 base_color = vec4(1.0);
uniform float frame_count = 1.0;
uniform bool frame_loop = true;
uniform float fx_blend;
instance uniform float fade = 1.0;
instance uniform vec2 uv_offset = vec2(0.0);
instance uniform float frame_raw = 0.0;

void vertex() {
    UV += uv_offset;
    fx_clip = fx_scene_view_projection * (INV_VIEW_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)));
}

void fragment() {
    float layer = frame_loop ? mod(frame_raw, frame_count) : min(frame_raw, frame_count - 1.0);
    vec4 fx_out = fx_shade(texture(frames, vec3(UV, layer)), base_color * vec4(1.0, 1.0, 1.0, fade), fx_blend, fx_clip, CAMERA_VISIBLE_LAYERS, FX_DEPTH_TESTED);
    if (fx_out.a < 0.0) discard;
#ifdef FX_OPAQUE
    ALBEDO = fx_out.rgb;
#else
    float fx_fade = ALPHA;
    ALBEDO = fx_out.rgb * fx_fade;
    ALPHA = fx_out.a * fx_fade;
#endif
}
