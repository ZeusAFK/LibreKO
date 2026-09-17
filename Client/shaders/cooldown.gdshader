shader_type canvas_item;

uniform float remain : hint_range(0.0, 1.0) = 0.0;
uniform vec4 shade : source_color = vec4(0.02, 0.005, 0.005, 0.66);
uniform vec4 rim : source_color = vec4(0.96, 0.38, 0.38, 0.85);

void fragment() {
    if (remain <= 0.0) discard;
    vec2 d = UV - vec2(0.5);
    float ang = fract(atan(d.x, -d.y) / TAU + 1.0);   // 0 at 12 o'clock, growing clockwise
    float done = 1.0 - remain;
    if (ang < done) discard;                          // the part already recovered stays clear
    COLOR = mix(shade, rim, smoothstep(0.03, 0.0, ang - done));
}
