// 엔진 화염: 노즐 쪽이 밝고 끝으로 갈수록 사라지는 가산 원뿔.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec3 color : source_color = vec3(0.45, 0.68, 1.0);
uniform float intensity = 1.1;
// 원뿔 메시 높이의 절반(m). 메시는 -half_length(노즐)..+half_length(끝).
uniform float half_length = 1.0;
// 함선별 밝기(ShipView.EngineVisibility). 화면에서 작은 함선은 화면 공간 글로가 고정 크기 번짐을 만들지 않게 줄인다.
// 팔레트는 함선마다 따로 만들어지므로 재질 파라미터로 둔다.
uniform float visibility = 1.0;

varying float along;

void vertex() {
	along = clamp(0.5 - VERTEX.y / (2.0 * half_length), 0.0, 1.0);
}

void fragment() {
	// 몇몇 화소에서 법선·시선 벡터가 NaN이 되었다(원뿔 끝처럼 길이가 0인 벡터의 정규화).
	// NaN 화소는 화면 공간 글로를 거쳐 거리와 무관한 같은 크기의 흰 번쩍임으로 번졌으므로,
	// 법선 대신 원뿔 축과 직접 계산한 시선만 쓰고, 마지막 값도 유한할 때만 낸다.
	vec3 to_eye = -VERTEX;
	to_eye *= inversesqrt(max(dot(to_eye, to_eye), 1e-12));
	vec3 axis = (VIEW_MATRIX * vec4(MODEL_MATRIX[1].xyz, 0.0)).xyz;
	axis *= inversesqrt(max(dot(axis, axis), 1e-12));
	float head_on = clamp(abs(dot(axis, to_eye)), 0.0, 1.0);
	float facing = sqrt(1.0 - head_on * head_on);
	// 축 방향으로 정면에서 볼 때 앞뒤 두 겹이 겹쳐 포화되지 않도록 정면 가중을 낮게 둔다.
	float a = pow(along, 2.5) * (0.25 + 0.35 * facing);
	// 분사 축을 똑바로 바라보면 원뿔 전체가 작은 원에 겹쳐 하얀 공으로 번진다. 그 각도에서 어둡게 한다.
	a *= 1.0 - 0.85 * pow(head_on, 4.0);
	a = (isnan(a) || isinf(a)) ? 0.0 : a;
	ALBEDO = color * intensity * a * visibility;
}
