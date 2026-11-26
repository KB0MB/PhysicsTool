// TOTK material shader (material.Product.100) uniform blocks.
// Mat / SceneMat: exact bfsha member names and offsets (Mat defaults from the bfsha default buffer).
// SceneMat defaults: captured scene buffer (defaults/SceneMat.bin). Env: rows named as decoded (see Common.glsl), defaults captured too.
// Context / ShpMtx: no member table in the archive; layout from your Context layout, names in [] from AglShader.sharcb, the rest decoded (rowN = unknown).
#ifndef TOTK_UNIFORMS_GLSL
#define TOTK_UNIFORMS_GLSL

layout (binding = 0, std140) uniform _Context //@ id="gsys_context" size="2368"
{
    layout(offset = 0) mat3x4 cView; //@ semantic="VIEW"  // [0] world -> view (rows)
    layout(offset = 48) mat4 cViewProj; //@ semantic="VIEW_PROJ"  // [3] [cGSysCtxViewProj]
    layout(offset = 112) mat4 cProj; //@ semantic="PROJ"  // [7] [cGSysCtxProj] view -> clip (rows)
    layout(offset = 176) mat3x4 cViewInv; //@ semantic="VIEW_INV"  // [11] [cGSysCtxInvView] view -> world (rows)
    layout(offset = 224) vec4 cNearFar;  // [14] [cGSysCtxNearFarParam] znear, zfar, ratio, inverse ratio; linear depth = (viewZ + x) * -cNearFarParam2.x
    layout(offset = 240) vec4 cNearFarParam2;  // [15] [cGSysCtxNearFarParam2] (your layout: cScreen)
    layout(offset = 256) vec4 cNearFarParam3;  // [16] [cGSysCtxNearFarParam3] (your layout: cDist)
    layout(offset = 272) vec4 cFovParams;  // [17] [cGSysCtxFovyParam] .y: projection scale (1 / (-viewZ * y) = size of a pixel)
    layout(offset = 288) vec4 cFrameBuffer;  // [18] [cGSysCtxFrameBuffer] (your layout: cf)
    layout(offset = 304) vec4 cc;  // [19]
    layout(offset = 320) vec4 cTime;  // [20] .x: time (vanishing patterns), .y: frame counter (miasma scroll) 
    layout(offset = 336) mat3x4 cPrevView;  // [21]
    layout(offset = 384) mat4 cPrevViewProj;  // [24]
    layout(offset = 448) mat4 cPrevProj;  // [28]
    layout(offset = 512) mat3x4 cPrevViewInv;  // [32]
    layout(offset = 560) mat3x4 cProjectionTexMtx0;  // [35] world -> projected shadow texture (cTex_Projection0)
    layout(offset = 608) mat3x4 cProjectionTexMtx1;  // [38]
    layout(offset = 656) vec4 cProjParams;  // [41]
    layout(offset = 672) mat4 cCascadeMtx[4];  // [42] [cGSysCtxDepthShadowProj] cascade c: view -> shadow map (rows x, y, z, w)
    layout(offset = 928) vec4 cShadowParam1;  // [58] [cGSysCtxDepthShadowParam] .xyz: view depth where cascades 1, 2, 3 start
    layout(offset = 944) vec4 cShadowParam2;  // [59]
    layout(offset = 960) mat4 cDepthShadow;  // [60]
    layout(offset = 1024) mat4 cLightMtx; //@ semantic="LIGHT_MTX"  // [64]
    layout(offset = 1088) vec4 cCubemapHDR;  // [68]
    layout(offset = 1104) vec4 row69;
    layout(offset = 1120) vec4 row70;
    layout(offset = 1136) vec4 row71;
    layout(offset = 1152) vec4 row72;
    layout(offset = 1168) vec4 row73;
    layout(offset = 1184) vec4 row74;
    layout(offset = 1200) vec4 row75;
    layout(offset = 1216) vec4 row76;
    layout(offset = 1232) vec4 row77;
    layout(offset = 1248) vec4 row78;
    layout(offset = 1264) vec4 row79;
    layout(offset = 1280) vec4 row80;
    layout(offset = 1296) vec4 row81;
    layout(offset = 1312) vec4 row82;
    layout(offset = 1328) vec4 row83;
    layout(offset = 1344) vec4 row84;
    layout(offset = 1360) vec4 row85;
    layout(offset = 1376) vec4 row86;
    layout(offset = 1392) vec4 row87;
    layout(offset = 1408) vec4 row88;
    layout(offset = 1424) vec4 row89;
    layout(offset = 1440) vec4 row90;
    layout(offset = 1456) vec4 row91;
    layout(offset = 1472) vec4 row92;
    layout(offset = 1488) vec4 row93;
    layout(offset = 1504) vec4 row94;
    layout(offset = 1520) vec4 row95;
    layout(offset = 1536) vec4 row96;
    layout(offset = 1552) vec4 row97;
    layout(offset = 1568) vec4 row98;
    layout(offset = 1584) vec4 row99;
    layout(offset = 1600) vec4 row100;
    layout(offset = 1616) vec4 row101;
    layout(offset = 1632) vec4 row102;
    layout(offset = 1648) vec4 row103;
    layout(offset = 1664) vec4 row104;
    layout(offset = 1680) vec4 row105;
    layout(offset = 1696) vec4 row106;
    layout(offset = 1712) vec4 row107;
    layout(offset = 1728) vec4 row108;
    layout(offset = 1744) vec4 row109;
    layout(offset = 1760) vec4 row110;
    layout(offset = 1776) vec4 row111;
    layout(offset = 1792) vec4 row112;
    layout(offset = 1808) vec4 row113;
    layout(offset = 1824) vec4 row114;
    layout(offset = 1840) vec4 row115;
    layout(offset = 1856) vec4 row116;
    layout(offset = 1872) vec4 row117;
    layout(offset = 1888) vec4 row118;
    layout(offset = 1904) vec4 row119;
    layout(offset = 1920) vec4 row120;
    layout(offset = 1936) vec4 row121;
    layout(offset = 1952) vec4 row122;
    layout(offset = 1968) vec4 row123;
    layout(offset = 1984) vec4 row124;
    layout(offset = 2000) vec4 row125;
    layout(offset = 2016) vec4 row126;
    layout(offset = 2032) vec4 row127;
    layout(offset = 2048) vec4 row128;
    layout(offset = 2064) vec4 row129;
    layout(offset = 2080) vec4 row130;
    layout(offset = 2096) vec4 row131;
    layout(offset = 2112) vec4 row132;
    layout(offset = 2128) vec4 row133;
    layout(offset = 2144) vec4 row134;
    layout(offset = 2160) vec4 row135;
    layout(offset = 2176) vec4 row136;
    layout(offset = 2192) vec4 row137;
    layout(offset = 2208) vec4 row138;
    layout(offset = 2224) vec4 row139;
    layout(offset = 2240) vec4 row140;
    layout(offset = 2256) vec4 row141;
    layout(offset = 2272) vec4 row142;
    layout(offset = 2288) vec4 row143;
    layout(offset = 2304) vec4 row144;
    layout(offset = 2320) vec4 row145;
    layout(offset = 2336) vec2 cTileBufferFragCoordToBlockHeader;  // [146] [cGSysCtxTileBufferFragCoordToBlockHeader] tiles per pixel
    layout(offset = 2344) int cTileBufferDivideNum;  // [146] [cGSysCtxTileBufferDivideNum] tiles per row
    layout(offset = 2352) vec2 cTileBufferRenderBufferScale;  // [147] [cGSysCtxTileBufferRenderBufferScale]
    layout(offset = 2360) float cTileBufferMaterialIdDepthOffset;  // [147] [cGSysCtxTileBufferMaterialIdDepthOffset]
} Context;

