// gsys_assign_material: forward toon shading of characters (colour + emission, atmosphere applied)
#include "Effects.glsl"
#include "Shadow.glsl"
#include "Sky.glsl"
#include "LightingChara.glsl"
#include "MaterialSurface.glsl"

layout (location = 0) out vec4 oColor;

void main()
{
    // ---------------- geometry ----------------
    vec3 viewPos = fViewPosition.xyz;
    vec3 V = normalize(viewPos);
    vec3 world = vec3(Row3(cInvView0, viewPos), Row3(cInvView1, viewPos), Row3(cInvView2, viewPos));
    vec3 local = fLocalPos.xyz;
    vec2 uv0 = fTexCoord01.xy;
    AlphaTest();
    vec2 screenUV = fScreenPos.xy * (1.0 / fScreenPos.w);
    vec3 volumeMask = texture(cTex_VolumeMask, screenUV).xyz;
    vec4 wsh = textureLod(cTex_WorldShadowHeight, WorldShadowUV(world), 0.0);
    float lightRatio = SceneMat.p_dynamic_base_light_change_ratio;
    vec3 Ls = eLightDir.xyz * lightRatio;

    vec3 vertexN = normalize(fViewNormal.xyz);
    vec3 N = ApplyNormalMap(vertexN, fViewTangent, TexSample(2).xy);
    float rim = Saturate(dot(V, N) + 1.0);
    ExprRim = rim;

    // ---------------- material ----------------
    MaterialSurface surf = EvalMaterialSurface(N, Ls, rim, local, volumeMask.z);
    vec3 albedo = surf.albedo, emission = surf.emission;
    float spec = surf.spec, metal = surf.metal, ao = surf.ao, transmission = surf.transmission;

    if (Mat.p_kari_mottled_ratio > 0.0) { albedo = vec3(0.0); emission = vec3(0.0); }

    // damage flash, debug colours
    emission = mix(emission, Mat.p_damage_color.rgb, Mat.p_damage_color.w);
    albedo = fma(albedo * (1.0 - Mat.p_damage_color.w), Mat.p_debug_mul_color.rgb, Mat.p_debug_add_color.rgb);
    EmissionEffects(albedo, emission, local, rim);

    // ---------------- shadow ----------------
    int cascade = CascadeIndex(viewPos.z);
    float shadow = CascadeShadow(viewPos, N, vertexN, Ls, metal, cascade);
    shadow = Saturate((mix(shadow, 1.0, SceneMat.p_dynamic_depth_shadow_off) * SceneMat.p_dynamic_depth_shadow_scale - 0.349999994) * 8.0)
           * EnvironmentShadow(world, viewPos, wsh);

    // ---------------- lighting ----------------
    CharaInput c;
    c.albedo = albedo;
    c.spec = spec * (1.0 - Mat.p_damage_color.w);
    c.N = N; c.V = V; c.Ls = Ls; c.rim = rim;
    c.shadow = shadow; c.shadowStrength = fAmbient.w;
    c.ao = ao; c.transmission = transmission; c.metal = metal;
    c.pixelSize = 1.0 / (viewPos.z * -cFovParams.y);
    c.ambient = AmbientColor(volumeMask);

    Atmosphere atm = EvalAtmosphere(viewPos, V, Ls, world, volumeMask);
    vec3 color = CharaLighting(c) * atm.transmittance + atm.add;
    emission *= atm.emissionScale;

    // translucent pass (gsys_xlu_zprepass_alpha < 1): soft-clip colour to [0, 4) and emission to [0, 1)
    bool opaque = Mat.gsys_xlu_zprepass_alpha >= 1.0;
    if (!opaque)
    {
        color = fma(exp2(color * -0.360673755), vec3(-4.0), vec3(4.0));
        emission = 1.0 - exp2(emission * -1.44269502);
    }

    // Depths: desaturate towards max^2 and darken in the dark
    float mf = MinusFieldLight(world);
    float m = max(max(color.r, color.g), color.b);
    float tint = fma(mf, 0.5, 0.5) * mix(1.0, eMinusFieldTint.z, Saturate(fma(world.y, eMinusFieldTint.x, eMinusFieldTint.y)));
    oColor.rgb = tint * mix(vec3(m * m), color, fma(mf, 0.800000012, 0.200000003)) + emission;
    oColor.a = Mat.gsys_xlu_zprepass_alpha;
}
