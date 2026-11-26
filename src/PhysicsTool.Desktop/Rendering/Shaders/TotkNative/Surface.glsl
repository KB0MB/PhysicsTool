// Material inputs: textures, normal map, mimicry
#ifndef TOTK_SURFACE_GLSL
#define TOTK_SURFACE_GLSL

vec2 TextureUV(int n)
{
    return GetTexCoord(n == 0 ? o_texture0_texcoord : n == 1 ? o_texture1_texcoord : n == 2 ? o_texture2_texcoord : n == 3 ? o_texture3_texcoord :
                       n == 4 ? o_texture4_texcoord : n == 5 ? o_texture5_texcoord : n == 6 ? o_texture6_texcoord : o_texture7_texcoord);
}

// ---------------- sources (o_*_color) and channels (o_*_channel), as in BOTW ----------------
// ---------------- procedural textures (o_enable_proctextureN replaces textureN) ----------------
// type 100: eye highlight -- a disc at texcoord * 2A - A, pushed by the screen-space light direction * B
vec4 ProcEyeHighlight(vec2 uv, float A, float B)
{
    vec2 l = eLightDir.xy * SceneMat.p_dynamic_base_light_change_ratio;
    l *= inversesqrt(dot(l, l));
    vec2 d = vec2(fma(l.x, B, fma(uv.x * A, 2.0, -A)), fma(abs(l.y), B, fma(uv.y * A, 2.0, -A)));
    float v = Saturate(1.0 - dot(d, d));
    return vec4(Saturate(fma(Saturate((v - 0.666670024) * 12.0), 0.300000012, v * 0.200000003)));
}
vec4 ProcTexture(int type, vec2 uv, int paramA, int paramB)
{
    if (type == 100) return ProcEyeHighlight(uv, GetConstSource(paramA).x, GetConstSource(paramB).x);
    return vec4(0.0);   // other types: not decoded yet
}
bool IsProcTexture(int n, out int type, out int A, out int B)
{
    switch (n)
    {
#if o_enable_proctexture0
        case 0: type = o_proctexture0_type; A = o_proctexture0_param_A; B = o_proctexture0_param_B; return true;
#endif
#if o_enable_proctexture1
        case 1: type = o_proctexture1_type; A = o_proctexture1_param_A; B = o_proctexture1_param_B; return true;
#endif
#if o_enable_proctexture2
        case 2: type = o_proctexture2_type; A = o_proctexture2_param_A; B = o_proctexture2_param_B; return true;
#endif
#if o_enable_proctexture3
        case 3: type = o_proctexture3_type; A = o_proctexture3_param_A; B = o_proctexture3_param_B; return true;
#endif
#if o_enable_proctexture4
        case 4: type = o_proctexture4_type; A = o_proctexture4_param_A; B = o_proctexture4_param_B; return true;
#endif
#if o_enable_proctexture5
        case 5: type = o_proctexture5_type; A = o_proctexture5_param_A; B = o_proctexture5_param_B; return true;
#endif
    }
    type = 0; A = 0; B = 0;
    return false;
}

vec4 TexSample(int n)
{
    vec2 uv = TextureUV(n);
    int ptype, pA, pB;
    if (IsProcTexture(n, ptype, pA, pB)) return ProcTexture(ptype, uv, pA, pB);
    switch (n)
    {
        case 0: return texture(cTexture0, uv);
        case 1: return texture(cTexture1, uv);
        case 2: return texture(cTexture2, uv);
        case 3: return texture(cTexture3, uv);
        case 4: return texture(cTexture4, uv);
        case 5: return texture(cTexture5, uv);
        case 6: return texture(cTexture6, uv);
        case 7: return texture(cTexture7, uv);
    }
    return vec4(0.0);
}

#include "Expressions.glsl"

// 0..7: material texture N, 200..: outputs of the expression graph, 401..406: literal constants
vec4 GetSource(int id)
{
    if (id >= 0 && id <= 7) return TexSample(id);
    if (id >= 200 && id < 300) return ExpressionOutput(id - 200);
    switch (id)
    {
        case 401: return vec4(0.5);
        case 402: return vec4(1.0);
        case 403: return vec4(2.0);
        case 404: return vec4(4.0);
        case 405: return vec4(0.25);
        case 406: return vec4(0.75);
    }
    return vec4(0.0);
}
// channel = component * 10 + modifier; component 0 = rgba, 1..4 = broadcast r/g/b/a; modifier 0 = x, 1 = 1 - x, 2 = -x, 3 = x - 1
vec4 GetChannel(vec4 v, int ch)
{
    int comp = (ch % 1000) / 10, mod = ch % 10;
    if (ch == 2000) return fma(v, vec4(2.00787401), vec4(-1.00787401));
    if (ch >= 1000 && ch < 2000) v = vec4(Luminance(v.rgb));
    else if (comp == 1) v = v.xxxx;
    else if (comp == 2) v = v.yyyy;
    else if (comp == 3) v = v.zzzz;
    else if (comp == 4) v = v.wwww;
    if (mod == 1) v = 1.0 - v;
    else if (mod == 2) v = -v;
    else if (mod == 3) v = v - 1.0;
    return v;
}
vec4 GetSourceChannel(int id, int ch) { return GetChannel(GetSource(id), ch); }

// ---------------- mimicry (Mat.p_mimicly_ratio = layer + blend) ----------------
// albedo blends towards layer floor(ratio) of the forward terrain albedo; returns the blend (0 when off)
float Mimicry(inout vec3 albedo, vec2 uv)
{
    float layer = floor(Mat.p_mimicly_ratio);
    if (!(layer < Mat.p_mimicly_ratio))
        return 0.0;
    float l = float(clamp(uint(max(roundEven(layer), 0.0)), 0u, 65535u));
    vec3 terrain = texture(cTex_ForwardTerrainAlbedo, vec3(uv, l)).rgb;
    float f = Mat.p_mimicly_ratio - layer;
    albedo = mix(albedo, terrain, f);
    return f;
}

// tangent-space normal map (xy stored, z rebuilt); T is not renormalized, B = cross(N, T) * sign
vec3 ApplyNormalMap(vec3 N, vec4 T, vec2 xy)
{
    vec2 n = fma(xy, vec2(2.00787401), vec2(-1.00787401));
    float z = sqrt(Saturate(1.0 - dot(n, n)));
    return normalize(N * z + T.xyz * n.x + cross(N, T.xyz) * (n.y * T.w));
}

// hair / toon specular pattern: soft bands along the toon spec coordinate
float ToonSpecPattern(vec2 coord)
{
    vec2 f = fract(coord) - 0.5;
    vec2 s = clamp(1.0 - f * f, 0.0, 1.0);
    float p = s.x * s.y;
    return p * p;
}

#endif
