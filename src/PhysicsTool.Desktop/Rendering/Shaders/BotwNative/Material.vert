#version 450 core
// BOTW uking_mat, gsys_assign_type = gsys_assign_material (variation 0) -- vertex shader
#include "uking_mat_options.h"
#if (BLENDER_RENDER == 0)
#include "Uniforms.glsl"
#endif
#include "Common.glsl"
#if (BLENDER_RENDER == 1)
#include "BlenderCompat.glsl"
#endif

// Vertex inputs and samplers. In Blender these come from MetaData.json (GPUShaderCreateInfo).
#if (BLENDER_RENDER == 0)
layout (location = 0)  in vec4 aPosition;     //@ id="_p0"
layout (location = 1)  in vec4 aNormal;       //@ id="_n0"
layout (location = 2)  in vec4 aTangent;      //@ id="_t0"
layout (location = 4)  in vec4 aBlendWeight0; //@ id="_w0"
layout (location = 5)  in vec4 aBlendWeight1; //@ id="_w1"
layout (location = 6)  in uvec4 aBlendIndex0; //@ id="_i0"
layout (location = 7)  in uvec4 aBlendIndex1; //@ id="_i1"
layout (location = 8)  in vec4 aTexCoord0;    //@ id="_u0"
layout (location = 9)  in vec4 aTexCoord1;    //@ id="_u1"
layout (location = 10) in vec4 aTexCoord2;    //@ id="_u2"
layout (location = 11) in vec4 aTexCoord3;    //@ id="_u3"
layout (location = 12) in vec4 aColor;        //@ id="_c0"
layout (location = 13) in vec4 aColor1;       //@ id="_c1"

layout (binding = 0) uniform sampler2D cGSys23_LightAnalyzedData;
layout (binding = 1) uniform sampler2D c1_TexWindSwell;
layout (binding = 2) uniform sampler2D c0_TexLieMap;
layout (binding = 3) uniform sampler2D c4_TexThickness;   // lumberjack cut profile
layout (binding = 4) uniform sampler2D cVtxTexture0;     // uking_vtx_texture0: vertex texture (water waves)
#endif

#define UKING_VERTEX
#define VARYING out
#if (BLENDER_RENDER == 0)
#include "Varyings.glsl"
#endif
#include "TexCoord.glsl"

vec4 GetChannelV(vec4 v, int ch)   // same as GetChannel() in Surface.glsl
{
    int comp = (ch % 1000) / 10, mod = ch % 10;
    if (comp == 1) v = v.xxxx; else if (comp == 2) v = v.yyyy; else if (comp == 3) v = v.zzzz; else if (comp == 4) v = v.wwww;
    if (mod == 1) v = 1.0 - v; else if (mod == 2) v = -v; else if (mod == 3) v = v - 1.0;
    return v;
}

// sources for uking_vtx_position_modify_*: 0 = cVtxTexture0 at uking_vtx_texture0_texcoord, else constants / vertex color
vec2 VtxGeneratedTexCoord(int sel)
{
    switch (sel)
    {
        case 0: return fTexCoord01.xy; case 1: return fTexCoord01.zw; case 2: return fTexCoord23.xy;
        case 6: return fTexCoord23.zw; case 7: return fTexCoord45.xy; case 8: return fTexCoord45.zw;
    }
    return fTexCoord01.xy;
}
vec4 GetVtxPosSource(int id)
{
#if uking_vtx_texture0
    if (id == 0) return textureLod(cVtxTexture0, VtxGeneratedTexCoord(uking_vtx_texture0_texcoord), 0.0);
#endif
    return GetVtxSource(id);
}

