shader_type canvas_item;

uniform float sat_left : hint_range(0.0, 1.0) = 0.0;
uniform float sat_right : hint_range(0.0, 1.0) = 0.0;
uniform float split : hint_range(0.0, 1.0) = 0.5;
uniform float feather : hint_range(0.0, 0.5) = 0.04;
uniform float dim : hint_range(0.0, 1.0) = 0.9;

void fragment() {
	vec4 src = texture(TEXTURE, UV);
	float grey = dot(src.rgb, vec3(0.299, 0.587, 0.114));
	float side = smoothstep(split - feather, split + feather, UV.x);
	float sat = mix(sat_left, sat_right, side);
	vec3 col = mix(vec3(grey), src.rgb, sat);
	COLOR = vec4(col * mix(dim, 1.0, sat), src.a);
}
