// Forward lighting of the material pass (diffuse ambient/irradiance, GGX sun specular, env specular)
#ifndef UKING_LIGHTING_GLSL
#define UKING_LIGHTING_GLSL

struct Surface
{
    vec3 albedo;
    float gloss;       // 1 = mirror (temp "gloss"; roughness = 1 - gloss)
    float metal;       // field metal: behave 1 = 1, behave 2 = uking_metal_color; 0 while mimicry is active
    float transmission; // uking_enable_transmission: 0.15 or 0
};

struct LightInput
{
    vec3 N;            // view normal (normalized)
    vec3 V;            // normalized view position (eye -> point)
    vec3 Ls;           // cLightDirection0 * base_light_change_ratio
    vec3 Hs;           // normalize(V + cLightDirection0) * base_light_change_ratio
    vec3 worldN;
    vec3 worldR;
    float NdotV;
    float NdotL;
    float sky;         // sky occlusion
    float shadow;      // CalcShadow()
    vec2 volumeMask;
    float farFade;     // clamp(-z * 0.01 - 0.5)
};

vec3 CalcLighting(Surface s, LightInput li, FogTerms fog, vec3 inscatter, vec4 lightAnalyze, vec3 analyzedAmbient)
{
    float rough = 1.0 - s.gloss;

    // -------------------------------------------------------------------------
    // Irradiance from environment cube (optionally bent, then re-saturated)
    // -------------------------------------------------------------------------
#if uking_material_behave == 105
    // field_leaf: bend irradiance direction upwards
    vec3 irrDir = li.worldN + vec3(0.0, 1.0, 0.0);
#else
    vec3 irrDir = li.worldN;
#endif

    vec3 irr = textureLod(cGSys07_CubeEnvMap, CubeDir(irrDir), 3.0).xyz;

    float irrMax = max(irr.x, max(irr.y, irr.z));
    float irrMin = min(irr.x, min(irr.y, irr.z));

    // Desaturation factor
    float desat = fma(irrMin, -(1.0 / (irrMax + 1e-10)), 1.0);

    // Saturation scale driven by lightAnalyze
    float satInput = Saturate(fma(li.sky, -0.1, desat) + 0.1);
    float satScale = lightAnalyze.z
                   * exp2(lightAnalyze.y * log2(satInput))
                   * (1.0 / (desat + 1e-10));

    // Re-saturated irradiance
    vec3 irrAdj = (irr - irrMin) * satScale
                + fma(irrMax - irrMin, -satScale, irrMax);

    // Ambient contribution
    float ambScale = fma(li.sky, lightAnalyze.x * 0.5, lightAnalyze.x * 0.5);
    vec3 amb = fma(vec3(li.farFade),
                   fma(irrAdj, vec3(-ambScale), analyzedAmbient),
                   irrAdj * ambScale);

    // Hemisphere volume modulation
    float vol = li.volumeMask.y * env.cHemiSkyColor.w;
    amb = fma(amb, fma(vec3(vol), env.cHemiSkyColor.xyz, vec3(-vol)), amb);

    // -------------------------------------------------------------------------
    // Sun diffuse (with transmission support)
    // -------------------------------------------------------------------------
    float sunVis = li.NdotL * li.shadow;
    sunVis = sunVis + fma(sunVis, -s.transmission, s.transmission);   // mix with transmission

    float diffuse = sunVis * Saturate(fma(li.sky, 0.5, li.NdotL) + 0.5);

    // -------------------------------------------------------------------------
    // Sun specular (GGX)
    // -------------------------------------------------------------------------
    float a  = Saturate(fma(rough, rough, 0.01));
    float a2 = a * a;

    float NdotH = Saturate(dot(-li.Hs, li.N));
    NdotH *= NdotH;

    float NdotV = Saturate(-li.NdotV);

    // Smith geometric term constants
    float k  = fma(rough, 0.5, 0.5);
    k *= k;
    float kH = k * 0.5;
    float k1 = fma(k, -0.5, 1.0);

    float LdotH = 1.0 - Saturate(dot(li.Ls, li.Hs));

    // Base reflectance F0 (material-dependent)
#if uking_material_behave == 1
    // Metal: albedo-tinted reflectance
    vec3 F0 = fma(s.albedo, vec3(s.metal), vec3(1.0 - s.metal));
#elif uking_material_behave == 2
    // Hybrid: mix(0.04, albedo, metal)
    vec3 F0 = fma(vec3(1.0 - s.metal), vec3(0.04), s.metal * s.albedo);
#else
    vec3 F0 = vec3(0.04);
#endif

    // Specular Fresnel (Schlick)
    float Fs = Pow5(LdotH);
    vec3  F  = Fs + fma(vec3(Fs), -F0, F0);   // mix(F0, 1.0, Fs)

    // Dynamic light ratio^4
    float r  = sceneMat.uking_dynamic_base_light_change_ratio;
    float r4 = r * r;
    r4 *= r4;

    // GGX distribution denominator
    float dTerm = fma(NdotH, a2, -NdotH) + 1.0;

    // Analytical specular
    vec3 spec = F
              * li.NdotL
              * li.shadow
              * sceneMat.uking_dynamic_cloud_ratio
              * r4
              * a2
              * (1.0 / (dTerm * dTerm))           // D
              * (1.0 / fma(k1, NdotV, kH))        // G1(V)
              * (1.0 / fma(li.NdotL, k1, kH));    // G1(L)

    // -------------------------------------------------------------------------
    // Environment specular (IBL)
    // -------------------------------------------------------------------------
    float fv  = 1.0 - NdotV;
    float fv2 = fv * fv;
    float Fe  = fv2 * fv * fv2 * (1.0 / fma(s.gloss, -3.0, 4.0));   // approx. environment Fresnel

#if uking_material_behave == 1 || uking_material_behave == 2
    float envBase = fma(s.metal, 0.25, 0.25);   // 0.25 .. 0.5
#else
    float envBase = 0.25;
#endif

    vec3 envF = (envBase + fma(li.sky, -envBase, li.sky))
              * (Fe + fma(F0, vec3(-Fe), F0));   // mix(F0, 1, Fe) * sky modulation

    vec3 envSpec = textureLod(cGSys07_CubeEnvMap, CubeDir(li.worldR), rough * 3.0).xyz
                 * envF;

    // Transmissive surfaces have no specular
    float specOn = (s.transmission > 0.0) ? 0.0 : 1.0;
    spec    *= specOn;
    envSpec *= specOn;

#if uking_material_behave == 105
    envSpec *= 0.75;
#endif

    // -------------------------------------------------------------------------
    // Final composition
    // -------------------------------------------------------------------------
    vec3 lit = fma(vec3(diffuse), env.cLightColor.xyz, amb)
             * Saturate(fma(s.albedo, vec3(1.07320976), vec3(-0.000528555829)));

#if uking_material_behave == 1 || uking_material_behave == 2
    lit *= 1.0 - s.metal;   // metals have no diffuse
#endif

    lit = fma(lit, vec3(0.931784332),
              fma(spec * env.cLightColor.xyz, vec3(0.25), envSpec))
        + 0.000492500025;

    return lit;
}

