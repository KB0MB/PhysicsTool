
#ifndef UKING_UNIFORMS_GLSL
#define UKING_UNIFORMS_GLSL

layout (binding = 0, std140) uniform Context //@ id="gsys_context" size="2320"
{
    layout(offset = 0)   mat3x4 cView; //@ semantic="VIEW_YUP"
    layout(offset = 48)  mat4   cViewProj; //@ semantic="VIEW_PROJ_YUP"
    layout(offset = 112) mat4   cProj; //@ semantic="PROJ"
    layout(offset = 176) mat3x4 cViewInv; //@ semantic="VIEW_INV_YUP"
    layout(offset = 224) vec4   cNearFar; //@ semantic="BOTW_NEAR_FAR"   // [14]
    layout(offset = 240) vec4   cScreen; //@ semantic="BOTW_SCREEN"   // [15]
    layout(offset = 256) vec4   cDist; //@ semantic="BOTW_DIST"   // [16]
    layout(offset = 272) vec4   cFovParams; //@ semantic="FOV_PARAMS"   // [17]
    layout(offset = 288) vec4   cf; //@ semantic="SCREEN_SIZE"   // [18]
    layout(offset = 304) vec4   cc;               // [19]
    layout(offset = 320) vec4   sf; //@ semantic="TIME"   // [20]  x = time (frames / 30 based) used by special effects
    layout(offset = 336) mat3x4 cPrevView;        // [21-23]
    layout(offset = 384) mat4   cPrevViewProj;    // [24-27]
    layout(offset = 448) mat4   cPrevProj;        // [28-31]
    layout(offset = 512) mat3x4 cPrevViewInv;     // [32-34]
    layout(offset = 560) mat3x4 cProjectionTexMtx0; // [35-37] view -> projection texture (rows)
    layout(offset = 608) mat3x4 cProjectionTexMtx1; // [38-40]
    layout(offset = 656) vec4   cProjParams;      // [41]
    layout(offset = 672) mat4   cCascadeMtx[4];   // [42-57] view -> shadow cascade (rows; row 3 = depth)
    layout(offset = 928) vec4   cShadowParam1;    // [58]  x,y = cascade split depths
    layout(offset = 944) vec4   cShadowParam2;    // [59]
} context;

layout (binding = 1, std140) uniform ShpMtx //@ id="gsys_shape" type="shape" size="112"
{
    layout(offset = 0)  mat3x4 cTransform; //@ semantic="TRANSFORM_YUP"
    layout(offset = 48) vec4   cShapeData3;       // [3]
    layout(offset = 64) vec4   cShapeData4;       // [4]
    layout(offset = 80) vec4   cShapeData5;       // [5]
    layout(offset = 96) vec4   cShapeParam;  //@ default_value="1 1 1 1" // [6] x = visibility alpha (multiplies gsys_xlu_zprepass_alpha)
} shape;

layout (binding = 6, std140) uniform Mtx //@ id="gsys_skeleton" type="skeleton" size="48"
{
    mat3x4 cBoneMtx[1024];
} skeleton;

