// Character (uking_material_attribute == 0) toon lighting
#ifndef UKING_LIGHTING_CHARA_GLSL
#define UKING_LIGHTING_CHARA_GLSL

struct CharaInput
{
    vec3  N;          // normal-mapped view normal (normalized)
    vec3  V;
    vec3  Ls;         // cLightDirection0 * base_light_change_ratio
    float fovRatio;   // 1 / (-z * cFovParams.y)
    float NdotV1;     // clamp(dot(N, V) + 1)
    float specular;   // specular mask (toon ramp already applied)
    float shadow;     // CalcShadowChara()
    float shadowAdd;  // 0.3 under chemical ice
    float metal;      // chara field_hybrid (behave 2) metal mask
    vec3  T;          // view tangent (raw varying), hair
    float hairSpec;   // chara_hair anisotropic term (CalcHairSpecular)
    float ao;         // chara_skin: raw uking_ao_color value (1 when AO is off)
};

// -----------------------------------------------------------------------------
// chara_hair (behave 100): Kajiya-Kay style highlight along the tangent
// -----------------------------------------------------------------------------
float CalcHairSpecular(vec3 N, vec3 T, vec3 V, vec3 Ls)
{
    // Approximate half-vector biased toward the light
    vec3 H = fma(Ls, vec3(200.0), V);
    H *= inversesqrt(dot(H, H));

    float NdotV = dot(N, V);
    float tH    = dot(T, H);
    float tR    = dot(T, fma(N * NdotV, vec3(-2.0), V));   // reflected tangent

    float fres = 1.0 - Saturate(NdotV + 1.0);

    // Anisotropic highlight term
    float term = Saturate(
        fma(sqrt(Saturate(fma(tH, -tH, 1.0))),
            sqrt(Saturate(fma(tR, -tR, 1.0))),
            -tR * tH)
    );

    return exp2(log2(term) * 60.0) * (fres - fres * (1.0 - fres));
}

// -----------------------------------------------------------------------------
// Character AO helpers
// -----------------------------------------------------------------------------
float CharaAO(CharaInput c)
{
#if uking_enable_ao
    return Saturate((c.ao - 0.5) * 4.0);
#else
    return 1.0;
#endif
}

float CharaFillScale(CharaInput c)
{
#if uking_enable_ao
    float backLit = Saturate(Saturate(-dot(c.V, c.Ls)) * 5.0);
    return fma(Saturate(CharaAO(c) + 0.8), backLit, -backLit) + 1.0;
#else
    return 1.0;
#endif
}

// -----------------------------------------------------------------------------
// Common toon light combination (sun + fill)
// -----------------------------------------------------------------------------
vec3 CharaLight(float sun, float lit, vec4 lightAnalyze, CharaInput c)
{
    vec3 toonSun = fma(lightAnalyze.xyz, vec3(-0.8), env.cLightColor.xyz);
    vec3 mixed   = fma(vec3(lit), toonSun, lightAnalyze.xyz * 0.8)
                 * sceneMat.uking_dynamic_toon_light_adjust_for_demo.xyz;

    return fma(vec3(sun), mixed, lightAnalyze.xyz * CharaFillScale(c));
}

float CharaSunTerm(float shadowMix, CharaInput c)
{
    float NdotL = Saturate(-dot(c.N, env.cLightDirection0.xyz));
    float term  = Saturate((NdotL - 0.4) * fma(c.fovRatio, 7.5, 75.0));

    return Saturate(fma(shadowMix * term, CharaAO(c), c.shadowAdd));
}

// -----------------------------------------------------------------------------
// Saturation-preserving albedo boost driven by the toon highlight ramp
// -----------------------------------------------------------------------------
vec3 CharaToneAlbedo(vec3 albedo, float ramp)
{
    float aMax = max(albedo.x, max(albedo.y, albedo.z));
    float aMin = min(albedo.x, min(albedo.y, albedo.z));
    float inv  = 1.0 / (aMax + 1e-10);

    float minRatio = aMin * inv;
    float desat    = 1.0 - minRatio;

    float p06 = exp2(log2(aMax) * 0.6);

    float k    = Saturate(fma(ramp, -desat, desat))
               * (1.0 / max(desat + 1e-10, 1e-10));
    float base = fma(aMax - aMin, -k, aMax);
    float gain = Saturate(fma(ramp, fma(p06 * minRatio, 3.0, p06), aMax)) * inv;

    return fma(vec3(k), albedo - aMin, vec3(base)) * gain;
}

