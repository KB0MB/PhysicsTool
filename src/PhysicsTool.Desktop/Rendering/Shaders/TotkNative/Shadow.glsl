// Shadows: cascaded depth shadow (4-tap PCF), projected shadow, sky-island shadow, world shadow height
#ifndef TOTK_SHADOW_GLSL
#define TOTK_SHADOW_GLSL

// cascade = how many split depths the fragment is behind
int CascadeIndex(float viewZ)
{
    return int(-viewZ > cCascadeSplit.x) + int(-viewZ > cCascadeSplit.y) + int(-viewZ > cCascadeSplit.z);
}

// N: shading normal, vertexN: vertex normal, Ls: light direction (scaled by the light change ratio),
// push: extra offset along N (chemical ice)
float CascadeShadow(vec3 viewPos, vec3 N, vec3 vertexN, vec3 Ls, float push, int cascade)
{
    // normal offset: blend of both normals, pushed perpendicular to the light, more at grazing angles
    vec3  n   = normalize(N * eShadowNormal.x + vertexN * eShadowNormal.y);
    float NdL = dot(Ls, n);
    float off = Pow(abs(1.0 - abs(NdL)), eShadowNormal.z) * eShadowBias.x;
    vec3  p   = viewPos + (n - Ls * NdL) * off + N * 0.5 * push;

    float w = 1.0 / Row3(cShadowRow(cascade, 3), p);
    vec2  uv = vec2(Row3(cShadowRow(cascade, 0), p), Row3(cShadowRow(cascade, 1), p)) * w;
    float depth = max(Row3(cShadowRow(cascade, 2), p) * w - eShadowBias.y, 9.99999975e-06);

    // cascades are packed into array layers and mip levels
    int   perLayer = eCascadeLayout.x, perLod = eCascadeLayout.y;
    float layer = float(int(clamp(uint(max(roundEven(float(RemU(cascade, perLayer))), 0.0)), 0u, 65535u)));
    float lod   = float(DivS(cascade, perLod));
    vec2  o = SceneMat.p_const_depth_shadow_chara_pcf * 0.5 * eShadowTexel.xy * (lod + 1.0);
    float s = textureLod(cTex_DepthShadowCascade, vec4(uv.x - o.x, uv.y - o.y, layer, depth), lod)
            + textureLod(cTex_DepthShadowCascade, vec4(uv.x + o.x, uv.y - o.y, layer, depth), lod)
            + textureLod(cTex_DepthShadowCascade, vec4(uv.x - o.x, uv.y + o.y, layer, depth), lod)
            + textureLod(cTex_DepthShadowCascade, vec4(uv.x + o.x, uv.y + o.y, layer, depth), lod);
    s *= 0.25;

    // fade out at the far end of the cascade range
    float fade = cascade == 3 ? Saturate(eCascadeFade.w - viewPos.z * eCascadeFade.z) : Saturate(eCascadeFade.y - viewPos.z * eCascadeFade.x);
    return mix(s, 1.0, fade);
}

// world shadow height map: 1 / 10000 world units per texel in x, 1 / 8000 in z, centred on the origin
vec2 WorldShadowUV(vec3 world) { return vec2(fma(world.x, 9.99999975e-05, 0.5), fma(world.z, 0.000125000006, 0.5)); }

// shadows that only apply below the world shadow height (terrain / sky islands / projected clouds)
float EnvironmentShadow(vec3 world, vec3 viewPos, vec4 wsh)
{
    float dist = length(viewPos);
    vec2  wuv  = WorldShadowUV(world);
    if (!(world.y < fma(wsh.w, 1200.0, 100.0)))
        return 1.0;

    float ws = dot(wsh.xyz, eWorldShadowDir.xyz); ws *= ws;
    ws = mix(ws, 1.0, SceneMat.p_dynamic_world_shadow_off);
    float wsFade = Saturate(fma(dist, eWorldShadowFade.x, eWorldShadowFade.y));

    float island = textureLod(cTex_SkyIslandShadow, wuv - SceneMat.p_const_sky_island_shadow_offset_scale * eLightDirWorld.xz, viewPos.z * -0.00300000003).x;
    island = Saturate(1.0 - SceneMat.p_const_sky_island_shadow_density * (1.0 - island));

    float pw = 1.0 / Row3(cProjTexMtx2, world);
    float proj = textureLod(cTex_Projection0, vec2(Row3(cProjTexMtx0, world), Row3(cProjTexMtx1, world)) * pw, viewPos.z * -0.00499999989).x;
    proj = mix(proj, 1.0, SceneMat.p_dynamic_proj_shadow_off);

    float s = proj * island * mix(1.0, ws, wsFade);
    // fade in below the height-map top and with the Env height ramp
    float h = min(1.0 - Saturate((world.y - wsh.w * 1200.0) * 0.00999999978), 1.0 - Saturate(fma(world.y, eMinusFieldFadeY.x, eMinusFieldFadeY.y)));
    return mix(1.0, s, h);
}

#endif
