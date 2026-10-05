// 우주 먼지: 카메라 주변의 가산 줄무늬. 가까운 것과 상자 가장자리의 것은 흐리게 해서 반복 경계가 보이지 않게 한다.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec3 color : source_color = vec3(0.55, 0.62, 0.75);
uniform float intensity = 0.45;
uniform float near_fade = 1.0;
uniform float far_fade = 100.0;

varying float brightness;

void vertex() {
	// INSTANCE_CUSTOM.r: 줄무늬가 길어질수록 어둡게(움직임 흐림의 에너지 보존).
	brightness = INSTANCE_CUSTOM.r;
}

void fragment() {
	float d = length(VERTEX);
	float fade = smoothstep(near_fade, near_fade * 3.0, d) * (1.0 - smoothstep(far_fade * 0.55, far_fade, d));
	ALBEDO = color * intensity * brightness * fade;
}