layout (binding = 3, std140) uniform Env //@ id="gsys_environment" type="scene" size="992"
{
    layout(offset = 0)   vec4 cAmbientColor; //@ default_value="0 0 0 1"   // [0]
    layout(offset = 16)  vec4 cHemiSkyColor; //@ default_value="0.5019608 0.5019608 0.5019608 1"   // [1]  BOTW: xyz,w used with cGSys17_VolumeMask.y (volume/cave light)
    layout(offset = 32)  vec4 cHemiGroundColor; //@ default_value="0.5019608 0.5019608 0.5019608 1"   // [2]
    layout(offset = 48)  vec4 cHemiDirection; //@ default_value="-0.01 0.9999935 -0.003626907 0"   // [3]
    layout(offset = 64)  vec4 cLightDirection0; //@ semantic="LIGHT_DIRECTION_VIEW" default_value="0.8175079 -0.4740172 -0.3270913 4"   // [4]  view space, points from light
    layout(offset = 80)  vec4 cLightColor; //@ default_value="4.4 3.96 3.74 4"   // [5]
    layout(offset = 96)  vec4 cLightSpecColor; //@ default_value="4 4 4 4"   // [6]
    layout(offset = 112) vec4 cLightDirection1; //@ default_value="0 1 0 0"   // [7]
    layout(offset = 128) vec4 cLightColor1; //@ default_value="0 0 0 1"   // [8]
    layout(offset = 144) vec4 cLightSpecColor1; //@ default_value="0 0 0 1"   // [9]
    layout(offset = 160) vec4 Fog0_Color; //@ default_value="0 0 0 0"   // [10]
    layout(offset = 176) vec4 Fog0_Direction; //@ default_value="0 0 0 0"   // [11]
    layout(offset = 192) vec4 Fog0_Param; //@ default_value="-0.01010101 0.00001010101 0 0"   // [12] x start, y end, z damp
    layout(offset = 208) vec4 Fog1_Color; //@ default_value="0 0 0 0"   // [13] w = intensity
    layout(offset = 224) vec4 Fog1_Direction; //@ default_value="0 0 0 0"   // [14] w = offset
    layout(offset = 240) vec4 Fog1_Param; //@ default_value="-0.01010101 0.00001010101 0 0"   // [15] x = start (height scale)
    layout(offset = 256) vec4 Fog2_Color; //@ default_value="0 0 0 0"   // [16] w = intensity
    layout(offset = 272) vec4 Fog2_Direction; //@ default_value="0 0 0 0"   // [17] w = offset
    layout(offset = 288) vec4 Fog2_Param; //@ default_value="-0.01010101 0.00001010101 0 0"   // [18] x = start, y = damp (pow)
    layout(offset = 304) vec4 Fog3_Color; //@ default_value="0 0 0 0"   // [19]
    layout(offset = 320) vec4 Fog3_Direction; //@ default_value="0 0 0 0"   // [20]
    layout(offset = 336) vec4 Fog3_Param; //@ default_value="-0.01010101 0.00001010101 0 0"   // [21]
    layout(offset = 352) vec4 LightDirection0World; //@ semantic="LIGHT_DIRECTION_WORLD" default_value="0 1 0 0"   // [22]
    layout(offset = 368) vec4 HemDirection0World; //@ default_value="0.292369 -0.9563048 -0.001275723 0"   // [23]
    layout(offset = 384) vec4 LightDirection1World; //@ default_value="0 1 0 0"   // [24]
    layout(offset = 400) vec4 data25; //@ default_value="-0.07066349 0.0104383 0.01484864 0.3762342"   // [25]
    layout(offset = 416) vec4 cSkyFogDistance; //@ default_value="-0.07834358 0.022879 0.00286472 0.3644175"   // [26] x scale, y bias (view depth -> sky factor)
    layout(offset = 432) vec4 cSkyInscatterParam; //@ default_value="-0.08234646 0.07531384 -0.001357785 0.3541074"   // [27] x,y pow, z,w scale/bias
    layout(offset = 448) vec4 cSkyHeightParam; //@ default_value="-0.03421519 -0.01010032 -0.07968342 0.001463851"   // [28] x scale, y bias (view depth)
    layout(offset = 464) vec4 cSkyParam; //@ default_value="-0.03502026 -0.004357998 -0.08492773 -0.003487148"   // [29] x,z pow, w scale
    layout(offset = 480) vec4 cSkyColor; //@ default_value="-0.04025555 0.002667315 -0.0995281 -0.01150249"   // [30] xyz color, w intensity
    layout(offset = 496) vec4 data31; //@ default_value="-0.02927722 -0.03509886 -0.04652298 1"   // [31]
    layout(offset = 512) vec4 cWindParam; //@ default_value="0.6120952 -0.7071068 -0.7071068 1.7729167" semantic="WIND"   // [32] x strength, yz direction (swell B), w time
    layout(offset = 528) vec4 cWindParam2; //@ default_value="0 -0.9792219 -0.20279174 1"   // [33] x swell blend, yz direction (swell A)
    layout(offset = 544) vec4 cWindLieMapST; //@ default_value="0.015625 0.015625 16.546875 -27.5625"   // [34] world xz -> c0_TexLieMap uv (scale xy, offset zw)
    layout(offset = 560) vec4 data35; //@ default_value="0.015625 0.015625 16.5625 -27.5625"   // [35]
    layout(offset = 576) vec4 data36; //@ default_value="0.001953125 0.001953125 2.5 -3.5"   // [36]
    layout(offset = 592) vec4 data37; //@ default_value="5445.358 0.35791016 0.85791016 0.2841797"   // [37]
    layout(offset = 608) vec4 data38; //@ default_value="0.01 100 40 -4000"   // [38]
    layout(offset = 624) vec4 cSkyOcclusionParam; //@ default_value="-5.84375 8.885417 0.0052083335 1000"   // [39] xy origin, z scale, w height scale
    layout(offset = 640) vec4 data40; //@ default_value="0.0070921984 0.0070921984 7.5 -12.5"   // [40]
    layout(offset = 656) vec4 data41; //@ default_value="0.22971955 0 0 0"   // [41]
    layout(offset = 672) vec4 cWorldShadowWeights; //@ default_value="0.9981837 0.0018162727 0 0"   // [42] dot weights for cGSys18_WorldShadow
    layout(offset = 688) mat3x4 cViewToWorldPos; //@ semantic="VIEW_INV_YUP" default_value="-0.9331805 0.017636223 -0.35897502 -1026.4442 -0 0.9987954 0.04907021 254.56644 0.35940796 0.04579136 -0.9320564 1796.9702"   // [43-45] view position -> world position (rows x, y, z)
    layout(offset = 736) vec4 data46; //@ default_value="0 0 0 0"   // [46]
    layout(offset = 752) vec4 data47; //@ default_value="0 0 0 0"   // [47]
    layout(offset = 768) vec4 data48; //@ default_value="0 0 0 0"   // [48]
    layout(offset = 784) vec4 data49; //@ default_value="0 0 0 0"   // [49]
    layout(offset = 800) vec4 data50; //@ default_value="0 0 0 0"   // [50]
    layout(offset = 816) vec4 data51; //@ default_value="0 0 0 0"   // [51]
    layout(offset = 832) vec4 cFogHeightBase; //@ default_value="0 0 0 0"   // [52] x = fog base height
    layout(offset = 848) vec4 cShadowFade; //@ default_value="0 0 0 0"   // [53] z = depth shadow fade start, w = world shadow fade start
} env;