layout (binding = 1, std140) uniform _ShpMtx //@ id="gsys_shape" type="shape" size="256"
{
    layout(offset = 0) mat3x4 cTransform; //@ semantic="TRANSFORM_YUP" default_value="1 0 0 0 0 1 0 0 0 0 1 0"  // [0] [cMtxShp] rigid shape transform (gsys_weight 0), rows
    layout(offset = 48) vec4 cShapeData3;  // [3] .y blends tex_srt1 towards tex_srt5 in some field programs
    layout(offset = 64) vec4 row4;
    layout(offset = 80) vec4 row5;
    layout(offset = 96) vec4 row6;
    layout(offset = 112) vec4 cShapeOffset; //@ default_value="0 0 0 0"  // [7] .xyz added to the skinned position (shape origin)
    layout(offset = 128) ivec4 cShapeFlags;  // [8] .y: bit fields (low 20 bits index, top 2 bits mode, ...)
    layout(offset = 144) vec4 row9;
    layout(offset = 160) vec4 row10;
    layout(offset = 176) vec4 row11;
    layout(offset = 192) vec4 row12;
    layout(offset = 208) vec4 row13;
    layout(offset = 224) vec4 row14;
    layout(offset = 240) vec4 row15;
} ShpMtx;

layout (binding = 6, std140) uniform _Mtx //@ id="gsys_skeleton" type="skeleton"
{
    vec4 data[4096];
} Mtx;

