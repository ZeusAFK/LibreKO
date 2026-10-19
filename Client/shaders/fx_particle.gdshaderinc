#include "res://shaders/fx_common.gdshaderinc"

#ifdef FX_LAYERS
uniform sampler2DArray texture_albedo : filter_linear_mipmap, repeat_enable;
#else
uniform sampler2D texture_albedo : filter_linear_mipmap, repeat_enable;
#endif
uniform float fx_blend;
uniform float particle_lifetime;
uniform float fade_in;
uniform float fade_out;
uniform float roll_rate;
uniform vec2 size_velocity;
uniform int orientation;
uniform mat3 fixed_basis;
uniform vec2 atlas_grid = vec2(1.0);
uniform float atlas_frames = 1.0;
uniform float frame_count = 1.0;
uniform float texture_fps;
varying flat float texture_layer;

void vertex() {
    float age = INSTANCE_CUSTOM.y * particle_lifetime;
    float life = INSTANCE_CUSTOM.w * particle_lifetime;
    vec3 birth_scale = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
    vec2 size = max(vec2(0.0), birth_scale.xy + size_velocity * age);
    vec3 point = vec3(VERTEX.xy * size, VERTEX.z * birth_scale.z);
    float angle = age * roll_rate;
    point.xy = mat2(vec2(cos(angle), sin(angle)), vec2(-sin(angle), cos(angle))) * point.xy;
    mat3 facing = mat3(INV_VIEW_MATRIX);
    if (orientation == 1) {
        vec3 right = cross(vec3(0.0, 1.0, 0.0), INV_VIEW_MATRIX[2].xyz);
        right = length(right) > 0.0001 ? normalize(right) : vec3(1.0, 0.0, 0.0);
        facing = mat3(right, vec3(0.0, 1.0, 0.0), cross(right, vec3(0.0, 1.0, 0.0)));
    } else if (orientation == 2) {
        facing = fixed_basis;
    }
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(facing[0], 0.0), vec4(facing[1], 0.0), vec4(facing[2], 0.0), MODEL_MATRIX[3]);
    VERTEX = point;
    float frame = floor(age * texture_fps);
    float cell = mod(frame, atlas_frames);
    UV = (UV + vec2(mod(cell, atlas_grid.x), floor(cell / atlas_grid.x))) / atlas_grid;
    texture_layer = mod(frame, frame_count);
    if (fade_in > 0.0) COLOR.a *= clamp(age / fade_in, 0.0, 1.0);
    if (fade_out > 0.0) COLOR.a *= clamp((life - age) / fade_out, 0.0, 1.0);
    fx_clip = fx_scene_view_projection * (INV_VIEW_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)));
}

void fragment() {
#ifdef FX_LAYERS
    vec4 texel = texture(texture_albedo, vec3(UV, texture_layer));
#else
    vec4 texel = texture(texture_albedo, UV);
#endif
    vec4 fx_out = fx_shade(texel, COLOR, fx_blend, fx_clip, CAMERA_VISIBLE_LAYERS, FX_DEPTH_TESTED);
    if (fx_out.a < 0.0) discard;
    float fx_fade = ALPHA;
    ALBEDO = fx_out.rgb * fx_fade;
    ALPHA = fx_out.a * fx_fade;
}
