// Material inputs: texture fetches, source selectors (uking_*_color / uking_colorN_X) and channel selection
#ifndef UKING_SURFACE_GLSL
#define UKING_SURFACE_GLSL

// texture arrays (uking_sampler_use_array): textureN reads layer texture_array_indexN.x of cTextureArray0/1
float ArrayLayer(float i) { return float(int(clamp(uint(max(roundEven(i), 0.0)), 0u, 65535u))); }

// plain fetch of material texture n (2D or array layer) at a given uv
vec4 FetchTex0(vec2 uv)
{
#if uking_sampler_use_array && uking_texture_array_texture0 == 0
    return texture(cTextureArray0, vec3(uv, ArrayLayer(mat.texture_array_index0.x)));
#elif uking_sampler_use_array && uking_texture_array_texture0 == 1
    return texture(cTextureArray1, vec3(uv, ArrayLayer(mat.texture_array_index0.x)));
#else
    return texture(cTexture0, uv);
#endif
}
vec4 FetchTex1(vec2 uv)
{
#if uking_sampler_use_array && uking_texture_array_texture1 == 0
    return texture(cTextureArray0, vec3(uv, ArrayLayer(mat.texture_array_index1.x)));
#elif uking_sampler_use_array && uking_texture_array_texture1 == 1
    return texture(cTextureArray1, vec3(uv, ArrayLayer(mat.texture_array_index1.x)));
#else
    return texture(cTexture1, uv);
#endif
}
vec4 FetchTex2(vec2 uv)
{
#if uking_sampler_use_array && uking_texture_array_texture2 == 0
    return texture(cTextureArray0, vec3(uv, ArrayLayer(mat.texture_array_index2.x)));
#elif uking_sampler_use_array && uking_texture_array_texture2 == 1
    return texture(cTextureArray1, vec3(uv, ArrayLayer(mat.texture_array_index2.x)));
#else
    return texture(cTexture2, uv);
#endif
}
vec4 FetchTex3(vec2 uv)
{
#if uking_sampler_use_array && uking_texture_array_texture3 == 0
    return texture(cTextureArray0, vec3(uv, ArrayLayer(mat.texture_array_index3.x)));
#elif uking_sampler_use_array && uking_texture_array_texture3 == 1
    return texture(cTextureArray1, vec3(uv, ArrayLayer(mat.texture_array_index3.x)));
#else
    return texture(cTexture3, uv);
#endif
}
vec4 FetchTex4(vec2 uv)
{
#if uking_sampler_use_array && uking_texture_array_texture4 == 0
    return texture(cTextureArray0, vec3(uv, ArrayLayer(mat.texture_array_index4.x)));
#elif uking_sampler_use_array && uking_texture_array_texture4 == 1
    return texture(cTextureArray1, vec3(uv, ArrayLayer(mat.texture_array_index4.x)));
#else
    return texture(cTexture4, uv);
#endif
}
vec4 FetchTex5(vec2 uv)
{
#if uking_sampler_use_array && uking_texture_array_texture5 == 0
    return texture(cTextureArray0, vec3(uv, ArrayLayer(mat.texture_array_index5.x)));
#elif uking_sampler_use_array && uking_texture_array_texture5 == 1
    return texture(cTextureArray1, vec3(uv, ArrayLayer(mat.texture_array_index5.x)));
#else
    return texture(cTexture5, uv);
#endif
}
// textures 6/7 reuse the sampler of texture uking_textureN_texture with their own texcoord
vec4 FetchAlias(int samplerId, vec2 uv)
{
    switch (samplerId)
    {
        case 0: return FetchTex0(uv);
        case 1: return FetchTex1(uv);
        case 2: return FetchTex2(uv);
        case 3: return FetchTex3(uv);
        case 4: return FetchTex4(uv);
        case 5: return FetchTex5(uv);
    }
    return vec4(0.0);
}

