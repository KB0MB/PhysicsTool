#version 450 core
// BOTW uking_mat, gsys_assign_type = gsys_assign_material (variation 0) -- fragment shader
#include "uking_mat_options.h"

// light analysis (material variation 2): plain surface (no AO / bake map on albedo, no mimicry / chemical ice),
// lit from the hemisphere direction
#define UKING_LIGHT_ANALYSIS (gsys_assign_type == GSYS_ASSIGN_MATERIAL && gsys_assign_variation == 2)
// reduced gbuffer (variations 0 and 2): no mimicry / chemical ice / special effects
#define UKING_GBUFFER_REDUCED (gsys_assign_type == GSYS_ASSIGN_GBUFFER && gsys_assign_variation != 1)
#if (BLENDER_RENDER == 0)
#include "Uniforms.glsl"
#endif
#include "Common.glsl"
#if (BLENDER_RENDER == 1)
#include "BlenderCompat.glsl"
#endif

#define UKING_FRAGMENT
#define VARYING in
#if (BLENDER_RENDER == 0)
#include "Varyings.glsl"
#include "Samplers.glsl"
#endif
#include "TexCoord.glsl"
#include "Surface.glsl"
#include "Shadow.glsl"
#include "Sky.glsl"
#include "Effects.glsl"
#include "Lighting.glsl"
#include "LightingChara.glsl"

// Fragment outputs (Blender: declared from MetaData.json, material pass only)
#if (BLENDER_RENDER == 0)
#if gsys_assign_type == GSYS_ASSIGN_MATERIAL && gsys_assign_variation == 1
layout (location = 1) out vec4 oColor;     // cooking overlay goes to target 1
#else
layout (location = 0) out vec4 oColor;
#endif
#if gsys_assign_type == GSYS_ASSIGN_ZONLY && gsys_assign_variation == 2
layout (location = 1) out vec4 oColor1;
#endif
#if gsys_assign_type == GSYS_ASSIGN_GBUFFER
layout (location = 1) out vec4 oGBuf1;
layout (location = 3) out vec4 oGBuf3;
#if gsys_assign_variation != 0 || uking_enable_emission
layout (location = 5) out vec4 oGBuf5;
#endif
#endif
#endif
#include "Discard.glsl"
#if gsys_assign_type == GSYS_ASSIGN_ZONLY
#include "PassZOnly.glsl"
#endif

// ======================= surface inputs shared by the material and gbuffer passes =======================
struct SurfaceData
{
    vec3 viewPos, V, vertexN, N, Nraw, Ls, worldPos;
    vec2 screenUV, volumeMask, uv0;
    float lightRatio, NdotV, NdotV1, specular;
    vec3 albedo, emission;
    bool ice;
};

