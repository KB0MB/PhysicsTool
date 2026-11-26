// Gameplay effects on albedo / specular / emission: chemical ice, figure, blueprint, damage, vanishing, miasma
#ifndef TOTK_EFFECTS_GLSL
#define TOTK_EFFECTS_GLSL

// ---------------- chemical ice (Mat.p_kari_chemical_ice_ratio0 > 0) ----------------
// albedo is replaced by a pale blue built from its luminance, frosted by 3D noise and the rim (rim = sat(1 + V.N))
vec3 ChemicalIceAlbedo(vec3 albedo, float noise, float rim)
{
    float lum   = Luminance(albedo);
    vec3  base  = fma(vec3(lum), vec3(0.5, 0.600000024, 0.649999976), vec3(0.100000001, 0.200000003, 0.349999994));
    vec3  tint  = vec3(0.159999996, 0.25999999, 0.25);
    float frost = Pow(abs(1.0 - noise), 1.29999995) * 0.200000003;
    float glow  = fma(Pow(rim, 2.5), 0.5, frost);
    vec3  ice   = Saturate(fma(tint - base, vec3(frost), base) + glow);
    return mix(albedo, ice, Mat.p_kari_chemical_ice_ratio0);
}

// ---------------- blueprint (Mat.p_blue_print_alternative) ----------------
// flat two-tone colour by the rim, an edge line where the light grazes, partly emissive (scaled by exposure)
void BlueprintSurface(inout vec3 albedo, inout float spec, inout vec3 emission, vec3 N, vec3 Ls, float rim, float exposureMask)
{
    float b    = Saturate(2.0 * (1.0 - rim - SceneMat.p_const_blueprint_base_albedo_blend));
    vec3  base = mix(SceneMat.p_const_blueprint_base_albedo0.xyz, SceneMat.p_const_blueprint_base_albedo1.xyz, b);
    float edge = Saturate((rim + fma(dot(-Ls, N), SceneMat.p_const_blueprint_edge_light_dark_range - SceneMat.p_const_blueprint_edge_light_bright_range,
                                     -SceneMat.p_const_blueprint_edge_light_dark_range)) * 1000.0);
    vec3  col  = mix(base, SceneMat.p_const_blueprint_edge_color.xyz, edge);
    float expo = SceneMat.p_dynamic_exposure * (1.0 - exposureMask);
    float emisCoef = SceneMat.p_const_blueprint_albedo_emission_coef * mix(SceneMat.p_const_blueprint_albedo_emission_exposure_coef, 1.0, expo);
    emission = mix(emission, fma(col, vec3(emisCoef), emission * Mat.p_blue_print_alternative_emission), Mat.p_blue_print_alpha);
    albedo   = mix(albedo, col * mix(SceneMat.p_const_blueprint_albedo_exposure_coef, 1.0, expo), Mat.p_blue_print_alpha);
    spec     = mix(spec, SceneMat.p_const_blueprint_specular, Mat.p_blue_print_alpha);
}

// ---------------- emission effects (first one that applies) ----------------
// proc discard: burning edge while an object is removed
vec3 ProcDiscardEmission(vec3 local)
{
    float k = Saturate(fma(Mat.p_proc_discard, -2.5, 2.5));
    vec3  s = local * Mat.p_edit_proc_discard_scale;
    float warp = texture(cTex_Proc3DNoise, s * 0.850000024).x;
    float n = Pow(abs(texture(cTex_Proc3DNoise, vec3(s.x * 0.5, s.y * 0.119999997, fma(s.z, 0.5, warp * 2.5))).x), 1.79999995);
    return fma(vec3(n), vec3(0.100000009, 5.55000019, 10.0), vec3(0.200000003, 0.449999988, 4.0)) * k;
}

