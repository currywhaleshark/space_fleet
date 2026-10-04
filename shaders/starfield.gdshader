// 절차적 별 배경 + 태양 원반. 방향 벡터를 3D 격자로 나눠 칸마다 별을 하나씩 둔다.
shader_type sky;

uniform float star_brightness = 2.2;

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

vec3 hash33(vec3 p) {
	p = fract(p * vec3(0.1031, 0.1030, 0.0973));
	p += dot(p, p.yxz + 33.33);
	return fract((p.xxy + p.yxx) * p.zyx);
}

// scale: 격자 밀도, density: 별이 있는 칸 비율, radius: 칸 대비 별 반경
float star_layer(vec3 dir, float scale, float density, float radius) {
	vec3 p = dir * scale;
	vec3 cell = floor(p);
	float result = 0.0;
	for (int x = -1; x <= 1; x++)
	for (int y = -1; y <= 1; y++)
	for (int z = -1; z <= 1; z++) {
		vec3 c = cell + vec3(float(x), float(y), float(z));
		if (hash13(c) > density) {
			continue;
		}
		vec3 star_dir = normalize(c + hash33(c));
		float d = length(dir - star_dir) * scale;
		float mag = pow(hash13(c + 17.0), 6.0);
		result += smoothstep(radius, 0.0, d) * (0.12 + mag * 3.0);
	}
	return result;
}

void sky() {
	vec3 dir = normalize(EYEDIR);
	vec3 col = vec3(0.0);

	// 은하수 띠
	vec3 band_axis = normalize(vec3(0.35, 1.0, 0.25));
	float band = exp(-pow(dot(dir, band_axis) * 4.0, 2.0));
	float band_noise = hash13(floor(dir * 90.0)) * 0.5 + 0.5;
	col += vec3(0.010, 0.012, 0.018) * band * (0.6 + band_noise * 0.4);

	if (!AT_CUBEMAP_PASS) {
		// 3x3x3 이웃을 훑으므로 실제 밀도는 density의 약 3배.
		float s = star_layer(dir, 220.0, 0.004, 0.3) + star_layer(dir, 520.0, 0.0015 + band * 0.006, 0.55) * 0.5;
		vec3 tint = mix(vec3(0.75, 0.85, 1.0), vec3(1.0, 0.85, 0.7), hash13(floor(dir * 220.0)));
		col += tint * s * star_brightness;
	}

	if (LIGHT0_ENABLED) {
		float sd = dot(dir, LIGHT0_DIRECTION);
		col += LIGHT0_COLOR * LIGHT0_ENERGY * (smoothstep(0.99985, 0.99992, sd) * 40.0 + pow(max(sd, 0.0), 900.0) * 1.5);
	}

	COLOR = col;
}