SurfaceData EvalSurface()
{
    // -------------------------------------------------------------------------
    // View-space basics
    // -------------------------------------------------------------------------
    vec3 viewPos = fViewPosition.xyz;
    vec3 V       = normalize(viewPos);

#if uking_modify_normal_type == 1 && !UKING_LIGHT_ANALYSIS
    vec3 vertexN = normalize(fViewNormal.xyz + fNormalModify.xyz);
#else
    vec3 vertexN = normalize(fViewNormal.xyz);
#endif

    float lightRatio = sceneMat.uking_dynamic_base_light_change_ratio;
#if UKING_LIGHT_ANALYSIS
    vec3  Ls         = MulRowsDir(context.cView, env.HemDirection0World.xyz);   // lit from the hemisphere direction
#else
    vec3  Ls         = env.cLightDirection0.xyz * lightRatio;
#endif

    vec3 worldPos   = MulRows(env.cViewToWorldPos, viewPos);
    vec2 screenUV   = fScreenCoords.xy * (1.0 / fScreenCoords.w);
#if (BLENDER_RENDER == 1)
    vec2 volumeMask = vec2(0.0);   // no volume / cave mask in Blender
#else
    vec2 volumeMask = texture(cGSys17_VolumeMask, screenUV).xy;
#endif

    // -------------------------------------------------------------------------
    // Surface setup (first texture fetch + globals)
    // -------------------------------------------------------------------------
    gSphereMapN = vertexN;
    FetchTextures(false);

    gSourceNdotV       = Saturate(dot(V, vertexN) + 1.0);
#if UKING_LIGHT_ANALYSIS
    gSourceNdotVVertex = Saturate(dot(V, vertexN) + 1.0);           // light analysis: normalized vertex normal
#else
    gSourceNdotVVertex = Saturate(dot(V, fViewNormal.xyz) + 1.0);
#endif
    gSourceDepth       = -viewPos.z;
    gSourceFovRatio    = 1.0 / (viewPos.z * -context.cFovParams.y);

    SetupSceneUV();

    // If any normal-map related source is a calc-color, evaluate the
    // "before_normal" colors first.
#if (uking_normal_color >= 200 && uking_normal_color <= 213) || \
    (uking_enable_normalmap2 && ((uking_normal_color2 >= 200 && uking_normal_color2 <= 213) || \
                                 (uking_normalmap_blend_ratio >= 200 && uking_normalmap_blend_ratio <= 213)))
    EvalCalcColors(1);
#endif

    // -------------------------------------------------------------------------
    // Normal mapping
    // -------------------------------------------------------------------------
    vec3 N    = vertexN;
    vec3 Nraw = vertexN;   // un-normalized normal-mapped result (field G-buffer stores it as-is)

#if uking_enable_normalmap && !UKING_LIGHT_ANALYSIS   // light analysis lights the vertex normal
    vec3 tn = DecodeNormalMap(GetSource(uking_normal_color));

  #if uking_enable_normalmap2
    // Second normal map, blended in tangent space
    // (method 0 = lerp, method 1 = xy add)
    vec3  tn2   = DecodeNormalXY(GetSource(uking_normal_color2).xy);
    float blend = GetSourceChannel(uking_normalmap_blend_ratio, uking_normalmap_blend_channel).x;

    #if uking_normalmap_blend_method == 1
    // Detail normal added in xy (scaled by ratio), base Z kept
    tn = normalize(vec3(fma(tn2.xy, vec2(blend), tn.xy), tn.z));
    #else
    tn = normalize(fma(tn2 - tn, vec3(blend), tn));
    #endif
  #endif

  #if uking_modify_normal_type == 1
    // Tangent frame rebuilt around the modified normal
    vec3 Bm = cross(vertexN, fViewTangent.xyz);
    Nraw = cross(Bm, vertexN) * tn.x
         + Bm * (fViewTangent.w * tn.y)
         + vertexN * tn.z;
    N = normalize(Nraw);

  #elif uking_enable_calc_binormal_vertex
    Nraw = fViewTangent.xyz  * tn.x
         + fViewBinormal.xyz * tn.y
         + fViewNormal.xyz   * tn.z;
    N = normalize(Nraw);

  #else
    Nraw = ApplyNormalMap(fViewNormal.xyz, fViewTangent, tn);
    N    = normalize(Nraw);
  #endif
#endif

    // Second texture fetch now that the final normal is known
    gSphereMapN = N;
    FetchTextures(true);

    float NdotV  = dot(V, N);
    float NdotV1 = Saturate(NdotV + 1.0);

#if uking_enable_inner_light
    gSourceInnerLight = CalcInnerLight(
        N, V,
        GetConstSource(uking_inner_light_source_radius).x,
        GetConstSource(uking_inner_light_diffusivity).x
    );
#endif

    gSourceNdotV = NdotV1;
    gSourceVdotL = dot(V, Ls);

    // Front-face corrected N.L
    {
        vec3 M  = cross(fViewTangent.xyz, cross(fViewNormal.xyz, fViewTangent.xyz)); // vertex normal orthogonal to tangent
        vec3 Nf = gl_FrontFacing ? N : fma(M, vec3(-2.0 * dot(N, M)), N);
        gSourceNdotL = -dot(Ls, normalize(Nf));
    }

    // Evaluate remaining calc-colors (if we didn't already do the before_normal pass)
#if !((uking_normal_color >= 200 && uking_normal_color <= 213) || \
      (uking_enable_normalmap2 && ((uking_normal_color2 >= 200 && uking_normal_color2 <= 213) || \
                                   (uking_normalmap_blend_ratio >= 200 && uking_normalmap_blend_ratio <= 213))))
    EvalCalcColors(1);
