#version 330 core
// Adapted from Switch Toolbox BFRES_Totk.frag (GPL-3.0).
// Material routing is preserved; lighting uses PhysicsTool's viewport lights.

in vec2 f_raw_texcoord0;
in vec2 f_raw_texcoord1;
in vec2 f_raw_texcoord2;
in vec2 f_raw_texcoord3;
in vec3 normal;
in vec4 vertexColor;
in vec3 tangent;
in vec3 bitangent;

uniform sampler2D Texture0;
uniform sampler2D Texture1;
uniform sampler2D Texture2;
uniform sampler2D Texture3;
uniform sampler2D Texture4;
uniform sampler2D Texture5;
uniform sampler2D Texture6;
uniform sampler2D Texture7;
uniform int totkEyeGlintEnabled;

uniform int totkTextureCount;
uniform int totkTextureEnabled0;
uniform int totkTextureEnabled1;
uniform int totkTextureEnabled2;
uniform int totkTextureEnabled3;
uniform int totkTextureEnabled4;
uniform int totkTextureEnabled5;
uniform int totkTextureEnabled6;
uniform int totkTextureEnabled7;
uniform int totkTextureSrgb0;
uniform int totkTextureSrgb1;
uniform int totkTextureSrgb2;
uniform int totkTextureSrgb3;
uniform int totkTextureSrgb4;
uniform int totkTextureSrgb5;
uniform int totkTextureSrgb6;
uniform int totkTextureSrgb7;
uniform int totkTextureLuminance0;
uniform int totkTextureLuminance1;
uniform int totkTextureLuminance2;
uniform int totkTextureLuminance3;
uniform int totkTextureLuminance4;
uniform int totkTextureLuminance5;
uniform int totkTextureLuminance6;
uniform int totkTextureLuminance7;
uniform int totkTextureMapping0;
uniform int totkTextureMapping1;
uniform int totkTextureMapping2;
uniform int totkTextureMapping3;
uniform int totkTextureMapping4;
uniform int totkTextureMapping5;
uniform int totkTextureMapping6;
uniform int totkTextureMapping7;
uniform vec4 totkTextureChannels0;
uniform vec4 totkTextureChannels1;
uniform vec4 totkTextureChannels2;
uniform vec4 totkTextureChannels3;
uniform vec4 totkTextureChannels4;
uniform vec4 totkTextureChannels5;
uniform vec4 totkTextureChannels6;
uniform vec4 totkTextureChannels7;

uniform vec4 totkTextureSrt0;
uniform vec4 totkTextureSrt1;
uniform vec4 totkTextureSrt2;
uniform vec4 totkTextureSrt3;
uniform vec4 totkTextureSrt4;
uniform vec4 totkTextureSrt5;
uniform vec4 totkTextureSrt6;
uniform vec4 totkTextureSrt7;

uniform float totkTextureRotation0;
uniform float totkTextureRotation1;
uniform float totkTextureRotation2;
uniform float totkTextureRotation3;
uniform float totkTextureRotation4;
uniform float totkTextureRotation5;
uniform float totkTextureRotation6;
uniform float totkTextureRotation7;

uniform int totkAlbedoSource;
uniform int totkAlbedoOutput;
uniform int totkLayeredAlbedoSource;
uniform int totkEyeShadowSource;
uniform int totkNormalSource;
uniform int totkSpecularSource;
uniform int totkMetalSource;
uniform int totkSpecularOutput;
uniform int totkMetalOutput;
uniform int totkEmissionSource;
uniform int totkSecondaryEmissionSource;
uniform int totkEmissionEnabled;
uniform int totkEmissionIsMask;
uniform float totkEmissionStrength;
uniform int totkAoSource;
uniform int totkAoOutput;
uniform int totkTransparencySource;
uniform int totkTransparencyOutput;
uniform int totkMetalChannel;
uniform int totkSpecularChannel;
uniform int totkAoChannel;
uniform int totkTransparencyChannel;
uniform int totkMaterialBehavior;
uniform int totkAlphaTestEnabled;
uniform int totkAlphaTestFunc;
uniform int totkForceOpaqueOutput;

