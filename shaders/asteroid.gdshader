// Unit-scale object coordinates keep the surface attached to each instanced rock.
// Ship readability lights are excluded by BattleLook.AsteroidLayer. The real sun
// still supplies directional lighting; no camera-relative or hard-coded sun vector.
shader_type spatial;
render_mode diffuse_burley, specular_disabled, ambient_light_disabled;

uniform vec3 dark_rock : source_color = vec3(0.16, 0.155, 0.145);
uniform vec3 light_rock : source_color = vec3(0.38, 0.355, 0.31);
// A very faint indirect-light floor, not an emissive outline or bloom source.
uniform float night_irradiance : hint_range(0.0, 0.03) = 0.008;
uniform float relief_strength : hint_range(0.0, 2.0) = 0.55;

varying vec3 rock_pos;
varying vec3 rock_view_x;
varying vec3 rock_view_y;
varying vec3 rock_view_z;

float hash(vec3 p) {
    // Integer hashing avoids sin/fract precision artifacts on large noise cells.
    uvec3 q = uvec3(ivec3(p)) * uvec3(1597334677u, 3812015801u, 2798796415u);
    uint h = q.x ^ q.y ^ q.z;
    h = (h ^ (h >> 16u)) * 2246822519u;
    h = (h ^ (h >> 13u)) * 3266489917u;
    return float((h ^ (h >> 16u)) & 0x00ffffffu) / 16777216.0;
}
// xyz = analytic gradient, w = value. Reuse the same eight corners for both.
vec4 rock_noise(vec3 p) {
    vec3 i = floor(p), f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f), du = 6.0 * f * (1.0 - f);
    float a = hash(i), b = hash(i + vec3(1,0,0));
    float c = hash(i + vec3(0,1,0)), d = hash(i + vec3(1,1,0));
    float e = hash(i + vec3(0,0,1)), f1 = hash(i + vec3(1,0,1));
    float g = hash(i + vec3(0,1,1)), h = hash(i + vec3(1,1,1));
    vec4 x = vec4(mix(a,b,u.x), mix(c,d,u.x), mix(e,f1,u.x), mix(g,h,u.x));
    vec2 y = vec2(mix(x.x,x.y,u.y), mix(x.z,x.w,u.y));
    vec3 gradient = vec3(mix(mix(b-a,d-c,u.y),mix(f1-e,h-g,u.y),u.z),
                         mix(x.y-x.x,x.w-x.z,u.z), y.y-y.x) * du;
    return vec4(gradient, mix(y.x,y.y,u.z));
}

void vertex() {
    rock_pos = VERTEX;
    // Uniformly scaled instances: retain rotation only for the relief gradient.
    rock_view_x = normalize(MODELVIEW_MATRIX[0].xyz);
    rock_view_y = normalize(MODELVIEW_MATRIX[1].xyz);
    rock_view_z = normalize(MODELVIEW_MATRIX[2].xyz);
}

void fragment() {
    float footprint = max(length(dFdx(rock_pos)), length(dFdy(rock_pos)));
    vec4 coarse = mix(rock_noise(rock_pos * 14.0), vec4(0,0,0,0.5), smoothstep(0.12, 0.65, footprint * 14.0));
    vec4 medium = mix(rock_noise(rock_pos * 43.0), vec4(0,0,0,0.5), smoothstep(0.12, 0.65, footprint * 43.0));
    vec4 fine = mix(rock_noise(rock_pos * 131.0), vec4(0,0,0,0.5), smoothstep(0.12, 0.65, footprint * 131.0));
    vec4 cracks = rock_noise(rock_pos * 8.0);
    float t = clamp((abs(cracks.w - 0.5) - 0.035) / 0.125, 0.0, 1.0);
    float crack_detail = 1.0 - smoothstep(0.12, 0.65, footprint * 8.0);
    float seam = mix(0.7, t * t * (3.0 - 2.0 * t), crack_detail);
    vec3 seam_gradient = cracks.xyz * (8.0 * sign(cracks.w - 0.5) * 6.0 * t * (1.0 - t) / 0.125 * crack_detail);
    float grain = coarse.w * 0.50 + medium.w * 0.30 + fine.w * 0.20;
    ALBEDO = mix(dark_rock, light_rock, grain) * mix(0.55, 1.0, seam);

    // Analytic slopes avoid quad/triangle seams in screen-derived bump normals.
    // Subpixel detail fades rather than sparkling at range; silhouette is unchanged.
    vec3 gradient = relief_strength * (medium.xyz * (43.0 * 0.009) + fine.xyz * (131.0 * 0.003) + seam_gradient * 0.006);
    vec3 n = normalize(NORMAL);
    vec3 slope = mat3(rock_view_x, rock_view_y, rock_view_z) * gradient;
    slope -= n * dot(n, slope);
    slope /= max(1.0, length(slope) * 2.5);
    NORMAL = normalize(n - slope);
    ROUGHNESS = 1.0;
    SPECULAR = 0.0;
    EMISSION = ALBEDO * vec3(0.8, 0.9, 1.0) * night_irradiance;
}
