#version 450

layout (location = 0) in vec3 inPos;
layout (location = 1) in mat4 inModel;

void main()
{
	gl_Position = inModel * vec4(inPos,1);
}