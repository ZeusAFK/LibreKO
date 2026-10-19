uniform sampler2D slot_texture_0 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D slot_texture_1 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D slot_texture_2 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D slot_texture_3 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D slot_texture_4 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D slot_texture_5 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D slot_texture_6 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D slot_texture_7 : source_color, filter_linear_mipmap, repeat_enable;
uniform vec4 slot_color[8];
varying flat int slot;

vec4 slot_sample(vec2 uv, vec2 ddx, vec2 ddy) {
    if (slot == 0) return textureGrad(slot_texture_0, uv, ddx, ddy);
    if (slot == 1) return textureGrad(slot_texture_1, uv, ddx, ddy);
    if (slot == 2) return textureGrad(slot_texture_2, uv, ddx, ddy);
    if (slot == 3) return textureGrad(slot_texture_3, uv, ddx, ddy);
    if (slot == 4) return textureGrad(slot_texture_4, uv, ddx, ddy);
    if (slot == 5) return textureGrad(slot_texture_5, uv, ddx, ddy);
    if (slot == 6) return textureGrad(slot_texture_6, uv, ddx, ddy);
    return textureGrad(slot_texture_7, uv, ddx, ddy);
}

vec2 slot_size() {
    if (slot == 0) return vec2(textureSize(slot_texture_0, 0));
    if (slot == 1) return vec2(textureSize(slot_texture_1, 0));
    if (slot == 2) return vec2(textureSize(slot_texture_2, 0));
    if (slot == 3) return vec2(textureSize(slot_texture_3, 0));
    if (slot == 4) return vec2(textureSize(slot_texture_4, 0));
    if (slot == 5) return vec2(textureSize(slot_texture_5, 0));
    if (slot == 6) return vec2(textureSize(slot_texture_6, 0));
    return vec2(textureSize(slot_texture_7, 0));
}

float slot_alpha_lod(vec2 uv, float lod) {
    if (slot == 0) return textureLod(slot_texture_0, uv, lod).a;
    if (slot == 1) return textureLod(slot_texture_1, uv, lod).a;
    if (slot == 2) return textureLod(slot_texture_2, uv, lod).a;
    if (slot == 3) return textureLod(slot_texture_3, uv, lod).a;
    if (slot == 4) return textureLod(slot_texture_4, uv, lod).a;
    if (slot == 5) return textureLod(slot_texture_5, uv, lod).a;
    if (slot == 6) return textureLod(slot_texture_6, uv, lod).a;
    return textureLod(slot_texture_7, uv, lod).a;
}
