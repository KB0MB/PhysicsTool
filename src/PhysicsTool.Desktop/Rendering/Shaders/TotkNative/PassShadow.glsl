// gsys_assign_dynamic1: shadow depth. Alpha test, the dissolve effects, and a screen-door dither for shadow alpha
#include "Effects.glsl"

layout (location = 0) out vec4 oColor;

void main()
{
    AlphaTest();
    EffectDiscard(fLocalPos.xyz);
    if (Mat.p_proc_discard >= 1.0)
    {
        // 4x4 dither: keep 1 / steps of the pattern, steps = 1 + 7 sat(shadow alpha + mimicry)
        int steps = int(trunc(fma(Saturate(Mat.p_shadow_alpha + Mat.p_mimicly_ratio), 7.0, 1.0)));
        int cell = int(trunc((fract(gl_FragCoord.x * 0.25) + fract(gl_FragCoord.y * 0.25)) * 4.0));
        if (RemU(cell, steps) == 0) discard;
    }
    oColor = vec4(1.0, 1.0, 1.0, 0.0);
}
