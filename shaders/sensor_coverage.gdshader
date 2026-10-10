shader_type canvas_item;
render_mode unshaded;

// One composite for the whole network: overlapping sensors never stack opacity.
uniform int volume_count = 0;
uniform vec4 volumes[64]; // map-view xyz, nominal forward radius
uniform vec4 directions[64]; // map-view forward, 0 ship / 1 probe / 2 preview / 3 unavailable preview
uniform vec2 map_scale = vec2(2.0);

vec3 tint(float kind) {
    if (kind > 2.5) return vec3(1.0, 0.32, 0.18);
    if (kind > 1.5) return vec3(1.0, 0.7, 0.23);
    if (kind > 0.5) return vec3(0.19, 0.85, 0.65);
    return vec3(0.19, 0.59, 0.95);
}

void fragment() {
    vec2 p = (UV - vec2(0.5)) * map_scale;
    float globe_squared = 1.0 - dot(p, p);
    vec4 result = vec4(0.0);
    if (globe_squared > 0.0) {
        float globe_z = sqrt(globe_squared);
        float pixel = max(fwidth(p.x), fwidth(p.y));
        float coverage = 0.0;
        float top = -10000.0;
        vec3 normal = vec3(0.0);
        vec3 color = vec3(0.0);
        float weights = 0.0;
        float blend_width = max(pixel * 3.0, 0.035);
        for (int i = 0; i < volume_count; i++) {
            vec3 center = volumes[i].xyz;
            float radius = volumes[i].w;
            vec2 offset = p - center.xy;
            float radial = length(offset);
            if (radial > radius + pixel * 2.0) continue;
            float effective = radius;
            // Refine the forward-biased probe surface with the same sensor falloff as the simulation.
            for (int j = 0; j < 4; j++) {
                float height = sqrt(max(0.0, effective * effective - dot(offset, offset)));
                vec3 direction = normalize(vec3(offset, height) + vec3(0.0, 0.0, 0.000001));
                if (directions[i].w > 0.5)
                    effective = radius * sqrt(0.65 + 0.35 * max(0.0, dot(direction, directions[i].xyz)));
            }
            float edge = radial - effective;
            float mask = 1.0 - smoothstep(-pixel * 1.5, pixel * 1.5, edge);
            if (mask <= 0.0) continue;
            float height = sqrt(max(0.0, effective * effective - dot(offset, offset)));
            float front = min(center.z + height, globe_z);
            float back = max(center.z - height, -globe_z);
            if (front < back) continue;
            coverage = max(coverage, mask);
            vec3 n = normalize(vec3(offset, max(height, pixel)));
            if (center.z + height > globe_z) n = normalize(vec3(p, globe_z));
            // Stable front-surface weighting: no interior outlines or dependency on ship iteration order.
            float next_top = max(top, front);
            float old_weight = exp((top - next_top) / blend_width);
            float weight = exp((front - next_top) / blend_width);
            normal = normal * old_weight + n * weight;
            color = color * old_weight + tint(directions[i].w) * weight;
            weights = weights * old_weight + weight;
            top = next_top;
        }
        if (coverage > 0.0) {
            normal = normalize(normal);
            color /= weights;
            vec3 light = normalize(vec3(-0.45, -0.55, 0.85));
            float diffuse = 0.62 + 0.38 * max(0.0, dot(normal, light));
            float rim = pow(1.0 - max(0.0, normal.z), 2.5);
            result = vec4(color * diffuse + vec3(0.08, 0.12, 0.14) * rim, coverage * 0.36);
        }
    }
    COLOR = result;
}