// Toon highlight amount: specular lobe * view-size-scaled specular mask squared (feeds the albedo tone ramp)
float CharaHighlight(CharaInput c)
{
    // Half-vector for specular lobe
    vec3 Hb = fma(c.V, vec3(-0.3), -c.Ls);
    Hb *= inversesqrt(dot(Hb, Hb));

    float specAmt = Saturate(c.fovRatio * 1.2) * c.specular * c.specular;
    float NdotH   = Saturate(dot(c.N, Hb));

#if uking_material_behave == 2
    // Hybrid metal: override NdotH with anisotropic term when metal > 0
    if (c.metal > 0.0)
    {
        float hx   = c.Ls.x * -1.5;
        float invH = inversesqrt(fma(hx, hx, 0.36) + 1.0);
        NdotH = Saturate(fma(c.N.z, invH,
                             fma(c.N.y, invH * 0.6, c.N.x * hx * invH)));
    }
#endif

#if uking_material_behave == 101
    // chara_skin: tighter specular lobe
    float specPow = c.NdotV1 * Saturate(exp2(log2(NdotH) * 20.0));
#else
    float specPow = exp2(log2(c.NdotV1) * 0.4)
                  * Saturate(exp2(log2(NdotH) * 12.0));
#endif
    return specPow * specAmt;
}

// Albedo tone ramp (how far the toon highlight brightens the albedo). shadowTerm is the shadow mix in the
// material pass and the raw character shadow in light analysis.
float CharaToneRamp(CharaInput c, vec3 albedo, float shadowTerm)
{
#if uking_material_behave == 100
    // chara_hair: lobe ^ 6 at 6x view scale, plus the anisotropic hair highlight
    vec3 Hb = fma(c.V, vec3(-0.3), -c.Ls);
    Hb *= inversesqrt(dot(Hb, Hb));
    float aMax     = max(albedo.x, max(albedo.y, albedo.z));
    float aMin     = min(albedo.x, min(albedo.y, albedo.z));
    float minRatio = aMin * (1.0 / (aMax + 1e-10));
    float desat    = 1.0 - minRatio;
    float fov6 = Saturate(c.fovRatio * 6.0);
    float lobe = Saturate(exp2(log2(Saturate(dot(c.N, Hb))) * 6.0)) * fov6 * c.specular * c.specular;
    float aniso = Saturate(fma(fov6, c.hairSpec * lobe, -0.14) * 70.0);
    return Saturate(
        Saturate(shadowTerm + 0.5) *
        fma(Saturate(fma(aMax, 1.9, desat) + 0.1) *
            Saturate(Saturate((lobe - 0.14) * 70.0) * 0.7),
            0.1,
            fma(aniso, -Saturate(aMax * Saturate(minRatio) * 3.0), aniso) * 0.15));
#else
    float highlight = CharaHighlight(c);
    return Saturate(Saturate(shadowTerm + 0.08) *
                    Saturate(fma(Saturate((highlight - 0.05) * 20.0), 0.12,
                                 Saturate((highlight - 0.01) * 50.0) * 0.05)));
#endif
}

