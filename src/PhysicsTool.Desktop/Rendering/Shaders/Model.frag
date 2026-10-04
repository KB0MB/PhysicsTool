#version 330 core
in vec3 vNormal;
in vec3 vTangent;
in float vTangentSign;
in vec3 vWorldPosition;
in vec2 vAlbedoUv;
in vec2 vSpmUv;
in vec2 vNormalUv;
in vec2 vAmbientOcclusionUv;
in vec2 vAuxiliaryUv;
uniform mat4 uModel;
uniform vec4 uColor;
uniform sampler2D uTexture;
uniform sampler2D uSpecularTexture;
uniform sampler2D uNormalTexture;
uniform sampler2D uAmbientOcclusionTexture;
uniform sampler2D uAuxiliaryTexture;
uniform sampler2D uEyeGlintTexture;
uniform int uUseTexture;
uniform int uUseSpecularTexture;
uniform int uUseNormalTexture;
uniform int uUseAmbientOcclusionTexture;
uniform int uUseAuxiliaryTexture;
uniform int uUseEyeGlintTexture;
uniform int uAlphaMode;
uniform int uShaderModel;
uniform int uMetalChannel;
uniform int uSmoothnessChannel;
uniform float uMetalScale;
uniform float uSmoothnessScale;
uniform float uNormalStrength;
uniform float uAmbientOcclusionStrength;
uniform float uAuxiliaryStrength;
uniform float uEyeGlintStrength;
uniform float uOpacity;
uniform vec3 uKeyLight;
uniform vec3 uFillLight;
uniform vec3 uViewDirection;
out vec4 FragColor;

float sampleChannel(vec3 value, int channel)
{
    if (channel == 0) return value.r;
    if (channel == 1) return value.g;
    if (channel == 2) return value.b;
    return 0.0;
}

float antialiasedStep(float edge, float value)
{
    // The Blender graph uses clamped x10000 thresholds. Widen only
    // by the screen-space derivative so minified normal-map texels
    // cannot become isolated square patches at narrow mesh tips.
    float transition = max(fwidth(value) * 0.75, 0.001);
    return smoothstep(edge - transition, edge + transition, value);
}

vec3 srgbToLinear(vec3 value)
{
    bvec3 low = lessThanEqual(value, vec3(0.04045));
    vec3 lower = value / 12.92;
    vec3 upper = pow((value + 0.055) / 1.055, vec3(2.4));
    return mix(upper, lower, low);
}

vec3 linearToSrgb(vec3 value)
{
    value = max(value, vec3(0.0));
    bvec3 low = lessThanEqual(value, vec3(0.0031308));
    vec3 lower = value * 12.92;
    vec3 upper = 1.055 * pow(value, vec3(1.0 / 2.4)) - 0.055;
    return mix(upper, lower, low);
}

vec3 agxDefaultContrast(vec3 value)
{
    vec3 value2 = value * value;
    vec3 value4 = value2 * value2;
    return 15.5 * value4 * value2
        - 40.14 * value4 * value
        + 31.96 * value4
        - 6.868 * value2 * value
        + 0.4298 * value2
        + 0.1191 * value
        - 0.00232;
}

vec3 agxToneMap(vec3 value)
{
    const mat3 inset = mat3(
        0.8424790623, 0.0423282423, 0.0423756549,
        0.0784336000, 0.8784686365, 0.0784336000,
        0.0792237451, 0.0791661275, 0.8791429738);
    const mat3 outset = mat3(
        1.1968790051, -0.0528968518, -0.0529716355,
        -0.0980208811, 1.1519031299, -0.0980434501,
        -0.0990297441, -0.0989611768, 1.1510736726);
    const float minimumEv = -12.47393;
    const float maximumEv = 4.026069;

    value = inset * max(value, vec3(0.0000001));
    value = clamp(log2(value), minimumEv, maximumEv);
    value = (value - minimumEv) / (maximumEv - minimumEv);
    value = agxDefaultContrast(value);
    value = max(outset * value, vec3(0.0));
    // AgX's contrast approximation is display-encoded. Restore a
    // linear value before the shared linear-to-sRGB output stage.
    return clamp(pow(value, vec3(2.2)), 0.0, 1.0);
}

