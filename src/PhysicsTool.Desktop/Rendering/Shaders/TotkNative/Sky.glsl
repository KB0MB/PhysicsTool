// Atmosphere applied in the forward (material) pass: sky inscatter, haze, distance fogs, ground fog.
// Depths (minus field) darkness.
#ifndef TOTK_SKY_GLSL
#define TOTK_SKY_GLSL

struct Atmosphere
{
    vec3  add;            // in-scattered light added after shading
    float transmittance;  // what is left of the shaded colour
    float emissionScale;  // applied to emission
};

// V: normalized view vector, Ls: light direction (scaled), volumeMask: cTex_VolumeMask at the pixel
Atmosphere EvalAtmosphere(vec3 viewPos, vec3 V, vec3 Ls, vec3 world, vec3 volumeMask)
{
    float dist = length(viewPos);
    float fogVol = volumeMask.z * SceneShadingF(104);            // volume fog (mask z, global strength)
    float cloud  = 1.0 - 0.850000024 * volumeMask.y;
    float dim    = cloud * fma(fogVol, -0.850000024, 1.0);

    // sky inscatter lookup: u = angle to the light (horizontal angle up close, 3D angle far away), v = distance
    float near  = 1.0 - Saturate(fma(dist, eInscatterDist.x, -eInscatterDist.y));
    float cosH  = inversesqrt(1.0 - V.y * V.y) * inversesqrt(1.0 - Ls.y * Ls.y) * (V.x * Ls.x + V.z * Ls.z);
    float c     = fma(max(-mix(cosH, dot(V, Ls), near), -0.99000001), 0.5, 0.5);
    vec2  uv    = vec2(fma(-FastAcos(c), 0.636619747, 1.0), fma(mix(Pow(near, eInscatterParam.y), 1.0, eInscatterParam.w), 0.5, 0.5));
    vec3  inscatter = texture(cTex_SkyInscatter, uv).rgb;
    float inscatterAmt = dim * eInscatterParam.z * (1.0 - Pow(near, eInscatterParam.x));

    // haze: stronger towards the horizon (view vector against world up) and with distance
    float up   = Saturate(dot(V, cUpView));
    float hz   = 1.0 - Pow(up, eHazeParam.z) * eHazeParam.w;
    float haze = hz * (1.0 - Pow(1.0 - Saturate(fma(dist, eHazeDist.x, -eHazeDist.y)), eHazeParam.x)) * eHazeColor.w * dim;

    float fogA = volumeMask.y * Pow(Saturate(fma(dist, eFogA_Param.x, eFogA_Start.w)), eFogA_Param.y) * eFogA_Color.w;
    float fogB = fogVol * Pow(Saturate(fma(dist, eFogB_Param.x, eFogB_Start.w)), eFogB_Param.y) * eFogB_Color.w;
    float ground = cloud * (1.0 - fogVol) * (1.0 - fogVol) * Saturate(fma(eGroundFogY.z - world.y, eGroundFogY.y, eGroundFogY.x)) * eGroundFog.w;

    // inscatter mask: SceneShadingInfo[20] (vertex) and the volume mask x
    float m = 0.100000001 * Saturate((fShadingParam.x - 0.300000012) * 2.50000024);
    float t = fma(dist, 0.00499999989, -1.79999995);
    float mask = mix(m + Saturate(t * (1.0 - m)), 1.0, volumeMask.x);

    // layers composite front to back: haze, inscatter, ground fog, fog A / B
    float tHaze = 1.0 - haze;
    float tIns  = tHaze * (1.0 - inscatterAmt);
    float tGnd  = tIns * (1.0 - ground);
    Atmosphere a;
    a.add = haze * eHazeColor.rgb + mask * inscatter * (inscatterAmt * tHaze) + (ground * tIns) * eGroundFog.rgb
          + (tGnd * fogA) * eFogA_Color.rgb + (tGnd * fogB) * eFogB_Color.rgb;
    a.transmittance = tGnd * (1.0 - fogA) * (1.0 - fogB);
    a.emissionScale = a.transmittance * a.transmittance * (1.0 - Pow(0.0, eUnknown70.z) * eUnknown70.w);
    return a;
}

// ---------------- Depths (minus field) ----------------
// 1 = lit, 0 = dark. Inside the light-map rectangle the baked light map (Lightroots) decides,
// outside a darkness texture blended over two height ramps.
float MinusFieldLight(vec3 world)
{
    vec2 uv = (world.xz - eMinusFieldRect.xy) * eMinusFieldRect.z;
    if (uv.y > 0.0 && uv.x > 0.0 && uv.x < 1.0 && uv.y < 1.0)
        return texture(cTex_MinusFieldLightMap, uv).w;
    float floorV = 1.0 - eMinusFieldDark.x;
    float h = 1.0 - Saturate(fma(world.y, eMinusFieldFadeY.z, eMinusFieldFadeY.w));
    float d = mix(eMinusFieldDark.y, eMinusFieldDark.z, texture(cTex_MinusFieldDarkness, fma(world.xz, eMinusFieldDarkUV.xy, eMinusFieldDarkUV.zw)).y);
    return Saturate(fma(d, h, floorV * (1.0 - Saturate(fma(world.y, eMinusFieldFadeY.x, eMinusFieldFadeY.y))) + eMinusFieldDark.x));
}

#endif