layout (binding = 3, std140) uniform _Env //@ id="gsys_environment" type="scene" size="1328"
{
    layout(offset = 0) vec4 AmbientColor; //@ default_value="0 0 0 1"
    layout(offset = 16) vec4 HemiSkyColor; //@ default_value="0.501960814 0.501960814 0.501960814 1"  // [cLightHemiSkyColor]
    layout(offset = 32) vec4 HemiGroundColor; //@ default_value="0.501960814 0.501960814 0.501960814 1"
    layout(offset = 48) vec4 HemiDirection; //@ default_value="-2.32830644e-10 0.999993503 -0.00362690678 0"  // view space
    layout(offset = 64) vec4 LightDirection0; //@ default_value="0.817507863 -0.474017173 -0.327091336 4"  // [cLightDir0] .xyz: view-space direction the main light travels
    layout(offset = 80) vec4 LightColor0; //@ default_value="4.4000001 3.96000004 3.74000025 4"  // [cLightColor0] .rgb: main (toon) light colour
    layout(offset = 96) vec4 LightSpecColor0; //@ default_value="4 4 4 4"
    layout(offset = 112) vec4 LightDirection1; //@ default_value="0 1 0 0"
    layout(offset = 128) vec4 LightColor1; //@ default_value="0 0 0 1"
    layout(offset = 144) vec4 LightSpecColor1; //@ default_value="0 0 0 1"
    layout(offset = 160) vec4 Fog0Color; //@ default_value="0.629200041 0.529100001 0.275000006 0.119999997"  // fog[0] colour, w = density
    layout(offset = 176) vec4 Fog0Dir; //@ default_value="0 0 -1 -0"  // fog[0] dir, w
    layout(offset = 192) vec4 Fog0Param; //@ default_value="0.00333333341 1 0 0"  // fog[0] start / end / damp
    layout(offset = 208) vec4 Fog1Color; //@ default_value="1 1 1 1"
    layout(offset = 224) vec4 Fog1Dir; //@ default_value="0 0 -1 -0.0526315756"
    layout(offset = 240) vec4 Fog1Param; //@ default_value="0.00105263153 1.5 0 0"
    layout(offset = 256) vec4 Fog2Color; //@ default_value="0.315833777 0.265587479 0.138039231 0.119999997"  // fog[2] (distance fog A): rgb, w = density
    layout(offset = 272) vec4 Fog2Dir; //@ default_value="0 0 -1 0.199999988"  // fog[2] dir; .w used as the distance offset
    layout(offset = 288) vec4 Fog2Param; //@ default_value="0.0199999996 1 0 0"  // fog[2] .x: distance scale, .y: exponent
    layout(offset = 304) vec4 Fog3Color; //@ default_value="0.0588235296 0.200000003 0.200000003 0.300000012"  // fog[3] (volume-mask fog B): rgb, w = density
    layout(offset = 320) vec4 Fog3Dir; //@ default_value="0 0 -1 -0"  // fog[3] dir; .w used as the distance offset
    layout(offset = 336) vec4 Fog3Param; //@ default_value="0.0125000002 1 0 0"  // fog[3] .x: distance scale, .y: exponent
    layout(offset = 352) vec4 HemiDirectionWorld; //@ default_value="0 1 0 0"  // world space
    layout(offset = 368) vec4 LightDirection0World; //@ default_value="-0.857159376 -0.472827673 -0.204234809 0"  // [cLightDir0World] (.xz scrolls the sky island shadow)
    layout(offset = 384) vec4 Dir2; //@ default_value="0 1 0 0"
    layout(offset = 400) vec4 row25; //@ default_value="0 0 0 0"
    layout(offset = 416) vec4 InscatterDist; //@ default_value="4.17449373e-05 0.00187852222 0 0"  // .x: distance scale, .y: offset (GSYSENV.bt: vec[1])
    layout(offset = 432) vec4 InscatterParam; //@ default_value="45 2.5 0.850000024 -0.0218843166"  // .x/.y: exponents, .z: strength, .w: v floor (bt: vec[2])
    layout(offset = 448) vec4 HazeDist; //@ default_value="0.00333333341 0 0 0"  // haze distance ramp: sat(dist * x - y) (bt: vec[3])
    layout(offset = 464) vec4 HazeParam; //@ default_value="4 0 0.576000035 0.699999988"  // .x distance exponent, .z/.w horizon exponent / strength (bt: vec[4])
    layout(offset = 480) vec4 HazeColor; //@ default_value="0.629200041 0.529100001 0.275000006 0.119999997"  // rgb, w = strength (bt: vec[5])
    layout(offset = 496) vec4 row31; //@ default_value="0.00140380859 -0.000244140625 -5.28704834 1"
    layout(offset = 512) vec4 row32; //@ default_value="0.846519709 0.979525745 -0.201318935 0.0549999997"
    layout(offset = 528) vec4 row33; //@ default_value="1 0.979525745 -0.201318935 0"
    layout(offset = 544) vec4 row34; //@ default_value="0.015625 0.015625 -6.5625 -15.5234375"
    layout(offset = 560) vec4 row35; //@ default_value="0 0 0 0"
    layout(offset = 576) vec4 row36; //@ default_value="0 0 0 0"
    layout(offset = 592) vec4 row37; //@ default_value="7.28333282 0.283332825 0.783332825 0.433334351"
    layout(offset = 608) vec4 row38; //@ default_value="0.00999999978 100 40 -4000"
    layout(offset = 624) vec4 row39; //@ default_value="1.84375 4.86458349 0.00520833349 0"
    layout(offset = 640) vec4 row40; //@ default_value="1000 3600 1300 0"
    layout(offset = 656) vec4 row41; //@ default_value="0 0 0 0"
    layout(offset = 672) vec4 row42; //@ default_value="0.21479167 0 0 0"
    layout(offset = 688) vec4 WorldShadowWeight; //@ default_value="1 0 0 0"  // [cEnvWorldShadowWeight] .xyz: weights of the world shadow height texel
    layout(offset = 704) vec4 EnvironmentMaskParam0; //@ default_value="-0.00999999978 0 0.00666666683 1.66666675"  // [cEnvironmentMaskParam0] Depths: height fades (.xy / .zw = scale, offset)
    layout(offset = 720) vec4 EnvironmentMaskParam1; //@ default_value="9.99999975e-05 0.000125000006 0.5 0.5"  // [cEnvironmentMaskParam1] Depths darkness texture: uv = world.xz * xy + zw
    layout(offset = 736) vec4 EnvironmentMaskParam2; //@ default_value="0 0 1 0"  // [cEnvironmentMaskParam2] Depths darkness: .x floor, .y/.z texture range
    layout(offset = 752) vec4 MinusFieldTint; //@ default_value="-0.00999999978 -1 0 0"  // .xy: height ramp, .z: tint at full ramp
    layout(offset = 768) vec4 DepthShadowFade0; //@ default_value="0.0500000007 -5 0.0250000004 -10.25"  // [cEnvDepthShadowFade0] .xy: fade of cascades 0..2, .zw: fade of cascade 3
    layout(offset = 784) vec4 DepthShadowFade1; //@ default_value="0.0199999996 -1 0 0"  // [cEnvDepthShadowFade1] .xy: distance fade of the world shadow
    layout(offset = 800) vec4 ShadowBias; //@ default_value="0.00499999989 0.00200000009 0 0"  // .x: normal offset scale, .y: depth bias
    layout(offset = 816) vec4 DepthShadowOffsetField; //@ default_value="0.0500000007 0.150000006 0.00150000001 0"  // [cEnvDepthShadowOffsetField]
    layout(offset = 832) ivec4 DepthShadowCascadeInfo; //@ default_value="4 4 0 0"  // [cEnvDepthShadowCascadeInfo] ints: .x layers per lod, .y cascades per lod
    layout(offset = 848) vec4 row53; //@ default_value="0 0 0 0"
    layout(offset = 864) vec4 row54; //@ default_value="0 0 0 0"
    layout(offset = 880) vec4 row55; //@ default_value="0 0 0 0"
    layout(offset = 896) vec4 DepthShadowPcf; //@ default_value="0.00124999997 0.00124999997 0.00200000009 0"  // [cEnvDepthShadowPcf] .xy: cascade texel size
    layout(offset = 912) vec4 DepthShadowNormalCoeff; //@ default_value="1 1 0 2"  // [cEnvDepthShadowNormalCoeff] .x/.y: weight of shading / vertex normal, .z: exponent
    layout(offset = 928) vec4 row58; //@ default_value="0 0 0 0"
    layout(offset = 944) vec4 row59; //@ default_value="0.569129109 0.238617033 5.65342331 0.359908104"
    layout(offset = 960) vec4 row60; //@ default_value="0.0370369032 -0.000100156773 0.0370369032 -0.000100156773"
    layout(offset = 976) vec4 row61; //@ default_value="0.0340742432 -0.0145151 0.0340742432 -0.0145151"
    layout(offset = 992) vec4 row62; //@ default_value="0.0208409727 -0.00428338163 0.0208409727 -0.00428338163"
    layout(offset = 1008) vec4 row63; //@ default_value="0 0 0 0"
    layout(offset = 1024) vec4 SkyFogParam0; //@ default_value="-0.5 0.00100000005 0 0"  // [cSkyFogParam0]
    layout(offset = 1040) vec4 SkyFogParam1; //@ default_value="0 0 1523.23926 1"  // [cSkyFogParam1]
    layout(offset = 1056) vec4 SkyFogParam2; //@ default_value="0.600000024 2.50000024 0.949999988 0.100000001"  // [cSkyFogParam2]
    layout(offset = 1072) vec4 row67; //@ default_value="15000 20000 24800 0"
    layout(offset = 1088) vec4 GroundFog; //@ default_value="0.646800041 1.3706001 0.838200033 0.5"  // rgb, w = density
    layout(offset = 1104) vec4 GroundFogY; //@ default_value="-0.0909090862 0.0045454544 250 0"  // height ramp: sat(x + (z - worldY) * y)
    layout(offset = 1120) vec4 Unknown70; //@ default_value="950 50 1.5 1"  // emission dim (1 - pow(0, z) * w); meaning unknown
    layout(offset = 1136) vec4 row71; //@ default_value="1 1 1 1"
    layout(offset = 1152) vec4 row72; //@ default_value="0 0 0 0"
    layout(offset = 1168) vec4 row73; //@ default_value="0 0 0 0"
    layout(offset = 1184) vec4 row74; //@ default_value="0.176470593 0.411764711 0.203921601 1"
    layout(offset = 1200) vec4 row75; //@ default_value="0.0666666701 0.200000003 0.125490203 1"
    layout(offset = 1216) vec4 row76; //@ default_value="0 0.0313725509 0.0196078401 0.899999976"
    layout(offset = 1232) vec4 row77; //@ default_value="-0 0.00714285718 1 1"
    layout(offset = 1248) vec4 row78; //@ default_value="9.99999975e-05 0.5 0.000125000006 0.5"
    layout(offset = 1264) vec4 row79; //@ default_value="3.43925452 1.89716733 0.819468975 1"
    layout(offset = 1280) vec4 row80; //@ default_value="0.979525745 0 -0.201318935 0"
    layout(offset = 1296) vec4 CaveLightColor; //@ default_value="1 1 1 1"  // [cCaveLightColor] rgb tint applied by the volume mask (.w)
    layout(offset = 1312) vec4 MinusFieldRect; //@ default_value="248 828 0.00249999994 0"  // Depths light map: uv = (world.xz - xy) * z
} Env;

