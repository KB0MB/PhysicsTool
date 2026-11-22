// Discards shared by the z-only / gbuffer passes
#ifndef UKING_DISCARD_GLSL
#define UKING_DISCARD_GLSL

// gsys_alpha_test_func: 0 never, 1 less, 2 equal, 3 lequal, 4 greater, 5 notequal, 6 gequal, 7 always (BOTW only uses 6)
void AlphaTest(float a)
{
#if gsys_alpha_test_enable
    float ref = mat.gsys_alpha_test_ref_value;
    bool pass = true;
  #if gsys_alpha_test_func == 0
    pass = false;
  #elif gsys_alpha_test_func == 1
    pass = a < ref;
  #elif gsys_alpha_test_func == 2
    pass = a == ref;
  #elif gsys_alpha_test_func == 3
    pass = a <= ref;
  #elif gsys_alpha_test_func == 4
    pass = a > ref;
  #elif gsys_alpha_test_func == 5
    pass = a != ref;
  #elif gsys_alpha_test_func == 6
    pass = !(a < ref);
  #endif
    if (!pass) discard;
#endif
}

// uk_proc_discard < 1: object dissolves with 3D noise in object space, >= 1: screen-door dither by uk_shadow_alpha
void ProcDiscard(vec3 localPos)
{
    if (mat.uk_proc_discard < 1.0)
    {
        vec3 p = localPos * mat.uking_edit_proc_discard_scale;
        float warp = texture(cProc3DNoise, p * sceneMat.uking_const_proc_discard_scale2).x * sceneMat.uking_const_proc_discard_scale3;
        float n = texture(cProc3DNoise, vec3(p.x * 0.5, p.y * sceneMat.uking_const_proc_discard_scale1, fma(p.z, 0.5, warp))).x;
        if (n < Saturate((0.850000024 - mat.uk_proc_discard) * 1.17647052)) discard;
    }
    if (mat.uk_proc_discard >= 1.0)
    {
        vec2 cell = gl_FragCoord.xy * 0.25;
        int levels = int(trunc(fma(Saturate(mat.uk_shadow_alpha + mat.uk_mimicly_ratio), 7.0, 1.0)));
        int idx = int(trunc(fma(fract(cell.x), 4.0, fract(cell.y) * 4.0)));
        if (idx % levels == 0) discard;
    }
}

// uk_user_data / uk_user_data1: two 16-bit values each, unpacked to [0,1]
vec4 UnpackUserData()
{
    uint a = floatBitsToUint(mat.uk_user_data1), b = floatBitsToUint(mat.uk_user_data);
    return vec4(float(a >> 16u), float(a & 0xFFFFu), float(b >> 16u), float(b & 0xFFFFu)) * 1.52590219E-05;
}
#endif
