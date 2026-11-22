// gsys_assign_material variations 1 and 2
//   1: cooking overlay (target 1): the albedo is re-saturated, tinted to a cooked brown and lit by cGSys16_Expand; alpha = uk_cook_ratio
//   2: light analysis: albedo under hemisphere light + analyzed ambient, mixed with the sky color (no effects)
#ifndef UKING_PASS_MATERIAL_VAR_GLSL
#define UKING_PASS_MATERIAL_VAR_GLSL
#if gsys_assign_type == GSYS_ASSIGN_MATERIAL && gsys_assign_variation != 0

float VariationAlpha()
{
#if gsys_alpha_test_enable || gsys_renderstate != 0
    float a = GetSourceChannel(uking_transparency_color, uking_transparency_channel).w;
    AlphaTest(a);
    return a;
#else
    return 1.0;
#endif
}

vec3 RgbToHsv(vec3 c)
{
    vec4 K = vec4(0.0, -0.333333343, 0.666666687, -1.0);
    vec4 p = c.g < c.b ? vec4(c.bg, K.wz) : vec4(c.gb, K.xy);
    vec4 q = c.r < p.x ? vec4(p.xyw, c.r) : vec4(c.r, p.yzx);
    float d = q.x - min(q.w, q.y);
    return vec3(abs(fma(q.w - q.y, 1.0 / fma(d, 6.0, 1.00000001E-10), q.z)), d * (1.0 / (q.x + 1.00000001E-10)), q.x);
}

void CookPass()
{
    SurfaceData sd = EvalSurface();
    vec3 hsv = RgbToHsv(sd.albedo);
    float s = Saturate(hsv.y + 0.100000001);
    vec3 h = abs(fma(fract(hsv.x + vec3(1.0, 0.666666687, 0.333333343)), vec3(6.0), vec3(-3.0)));
    vec3 c = fma(fma(Saturate(h - 1.0), vec3(s), vec3(-s)), vec3(hsv.z), vec3(hsv.z)) * vec3(0.838918269, 0.687082112, 0.544931769);
    float glow = Saturate((texture(cGSys16_Expand, ScreenUV()).y - 0.600000024) * 100.0);
    vec3 g = glow * vec3(0.206960559, 0.14346157, 0.0734196007);
    oColor = vec4(Saturate(c + fma(g, -c, g)), GetSourceChannel(uking_transparency_color, uking_transparency_channel).w * mat.uk_cook_ratio);
}

void LightAnalysisPass()
{
    SurfaceData sd = EvalSurface();
    float a = VariationAlpha();
    vec3 N = sd.vertexN, V = sd.V;
    float worldNy = dot(N, context.cViewInv[1].xyz);
    vec3 la = texture(cGSys23_LightAnalyzedData, vec2((Saturate(fma(worldNy, -0.5, 0.5)) + 6.5) * 0.0833333358, 0.0)).xyz;
    vec3 hemiDir = MulRowsDir(context.cView, env.HemDirection0World.xyz);

#if uking_material_attribute == 0
    // character inputs as in the material pass, but lighting the vertex normal (no normal map) from the
    // hemisphere direction
    SurfaceData vsd = sd;
    vsd.Ls     = hemiDir;
    vsd.N      = sd.vertexN;
    vsd.NdotV  = dot(sd.V, sd.vertexN);
    vsd.NdotV1 = Saturate(vsd.NdotV + 1.0);
    CharaInput ci = SetupCharaInput(vsd);
#if uking_material_behave == 100
    // hair highlight against the view x axis (light analysis has no tangent)
    ci.T = vec3(1.0, 0.0, 0.0);
    ci.hairSpec = CalcHairSpecular(ci.N, ci.T, ci.V, ci.Ls) * GetSourceChannel(uking_specular_hair, uking_specular_hair_channel).x;
#endif
#endif

    // hemisphere wrap: gloss for metal (1) / chara_grossy (10), the metal source for hybrid (2) and eyes (104,
    // where it carries the eye highlight), the anisotropic highlight for hair (100), none otherwise
#if uking_material_behave == 1 || uking_material_behave == 10
    float wrap = GetSourceChannel(uking_grossy_color, uking_grossy_channel).x;
#elif uking_material_behave == 2 || uking_material_behave == 104
    float wrap = GetSourceChannel(uking_metal_color, uking_metal_channel).x;
#elif uking_material_behave == 100 && uking_material_attribute == 0
    float wrap = ci.hairSpec;
#else
    float wrap = 0.0;
#endif
    float hemi = Saturate(wrap - dot(N, hemiDir));

#if uking_material_attribute == 0 && uking_material_behave != 1 && uking_material_behave != 10 && \
    uking_material_behave != 104 && (uking_material_behave == 100 || uking_texcoord_toon_spec_srt != -1)
    // character tone: the material pass's ramp, with the raw character shadow in place of the shadow mix
    // (no toon highlight coordinates -> no tone, except hair whose ramp does not use them)
    vec3 albedo = CharaToneAlbedo(sd.albedo, CharaToneRamp(ci, sd.albedo, ci.shadow));
#else
    vec3 albedo = sd.albedo;   // chara_metal, chara_grossy (ramp 0), eyes and field materials
#endif
    vec3 c = fma(vec3(hemi), env.cLightColor.xyz, la) * fma(Saturate(fma(albedo, vec3(1.07320976), vec3(-0.000528555829))), vec3(0.931784332), vec3(0.000492500025));
    vec3 upView = vec3(context.cView[0].y, context.cView[1].y, context.cView[2].y);
    float horizon = fma(pow(Saturate(dot(V, upView)), env.cSkyParam.z), -env.cSkyParam.w, 1.0);
    float heightTerm = pow(1.0 - Saturate(fma(sd.viewPos.z, -env.cSkyHeightParam.x, -env.cSkyHeightParam.y)), env.cSkyParam.x);
    float skyW = fma(horizon, -heightTerm, horizon) * env.cSkyColor.w;
#if gsys_renderstate >= 2 && gsys_gbuffer_xlu == 0
    // forward translucent: height fog (Fog1) between the lit color and the sky
    float fog1 = Saturate(fma(env.cFogHeightBase.x - sd.worldPos.y, env.Fog1_Param.x, env.Fog1_Direction.w)) * env.Fog1_Color.w;
    float keep = 1.0 - skyW;
    oColor = vec4(fma(c, vec3(fma(keep, -fog1, keep)), fma(vec3(keep * fog1), env.Fog1_Color.xyz, skyW * env.cSkyColor.xyz)), a);
#else
    oColor = vec4(fma(vec3(skyW), env.cSkyColor.xyz, fma(c, vec3(-skyW), c)), a);
#endif
}
#endif
#endif
