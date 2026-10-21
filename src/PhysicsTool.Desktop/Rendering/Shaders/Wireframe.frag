#version 330 core
uniform vec3 uWireColor;
out vec4 result;

void main()
{
    result = vec4(uWireColor, 1.0);
}
