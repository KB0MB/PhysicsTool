// Fragment samplers (names = the game's own GLSL names from the BNSH reflection)
#ifndef UKING_SAMPLERS_GLSL
#define UKING_SAMPLERS_GLSL
// (bindings are the replica's; the bfsha maps each sampler by its //@ id)
layout (binding = 0)  uniform sampler2D            cGSys04_Projction0; //@ id="gsys_projection0"   // projection texture shadow
layout (binding = 1)  uniform sampler2D            cGSys12_SkyInscatter; //@ id="gsys_user0"
layout (binding = 2)  uniform sampler2D            cGSys13_SkyOcclusion; //@ id="gsys_depth_shadow_quarter"
layout (binding = 3)  uniform sampler2D            cGSys17_VolumeMask; //@ id="gsys_user2"   // screen space: x = sky mask, y = volume (cave) mask
layout (binding = 4)  uniform sampler2D            cGSys18_WorldShadow; //@ id="gsys_dynamic_reflection"   // world xz height shadow
layout (binding = 5)  uniform sampler2DArrayShadow cGSys07_DepthShadowCascade; //@ id="gsys_depth_shadow_cascade"
layout (binding = 6)  uniform sampler3D            cProc3DNoise; //@ id="gsys_user5"
layout (binding = 7)  uniform sampler2DArray       cTerrainAlbedo; //@ id="gsys_user3"   // mimicry (uk_mimicly_ratio)
layout (binding = 8)  uniform samplerCube          cGSys07_CubeEnvMap; //@ id="gsys_cube_map"
layout (binding = 9)  uniform sampler2D            cGSys23_LightAnalyzedData; //@ id="gsys_user4"
// material textures: cTextureN = bfres sampler _a0/_s0/_n0/... (see bfsha sampler list)
layout (binding = 10) uniform sampler2D      cTexture0; //@ id="_a0"
layout (binding = 11) uniform sampler2D      cTexture1; //@ id="_s0"
layout (binding = 12) uniform sampler2D      cTexture2; //@ id="_n0"
layout (binding = 13) uniform sampler2D      cTexture3; //@ id="_e0"
layout (binding = 14) uniform sampler2D      cTexture4; //@ id="_t0"
layout (binding = 15) uniform sampler2D      cTexture5; //@ id="_a1"
layout (binding = 16) uniform sampler2D      cTexture6;
layout (binding = 17) uniform sampler2D      cTexture7;
layout (binding = 18) uniform sampler2DArray cTextureArray0; //@ id="array0"
layout (binding = 19) uniform sampler2DArray cTextureArray1; //@ id="array1"
layout (binding = 20) uniform sampler2D      cGSys03_NormalizedLinearDepth; //@ id="gsys_normalized_linear_depth"   // scene depth (source 500)
layout (binding = 21) uniform sampler2D      cGSysRenderTargetColor; //@ id="gsys_color_buffer"   // scene color behind translucent surfaces (source 8)
layout (binding = 22) uniform sampler2D      cGSys16_Expand; //@ id="gsys_sssss"   // material variation 1 (cooking): screen-space mask
#if gsys_assign_type == GSYS_ASSIGN_GBUFFER
// translucent gbuffer blend (uking_enable_gbuffer_xlu_blend): the gbuffer written so far
layout (binding = 23) uniform sampler2D      cGSys00_GBuffAlbedo; //@ id="gsys_gbuffer_albedo"
layout (binding = 24) uniform sampler2D      cGSys01_GBuffNormal; //@ id="gsys_gbuffer_normal"
layout (binding = 25) uniform sampler2D      cGSys02_GBuffEmission; //@ id="gsys_gbuffer_emission"
#endif
// samplers of the original shading model the replica never reads (declared so the bfsha model matches)
layout (binding = 26) uniform sampler2D cVtxTexture0; //@ id="_v0"
layout (binding = 27) uniform sampler2D c0_TexLieMap; //@ id="gsys_depth_shadow_half"
layout (binding = 28) uniform sampler2D c1_TexWindSwell; //@ id="gsys_user1"
layout (binding = 29) uniform sampler2D c4_TexThickness; //@ id="lumberjack_thickness"
layout (binding = 30) uniform sampler2D cGSys10_Shadow; //@ id="gsys_static_depth_shadow"
layout (binding = 31) uniform sampler2D cGSys14_PreFog; //@ id="gsys_normal_modify"
layout (binding = 26) uniform sampler2D cGSys07_LightPrePass; //@ id="gsys_light_prepass"
layout (binding = 27) uniform sampler2D cGSys11_GBuffMaterialID; //@ id="gsys_gbuffer_material_id"
layout (binding = 28) uniform sampler2D cGSysDepthBuffer; //@ id="gsys_depth_buffer"
#endif
