#version 450 core
// TOTK material shader -- vertex shader
#include "material_options.h"
#include "Uniforms.glsl"
#include "Storage.glsl"
#include "Common.glsl"

#define VARYING out
#include "Varyings.glsl"

layout (location = 0) in vec4 aPosition;
layout (location = 1) in vec4 aNormal;
layout (location = 2) in vec4 aTangent;
layout (location = 4) in vec4 aBlendWeight0;
layout (location = 6) in uvec4 aBlendIndex0;
layout (location = 8) in vec4 aTexCoord0;
layout (location = 9) in vec4 aTexCoord1;
layout (location = 12) in vec4 aColor;

#define TOTK_VERTEX
#include "TexCoord.glsl"

#if gsys_assign_type == GSYS_ASSIGN_DYNAMIC0
uniform sampler2D cTex_GBuffAlbedo;
#endif

// ---------------- skinning (gsys_weight = number of bone influences) ----------------
// bone b: rows 3b .. 3b+2 of the skeleton block (3x4, row-major)
vec3 BonePos(uint b, vec3 p) { return vec3(Row3(Mtx.data[3 * b], p), Row3(Mtx.data[3 * b + 1], p), Row3(Mtx.data[3 * b + 2], p)); }
vec3 BoneDir(uint b, vec3 d) { return vec3(dot(Mtx.data[3 * b].xyz, d), dot(Mtx.data[3 * b + 1].xyz, d), dot(Mtx.data[3 * b + 2].xyz, d)); }

// each influence transforms the vertex and the results are blended (not the matrices); one influence = unweighted
#define SKIN(F, v) ( \
      F(aBlendIndex0.x, v) * (gsys_weight == 1 ? 1.0 : aBlendWeight0.x) \
    + (gsys_weight >= 2 ? F(aBlendIndex0.y, v) * aBlendWeight0.y : vec3(0.0)) \
    + (gsys_weight >= 3 ? F(aBlendIndex0.z, v) * aBlendWeight0.z : vec3(0.0)) \
    + (gsys_weight >= 4 ? F(aBlendIndex0.w, v) * aBlendWeight0.w : vec3(0.0)))


// ---------------- position modify (o_modify_position_type) ----------------
// type 2: the rest position moves along a vector by a calc-op of two sources (Link's face: const_vector0 * value2 * (1 - vertex colour)).
// shadow_only programs apply it only in dynamic2. Other types and calc ops: not decoded yet.
vec4 GetVtxSource(int id) { return id == 300 ? aColor : GetConstSource(id); }
vec4 GetChannelV(vec4 v, int ch)   // same as GetChannel() in Surface.glsl
{
    int comp = (ch % 1000) / 10, mod = ch % 10;
    if (comp == 1) v = v.xxxx; else if (comp == 2) v = v.yyyy; else if (comp == 3) v = v.zzzz; else if (comp == 4) v = v.wwww;
    if (mod == 1) v = 1.0 - v; else if (mod == 2) v = -v; else if (mod == 3) v = v - 1.0;
    return v;
}
vec4 CalcOp(int type, vec4 A, vec4 B)
{
    switch (type)
    {
        case 1: return A + B;
        case 2: return A * B;
    }
    return A;
}
#if o_modify_position_type == 2 && (!o_vtx_position_modify_shadow_only || gsys_assign_type == GSYS_ASSIGN_DYNAMIC2)
#define APPLY_POSITION_MODIFY 1
#else
#define APPLY_POSITION_MODIFY 0
#endif

void main()
{
    vec3 rest = aPosition.xyz;
#if APPLY_POSITION_MODIFY
    vec4 amount = CalcOp(o_vtx_position_modify_calc_type,
                         GetChannelV(GetVtxSource(o_vtx_position_modify_A), o_vtx_position_modify_A_channel),
                         GetChannelV(GetVtxSource(o_vtx_position_modify_B), o_vtx_position_modify_B_channel));
  #if o_vtx_position_modify_clamp
    amount = Saturate(amount);
  #endif
    rest += amount.xyz * GetVtxSource(o_vtx_position_modify_vector).xyz;
#endif
    vec3 pos = SKIN(BonePos, rest);
    vec3 nrm = normalize(SKIN(BoneDir, aNormal.xyz));
    vec3 tan = normalize(SKIN(BoneDir, aTangent.xyz));

#if gsys_assign_type == GSYS_ASSIGN_DYNAMIC0
    // instanced drawing (no object effect, and the G-buffer centre texel not in Ultrahand voxel mode):
    // per-instance 3x4 matrix at Instancing.data[2 + 3 i]
    if (Mat.p_attachment_effect == 0.0 && Mat.p_cook_ratio == 0.0 && Mat.p_special_effect_A == 0.0
        && textureLod(cTex_GBuffAlbedo, vec2(0.5), 0.0).y < 0.5)
    {
        int r = 2 + 3 * gl_InstanceID;
        pos = vec3(Row3(Instancing.data[r], pos), Row3(Instancing.data[r + 1], pos), Row3(Instancing.data[r + 2], pos));
    }
#endif
    // shape origin offset, then world -> view
    vec3 world = pos + ShpMtx.cShapeOffset.xyz;
    vec3 viewPos = vec3(Row3(cView0, world), Row3(cView1, world), Row3(cView2, world));
    vec3 viewN = vec3(dot(cView0.xyz, nrm), dot(cView1.xyz, nrm), dot(cView2.xyz, nrm));
    vec3 viewT = vec3(dot(cView0.xyz, tan), dot(cView1.xyz, tan), dot(cView2.xyz, tan));

    vec4 clip = vec4(Row3(cProj0, viewPos), Row3(cProj1, viewPos), Row3(cProj2, viewPos), Row3(cProj3, viewPos));
    gl_Position = clip;
    gl_PointSize = 1.0;

    CalcTexCoords();
#if USE_SURFACE
    fViewNormal.xyz = viewN;
    fViewTangent = vec4(viewT, aTangent.w);
#endif
#if USE_VIEW_POS
    fViewPosition.xyz = viewPos;
#endif
#if USE_LOCAL
    fLocalPos.xyz = aPosition.xyz;
#endif
#if gsys_assign_type == GSYS_ASSIGN_DYNAMIC0
    fLocalPos.w = float(gl_InstanceID);   // instance index
#endif

    // screen position (y down) for the volume mask
    vec2 screen = vec2((clip.x + clip.w) * 0.5, (clip.w - clip.y) * 0.5);
#if USE_SCREEN
    fScreenPos.xy = screen;
    fScreenPos.w = clip.w;
#endif

#if gsys_assign_type == GSYS_ASSIGN_DYNAMIC0
    fAmbient = vec4(0.0);   // not written by the game (undefined there)
#elif USE_AMBIENT
    // ambient: gradient between two SceneShadingInfo colours over screen y
    float sy = screen.y / clip.w;
    vec3 ambTop = vec3(SceneShadingF(36), SceneShadingF(37), SceneShadingF(38));
    vec3 ambBottom = vec3(SceneShadingF(40), SceneShadingF(41), SceneShadingF(42));
    fAmbient.xyz = mix(ambTop, ambBottom, sy);
    fAmbient.w = SceneShadingF(21);
    fShadingParam.x = SceneShadingF(20);
#endif
}