uniform float gsys_alpha_test_ref_value;
uniform int useNormalMap;
uniform int renderVertColor;
uniform int renderDiffuse;
uniform int renderSpecular;
uniform int renderFresnel;
uniform int colorOverride;
uniform vec3 uViewDirection;
uniform vec3 specLightDirection;
uniform int totkHairSpecularEnabled;
uniform int totkHairSpecularOutput;
uniform int totkHairSpecularChannel;
uniform vec4 p_const_color0;
uniform vec4 p_const_color1;
uniform vec4 p_const_color2;
uniform vec4 p_const_color3;
uniform vec4 p_const_color4;
uniform vec4 p_const_color5;
uniform float p_const_value0;
uniform float p_const_value1;
uniform float p_const_value2;
uniform float p_const_value3;
uniform float p_const_value4;
uniform float p_const_value5;
uniform float p_const_value6;
uniform float p_const_value7;

out vec4 fragColor;

vec3 SafeNormalize(vec3 value, vec3 fallback)
{
    float squaredLength = dot(value, value);
    return squaredLength > 1e-12 ? value * inversesqrt(squaredLength) : fallback;
}

vec2 SourceUv(int mapping)
{
    if (mapping == 1)
        return f_raw_texcoord1;
    if (mapping == 2)
        return f_raw_texcoord2;
    if (mapping == 3)
        return f_raw_texcoord3;
    if (mapping == 20)
        return SafeNormalize(normal, vec3(0.0, 0.0, 1.0)).xy * vec2(0.5, -0.5) + vec2(0.5);
    return f_raw_texcoord0;
}

vec2 TransformUv(vec2 uv, vec4 srt, float rotation)
{
    vec2 scaled = uv * srt.xy;
    float c = cos(rotation);
    float s = sin(rotation);
    vec2 rotated = vec2(
        c * scaled.x + s * scaled.y,
       -s * scaled.x + c * scaled.y);
    return rotated + srt.zw;
}

vec2 TextureUv(int slot)
{
    if (slot == 0) return TransformUv(SourceUv(totkTextureMapping0), totkTextureSrt0, totkTextureRotation0);
    if (slot == 1) return TransformUv(SourceUv(totkTextureMapping1), totkTextureSrt1, totkTextureRotation1);
    if (slot == 2) return TransformUv(SourceUv(totkTextureMapping2), totkTextureSrt2, totkTextureRotation2);
    if (slot == 3) return TransformUv(SourceUv(totkTextureMapping3), totkTextureSrt3, totkTextureRotation3);
    if (slot == 4) return TransformUv(SourceUv(totkTextureMapping4), totkTextureSrt4, totkTextureRotation4);
    if (slot == 5) return TransformUv(SourceUv(totkTextureMapping5), totkTextureSrt5, totkTextureRotation5);
    if (slot == 6) return TransformUv(SourceUv(totkTextureMapping6), totkTextureSrt6, totkTextureRotation6);
    if (slot == 7) return TransformUv(SourceUv(totkTextureMapping7), totkTextureSrt7, totkTextureRotation7);
    return f_raw_texcoord0;
}

vec2 StaticTextureUv(int slot)
{
    if (slot == 0) return SourceUv(totkTextureMapping0);
    if (slot == 1) return SourceUv(totkTextureMapping1);
    if (slot == 2) return SourceUv(totkTextureMapping2);
    if (slot == 3) return SourceUv(totkTextureMapping3);
    if (slot == 4) return SourceUv(totkTextureMapping4);
    if (slot == 5) return SourceUv(totkTextureMapping5);
    if (slot == 6) return SourceUv(totkTextureMapping6);
    if (slot == 7) return SourceUv(totkTextureMapping7);
    return f_raw_texcoord0;
}
float TextureChannel(vec4 value, int channel)
{
    if (channel == 0) return value.r;
    if (channel == 1) return value.g;
    if (channel == 2) return value.b;
    if (channel == 3) return value.a;
    if (channel == 4) return 1.0;
    return 0.0;
}

