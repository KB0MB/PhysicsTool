// Alpha test (gsys_alpha_test_enable): the transparency source (o_transparency_color / _channel) against Mat.gsys_alpha_test_ref_value
#ifndef TOTK_DISCARD_GLSL
#define TOTK_DISCARD_GLSL

// gsys_alpha_test_func: 0 never, 1 less, 2 equal, 3 lequal, 4 greater, 5 notequal, 6 gequal, 7 always (pass condition)
bool AlphaTestPass(float a, float ref)
{
    switch (gsys_alpha_test_func)
    {
        case 0: return false;
        case 1: return a < ref;
        case 2: return a == ref;
        case 3: return a <= ref;
        case 4: return a > ref;
        case 5: return a != ref;
        case 6: return !(a < ref);
    }
    return true;
}

float Transparency() { return GetSourceChannel(o_transparency_color, o_transparency_channel).x; }

void AlphaTest()
{
#if gsys_alpha_test_enable
    if (!AlphaTestPass(Transparency(), Mat.gsys_alpha_test_ref_value))
        discard;
#endif
}

#endif
