// Texture coordinate generation (vertex) and lookup (fragment)
#ifndef UKING_TEXCOORD_GLSL
#define UKING_TEXCOORD_GLSL

#ifndef uking_enable_texcoord0
#error options header missing
#endif

#if defined(UKING_VERTEX) || !defined(UKING_FRAGMENT)
// srt = tex_srtN, a 2x3 matrix stored as two vec4 (m[0] = scale/rotate xy,zw, m[1] = translate xy)
vec2 ApplySrt(mat2x4 m, vec2 uv)
{
    return vec2(fma(uv.x, m[0].x, uv.y * m[0].z) + m[1].x, fma(uv.x, m[0].y, uv.y * m[0].w) + m[1].y);
}

mat2x4 GetSrt(int srt)
{
    switch (srt)
    {
        case 0: return mat.tex_srt0;
        case 1: return mat.tex_srt1;
        case 2: return mat.tex_srt2;
        case 3: return mat.tex_srt3;
        case 4: return mat.tex_srt4;
        case 5: return mat.tex_srt5;
    }
    return mat.tex_srt0;
}

vec2 TexCoordSrt(int srt, vec2 uv) { return srt < 0 ? uv : ApplySrt(GetSrt(srt), uv); }

// constant / vertex sources usable in the vertex shader (uking_vtx_normal_modify_A)
vec4 GetVtxSource(int id)
{
    switch (id)
    {
        case 100: return mat.const_color0;   case 101: return mat.const_color1;
        case 102: return mat.const_color2;   case 103: return mat.const_color3;
        case 104: return vec4(mat.const_value0); case 105: return vec4(mat.const_value1);
        case 106: return vec4(mat.const_value2); case 107: return vec4(mat.const_value3);
        case 108: return vec4(mat.const_value4); case 109: return vec4(mat.const_value5);
        case 110: return vec4(mat.const_value6); case 111: return vec4(mat.const_value7);
        case 112: return mat.const_color4;   case 113: return mat.const_color5;
        case 114: return mat.const_vector0;  case 115: return mat.const_vector1;
        case 300: return aColor;
        case 401: return vec4(0.5); case 402: return vec4(1.0); case 403: return vec4(2.0);
        case 404: return vec4(4.0);
        case 405: return vec4(0.25); case 406: return vec4(0.75);
    }
    return vec4(0.0);
}

vec2 gTexCoord0Offset = vec2(0.0);   // lumberjack: bark uv follows the cut
vec2 TexCoordSource(int mapping)
{
    if (mapping >= 100) return GetVtxSource(mapping).xy;   // constant coordinates (const_color/value/vector)
    switch (mapping)
    {
        case 0: return aTexCoord0.xy + gTexCoord0Offset;
        case 1: return aTexCoord1.xy;
        case 2: return aTexCoord2.xy;
        case 3: return aTexCoord3.xy;
    }
    return aTexCoord0.xy;
}

// uking_texcoordN_mapping: 0-3 aTexCoordN, 10 sphere map (view normal), 11 world xz,
// 20-25 result of texcoord 0-5 (after its srt), 100-115 a constant
vec2 TexCoordMapped(int mapping, vec2 tc[6], vec3 worldPos, vec3 viewNormal)
{
    if (mapping == 10) return vec2(fma(viewNormal.x, 0.5, 0.5), fma(viewNormal.y, -0.5, 0.5));
    if (mapping == 11) return worldPos.xz;
    if (mapping >= 20 && mapping <= 25) return tc[mapping - 20];
    return TexCoordSource(mapping);
}

void CalcTexCoords(vec3 worldPos, vec3 viewNormal)
{
    vec2 tc[6] = vec2[6](vec2(0.0), vec2(0.0), vec2(0.0), vec2(0.0), vec2(0.0), vec2(0.0));
#if uking_enable_texcoord0
    tc[0] = TexCoordSrt(uking_texcoord0_srt, TexCoordMapped(uking_texcoord0_mapping, tc, worldPos, viewNormal));
#endif
#if uking_enable_texcoord1
    tc[1] = TexCoordSrt(uking_texcoord1_srt, TexCoordMapped(uking_texcoord1_mapping, tc, worldPos, viewNormal));
#endif
#if uking_enable_texcoord2
    tc[2] = TexCoordSrt(uking_texcoord2_srt, TexCoordMapped(uking_texcoord2_mapping, tc, worldPos, viewNormal));
#endif
#if uking_enable_texcoord3
    tc[3] = TexCoordSrt(uking_texcoord3_srt, TexCoordMapped(uking_texcoord3_mapping, tc, worldPos, viewNormal));
#endif
#if uking_enable_texcoord4
    tc[4] = TexCoordSrt(uking_texcoord4_srt, TexCoordMapped(uking_texcoord4_mapping, tc, worldPos, viewNormal));
#endif
#if uking_enable_texcoord5
    tc[5] = TexCoordSrt(uking_texcoord5_srt, TexCoordMapped(uking_texcoord5_mapping, tc, worldPos, viewNormal));
#endif
    fTexCoord01 = vec4(tc[0], tc[1]); fTexCoord23 = vec4(tc[2], tc[3]); fTexCoord45 = vec4(tc[4], tc[5]);
    // toon specular ramp: only x of the srt result is used, y = x * 0.7
    float toon = TexCoordSrt(uking_texcoord_toon_spec_srt, TexCoordMapped(uking_texcoord_toon_spec_mapping, tc, worldPos, viewNormal)).x;
    fToonSpecCoords = vec4(toon, toon * 0.699999988, 0.0, 0.0);
    fColor = aColor;
    fLocalPosition = vec4(aPosition.xyz, 1.0);
}
#endif

#ifdef UKING_FRAGMENT
// selector 3: sphere map of the normal-mapped view normal, computed per fragment
vec3 gSphereMapN = vec3(0.0, 0.0, 1.0);
// uking_textureN_texcoord: which generated texcoord a texture uses
// selectors 3/4/5 = procedural texcoords 0/1/2 (uking_proctexcoordN_mapping):
//   0 sphere map of the normal-mapped view normal, 1 screen uv, 2 lumberjack annual rings (3: not decoded)
vec2 ScreenUV();
vec2 ProcTexCoord(int mapping)
{
    if (mapping == 0) return vec2(fma(gSphereMapN.x, 0.5, 0.5), fma(gSphereMapN.y, -0.5, 0.5));
    if (mapping == 1) return ScreenUV();
#if uking_enable_lumberjack
    if (mapping == 2) return fma(fLumberjack.xy * fLumberjack.z, vec2(0.5), vec2(0.5));
#endif
    return vec2(0.0);
}
vec2 ScreenUV() { return fScreenCoords.xy * (1.0 / fScreenCoords.w); }
vec2 GetTexCoord(int sel)
{
    switch (sel)
    {
        case 0: return fTexCoord01.xy;
        case 1: return fTexCoord01.zw;
        case 2: return fTexCoord23.xy;
        case 3: return ProcTexCoord(uking_proctexcoord0_mapping);
        case 4: return ProcTexCoord(uking_proctexcoord1_mapping);
        case 5: return ProcTexCoord(uking_proctexcoord2_mapping);
        // 6/7/8 = generated texcoords 3/4/5
        case 6: return fTexCoord23.zw;
        case 7: return fTexCoord45.xy;
        case 8: return fTexCoord45.zw;
    }
    return fTexCoord01.xy;
}
#endif
#endif