// ---------------- skinning (gsys_weight = number of bone influences, 0 = rigid shape matrix) ----------------
mat3x4 GetSkinMatrix()
{
#if gsys_weight <= 0
    return shape.cTransform;
#else
    mat3x4 m = skeleton.cBoneMtx[aBlendIndex0.x];
  #if gsys_weight >= 2
    m = m * aBlendWeight0.x + skeleton.cBoneMtx[aBlendIndex0.y] * aBlendWeight0.y;
  #endif
  #if gsys_weight >= 3
    m += skeleton.cBoneMtx[aBlendIndex0.z] * aBlendWeight0.z;
  #endif
  #if gsys_weight >= 4
    m += skeleton.cBoneMtx[aBlendIndex0.w] * aBlendWeight0.w;
  #endif
  #if gsys_weight >= 5
    m += skeleton.cBoneMtx[aBlendIndex1.x] * aBlendWeight1.x;
  #endif
  #if gsys_weight >= 6
    m += skeleton.cBoneMtx[aBlendIndex1.y] * aBlendWeight1.y;
  #endif
  #if gsys_weight >= 7
    m += skeleton.cBoneMtx[aBlendIndex1.z] * aBlendWeight1.z;
  #endif
  #if gsys_weight >= 8
    m += skeleton.cBoneMtx[aBlendIndex1.w] * aBlendWeight1.w;
  #endif
    return m;
#endif
}

void CalcLightAnalyze(float screenY)
{
#if uking_material_attribute == 0
    // character: blend between two analyzed samples by screen height
    vec3 a = textureLod(cGSys23_LightAnalyzedData, vec2(0.375, 0.0), 0.0).xyz;
    vec3 b = textureLod(cGSys23_LightAnalyzedData, vec2(0.458333343, 0.0), 0.0).xyz;
    fLightAnalyzeColor.rgb = fma(vec3(screenY), b - a, a);
    fLightAnalyzeColor.w = textureLod(cGSys23_LightAnalyzedData, vec2(0.0416666679, 0.0), 0.0).y;
#else
    fLightAnalyzeColor.rgb = textureLod(cGSys23_LightAnalyzedData, vec2(0.125, 0.0), 0.0).xyz;
    fLightAnalyzeColor.w = textureLod(cGSys23_LightAnalyzedData, vec2(0.0416666679, 0.0), 0.0).x;
#endif
}

// ---------------- lumberjack (uking_enable_lumberjack): trees cut / chopped at height 1.1 ----------------
float AtanApprox(float r)   // atan on [0,1], as compiled by the game
{
    float r2 = r * r;
    return r * fma(r2, fma(r2, fma(r2, fma(r2, fma(r2, -0.0134804696, 0.0574773103), -0.121239103), 0.195635900), -0.332994610), 0.999995589);
}

vec3 LumberjackDeform(vec3 p)
{
    // -------------------------------------------------------------------------
    // Approximate atan2(p.x, p.z) -> [0, 1] UV coordinate around the trunk
    // -------------------------------------------------------------------------
    float ax = abs(p.x);
    float az = abs(p.z);

    float ang = AtanApprox(min(ax, az) * (1.0 / max(ax, az)));

    if (ax > az)      ang = 1.57079601 - ang;   // pi/2
    if (p.z < 0.0)    ang = 3.14159298 - ang;   // pi
    if (p.x < 0.0)    ang = -ang;

    // Normalize angle to [0, 1]
    float u = fma(ang, 0.159154937, 0.5);       // ang / (2pi) + 0.5

    // -------------------------------------------------------------------------
    // Radial distance (slightly padded to avoid division by zero)
    // -------------------------------------------------------------------------
    float d2     = fma(p.z, p.z, p.x * p.x) + 0.01;
    float radial = Saturate(sqrt(d2)
                 * (1.0 / (mat.uking_lumberjack_canopy_radius * mat.uk_model_tree_scale)));

    // -------------------------------------------------------------------------
    // Thickness samples
    // -------------------------------------------------------------------------
    float thick  = textureLod(c4_TexThickness, vec2(u, fma(radial, 0.5, 0.375)), 0.0).x;
    float thick0 = textureLod(c4_TexThickness, vec2(u, 0.125), 0.0).x;

    // Horizontal direction away from the trunk axis
    vec2 dir = p.xz * inversesqrt(d2);

    // Height relative to the cut plane
    float h = p.y - 1.1;

    // Hard cut mask (very sharp)
    float cut    = Saturate((abs(h) - mat.uking_lumberjack_height_eps) * 10000.0);
    float t      = Saturate(thick * 10000.0);
    float notCut = 1.0 - cut;

    // -------------------------------------------------------------------------
    // Canopy sag
    // -------------------------------------------------------------------------
    float sag = radial
              * fma(thick * t, 2.0, -t)
              * fma(sqrt(fma(dir.y, 0.5, 0.5)), 0.5, 0.5)
              * Saturate(fma(-abs(h), 2.66666675, 1.0))
              * 0.375;

    // Weight that disables sag once the trunk is fully cut
    float sagW = fma(Saturate(fma(thick0, -10000.0, 10000.0)), -notCut, notCut);

    // Apply vertical sag
    float y = fma(sag, -sagW, p.y);

    // Radial scale (shrinks the cut part)
    float s = thick0 + fma(thick0, -cut, cut);

    // Store lumberjack data for later use
    fLumberjack = vec4(dir, s, 0.0);

    // -------------------------------------------------------------------------
    // Vertical lift / UV offset
    // -------------------------------------------------------------------------
    float q    = fma(y, mat.uk_model_tree_scale, -y) * (1.0 / mat.uk_model_tree_scale);
    float lift = fma(q, -Saturate(abs(h * 2.5)), q);

    gTexCoord0Offset = vec2(
        0.0,
        fma(sagW, sag * mat.uking_lumberjack_uv_scale,
                lift * mat.uking_lumberjack_uv_scale) * 0.5
    );
    return vec3(p.x * s, y - lift, p.z * s);
}