vec3 applyNormalMap(vec3 geometricNormal, vec3 encodedNormal)
{
    vec3 tangent = normalize(
        vTangent - geometricNormal * dot(geometricNormal, vTangent));
    vec3 bitangent = normalize(
        cross(geometricNormal, tangent) * vTangentSign);
    mat3 tangentBasis = mat3(tangent, bitangent, geometricNormal);
    vec3 mapped = encodedNormal * 2.0 - 1.0;
    // Blender's node groups feed the authored normal into every
    // directional and facing band. A small gain keeps that detail
    // legible after the broad real-time interpolation.
    mapped.xy *= uNormalStrength * (uShaderModel == 3 ? 1.0 : 1.20);
    return normalize(tangentBasis * normalize(mapped));
}

vec3 inverseRotateEulerXyz(vec3 value, vec3 rotation)
{
    float cz = cos(-rotation.z);
    float sz = sin(-rotation.z);
    value = vec3(
        cz * value.x - sz * value.y,
        sz * value.x + cz * value.y,
        value.z);

    float cy = cos(-rotation.y);
    float sy = sin(-rotation.y);
    value = vec3(
        cy * value.x + sy * value.z,
        value.y,
        -sy * value.x + cy * value.z);

    float cx = cos(-rotation.x);
    float sx = sin(-rotation.x);
    return vec3(
        value.x,
        cx * value.y - sx * value.z,
        sx * value.y + cx * value.z);
}

