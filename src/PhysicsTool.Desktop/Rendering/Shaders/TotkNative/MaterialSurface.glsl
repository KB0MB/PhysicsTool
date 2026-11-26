// Material inputs after the per-material options and the surface-replacing effects (chemical ice, figure, blueprint).
// Shared by the passes that shade or store the full surface (material, dynamic3).
#ifndef TOTK_MATERIAL_SURFACE_GLSL
#define TOTK_MATERIAL_SURFACE_GLSL

struct MaterialSurface
{
    vec3  albedo;
    vec3  emission;
    float spec;          // before damage
    float metal;         // also the chemical-ice flag (1) and the shadow normal push
    float ao;
    float transmission;  // chemical ice: 0.3
    float effectFlag;    // 1 under chemical ice or figure (dynamic3 RT3.w bit 0)
};

// specular and metal from the material options; mimic = mimicry blend (0 when off)
vec2 MaterialSpecMetal(float mimic)
{
    float specMask = GetSourceChannel(o_specmask_scaler, o_specular_channel).x;
#if HAS_TOON_SPEC_COORD
    float pattern = ToonSpecPattern(fToonSpecCoord.xy);
#else
    float pattern = 1.0;
#endif
#if o_material_behave == 104
    // eye: the metal input is the iris highlight; the specular mask is only stored (eye lighting does not use it)
    float metal = GetSourceChannel(o_metal_color, o_metal_channel).x * (1.0 - mimic);
    float spec = specMask;
#elif o_material_behave == 1 || o_material_behave == 2
    // metal (behave 1: fully metal, 2: metal mask); the specular pattern is lifted by the metal amount
  #if o_material_behave == 1
    float metal = 1.0;
  #else
    float metal = GetSourceChannel(o_metal_color, o_metal_channel).x;
  #endif
    float spec = specMask * Saturate(pattern + metal);
    if (mimic > 0.0)
    {
        // mimicry fades the specular out and back in, and the metal out
        spec = mimic < 0.5 ? fma(spec * -mimic, 2.0, spec) : spec * 2.0 * (mimic - 0.5);
        metal *= 1.0 - mimic;
    }
#else
    float metal = 0.0;
    float spec = pattern * specMask;
#endif
    return vec2(spec, metal);
}

// exposureMask: volume-mask channel that cancels the blueprint exposure (material pass: z, dynamic3: y)
MaterialSurface EvalMaterialSurface(vec3 N, vec3 Ls, float rim, vec3 local, float exposureMask)
{
    vec3 albedo = GetSource(o_albedo_color).rgb;
    float mimic = Mimicry(albedo, fTexCoord01.xy);
    vec2 sm = MaterialSpecMetal(mimic);
    float spec = sm.x, metal = sm.y;
#if o_enable_emission
    vec3 emission = GetSource(o_emission_color).rgb;
#else
    vec3 emission = vec3(0.0);
#endif
#if o_enable_ao
    float ao = GetSourceChannel(o_ao_color, o_ao_channel).x;
#else
    float ao = 1.0;
#endif
#if o_enable_transmission
    float transmission = GetSourceChannel(o_transmission_color, o_transmission_channel).x;
#else
    float transmission = 0.0;
#endif

    if (Mat.p_kari_chemical_ice_ratio0 > 0.0)
    {
        albedo = ChemicalIceAlbedo(albedo, texture(cTex_Proc3DNoise, local * 0.300000012).x, rim);
        spec = 1.0; metal = 1.0; transmission = 0.300000012;
        emission *= 1.0 - Mat.p_kari_chemical_ice_ratio0;
    }
    else if (Mat.p_figure > 0.0)
    {
        // figure (amiibo-style statue) look
        spec = SceneMat.p_const_figure_param.x;
        metal = 0.0;
        transmission = 0.0;
        ao = Saturate(ao - SceneMat.p_const_figure_param.z) + SceneMat.p_const_figure_param.z;
    }
    else if (Mat.p_blue_print_alternative > 0.0 || Mat.p_blue_print_alternative_vanishing < 1.0)
    {
        BlueprintSurface(albedo, spec, emission, N, Ls, rim, exposureMask);
        metal *= 1.0 - Mat.p_blue_print_alpha;
    }

    float effectFlag = (Mat.p_kari_chemical_ice_ratio0 > 0.0 || Mat.p_figure > 0.0) ? 1.0 : 0.0;
    return MaterialSurface(albedo, emission, spec, metal, ao, transmission, effectFlag);
}

#endif