// ---------------- wind (uking_enable_wind_vtx_transform): sways world position ----------------
vec3 ApplyWind(vec3 wp, vec3 worldNormal)
{
    // -------------------------------------------------------------------------
    // Vertex weight
    // -------------------------------------------------------------------------
#if uking_enable_wind_vtx_transform_height
    float w = max(0.0, aPosition.y);                                    // weight from local height
#else
    float w = GetChannelV(aColor, uking_wind_vtx_transform_channel).x;  // per-vertex weight
#endif

    // -------------------------------------------------------------------------
    // Phase (per-object + optional per-blade variation)
    // -------------------------------------------------------------------------
    float phase = fract(fma(shape.cTransform[0].w, 0.414213568, shape.cTransform[0].x));

#if uking_enable_wind_vtx_transform_coreinfo
    // aColor1.xyz = offset to the blade / branch root
    // Wind is sampled at the root; phase varies per blade
    phase = fma(aPosition.x + aColor1.x, 0.02, phase);
    vec3 ws = wp + MulRowsDir(shape.cTransform, aColor1.xyz);
#else
    vec3 ws = wp;
#endif

    // -------------------------------------------------------------------------
    // Gust & flutter
    // -------------------------------------------------------------------------
    float swellOffset = fma(env.cWindParam.w, 403.677948, phase * 0.224265531);
    float flutter     = fma(w, 0.5, env.cWindParam.w) * 314.15;

    float gt = (phase + env.cWindParam.w) * 3.0;
    float gs = (abs(fract(gt) - 0.5) + 0.5) * env.cWindParam.x;

    float gust = fma(
        sin(gt * 157.079636) * Saturate(min(gs, 1.0 - gs) * 2.0),
        0.02,
        sin(gt * 157.079636 * 4.0) * Saturate(gs - 0.3) * 0.0571439974
    );

    float shake = sin(flutter * 5.0)
                * sin(flutter * 3.0)
                * sin(flutter * 7.0)
                * Saturate(env.cWindParam.x - 0.5);

    // -------------------------------------------------------------------------
    // Amplitude
    // -------------------------------------------------------------------------
#if uking_enable_wind_vtx_transform_lie
    float amp = fma(shake * Saturate(w - mat.uking_wind_vtx_transform_lie_height),
                    0.05,
                    w * mat.uking_wind_vtx_transform_intensity);
#else
    float amp = fma(w, mat.uking_wind_vtx_transform_intensity,
                    w * shake * 0.05);
#endif

    // -------------------------------------------------------------------------
    // Wind swell (two samples, blended)
    // -------------------------------------------------------------------------
    float swellA = gust + textureLod(
        c1_TexWindSwell,
        vec2(fma(ws.z, -env.cWindParam2.z,
                 fma(ws.x, -env.cWindParam2.y, swellOffset)) * 0.022295,
             env.cWindParam.x),
        0.0).x;

    float swellB = gust + textureLod(
        c1_TexWindSwell,
        vec2(fma(ws.z, -env.cWindParam.z,
                 fma(ws.x, -env.cWindParam.y, swellOffset)) * 0.022295,
             env.cWindParam.x),
        0.0).x;

    float lerpS = fma(swellA - swellB, env.cWindParam2.x, swellB) * amp * 20.0;

    // -------------------------------------------------------------------------
    // Displacement vector
    // -------------------------------------------------------------------------
    vec3 d;
    d.x = amp * fma(fma(swellA, env.cWindParam2.y, -swellB * env.cWindParam.y),
                    env.cWindParam2.x,
                    swellB * env.cWindParam.y) * 20.0;

    d.z = amp * fma(fma(swellA, env.cWindParam2.z, -swellB * env.cWindParam.z),
                    env.cWindParam2.x,
                    swellB * env.cWindParam.z) * 20.0;

    d.y = -max(0.0, lerpS * lerpS * lerpS * 0.16);

#if uking_edit_enable_wind_vtx_transform_scale
    // Sway scales with the object's vertical scale
    d *= length(shape.cTransform[1].xyz);
#endif

#if uking_enable_wind_vtx_transform_normal_lock
    // Never push the vertex into the surface
    float into = dot(worldNormal, d);
    if (into < 0.0)
        d -= worldNormal * into;
#endif

    vec3 o = wp + d;

    // -------------------------------------------------------------------------
    // Optional lie / flattening (grass)
    // -------------------------------------------------------------------------
#if uking_enable_wind_vtx_transform_lie
    vec2 lie = textureLod(
        c0_TexLieMap,
        vec2(fma(ws.x, env.cWindLieMapST.x, env.cWindLieMapST.z),
             fma(ws.z, env.cWindLieMapST.y, env.cWindLieMapST.w)),
        0.0).xy;

    float lieAmt = w * mat.uking_wind_vtx_transform_intensity
                     * mat.uking_wind_vtx_transform_lie_intensity;

    vec2 dl = vec2(
        fma(lie.x, 2.0, -1.0) *  lieAmt,
        fma(lie.y, 2.0, -1.0) * -lieAmt
    );

    float h = mat.uking_wind_vtx_transform_lie_height;

    o.x += dl.x;
    o.z += dl.y;
    o.y += sqrt(max(0.0, fma(h, h, -dot(dl, dl)))) - h;   // keep original length
#endif

    return o;
}

