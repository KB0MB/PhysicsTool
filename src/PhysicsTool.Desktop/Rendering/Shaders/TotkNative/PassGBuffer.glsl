// gsys_assign_gbuffer: deferred G-buffer of static objects (no gameplay effects); layout in GBuffer.glsl
#include "GBuffer.glsl"

layout (location = 0) out vec4 oGBuffer0;
layout (location = 1) out vec4 oGBuffer1;
layout (location = 3) out vec4 oGBuffer3;
#if o_enable_emission
layout (location = 5) out vec4 oGBuffer5;
#endif

void main()
{
    AlphaTest();
    MarkTile();

    vec3 vertexN = normalize(fViewNormal.xyz);
    vec3 N = ApplyNormalMap(vertexN, fViewTangent, TexSample(2).xy);
#if o_expression_post_normal != 0
    ExprRim = Saturate(dot(normalize(fViewPosition.xyz), N) + 1.0);
#endif

    vec3 albedo = GetSource(o_albedo_color).rgb;
    float specMask = GetSourceChannel(o_specmask_scaler, o_specular_channel).x;
#if HAS_TOON_SPEC_COORD
    float pattern = ToonSpecPattern(fToonSpecCoord.xy);
#else
    float pattern = 1.0;
#endif
#if o_material_behave == 104
    float spec = specMask;
#elif o_material_behave == 1 || o_material_behave == 2
    float spec = specMask * Saturate(pattern + (o_material_behave == 1 ? 1.0 : GetSourceChannel(o_metal_color, o_metal_channel).x));
#else
    float spec = pattern * specMask;
#endif

    // shading model and the RT1 flag byte
#if o_material_behave == 1
    float metal = 1.0;
#elif o_material_behave == 2
    float metal = GetSourceChannel(o_metal_color, o_metal_channel).x;
#elif o_material_behave == 101
  #if o_enable_transmission
    float transmission = GetSourceChannel(o_transmission_color, o_transmission_channel).x;
    float metal = transmission > 0.0 ? transmission : 0.0;
  #else
    float metal = 0.0;
  #endif
#elif o_material_behave == 104
    float metal = GetSourceChannel(o_metal_color, o_metal_channel).x;   // iris highlight
#else
    float metal = 0.0;
#endif
    float model = ShadingModel(metal);
    int flags = (int(trunc(metal * 255.0)) & 248) | 4;
#if o_material_behave == 101 && o_enable_transmission
    if (transmission > 0.0) flags |= 2;
#endif
#if o_enable_emission
    flags |= 1;
#endif

#if o_enable_ao
    float ao = GetSourceChannel(o_ao_color, o_ao_channel).x;
#else
    float ao = 1.0;
#endif

    oGBuffer0 = vec4(Pack8(int(model)), Mat.p_object_attribute, 0.0, 0.0);
    oGBuffer1 = vec4(albedo, Pack8(flags));
    oGBuffer3 = vec4(EncodeNormal(N, ao), Pack8((int(trunc(spec * 255.0)) & 252) | 2));
#if o_enable_emission
    oGBuffer5 = vec4(GetSource(o_emission_color).rgb, 1.0);
#endif
}
