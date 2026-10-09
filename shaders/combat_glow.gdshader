shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;
uniform vec4 tint : source_color = vec4(0.55,0.85,1.0,1.0);
uniform float strength = 1.0;
uniform bool streak = false;
void fragment() {
    vec2 p=UV*2.0-1.0;
    float r=length(p);
    float a;
    if(streak) {
        a=pow(max(0.0,1.0-abs(p.x)),2.0)*pow(1.0-UV.y,0.65);
    } else {
        float core=exp(-r*r*45.0);
        float halo=exp(-r*r*5.5)*0.35;
        a=(core+halo)*smoothstep(1.0,0.7,r);
    }
    ALBEDO=tint.rgb;
    EMISSION=tint.rgb*3.5;
    ALPHA=clamp(a*strength*tint.a,0.0,1.0);
}
