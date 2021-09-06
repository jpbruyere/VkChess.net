#version 450
#extension GL_ARB_separate_shader_objects : enable
#extension GL_ARB_shading_language_420pack : enable

layout(push_constant) uniform PushConsts {
	float exposure;
	float gamma;
	float step;
	float depthTreshold;
	int maxStep;
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

layout (constant_id = 0) const float NEAR_PLANE = 0.1f;
layout (constant_id = 1) const float FAR_PLANE = 32.0f;

layout (set = 0, binding = 0) uniform sampler2D uiImage;
layout (set = 0, binding = 1) uniform sampler2D samplerHDR;
layout (set = 0, binding = 2) uniform sampler2D gbPos;//gbuffer world pos + linear depth
layout (set = 0, binding = 3) uniform sampler2D gbN_AO;//gbuffer normal

layout (set = 0, binding = 4) uniform UBO {
	mat4 projection;
	mat4 model;
	mat4 view;
	vec4 camPos;    
} ubo;

layout (location = 0) in vec2 inUV;
layout (location = 0) out vec4 outColor;

const float flteps = 0.001;

bool float_equ (float a, float b) {
	return abs(a - b) < flteps;
}

float linearDepth(float depth)
{
    float z = depth * 2.0f - 1.0f; 
    return (2.0f * NEAR_PLANE * FAR_PLANE) / (FAR_PLANE + NEAR_PLANE - z * (FAR_PLANE - NEAR_PLANE));   
}

void main() 
{
		const float reflectionCoef = 0.6;
		//ivec2 ts = textureSize(samplerHDR, 0);
		//vec4 hdrColor = texelFetch (samplerHDR, ivec2(gl_FragCoord.xy), gl_SampleID);
		
		//vec4 hdrColor = texelFetch (samplerHDR, inUV);    
		//vec4 c = texture (bloom, inUV);
		//float lum = (0.299*c.r + 0.587*c.g + 0.114*c.b);
		//if (lum>1.0)
		//    hdrColor.rgb += c.rgb * 0.05;
		//outColor = SRGBtoLINEAR(tonemap(hdrColor, exposure, gamma));
		vec4 hdrColor = texture(samplerHDR, inUV);
		vec4 uiColor = texture(uiImage, inUV);
		vec3 pos = texture(gbPos, inUV).rgb;
		float depth = texture(gbPos, inUV).a;
		vec3 n = texture(gbN_AO, inUV).rgb;

		
		//outColor = vec4 (vec3(linDepth/100.0), 1);
		if (float_equ (n.g, 1.0) && float_equ (pos.y, 0.0)) {
			//reflecting plane
			vec3 v = normalize(pos - ubo.camPos.xyz); // Vector from surface point to camera
			vec3 reflection = normalize(reflect(v, n));

			/*outColor = vec4 (reflection, 1);
			return;*/

			float curLength = step;
			bool hit = false;
			vec2 uv;

			for (int i=0; i<maxStep; i++) {

				vec3 curPos = pos + reflection * curLength;

				vec4 ndc = ubo.projection * ubo.view * vec4(curPos, 1.0);
				uv = vec2 ((ndc.x / ndc.w + 1.0) / 2.0, (ndc.y / ndc.w + 1.0) / 2.0);

				if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1)
					break;

				vec4 cp = texture(gbPos, uv);

				float linDepthCP = linearDepth (ndc.z / ndc.w);
				//outColor = vec4 (vec3 (linDepthCP/100.0), 1);
				/*outColor = vec4 (1,0,0, 1);
				return;*/

				if (abs(linDepthCP - cp.a) < depthTreshold) {
					hit = true;
					break;
				}

				curLength += step;
			}
			

			if (hit)
				hdrColor = mix (hdrColor, texture(samplerHDR, uv), reflectionCoef);
				//hdrColor = vec4 (1,0,0,1);// texture(samplerHDR, uv);
			
		}

		outColor = tonemap(hdrColor, exposure, gamma);
		outColor = vec4(outColor.rgb * (1 - uiColor.a) + uiColor.rgb * uiColor.a, 1);

		//outColor = vec4 (vec3 (depth/100.0), 1);
	
	/*
	outColor = vec4(SRGBtoLINEAR(tonemap(hdrColor.rgb)), hdrColor.a);;*/
	
/*  vec3 mapped = vec3(1.0) - exp(-hdrColor.rgb * pc.exposure);        
	mapped = pow(mapped, vec3(1.0 / pc.gamma));
	outColor = vec4(mapped, hdrColor.a);*/
}