#endif
    EvalCalcColors(0);

    // -------------------------------------------------------------------------
    // Albedo / specular / emission
    // -------------------------------------------------------------------------
#if uking_enable_texcoord0
    vec2 uv0 = fTexCoord01.xy;
#else
    vec2 uv0 = vec2(0.0);
#endif

    vec3 albedo = GetSource(uking_albedo_color).rgb;

#if uking_enable_lumberjack
    // Cut surface (trunk scale < 1): blend in annual-ring texture over the last 10%
    if (fLumberjack.z < 1.0)
        albedo = mix(GetSource(uking_lumberjack_annual_ring_albedo_color).rgb,
                     albedo,
                     Saturate(fma(fLumberjack.z, 10.0, -9.0)));
#endif

#if uking_enable_ao && uking_material_attribute != 0 && !UKING_LIGHT_ANALYSIS
    // Field materials apply AO to albedo; character materials handle it in lighting
    albedo *= GetSourceChannel(uking_ao_color, uking_ao_channel).x;
#endif

#if enable_bake_texture && !UKING_LIGHT_ANALYSIS
    // Baked occlusion map (bfres bake texture in cTexture4)
    albedo *= texture(cTexture4, fBakeCoord.xy).x;
#endif

#if !UKING_LIGHT_ANALYSIS && !UKING_GBUFFER_REDUCED
    albedo = ApplyMimicry(albedo, uv0);
#endif

    float specular = GetSourceChannel(uking_specmask_scaler, uking_specular_channel).x;

#if uking_enable_emission
    vec3 emission = GetSource(uking_emission_color).rgb;
#else
    vec3 emission = vec3(0.0);
#endif

    // -------------------------------------------------------------------------
    // Chemical ice
    // -------------------------------------------------------------------------
#define UKING_USES_LOCAL_POS \
    (uking_enable_proctexture0 && uking_proctexture0_type == 10 || \
     uking_enable_proctexture1 && uking_proctexture1_type == 10 || \
     uking_enable_proctexture2 && uking_proctexture2_type == 10 || \
     uking_enable_proctexture3 && uking_proctexture3_type == 10 || \
     uking_enable_proctexture4 && uking_proctexture4_type == 10 || \
     uking_enable_proctexture5 && uking_proctexture5_type == 10)

#if UKING_USES_LOCAL_POS
    // Ice noise follows the object when local position is available
    vec3 iceNoisePos = fLocalPosition.xyz * 0.3;
#else
    vec3 iceNoisePos = vec3(0.0);
#endif

#if UKING_LIGHT_ANALYSIS || UKING_GBUFFER_REDUCED
    bool ice = false;
#else
    bool ice = ApplyChemicalIce(albedo, emission, NdotV1, iceNoisePos);
#endif

    // -------------------------------------------------------------------------
    // Pack result
    // -------------------------------------------------------------------------
    SurfaceData sd;
    sd.viewPos     = viewPos;
    sd.V           = V;
    sd.vertexN     = vertexN;
    sd.N           = N;
    sd.Nraw        = Nraw;
    sd.Ls          = Ls;
    sd.worldPos    = worldPos;
    sd.screenUV    = screenUV;
    sd.volumeMask  = volumeMask;
    sd.uv0         = uv0;
    sd.lightRatio  = lightRatio;
    sd.NdotV       = NdotV;
    sd.NdotV1      = NdotV1;
    sd.specular    = specular;
    sd.albedo      = albedo;
    sd.emission    = emission;
    sd.ice         = ice;

    return sd;
}

