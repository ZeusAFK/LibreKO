shader_type canvas_item;

uniform float radius : hint_range(0.0, 0.5) = 0.5;
uniform float feather : hint_range(0.0, 0.1) = 0.012;

void fragment() {
	vec4 src = texture(TEXTURE, UV);
	float d = length(UV - vec2(0.5));
	src.a *= 1.0 - smoothstep(radius - feather, radius, d);
	COLOR = src;
}
