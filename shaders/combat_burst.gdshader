shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

// Original Space Fleet material; Carbon ships effect orchestration, not these textures.
uniform vec4 tint : source_color = vec4(1.0, 0.48, 0.12, 1.0);
uniform float age = 0.0;
uniform float seed = 0.0;
uniform float strength = 1.0;
uniform float electric = 0.0;
// 1이면 멀리서 본 격침: 화면 픽셀 단위의 작고 선명한 점광(약 3px 심, 1px 십자 광선, 얇은 고리).
// pixels = 사각형의 화면 크기(px). 흐린 불덩이를 키워 보이게 하는 대신 작아도 또렷하게 그린다.
uniform float point = 0.0;
uniform float pixels = 64.0;

// rgb = 색, a = 가산 세기.
vec4 point_flash(vec2 p, float t) {
    vec2 q=p*pixels*0.5;
    float d=length(q);
    float spread=8.0+18.0*t;
    float core=exp(-d*d/4.0)*(1.0-0.4*t)*4.5;
    float hot=exp(-d*d/30.0)*exp(-t*2.0)*1.2;
    float rays=(exp(-q.y*q.y*2.0)*exp(-abs(q.x)/spread)+exp(-q.x*q.x*2.0)*exp(-abs(q.y)/spread))*exp(-t*2.5)*1.4;
    float ring_radius=2.0+pixels*0.32*sqrt(t);
    float ring=exp(-pow((d-ring_radius)/1.1,2.0))*pow(1.0-t,1.5)*1.1;
    float intensity=(core+hot+rays+ring)*strength*smoothstep(pixels*0.5,pixels*0.42,d);
    vec3 color=mix(tint.rgb,vec3(1.0,0.97,0.9),clamp(core*0.6+rays*0.3,0.0,1.0));
    return vec4(color,intensity);
}

void fragment() {
    vec2 p=UV*2.0-1.0;
    float t=clamp(age,0.0,1.0);
    if (point > 0.5) {
        vec4 flash=point_flash(p,t);
        ALBEDO=flash.rgb;
        EMISSION=flash.rgb*2.0;
        ALPHA=clamp(flash.a*tint.a,0.0,1.0);
    } else {
        float r=length(p);
        float a=atan(p.y,p.x);
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
}
