shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;
uniform vec3 hit_direction=vec3(0.0,0.0,-1.0);
uniform vec4 tint : source_color=vec4(0.25,0.65,1.0,1.0);
uniform float age=0.0;
uniform float strength=1.0;
uniform int effect=0; // impact, collapse, recovery
varying vec3 skin_direction;
void vertex() { skin_direction=normalize(VERTEX); }
void fragment() {
    vec3 n=normalize(skin_direction);
    float angle=acos(clamp(dot(n,normalize(hit_direction)),-1.0,1.0));
    float noise=sin(n.x*41.0+n.y*27.0+age*19.0)*sin(n.z*33.0-n.y*17.0)*0.028;
    float a=0.0;
    if(effect==1) {
        float front=age/0.72*3.3;
        float edge=abs(angle+noise-front);
        a=exp(-edge*edge*2300.0)+exp(-edge*edge*130.0)*0.2;
        a*=smoothstep(0.0,0.035,age)*(1.0-smoothstep(0.65,0.78,age));
    } else if(effect==2) {
        float edge=abs(angle-age/0.65*3.3);
        a=exp(-edge*edge*1100.0)*0.24*(1.0-smoothstep(0.5,0.7,age));
    } else {
        float width=0.13+strength*0.10;
        float patch=exp(-angle*angle/(width*width))*exp(-age*7.0)*0.38;
        float ring=abs(angle-age*1.5);
        a=patch+exp(-ring*ring*1700.0)*exp(-age*6.0)*0.65;
    }
    if(a<0.004) discard;
    ALBEDO=tint.rgb;
    EMISSION=tint.rgb*2.2;
    ALPHA=clamp(a*strength,0.0,0.85);
}
