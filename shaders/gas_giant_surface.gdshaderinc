// 배경 가스 행성 표면: 위도 띠 + 노이즈 난류. 구 메시와 화면 원판(gas_giant_disc) 둘 다 쓴다.
// n은 행성 자체 좌표의 단위 방향(자전축 = +Y).

float gas_giant_hash(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float gas_giant_noise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(mix(gas_giant_hash(i + vec3(0, 0, 0)), gas_giant_hash(i + vec3(1, 0, 0)), f.x),
			mix(gas_giant_hash(i + vec3(0, 1, 0)), gas_giant_hash(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(gas_giant_hash(i + vec3(0, 0, 1)), gas_giant_hash(i + vec3(1, 0, 1)), f.x),
			mix(gas_giant_hash(i + vec3(0, 1, 1)), gas_giant_hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float gas_giant_fbm(vec3 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 5; i++) {
		v += a * gas_giant_noise(p);
		p *= 2.03;
		a *= 0.5;
	}
	return v;
}

vec3 gas_giant_albedo(vec3 n) {
	float turb = gas_giant_fbm(n * 5.0 + vec3(0.0, 3.1, 0.0)) - 0.5;
	float lat = n.y + turb * 0.12;
	float bands = sin(lat * 38.0) * 0.5 + 0.5;
	float wide = sin(lat * 9.0 + 1.3) * 0.5 + 0.5;

	vec3 pale = vec3(0.86, 0.78, 0.64);
	vec3 tan = vec3(0.66, 0.50, 0.36);
	vec3 rust = vec3(0.50, 0.32, 0.22);
	vec3 col = mix(tan, pale, bands);
	col = mix(col, rust, smoothstep(0.65, 1.0, wide) * 0.6);
	return col * (0.85 + gas_giant_fbm(n * 22.0) * 0.3);
}

// 가장자리 대기 림. facing = dot(표면 법선, 관측자 방향).
vec3 gas_giant_rim(float facing) {
	return vec3(0.35, 0.5, 0.9) * pow(1.0 - clamp(facing, 0.0, 1.0), 4.0) * 0.08;
}
