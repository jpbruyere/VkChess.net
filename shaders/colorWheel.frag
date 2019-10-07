#version 450

#extension GL_ARB_separate_shader_objects : enable
#extension GL_ARB_shading_language_420pack : enable

layout (location = 0) in vec2 inUV;
layout (location = 0) out vec4 outFragColor;

#define PI 3.14159265359
#define TWO_PI 6.28318530718

layout(push_constant) uniform PushConsts {
    vec2 resolution;
    float innerRadius;
    float outerRadius;
};

//  Function from Iñigo Quiles
//  https://www.shadertoy.com/view/MsS3Wc
vec3 hsb2rgb( in vec3 c ){
    vec3 rgb = clamp(abs(mod(c.x*6.0+vec3(0.0,4.0,2.0),
                             6.0)-3.0)-1.0,
                     0.0,
                     1.0 );
    rgb = rgb*rgb*(3.0-2.0*rgb);
    return c.z * mix( vec3(1.0), rgb, c.y);
}

float circle(vec2 s, float rad) {
    // This calculation will make an oval at full screen size,
    // so I'm cheating and making the canvas a perfect square.
    vec2 dist = s - vec2(0.5);
    return 1.0 - smoothstep(rad - (rad * 0.01), rad + (rad * 0.01), dot(dist, dist) * 8.0);
}
void main(){
    vec2 st = gl_FragCoord.xy/resolution;
    vec3 color = vec3(0.0);

    // Use polar coordinates instead of cartesian
    vec2 toCenter = vec2(0.5)-st;
    float angle = atan(toCenter.y,toCenter.x) + PI / 2.0;
    float radius = length(toCenter)*2.0;

    // Map the angle (-PI to PI) to the Hue (from 0 to 1)
    // and the Saturation to the radius
    color = hsb2rgb(vec3((angle/TWO_PI)+0.5,1.0,1.0));    

    // Create the inner and outer circles.
    vec4 outerCircle = vec4(circle(st, outerRadius));
    vec4 innerCircle = vec4(circle(st, innerRadius));    
	
    outFragColor = outerCircle * vec4(color,1.0) - innerCircle;
}