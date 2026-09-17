shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform vec4 albedo_color : source_color = vec4(1.0);
uniform vec3 uv_scale = vec3(1.0);
uniform vec3 uv_offset = vec3(0.0);
uniform bool use_texture = false;
uniform bool use_alpha = false;
uniform float alpha_cutoff = 0.01;
uniform float strength = 0.0;

varying vec3 surface_position;

void vertex() {
    surface_position = VERTEX;
}

void fragment() {
    vec2 uv = UV * uv_scale.xy + uv_offset.xy;
    vec4 texel = albedo_color;
    if (use_texture) texel *= texture(albedo_texture, uv);
    float coverage = use_alpha ? texel.a * step(alpha_cutoff, texel.a) : 1.0;

    float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.8);
    float edge = 0.0;
    float halo = 0.0;
    if (use_texture && use_alpha) {
        vec2 size = vec2(textureSize(albedo_texture, 0));
        float footprint = max(length(dFdx(uv) * size), length(dFdy(uv) * size));
        float soft_alpha = textureLod(albedo_texture, uv, max(3.0, log2(max(footprint, 1.0)) + 1.5)).a;
        edge = max(0.0, coverage - soft_alpha);
        halo = max(0.0, soft_alpha - coverage);
        rim *= 0.5;
    }
    if (coverage + halo < 0.005) discard;
    float detail = smoothstep(0.08, 0.65, dot(texel.rgb, vec3(0.2126, 0.7152, 0.0722)));
    float flow = sin(surface_position.y * 13.0 - TIME * 1.8
        + sin(surface_position.x * 8.0 + surface_position.z * 6.0 + TIME * 0.7) * 1.6);
    float filament = pow(max(flow, 0.0), 18.0);
    float pulse = 0.9 + 0.1 * sin(TIME * 1.4 + surface_position.y * 3.0);
    float energy = ((rim * (0.6 + filament * 0.3) + detail * (0.055 + filament * 0.2)) * coverage
        + edge * (1.1 + filament * 0.6) + halo * 0.7) * strength;
    vec3 gold = mix(vec3(1.0, 0.65, 0.28), vec3(1.0, 0.94, 0.78), smoothstep(0.2, 1.2, energy));
    ALBEDO = gold * energy * pulse;
    ALPHA = 1.0;
}
