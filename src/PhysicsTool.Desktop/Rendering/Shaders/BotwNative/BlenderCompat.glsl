// Blender plugin compatibility helper
// We mostly need to fix Y/Z up conversion here

#ifndef UKING_BLENDER_COMPAT_GLSL
#define UKING_BLENDER_COMPAT_GLSL

// ---- vertex attributes ----
// Blender meshes are Z-up and the game math is Y-up.
#define aPosition  vec4(vPosition.x, vPosition.z, -vPosition.y, 1.0)
#define aNormal    vec4(vNormal.x, vNormal.z, -vNormal.y, 0.0)
#define aTangent   vec4(vTangent.x, vTangent.z, -vTangent.y, vTangent.w)
#define aTexCoord0 vec4(vTexCoords0, 0.0, 0.0)
#define aTexCoord1 vec4(vTexCoords1, 0.0, 0.0)
#define aTexCoord2 vec4(vTexCoords2, 0.0, 0.0)
#define aTexCoord3 vec4(vTexCoords3, 0.0, 0.0)
#define aColor     vColor
#define aColor1    vec4(0.0)   // second color set (wind info) is not imported yet

#define cGSys03_NormalizedLinearDepth gsys_normalized_linear_depth   // linear depth / far (see BOTW_NEAR_FAR / BOTW_DIST)
#define cGSysRenderTargetColor        gsys_color_buffer              // scene color behind translucent surfaces

#define BlenderScreenShadow() texelFetch(gsys_static_depth_shadow, ivec2(gl_FragCoord.xy), 0).r

#endif