layout (binding = 4, std140) uniform _SceneMat //@ id="gsys_scene_material" size="576" type="scene"
{
    layout(offset = 0) vec4 p_dynamic_toon_light_adjust_for_demo; //@ default_value="1 1 1 1"
    layout(offset = 16) float p_dynamic_cloud_ratio; //@ default_value="1"
    layout(offset = 20) float p_dynamic_depth_shadow_off; //@ default_value="0"
    layout(offset = 24) float p_dynamic_proj_shadow_off; //@ default_value="1"
    layout(offset = 28) float p_dynamic_rain_ratio; //@ default_value="0"
    layout(offset = 32) float p_dynamic_rainfall; //@ default_value="0"
    layout(offset = 36) float p_dynamic_exposure; //@ default_value="0"
    layout(offset = 40) float p_dynamic_world_shadow_off; //@ default_value="0"
    layout(offset = 44) float p_dynamic_base_light_change_ratio; //@ default_value="1"
    layout(offset = 48) float p_dynamic_main_rendering_light; //@ default_value="1"
    layout(offset = 52) float p_dynamic_skyocclusion_off; //@ default_value="0"
    layout(offset = 56) float p_dynamic_depth_shadow_scale; //@ default_value="1"
    layout(offset = 60) float p_dynamic_item_filter_alpha; //@ default_value="0"
    layout(offset = 64) float p_dynamic_ui_highlight; //@ default_value="0"
    layout(offset = 68) float p_const_vanishing_local_pos_scale; //@ default_value="0.100000001"
    layout(offset = 72) float p_const_vanishing_pattern_repeeat_ratio; //@ default_value="10"
    layout(offset = 76) float p_const_vanishing_pattern_scroll_speed; //@ default_value="0.25"
    layout(offset = 80) vec3 p_const_vanishing_color0_start; //@ default_value="1 1 5"
    layout(offset = 92) float p_const_vanishing_whole_alpha; //@ default_value="1"
    layout(offset = 96) vec3 p_const_vanishing_color0_end; //@ default_value="0.100000001 1 0.100000001"
    layout(offset = 108) float p_const_vanishing_shaving_fetch_scale; //@ default_value="40"
    layout(offset = 112) vec3 p_const_vanishing_color1_start; //@ default_value="1.5 6 1"
    layout(offset = 124) float p_const_vanishing_shaving_fetch_offset; //@ default_value="1"
    layout(offset = 128) vec3 p_const_vanishing_color1_end; //@ default_value="0.100000001 5 0.25"
    layout(offset = 140) float p_const_vanishing_become_max_intensity_ratio; //@ default_value="0.330000013"
    layout(offset = 144) vec3 p_const_vanishing_pattern_fetch_scale; //@ default_value="2 2 2"
    layout(offset = 156) float p_dynamic_water_specular_ao_in_cave; //@ default_value="0.800000012"
    layout(offset = 160) vec4 p_const_reverse_recorder_base_color; //@ default_value="1 0.899999976 0 0.300000012"
    layout(offset = 176) vec4 p_const_reverse_recorder_edge_color; //@ default_value="0 0 0 0"
    layout(offset = 192) vec4 p_const_reverse_recorder_stripe_color; //@ default_value="0 0 0 0"
    layout(offset = 208) float p_const_filter_min_range_for_terrain; //@ default_value="3"
    layout(offset = 212) float p_const_filter_min_range_for_target; //@ default_value="3"
    layout(offset = 216) float p_const_water_normal_offset_scale; //@ default_value="1"
    layout(offset = 220) float p_const_xlu_shadow_diffuse_ambient_scale; //@ default_value="4"
    layout(offset = 224) float p_const_ao_pow; //@ default_value="1"
    layout(offset = 228) float p_const_ao_mul; //@ default_value="1"
    layout(offset = 232) float p_const_ao_add; //@ default_value="0"
    layout(offset = 236) float p_dynamic_depth_shadow_softness; //@ default_value="0"
    layout(offset = 240) float p_dynamic_behind_screen_distance; //@ default_value="24600"
    layout(offset = 244) float p_dynamic_horizon_height; //@ default_value="120"
    layout(offset = 248) float p_const_sky_occlusion_radius; //@ default_value="2"
    layout(offset = 252) float p_const_sky_occlusion_height_diff; //@ default_value="64"
    layout(offset = 256) vec4 p_dynamic_object_id_output_ctrl; //@ default_value="1 0 0 0"
    layout(offset = 272) vec4 p_dynamic_darkenv_field_color; //@ default_value="0 0 0 0"
    layout(offset = 288) vec4 p_dynamic_darkenv_chara_color; //@ default_value="0 0 0 0.5"
    layout(offset = 304) float p_dynamic_darkenv_chara_sat; //@ default_value="0.200000003"
    layout(offset = 308) float p_const_ao_min; //@ default_value="0.319999993"
    layout(offset = 312) float p_const_edge_adjust_quat_to_half; //@ default_value="2"
    layout(offset = 316) float p_const_edge_adjust_normal; //@ default_value="5"
    layout(offset = 320) vec4 p_const_blueprint_ghost_shotage_color; //@ default_value="0.850000024 0.5 2 0.75"
    layout(offset = 336) vec4 p_const_blueprint_edge_param; //@ default_value="0.5 0.5 0.75 3.5"
    layout(offset = 352) vec4 p_dynamic_round_discard_param; //@ default_value="0 0 0.100000001 0.100000001"
    layout(offset = 368) vec4 p_const_figure_param; //@ default_value="0.649999976 0.75 0.5 0.5"
    layout(offset = 384) vec4 p_const_blueprint_ghost_alternate_color; //@ default_value="1.45000005 0.574999988 2 1"
    layout(offset = 400) vec3 p_const_blueprint_base_albedo0; //@ default_value="0.0399999991 0.129999995 0.0799999982"
    layout(offset = 412) float p_const_blueprint_base_albedo_blend; //@ default_value="0.699999988"
    layout(offset = 416) vec3 p_const_blueprint_base_albedo1; //@ default_value="0.100000001 0.239999995 0.125"
    layout(offset = 428) float p_const_edge_clamp_quat_to_half; //@ default_value="1000"
    layout(offset = 432) vec3 p_const_blueprint_edge_color; //@ default_value="0.117968217 0.253971577 0.134274334"
    layout(offset = 444) float p_const_blueprint_specular; //@ default_value="1"
    layout(offset = 448) float p_const_blueprint_edge_light_dark_range; //@ default_value="0.699999988"
    layout(offset = 452) float p_const_blueprint_edge_light_bright_range; //@ default_value="0.600000024"
    layout(offset = 456) float p_const_sky_island_shadow_density; //@ default_value="0.925000012"
    layout(offset = 460) float p_const_sky_island_shadow_offset_scale; //@ default_value="0.00899999961"
    layout(offset = 464) vec3 p_const_blueprint_emission_color; //@ default_value="0.00999999978 1 0.200000003"
    layout(offset = 476) float p_const_miasma_specular_scale; //@ default_value="0.75"
    layout(offset = 480) float p_const_blueprint_albedo_exposure_coef; //@ default_value="1"
    layout(offset = 484) float p_const_blueprint_albedo_emission_coef; //@ default_value="0.5"
    layout(offset = 488) float p_const_blueprint_albedo_emission_exposure_coef; //@ default_value="0.100000001"
    layout(offset = 492) float p_const_ao_sat_ssao; //@ default_value="2"
    layout(offset = 496) float p_dynamic_resolution_ratio; //@ default_value="1"
    layout(offset = 500) float p_const_cubemap_main_light_intensity; //@ default_value="1"
    layout(offset = 504) float p_const_cubemap_main_light_occlusion_intensity; //@ default_value="0"
    layout(offset = 508) float p_const_ao_sat_local_ao; //@ default_value="4"
    layout(offset = 512) float p_const_ao_cave_darkness; //@ default_value="1"
    layout(offset = 516) float p_cubemap_scatter_density; //@ default_value="1"
    layout(offset = 520) float p_const_edge_adjust_quat_normal; //@ default_value="20"
    layout(offset = 524) float p_const_water_prevent_nld_threshold; //@ default_value="0.0500000007"
    layout(offset = 528) float p_const_depth_shadow_chara_pcf; //@ default_value="1"
    layout(offset = 532) float p_const_depth_shadow_chara_blur_pcf; //@ default_value="2"
    layout(offset = 536) float p_const_depth_shadow_chara_blur_scale; //@ default_value="1"
    layout(offset = 540) float p_const_depth_shadow_reserved2; //@ default_value="0"
    layout(offset = 544) float p_const_depth_shadow_reserved3; //@ default_value="0"
    layout(offset = 548) float p_const_depth_shadow_reserved4; //@ default_value="0"
    layout(offset = 552) float p_const_depth_shadow_reserved5; //@ default_value="0"
    layout(offset = 556) float p_const_depth_shadow_reserved6; //@ default_value="0"
    layout(offset = 560) float p_dynamic_debug0; //@ default_value="0"
    layout(offset = 564) float p_dynamic_debug1; //@ default_value="0"
    layout(offset = 568) float p_dynamic_debug2; //@ default_value="0"
    layout(offset = 572) float p_dynamic_debug3; //@ default_value="0"
} SceneMat;

