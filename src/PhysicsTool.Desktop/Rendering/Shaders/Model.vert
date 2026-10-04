#version 330 core
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aAlbedoUv;
layout(location = 3) in vec2 aSpmUv;
layout(location = 4) in vec2 aNormalUv;
layout(location = 5) in vec2 aAmbientOcclusionUv;
layout(location = 6) in vec2 aAuxiliaryUv;
layout(location = 7) in vec4 aTangent;
uniform mat4 uMvp;
uniform mat4 uModel;
out vec3 vNormal;
out vec3 vTangent;
out float vTangentSign;
out vec3 vWorldPosition;
out vec2 vAlbedoUv;
out vec2 vSpmUv;
out vec2 vNormalUv;
out vec2 vAmbientOcclusionUv;
out vec2 vAuxiliaryUv;
void main()
{
    gl_Position = uMvp * vec4(aPosition, 1.0);
    mat3 normalMatrix = mat3(transpose(inverse(uModel)));
    vNormal = normalize(normalMatrix * aNormal);
    vTangent = normalize(normalMatrix * aTangent.xyz);
    vTangentSign = aTangent.w * sign(determinant(mat3(uModel)));
    vWorldPosition = (uModel * vec4(aPosition, 1.0)).xyz;
    vAlbedoUv = aAlbedoUv;
    vSpmUv = aSpmUv;
    vNormalUv = aNormalUv;
    vAmbientOcclusionUv = aAmbientOcclusionUv;
    vAuxiliaryUv = aAuxiliaryUv;
}