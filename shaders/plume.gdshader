// 엔진 화염: 노즐 쪽이 밝고 끝으로 갈수록 사라지는 가산 원뿔.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec3 color : source_color = vec3(0.45, 0.68, 1.0);
uniform float intensity = 1.1;
// 원뿔 메시 높이의 절반(m). 메시는 -half_length(노즐)..+half_length(끝).
uniform float half_length = 1.0;

varying float along;

void vertex() {
	along = clamp(0.5 - VERTEX.y / (2.0 * half_length), 0.0, 1.0);
}

void fragment() {
	float facing = abs(dot(NORMAL, VIEW));
	// 축 방향으로 정면에서 볼 때 앞뒤 두 겹이 겹쳐 포화되지 않도록 정면 가중을 낮게 둔다.
	float a = pow(along, 2.5) * (0.25 + 0.35 * facing);
	ALBEDO = color * intensity * a;
}
