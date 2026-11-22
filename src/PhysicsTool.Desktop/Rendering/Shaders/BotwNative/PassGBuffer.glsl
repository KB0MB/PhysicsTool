// gsys_assign_gbuffer: deferred pass. Targets:
//   RT0 (oColor)  x = material id, y = uk_object_attribute, z = 0, w = 1
//   RT1 (oGBuf1)  albedo * (1 - damage.w), w = material flags
//   RT3 (oGBuf3)  view normal * 0.5 + 0.5, w = 8-bit packed specular / gloss + flags
//   RT5 (oGBuf5)  emission (+ special effects, damage color, dissolve glow)
// variation 1 = full shader, 2 = reduced, 0 = minimal
#ifndef UKING_PASS_GBUFFER_GLSL
#define UKING_PASS_GBUFFER_GLSL

// smooth triangle wave used by the special effects: 3u^2 - 2u^3 with u = triangle(fract(x) * 2)
float FxWave(float x)
{
    float t = fract(x) * 2.0;
    float u = Saturate(t) - Saturate(t - 1.0);
    float u2 = u * u;
    return fma(u2, 3.0, u * u2 * -2.0);
}

struct GBufferFx
{
    vec3 color;     // effect emission
    float weight;   // how much the effect replaces the surface (albedo / spec / emission)
};

// uk_special_effect_A / B / C (Mat values, not options): glowing patterns used by game effects
GBufferFx CalcSpecialEffects(vec3 N, vec3 albedo, float NdotV1)
{
    GBufferFx fx; fx.color = vec3(0.0); fx.weight = 0.0;
    float S = mat.uking_edit_proc_discard_scale * mat.uk_bounding_radius;
    vec3 P = fLocalPosition.xyz;
    float time = context.sf.x;
    if (mat.uk_special_effect_A != 0.0)
    {
        float k = S * 7.19999981;
        float g = fma(sin(P.z * k) + sin(P.y * k) + sin(P.x * k), 0.25,
                  fma(S * (N.y + N.x + N.z), 0.300000012, S * dot(N, P)) + dot(albedo, albedo));
        float s = FxWave(fma(time, -0.0333333351, fma(g, 0.75, Saturate(g) * -0.349999994)));
        float a = sqrt(abs(mat.uk_special_effect_A));
        float p200 = exp2(log2(s) * 200.0);
        float s4 = s * 0.400000006;
        float rim8 = exp2(log2(Saturate(1.0 - N.z)) * 8.0);
        float w = mat.uk_special_effect_A < 0.0 ? fma(a, 0.0500000045, 0.100000001) : fma(a, 0.200000003, 0.200000003);
        fx.color.r = (fma(fma(a, -200.0, 300.0), rim8, fma(a, -1.79999995, fma(p200, fma(a, -130.0, 160.0), s4))) + 2.79999995) * w;
        fx.color.g = (fma(fma(a, -235.0, 255.0), rim8, fma(a, -2.79999995, fma(p200, fma(a, -154.0, 160.0), s4))) + 2.79999995) * w;
        fx.color.b = (fma(fma(a, 14.0, 6.0), rim8, fma(a, -0.0700000003, fma(p200, fma(a, -34.0, 40.0), s4))) + 0.0700000003) * w;
        fx.weight = w;
    }
    if (0.0 < mat.uk_special_effect_B)
    {
        float s = FxWave(fma(time, -0.0333333351, fma(P.y * S, 3.5, fma(N.y, 0.5, N.z - albedo.g))));
        float rim = Saturate(1.0 - N.z);
        float rim5 = rim * rim * rim * rim * rim;
        float pulse = fma(sin(time * -0.209439531), 0.5, 0.5);
        float p6 = exp2(log2(s) * 6.0);
        float base = pulse + 0.5;
        float h = fma(pulse, rim5, rim5);
        vec3 c = vec3(fma(h, 9.0, fma(p6, 1.79999995, base * 0.270000011)),
                      fma(h, 5.0, fma(p6, 0.600000024, base * 0.120000005)),
                      fma(h, 0.5, fma(p6, 0.100000001, base * 0.00300000003)));
        fx.color = fma(c - fx.color, vec3(mat.uk_special_effect_B), fx.color);
        fx.weight = fma(mat.uk_special_effect_B, 0.800000012, fx.weight);
    }
    if (0.0 < mat.uk_special_effect_C)
    {
        float c = NdotV1 * NdotV1;
        float cw = c * mat.uk_special_effect_C;
        fx.weight += cw;
        fx.color.r = fma(fx.color.r, -cw, fx.color.r);
        fx.color.g = fma(cw, c - fx.color.g, fx.color.g);
        fx.color.b = fma(cw, c - fx.color.b, fx.color.b);
    }
    fx.weight = Saturate(fx.weight);
    return fx;
}

