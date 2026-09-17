shader_type canvas_item;
uniform sampler2D map_tex : filter_linear, repeat_disable;
uniform vec2 center = vec2(0.5);
uniform float span = 0.3;
uniform float rot = 0.0;
uniform vec4 oob : source_color = vec4(0.04, 0.05, 0.07, 1.0);

void fragment() {
    vec2 p = UV - vec2(0.5);
    float c = cos(rot), s = sin(rot);
    vec2 pr = vec2(p.x * c - p.y * s, p.x * s + p.y * c);
    vec2 muv = center + pr * span;
    vec3 col;
    if (muv.x < 0.0 || muv.x > 1.0 || muv.y < 0.0 || muv.y > 1.0)
        col = oob.rgb;
    else
        col = texture(map_tex, muv).rgb;
    float edge = max(abs(p.x), abs(p.y));
    col *= mix(1.0, 0.62, smoothstep(0.22, 0.50, edge));       // square depth vignette
    float gloss = clamp(0.62 - length(p - vec2(-0.12, -0.15)) * 1.7, 0.0, 1.0);
    col += gloss * 0.05;                                        // glass sheen, upper-left
    COLOR = vec4(col, 1.0);
}