// indirect (uking_enable_indirectK): textureK offsets every texture that uses texcoord uking_indirectK_texcoord
//   type 0: offset = (textureK.xy * 2.00787401 - 1.00787401) * indirect_scaleK  (signed_fetch only changes the sampler format)
//   type 1: parallax, offset = textureK.x * (dot(V, T), dot(V, cross(N, T))) * indirect_scaleK
vec2 IndirectVector(vec4 t, int type, int signedFetch)
{
    if (type == 1)
    {
        vec3 V = normalize(fViewPosition.xyz);
#if UKING_LIGHT_ANALYSIS
        return t.x * V.xy;   // light analysis has no tangent frame: view x / y axes
#else
        vec3 T = fViewTangent.xyz, B = cross(fViewNormal.xyz, T);
        return t.x * vec2(dot(V, T), dot(V, B));
#endif
    }
    // uking_indirectN_signed_fetch: decode [0,1] -> [-1,1]; otherwise the texel is used as is
    return signedFetch != 0 ? fma(t.xy, vec2(2.00787401), vec2(-1.00787401)) : t.xy;
}
// uking_enable_indirectK_world_scaling: the offset shrinks with distance (1 / (-view z * cFovParams.y))
float IndirectWorldScale(int on) { return on != 0 ? 1.0 / (fViewPosition.z * -context.cFovParams.y) : 1.0; }
vec2 ApplyIndirect(int sel, vec2 uv)
{
#if uking_enable_indirect0
    if (uking_indirect0_texcoord == sel)
        uv = fma(IndirectVector(FetchTex0(GetTexCoord(uking_texture0_texcoord)), uking_indirect0_type, uking_indirect0_signed_fetch) * IndirectWorldScale(uking_enable_indirect0_world_scaling), mat.indirect_scale0, uv);
#endif
#if uking_enable_indirect1
    if (uking_indirect1_texcoord == sel)
        uv = fma(IndirectVector(FetchTex1(GetTexCoord(uking_texture1_texcoord)), uking_indirect1_type, uking_indirect1_signed_fetch) * IndirectWorldScale(uking_enable_indirect1_world_scaling), mat.indirect_scale1, uv);
#endif
#if uking_enable_indirect2
    if (uking_indirect2_texcoord == sel)
        uv = fma(IndirectVector(FetchTex2(GetTexCoord(uking_texture2_texcoord)), uking_indirect2_type, uking_indirect2_signed_fetch) * IndirectWorldScale(uking_enable_indirect2_world_scaling), mat.indirect_scale2, uv);
#endif
#if uking_enable_indirect3
    if (uking_indirect3_texcoord == sel)
        uv = fma(IndirectVector(FetchTex3(GetTexCoord(uking_texture3_texcoord)), uking_indirect3_type, uking_indirect3_signed_fetch) * IndirectWorldScale(uking_enable_indirect3_world_scaling), mat.indirect_scale3, uv);
#endif
#if uking_enable_indirect4
    if (uking_indirect4_texcoord == sel)
        uv = fma(IndirectVector(FetchTex4(GetTexCoord(uking_texture4_texcoord)), uking_indirect4_type, uking_indirect4_signed_fetch) * IndirectWorldScale(uking_enable_indirect4_world_scaling), mat.indirect_scale4, uv);
#endif
#if uking_enable_indirect5
    if (uking_indirect5_texcoord == sel)
        uv = fma(IndirectVector(FetchTex5(GetTexCoord(uking_texture5_texcoord)), uking_indirect5_type, uking_indirect5_signed_fetch) * IndirectWorldScale(uking_enable_indirect5_world_scaling), mat.indirect_scale5, uv);
#endif
    return uv;
}
vec2 TexUV(int sel) { return ApplyIndirect(sel, GetTexCoord(sel)); }

