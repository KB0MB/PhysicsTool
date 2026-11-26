// gsys_assign_zonly: depth pre-pass. Only the alpha test runs; colour is a constant
layout (location = 0) out vec4 oColor;

void main()
{
    AlphaTest();
    oColor = vec4(1.0, 1.0, 1.0, 0.0);
}