// field_glass / field_water (uking_material_behave 102 / 103): F0 0.08 / 0.02, no analyzed ambient (raw irradiance cube), sun visibility = sky occlusion * shadow
vec3 CalcLightingGlass(Surface s, LightInput li, float F0)
{
    float rough = 1.0 - s.gloss;
    float visibility = li.sky * li.shadow;

    // GGX alpha and alpha^2
    float a  = Saturate(fma(rough, rough, 0.01));
    float a2 = a * a;

    // N.H term (squared)
    float NdotH = Saturate(dot(-li.Hs, li.N));
    NdotH *= NdotH;

    float NdotV = Saturate(-li.NdotV);

    // Smith geometric term constants
    float k  = fma(rough, 0.5, 0.5);
    k *= k;
    float kH = k * 0.5;
    float k1 = fma(k, -0.5, 1.0);

    // Specular Fresnel (Schlick)
    float Fs = Pow5(1.0 - Saturate(dot(li.Ls, li.Hs)));

    // Dynamic light ratio (raised to the 4th power)
    float r  = sceneMat.uking_dynamic_base_light_change_ratio;
    float r4 = r * r;
    r4 *= r4;

    // GGX distribution denominator
    float dTerm = fma(a2, NdotH, -NdotH) + 1.0;

    // Analytical specular highlight
    float spec = fma(1.0 - Fs, F0, Fs)          // Fresnel
               * li.NdotL
               * visibility
               * sceneMat.uking_dynamic_cloud_ratio
               * r4
               * a2
               * (1.0 / (dTerm * dTerm))        // D term
               * (1.0 / fma(NdotV, k1, kH))     // G1(V)
               * (1.0 / fma(li.NdotL, k1, kH)); // G1(L)

    // Environment Fresnel / energy term
    float e = Pow5(1.0 - NdotV) * Saturate(visibility + 0.8);

    // Image-based specular
    vec3 envSpec = textureLod(cGSys07_CubeEnvMap, CubeDir(li.worldR), rough * 3.0).xyz
                 * fma(1.0 - e, F0, e);

    // Irradiance (diffuse IBL)
    vec3 irradiance = textureLod(cGSys07_CubeEnvMap, CubeDir(li.worldN), 3.0).xyz;

    // Diffuse lighting
    vec3 diffuse = fma(vec3(1.0 - e), irradiance,
                       Saturate(visibility + 0.25) * (1.0 - Fs) * env.cLightColor.xyz)
                 * Saturate(fma(s.albedo, vec3(1.07320976), vec3(-0.000528555829)));

    // Final combination
    return fma(diffuse, vec3(0.931784332),
               fma(spec * env.cLightColor.xyz, vec3(0.25), envSpec))
         + 0.000492500025;
}
#endif