vec4 MapTextureChannels(vec4 value, vec4 channels)
{
    return vec4(TextureChannel(value, int(channels.x)), TextureChannel(value, int(channels.y)),
                TextureChannel(value, int(channels.z)), TextureChannel(value, int(channels.w)));
}

bool TextureSlotEnabled(int slot)
{
    if (slot < 0 || slot >= totkTextureCount) return false;
    if (slot == 0) return totkTextureEnabled0 != 0;
    if (slot == 1) return totkTextureEnabled1 != 0;
    if (slot == 2) return totkTextureEnabled2 != 0;
    if (slot == 3) return totkTextureEnabled3 != 0;
    if (slot == 4) return totkTextureEnabled4 != 0;
    if (slot == 5) return totkTextureEnabled5 != 0;
    if (slot == 6) return totkTextureEnabled6 != 0;
    if (slot == 7) return totkTextureEnabled7 != 0;
    return false;
}

vec4 FetchTextureSlot(int slot)
{
    if (slot < 0 || slot >= totkTextureCount)
        return vec4(1.0);
    if (slot == 0 && totkTextureEnabled0 == 0) return vec4(1.0);
    if (slot == 1 && totkTextureEnabled1 == 0) return vec4(1.0);
    if (slot == 2 && totkTextureEnabled2 == 0) return vec4(1.0);
    if (slot == 3 && totkTextureEnabled3 == 0) return vec4(1.0);
    if (slot == 4 && totkTextureEnabled4 == 0) return vec4(1.0);
    if (slot == 5 && totkTextureEnabled5 == 0) return vec4(1.0);
    if (slot == 6 && totkTextureEnabled6 == 0) return vec4(1.0);
    if (slot == 7 && totkTextureEnabled7 == 0) return vec4(1.0);
    if (slot == 0) return MapTextureChannels(texture(Texture0, TextureUv(0)), totkTextureChannels0);
    if (slot == 1) return MapTextureChannels(texture(Texture1, TextureUv(1)), totkTextureChannels1);
    if (slot == 2) return MapTextureChannels(texture(Texture2, TextureUv(2)), totkTextureChannels2);
    if (slot == 3) return MapTextureChannels(texture(Texture3, TextureUv(3)), totkTextureChannels3);
    if (slot == 4) return MapTextureChannels(texture(Texture4, TextureUv(4)), totkTextureChannels4);
    if (slot == 5) return MapTextureChannels(texture(Texture5, TextureUv(5)), totkTextureChannels5);
    if (slot == 6) return MapTextureChannels(texture(Texture6, TextureUv(6)), totkTextureChannels6);
    if (slot == 7) return MapTextureChannels(texture(Texture7, TextureUv(7)), totkTextureChannels7);
    return vec4(1.0);
}

// Reuse samples to keep old Intel/AMD driver compilers from repeatedly
// expanding the sampler, UV, and channel-selection branches.
vec4 sampledTexture0;
vec4 sampledTexture1;
vec4 sampledTexture2;
vec4 sampledTexture3;
vec4 sampledTexture4;
vec4 sampledTexture5;
vec4 sampledTexture6;
vec4 sampledTexture7;

void PrepareTextureSamples()
{
    sampledTexture0 = FetchTextureSlot(0);
    sampledTexture1 = FetchTextureSlot(1);
    sampledTexture2 = FetchTextureSlot(2);
    sampledTexture3 = FetchTextureSlot(3);
    sampledTexture4 = FetchTextureSlot(4);
    sampledTexture5 = FetchTextureSlot(5);
    sampledTexture6 = FetchTextureSlot(6);
    sampledTexture7 = FetchTextureSlot(7);
}

