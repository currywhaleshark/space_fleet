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
uniform float power = 1.0;
uniform float wreck_burn = 0.0;
// 시안식 도장(선택). 0이면 기존 모습 그대로. 판 단위로 강조색 블록을 함선 길이 방향 띠처럼 모으고,
// 판 가장자리 도장 벗겨짐과 길이 방향 때 줄무늬를 더한다.
uniform vec3 accent_color : source_color = vec3(0.0);
uniform float accent_ratio = 0.0;       // 강조색 판의 대략적인 비율
uniform float accent_scale = 4.0;       // 띠 하나의 길이(판 개수)
uniform float wear = 0.0;               // 가장자리 도장 벗겨짐 세기
uniform float streaks = 0.0;            // 길이 방향 때 줄무늬 세기

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
vec4 plates(vec2 uv, float axis, out float accent, out float rim) {
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
    // u축이 대부분 함선 길이 방향이므로, 이웃한 판끼리 같은 띠에 묶여 측면과 갑판이 이어진다.
    float band = vnoise(vec2(sid.x / accent_scale, axis * 0.37));
    accent = step(1.0 - accent_ratio, band * 0.85 + hash(sid + 7.7) * 0.15) * step(0.001, accent_ratio);
    rim = 1.0 - smoothstep(0.0, 0.09, min(edge.x, edge.y));
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
    float accent, rim;
    // 가장 가까운 축 평면 하나만 쓴다(판 모서리가 선명하게 남는다).
    if (n.x >= n.y && n.x >= n.z) p = plates(obj_pos.zy, 1.0, accent, rim);
    else if (n.y >= n.z) p = plates(obj_pos.zx, 2.0, accent, rim);
    else p = plates(obj_pos.xy * vec2(1.0, 1.0), 3.0, accent, rim);

    float dirt = vnoise(obj_pos.zx / (panel * 9.0)) * 0.6 + vnoise(obj_pos.zy / (panel * 3.5)) * 0.4;
    vec3 paint = mix(base_color, accent_color, accent);
    vec3 color = paint * (1.0 + p.y * variation) * (1.0 - grime * dirt);
    // 길이 방향으로 흐른 때: 가는 세로 줄(z 방향으로 늘어진 잡음).
    float streak = vnoise(vec2(obj_pos.z / (panel * 2.5), (obj_pos.x + obj_pos.y) / (panel * 0.18)));
    color *= 1.0 - streaks * smoothstep(0.45, 0.95, streak) * 0.55;
    // 판 가장자리의 벗겨진 도장: 바탕 금속이 드러나 조금 밝고 매끈하다.
    float chips = rim * step(0.7, vnoise(obj_pos.zy * (4.0 / panel) + obj_pos.x * (3.0 / panel))) * wear;
    color = mix(color, base_color * 0.55 + vec3(0.09), chips);
    color *= 1.0 - p.x * 0.7;
    float char_pattern = mix(0.38, 0.65, vnoise(obj_pos.zy / (panel * 2.3)));
    ALBEDO = mix(color, color * char_pattern, wreck_burn);
    METALLIC = metallic_value;
    ROUGHNESS = clamp(roughness_value + p.w * 0.16 + p.x * 0.2 + dirt * 0.1 + wreck_burn * 0.3 - chips * 0.2, 0.05, 1.0);
    EMISSION = light_color * light_energy * p.z * power * (1.0 - wreck_burn);
}