layout (binding = 2, std140) uniform _Mat //@ id="gsys_material" size="672" type="material"
{
    layout(offset = 0) mat2x4 p_tex_srt0; //@ default_value="1 -0 0 1 0 0 0 0"
    layout(offset = 32) mat2x4 p_tex_srt1; //@ default_value="1 -0 0 1 0 0 0 0"
    layout(offset = 64) mat2x4 p_tex_srt2; //@ default_value="1 -0 0 1 0 0 0 0"
    layout(offset = 96) mat2x4 p_tex_srt3; //@ default_value="100 -0 0 100 0 -99 0 0"
    layout(offset = 128) mat2x4 p_tex_srt4; //@ default_value="1 -0 0 1 0 0 0 0"
    layout(offset = 160) mat2x4 p_tex_srt5; //@ default_value="1 -0 0 1 0 0 0 0"
    layout(offset = 192) vec4 p_const_color0; //@ default_value="1 1 1 1"
    layout(offset = 208) vec4 p_const_color1; //@ default_value="1 1 1 1"
    layout(offset = 224) vec4 p_const_color2; //@ default_value="1 1 1 1"
    layout(offset = 240) vec4 p_const_color3; //@ default_value="1 1 1 1"
    layout(offset = 256) vec4 p_const_color4; //@ default_value="1 1 1 1"
    layout(offset = 272) vec4 p_const_color5; //@ default_value="1 1 1 1"
    layout(offset = 288) vec4 p_const_vector0; //@ default_value="0 0 0 0"
    layout(offset = 304) vec4 p_const_vector1; //@ default_value="0 0 0 0"
    layout(offset = 320) vec4 p_damage_color; //@ default_value="0 0 0 0"
    layout(offset = 336) vec4 p_debug_add_color; //@ default_value="0 0 0 0"
    layout(offset = 352) vec4 p_debug_mul_color; //@ default_value="1 1 1 1"
    layout(offset = 368) vec4 gsys_bake_st0; //@ default_value="0 0 0 0"
    layout(offset = 384) vec2 p_indirect_scale0; //@ default_value="1 1"
    layout(offset = 392) vec2 p_indirect_scale1; //@ default_value="1 1"
    layout(offset = 400) vec2 p_indirect_scale2; //@ default_value="1 1"
    layout(offset = 408) vec2 p_indirect_scale3; //@ default_value="1 1"
    layout(offset = 416) vec2 p_indirect_scale4; //@ default_value="1 1"
    layout(offset = 424) vec2 p_indirect_scale5; //@ default_value="1 1"
    layout(offset = 432) vec2 p_indirect_scale6; //@ default_value="1 1"
    layout(offset = 440) vec2 p_indirect_scale7; //@ default_value="1 1"
    layout(offset = 448) float p_user_data; //@ default_value="0"
    layout(offset = 452) float p_user_data1; //@ default_value="0"
    layout(offset = 456) float gsys_material_id; //@ default_value="0"
    layout(offset = 460) float p_attachment_effect; //@ default_value="0"
    layout(offset = 464) float p_attachment_noise_scale; //@ default_value="20"
    layout(offset = 468) float p_blue_print_alpha; //@ default_value="1"
    layout(offset = 472) float p_blue_print_alternative; //@ default_value="0"
    layout(offset = 476) float p_blue_print_alternative_emission; //@ default_value="0"
    layout(offset = 480) float p_blue_print_alternative_vanishing; //@ default_value="1"
    layout(offset = 484) float p_const_value0; //@ default_value="1"
    layout(offset = 488) float p_const_value1; //@ default_value="1"
    layout(offset = 492) float p_const_value2; //@ default_value="1"
    layout(offset = 496) float p_const_value3; //@ default_value="1"
    layout(offset = 500) float p_const_value4; //@ default_value="1"
    layout(offset = 504) float p_const_value5; //@ default_value="1"
    layout(offset = 508) float p_const_value6; //@ default_value="1"
    layout(offset = 512) float p_const_value7; //@ default_value="1"
    layout(offset = 516) float p_cook_ratio; //@ default_value="0"
    layout(offset = 520) float p_edit_proc_discard_scale; //@ default_value="1"
    layout(offset = 524) float p_enable_attachment; //@ default_value="0"
    layout(offset = 528) float p_figure; //@ default_value="0"
    layout(offset = 532) float p_frame_count_coef; //@ default_value="1"
    layout(offset = 536) float gsys_alpha_test_ref_value; //@ default_value="0.5"
    layout(offset = 540) float gsys_xlu_zprepass_alpha; //@ default_value="1"
    layout(offset = 544) float p_kari_chemical_ice_ratio0; //@ default_value="0"
    layout(offset = 548) float p_kari_mottled_ratio; //@ default_value="0"
    layout(offset = 552) float p_lumberjack_canopy_radius; //@ default_value="0.5"
    layout(offset = 556) float p_lumberjack_height_eps; //@ default_value="0.100000001"
    layout(offset = 560) float p_lumberjack_jaggy_width; //@ default_value="0.375"
    layout(offset = 564) float p_lumberjack_uv_scale; //@ default_value="1"
    layout(offset = 568) float p_miasma_max_local_y; //@ default_value="1.5"
    layout(offset = 572) float p_miasma_min_local_y; //@ default_value="0.5"
    layout(offset = 576) float p_miasma_ratio; //@ default_value="0"
    layout(offset = 580) float p_miasma_scale; //@ default_value="1.5"
    layout(offset = 584) float p_mimicly_ratio; //@ default_value="0"
    layout(offset = 588) float p_model_tree_scale; //@ default_value="1"
    layout(offset = 592) float p_mottled_scale; //@ default_value="1"
    layout(offset = 596) float p_object_attribute; //@ default_value="0"
    layout(offset = 600) float p_proc_discard; //@ default_value="1"
    layout(offset = 604) float p_proc_vanishing; //@ default_value="1"
    layout(offset = 608) float p_reverse_vanishing; //@ default_value="0"
    layout(offset = 612) float p_round_discard; //@ default_value="0"
    layout(offset = 616) float p_shadow_alpha; //@ default_value="1"
    layout(offset = 620) float p_special_effect_A; //@ default_value="0"
    layout(offset = 624) float p_texture_array_index0; //@ default_value="0"
    layout(offset = 628) float p_texture_array_index1; //@ default_value="0"
    layout(offset = 632) float p_texture_array_index2; //@ default_value="0"
    layout(offset = 636) float p_texture_array_index3; //@ default_value="0"
    layout(offset = 640) float p_texture_array_index4; //@ default_value="0"
    layout(offset = 644) float p_texture_array_index5; //@ default_value="0"
    layout(offset = 648) float p_texture_array_index6; //@ default_value="0"
    layout(offset = 652) float p_texture_array_index7; //@ default_value="0"
    layout(offset = 656) float p_wind_vtx_transform_intensity; //@ default_value="0.100000001"
    layout(offset = 660) float p_wind_vtx_transform_lie_height; //@ default_value="0.5"
    layout(offset = 664) vec2 p_wind_vtx_transform_lie_intensity; //@ default_value="3 0"
} Mat;

layout (binding = 7, std140) uniform _Instancing //@ id="gsys_user1" size="16384"
{
    vec4 data[1024];   // per-instance data; dynamic0 stores data[0].x
} Instancing;

layout (binding = 8, std140) uniform _UltraHandVoxelInfoUBO //@ id="gsys_user2" size="32"
{
    vec4 data[2];   // Ultrahand voxel grid: [0].xyz size (int), [1].x radius, [1].zw pixels per cell
} UltraHandVoxelInfoUBO;

#endif