vec4 SampleTextureSlot(int slot)
{
    if (slot == 0) return sampledTexture0;
    if (slot == 1) return sampledTexture1;
    if (slot == 2) return sampledTexture2;
    if (slot == 3) return sampledTexture3;
    if (slot == 4) return sampledTexture4;
    if (slot == 5) return sampledTexture5;
    if (slot == 6) return sampledTexture6;
    if (slot == 7) return sampledTexture7;
    return vec4(1.0);
}

vec4 SampleOutput(int outputId, int fallbackSlot)
{
    if (outputId >= 0 && outputId <= 7)
        return SampleTextureSlot(outputId);
    // Generated expression outputs are not sampler indices. Until an expression
    // is evaluated, use the material's semantic input (albedo, normal, etc.).
    if (outputId >= 200 && outputId <= 207)
        return fallbackSlot >= 0 ? SampleTextureSlot(fallbackSlot) : vec4(1.0);
    if (outputId == 300)
        return vec4(1.0);
    if (outputId == 400) return p_const_color0;
    if (outputId == 401) return p_const_color1;
    if (outputId == 402) return p_const_color2;
    if (outputId == 403) return p_const_color3;
    if (outputId == 404) return p_const_color4;
    if (outputId == 405) return p_const_color5;
    if (outputId == 500) return vec4(p_const_value0);
    if (outputId == 501) return vec4(p_const_value1);
    if (outputId == 502) return vec4(p_const_value2);
    if (outputId == 503) return vec4(p_const_value3);
    if (outputId == 504) return vec4(p_const_value4);
    if (outputId == 505) return vec4(p_const_value5);
    if (outputId == 506) return vec4(p_const_value6);
    if (outputId == 507) return vec4(p_const_value7);
    return fallbackSlot >= 0 ? SampleTextureSlot(fallbackSlot) : vec4(1.0);
}

float SelectChannel(vec4 value, int channel)
{
    if (channel == 10) return value.r;
    if (channel == 20) return value.g;
    if (channel == 30) return value.b;
    if (channel == 40 || channel == 41) return value.a;
    return value.r;
}

vec3 SrgbToLinear(vec3 value)
{
    bvec3 low = lessThanEqual(value, vec3(0.04045));
    vec3 lower = value / 12.92;
    vec3 upper = pow((value + 0.055) / 1.055, vec3(2.4));
    return mix(upper, lower, low);
}

vec3 LinearToSrgb(vec3 value)
{
    value = max(value, vec3(0.0));
    bvec3 low = lessThanEqual(value, vec3(0.0031308));
    vec3 lower = value * 12.92;
    vec3 upper = 1.055 * pow(value, vec3(1.0 / 2.4)) - 0.055;
    return mix(upper, lower, low);
}

vec3 TextureColorToLinear(vec3 color, int slot)
{
    // BC4 supplies one scalar. Expand it only for color consumers; raw masks,
    // normal inputs, channel-selected opacity and alpha keep their data values.
    int luminance = 0;
    if (slot == 0) luminance = totkTextureLuminance0;
    else if (slot == 1) luminance = totkTextureLuminance1;
    else if (slot == 2) luminance = totkTextureLuminance2;
    else if (slot == 3) luminance = totkTextureLuminance3;
    else if (slot == 4) luminance = totkTextureLuminance4;
    else if (slot == 5) luminance = totkTextureLuminance5;
    else if (slot == 6) luminance = totkTextureLuminance6;
    else if (slot == 7) luminance = totkTextureLuminance7;
    if (luminance == 1) color = color.rrr;
    int srgb = 0;
    if (slot == 0) srgb = totkTextureSrgb0;
    else if (slot == 1) srgb = totkTextureSrgb1;
    else if (slot == 2) srgb = totkTextureSrgb2;
    else if (slot == 3) srgb = totkTextureSrgb3;
    else if (slot == 4) srgb = totkTextureSrgb4;
    else if (slot == 5) srgb = totkTextureSrgb5;
    else if (slot == 6) srgb = totkTextureSrgb6;
    else if (slot == 7) srgb = totkTextureSrgb7;
    return srgb == 1 ? SrgbToLinear(color) : color;
}