// ---------------- procedural textures (uking_enable_proctextureN replaces textureN) ----------------
// constant sources (material constants and literal numbers)
vec4 GetConstSource(int id)
{
    switch (id)
    {
        case 100: return mat.const_color0;
        case 101: return mat.const_color1;
        case 102: return mat.const_color2;
        case 103: return mat.const_color3;
        case 104: return vec4(mat.const_value0);
        case 105: return vec4(mat.const_value1);
        case 106: return vec4(mat.const_value2);
        case 107: return vec4(mat.const_value3);
        case 108: return vec4(mat.const_value4);
        case 109: return vec4(mat.const_value5);
        case 110: return vec4(mat.const_value6);
        case 111: return vec4(mat.const_value7);
        case 112: return mat.const_color4;
        case 113: return mat.const_color5;
        case 114: return mat.const_vector0;
        case 115: return mat.const_vector1;
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
// type 100: eye highlight -- a disc at texcoord * 2A - A, pushed by the screen-space light direction * B
vec4 ProcEyeHighlight(vec2 uv, float A, float B)
{
#if UKING_LIGHT_ANALYSIS
    vec2 l = MulRowsDir(context.cView, env.HemDirection0World.xyz).xy;   // light analysis: hemisphere direction
#else
    vec2 l = env.cLightDirection0.xy * sceneMat.uking_dynamic_base_light_change_ratio;
#endif
    l *= inversesqrt(dot(l, l));
    vec2 d = vec2(fma(l.x, B, fma(uv.x * A, 2.0, -A)), fma(abs(l.y), B, fma(uv.y * A, 2.0, -A)));
    float v = Saturate(1.0 - dot(d, d));
    return vec4(Saturate(fma(Saturate((v - 0.666670024) * 12.0), 0.300000012, v * 0.200000003)));
}
// type 2: sine wave along u; type 10: 3D noise at the object-space position * A
vec4 ProcSineU(vec2 uv) { return vec4(sin((uv.x - floor(uv.x)) * 6.28318548), 0.0, 0.0, 0.0); }
vec4 ProcNoise3D(float A) { return vec4(texture(cProc3DNoise, fLocalPosition.xyz * A).x); }
vec4 ProcTexture(int type, vec2 uv, int paramA, int paramB)
{
    if (type == 100) return ProcEyeHighlight(uv, GetConstSource(paramA).x, GetConstSource(paramB).x);
    if (type == 2) return ProcSineU(uv);
    if (type == 10) return ProcNoise3D(GetConstSource(paramA).x);
    return vec4(0.0);   // types 0 / 1: not used by any BOTW program
}

vec4 SampleTex0()
{
#if uking_enable_proctexture0
    return ProcTexture(uking_proctexture0_type, GetTexCoord(uking_texture0_texcoord), uking_proctexture0_param_A, uking_proctexture0_param_B);
#else
    return FetchTex0(TexUV(uking_texture0_texcoord));
#endif
}
vec4 SampleTex1()
{
#if uking_enable_proctexture1
    return ProcTexture(uking_proctexture1_type, GetTexCoord(uking_texture1_texcoord), uking_proctexture1_param_A, uking_proctexture1_param_B);
#else
    return FetchTex1(TexUV(uking_texture1_texcoord));
#endif
}
vec4 SampleTex2()
{
#if uking_enable_proctexture2
    return ProcTexture(uking_proctexture2_type, GetTexCoord(uking_texture2_texcoord), uking_proctexture2_param_A, uking_proctexture2_param_B);
#else
    return FetchTex2(TexUV(uking_texture2_texcoord));
#endif
}
vec4 SampleTex3()
{
#if uking_enable_proctexture3
    return ProcTexture(uking_proctexture3_type, GetTexCoord(uking_texture3_texcoord), uking_proctexture3_param_A, uking_proctexture3_param_B);
#else
    return FetchTex3(TexUV(uking_texture3_texcoord));
#endif
}
vec4 SampleTex4()
{
#if uking_enable_proctexture4
    return ProcTexture(uking_proctexture4_type, GetTexCoord(uking_texture4_texcoord), uking_proctexture4_param_A, uking_proctexture4_param_B);
#else
    return FetchTex4(TexUV(uking_texture4_texcoord));
#endif
}
vec4 SampleTex5()
{
#if uking_enable_proctexture5
    return ProcTexture(uking_proctexture5_type, GetTexCoord(uking_texture5_texcoord), uking_proctexture5_param_A, uking_proctexture5_param_B);
#else
    return FetchTex5(TexUV(uking_texture5_texcoord));
#endif
}

vec4 SampleTexture(int n)
{
    switch (n)
    {
        case 0: return SampleTex0();
        case 1: return SampleTex1();
        case 2: return SampleTex2();
        case 3: return SampleTex3();
        case 4: return SampleTex4();
        case 5: return SampleTex5();
        case 6: return FetchAlias(uking_texture6_texture, TexUV(uking_texture6_texcoord));
        case 7: return FetchAlias(uking_texture7_texture, TexUV(uking_texture7_texcoord));
    }
    return vec4(0.0);
}

// every material texture is fetched once per pass (keeps the compiled shader small)
vec4 gTex[8];
#define FETCH_ONCE(n) if (!sphereOnly || (uking_texture##n##_texcoord >= 3 && uking_texture##n##_texcoord <= 5)) gTex[n] = SampleTexture(n);
// sphereOnly: re-fetch only the textures on the procedural texcoords 3-5, which depend on the normal-mapped normal
void FetchTextures(bool sphereOnly)
{
    FETCH_ONCE(0) FETCH_ONCE(1) FETCH_ONCE(2) FETCH_ONCE(3) FETCH_ONCE(4) FETCH_ONCE(5) FETCH_ONCE(6) FETCH_ONCE(7)
}

vec4 calcColor[14] = vec4[14](vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0),
                               vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0));
