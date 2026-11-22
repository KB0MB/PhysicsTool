#ifndef UKING_EFFECTS_GLSL
#define UKING_EFFECTS_GLSL

// skip mimicry / chemical ice: gbuffer variations 2 / 0 (reduced / minimal) and material variations 1 / 2 (cook / light analysis)
#if (gsys_assign_type == GSYS_ASSIGN_GBUFFER && gsys_assign_variation != 1) || (gsys_assign_type == GSYS_ASSIGN_MATERIAL && gsys_assign_variation != 0)
#define UKING_NO_MAT_EFFECTS 1
#else
#define UKING_NO_MAT_EFFECTS 0
#endif

bool MimicryActive()
{
    if (UKING_NO_MAT_EFFECTS != 0)
        return false;

    // Active when the ratio has a fractional part (same test as ApplyMimicry)
    float ratio = mat.uk_mimicly_ratio;
    return floor(ratio) < ratio;
}

// Mimicry: blend albedo towards a layer of cTerrainAlbedo. Layer = floor(uk_mimicly_ratio), blend = fract.
vec3 ApplyMimicry(vec3 albedo, vec2 uv)
{
    float ratio = mat.uk_mimicly_ratio;
    float layerF = floor(ratio);
    // Skip if unused
    if (UKING_NO_MAT_EFFECTS != 0 || layerF >= ratio)
        return albedo;

    // Convert layer index to a safe 16-bit unsigned integer range
    // (roundEven + clamp to [0, 65535])
    float layer = float(clamp(uint(max(roundEven(layerF), 0.0)), 0u, 0xFFFFu));
    // Sample the corresponding terrain layer
    vec3 terrain = texture(cTerrainAlbedo, vec3(uv, layer)).xyz;
    // Lerp between original albedo and terrain based on the fractional part
    float frac = ratio - layerF;
    return mix(albedo, terrain, frac);
}

// Chemical (freeze) ice. Returns true when active. Albedo and emission are pulled towards the ice color.
bool ApplyChemicalIce(inout vec3 albedo, inout vec3 emission, float NdotV1, vec3 noisePos)
{
    float iceRatio = mat.uk_kari_chemical_ice_ratio0;
    // Skip if ice is unused
    if (UKING_NO_MAT_EFFECTS != 0 || iceRatio <= 0.0)
        return false;

    float lum = Luminance(albedo);

    // Base ice color
    vec3 base = vec3(
        fma(lum, 0.5,  0.1),
        fma(lum, 0.6,  0.2),
        fma(lum, 0.65, 0.35)
    );
    // Ice tint color
    const vec3 target = vec3(0.16, 0.26, 0.25);

    // Procedural noise
    float noiseSample = texture(cProc3DNoise, noisePos).x;
    float noise = exp2(log2(1.0 - noiseSample) * 1.3) * 0.2;

    // Fresnel-like edge 
    float edge = fma(exp2(log2(NdotV1) * 2.5), 0.5, noise);

    // Final ice color
    vec3 iceColor = Saturate(fma(target - base, vec3(noise), base) + edge);
    albedo   = mix(albedo,   iceColor, iceRatio);
    emission = mix(emission, iceColor, iceRatio);

    return true;
}
#endif
