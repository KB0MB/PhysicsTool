// G-buffer encoding shared by gsys_assign_gbuffer (static objects) and gsys_assign_dynamic3 (objects with gameplay effects)
//   RT0 = (shading model / 255, p_object_attribute, 0, 0)
//   RT1 = (albedo, flags): bits 3-7 metal (skin: transmission), bit 2 always, bit 1 transmission, bit 0 emission
//   RT3 = (encoded normal xy, ao + normal signs, specular): see EncodeNormal
//   RT5 = emission (gbuffer: only with o_enable_emission; dynamic3: always)
// The pass also marks the pixel's screen tile in SceneShadingInfo (tiled deferred lighting).
#ifndef TOTK_GBUFFER_GLSL
#define TOTK_GBUFFER_GLSL


void MarkTile()
{
    ivec2 tile = ivec2(trunc(gl_FragCoord.xy * Context.cTileBufferFragCoordToBlockHeader));
    int index = tile.y * Context.cTileBufferDivideNum + tile.x;
    SceneShadingInfo.data[int(uint(index * 16 + 452) >> 2u)] = 1u;
}

float Pack8(int bits) { return float(bits) * 0.00392156886; }

// normal: |n.xy| scaled to the azimuthal projection length sqrt((1 - z) / 2) / |n.xy|; the signs of x and y go into
// the low bits of z, above them ao (sat(2 ao - 1), top 3 bits) and the constant 8
vec3 EncodeNormal(vec3 n, float ao)
{
    float s = sqrt(max(0.0, fma(n.z, -0.5, 0.5) * (1.0 / max(fma(n.y, n.y, n.x * n.x), 9.99999975e-06))));
    vec2 sg = vec2(float(n.x > 0.0) - float(n.x < 0.0), float(n.y > 0.0) - float(n.y < 0.0));
    int aoBits = int(trunc(Saturate(fma(ao, 2.0, -1.0)) * 255.0)) & 224;
    int bits = aoBits | 8 | (int(n.x > 0.0) << 1) | int(n.y > 0.0);
    return vec3(n.x * s * sg.x, n.y * s * sg.y, Pack8(bits));
}

// shading model of the material (RT0.x * 255): 8 base, 9 metal, 12 skin, 13 eye
float ShadingModel(float metal)
{
#if o_material_behave == 1
    return 9.0;
#elif o_material_behave == 2
    return metal > 0.5 ? 9.0 : 8.0;
#elif o_material_behave == 101
    return 12.0;
#elif o_material_behave == 104
    return 13.0;
#else
    return 8.0;
#endif
}

#endif
