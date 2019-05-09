#version 450

layout (location = 0) in vec3 inPos;
layout (location = 1) in vec3 inNormal;
layout (location = 2) in vec2 inUV0;
layout (location = 3) in vec2 inUV1;
//instanced
layout (location = 4) in vec4 inColor;
layout (location = 5) in mat4 inModel;

layout (set = 0, binding = 0) uniform UBO {
    mat4 projection;
    mat4 model;
    mat4 view;    
} ubo;

layout (location = 0) out vec3 outWorldPos;
layout (location = 1) out vec3 outNormal;
layout (location = 2) out vec2 outUV0;
layout (location = 3) out vec2 outUV1;
layout (location = 4) out vec4 outColor;

out gl_PerVertex
{
    vec4 gl_Position;
};

void main() 
{        
    vec4 locPos = ubo.model * inModel * vec4(inPos, 1.0);
    outNormal = normalize(transpose(inverse(mat3(ubo.model * inModel))) * inNormal);
    outColor = inColor;                
    outWorldPos = locPos.xyz;
    outUV0 = inUV0;
    outUV1 = inUV1;
    gl_Position =  ubo.projection * ubo.view * vec4(outWorldPos, 1.0);
}