// ======================= gsys_assign_material, variation 0: forward lit color =======================
#if uking_material_attribute == 0
// Character toon inputs (specular with the toon mask, metal, shadow ...), shared by the material and
// light analysis passes
CharaInput SetupCharaInput(SurfaceData sd)
{
    vec3  viewPos    = sd.viewPos;
    vec3  V          = sd.V;
    vec3  N          = sd.N;
    vec3  Ls         = sd.Ls;
    vec3  worldPos   = sd.worldPos;
    float NdotV1     = sd.NdotV1;
    float specular   = sd.specular;
    bool  ice        = sd.ice;
    float shadowFade = Saturate((-viewPos.z - env.cShadowFade.z) * 0.05);

    vec2  toon     = fToonSpecCoords.xy - floor(fToonSpecCoords.xy) - 0.5;
    float toonMask = Saturate(fma(toon.x, -toon.x, 1.0))
                   * Saturate(fma(toon.y, -toon.y, 1.0));

    float metal = 0.0;

#if uking_material_behave == 2
    // Metal mask/hybrid
    metal = MimicryActive() ? 0.0 : GetSourceChannel(uking_metal_color, uking_metal_channel).x;
    if (ice) metal = 1.0;
    specular *= Saturate(fma(toonMask, toonMask, metal));
#elif uking_material_behave == 104
    // Eye highlight
    metal = MimicryActive() ? 0.0 : GetSourceChannel(uking_metal_color, uking_metal_channel).x;
    if (ice) metal = 1.0;
#elif uking_material_behave != 10 && uking_material_behave != 1 && uking_material_behave != 100
    specular *= toonMask * toonMask;
#endif

    CharaInput ci;
    ci.N        = N;
    ci.V        = V;
    ci.Ls       = Ls;
    ci.fovRatio = 1.0 / (viewPos.z * -context.cFovParams.y);
    ci.NdotV1   = NdotV1;
    ci.specular = ice ? 1.0 : specular;

#if uking_enable_transmission
    // Character transmission: floor of the toon sun term
    ci.shadowAdd = ice ? 0.3 : GetSourceChannel(uking_transmission_color, uking_transmission_channel).x;
#else
    ci.shadowAdd = ice ? 0.3 : 0.0;
#endif

    ci.metal = metal;
    ci.T     = fViewTangent.xyz;

#if uking_enable_ao
    ci.ao = GetSourceChannel(uking_ao_color, uking_ao_channel).x;
#else
    ci.ao = 1.0;
#endif

#if uking_material_behave == 100
    // Hair anisotropic specular
    ci.hairSpec = MimicryActive() ? 0.0
                : CalcHairSpecular(N, fViewTangent.xyz, V, Ls)
                  * GetSourceChannel(uking_specular_hair, uking_specular_hair_channel).x;
    if (ice) ci.hairSpec = 1.0;
#else
    ci.hairSpec = 0.0;
#endif

#if uking_material_behave == 104
    float cascade = CalcCascadeShadow(viewPos, shadowFade, fma(metal, -0.05, -0.0015));
#else
    float cascade = CalcCascadeShadow(viewPos, shadowFade, (ice ? -0.05 : 0.0) - 0.0015);
#endif

    ci.shadow = CalcShadowChara(viewPos, worldPos, cascade);
    return ci;
}
#endif