void main()
{
    // -------------------------------------------------------------------------
    // Skinning
    // -------------------------------------------------------------------------
    mat3x4 skin = GetSkinMatrix();

#if uking_enable_lumberjack
    vec3 worldPos = MulRows(skin, LumberjackDeform(aPosition.xyz));
#else
    vec3 worldPos = MulRows(skin, aPosition.xyz);
#endif

    vec3 worldPosRest   = worldPos;
    vec3 restViewNormal = MulRowsDir(context.cView,
                                     normalize(MulRowsDir(skin, aNormal.xyz)));

    CalcTexCoords(worldPosRest, restViewNormal);

    // -------------------------------------------------------------------------
    // Optional position modification (type 1)
    // -------------------------------------------------------------------------
#if uking_modify_position_type == 1 && !uking_vtx_position_modify_shadow_only
    // Position offset along a direction (often the normal).
    // Source 0 = cVtxTexture0 (e.g. water waves).
    vec4 pm = CalcColorOp(
        uking_vtx_position_modify_calc_type,
        GetChannelV(GetVtxPosSource(uking_vtx_position_modify_A), uking_vtx_position_modify_A_channel),
        GetChannelV(GetVtxPosSource(uking_vtx_position_modify_B), uking_vtx_position_modify_B_channel),
        GetChannelV(GetVtxPosSource(uking_vtx_position_modify_C), uking_vtx_position_modify_C_channel),
        GetChannelV(GetVtxPosSource(uking_vtx_position_modify_D), uking_vtx_position_modify_D_channel)
    );

  #if uking_vtx_position_modify_clamp
    pm = Saturate(pm);
  #endif

    vec3 pmDir = (uking_vtx_position_modify_vector == 100)
               ? aNormal.xyz
               : GetVtxSource(uking_vtx_position_modify_vector).xyz;

    worldPos = MulRows(skin, aPosition.xyz + pm.xyz * pmDir);
#endif

    // -------------------------------------------------------------------------
    // Optional wind
    // -------------------------------------------------------------------------
#if uking_enable_wind_vtx_transform
    worldPos = ApplyWind(worldPos, normalize(MulRowsDir(skin, aNormal.xyz)));
#endif

    // -------------------------------------------------------------------------
    // Final world -> view transform
    // -------------------------------------------------------------------------
    vec3 worldNormal = normalize(MulRowsDir(skin, aNormal.xyz));
    vec3 viewPos     = MulRows(context.cView, worldPos);

    fViewNormal = vec4(MulRowsDir(context.cView, worldNormal), 0.0);

#if uking_modify_position_type == 3
    // Outline-style extrusion along the view normal (constant in screen pixels).
    // A = width, cf.x = screen width.
    float extrude = GetVtxSource(uking_vtx_position_modify_A).x
                  * (viewPos.z * -context.cFovParams.y)
                  * (1.0 / context.cf.x);

    viewPos = fma(fViewNormal.xyz * extrude, vec3(1280.0), viewPos);
#endif

    fViewPosition = vec4(viewPos, 0.0);

    // -------------------------------------------------------------------------
    // Tangent / binormal
    // -------------------------------------------------------------------------
#if 1
    vec3 worldTangent = normalize(MulRowsDir(skin, aTangent.xyz));
    fViewTangent = vec4(MulRowsDir(context.cView, worldTangent), aTangent.w);

  #if uking_enable_calc_binormal_vertex
    vec3 worldBinormal = normalize(
        MulRowsDir(skin, cross(aNormal.xyz, aTangent.xyz) * aTangent.w)
    );
    fViewBinormal = vec4(MulRowsDir(context.cView, worldBinormal), 0.0);
  #endif
#endif

    // -------------------------------------------------------------------------
    // Inner light (lanterns, crystals, etc.)
    // -------------------------------------------------------------------------
#if uking_enable_inner_light
    // Light center is in object space
    vec3  toLight    = GetVtxSource(uking_inner_light_source_position).xyz - aPosition.xyz;
    float lightDist2 = dot(toLight, toLight);

    fInnerLight = vec4(
        MulRowsDir(context.cView, MulRowsDir(skin, toLight * inversesqrt(lightDist2))),
        sqrt(lightDist2)
    );
#endif

    // -------------------------------------------------------------------------
    // Normal modification (type 1) - bend normals away from a center
    // -------------------------------------------------------------------------
#if uking_modify_normal_type == 1
    // A.xyz = center (object space), A.w = strength  (e.g. round tree canopies)
    vec4 nm     = GetVtxSource(uking_vtx_normal_modify_A);
    vec3 center = MulRows(context.cView, MulRows(skin, nm.xyz));

    fNormalModify = vec4(normalize(viewPos - center) * nm.w, 0.0);
#endif

    // -------------------------------------------------------------------------
    // Projection & screen coordinates
    // -------------------------------------------------------------------------
    gl_Position = vec4(viewPos, 1.0) * context.cProj;

    float screenY = (gl_Position.w - gl_Position.y) * 0.5;
    fScreenCoords = vec4(
        (gl_Position.w + gl_Position.x) * 0.5,
        screenY,
        0.0,
        gl_Position.w
    );
#if (BLENDER_RENDER == 1)
    // Blender render targets have a bottom-left origin
    fScreenCoords.y = gl_Position.w - screenY;
#endif

#if enable_bake_texture
    fBakeCoord = vec4(
        fma(aTexCoord1.x, mat.gsys_bake_st0.x, mat.gsys_bake_st0.z),
        fma(aTexCoord1.y, mat.gsys_bake_st0.y, mat.gsys_bake_st0.w),
        0.0,
        0.0
    );
#endif

    CalcLightAnalyze(screenY * (1.0 / gl_Position.w));
}
