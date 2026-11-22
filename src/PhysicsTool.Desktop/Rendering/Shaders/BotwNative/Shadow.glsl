// Cascade depth shadow, projection-texture shadow and world (height) shadow
#ifndef UKING_SHADOW_GLSL
#define UKING_SHADOW_GLSL

// lookupPos: position projected into the cascade (the cascade index always comes from viewPos)
float CalcCascadeShadowAt(vec3 viewPos, vec3 lookupPos, float fade, float bias)
{
#if (BLENDER_RENDER == 1)
    // Blender: the plugin's cascade shadows are resolved to a screen-space texture (gsys_static_depth_shadow.r)
    return BlenderScreenShadow();
#else
    // Fully faded -> no shadow
    if (fade >= 1.0)
        return 1.0;

    // Select cascade based on view-space depth
    float depth = -viewPos.z;
    int cascade = int(depth > context.cShadowParam1.x)
                + int(depth > context.cShadowParam1.y);

    // Transform lookup position into the chosen cascade's shadow space
    mat4 cascadeMtx = context.cCascadeMtx[cascade];
    vec4 pos        = vec4(lookupPos, 1.0);

    float invW = 1.0 / dot(pos, cascadeMtx[3]);

    vec2 uv = vec2(
        dot(pos, cascadeMtx[0]) * invW,
        dot(pos, cascadeMtx[1]) * invW
    );

    float shadowDepth = dot(pos, cascadeMtx[2]) * invW + bias;

    // Sample the cascade shadow map (array slice = cascade index)
    float shadow = texture(cGSys07_DepthShadowCascade,
                           vec4(uv, float(cascade), shadowDepth));

    // Fade out the shadow toward the cascade boundary
    return mix(shadow, 1.0, fade);
#endif
}

float CalcCascadeShadow(vec3 viewPos, float fade, float bias) { return CalcCascadeShadowAt(viewPos, viewPos, fade, bias); }

void CalcShadowParts(vec3 viewPos, vec3 worldPos, float cascadeShadow, out float projShadow, out float depthMix)
{
#if (BLENDER_RENDER == 1)
    // Blender: no world height shadow or projection-texture (cloud) shadow
    depthMix = cascadeShadow;
    projShadow = Saturate(1.0 + sceneMat.uking_dynamic_proj_shadow_off);
#else
    vec3 worldShadowTex = textureLod(cGSys18_WorldShadow, vec2(fma(worldPos.x, 9.99999975e-05, 0.5), fma(worldPos.z, 0.000125000006, 0.5)), 0.0).xyz;
    float worldShadow = dot(worldShadowTex, env.cWorldShadowWeights.xyz);
    worldShadow *= worldShadow;

    float worldShadowFade = Saturate(1.0 - Saturate((-viewPos.z - env.cShadowFade.w) * 0.0500000007) + sceneMat.uking_dynamic_world_shadow_off);
#if uking_material_attribute != 0 && uking_material_behave == 105
    depthMix = cascadeShadow;   // field_leaf: no world height shadow
#else
    depthMix = fma(worldShadowFade, cascadeShadow - worldShadow, worldShadow);
#endif

    vec4 wp = vec4(worldPos, 1.0);
    float invW = 1.0 / dot(wp, context.cProjectionTexMtx0[2]);
    vec2 projUV = vec2(dot(wp, context.cProjectionTexMtx0[0]), dot(wp, context.cProjectionTexMtx0[1])) * invW;
    projShadow = Saturate(textureLod(cGSys04_Projction0, projUV, viewPos.z * -0.00499999989).x + sceneMat.uking_dynamic_proj_shadow_off);
#endif
}

// Combined direct-light shadow term (projection texture * depth/world shadow)
float CalcShadow(vec3 viewPos, vec3 worldPos, float cascadeShadow)
{
    float projShadow, depthMix;
    CalcShadowParts(viewPos, worldPos, cascadeShadow, projShadow, depthMix);
    return projShadow * Saturate(fma(depthMix, sceneMat.uking_dynamic_depth_shadow_scale, sceneMat.uking_dynamic_depth_shadow_off));
}

// character variant: hard threshold on the depth shadow
float CalcShadowChara(vec3 viewPos, vec3 worldPos, float cascadeShadow)
{
    float projShadow, depthMix;
    CalcShadowParts(viewPos, worldPos, cascadeShadow, projShadow, depthMix);
    return projShadow * sceneMat.uking_dynamic_depth_shadow_scale *
        Saturate((Saturate(depthMix + sceneMat.uking_dynamic_depth_shadow_off) - 0.400000006) * 10.0);
}

#endif