// proc vanishing: patterned glow that dissolves the object
vec3 ProcVanishingEmission(vec3 local)
{
    float v = Saturate(Mat.p_proc_vanishing), t = 1.0 - v;
    vec3  s = local * SceneMat.p_const_vanishing_local_pos_scale * Mat.p_edit_proc_discard_scale;
    float shave = Saturate(texture(cTex_Proc3DNoise, fma(s, vec3(SceneMat.p_const_vanishing_shaving_fetch_scale), vec3(SceneMat.p_const_vanishing_shaving_fetch_offset))).x
                           * (1.0 / Saturate((t - 0.5) * 2.0)));
    vec3  c0 = mix(SceneMat.p_const_vanishing_color0_start.xyz, SceneMat.p_const_vanishing_color0_end.xyz, t);
    vec3  c1 = mix(SceneMat.p_const_vanishing_color1_start.xyz, SceneMat.p_const_vanishing_color1_end.xyz, t);
    float wave = sin(fma(v, SceneMat.p_const_vanishing_pattern_scroll_speed, s.x + texture(cTex_Proc3DNoise, s * SceneMat.p_const_vanishing_pattern_fetch_scale.xyz).x)
                     * SceneMat.p_const_vanishing_pattern_repeeat_ratio * 6.28318548);
    float pattern = Saturate((fma(v, 2.5, wave) - 0.5) * SceneMat.p_const_vanishing_whole_alpha) * Saturate(shave * shave * fma(shave, -2.0, 3.0));
    float inten = Saturate(t * (1.0 / SceneMat.p_const_vanishing_become_max_intensity_ratio));
    return inten * mix(c0, c1, pattern);
}

// blueprint "alternative" object fading in
vec3 BlueprintVanishingEmission()
{
    float v2 = Mat.p_blue_print_alternative_vanishing * Mat.p_blue_print_alternative_vanishing;
    return SceneMat.p_const_blueprint_emission_color.xyz * (1.0 - v2) * 7.0;
}

// reverse vanishing (Recall-style rebuild): replaces the emission
vec3 ReverseVanishingEmission(vec3 local, float rim)
{
    float r = Mat.p_reverse_vanishing;
    float w;                                   // fade in until 0.3, full until 0.8, fade out after
    if (r < 0.300000012) { float a = Saturate(r * 3.33333325); w = a * a * fma(a, -2.0, 3.0); }
    else if (r > 0.800000012) { float c = Saturate((r - 0.800000012) * 5.00000048); w = 1.0 - c * c * fma(c, -2.0, 3.0); }
    else w = 1.0;
    float b  = Saturate((r - 0.300000012) * 1.42857146);
    float sb = b * b * fma(b, -2.0, 3.0);
    float phase = (texture(cTex_Proc3DNoise, local).x + cTime.x) * 31.4159279;
    float pulse = fma(sin(phase), 0.5, 0.5);
    float edge  = Saturate((rim - 0.5) * 2.50000024);
    float glow  = fma(pulse, 1.89999998, edge * 10.0);
    float build = sb * Saturate((1.0 - texture(cTex_Proc3DNoise, local * 5.0).x - sb) * (1.0 / (1.0 - sb)));
    return vec3(glow + 0.600000024, glow + 0.349999994, fma(pulse, 0.824999988, edge * 2.0) + 0.0500000007) * w + build * vec3(100.0, 100.0, 10.0);
}

// miasma (Gloom) crawling up the object: darkens albedo, red glow at the noise edges
void Miasma(inout vec3 albedo, inout vec3 emission, vec3 local, float rim)
{
    float n = texture(cTex_Proc3DNoise, vec3(local.x * Mat.p_miasma_scale, fma(local.y, Mat.p_miasma_scale, -cTime.y * Mat.p_frame_count_coef), local.z * Mat.p_miasma_scale)).x;
    float h = Saturate((local.y - Mat.p_miasma_min_local_y) * (1.0 / (Mat.p_miasma_max_local_y - Mat.p_miasma_min_local_y)));
    float a = h * Mat.p_miasma_ratio;
    float edge = Saturate((n - 0.600000024) * 6.66666794);
    float body = Saturate((0.699999988 - n) * 6.66666794);
    float rimm = Saturate((rim - 0.449999988) * 1.81818175);
    albedo = fma(albedo, vec3(fma(body, a, -a)), albedo);                 // * (1 - a (1 - body))
    emission = fma(vec3(fma(rimm, 0.5, edge * 5.0), 0.0, fma(rimm, 0.00499999989, edge * 0.0250000004)) * body, vec3(a),
                   fma(emission, vec3(fma(body, h, -h)), emission));        // * (1 - h (1 - body)) + glow
}

