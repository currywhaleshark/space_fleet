// Laser beam: a quad whose local Y spans muzzle -> hit point. The vertex stage widens it in screen space,
// so the beam keeps the same pixel thickness from the muzzle to a target kilometres away, and the hot core
// ends in a ~1 px antialiased edge instead of a soft radial fade. Endpoints must lie in front of the camera.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec4 core_color : source_color = vec4(1.0, 0.97, 0.92, 1.0);
uniform vec4 halo_color : source_color = vec4(0.25, 0.6, 1.0, 1.0);
uniform float core_px = 1.4;  // core half width at 1080p
uniform float halo_px = 7.0;  // glow half width at 1080p
instance uniform float focus = 0.0;  // channelling amplification 0..1

// Signed distance from the centre line (1080p pixels), carried as (distance * w, w) so the ratio is
// interpolated linearly on screen; plain perspective-correct interpolation skews it along a receding beam.
varying float across_w;
varying float clip_w;

void vertex() {
    float scale = VIEWPORT_SIZE.y / 1080.0;
    float half_width = halo_px * (1.0 + 0.35 * focus) + 1.5;
    mat4 view_projection = PROJECTION_MATRIX * VIEW_MATRIX;
    vec3 centre = (MODEL_MATRIX * vec4(0.0, VERTEX.y, 0.0, 1.0)).xyz;
    vec3 along = (MODEL_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz;
    vec4 clip = view_projection * vec4(centre, 1.0);
    vec4 ahead = view_projection * vec4(centre + along * 0.001, 1.0);
    vec2 half_viewport = VIEWPORT_SIZE * 0.5;
    vec2 direction = (ahead.xy / ahead.w - clip.xy / clip.w) * half_viewport;
    vec2 normal = length(direction) > 1e-6 ? normalize(vec2(-direction.y, direction.x)) : vec2(0.0, 1.0);
    float side = VERTEX.x > 0.0 ? 1.0 : -1.0;
    clip.xy += normal * side * half_width * scale / half_viewport * clip.w;
    POSITION = clip;
    across_w = side * half_width * clip.w;
    clip_w = clip.w;
}

void fragment() {
    float d = abs(across_w / clip_w);
    float core_width = core_px * (1.0 + 0.25 * focus);
    float core = 1.0 - smoothstep(core_width - 0.6, core_width + 0.6, d);
    float halo = pow(clamp(1.0 - d / (halo_px * (1.0 + 0.35 * focus)), 0.0, 1.0), 2.5);
    vec3 colour = core_color.rgb * core + halo_color.rgb * halo * (1.0 - core) * (0.75 + 0.25 * focus);
    ALBEDO = colour;
    EMISSION = colour * 3.0;
    ALPHA = clamp(max(core, halo * 0.85), 0.0, 1.0);
}