// uk_kari_mottled_ratio: blotchy darkening in object space
float CalcMottle()
{
    vec3 p = fLocalPosition.xyz * (1.0 / mat.uk_mottled_scale);
    float n1 = texture(cProc3DNoise, p * 3.5999999).x;
    return Saturate((fma(texture(cProc3DNoise, p * 0.720000029).x - n1, 0.699999988, n1) - mat.uk_kari_mottled_ratio) * 20.0);
}

// uk_proc_discard < 1: dissolve with a glowing edge (adds to the emission)
void GBufferProcDiscard(inout vec3 emission)
{
    if (mat.uk_proc_discard < 1.0)
    {
        float t = 1.0 - mat.uk_proc_discard;
        vec3 p = fLocalPosition.xyz * mat.uking_edit_proc_discard_scale;
        float warp = texture(cProc3DNoise, p * sceneMat.uking_const_proc_discard_scale2).x * sceneMat.uking_const_proc_discard_scale3;
        float n = texture(cProc3DNoise, vec3(p.x * 0.5, p.y * sceneMat.uking_const_proc_discard_scale1, fma(p.z, 0.5, warp))).x;
        if (n < Saturate((t - 0.150000006) * 1.17647052)) discard;
        float glow = Saturate(t * 2.5);
        float e = exp2(log2(n) * 1.79999995);
        emission += glow * vec3(fma(e, 0.100000009, 0.200000003), fma(e, 5.55000019, 0.449999988), fma(e, 10.0, 4.0));
    }
}

float PackSpec(float v, float low) { return fma(floor(v * 63.75), 4.0, low) * 0.00392156886; }

#if UKING_GBUFFER_REDUCED
// reduced variations: material id per behave (/255), +16 for forward translucent (render state 2/3, gsys_pass 0)
float ReducedMaterialId()
{
  #if uking_material_attribute == 10
    #if uking_material_behave == 105
    float id = 7.0;                                   // leaf
    #elif uking_material_behave == 102 || uking_material_behave == 103
    float id = 6.0;
    #else
    float id = 4.0;                                   // field
    #endif
  #elif uking_material_behave == 1
    float id = 10.0;                                  // metal
  #elif uking_material_behave == 2
    float id = GetSourceChannel(uking_metal_color, uking_metal_channel).x > 0.5 ? 10.0 : 8.0;   // hybrid
  #elif uking_material_behave == 10
    float id = GetSourceChannel(uking_grossy_color, uking_grossy_channel).x > 0.5 ? 11.0 : 8.0; // chara_grossy
  #elif uking_material_behave == 100
    float id = 12.0;                                  // hair
  #elif uking_material_behave == 101
    float id = 13.0;                                  // skin
  #elif uking_material_behave == 104
    float id = 14.0;                                  // eyes
  #else
    float id = 8.0;
  #endif
  #if gsys_renderstate >= 2 && gsys_pass == 0
    id += 16.0;
  #endif
    return id * 0.00392156886;
}