// results of uking_colorN (source ids 200..213); a disabled calc color reads as 0 like in game
bool gNoVertexColor = false;  // z-only pass: vertex color is not interpolated (reads 0)
bool gEarlyEval = false;     // evaluating a color not flagged before_normal during the before_normal pass
float gSourceNdotV = 0.0;     // 503: clamp(dot(N, V) + 1)
float gSourceNdotVVertex = 0.0; // 502: clamp(dot(V, raw vertex normal) + 1)
float gSourceInnerLight = 0.0; // 511: inner light (uking_enable_inner_light)
float gSourceDepth = 0.0;     // 504: view depth (-viewPos.z)
float gSourceFovRatio = 0.0;  // 508: 1 / (-viewPos.z * cFovParams.y)
float gSourceNdotL = 0.0;     // 513: dot(N, -Ls) unclamped; back faces mirror N across the vertex normal
float gSourceVdotL = 0.0;     // 515: dot(V, Ls)
float gSourceDepthDiff = 0.0; // 500: scene linear depth - fragment depth (needs cGSys03_NormalizedLinearDepth)

vec2 gSceneColor0UV = vec2(0.0);  // set by SetupSceneUV()

float SceneDepthDiff(vec2 uv)   // scene linear depth - fragment depth
{
    return fma(texture(cGSys03_NormalizedLinearDepth, uv).x, context.cDist.x, context.cNearFar.x) + fViewPosition.z;
}

// scene color 0 / scene depth uv (screen texcoord plus the indirect offsets on it):
//  uking_enable_scene_color0_depthdiff1_effect: the offset is scaled by sat(depth diff at scene_depth1 * uking_depth_diff_scale_to_scene_color0)
//  uking_enable_scene_color0_edge_stop: the offset is dropped when the offset texel (point sampled) is in front of this surface
vec2 SceneColor0UV()
{
    vec2 base = GetTexCoord(uking_texture_scene_color0_texcoord);
    vec2 ofs = TexUV(uking_texture_scene_color0_texcoord) - base;
#if uking_enable_scene_color0_depthdiff1_effect
    ofs *= Saturate(SceneDepthDiff(GetTexCoord(uking_texture_scene_depth1_texcoord)) * GetConstSource(uking_depth_diff_scale_to_scene_color0).x);
#endif
#if uking_enable_scene_color0_edge_stop
    vec2 px = (floor((base + ofs) * context.cf.xy) + 0.5) * context.cf.zw;
    float keep = texture(cGSys03_NormalizedLinearDepth, px).x < -((fViewPosition.z + context.cNearFar.x) * context.cScreen.x) ? 0.0 : 1.0;
    return fma(ofs, vec2(keep), base);
#else
    return base + ofs;
#endif
}

