// gsys_assign_dynamic3: G-buffer for objects with gameplay effects (layout in GBuffer.glsl). Unlike the static
// G-buffer it runs the material surface (mimicry, chemical ice, figure, blueprint), mottling, damage / debug colours
// and the proc discard cut, and always writes emission.
#include "Effects.glsl"
#include "GBuffer.glsl"
#include "MaterialSurface.glsl"

layout (location = 0) out vec4 oGBuffer0;
layout (location = 1) out vec4 oGBuffer1;
layout (location = 3) out vec4 oGBuffer3;
layout (location = 5) out vec4 oGBuffer5;

void main()
{
    AlphaTest();
    vec3 V = normalize(fViewPosition.xyz);
    vec3 local = fLocalPos.xyz;
    vec3 volumeMask = texture(cTex_VolumeMask, fScreenPos.xy * (1.0 / fScreenPos.w)).xyz;
    vec3 Ls = eLightDir.xyz * SceneMat.p_dynamic_base_light_change_ratio;
    vec3 N = ApplyNormalMap(normalize(fViewNormal.xyz), fViewTangent, TexSample(2).xy);
    float rim = Saturate(dot(V, N) + 1.0);
    ExprRim = rim;

    MaterialSurface m = EvalMaterialSurface(N, Ls, rim, local, volumeMask.y);
    vec3 albedo = m.albedo, emission = m.emission;

    // mottling (camouflage-like dark patches from two octaves of noise)
    if (Mat.p_kari_mottled_ratio > 0.0)
    {
        vec3 p = local * (1.0 / Mat.p_mottled_scale);
        float n0 = texture(cTex_Proc3DNoise, p * 3.5999999).x;
        float n = fma(texture(cTex_Proc3DNoise, p * 0.720000029).x - n0, 0.699999988, n0);
        float k = Saturate((n - Mat.p_kari_mottled_ratio) * 20.0);
        albedo *= k; emission *= k;
    }
    emission = mix(emission, Mat.p_damage_color.rgb, Mat.p_damage_color.w);
    albedo = fma(albedo * (1.0 - Mat.p_damage_color.w), Mat.p_debug_mul_color.rgb, Mat.p_debug_add_color.rgb);

    // proc discard: cut and burning edge (same noise as the shadow pass and the material pass glow)
    if (Mat.p_proc_discard < 1.0)
    {
        vec3 s = local * Mat.p_edit_proc_discard_scale;
        float warp = texture(cTex_Proc3DNoise, s * 0.850000024).x * 2.5;
        float n = texture(cTex_Proc3DNoise, vec3(s.x * 0.5, s.y * 0.119999997, fma(s.z, 0.5, warp))).x;
        if (n < Saturate((1.0 - Mat.p_proc_discard - 0.150000006) * 1.17647052)) discard;
        float k = Saturate((1.0 - Mat.p_proc_discard) * 2.5);
        emission = fma(vec3(k), fma(vec3(Pow(abs(n), 1.79999995)), vec3(0.100000009, 5.55000019, 10.0), vec3(0.200000003, 0.449999988, 4.0)), emission);
    }

    // shading model: figure and blueprint write 0, chemical ice 10
    float model = ShadingModel(m.metal);
    if (Mat.p_figure > 0.0) model = 0.0;
    else if (Mat.p_kari_chemical_ice_ratio0 > 0.0) model = 10.0;
    else if (Mat.p_blue_print_alternative > 0.0 || Mat.p_blue_print_alternative_vanishing < 1.0) model = 0.0;

    float metal = m.transmission > 0.0 ? m.transmission : m.metal;
    int flags = (int(trunc(metal * 255.0)) & 248) | 4 | (m.transmission > 0.0 ? 2 : 0) | 1;

    vec3 n = EncodeNormal(N, m.ao);
    if ((int(trunc(model * 255.0 / 255.0)) & 8) == 0)
    {
        // models without bit 3 (figure / blueprint) store sqrt(ao) in 6 bits instead
        int signs = (int(N.x > 0.0) << 1) | int(N.y > 0.0);
        n.z = Pack8((int(trunc(sqrt(m.ao) * 255.0)) & 252) | signs);
    }
    float spec = m.spec * (1.0 - Mat.p_damage_color.w);

    MarkTile();
    oGBuffer0 = vec4(Pack8(int(model)), Mat.p_object_attribute, 0.0, 0.0);
    oGBuffer1 = vec4(albedo, Pack8(flags));
    oGBuffer3 = vec4(n, Pack8((int(trunc(spec * 255.0)) & 252) | 2 | int(m.effectFlag > 0.0)));
    oGBuffer5 = vec4(emission, 1.0);
}