// character specular as the material pass sees it (toon mask, hybrid metal)
float ReducedCharaSpec(float specular)
{
  #if uking_texcoord_toon_spec_srt == -1
    float toonMask = 0.0;                       // no toon highlight coordinates
  #else
    vec2  toon     = fToonSpecCoords.xy - floor(fToonSpecCoords.xy) - 0.5;
    float toonMask = Saturate(fma(toon.x, -toon.x, 1.0)) * Saturate(fma(toon.y, -toon.y, 1.0));
  #endif
  #if uking_material_behave == 2
    return specular * Saturate(fma(toonMask, toonMask, GetSourceChannel(uking_metal_color, uking_metal_channel).x));
  #elif uking_material_behave == 1 || uking_material_behave == 10
    return specular * Saturate(fma(toonMask, toonMask, GetSourceChannel(uking_grossy_color, uking_grossy_channel).x));
  #elif uking_material_behave != 100 && uking_material_behave != 104
    return specular * toonMask * toonMask;
  #else
    return specular;
  #endif
}
#endif

void GBufferPass()
{
    SurfaceData sd = EvalSurface();
#if gsys_alpha_test_enable && !gsys_force_zprepass
    // (with a forced z-prepass the depth test already resolved the cutout)
    AlphaTest(GetSourceChannel(uking_transparency_color, uking_transparency_channel).w);
#endif
    vec3 N = sd.N, albedo = sd.albedo, emission = sd.emission;
    bool ice = sd.ice;
    float NdotV1 = sd.NdotV1;
#if uking_material_attribute == 10 && uking_enable_normalmap
    N = sd.Nraw;                                   // field: the normal-mapped normal is not renormalized here
    NdotV1 = Saturate(dot(N, sd.V) + 1.0);
#endif

#if uking_material_attribute == 10
    // field: gloss (spec * grossy), chemical ice pushes it to 0.9
    float spec = sd.specular * GetSourceChannel(uking_grossy_color, uking_grossy_channel).x;
    if (ice) spec = fma(0.899999976 - spec, mat.uk_kari_chemical_ice_ratio0, spec);
#else
    // character
  #if UKING_GBUFFER_REDUCED
    float spec = ReducedCharaSpec(sd.specular);
  #elif uking_material_behave == 0
    float spec = 0.0;                 // toon specular is not stored
  #else
    float spec = sd.specular;
  #endif
  #if uking_enable_transmission
    float shadowAdd = GetSourceChannel(uking_transmission_color, uking_transmission_channel).x;   // toon sun floor
  #else
    float shadowAdd = 0.0;            // toon sun floor (transmission), 0.3 under ice
  #endif
  #if UKING_GBUFFER_REDUCED && uking_material_behave == 1
    float toonParam = GetSourceChannel(uking_grossy_color, uking_grossy_channel).x;   // reduced: metal stores gloss
    bool highId = false;
  #elif UKING_GBUFFER_REDUCED && uking_material_behave == 2
    float toonParam = GetSourceChannel(uking_metal_color, uking_metal_channel).x;     // reduced: hybrid stores metal
    bool highId = false;
  #elif UKING_GBUFFER_REDUCED && uking_material_behave == 100
    // reduced: hair stores its anisotropic highlight
    float toonParam = CalcHairSpecular(N, fViewTangent.xyz, sd.V, sd.Ls) * GetSourceChannel(uking_specular_hair, uking_specular_hair_channel).x;
    bool highId = false;
  #elif uking_material_behave == 10
    // chara_grossy: gloss * 0.2 * (uking_grossy_intensity + 1)  (intensity 2 -> 0.6)
    float toonParam = GetSourceChannel(uking_grossy_color, uking_grossy_channel).x * (float(uking_grossy_intensity) + 1.0) * 0.200000003;
    bool highId = !MimicryActive();
  #else
    float toonParam = 0.0;
    bool highId = false;
  #endif
    if (ice) { spec = 1.0; shadowAdd = 0.300000012; highId = true; }
#endif

#if gsys_assign_variation == 1
    if (0.0 < mat.uk_kari_mottled_ratio)
    {
        float m = CalcMottle();
        albedo *= m; emission *= m;
    }
    GBufferFx fx = CalcSpecialEffects(N, albedo, NdotV1);
    if (fx.weight > 0.0)
    {
        float keep = 1.0 - fx.weight;
        emission = fma(emission, vec3(keep), fx.color * fx.weight);
        albedo *= keep; spec *= keep;
    }
#endif
#if gsys_assign_variation == 0
    vec4 dmg = vec4(0.0);             // minimal: no damage color
#else
    vec4 dmg = mat.uk_damage_color;
#endif
    emission = fma(dmg.rgb - emission, vec3(dmg.w), emission);
#if gsys_assign_variation == 1
    GBufferProcDiscard(emission);
#endif
    // RT5 is written by variations 1 / 2 always, by variation 0 only with emission; RT1.w bit 0 says so
#if gsys_assign_variation != 0 || uking_enable_emission
    float hasRT5 = 1.0;
#else
    float hasRT5 = 0.0;
#endif

    float objAttr = 0.0;
#if uking_enable_output_object_attribute
    objAttr = mat.uk_object_attribute;
#endif
    vec3 outAlbedo = fma(albedo, vec3(-dmg.w), albedo);
    float outSpec = fma(spec, -dmg.w, spec);

#if uking_material_attribute == 10
  #if UKING_GBUFFER_REDUCED
    float id = ReducedMaterialId();
  #elif uking_material_behave == 105
    float id = 0.0274509806;            // 7: leaf
  #else
    float id = 0.0156862754;            // 4: field
  #endif
  #if (gsys_alpha_test_enable || gsys_renderstate != 0) && !gsys_force_zprepass
    float a = GetSourceChannel(uking_transparency_color, uking_transparency_channel).w;   // RT0.w = alpha unless opaque
  #else
    float a = 1.0;
  #endif
    oColor = vec4(id, objAttr, 0.0, a);
  #if uking_material_behave == 1 || uking_material_behave == 2
    // metal (1: fully metal, 2: the metal source), 0 under mimicry
    #if uking_material_behave == 1
    float metal = MimicryActive() ? 0.0 : 1.0;
    #else
    float metal = MimicryActive() ? 0.0 : GetSourceChannel(uking_metal_color, uking_metal_channel).x;
    #endif
    oGBuf1 = vec4(outAlbedo, PackSpec(metal, hasRT5));
  #else
    // bit 0: RT5 written, bit 1: transmission enabled
    oGBuf1 = vec4(outAlbedo, (hasRT5 + (uking_enable_transmission != 0 ? 2.0 : 0.0)) * 0.00392156886);
  #endif
  #if uking_material_behave == 105
    oGBuf3 = vec4(fma(N, vec3(0.5), vec3(0.5)), PackSpec(outSpec, ice ? 1.0 : 0.0));   // leaf: low bits = ice flag
  #else
    // low bit: 1 with uking_enable_fresnel_cheat, otherwise the chemical ice flag
    oGBuf3 = vec4(fma(N, vec3(0.5), vec3(0.5)), PackSpec(outSpec, (uking_enable_fresnel_cheat != 0 || ice) ? 1.0 : 0.0));
  #endif
#else
    // character: id 11 (8 when the toon highlight is off),
    // RT1.w = 32 * uking_chara_size + 2 * (shadowAdd > 0) + 3-bit toon parameter + RT5 bit
    float flag = shadowAdd > 0.0 ? 1.0 : 0.0;
    float param = flag > 0.0 ? shadowAdd : toonParam;
  #if (gsys_alpha_test_enable || gsys_renderstate != 0) && !gsys_force_zprepass
    float a = GetSourceChannel(uking_transparency_color, uking_transparency_channel).w;   // RT0.w = alpha unless opaque
  #else
    float a = 1.0;
  #endif
  #if UKING_GBUFFER_REDUCED
    oColor = vec4(ReducedMaterialId(), objAttr, 0.0, a);
  #else
    oColor = vec4(highId ? 0.0431372561 : 0.0313725509, objAttr, 0.0, a);
  #endif
  #if uking_material_behave == 101
    oGBuf1 = vec4(outAlbedo, (float((int(trunc(shadowAdd * 255.0)) & 240) >> 3) + float(uking_chara_size) * 32.0 + hasRT5) * 0.00392156886);   // skin: 4-bit scatter
  #elif uking_material_behave == 104
    // eyes: the eye highlight (metal source; 0 under mimicry, 1 under chemical ice) packed like a specular value
    float eyeHighlight = MimicryActive() ? 0.0 : GetSourceChannel(uking_metal_color, uking_metal_channel).x;
    if (ice) eyeHighlight = 1.0;
    oGBuf1 = vec4(outAlbedo, (fma(floor(eyeHighlight * 63.75), 4.0, shadowAdd * 2.0) + hasRT5) * 0.00392156886);
  #else
    oGBuf1 = vec4(outAlbedo, (fma(flag, 2.0, float((int(trunc(param * 255.0)) & 224) >> 3)) + float(uking_chara_size) * 32.0 + hasRT5) * 0.00392156886);
  #endif
  #if uking_enable_ao
    // character AO is stored in the normal's length (0.75 .. 1)
    vec3 Nout = N * fma(GetSourceChannel(uking_ao_color, uking_ao_channel).x, 0.25, 0.75);
  #else
    vec3 Nout = N;
  #endif
    oGBuf3 = vec4(fma(Nout, vec3(0.5), vec3(0.5)), (fma(floor(outSpec * 63.75), 4.0, ice ? 1.0 : 0.0) + 2.0) * 0.00392156886);
#endif
#if gsys_renderstate >= 2 && uking_enable_gbuffer_xlu_blend && uking_material_attribute == 10
    // translucent gbuffer: blend over what is already in the gbuffer by the surface alpha.
    // The decompiled shaders read the gbuffer at texel (0, 0) (most likely the decompiler's rendering of a
    // read of this pixel); this matches the decompile.
    float xa = GetSourceChannel(uking_transparency_color, uking_transparency_channel).w;
    vec4 gA = texture(cGSys00_GBuffAlbedo, vec2(0.0));
    vec4 gN = texture(cGSys01_GBuffNormal, vec2(0.0));
    oColor.w = xa;
    oGBuf1.rgb = mix(gA.rgb, oGBuf1.rgb, xa);
    float prevMetal = float(int(trunc(gA.w * 255.0)) & 252) * 0.00396825397;   // upper 6 bits as 0..1
  #if uking_material_behave == 1 || uking_material_behave == 2
    float newMetal = floor(oGBuf1.w * 63.75) * 0.0158730159;
  #else
    float newMetal = 0.0;
  #endif
    oGBuf1.w = fma(floor(mix(prevMetal, newMetal, xa) * 63.75), 0.0156862754, hasRT5 * 0.00392156886);
    // RT5: blend over the previous emission when that pixel had one (bit 0 of its RT1.w)
    float prevHasRT5 = float(int(trunc(gA.w * 255.0)) & 1);
    emission = mix(texture(cGSys02_GBuffEmission, vec2(0.0)).rgb * prevHasRT5, emission, xa);
    oGBuf3.xyz = mix(gN.xyz, oGBuf3.xyz, xa);
    oGBuf3.w = fma(floor(mix(gN.w, outSpec, xa) * 63.75), 0.0156862754, (uking_enable_fresnel_cheat != 0 || ice) ? 0.00392156886 : 0.0);
#endif
#if gsys_assign_variation != 0 || uking_enable_emission
    oGBuf5 = vec4(emission, 1.0);
#endif
}
#endif