vec4 SampleColorOutput(int outputId, int fallbackSlot)
{
    vec4 sampleValue = SampleOutput(outputId, fallbackSlot);
    // Shader constants are already linear; only texture colors need decoding.
    int slot = outputId >= 0 && outputId <= 7 ? outputId
        : outputId >= 300 && outputId <= 507 ? -1 : fallbackSlot;
    sampleValue.rgb = TextureColorToLinear(sampleValue.rgb, slot);
    return sampleValue;
}

float SoftDisc(vec2 uv, vec2 center, float radius, float feather)
{
    return 1.0 - smoothstep(radius - feather, radius, length(uv - center));
}

bool AlphaTestFails(float alpha)
{
    if (totkAlphaTestEnabled == 0)
        return false;
    if (totkAlphaTestFunc == 0) return alpha < gsys_alpha_test_ref_value;
    if (totkAlphaTestFunc == 1) return alpha <= gsys_alpha_test_ref_value;
    if (totkAlphaTestFunc == 2) return alpha != gsys_alpha_test_ref_value;
    if (totkAlphaTestFunc == 3) return alpha >= gsys_alpha_test_ref_value;
    if (totkAlphaTestFunc == 4) return alpha > gsys_alpha_test_ref_value;
    if (totkAlphaTestFunc == 5) return alpha == gsys_alpha_test_ref_value;
    if (totkAlphaTestFunc == 6) return true;
    return false;
}

vec3 FresnelPass(vec3 N, vec3 V)
{
    float nDotV = clamp(dot(N, V), 0.0, 1.0);
    float fresnel = 1.0 - nDotV;
    float center = 0.75;
    float smoothness = 0.015;
    fresnel = smoothstep(center - smoothness, center + smoothness, fresnel);
    return vec3(1.0, 1.0, 0.75) * fresnel * 0.2;
}

