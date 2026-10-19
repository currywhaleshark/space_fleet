// 디코이: 미사일 탐색기를 현혹하는 밝은 노란 섬광(마그네슘 조명탄처럼 타며 깜빡인다).
// 카메라를 향한 사각형에 그린다. 흰 심 + 노란 광륜 + 짧은 십자 광선, 남은 수명만큼 밝다.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform float life = 1.0;   // 남은 수명 비율(1 → 0)
uniform float seed = 0.0;
uniform float time = 0.0;   // 시뮬레이션 시각(초)

void fragment() {
    vec2 p = UV * 2.0 - 1.0;
    float r = length(p);
    // 타는 조명탄의 불규칙한 깜빡임(0.7~1.15).
    float flicker = 0.92 + 0.14 * sin(time * 37.0 + seed) * sin(time * 23.0 + seed * 1.7) + 0.08 * sin(time * 71.0 + seed * 3.1);
    float core = exp(-r * r * 40.0) * 2.6;
    float glow = exp(-r * r * 6.0) * 0.85;
    float rays = (exp(-abs(p.y) * 38.0) * exp(-abs(p.x) * 3.2) + exp(-abs(p.x) * 38.0) * exp(-abs(p.y) * 3.2)) * 0.85;
    float fade = smoothstep(0.0, 0.15, life) * (0.45 + 0.55 * life);
    float intensity = (core + glow + rays) * flicker * fade * smoothstep(1.0, 0.8, r);
    vec3 color = mix(vec3(1.0, 0.72, 0.18), vec3(1.0, 0.97, 0.82), clamp(core * 0.5, 0.0, 1.0));
    ALBEDO = color;
    EMISSION = color * 1.4;
    ALPHA = clamp(intensity, 0.0, 1.0);
}
