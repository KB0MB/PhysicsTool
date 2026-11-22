#ifndef UKING_VARYINGS_GLSL
#define UKING_VARYINGS_GLSL
layout (location = 0) VARYING vec4 fViewNormal;        // xyz view-space normal (not normalized)
layout (location = 1) VARYING vec4 fViewPosition;      // xyz view-space position
layout (location = 2) VARYING vec4 fScreenCoords;      // x,y = (clip.w +- clip.xy) * 0.5, w = clip.w
layout (location = 3) VARYING vec4 fLightAnalyzeColor; // rgb + w from cGSys23_LightAnalyzedData
layout (location = 4) VARYING vec4 fViewTangent;       // xyz view-space tangent, w = bitangent sign
layout (location = 5) VARYING vec4 fTexCoord01;        // uking_texcoord0 (xy), uking_texcoord1 (zw)
layout (location = 6) VARYING vec4 fTexCoord23;
layout (location = 7) VARYING vec4 fTexCoord45;
layout (location = 8) VARYING vec4 fToonSpecCoords;    // xy = toon specular ramp coords
layout (location = 9) VARYING vec4 fColor;             // vertex color (aColor)
layout (location = 10) VARYING vec4 fLocalPosition;    // object-space position (procedural noise)
layout (location = 11) VARYING vec4 fNormalModify;     // uking_modify_normal_type 1: view-space offset added to the normal
layout (location = 12) VARYING vec4 fInnerLight;       // uking_enable_inner_light: xyz view dir to the light center, w = local distance
layout (location = 13) VARYING vec4 fLumberjack;        // uking_enable_lumberjack: xy = horizontal direction, z = trunk thickness scale
layout (location = 14) VARYING vec4 fViewBinormal;      // uking_enable_calc_binormal_vertex: per-vertex bitangent
layout (location = 15) VARYING vec4 fBakeCoord;        // enable_bake_texture: xy = aTexCoord1 * gsys_bake_st0.xy + gsys_bake_st0.zw
#endif
