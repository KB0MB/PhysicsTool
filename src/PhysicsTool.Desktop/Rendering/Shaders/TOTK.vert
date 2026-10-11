#version 330 core
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aAlbedoUv;
layout(location = 3) in vec2 aSpmUv;
layout(location = 4) in vec2 aNormalUv;
layout(location = 5) in vec2 aAmbientOcclusionUv;
layout(location = 7) in vec4 aTangent;
layout(location = 8) in vec4 aVertexColor;
uniform mat4 uMvp;
out vec2 f_raw_texcoord0;
out vec2 f_raw_texcoord1;
out vec2 f_raw_texcoord2;
out vec2 f_raw_texcoord3;
out vec3 normal;
out vec3 tangent;
out vec3 bitangent;
out vec4 vertexColor;
void main()
{
    gl_Position = uMvp * vec4(aPosition, 1.0);
    normal = normalize(aNormal);
    tangent = normalize(aTangent.xyz);
    bitangent = cross(normal, tangent) * aTangent.w;
    f_raw_texcoord0 = aAlbedoUv;
    f_raw_texcoord1 = aSpmUv;
    f_raw_texcoord2 = aNormalUv;
    f_raw_texcoord3 = aAmbientOcclusionUv;
    vertexColor = aVertexColor;
}