void MaterialPass()
{
    // -------------------------------------------------------------------------
    // Unpack surface data
    // -------------------------------------------------------------------------
    SurfaceData sd = EvalSurface();

    vec3  viewPos    = sd.viewPos;
    vec3  V          = sd.V;
    vec3  N          = sd.N;
    vec3  Ls         = sd.Ls;
    vec3  worldPos   = sd.worldPos;
    vec3  albedo     = sd.albedo;
    vec3  emission   = sd.emission;
    vec2  volumeMask = sd.volumeMask;
    vec2  uv0        = sd.uv0;
    float lightRatio = sd.lightRatio;
    float NdotV      = sd.NdotV;
    float NdotV1     = sd.NdotV1;
    float specular   = sd.specular;
    bool  ice        = sd.ice;

    vec3 color;

    // -------------------------------------------------------------------------
    // Shared sky / fog / inscatter terms
    // -------------------------------------------------------------------------
    float skyDist = 1.0 - Saturate(fma(viewPos.z, -env.cSkyFogDistance.x, -env.cSkyFogDistance.y));

    // Cosine of azimuth between view and light (horizontal components)
    float cosAzimuth = inversesqrt(fma(Ls.y, -Ls.y, 1.0))
                     * inversesqrt(fma(V.y,  -V.y,  1.0))
                     * fma(V.z, Ls.z, V.x * Ls.x);

    float mu = fma(max(-fma(skyDist, dot(V, Ls) - cosAzimuth, cosAzimuth), -0.99), 0.5, 0.5);
    vec3 inscatter = SampleSkyInscatter(mu, skyDist);

    // Depth-based scatter weight
    float depthScatter = fma(viewPos.z, -0.005, -1.8);
    float lightScatter = Saturate((fLightAnalyzeColor.w - 0.3) * 2.5) * 0.1;
    float scatterW     = lightScatter + Saturate(fma(lightScatter, -depthScatter, depthScatter));
    scatterW = scatterW + fma(Saturate(volumeMask.x), -scatterW, Saturate(volumeMask.x));

    FogTerms fog = CalcFog(viewPos, worldPos, V, volumeMask);

    float shadowFade = Saturate((-viewPos.z - env.cShadowFade.z) * 0.05);

#if uking_material_attribute == 0
    // =========================================================================
    // Character toon lighting
    // =========================================================================
    CharaInput ci = SetupCharaInput(sd);

    vec3 lit = CalcCharaLighting(albedo, ci, fLightAnalyzeColor);

#else
    // =========================================================================
    // Field lighting
    // =========================================================================
    float gloss = specular * GetSourceChannel(uking_grossy_color, uking_grossy_channel).x;
    if (ice)
        gloss = fma(0.9 - gloss, mat.uk_kari_chemical_ice_ratio0, gloss);

#if uking_material_behave == 102 || uking_material_behave == 103
    // Glass / water: no normal bias on sky occlusion
    float normalBias = 0.0;
#elif uking_inner_material || uking_material_behave == 105
    // Inner material & leaf: no normal bias unless iced
    float normalBias = ice ? 1.0 : 0.0;
#else
    float normalBias = 1.0;
#endif

    vec3 worldN = MulRowsDir(context.cViewInv, N);

    LightInput li;
    li.N          = N;
    li.V          = V;
    li.Ls         = Ls;
    li.Hs         = normalize(V + env.cLightDirection0.xyz) * lightRatio;
    li.worldN     = worldN;
    li.worldR     = MulRowsDir(context.cViewInv, fma(N * NdotV, vec3(-2.0), V));
    li.NdotV      = NdotV;
    li.NdotL      = Saturate(dot(-Ls, N));
    li.volumeMask = volumeMask;
    li.farFade    = Saturate(fma(viewPos.z, -0.01, -0.5));

#if uking_material_behave == 102 || uking_material_behave == 103
    li.sky = CalcSkyOcclusion(viewPos, worldPos, worldN.y, normalBias, 0.0);
#else
    li.sky = CalcSkyOcclusion(viewPos, worldPos, worldN.y, normalBias, 1.3);
#endif

#if uking_material_behave == 103
    // Water: push cascade lookup 0.6 along the horizontal part of the normal
    vec3 upView    = vec3(context.cView[0].y, context.cView[1].y, context.cView[2].y);
    vec3 shadowPos = fma(fma(vec3(dot(N, upView)), -upView, N), vec3(0.6), viewPos);
    li.shadow = CalcShadow(viewPos, worldPos,
                           CalcCascadeShadowAt(viewPos, shadowPos, shadowFade, -0.002));
#else
    li.shadow = CalcShadow(viewPos, worldPos,
                           CalcCascadeShadow(viewPos, shadowFade, -0.002));
#endif

    vec3 analyzedAmbient = texture(cGSys23_LightAnalyzedData,
                                   vec2(fma(worldN.y, -0.5, 3.0) * 0.0833333358, 0.0)).xyz;

    Surface s;
    s.albedo = albedo;
    s.gloss  = gloss;
    s.metal  = MimicryActive() ? 0.0 : 1.0;

#if uking_material_behave == 2
    if (!MimicryActive())
        s.metal = GetSourceChannel(uking_metal_color, uking_metal_channel).x;
#endif

#if uking_enable_transmission && uking_material_behave == 105
    // Leaf transmission: 15% of the sun reaches the back side (on/off only)
    s.transmission = GetSourceChannel(uking_transmission_color, uking_transmission_channel).x > 0.0
                   ? 0.15 : 0.0;
#else
    s.transmission = 0.0;
#endif

#if uking_material_behave == 102
    // Glass
    vec3 lit = CalcLightingGlass(s, li, 0.08);

#elif uking_material_behave == 103
    // Water
#if uking_enable_fresnel_cheat
    // Fresnel cheat: reduce world-up component of the normal near the camera
    vec3  upV   = vec3(context.cView[0].y, context.cView[1].y, context.cView[2].y);
    float cheat = dot(N, upV) * fma(Saturate(1.0 / (viewPos.z * -context.cFovParams.y) * 25.0), -0.5, 0.5);
    vec3  Nf    = normalize(fma(vec3(cheat), -upV, N));

    li.N     = Nf;
    li.NdotV = dot(V, Nf);
    li.NdotL = Saturate(dot(-Ls, Nf));
#endif

    {
        // Water reflection: project view-space reflection onto a sphere
        // (radius = |viewPos| + 200) centered at env.data31
        vec3  R = fma(N * dot(V, N), vec3(-2.0), V);
        vec3  d = viewPos - env.data31.xyz;
        float r = length(viewPos) + 200.0;
        float b = dot(d, R);
        float t = -b + sqrt(b * b - fma(r, -r, dot(d, d)));

        li.worldR = MulRowsDir(context.cViewInv,
                               normalize(fma(R, vec3(t), viewPos) - env.data31.xyz));
    }

    vec3 lit = CalcLightingGlass(s, li, 0.02);

#else
    // Standard field lighting
    vec3 lit = CalcLighting(s, li, fog, inscatter, fLightAnalyzeColor, analyzedAmbient);
#endif
#endif // uking_material_attribute

    // -------------------------------------------------------------------------
    // Compose final color with fog / sky / inscatter
    // -------------------------------------------------------------------------
    color = fma(vec3(fog.skyAtten), lit,
            fma(vec3(fog.skyColorW), env.cSkyColor.xyz,
            fma(scatterW * inscatter, vec3(fog.inscatterW),
            fma(vec3(fog.fog2W), env.Fog2_Color.xyz,
                fog.fog1W * env.Fog1_Color.xyz))));

    // -------------------------------------------------------------------------
    // Output
    // -------------------------------------------------------------------------
#if gsys_weight > 0
    float alpha = mat.gsys_xlu_zprepass_alpha;          // skinned: no shape block
#else
    float alpha = shape.cShapeParam.x * mat.gsys_xlu_zprepass_alpha;
#endif

#if gsys_renderstate != 0
    // Translucent / alpha-blended: use transparency source
    float outAlpha = GetSourceChannel(uking_transparency_color, uking_transparency_channel).w * alpha;
#else
    float outAlpha = alpha;
#endif

    vec3 emissive = emission * (fog.skyAtten * fog.skyAtten);

    if (alpha < 1.0)
    {
        // Soft tone-map for translucent surfaces
        color    = fma(exp2(color * -0.360673755), vec3(-4.0), vec3(4.0));
        emissive = 1.0 - exp2(emissive * -1.44269502);
    }

#if uking_enable_emission
    color += emissive;
#endif

    oColor = vec4(color, outAlpha);
}

#if gsys_assign_type == GSYS_ASSIGN_GBUFFER
#include "PassGBuffer.glsl"
#endif
#include "PassMaterialVariations.glsl"

void main()
{
#if gsys_assign_type == GSYS_ASSIGN_ZONLY
    ZOnlyPass();
#elif gsys_assign_type == GSYS_ASSIGN_GBUFFER
    GBufferPass();
#elif gsys_assign_type == GSYS_ASSIGN_MATERIAL && gsys_assign_variation == 1
    CookPass();
#elif gsys_assign_type == GSYS_ASSIGN_MATERIAL && gsys_assign_variation == 2
    LightAnalysisPass();
#else
    MaterialPass();
#endif
}
