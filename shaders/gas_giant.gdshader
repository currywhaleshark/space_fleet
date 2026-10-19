// 배경 가스 행성(구 메시): 위도 띠 + 노이즈 난류 + 대기 림. 연출 장면용이며 전투 화면은 gas_giant_disc를 쓴다.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/gas_giant_surface.gdshaderinc"

varying vec3 local_dir;

void vertex() {
	local_dir = normalize(VERTEX);
}

void fragment() {
	ALBEDO = gas_giant_albedo(normalize(local_dir));
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
	EMISSION = gas_giant_rim(dot(NORMAL, VIEW));
}
