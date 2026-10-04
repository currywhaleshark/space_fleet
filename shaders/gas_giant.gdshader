// 배경 가스 행성: 위도 띠 + 노이즈 난류 + 대기 림.
shader_type spatial;
render_mode cull_back;

varying vec3 local_dir;

float hash(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(mix(hash(i + vec3(0, 0, 0)), hash(i + vec3(1, 0, 0)), f.x),
			mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x),
			mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float fbm(vec3 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 5; i++) {
		v += a * noise(p);
		p *= 2.03;
		a *= 0.5;
	}
	return v;
}

void vertex() {
	local_dir = normalize(VERTEX);
}

void fragment() {
	vec3 n = normalize(local_dir);
	float turb = fbm(n * 5.0 + vec3(0.0, 3.1, 0.0)) - 0.5;
	float lat = n.y + turb * 0.12;
	float bands = sin(lat * 38.0) * 0.5 + 0.5;
	float wide = sin(lat * 9.0 + 1.3) * 0.5 + 0.5;

	vec3 pale = vec3(0.86, 0.78, 0.64);
	vec3 tan = vec3(0.66, 0.50, 0.36);
	vec3 rust = vec3(0.50, 0.32, 0.22);
	vec3 col = mix(tan, pale, bands);
	col = mix(col, rust, smoothstep(0.65, 1.0, wide) * 0.6);
	col *= 0.85 + fbm(n * 22.0) * 0.3;

	ALBEDO = col;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;

	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
	EMISSION = vec3(0.35, 0.5, 0.9) * rim * 0.08;
}
