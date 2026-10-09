shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

// Original Space Fleet material; Carbon ships effect orchestration, not these textures.
uniform vec4 tint : source_color = vec4(1.0, 0.48, 0.12, 1.0);
uniform float age = 0.0;
uniform float seed = 0.0;
uniform float strength = 1.0;
uniform float electric = 0.0;

void fragment() {
    vec2 p=UV*2.0-1.0;
    float r=length(p);
    float a=atan(p.y,p.x);
    float t=clamp(age,0.0,1.0);
    float uneven=1.0+0.065*sin(a*11.0+seed)+0.035*sin(a*23.0-seed*2.0);
    float core=exp(-r*r*mix(45.0,100.0,t))*exp(-t*5.0)*4.0;
    float halo=exp(-r*r*9.0)*exp(-t*5.0)*0.4;
    float ring_radius=0.13+0.67*sqrt(t);
    float ring=exp(-pow((r*uneven-ring_radius)/0.035,2.0))*pow(1.0-t,2.0)*0.5;
    float rays=pow(abs(cos(a*3.0+seed)),28.0)+0.45*pow(abs(sin(a*7.0-seed)),40.0);
    float spikes=rays*exp(-r*4.0)*exp(-t*6.0)*(0.9+electric*1.5);
    float intensity=(core+halo+ring+spikes)*strength*smoothstep(1.0,0.85,r);
    vec3 color=mix(tint.rgb,vec3(0.83,0.94,1.0),clamp(core*0.7,0.0,1.0));
    ALBEDO=color;
    EMISSION=color*4.5;
    ALPHA=clamp(intensity*tint.a,0.0,1.0);
}
