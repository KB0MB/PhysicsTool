// gsys_assign_dynamic0: forward pass for object effects drawn over the scene. Four cases, first one that applies:
//   cooking (p_cook_ratio)          RT1 = cooked albedo (browned in HSV, burnt by cTex_PreShadow.y)
//   attachment (p_attachment_effect) RT0 = cTex_ColorBuffer refracted by noise + a rim glow (Fuse preview)
//   special effect A                RT0 = blueprint ghost fresnel in the ghost colour (Autobuild)
//   otherwise the centre texel of cTex_GBuffAlbedo picks the output:
//     .y > 0.5  discard (Ultrahand voxel view: the game loops over the voxel grid here but nothing in the loop is kept)
//     .x > 0.5  RT0 = unshadowed toon colour, w = Instancing.data[0].x
//     else      RT0 = (linear depth, instance id * 0.01 + 0.01, 0, 0)
#include "Effects.glsl"
#include "LightingChara.glsl"
#include "MaterialSurface.glsl"

layout (location = 0) out vec4 oColor;
layout (location = 1) out vec4 oCook;

uniform sampler2D cTex_PreShadow;
uniform sampler2D cTex_GBuffAlbedo;
uniform sampler2D cTex_ColorBuffer;

// cooking: rgb -> hsv, saturation + 0.1, back to rgb, browned, then screen-blended towards a burnt colour
vec3 CookColor(vec3 c, float burnMask)
{
    // rgb -> hsv (branchless form)
    vec4 p = c.g < c.b ? vec4(c.b, c.g, -1.0, 0.666666687) : vec4(c.g, c.b, 0.0, -0.333333343);
    vec4 q = c.r < p.x ? vec4(p.x, p.y, p.w, c.r) : vec4(c.r, p.y, p.z, p.x);
    float d = q.x - min(q.w, q.y);
    float h = abs(fma(1.0 / fma(d, 6.0, 1.00000001E-10), q.w - q.y, q.z));
    float s = Saturate(fma(d, 1.0 / (q.x + 1.00000001E-10), 0.100000001));
    vec3 k = Saturate(abs(fma(fract(vec3(h + 1.0, h + 0.666666687, h + 0.333333343)), vec3(6.0), vec3(-3.0))) - 1.0);
    vec3 rgb = fma(fma(vec3(s), k, vec3(-s)), vec3(q.x), vec3(q.x)) * vec3(0.838918269, 0.687082112, 0.544931769);
    vec3 burnt = Saturate((burnMask - 0.600000024) * 100.0) * vec3(0.206960559, 0.14346157, 0.0734196007);
    return Saturate(rgb + fma(rgb, -burnt, burnt));
}

void main()
{
    AlphaTest();
    vec3 V = normalize(fViewPosition.xyz);
    vec2 screenUV = fScreenPos.xy * (1.0 / fScreenPos.w);
    vec3 vertexN = normalize(fViewNormal.xyz);
    vec3 N = ApplyNormalMap(vertexN, fViewTangent, TexSample(2).xy);
    vec3 Ls = eLightDir.xyz * SceneMat.p_dynamic_base_light_change_ratio;
    ExprRim = Saturate(dot(V, N) + 1.0);
    vec3 albedo = GetSource(o_albedo_color).rgb;
    if (Mat.p_attachment_effect > 0.0) albedo = vec3(0.0);   // (also seen by the cooking branch)

    if (Mat.p_cook_ratio > 0.0)
    {
        oColor = vec4(0.0);
        oCook = vec4(CookColor(albedo, texture(cTex_PreShadow, screenUV).y), 1.0);
        return;
    }
    if (Mat.p_attachment_effect > 0.0)
    {
        // refraction strength fades out between 1 and 10 units from the camera
        float strength = fma(Saturate(fma(fViewPosition.z, -0.111111112, -0.111111112)), -0.00700000022, 0.00700000022);
        float off = fma(texture(cTex_Proc3DNoise, fLocalPos.xyz * Mat.p_attachment_noise_scale).x * strength, 2.0, -strength);
        vec3 scene = texture(cTex_ColorBuffer, screenUV + off * Mat.gsys_xlu_zprepass_alpha).rgb;
        float rim = Saturate((Saturate(dot(vertexN, V) + 1.0) - 0.800000012) * 100.0);
        vec3 glow = fma(vec3(rim), vec3(1.1265837, 2.25420928, 1.1699791), vec3(0.0974550992, 0.514371157, 0.265283525));
        oColor = vec4(fma(glow * Mat.gsys_xlu_zprepass_alpha, vec3(Mat.gsys_xlu_zprepass_alpha), scene), 1.0);
        oCook = vec4(0.0);
        return;
    }
    if (Mat.p_special_effect_A > 0.0)
    {
        // object attribute bits 74 set = "alternate" ghost, else "shortage" ghost (not enough parts)
        uint attr = uint(max(trunc(Mat.p_object_attribute * 255.0), 0.0));
        vec4 ghost = (attr & 74u) == 74u ? SceneMat.p_const_blueprint_ghost_alternate_color : SceneMat.p_const_blueprint_ghost_shotage_color;
        vec4 e = SceneMat.p_const_blueprint_edge_param;
        float fres = Pow(abs(1.0 - abs(dot(vertexN, V))), e.w) + e.x;
        float k = Saturate(fma(fres, Mat.p_special_effect_A, -e.y) * (1.0 / (e.z - e.y)));
        oColor = ghost * k * Mat.gsys_xlu_zprepass_alpha;
        oCook = vec4(0.0);
        return;
    }

    vec2 mode = texture(cTex_GBuffAlbedo, vec2(0.5)).xy;
    if (mode.y > 0.5)
        discard;
    if (mode.x > 0.5)
    {
        // the material-pass toon shading, fully lit (no shadow maps) and without the surface-replacing effects
        vec2 sm = MaterialSpecMetal(0.0);
        CharaInput c;
        c.albedo = albedo; c.spec = sm.x;
        c.N = N; c.V = V; c.Ls = Ls; c.rim = ExprRim;
        c.shadow = 1.0; c.shadowStrength = fAmbient.w;
#if o_enable_ao
        c.ao = GetSourceChannel(o_ao_color, o_ao_channel).x;
#else
        c.ao = 1.0;
#endif
#if o_enable_transmission
        c.transmission = GetSourceChannel(o_transmission_color, o_transmission_channel).x;
#else
        c.transmission = 0.0;
#endif
        c.metal = sm.y;
        c.pixelSize = 1.0 / (fViewPosition.z * -cFovParams.y);
        c.ambient = AmbientColor(texture(cTex_VolumeMask, screenUV).xyz);
        vec3 color = CharaLighting(c);
#if o_enable_emission
        vec3 emission = GetSource(o_emission_color).rgb;
#else
        vec3 emission = vec3(0.0);
#endif
        if (!(Mat.gsys_xlu_zprepass_alpha >= 1.0))
        {
            color = fma(exp2(color * -0.360673755), vec3(-4.0), vec3(4.0));
            emission = 1.0 - exp2(emission * -1.44269502);
        }
        oColor = vec4(color + emission, Instancing.data[0].x);
    }
    else
        oColor = vec4((fViewPosition.z + cNearFarParam.x) * -cNearFarParam2.x, fma(fLocalPos.w, 0.00999999978, 0.00999999978), 0.0, 0.0);
    oCook = vec4(0.0);
}