void SetupSceneUV()
{
    gSceneColor0UV = SceneColor0UV();
#if uking_enable_depth_buffer
    // source 500 reads the scene depth through the same (offset, edge-stopped) uv when it shares the scene color texcoord
    vec2 depthUV = uking_texture_scene_depth0_texcoord == uking_texture_scene_color0_texcoord ? gSceneColor0UV : TexUV(uking_texture_scene_depth0_texcoord);
    gSourceDepthDiff = SceneDepthDiff(depthUV);
#else
    gSourceDepthDiff = SceneDepthDiff(fScreenCoords.xy * (1.0 / fScreenCoords.w));
#endif
}

// source ids shared by every selector option (see uking_mat_calccolor.h)
vec4 GetSource(int id)
{
    if (id >= 0 && id <= 7) return gTex[id];   // filled by FetchTextures()
    switch (id)
    {
        case 100: return mat.const_color0;
        case 101: return mat.const_color1;
        case 102: return mat.const_color2;
        case 103: return mat.const_color3;
        case 104: return vec4(mat.const_value0);
        case 105: return vec4(mat.const_value1);
        case 106: return vec4(mat.const_value2);
        case 107: return vec4(mat.const_value3);
        case 108: return vec4(mat.const_value4);
        case 109: return vec4(mat.const_value5);
        case 110: return vec4(mat.const_value6);
        case 111: return vec4(mat.const_value7);
        case 112: return mat.const_color4;
        case 113: return mat.const_color5;
        case 114: return mat.const_vector0;
        case 115: return mat.const_vector1;
        case 300: return gNoVertexColor ? vec4(0.0) : fColor;   // vertex color (aColor; not passed to the z-only pass)
        case 400: return vec4(0.0);
        case 401: return vec4(0.5);
        case 402: return vec4(1.0);
        case 403: return vec4(2.0);
        case 404: return vec4(4.0);
        case 405: return vec4(0.25);
        case 406: return vec4(0.75);
    }
    if (id == 503) return vec4(gEarlyEval ? 0.0 : gSourceNdotV);   // N.V is not known yet when pulled in early
    if (id == 500) return vec4(gSourceDepthDiff);
    if (id == 504) return vec4(gSourceDepth);
    if (id == 508) return vec4(gSourceFovRatio);
    if (id == 513) return vec4(gSourceNdotL);
    if (id == 502) return vec4(gSourceNdotVVertex);
    if (id == 8) return texture(cGSysRenderTargetColor, gSceneColor0UV);
    if (id == 511) return vec4(gSourceInnerLight);
    if (id == 518) return texture(cGSys23_LightAnalyzedData, vec2(0.875, 0.0));   // analyzed light color (slot 10)
    if (id == 515) return vec4(gSourceVdotL);
    if (id == 507) return vec4(sceneMat.uking_dynamic_exposure);
    if (id == 509) return vec4(env.data41.x);          // time
#if gsys_weight <= 0
    if (id == 510) return vec4(shape.cShapeData3.y);   // per-object phase
#endif
    if (id >= 200 && id <= 213) return calcColor[id - 200];
    return vec4(0.0);
}

