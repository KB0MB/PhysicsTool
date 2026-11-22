// Sky occlusion, sky inscatter and height / distance fog terms
#ifndef UKING_SKY_GLSL
#define UKING_SKY_GLSL

// heightOffset: 1.3 normally, 0 for field_glass
float CalcSkyOcclusion(vec3 viewPos, vec3 worldPos, float worldNormalY, float normalBias, float heightOffset)
{
#if (BLENDER_RENDER == 1)
    return 1.0;   // Blender: no sky occlusion map
#else
    vec4 P = env.cSkyOcclusionParam;
    float occ = textureLod(cGSys13_SkyOcclusion, vec2(fma(worldPos.x, P.z, -P.x), fma(worldPos.z, P.z, -P.y)), 0.0).x;
    float h = fma(occ, -P.w, -worldPos.y + P.w - fma(worldNormalY * normalBias, 2.0, -normalBias)) + heightOffset;
    float t = 1.0 - Saturate(h * 0.25);
    t = t * t * t;
    float fadeOut = Saturate(fma(viewPos.z, -0.0500000007, -3.5)) + sceneMat.uking_dynamic_skyocclusion_off;
    return Saturate(t + fma(fadeOut, -t, fadeOut));
#endif
}

struct FogTerms
{
    float skyAtten;     // temp_142 in the decompile: weight of the lit surface color
    float skyColorW;    // weight of env.cSkyColor
    float inscatterW;   // weight of cGSys12_SkyInscatter
    float fog1W;        // weight of env.Fog1_Color
    float fog2W;        // weight of env.Fog2_Color
};

FogTerms CalcFog(vec3 viewPos, vec3 worldPos, vec3 V, vec2 volumeMask)
{
    FogTerms f;

    // Distance-based sky fog factor
    float skyDist = 1.0 - Saturate(fma(viewPos.z, -env.cSkyFogDistance.x, -env.cSkyFogDistance.y));

    // Volume attenuation (mainly from volumeMask.y)
    float volume = fma(volumeMask.y, -0.85, 1.0);

    // View-space up vector
    vec3 upView = vec3(context.cView[0].y,
                       context.cView[1].y,
                       context.cView[2].y);

    // Horizon falloff
    float horizon = fma(pow(Saturate(dot(V, upView)), env.cSkyParam.z),
                        -env.cSkyParam.w,
                        1.0);

    // Height-based term
    float heightTerm = pow(
        1.0 - Saturate(fma(viewPos.z, -env.cSkyHeightParam.x, -env.cSkyHeightParam.y)),
        env.cSkyParam.x
    );

    // Sky color weight
    float skyColorW = volume
                    * fma(horizon, -heightTerm, horizon)
                    * env.cSkyColor.w;

    // Inscatter weight
    float inscatter = volume
                    * fma(pow(skyDist, env.cSkyInscatterParam.x),
                          -env.cSkyInscatterParam.z,
                          env.cSkyInscatterParam.z);

    // Height fog (Fog1)
    float fog1 = volume
               * Saturate(fma(-worldPos.y + env.cFogHeightBase.x,
                              env.Fog1_Param.x,
                              env.Fog1_Direction.w))
               * env.Fog1_Color.w;

    // Distance fog (Fog2)
    float fog2 = volumeMask.y
               * pow(Saturate(fma(viewPos.z, -env.Fog2_Param.x, env.Fog2_Direction.w)),
                     env.Fog2_Param.y)
               * env.Fog2_Color.w;

#if gsys_renderstate == 2 && !gsys_gbuffer_xlu
    // Special blend state: only sky color + height fog
    // (no inscatter, no distance fog)
    inscatter = 0.0;
    fog2      = 0.0;
#endif

    // Sequential weight composition
    float rem = 1.0 - skyColorW;
    float a   = fma(inscatter, -rem, rem); // remaining after sky + inscatter

    f.skyColorW  = skyColorW;
    f.inscatterW = inscatter * rem;

    float b = fma(fog1, -a, a); // remaining after fog1

    f.fog1W    = fog1 * a;
    f.fog2W    = fog2 * b;
    f.skyAtten = fma(fog2, -b, b); // final remaining attenuation

    return f;
}

vec3 SampleSkyInscatter(float mu, float skyDist)
{
    float x = fma(-AcosApprox(mu), 0.636619747, 1.0);
    float p = pow(skyDist, env.cSkyInscatterParam.y);
    float y = fma(fma(p, -env.cSkyInscatterParam.w, p) + env.cSkyInscatterParam.w, 0.5, 0.5);
#if (BLENDER_RENDER == 1)
    return env.cSkyColor.xyz;   // Blender: no inscatter lookup table, use the sky color
#else
    return texture(cGSys12_SkyInscatter, vec2(x, y)).xyz;
#endif
}
#endif