layout (binding = 4, std140) uniform SceneMat //@ id="gsys_scene_material" type="scene" size="96"
{
    layout(offset = 0) vec4 uking_dynamic_toon_light_adjust_for_demo; //@ default_value="0 0 0 0"
    layout(offset = 16) float uking_dynamic_cloud_ratio; //@ default_value="1"
    layout(offset = 20) float uking_dynamic_depth_shadow_off; //@ default_value="0"
    layout(offset = 24) float uking_dynamic_proj_shadow_off; //@ default_value="0"
    layout(offset = 28) float uking_dynamic_rain_ratio; //@ default_value="1"
    layout(offset = 32) float uking_dynamic_rainfall; //@ default_value="0"
    layout(offset = 36) float uking_dynamic_exposure; //@ default_value="1"
    layout(offset = 40) float uking_dynamic_world_shadow_off; //@ default_value="0"
    layout(offset = 44) float uking_dynamic_base_light_change_ratio; //@ default_value="1"
    layout(offset = 48) float uking_dynamic_main_rendering_light; //@ default_value="0"
    layout(offset = 52) float uking_dynamic_skyocclusion_off; //@ default_value="0"
    layout(offset = 56) float uking_dynamic_depth_shadow_scale; //@ default_value="1"
    layout(offset = 60) float uking_dynamic_item_filter_alpha; //@ default_value="1"
    layout(offset = 64) float uking_dynamic_ui_highlight; //@ default_value="1"
    layout(offset = 68) float uking_const_proc_discard_scale1; //@ default_value="1"
    layout(offset = 72) float uking_const_proc_discard_scale2; //@ default_value="0"
    layout(offset = 76) float uking_const_proc_discard_scale3; //@ default_value="1"
    layout(offset = 80) float uking_dynamic_debug0; //@ default_value="1"
    layout(offset = 84) float uking_dynamic_debug1; //@ default_value="0"
    layout(offset = 88) float uking_dynamic_debug2; //@ default_value="1"
    layout(offset = 92) float uking_dynamic_debug3; //@ default_value="1"
} sceneMat;