// uking_*_channel = component * 10 + modifier
//   component: 0 = rgba, 1/2/3/4 = broadcast r/g/b/a
//   modifier:  0 = x, 1 = 1 - x, 2 = -x, 3 = x - 1
vec4 GetChannel(vec4 v, int ch)
{
    int comp = (ch % 1000) / 10, mod = ch % 10;
    if (ch == 2000) return fma(v, vec4(2.00787401), vec4(-1.00787401));   // 2000: signed decode (normal maps)
    if (ch >= 1000 && ch < 2000) v = vec4(Luminance(v.rgb));   // 1000-1003: luminance
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

// tangent-space normal map: xy from the selected source (decoded to [-1,1]), z reconstructed.
// uking_enable_decode_normalmap 0: the source already is a tangent-space normal (calc types 25 / 26 with channel 2000), used normalized.
vec3 DecodeNormalXY(vec2 xy)
{
    xy = fma(xy, vec2(2.00787401), vec2(-1.00787401));
    return vec3(xy, sqrt(1.0 - Saturate(fma(xy.x, xy.x, xy.y * xy.y))));
}
vec3 DecodeNormalMap(vec4 src)
{
#if uking_enable_decode_normalmap
    return DecodeNormalXY(src.xy);
#else
    return normalize(src.xyz);
#endif
}

vec3 ApplyNormalMap(vec3 N, vec4 T, vec3 n)
{
    vec3 B = cross(N, T.xyz) * T.w;
    return T.xyz * n.x + B * n.y + N * n.z;
}

// ---------------- inner light (source 511) ----------------
// the view ray refracted into the surface (eta 1.5) is aimed at a sphere (radius) around the light center;
// specular-like lobe pow(.., 8) mixed with a diffuse term by the diffusivity, inverse-square falloff clamped at the radius
float CalcInnerLight(vec3 N, vec3 V, float radius, float diffusivity)
{
#if uking_enable_inner_light
    // Light direction and distance from the inner-light uniform
    vec3  D    = normalize(fInnerLight.xyz);
    float dist = fInnerLight.w;
    vec3  P    = D * dist;                 // light position in view space

    // Refraction of V through the surface (eta = 1.5); zero on total internal reflection
    float c = dot(N, V);
    float k = fma(c * c, 2.25, -1.25);

    vec3 R = vec3(0.0);
    if (k >= 0.0)
    {
        // Stable reflection construction when k is non-negative
        float invSqrtK = 1.0 / inversesqrt(abs(k));
        R = fma(V, vec3(1.5), N * fma(c, -1.5, -invSqrtK));
    }

    // Project light position onto the plane perpendicular to R
    vec3 perp = fma(R, vec3(dot(R, P)), -P);

    // Closest point on the light sphere of given radius
    vec3 Q = fma(perp, vec3(Saturate(inversesqrt(dot(perp, perp)) * radius)), P);

    // Specular term (raised to a high power)
    float x  = Saturate(dot(R, Q * inversesqrt(dot(Q, Q))));
    float x2 = x * x;
    float x4 = x2 * x2;
    float x8 = x4 * x4;

    // Diffuse term
    float diffuse = Saturate(-dot(N, D));

    // Distance falloff (quadratic, scaled by radius)
    float invRadius = 1.0 / radius;
    float falloff   = Saturate(1.0 / (dist * invRadius * dist * invRadius));

    // Final intensity: mix between diffuse and specular, then scale by 1/r^2
    return falloff
         * fma(diffuse, diffusivity, fma(x8, -diffusivity, x8))
         * (invRadius * invRadius);
#else
    return 0.0;
#endif
}

// ---------------- calc color graph (uking_colorN_*) ----------------
// calc types documented in uking_mat_calccolor.h; 0 = A

#define CALC_COLOR(n) \
    if (uking_enable_calc_color##n != 0 && (BEFORE == 1 || uking_color##n##_before_normal == 0)) { \
        gEarlyEval = BEFORE == 1 && uking_color##n##_before_normal == 0; \
        vec4 cA = GetSourceChannel(uking_color##n##_A, uking_color##n##_A_channel); \
        vec4 cB = GetSourceChannel(uking_color##n##_B, uking_color##n##_B_channel); \
        vec4 cC = GetSourceChannel(uking_color##n##_C, uking_color##n##_C_channel); \
        vec4 cD = GetSourceChannel(uking_color##n##_D, uking_color##n##_D_channel); \
        calcColor[n] = CalcColorOp(uking_color##n##_calc_type, cA, cB, cC, cD); \
        if (uking_color##n##_clamp01 != 0) calcColor[n] = Saturate(calcColor[n]); \
        gEarlyEval = false; \
    }

// BEFORE = 1: every color, with the vertex normal (so before_normal colors can read any color they depend on);
// BEFORE = 0: colors not flagged uking_colorN_before_normal are re-evaluated with the normal-mapped normal
void EvalCalcColors(int BEFORE)
{
    CALC_COLOR(0) CALC_COLOR(1) CALC_COLOR(2) CALC_COLOR(3) CALC_COLOR(4) CALC_COLOR(5) CALC_COLOR(6)
    CALC_COLOR(7) CALC_COLOR(8) CALC_COLOR(9) CALC_COLOR(10) CALC_COLOR(11) CALC_COLOR(12) CALC_COLOR(13)
}
#endif
