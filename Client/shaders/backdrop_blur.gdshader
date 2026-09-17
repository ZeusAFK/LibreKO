shader_type canvas_item;
render_mode unshaded;

const float LOD    = 4.0;    // mip to sample -- does the heavy lifting
const float RADIUS = 0.010;  // extra spread between taps, in UV
const float DESAT  = 0.55;
const float DIM    = 0.26;   // bright skies blur to a pale wash otherwise -- keep it a matte

void fragment() {
    vec3 sum = vec3(0.0);
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 uv = clamp(UV + vec2(float(x), float(y)) * RADIUS, vec2(0.0), vec2(1.0));
            sum += textureLod(TEXTURE, uv, LOD).rgb;
        }
    }
    vec3 col = sum / 9.0;
    col = mix(col, vec3(dot(col, vec3(0.299, 0.587, 0.114))), DESAT) * DIM;
    col *= mix(0.40, 1.0, smoothstep(0.95, 0.25, length(UV - vec2(0.5))));   // vignette
    COLOR = vec4(col, 1.0);
}