void main()
{
    if (colorOverride == 1)
    {
        fragColor = vec4(1.0);
        return;
    }

    PrepareTextureSamples();
    vec4 albedoSample = SampleColorOutput(totkAlbedoOutput, totkAlbedoSource);
    if (totkLayeredAlbedoSource >= 0)
    {
        vec4 layer = SampleTextureSlot(totkLayeredAlbedoSource);
        layer.rgb = TextureColorToLinear(layer.rgb, totkLayeredAlbedoSource);
        albedoSample.rgb = mix(albedoSample.rgb, layer.rgb, clamp(layer.a, 0.0, 1.0));
    }
    vec3 albedo = albedoSample.rgb;

    if (totkEyeShadowSource >= 0)
    {
        // The eye-shadow texture uses white as a transparent/neutral area.
        vec3 eyeShadow = TextureColorToLinear(
            SampleTextureSlot(totkEyeShadowSource).rgb, totkEyeShadowSource);
        albedo *= eyeShadow;
    }

    if (renderVertColor == 1)
        albedo *= mix(vec3(1.0), vertexColor.rgb, 0.15);

    float alpha = albedoSample.a;
    if (totkTransparencyOutput >= 200 && totkTransparencyOutput <= 207)
    {
        // An unevaluated expression is not the albedo's selected RGB channel.
        // Preserve authored base coverage until its procedural dissolve is modeled.
        alpha = albedoSample.a;
    }
    else if (totkTransparencySource >= 0 || totkTransparencyOutput >= 300)
        alpha = SelectChannel(
            SampleOutput(totkTransparencyOutput, totkTransparencySource),
            totkTransparencyChannel);
    if (AlphaTestFails(alpha))
        discard;
    if (totkForceOpaqueOutput == 1)
        alpha = 1.0;

    vec3 N = SafeNormalize(normal, vec3(0.0, 0.0, 1.0));
    if (useNormalMap == 1 && totkNormalSource >= 0)
    {
        vec2 mapNormalXY = SampleTextureSlot(totkNormalSource).xy * 2.007874 - 1.00787401;
        float mapNormalZ = sqrt(clamp(1.0 - dot(mapNormalXY, mapNormalXY), 0.0, 1.0));
        vec3 mapNormal = vec3(mapNormalXY, mapNormalZ);
        vec3 T = SafeNormalize(tangent, vec3(0.0));
        vec3 B = SafeNormalize(bitangent, vec3(0.0));
        if (length(T) > 0.001 && length(B) > 0.001)
            N = SafeNormalize(mat3(T, B, N) * mapNormal, N);
    }

    float ao = SelectChannel(
        SampleOutput(totkAoOutput, totkAoSource), totkAoChannel);

    vec4 specularSample = SampleOutput(totkSpecularOutput, totkSpecularSource);
    vec4 metalSample = SampleOutput(totkMetalOutput, totkMetalSource);
    // These channels are output masks. The game resolves their surface response
    // later in deferred lighting, so do not treat the selected specular mask as
    // a literal PBR smoothness value here.
    vec3 specularData = specularSample.rgb;
    vec3 metalData = metalSample.rgb;
    float specularMask = totkSpecularSource >= 0
        ? SelectChannel(vec4(specularData, specularSample.a), totkSpecularChannel)
        : 0.25;
    specularMask = clamp(specularMask, 0.0, 1.0);
    float metal = totkMetalSource >= 0
        ? SelectChannel(vec4(metalData, metalSample.a), totkMetalChannel)
        : 0.0;

    // Character non-metal, skin, hair, and procedural eye materials do not use
    // the packed metal channel as literal metallic color.
    if (totkMaterialBehavior == 0 || totkMaterialBehavior == 10 ||
        totkMaterialBehavior == 100 || totkMaterialBehavior == 101 ||
        totkMaterialBehavior == 104)
        metal = 0.0;

    vec3 I = uViewDirection;
    vec3 V = SafeNormalize(I, vec3(0.0, 0.0, 1.0));
    vec3 L = SafeNormalize(specLightDirection, vec3(0.0, 0.0, 1.0));
    vec3 H = SafeNormalize(L + V, N);
    vec3 R = reflect(-V, N);

    float lambert = max(dot(N, L), 0.0);
    float smoothness = 0.1;
    float center = 0.5;
    float smoothLambert = smoothstep(center, center + (smoothness * 0.5), lambert);
    smoothLambert = clamp(smoothLambert + 0.6, 0.0, 1.0);
    float diffuseLight = smoothLambert * 1.5;
    vec3 color = albedo * diffuseLight * mix(0.65, 1.0, ao)
        * mix(1.0, 0.78, metal) * float(renderDiffuse);

    if (totkMaterialBehavior == 100)
    {
        // Hair uses a cel multiplier over its albedo. o_specular_hair selects
        // the scalar controlling that response; it is not an RGB highlight.
        vec4 hairOutput = SampleOutput(totkHairSpecularOutput, -1);
        float hairMask = clamp(
            SelectChannel(hairOutput, totkHairSpecularChannel) * specularMask,
            0.0, 1.0)
            * float(totkHairSpecularEnabled)
            * float(renderSpecular);

        float keyLight = dot(N, L);
        float positiveLight = smoothstep(0.08, 0.12, keyLight);
        float grazing = clamp(1.0 - abs(dot(N, V)), 0.0, 1.0);
        float rimBand = smoothstep(0.70, 0.76, grazing);

        vec3 reflectedView = reflect(-V, N);
        float reflectedLight = dot(reflectedView, L);
        float reflectedLow = smoothstep(0.08, 0.12, reflectedLight);
        float reflectedHigh = smoothstep(0.87, 0.93, reflectedLight);
        float hairSpecular = positiveLight * hairMask
            * clamp(reflectedLow * 0.5 + reflectedHigh, 0.0, 1.0);

        vec3 hairLayers = albedo * (1.0 + positiveLight);
        hairLayers *= 1.0 + hairSpecular;
        hairLayers *= 1.0 + rimBand * positiveLight * 0.15;
        // Preserve hair lighting without the hard-coded colored light tint.
        color = hairLayers
            * mix(1.0, 2.0, positiveLight)
            * mix(0.65, 1.0, ao)
            * float(renderDiffuse);
    }

    if (renderSpecular == 1 && totkSpecularSource >= 0 &&
        totkMaterialBehavior != 100 && totkMaterialBehavior != 101 &&
        totkMaterialBehavior != 104)
    {
        // The supplied Blender recreation uses the SPM channel to gate stylized
        // lighting bands. Keep the highlight shape material-driven and let the
        // texture control only its strength. Driving both width and strength
        // from SPM makes ordinary cloth and skin look like wet rubber.
        float specularPower = 28.0;
        float materialSpecularScale = 0.026;
        if (totkMaterialBehavior == 1 || totkMaterialBehavior == 2)
        {
            specularPower = 48.0;
            materialSpecularScale = 0.050;
        }
        else if (totkMaterialBehavior == 10)
        {
            specularPower = 24.0;
            materialSpecularScale = 0.020;
        }
        float halfDot = max(dot(N, H), 0.0);
        float specularLobe = pow(halfDot, specularPower);
        float specularBand = smoothstep(0.08, 0.28, specularLobe);

        // Environment reflection from irradiance cubemap
        vec3 envSample = vec3(0.25 + 0.15 * max(R.y, 0.0));
        float envBand = smoothstep(0.34, 0.41, envSample.r);

        float specularStrength = materialSpecularScale * specularMask
            * mix(1.0, 1.65, metal);
        vec3 specularColor = mix(vec3(0.96, 0.95, 1.0), albedo, metal);
        color += specularColor * max(specularBand, envBand) * specularStrength;
    }

    if (renderFresnel == 1 && totkMaterialBehavior != 104)
    {
        color += FresnelPass(N, V);
    }

    if (totkMaterialBehavior == 104 && totkEyeGlintEnabled == 1)
    {
        // Match the two sparse highlights in the authored PhysicsTool mask
        // without allocating another sampler that can inherit viewport state.
        vec2 glintUv = StaticTextureUv(totkAlbedoSource);
        float primaryGlint = SoftDisc(glintUv, vec2(0.438, 0.406), 0.052, 0.026);
        float secondaryGlint = SoftDisc(glintUv, vec2(0.555, 0.590), 0.023, 0.012);
        float glintAmount = clamp(primaryGlint * 0.40 + secondaryGlint * 0.24, 0.0, 0.40);
        color = mix(color, vec3(0.56), glintAmount);
    }

    if (totkEmissionEnabled == 1 && TextureSlotEnabled(totkEmissionSource))
    {
        vec3 emissionSample = SampleTextureSlot(totkEmissionSource).rgb;
        vec3 emission = TextureColorToLinear(emissionSample, totkEmissionSource);
        if (totkEmissionIsMask == 1)
        {
            emission = emissionSample.rrr
                * max(p_const_color0.rgb, vec3(0.0))
                * max(totkEmissionStrength, 0.0);
        }
        color += emission;
        if (totkEmissionIsMask == 1 && totkSecondaryEmissionSource >= 0 &&
            TextureSlotEnabled(totkSecondaryEmissionSource))
        {
            vec4 secondary = SampleTextureSlot(totkSecondaryEmissionSource);
            color += secondary.rrr * max(p_const_color1.rgb, vec3(0.0))
                * max(totkEmissionStrength, 0.0);
        }
    }

    fragColor = vec4(LinearToSrgb(color), alpha);
}
