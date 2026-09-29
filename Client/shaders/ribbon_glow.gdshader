shader_type canvas_item;

uniform vec3 breathe = vec3(0.0);
uniform float rate = 0.62;

void fragment() {
    COLOR.rgb += breathe * sin(TIME * rate) * UV.x;
}
