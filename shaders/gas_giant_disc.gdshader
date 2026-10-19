// 배경 가스 행성을 화면상 원판으로 그린다.
// 넓은 시야각의 직선 투영은 화면 중심에서 θ만큼 떨어진 구를 바깥쪽으로 1/cos θ배 늘린다(70° 세로 시야, 16:9 끝에서 약 1.6배).
// 그래서 행성이 화면 중앙에 있을 때 보이는 모습(같은 시각 크기, 같은 조명 방향)을 그 자리에 그대로 옮겨 그린다.
// - 메시: 2×2 사각형(QuadMesh). 노드의 위치·배율은 PlaceBackdrop이 정한 그대로 쓴다.
// - 깊이: 행성 중심의 실제 깊이. 앞을 지나가는 함선은 지금처럼 행성을 가린다.
// - 표면: 가상 카메라가 행성을 정면으로 볼 때의 광선-구 교차로 법선을 구해 행성 자체 좌표의 무늬를 그린다.
shader_type spatial;
render_mode skip_vertex_transform, cull_disabled, depth_draw_opaque, specular_disabled;

#include "res://shaders/gas_giant_surface.gdshaderinc"

uniform float radius = 1.0; // 노드 배율 적용 전 행성 반지름(m)

varying vec2 disc;          // 원판 좌표, 단위 원 = 행성 실루엣
varying vec3 center_view;   // 행성 중심(뷰 공간)
varying vec3 axis_right;    // 가상 정면 카메라의 오른쪽·위쪽·전방(뷰 공간)
varying vec3 axis_up;
varying vec3 axis_forward;
varying float tan_radius;   // 시각 반지름의 탄젠트
varying float sphere_radius;

void vertex() {
	vec3 c = (MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float r = radius * length(MODEL_MATRIX[0].xyz);
	float dist = length(c);
	vec4 clip = PROJECTION_MATRIX * vec4(c, 1.0);
	// 가장자리 안티에일리어싱용으로 1~2픽셀 여유를 둔다.
	disc = VERTEX.xy * 1.03;
	center_view = c;
	sphere_radius = r;
	if (dist <= r * 1.0001 || clip.w <= 0.0) {
		// 카메라가 행성 안이거나 행성이 카메라 뒤: 그리지 않는다.
		POSITION = vec4(2.0, 2.0, 2.0, 1.0);
		tan_radius = 0.0;
		axis_right = vec3(1.0, 0.0, 0.0); axis_up = vec3(0.0, 1.0, 0.0); axis_forward = vec3(0.0, 0.0, -1.0);
	} else {
		vec3 d = c / dist;
		// 카메라 위쪽을 기준으로 정면 카메라 축을 만든다. 화면의 가로·세로와 원판 축이 맞는다.
		vec3 up_hint = abs(d.y) > 0.999 ? vec3(0.0, 0.0, -1.0) : vec3(0.0, 1.0, 0.0);
		vec3 up = normalize(up_hint - d * dot(up_hint, d));
		vec3 right = cross(up, -d);
		float t = r / sqrt(dist * dist - r * r);
		axis_right = right; axis_up = up; axis_forward = d; tan_radius = t;
		vec2 ndc = clip.xy / clip.w;
		POSITION = vec4(ndc + disc * vec2(PROJECTION_MATRIX[0][0], PROJECTION_MATRIX[1][1]) * t, clip.z / clip.w, 1.0);
		VERTEX = c - d * r + (right * disc.x + up * disc.y) * r;
		NORMAL = -d;
	}
}

void fragment() {
	float edge = length(disc);
	float coverage = clamp((1.0 - edge) / max(fwidth(edge), 1e-4), 0.0, 1.0);
	if (coverage <= 0.0 || tan_radius <= 0.0) discard;
	// 가상 정면 카메라에서 이 픽셀로 가는 광선과 구의 교차점.
	vec3 ray = normalize(axis_forward + (axis_right * disc.x + axis_up * disc.y) * tan_radius);
	float b = dot(ray, center_view);
	float h = max(b * b - (dot(center_view, center_view) - sphere_radius * sphere_radius), 0.0);
	vec3 hit = ray * (b - sqrt(h));
	vec3 n = normalize(hit - center_view);
	vec3 n_world = (INV_VIEW_MATRIX * vec4(n, 0.0)).xyz;
	vec3 n_local = normalize(inverse(mat3(MODEL_MATRIX)) * n_world);

	ALBEDO = gas_giant_albedo(n_local);
	NORMAL = n;
	ROUGHNESS = 1.0;
	EMISSION = gas_giant_rim(dot(n, -ray));
	ALPHA = coverage;
}
