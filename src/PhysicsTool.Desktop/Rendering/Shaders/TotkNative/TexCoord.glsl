// Texture coordinates: generation in the vertex shader, selection in the fragment shader (same scheme as BOTW)
#ifndef TOTK_TEXCOORD_GLSL
#define TOTK_TEXCOORD_GLSL

// constant sources (material constants and literal numbers)
vec4 GetConstSource(int id)
{
    switch (id)
    {
        case 100: return Mat.p_const_color0;
        case 101: return Mat.p_const_color1;
        case 102: return Mat.p_const_color2;
        case 103: return Mat.p_const_color3;
        case 104: return vec4(Mat.p_const_value0);
        case 105: return vec4(Mat.p_const_value1);
        case 106: return vec4(Mat.p_const_value2);
        case 107: return vec4(Mat.p_const_value3);
        case 108: return vec4(Mat.p_const_value4);
        case 109: return vec4(Mat.p_const_value5);
        case 110: return vec4(Mat.p_const_value6);
        case 111: return vec4(Mat.p_const_value7);
        case 112: return Mat.p_const_color4;
        case 113: return Mat.p_const_color5;
        case 114: return Mat.p_const_vector0;
        case 115: return Mat.p_const_vector1;
        case 400: return vec4(0.0);
        case 401: return vec4(0.5);
        case 402: return vec4(1.0);
        case 403: return vec4(2.0);
        case 404: return vec4(4.0);
        case 405: return vec4(0.25);
        case 406: return vec4(0.75);
    }
    return vec4(0.0);
}

#ifdef TOTK_VERTEX
// srt = p_tex_srtN: m[0] = 2x2 scale / rotate (xy, zw columns), m[1].xy = translate
vec2 ApplySrt(mat2x4 m, vec2 uv) { return vec2(fma(uv.x, m[0].x, uv.y * m[0].z) + m[1].x, fma(uv.x, m[0].y, uv.y * m[0].w) + m[1].y); }
mat2x4 GetSrt(int srt)
{
    switch (srt)
    {
        case 0: return Mat.p_tex_srt0;
        case 1: return Mat.p_tex_srt1;
        case 2: return Mat.p_tex_srt2;
        case 3: return Mat.p_tex_srt3;
        case 4: return Mat.p_tex_srt4;
        case 5: return Mat.p_tex_srt5;
    }
    return Mat.p_tex_srt0;
}
vec2 TexCoordSrt(int srt, vec2 uv) { return srt < 0 ? uv : ApplySrt(GetSrt(srt), uv); }

// o_texcoordN_mapping: 0 / 1 vertex uv0 / uv1, 20-25 result of texcoord 0-5 (after its srt), 100-115 a constant
// (10 / 11 and 2 / 3 as in BOTW, not seen in TOTK programs decoded so far)
vec2 TexCoordMapped(int mapping, vec2 tc[6])
{
    if (mapping >= 100) return GetConstSource(mapping).xy;
    if (mapping >= 20 && mapping <= 25) return tc[mapping - 20];
    return mapping == 1 ? aTexCoord1.xy : aTexCoord0.xy;
}

void CalcTexCoords()
{
    vec2 tc[6] = vec2[6](vec2(0.0), vec2(0.0), vec2(0.0), vec2(0.0), vec2(0.0), vec2(0.0));
#if o_enable_texcoord0
    tc[0] = TexCoordSrt(o_texcoord0_srt, TexCoordMapped(o_texcoord0_mapping, tc));
#endif
#if o_enable_texcoord1
    tc[1] = TexCoordSrt(o_texcoord1_srt, TexCoordMapped(o_texcoord1_mapping, tc));
#endif
#if o_enable_texcoord2
    tc[2] = TexCoordSrt(o_texcoord2_srt, TexCoordMapped(o_texcoord2_mapping, tc));
#endif
#if o_enable_texcoord3
    tc[3] = TexCoordSrt(o_texcoord3_srt, TexCoordMapped(o_texcoord3_mapping, tc));
#endif
#if o_enable_texcoord4
    tc[4] = TexCoordSrt(o_texcoord4_srt, TexCoordMapped(o_texcoord4_mapping, tc));
#endif
#if o_enable_texcoord5
    tc[5] = TexCoordSrt(o_texcoord5_srt, TexCoordMapped(o_texcoord5_mapping, tc));
#endif
#if USE_TEXCOORD
    fTexCoord01 = vec4(tc[0], tc[1]);
#if HAS_TEXCOORD23
    fTexCoord23 = vec4(tc[2], tc[3]);
#endif
#if HAS_TEXCOORD45
    fTexCoord45 = vec4(tc[4], tc[5]);
#endif
#endif
#if HAS_TOON_SPEC_COORD && USE_SURFACE
    // hair / toon specular pattern: only x of the srt result is used, y = 0.7 x
    float toon = TexCoordSrt(o_texcoord_toon_spec_srt, TexCoordMapped(o_texcoord_toon_spec_mapping, tc)).x;
    fToonSpecCoord.xy = vec2(toon, toon * 0.699999988);
#endif
}
#else
// o_textureN_texcoord: 0 / 1 / 2 texcoord 0 / 1 / 2, 6 / 7 / 8 texcoord 3 / 4 / 5 (3 / 4 / 5 procedural: not decoded yet)
vec2 GetTexCoord(int sel)
{
#if !USE_TEXCOORD
    return vec2(0.0);
#endif
    switch (sel)
    {
#if USE_TEXCOORD
        case 0: return fTexCoord01.xy;
        case 1: return fTexCoord01.zw;
#endif
#if USE_TEXCOORD && HAS_TEXCOORD23
        case 2: return fTexCoord23.xy;
        case 6: return fTexCoord23.zw;
#endif
#if USE_TEXCOORD && HAS_TEXCOORD45
        case 7: return fTexCoord45.xy;
        case 8: return fTexCoord45.zw;
#endif
    }
#if USE_TEXCOORD
    return fTexCoord01.xy;
#else
    return vec2(0.0);
#endif
}
#endif

#endif
