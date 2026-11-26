// Character toon lighting of the forward (material) pass
#ifndef TOTK_LIGHTING_CHARA_GLSL
#define TOTK_LIGHTING_CHARA_GLSL

// ambient colour tinted inside the volume mask
vec3 AmbientColor(vec3 volumeMask)
{
    float k = volumeMask.z * eAmbientTint.w;
    return fAmbient.rgb * (1.0 + k * (eAmbientTint.rgb - 1.0));
}

struct CharaInput
{
    vec3  albedo;
    float spec;         // specular mask (already without damage)
    vec3  N, V, Ls;     // shading normal, view vector, light direction scaled by the light change ratio
    float rim;          // sat(1 + V.N)
    float shadow;       // 0 = shadowed, 1 = lit (depth shadow * environment shadow)
    float shadowStrength; // fAmbient.w
    float ao;           // occlusion (o_ao_color), lifted by the figure parameter
    float transmission; // o_transmission_color (chemical ice: 0.3)
    float metal;
    float pixelSize;    // 1 / (-viewZ * fov y)
    vec3  ambient;
};

vec3 CharaLighting(CharaInput c)
{
    float aMax = max(max(c.albedo.b, c.albedo.g), c.albedo.r);
    float aInv = 1.0 / (aMax + 1.00000001E-10);
    float satur = 1.0 - min(min(c.albedo.b, c.albedo.g), c.albedo.r) * aInv;

    // shadow term: blend to 1 by the shadow strength (fAmbient.w)
    float sStr = Saturate(c.shadowStrength);
    float shadowed = 1.0 + sStr * (c.shadow - 1.0);
    float lit = Saturate(c.shadow + c.shadowStrength);

    float aoLit = Saturate((c.ao - 0.5) * 4.0);
    float terminator = Saturate((Saturate(-dot(c.N, eLightDir.xyz)) - 0.400000006) * fma(c.pixelSize, 7.5, 75.0));
    vec3  Hmetal = vec3(c.Ls.x * -1.5, 0.600000024, 1.0) * inversesqrt(fma(c.Ls.x * -1.5, c.Ls.x * -1.5, 0.360000014) + 1.0);

#if o_material_behave == 104
    // eye: no specular step; light and ambient are dimmed up close, the iris highlight (the "metal" input,
    // procedural discs) is added on top in the light colour
    float nearL = fma(Saturate(c.pixelSize * 0.600000024), 0.600000024, 0.400000006);
    float nearH = fma(Saturate(c.pixelSize * 0.300000012), 0.600000024, 0.400000006);
    float toon = terminator * shadowed * aoLit * nearL * SceneMat.p_dynamic_base_light_change_ratio;
    vec3  albedo = c.albedo;
#elif o_material_behave == 1
    // metal: sharp highlight in a fixed view-space direction (follows only the light's x), exponent e^(3 + spec^2),
    // boosted where the reflection looks at the sky; the highlight adds to the toon light instead of changing the albedo.
    // the shadow is applied at half strength.
    float specSq = c.spec * c.spec;
    float hl = Pow(Saturate(dot(c.N, Hmetal)), exp2((specSq + 3.0) * 1.44269502));
    vec3  R = fma(c.N * dot(c.N, c.V), vec3(-2.0), c.V);
    float sky = fma(dot(R, cUpView), 0.5, 0.199999988);
    float halfShadow = fma(sStr * c.shadow - sStr, 0.5, 1.0);
    float steps = Saturate(fma(Saturate((hl - 0.140000001) * 70.0), 0.300000012, Saturate((hl - 0.00499999989) * 100.0) * 0.200000003));
    float highlight = Saturate(specSq * steps * Saturate(Saturate(sky * 8.0 * sky) + 0.899999976) * 8.0 * halfShadow);
    float toon = fma(highlight, 3.0, Saturate(fma(aoLit, halfShadow * terminator, c.transmission))) * SceneMat.p_dynamic_base_light_change_ratio;
    vec3  albedo = c.albedo;
#else
    // toon highlight: two steps of a sharp lobe around H = -(0.3 V + L), fades with distance
    // (behave 2 metal parts: the fixed metal direction)
  #if o_material_behave == 2
    vec3  H = c.metal > 0.0 ? Hmetal : normalize(-0.300000012 * c.V - c.Ls);
  #else
    vec3  H = normalize(-0.300000012 * c.V - c.Ls);
  #endif
  #if o_material_behave == 101
    float hl = c.rim * Saturate(Pow(Saturate(dot(c.N, H)), 20.0));      // skin: tighter lobe, linear rim
  #else
    float hl = Pow(c.rim, 0.400000006) * Saturate(Pow(abs(Saturate(dot(c.N, H))), 12.0));
  #endif
    float spec = c.spec * c.spec * Saturate(c.pixelSize * 1.20000005) * hl;
    float highlight = Saturate(Saturate(fma(Saturate((spec - 0.0500000007) * 20.0), 0.119999997, Saturate((spec - 0.00999999978) * 50.0) * 0.0500000007))
                               * Saturate(shadowed + 0.0799999982));

    // the highlight desaturates and brightens the albedo
    float keep = Saturate(satur * (1.0 - highlight)) * (1.0 / (satur + 1.00000001E-10));
    float bright = Pow(abs(aMax), 0.600000024);
    float scale = Saturate(fma(highlight, fma((1.0 - satur) * bright, 3.0, bright), aMax));
    vec3  albedo = mix(vec3(aMax), c.albedo, keep) * scale * aInv;

  #if o_material_behave == 101
    // skin: the terminator blends to a subsurface term seen against the light (transmission), no clamp
    vec3  H2 = normalize(c.V + c.Ls);
    float back0 = Saturate(-dot(c.V, c.Ls));
    float sss = Saturate(back0 * Saturate(fma(Saturate(-dot(c.N, H2)), c.transmission, -0.5) * 40.0) * 0.150000006);
    float toonRaw = aoLit * mix(sss, shadowed, terminator);
  #else
    // two-tone light: sharp terminator (wider when far away), transmission adds light
    float toonRaw = Saturate(fma(shadowed * terminator, aoLit, c.transmission));
  #endif
    float toon = toonRaw * SceneMat.p_dynamic_base_light_change_ratio;
#endif
    float back = Saturate(Saturate(-dot(c.V, c.Ls)) * 5.0);
    float ambScale = 1.0 + back * (Saturate(aoLit + 0.800000012) - 1.0);

    vec3 lightColor = mix(0.800000012 * c.ambient, eLightColor.rgb, lit) * SceneMat.p_dynamic_toon_light_adjust_for_demo.rgb;
    vec3 ambient = c.ambient;
#if o_material_behave == 101
    // skin: warm fill where the toon light does not reach (scaled by the shadow strength)
    ambient += c.shadowStrength * (1.0 - toonRaw) * lightColor * vec3(0.0799999982, 0.0399999991, 0.0);
#endif
#if o_material_behave == 104
    vec3 light = lightColor * toon + ambient * nearL * 1.79999995 * ambScale;
    return albedo * light + fma(lightColor, vec3(0.800000012), vec3(0.200000003)) * (nearH * c.metal);
#else
    vec3 light = lightColor * toon + ambient * ambScale;
    return albedo * light;
#endif
}

#endif