// -----------------------------------------------------------------------------
// Main character lighting entry point
// -----------------------------------------------------------------------------
vec3 CalcCharaLighting(vec3 albedo, CharaInput c, vec4 lightAnalyze)
{
#if uking_material_behave == 100
    // ===== chara_hair ========================================================
    vec3 Hb = fma(c.V, vec3(-0.3), -c.Ls);
    Hb *= inversesqrt(dot(Hb, Hb));

    float aMax     = max(albedo.x, max(albedo.y, albedo.z));
    float aMin     = min(albedo.x, min(albedo.y, albedo.z));
    float inv      = 1.0 / (aMax + 1e-10);
    float minRatio = aMin * inv;
    float desat    = 1.0 - minRatio;

    float law       = Saturate(lightAnalyze.w);
    float shadowMix = fma(c.shadow, law, -law) + 1.0;
    float lit       = Saturate(c.shadow + lightAnalyze.w);

    float ramp = CharaToneRamp(c, albedo, shadowMix);

    float sun0 = CharaSunTerm(shadowMix, c);
    float rim  = fma(lightAnalyze.w, -sun0, lightAnalyze.w);

    vec3 toonLight = fma(vec3(lit),
                         fma(lightAnalyze.xyz, vec3(-0.8), env.cLightColor.xyz),
                         lightAnalyze.xyz * 0.8)
                   * sceneMat.uking_dynamic_toon_light_adjust_for_demo.xyz;

    float sun = sun0 * sceneMat.uking_dynamic_base_light_change_ratio;

    // Tone albedo
    float k    = Saturate(fma(desat, -ramp, desat))
               * (1.0 / max(desat + 1e-10, 1e-10));
    float base = fma(k, -(aMax - aMin), aMax);
    float p06  = exp2(log2(aMax) * 0.6);
    float gain = Saturate(fma(ramp, fma(minRatio * p06, 3.0, p06), aMax)) * inv;
    vec3 toned = fma(vec3(k), albedo - aMin, vec3(base)) * gain;

    vec3 light = fma(toonLight, vec3(sun),
                     fma(rim * toonLight, vec3(0.08), lightAnalyze.xyz) * CharaFillScale(c));

    return fma(Saturate(fma(toned, vec3(1.07320976), vec3(-0.000528555829))) * light,
               vec3(0.931784332),
               vec3(0.000492500025));

#elif uking_material_behave == 104
    // ===== chara_eye =========================================================
    // Strong light-analyze fill; c.metal = highlight mask (e.g. procedural eye disc)
    float law       = Saturate(lightAnalyze.w);
    float shadowMix = fma(c.shadow, law, -law) + 1.0;

    float NdotL = Saturate(-dot(c.N, env.cLightDirection0.xyz));
    float g     = fma(Saturate(c.fovRatio * 0.6), 0.6, 0.4);
    float highlight = fma(Saturate(c.fovRatio * 0.3), 0.6, 0.4) * c.metal;

    float sunTerm = Saturate((NdotL - 0.4) * fma(c.fovRatio, 7.5, 75.0));
    float sun = g * fma(shadowMix - 1.0, sunTerm, sunTerm)
              * sceneMat.uking_dynamic_base_light_change_ratio;

    vec3 toonLight = fma(vec3(Saturate(c.shadow + lightAnalyze.w)),
                         fma(lightAnalyze.xyz, vec3(-0.8), env.cLightColor.xyz),
                         lightAnalyze.xyz * 0.8)
                   * sceneMat.uking_dynamic_toon_light_adjust_for_demo.xyz;

    vec3 diffuse = fma(toonLight, vec3(sun), lightAnalyze.xyz * g * 1.8)
                 * Saturate(fma(albedo, vec3(1.07320976), vec3(-0.000528555829)));

    return fma(diffuse, vec3(0.931784332),
               highlight * fma(toonLight, vec3(0.8), vec3(0.2)))
         + 0.000492500025;

#elif uking_material_behave == 1
    // ===== chara_metal =======================================================
    // Anisotropic-ish highlight around a fixed half-vector, no albedo re-saturation
    float law       = Saturate(lightAnalyze.w);
    float shadowMix = fma(fma(c.shadow, law, -law), 0.5, 1.0);

    float hx   = c.Ls.x * -1.5;
    float invH = inversesqrt(fma(hx, hx, 0.36) + 1.0);

    float specAmt = c.specular * c.specular;
    float nh = exp2(
        exp2((specAmt + 3.0) * 1.44269502) *
        log2(Saturate(fma(c.N.z, invH,
                          fma(c.N.y, invH * 0.6, c.N.x * hx * invH))))
    );

    float ramp = Saturate(
        fma(Saturate((nh - 0.14) * 70.0), 0.3,
            Saturate((nh - 0.005) * 100.0) * 0.2)
    );

    float angle = Saturate(dot(c.N, -c.Ls) + 0.9);
    float sun = fma(Saturate(shadowMix * 8.0 * specAmt * ramp * angle), 3.0,
                    CharaSunTerm(shadowMix, c))
              * sceneMat.uking_dynamic_base_light_change_ratio;

    vec3 light = CharaLight(sun, Saturate(c.shadow + lightAnalyze.w), lightAnalyze, c);

    return fma(light * Saturate(fma(albedo, vec3(1.07320976), vec3(-0.000528555829))),
               vec3(0.931784332),
               vec3(0.000492500025));

#else
    // ===== Default toon path (skin, hybrid, glossy, etc.) ====================

    float highlight = CharaHighlight(c);

#if uking_material_behave == 10
    // chara_grossy: half shadow, glossy lobe (base is compile-time 0 -> ramp 0)
    float shadowMix = fma(fma(c.shadow, Saturate(lightAnalyze.w), -Saturate(lightAnalyze.w)), 0.5, 1.0);
    float ramp = Saturate(
        shadowMix *
        Saturate((exp2(fma(c.specular * c.specular, 1.5, 1.0) * log2(0.0)) - 0.4) * 75.0) *
        0.15
    );
#else
    float shadowMix = fma(c.shadow, Saturate(lightAnalyze.w), -Saturate(lightAnalyze.w)) + 1.0;
    float ramp = CharaToneRamp(c, albedo, shadowMix);
#endif

    // Saturation-preserving albedo boost
    float aMax     = max(albedo.x, max(albedo.y, albedo.z));
    float aMin     = min(albedo.x, min(albedo.y, albedo.z));
    float inv      = 1.0 / (aMax + 1e-10);
    float minRatio = aMin * inv;
    float desat    = 1.0 - minRatio;
    float p06      = exp2(log2(aMax) * 0.6);

    float k    = Saturate(fma(ramp, -desat, desat))
               * (1.0 / max(desat + 1e-10, 1e-10));
    float base = fma(aMax - aMin, -k, aMax);
    float gain = Saturate(fma(ramp, fma(p06 * minRatio, 3.0, p06), aMax)) * inv;
    vec3 toned = fma(vec3(k), albedo - aMin, vec3(base)) * gain;

    // Light combination
    float lit = Saturate(c.shadow + lightAnalyze.w);

#if uking_material_behave == 101
    // chara_skin: back-light scatter + warm ambient rim
    vec3 Hf = c.V + c.Ls;
    Hf *= inversesqrt(dot(Hf, Hf));

    float scatter = Saturate(
        Saturate(-dot(c.V, c.Ls)) *
        Saturate(fma(c.shadowAdd, Saturate(-dot(c.N, Hf)), -0.5) * 40.0) *
        0.15
    );

    float NdotL = Saturate(-dot(c.N, env.cLightDirection0.xyz));
    float sun0  = fma(shadowMix - scatter,
                      Saturate((NdotL - 0.4) * fma(c.fovRatio, 7.5, 75.0)),
                      scatter);
    sun0 *= CharaAO(c);

    float fillScale = CharaFillScale(c);

    vec3 toonLight = fma(vec3(lit),
                         fma(lightAnalyze.xyz, vec3(-0.8), env.cLightColor.xyz),
                         lightAnalyze.xyz * 0.8)
                   * sceneMat.uking_dynamic_toon_light_adjust_for_demo.xyz;

    float rim  = fma(lightAnalyze.w, -sun0, lightAnalyze.w);
    vec3  fill = fma(rim * toonLight, vec3(0.08, 0.04, 0.0), lightAnalyze.xyz);

    vec3 light = fma(toonLight,
                     vec3(sun0 * sceneMat.uking_dynamic_base_light_change_ratio),
                     fill * fillScale);
#else
    float sun  = CharaSunTerm(shadowMix, c) * sceneMat.uking_dynamic_base_light_change_ratio;
    vec3 light = CharaLight(sun, lit, lightAnalyze, c);
#endif

    return fma(Saturate(fma(toned, vec3(1.07320976), vec3(-0.000528555829))) * light,
               vec3(0.931784332),
               vec3(0.000492500025));
#endif
}

#endif // UKING_LIGHTING_CHARA_GLSL