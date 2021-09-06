#version 450
#extension GL_ARB_separate_shader_objects : enable
#extension GL_ARB_shading_language_420pack : enable

layout(push_constant) uniform PushConsts {
	float exposure;
	float gamma;	
};

vec3 Uncharted2Tonemap(vec3 color)
{
    const float A = 0.15;
    const float B = 0.50;
    const float C = 0.10;
    const float D = 0.20;
    const float E = 0.02;
    const float F = 0.30;
    const float W = 11.2;
    return ((color*(A*color+C*B)+D*E)/(color*(A*color+B)+D*F))-E/F;
}

vec4 tonemap(vec4 color, float exposure, float gamma)
{
    vec3 outcol = Uncharted2Tonemap(color.rgb * exposure);
    outcol = outcol * (1.0f / Uncharted2Tonemap(vec3(11.2f)));  
    return vec4(pow(outcol, vec3(1.0f / gamma)), color.a);
}

vec4 SRGBtoLINEAR(vec4 srgbIn)
{
    #ifdef MANUAL_SRGB
    #ifdef SRGB_FAST_APPROXIMATION
    vec3 linOut = pow(srgbIn.xyz,vec3(2.2));
    #else //SRGB_FAST_APPROXIMATION
    vec3 bLess = step(vec3(0.04045),srgbIn.xyz);
    vec3 linOut = mix( srgbIn.xyz/vec3(12.92), pow((srgbIn.xyz+vec3(0.055))/vec3(1.055),vec3(2.4)), bLess );
    #endif //SRGB_FAST_APPROXIMATION
    return vec4(linOut,srgbIn.w);;
    #else //MANUAL_SRGB
    return srgbIn;
    #endif //MANUAL_SRGB
}

layout (set = 0, binding = 0) uniform sampler2D uiImage;
layout (set = 0, binding = 1) uniform sampler2D samplerHDR;

layout (location = 0) in vec2 inUV;
layout (location = 0) out vec4 outColor;
									                                  
void main() 
{
		//ivec2 ts = textureSize(samplerHDR);
	    //vec4 hdrColor = texelFetch (samplerHDR, ivec2(gl_FragCoord.xy), gl_SampleID);
        
	    //vec4 hdrColor = texelFetch (samplerHDR, inUV);    
	    //vec4 c = texture (bloom, inUV);
	    //float lum = (0.299*c.r + 0.587*c.g + 0.114*c.b);
	    //if (lum>1.0)
	    //    hdrColor.rgb += c.rgb * 0.05;
	    //outColor = SRGBtoLINEAR(tonemap(hdrColor, exposure, gamma));
        vec4 hdrColor = texture(samplerHDR, inUV);
        vec4 uiColor = texture(uiImage, inUV);
	    outColor = tonemap(hdrColor, exposure, gamma);	        
        outColor = vec4(outColor.rgb * (1 - uiColor.a) + uiColor.rgb * uiColor.a, 1);
    
    /*
    outColor = vec4(SRGBtoLINEAR(tonemap(hdrColor.rgb)), hdrColor.a);;*/
    
/*  vec3 mapped = vec3(1.0) - exp(-hdrColor.rgb * pc.exposure);        
    mapped = pow(mapped, vec3(1.0 / pc.gamma));
    outColor = vec4(mapped, hdrColor.a);*/
}
