// 함선 선체 표면: 모델 좌표 기준 장갑판 이음새, 판마다 색·거칠기 차이, 낮은 주파수 얼룩, 드문드문 켜진 주황 작업등.
// 텍스처 없이 Blender 재질의 기본색에 얹는다. 함선이 움직여도 무늬는 선체에 붙어 있다(모델 좌표).
// 패널 크기는 함선 길이에 비례해 준다(전함 약 9 m, 요격함 약 0.5 m).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3 base_color : source_color = vec3(0.1, 0.11, 0.13);
uniform float metallic_value = 0.55;
uniform float roughness_value = 0.6;
uniform float panel = 6.0;              // 판 한 장의 기준 크기(m)
uniform float seam = 0.035;             // 이음새 폭(판 크기 대비)
uniform float variation = 0.22;         // 판마다 명암 차이
uniform float grime = 0.22;             // 큰 얼룩 세기
uniform float hatch = 0.35;             // 작은 판 무늬(통풍구 줄) 세기
uniform float light_density = 0.0;      // 작업등이 켜진 판의 비율
uniform vec3 light_color : source_color = vec3(1.0, 0.5, 0.12);
uniform float light_energy = 4.0;
uniform float seed = 0.0;

varying vec3 obj_pos;
varying vec3 obj_nrm;

void vertex() {
    obj_pos = VERTEX;
    obj_nrm = NORMAL;
}

float hash(vec2 p) { return fract(sin(dot(p + seed, vec2(127.1, 311.7))) * 43758.5453); }

float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}

// 한 평면의 장갑판. x = 이음새(1이면 이음새), y = 판 밝기 차이, z = 작업등, w = 판 거칠기 차이.
vec4 plates(vec2 uv, float axis) {
    // 함선 길이 방향(z)으로 긴 판: 판 비율 1.7:1. 줄마다 엇갈려 벽돌처럼.
    vec2 c = uv / vec2(panel * 1.7, panel);
    float row = floor(c.y);
    c.x += hash(vec2(row, axis * 13.0)) * 0.7;
    vec2 id = floor(c);
    vec2 f = fract(c);
    // 판의 절반은 다시 두 장으로 나눈다(크기 섞기).
    float split = step(0.55, hash(id + 3.7 + axis));
    vec2 sid = id;
    if (split > 0.5) { sid += vec2(floor(f.x * 2.0) * 0.5, 0.0); f.x = fract(f.x * 2.0); }
    vec2 scale = vec2(split > 0.5 ? 0.5 : 1.0, 1.0);
    // 이음새: 판 가장자리까지의 거리(판 단위)를 화면 미분으로 부드럽게. 너무 작아지면 사라진다.
    vec2 edge = min(f, 1.0 - f) * scale;
    vec2 aa = fwidth(c) * 1.2;
    float sx = 1.0 - smoothstep(seam - aa.x, seam + aa.x, edge.x);
    float sy = 1.0 - smoothstep(seam - aa.y, seam + aa.y, edge.y);
    float fade = 1.0 - smoothstep(seam * 0.6, seam * 2.5, max(aa.x, aa.y));
    float s = max(sx, sy) * fade;
    float tone = (hash(sid + 1.3) - 0.5) * 2.0;
    // 일부 판에 가로 통풍구 줄.
    float lines = 0.0;
    if (hash(sid + 9.1) < 0.18) {
        float g = fract(f.y * 7.0);
        float ga = fwidth(f.y * 7.0);
        lines = (1.0 - smoothstep(0.35 - ga, 0.35 + ga, abs(g - 0.5))) * step(0.15, f.x) * step(f.x, 0.85) * step(0.2, f.y) * step(f.y, 0.8);
        lines *= 1.0 - smoothstep(0.15, 0.5, ga);
    }
    // 작업등: 판 한쪽 모서리 근처 작은 점.
    float lamp = 0.0;
    if (hash(sid + 5.3) < light_density) {
        vec2 d = (f - vec2(0.12, 0.2)) / scale * vec2(1.7, 1.0);
        float r = length(d);
        lamp = 1.0 - smoothstep(0.035, 0.06, r);
    }
    return vec4(max(s, lines * hatch), tone, lamp, hash(sid + 2.9) - 0.5);
}

void fragment() {
    vec3 n = abs(normalize(obj_nrm));
    vec4 p;
    // 가장 가까운 축 평면 하나만 쓴다(판 모서리가 선명하게 남는다).
    if (n.x >= n.y && n.x >= n.z) p = plates(obj_pos.zy, 1.0);
    else if (n.y >= n.z) p = plates(obj_pos.zx, 2.0);
    else p = plates(obj_pos.xy * vec2(1.0, 1.0), 3.0);

    float dirt = vnoise(obj_pos.zx / (panel * 9.0)) * 0.6 + vnoise(obj_pos.zy / (panel * 3.5)) * 0.4;
    vec3 color = base_color * (1.0 + p.y * variation) * (1.0 - grime * dirt);
    color *= 1.0 - p.x * 0.7;
    ALBEDO = color;
    METALLIC = metallic_value;
    ROUGHNESS = clamp(roughness_value + p.w * 0.16 + p.x * 0.2 + dirt * 0.1, 0.05, 1.0);
    EMISSION = light_color * light_energy * p.z;
}
