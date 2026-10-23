// Target preview: faceted cyan sensor reconstruction (HUD v4). Flat facets come from screen
// derivatives so imported smooth normals still read as the prototype's plated hull.
shader_type spatial;
render_mode unshaded, cull_disabled;

uniform float rim_strength = 0.55;

void fragment() {
	vec3 n = normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
	vec3 view = normalize(-VERTEX);
	if (dot(n, view) < 0.0) n = -n;
	// View-space key light from above/left, as in the prototype's top-lit plating.
	float key = clamp(dot(n, normalize(vec3(-0.3, 0.85, 0.45))), 0.0, 1.0);
	float lum = 15.0 + 105.0 * key;
	vec3 color = vec3(27.0 + lum * 0.47, 52.0 + lum * 0.66, 75.0 + lum * 0.77) / 255.0;
	float rim = pow(1.0 - clamp(dot(n, view), 0.0, 1.0), 3.0);
	// Prototype colours are sRGB; unshaded ALBEDO is linear.
	ALBEDO = pow(color + vec3(0.18, 0.32, 0.38) * rim * rim_strength, vec3(2.2));
}