layout (binding = 2, std140) uniform Mat //@ id="gsys_material" type="material" size="624"
{
    layout(offset = 0) float uking_wind_vtx_transform_intensity; //@ default_value="0.1"
    layout(offset = 4) float uking_wind_vtx_transform_lie_intensity; //@ default_value="3"
    layout(offset = 8) float uking_wind_vtx_transform_lie_height; //@ default_value="0.5"
    layout(offset = 12) float uking_lumberjack_height_eps; //@ default_value="0.1"
    layout(offset = 16) float uking_lumberjack_canopy_radius; //@ default_value="0.5"
    layout(offset = 20) float uking_lumberjack_uv_scale; //@ default_value="1 0 0"
    layout(offset = 32) vec4 gsys_bake_st0; //@ default_value="0 0 0 0"
    layout(offset = 48) vec4 gsys_bake_st1; //@ default_value="0 0 0 0"
    layout(offset = 64) vec4 gsys_bake_light_scale; //@ default_value="1 1 1 0"
    layout(offset = 80) mat2x4 tex_srt0; //@ default_value="1 -0 0 1 0 0 0 0"
    layout(offset = 112) mat2x4 tex_srt1; //@ default_value="1 -0 0 1 0 0 0 0"
    layout(offset = 144) mat2x4 tex_srt2; //@ default_value="1 -0 0 1 0 0 0 0"
    layout(offset = 176) mat2x4 tex_srt3; //@ default_value="100 -0 0 100 0 -99 0 0"
    layout(offset = 208) mat2x4 tex_srt4; //@ default_value="1 -0 0 1 0 0 0 0"
    layout(offset = 240) mat2x4 tex_srt5; //@ default_value="1 -0 0 1 0 0 0 0"
    layout(offset = 272) vec2 indirect_scale0; //@ default_value="1 1"
    layout(offset = 280) vec2 texture_array_index0; //@ default_value="0 0"
    layout(offset = 288) vec2 indirect_scale1; //@ default_value="1 1"
    layout(offset = 296) vec2 texture_array_index1; //@ default_value="0 0"
    layout(offset = 304) vec2 indirect_scale2; //@ default_value="1 1"
    layout(offset = 312) vec2 texture_array_index2; //@ default_value="0 0"
    layout(offset = 320) vec2 indirect_scale3; //@ default_value="1 1"
    layout(offset = 328) vec2 texture_array_index3; //@ default_value="0 0"
    layout(offset = 336) vec2 indirect_scale4; //@ default_value="1 1"
    layout(offset = 344) vec2 texture_array_index4; //@ default_value="0 0"
    layout(offset = 352) vec2 indirect_scale5; //@ default_value="1 1"
    layout(offset = 360) vec2 texture_array_index5; //@ default_value="0 0"
    layout(offset = 368) vec4 const_color0; //@ default_value="1 1 1 1"
    layout(offset = 384) vec4 const_color1; //@ default_value="1 1 1 1"
    layout(offset = 400) vec4 const_color2; //@ default_value="1 1 1 1"
    layout(offset = 416) vec4 const_color3; //@ default_value="1 1 1 1"
    layout(offset = 432) vec4 const_color4; //@ default_value="1 1 1 1"
    layout(offset = 448) vec4 const_color5; //@ default_value="1 1 1 1"
    layout(offset = 464) vec4 const_vector0; //@ default_value="0 0 0 0"
    layout(offset = 480) vec4 const_vector1; //@ default_value="0 0 0 0"
    layout(offset = 496) float const_value0; //@ default_value="1"
    layout(offset = 500) float const_value1; //@ default_value="1"
    layout(offset = 504) float const_value2; //@ default_value="1"
    layout(offset = 508) float const_value3; //@ default_value="1"
    layout(offset = 512) float const_value4; //@ default_value="1"
    layout(offset = 516) float const_value5; //@ default_value="1"
    layout(offset = 520) float const_value6; //@ default_value="1"
    layout(offset = 524) float const_value7; //@ default_value="1"
    layout(offset = 528) float uk_kari_mottled_ratio; //@ default_value="0"
    layout(offset = 532) float uk_mottled_scale; //@ default_value="1"
    layout(offset = 536) float uk_cook_ratio; //@ default_value="0"
    layout(offset = 540) float uk_kari_chemical_ice_ratio0; //@ default_value="0"
    layout(offset = 544) vec4 uk_damage_color; //@ default_value="1 0 0 0"
    layout(offset = 560) float uk_user_data; //@ default_value="0"
    layout(offset = 564) float uk_user_data1; //@ default_value="0"
    layout(offset = 568) float uk_object_attribute; //@ default_value="0"
    layout(offset = 572) float uk_special_effect_A; //@ default_value="0"
    layout(offset = 576) float uk_special_effect_B; //@ default_value="0"
    layout(offset = 580) float uk_special_effect_C; //@ default_value="0"
    layout(offset = 584) float uk_model_tree_scale; //@ default_value="1"
    layout(offset = 588) float uk_mimicly_ratio; //@ default_value="0"
    layout(offset = 592) float gsys_xlu_zprepass_alpha; //@ default_value="1"
    layout(offset = 596) float uk_bounding_radius; //@ default_value="1"
    layout(offset = 600) float uk_shadow_alpha; //@ default_value="1"
    layout(offset = 604) float uk_proc_discard; //@ default_value="1"
    layout(offset = 608) float uking_edit_proc_discard_scale; //@ default_value="1"
    layout(offset = 612) float gsys_alpha_test_ref_value; //@ default_value="0 0 0"
} mat;

layout (binding = 7, std140) uniform ResMat //@ id="gsys_res_material" size="16"
{
    vec4 cResMatData;
} resMat;
layout (binding = 8, std140) uniform ShaderOption //@ id="gsys_shader_option" type="option" size="0"
{
    vec4 cShaderOptionData;
} shaderOption;
layout (binding = 9, std140) uniform SkeletonEx //@ id="gsys_skeleton_ex" size="0"
{
    vec4 cSkeletonExData;
} skeletonEx;
layout (binding = 10, std140) uniform ShapeEx //@ id="gsys_shape_ex" size="0"
{
    vec4 cShapeExData;
} shapeEx;

#endif