void main()
{
    vec4 surface = uUseTexture != 0 ? texture(uTexture, vAlbedoUv) : vec4(1.0);
    float sourceAlpha = surface.a * uColor.a;
    if ((uAlphaMode == 1 && sourceAlpha < 0.5) ||
        (uAlphaMode == 2 && sourceAlpha < 0.01))
        discard;

    vec3 geometricNormal = normalize(vNormal);
    vec3 normal = geometricNormal;
    if (uUseNormalTexture != 0)
        normal = applyNormalMap(normal, texture(uNormalTexture, vNormalUv).xyz);

    // The supplied Blender materials mark SPM as sRGB, then split
    // its linearized R/G channels into smoothness and metal.
    vec3 spm = uUseSpecularTexture != 0
        ? srgbToLinear(texture(uSpecularTexture, vSpmUv).rgb)
        : vec3(0.0);
    float metal = uUseSpecularTexture != 0
        ? clamp(sampleChannel(spm, uMetalChannel) * uMetalScale, 0.0, 1.0)
        : 0.0;
    float smoothness = uUseSpecularTexture != 0
        ? clamp(sampleChannel(spm, uSmoothnessChannel) * uSmoothnessScale, 0.0, 1.0)
        : 0.25;
    if (uShaderModel == 2)
    {
        // The skin group also uses SPM green as a separate shine
        // control, while metal is not meaningful for skin.
        smoothness = max(smoothness, spm.g * 0.65 * uSmoothnessScale);
        metal = 0.0;
    }
    else if (uShaderModel == 3)
    {
        metal = 0.0;
    }
    float occlusion = 1.0;
    if (uUseAmbientOcclusionTexture != 0)
    {
        float sampledAo = srgbToLinear(texture(uAmbientOcclusionTexture, vAmbientOcclusionUv).rgb).r;
        occlusion = mix(1.0, sampledAo, uAmbientOcclusionStrength);
    }

    vec3 baseColor = srgbToLinear(surface.rgb) * uColor.rgb;
    if (uShaderModel == 4 && uUseAuxiliaryTexture != 0)
    {
        vec3 eyeShadow = srgbToLinear(texture(uAuxiliaryTexture, vAuxiliaryUv).rgb);
        baseColor *= mix(vec3(1.0), eyeShadow, clamp(uAuxiliaryStrength, 0.0, 1.0));
    }
    if (uShaderModel == 4 && uUseEyeGlintTexture != 0)
    {
        vec3 viewDirection = normalize(uViewDirection);
        vec2 glintUv = vAlbedoUv + vec2(viewDirection.x, -viewDirection.y) * 0.025;
        vec4 glint = texture(uEyeGlintTexture, glintUv);
        float glintAmount = glint.a * clamp(uEyeGlintStrength, 0.0, 2.0);
        baseColor = mix(baseColor, srgbToLinear(glint.rgb), glintAmount);
    }
    vec3 viewDirection = normalize(uViewDirection);
    float facing = clamp(dot(normal, viewDirection), 0.0, 1.0);
    float key = dot(normal, uKeyLight);
    float geometricKey = dot(geometricNormal, uKeyLight);
    float fill = max(dot(normal, uFillLight), 0.0);
    vec3 halfVector = normalize(uKeyLight + viewDirection);
    vec3 sunColor = vec3(0.61634, 0.64574, 0.48622);
    vec3 indirectColor = vec3(0.96272, 0.94923, 1.0);

    if (uShaderModel == 3)
    {
        // FBX SDK converts Blender Z-up to OpenGL Y-up, and the
        // viewport''s presentation matrix then reflects X. Restore
        // Blender space before evaluating its authored hair graph.
        vec3 blenderNormal = normalize(vec3(-normal.x, -normal.z, normal.y));
        vec3 blenderView = normalize(vec3(
            -viewDirection.x,
            -viewDirection.z,
            viewDirection.y));
        // Match the inverse Euler rotation authored in the supplied
        // BOTW/TOTK Hair Shader group. These values steer every
        // normal-map lighting band, so approximating them moves the
        // broad highlight regions to the wrong side of the hair.
        const vec3 hairLightRotation = vec3(
            1.823601,
            -0.173579,
            2.596994);
        float lightZ = inverseRotateEulerXyz(
            blenderNormal,
            hairLightRotation).z;

        float positiveLight = antialiasedStep(0.10, lightZ);
        float negativeLight = 1.0 - positiveLight;
        // Blender Layer Weight/Facing is one minus the absolute
        // view-normal dot product. It selects grazing silhouettes,
        // not the camera-facing center of the surface.
        float facingBlender = clamp(
            1.0 - abs(dot(blenderNormal, blenderView)),
            0.0,
            1.0);
        float facingBand = antialiasedStep(0.70, facingBlender);

        // Base World Light: Alb * (1 + positiveLight).
        vec3 hairLayers = baseColor * (1.0 + positiveLight);

        // Back Rim Light. Blender transforms world Z into camera
        // space; the view elevation is its local equivalent here.
        float cameraLightZ = inverseRotateEulerXyz(
            blenderView,
            hairLightRotation).z;
        float negativeSlope = max(negativeLight - clamp(-lightZ, 0.0, 1.0), 0.0);
        float backRim = antialiasedStep(
            0.20,
            cameraLightZ * negativeSlope * facingBlender);
        hairLayers *= 1.0 + backRim;

        // Vector Sunlight Rimlight and Fade Rimlight add neutral
        // bands before the lighting-controller tint is applied.
        hairLayers += vec3(0.30) * positiveLight * facingBand;
        float fadeRim = negativeLight * facingBand * cameraLightZ;
        hairLayers += vec3(0.10) * fadeRim;

        // Hair Specular is another cel multiplier, not a glossy
        // lobe. The graph combines reflected-Z thresholds at 0.1
        // and 0.9, then gates them with positiveLight.
        // Blender builds this as Incoming - 2*N*dot(N, Incoming),
        // then multiplies the result by -1 before rotating it.
        vec3 reflectedIncoming = -reflect(-blenderView, blenderNormal);
        float reflectedZ = inverseRotateEulerXyz(
            reflectedIncoming,
            hairLightRotation).z;
        float reflectedLow = antialiasedStep(0.10, reflectedZ);
        float reflectedHigh = antialiasedStep(0.90, reflectedZ);
        float hairSpecular = positiveLight
            * clamp(reflectedLow * 0.5 + reflectedHigh, 0.0, 1.0);
        hairLayers *= 1.0 + hairSpecular;

        // Scene Lighting Data selects between the supplied indirect
        // tint and the sun color at strength 2.
        vec3 hairColor = hairLayers
            * mix(indirectColor, sunColor * 2.0, positiveLight);
        float hairAlpha = uAlphaMode == 1 ? 1.0 : sourceAlpha;
        FragColor = vec4(
            linearToSrgb(agxToneMap(hairColor)),
            hairAlpha * uOpacity);
        return;
    }

    vec3 shadowTint = vec3(0.82, 0.80, 0.76);
    if (uShaderModel == 2)
        shadowTint = vec3(0.82, 0.70, 0.64);
    else if (uShaderModel == 3)
        shadowTint = vec3(0.62, 0.50, 0.20);
    else if (uShaderModel == 5)
        shadowTint = vec3(0.24);

    // The reference Blender groups use large multiply/add values to
    // turn diffuse light into hard BOTW/TOTK-style color bands.
    float mainBand = smoothstep(0.055, 0.085, key);
    float brightBand = smoothstep(0.58, 0.64, key);
    vec3 lighting = mix(shadowTint, indirectColor, mainBand);
    lighting = mix(lighting, vec3(1.10, 1.07, 0.98), brightBand * 0.58);
    lighting += indirectColor * smoothstep(0.30, 0.55, fill) * 0.035;

    // The Blender groups use Layer Weight/Facing repeatedly. This
    // broad view-facing band is what makes their shading move as
    // the camera orbits, without turning the material glossy.
    float grazing = 1.0 - facing;
    float facingShade = smoothstep(0.70, 0.94, grazing);
    lighting = mix(lighting, shadowTint * 0.92, facingShade * 0.18);

    if (uUseNormalTexture != 0)
    {
        float normalDetail = clamp(key - geometricKey, -0.50, 0.50);
        lighting *= 1.0 + normalDetail * 0.30;
    }

    if (uShaderModel == 4)
        lighting = mix(vec3(0.84), vec3(1.10), smoothstep(-0.15, 0.24, key));

    float resolvedAo = uShaderModel == 2
        ? clamp(occlusion * 5.0 - 1.25, 0.0, 1.0)
        : occlusion;
    // In the reference skin group AO participates in the color-band
    // selection (AO * 5 - 1.25), rather than uniformly darkening the
    // final color. Approximate that by steering only shadowed and
    // grazing lighting toward the authored shadow tint.
    float aoAmount = 1.0 - resolvedAo;
    float aoShadowGate = 1.0 - smoothstep(-0.10, 0.56, key);
    float aoWeight = aoAmount
        * mix(0.08, uShaderModel == 2 ? 0.42 : 0.28, aoShadowGate)
        * mix(0.82, 1.14, smoothstep(0.42, 0.90, grazing));
    lighting = mix(lighting, shadowTint * 0.86, clamp(aoWeight, 0.0, 0.46));
    vec3 diffuse = baseColor * lighting;
    diffuse *= mix(1.0, 0.78, metal);

    float halfDot = max(dot(normal, halfVector), 0.0);
    float lightGate = smoothstep(-0.04, 0.28, key);
    float specularPower = mix(12.0, 88.0, smoothness);
    float specularLobe = pow(halfDot, specularPower);
    float specularBand = smoothstep(0.07, 0.24, specularLobe);
    float specularStrength = mix(0.006, 0.090, smoothness) * mix(1.0, 1.65, metal) * lightGate;
    vec3 specularColor = mix(indirectColor, baseColor, metal);

    if (uShaderModel == 1)
    {
        // Cloth uses broad view-facing color bands. Smoothness opens
        // the band while metal selects a stronger albedo-tinted path.
        float clothLobe = pow(halfDot, mix(7.0, 34.0, smoothness));
        float clothFacing = smoothstep(
            mix(0.92, 0.66, smoothness),
            mix(0.99, 0.86, smoothness),
            grazing);
        specularBand = max(smoothstep(0.08, 0.28, clothLobe), clothFacing * 0.72);
        specularStrength = mix(0.004, 0.048, smoothness)
            * mix(1.0, 2.35, metal) * lightGate;
        specularColor = mix(indirectColor, baseColor, metal);
    }
    else if (uShaderModel == 2)
    {
        float skinLobe = pow(halfDot, mix(18.0, 72.0, smoothness));
        specularBand = smoothstep(0.10, 0.30, skinLobe);
        specularStrength = mix(0.002, 0.028, smoothness) * lightGate;
        specularColor = mix(indirectColor, vec3(1.0, 0.78, 0.70), 0.18);
    }
    else if (uShaderModel == 3)
    {
        // Smoothness is exposed but unused by the supplied Blender
        // hair group. Its highlights are already in the cel bands.
        specularStrength = 0.0;
    }
    else if (uShaderModel == 4)
    {
        // Eye highlights come from the authored glint texture. A
        // second view-dependent lobe makes the eyeballs look wet.
        specularStrength = 0.0;
    }
    else if (uShaderModel == 5)
    {
        specularStrength = 0.0;
    }

    specularColor = mix(specularColor, sunColor * 1.12, metal * 0.32);
    float rim = 1.0 - facing;
    float rimBand = smoothstep(0.82, 0.94, rim);
    float rimStrength = uShaderModel == 3 ? 0.03 :
        (uShaderModel == 4 || uShaderModel == 5 ? 0.0 : 0.018);
    vec3 color = diffuse
        + specularColor * specularBand * specularStrength
        + baseColor * rimBand * rimStrength;
    color *= 2.0;
    float resolvedAlpha = uAlphaMode == 1 ? 1.0 : sourceAlpha;
    FragColor = vec4(linearToSrgb(color), resolvedAlpha * uOpacity);
}