void EmissionEffects(inout vec3 albedo, inout vec3 emission, vec3 local, float rim)
{
    if (Mat.p_proc_discard < 1.0)
        emission += ProcDiscardEmission(local);
    else if (Mat.p_proc_vanishing < 1.0)
        emission += ProcVanishingEmission(local);
    else if (Mat.p_blue_print_alternative_vanishing < 1.0)
        emission += BlueprintVanishingEmission();
    else if (Mat.p_round_discard == 0.0)
    {
        if (Mat.p_reverse_vanishing > 0.0)
            emission = ReverseVanishingEmission(local, rim);
        else if (Mat.p_miasma_ratio > 0.0 && Mat.p_kari_chemical_ice_ratio0 == 0.0)
            Miasma(albedo, emission, local, rim);
    }
}

// ---------------- discards (shadow pass): the dissolve effects cut the shadow out ----------------
// first effect that applies decides, like EmissionEffects
void EffectDiscard(vec3 local)
{
    if (Mat.p_proc_discard < 1.0)
    {
        vec3 s = local * Mat.p_edit_proc_discard_scale;
        float warp = texture(cTex_Proc3DNoise, s * 0.850000024).x * 2.5;
        float n = texture(cTex_Proc3DNoise, vec3(s.x * 0.5, s.y * 0.119999997, fma(s.z, 0.5, warp))).x;
        if (n < Saturate((0.850000024 - Mat.p_proc_discard) * 1.17647052)) discard;
    }
    else if (Mat.p_proc_vanishing < 1.0)
    {
        float v = Saturate(Mat.p_proc_vanishing);
        vec3 s = local * SceneMat.p_const_vanishing_local_pos_scale * Mat.p_edit_proc_discard_scale;
        float shave = Saturate(texture(cTex_Proc3DNoise, fma(s, vec3(SceneMat.p_const_vanishing_shaving_fetch_scale), vec3(SceneMat.p_const_vanishing_shaving_fetch_offset))).x
                               * (1.0 / Saturate((0.5 - v) * 2.0)));
        float wave = sin(fma(v, SceneMat.p_const_vanishing_pattern_scroll_speed, s.x + texture(cTex_Proc3DNoise, s * SceneMat.p_const_vanishing_pattern_fetch_scale.xyz).x)
                         * SceneMat.p_const_vanishing_pattern_repeeat_ratio * 6.28318548);
        float a = Saturate(shave * shave * fma(shave, -2.0, 3.0)) * Saturate((fma(v, 2.5, wave) - 0.5) * SceneMat.p_const_vanishing_whole_alpha);
        if (a < 0.5) discard;
    }
    else if (Mat.p_blue_print_alternative_vanishing < 1.0)
    {
        if (texture(cTex_Proc3DNoise, local * Mat.p_edit_proc_discard_scale).x + Mat.p_blue_print_alternative_vanishing - 0.5 < 0.5) discard;
    }
    else if (Mat.p_round_discard != 0.0)
    {
        vec4 r = SceneMat.p_dynamic_round_discard_param;
        if (!(fma(fma(r.x * r.x, 1.0 / r.z, r.y * r.y * (1.0 / r.w)), Mat.p_round_discard, -Mat.p_round_discard) > 0.0)) discard;
    }
    else if (Mat.p_reverse_vanishing > 0.0)
    {
        float b = Saturate((Mat.p_reverse_vanishing - 0.300000012) * 1.42857146);
        if (1.0 - texture(cTex_Proc3DNoise, local * 5.0).x < b * b * fma(b, -2.0, 3.0)) discard;
    }
}

#endif
