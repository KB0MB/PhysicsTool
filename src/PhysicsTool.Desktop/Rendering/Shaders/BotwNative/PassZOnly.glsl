// gsys_assign_zonly: depth / shadow pass. Only the transparency matters (alpha test); rgb is a constant.
//   variation 0: alpha test                      -> rgb 1 (8/255 for uking_inner_material)
//   variation 1: + uk_proc_discard dissolve / dither
//   variation 2: rgb 0, user data in target 1
#ifndef UKING_PASS_ZONLY_GLSL
#define UKING_PASS_ZONLY_GLSL

// view-dependent sources are compile-time constants in this pass (no view position)
void ZOnlySources()
{
    gSourceNdotV = 0.0; gSourceNdotVVertex = 0.0; gSourceDepth = 0.0; gSourceVdotL = 0.0; gSourceNdotL = 0.0;
    gSourceFovRatio = uintBitsToFloat(0xFF800000u);   // 1 / (-0 * fov) = -inf
    gSourceInnerLight = 0.0; gSourceDepthDiff = 0.0;
    gNoVertexColor = true;
}

float PassTransparency()
{
#if gsys_gbuffer_xlu && gsys_renderstate != 3
    return 0.0;                      // translucent gbuffer objects (except render state 3) write no coverage here
#elif gsys_alpha_test_enable || gsys_renderstate == 2 || gsys_renderstate == 3
    return GetSourceChannel(uking_transparency_color, uking_transparency_channel).w;
#else
    return 1.0;
#endif
}

void ZOnlyPass()
{
    ZOnlySources();
    FetchTextures(false);
    EvalCalcColors(1);
    EvalCalcColors(0);
    float a = PassTransparency();
    AlphaTest(a);
#if gsys_assign_variation == 1
    ProcDiscard(fLocalPosition.xyz);
#endif
#if gsys_assign_variation == 2
    oColor = vec4(0.0, 0.0, 0.0, a);
  #if uking_edit_disable_actor_id
    oColor1 = vec4(0.0);             // actor id output disabled
  #else
    oColor1 = UnpackUserData();
  #endif
#elif uking_inner_material
    oColor = vec4(vec3(0.0313725509), a);
#else
    oColor = vec4(vec3(1.0), a);
#endif
}
#endif
