//------------ Shader generated code for blender (make_blender_meta.py) -------------

//-------------------------- macros --------------------------
#define BLENDER_RENDER 1
#define gsys_assign_type 1
#define gsys_assign_variation 0
#define gsys_weight 0
#define system_id 0
#define uking_enable_hide_normal_pass 0
#define uking_reverse_polygon_offset 0
#define uking_edit_expand_depthshadow_far 0
#define uking_enable_map_rendering 0
#define uking_map_layer_id 0
#define uking_sampler_use_array 0
#define uking_texture0_texcoord 0
#define uking_texture0_decode 0
#define uking_enable_proctexture0 0
#define uking_enable_indirect0 0
#define uking_texture0_post 0
#define uking_proctexture0_type 0
#define uking_proctexture0_param_A 0
#define uking_proctexture0_param_B 0
#define uking_indirect0_texcoord 0
#define uking_enable_indirect0_world_scaling 0
#define uking_indirect0_signed_fetch 1
#define uking_indirect0_type 0
#define uking_texture_array_texture0 -1
#define uking_texture1_texcoord 0
#define uking_texture1_decode 0
#define uking_enable_proctexture1 0
#define uking_enable_indirect1 0
#define uking_texture1_post 0
#define uking_proctexture1_type 0
#define uking_proctexture1_param_A 0
#define uking_proctexture1_param_B 0
#define uking_indirect1_texcoord 0
#define uking_enable_indirect1_world_scaling 0
#define uking_indirect1_signed_fetch 1
#define uking_indirect1_type 0
#define uking_texture_array_texture1 -1
#define uking_texture2_texcoord 0
#define uking_texture2_decode 0
#define uking_enable_proctexture2 0
#define uking_enable_indirect2 0
#define uking_texture2_post 0
#define uking_proctexture2_type 0
#define uking_proctexture2_param_A 0
#define uking_proctexture2_param_B 0
#define uking_indirect2_texcoord 0
#define uking_enable_indirect2_world_scaling 0
#define uking_indirect2_signed_fetch 1
#define uking_indirect2_type 0
#define uking_texture_array_texture2 -1
#define uking_texture3_texcoord 0
#define uking_texture3_decode 0
#define uking_enable_proctexture3 0
#define uking_enable_indirect3 0
#define uking_texture3_post 0
#define uking_proctexture3_type 0
#define uking_proctexture3_param_A 0
#define uking_proctexture3_param_B 0
#define uking_indirect3_texcoord 0
#define uking_enable_indirect3_world_scaling 0
#define uking_indirect3_signed_fetch 1
#define uking_indirect3_type 0
#define uking_texture_array_texture3 -1
#define uking_texture4_texcoord 0
#define uking_texture4_decode 0
#define uking_enable_proctexture4 0
#define uking_enable_indirect4 0
#define uking_texture4_post 0
#define uking_proctexture4_type 0
#define uking_proctexture4_param_A 0
#define uking_proctexture4_param_B 0
#define uking_indirect4_texcoord 0
#define uking_enable_indirect4_world_scaling 0
#define uking_indirect4_signed_fetch 1
#define uking_indirect4_type 0
#define uking_texture_array_texture4 -1
#define uking_texture5_texcoord 0
#define uking_texture5_decode 0
#define uking_enable_proctexture5 0
#define uking_enable_indirect5 0
#define uking_texture5_post 0
#define uking_proctexture5_type 0
#define uking_proctexture5_param_A 0
#define uking_proctexture5_param_B 0
#define uking_indirect5_texcoord 0
#define uking_enable_indirect5_world_scaling 0
#define uking_indirect5_signed_fetch 1
#define uking_indirect5_type 0
#define uking_texture_array_texture5 -1
#define uking_texture6_texture 0
#define uking_texture6_texcoord 0
#define uking_texture6_decode 0
#define uking_texture7_texture 0
#define uking_texture7_texcoord 0
#define uking_texture7_decode 0
#define gsys_enable_color_buffer 0
#define uking_texture_scene_color_post 0
#define uking_texture_scene_color0_texcoord 0
#define uking_enable_scene_color0_fog 0
#define uking_enable_scene_color0_edge_stop 0
#define uking_enable_scene_color0_edge_stop_magic_N 1
#define uking_texture_scene_color1_texcoord 0
#define uking_enable_scene_color1_fog 0
#define uking_enable_depth_buffer 0
#define uking_texture_scene_depth_post 0
#define uking_texture_scene_depth0_texcoord 0
#define uking_texture_scene_depth1_texcoord 0
#define uking_enable_scene_color0_depthdiff1_effect 0
#define uking_depth_diff_scale_to_scene_color0 1
#define uking_enable_calc_color0 0
#define uking_color0_calc_type 0
#define uking_color0_clamp01 0
#define uking_color0_before_normal 0
#define uking_color0_A 0
#define uking_color0_A_channel 0
#define uking_color0_B 0
#define uking_color0_B_channel 0
#define uking_color0_C 0
#define uking_color0_C_channel 0
#define uking_color0_D 0
#define uking_color0_D_channel 0
#define uking_enable_calc_color1 0
#define uking_color1_calc_type 0
#define uking_color1_clamp01 0
#define uking_color1_before_normal 0
#define uking_color1_A 0
#define uking_color1_A_channel 0
#define uking_color1_B 0
#define uking_color1_B_channel 0
#define uking_color1_C 0
#define uking_color1_C_channel 0
#define uking_color1_D 0
#define uking_color1_D_channel 0
#define uking_enable_calc_color2 0
#define uking_color2_calc_type 0
#define uking_color2_clamp01 0
#define uking_color2_before_normal 0
#define uking_color2_A 0
#define uking_color2_A_channel 0
#define uking_color2_B 0
#define uking_color2_B_channel 0
#define uking_color2_C 0
#define uking_color2_C_channel 0
#define uking_color2_D 0
#define uking_color2_D_channel 0
#define uking_enable_calc_color3 0
#define uking_color3_calc_type 0
#define uking_color3_clamp01 0
#define uking_color3_before_normal 0
#define uking_color3_A 0
#define uking_color3_A_channel 0
#define uking_color3_B 0
#define uking_color3_B_channel 0
#define uking_color3_C 0
#define uking_color3_C_channel 0
#define uking_color3_D 0
#define uking_color3_D_channel 0
#define uking_enable_calc_color4 0
#define uking_color4_calc_type 0
#define uking_color4_clamp01 0
#define uking_color4_before_normal 0
#define uking_color4_A 0
#define uking_color4_A_channel 0
#define uking_color4_B 0
#define uking_color4_B_channel 0
#define uking_color4_C 0
#define uking_color4_C_channel 0
#define uking_color4_D 0
#define uking_color4_D_channel 0
#define uking_enable_calc_color5 0
#define uking_color5_calc_type 0
#define uking_color5_clamp01 0
#define uking_color5_before_normal 0
#define uking_color5_A 0
#define uking_color5_A_channel 0
#define uking_color5_B 0
#define uking_color5_B_channel 0
#define uking_color5_C 0
#define uking_color5_C_channel 0
#define uking_color5_D 0
#define uking_color5_D_channel 0
#define uking_enable_calc_color6 0
#define uking_color6_calc_type 0
#define uking_color6_clamp01 0
#define uking_color6_before_normal 0
#define uking_color6_A 0
#define uking_color6_A_channel 0
#define uking_color6_B 0
#define uking_color6_B_channel 0
#define uking_color6_C 0
#define uking_color6_C_channel 0
#define uking_color6_D 0
#define uking_color6_D_channel 0
#define uking_enable_calc_color7 0
#define uking_color7_calc_type 0
#define uking_color7_clamp01 0
#define uking_color7_before_normal 0
#define uking_color7_A 0
#define uking_color7_A_channel 0
#define uking_color7_B 0
#define uking_color7_B_channel 0
#define uking_color7_C 0
#define uking_color7_C_channel 0
#define uking_color7_D 0
#define uking_color7_D_channel 0
#define uking_enable_calc_color8 0
#define uking_color8_calc_type 0
#define uking_color8_clamp01 0
#define uking_color8_before_normal 0
#define uking_color8_A 0
#define uking_color8_A_channel 0
#define uking_color8_B 0
#define uking_color8_B_channel 0
#define uking_color8_C 0
#define uking_color8_C_channel 0
#define uking_color8_D 0
#define uking_color8_D_channel 0
#define uking_enable_calc_color9 0
#define uking_color9_calc_type 0
#define uking_color9_clamp01 0
#define uking_color9_before_normal 0
#define uking_color9_A 0
#define uking_color9_A_channel 0
#define uking_color9_B 0
#define uking_color9_B_channel 0
#define uking_color9_C 0
#define uking_color9_C_channel 0
#define uking_color9_D 0
#define uking_color9_D_channel 0
#define uking_enable_calc_color10 0
#define uking_color10_calc_type 0
#define uking_color10_clamp01 0
#define uking_color10_before_normal 0
#define uking_color10_A 0
#define uking_color10_A_channel 0
#define uking_color10_B 0
#define uking_color10_B_channel 0
#define uking_color10_C 0
#define uking_color10_C_channel 0
#define uking_color10_D 0
#define uking_color10_D_channel 0
#define uking_enable_calc_color11 0
#define uking_color11_calc_type 0
#define uking_color11_clamp01 0
#define uking_color11_before_normal 0
#define uking_color11_A 0
#define uking_color11_A_channel 0
#define uking_color11_B 0
#define uking_color11_B_channel 0
#define uking_color11_C 0
#define uking_color11_C_channel 0
#define uking_color11_D 0
#define uking_color11_D_channel 0
#define uking_enable_calc_color12 0
#define uking_color12_calc_type 0
#define uking_color12_clamp01 0
#define uking_color12_before_normal 0
#define uking_color12_A 0
#define uking_color12_A_channel 0
#define uking_color12_B 0
#define uking_color12_B_channel 0
#define uking_color12_C 0
#define uking_color12_C_channel 0
#define uking_color12_D 0
#define uking_color12_D_channel 0
#define uking_enable_calc_color13 0
#define uking_color13_calc_type 0
#define uking_color13_clamp01 0
#define uking_color13_before_normal 0
#define uking_color13_A 0
#define uking_color13_A_channel 0
#define uking_color13_B 0
#define uking_color13_B_channel 0
#define uking_color13_C 0
#define uking_color13_C_channel 0
#define uking_color13_D 0
#define uking_color13_D_channel 0
#define uking_enable_replace_albedo 0
#define uking_replace_albedo_color 0
#define uking_enable_emission_cubemap 0
#define uking_emission_color_cubemap 0
#define gsys_gbuffer_after_fx 0
#define uking_enable_fresnel_cheat 1
#define uking_enable_output_object_attribute 1
#define uking_pass_object_attribute 0
#define uking_edit_disable_actor_id 0
#define uking_inner_material 0
#define gsys_deferred_shading_material 1
#define uking_vtx_calc_type 0
#define uking_vtx_texture0 0
#define uking_vtx_texture0_texcoord 0
#define uking_modify_position_type 0
#define uking_vtx_position_modify_calc_type 0
#define uking_vtx_position_modify_shadow_only 0
#define uking_vtx_position_modify_clamp 0
#define uking_vtx_position_modify_vector 100
#define uking_vtx_position_modify_A 100
#define uking_vtx_position_modify_A_channel 0
#define uking_vtx_position_modify_B 100
#define uking_vtx_position_modify_B_channel 0
#define uking_vtx_position_modify_C 100
#define uking_vtx_position_modify_C_channel 0
#define uking_vtx_position_modify_D 100
#define uking_vtx_position_modify_D_channel 0
#define uking_modify_normal_type 0
#define uking_vtx_normal_modify_A 100
#define uking_enable_wind_vtx_transform 0
#define uking_wind_vtx_transform_channel 10
#define uking_enable_wind_vtx_transform_coreinfo 0
#define uking_enable_wind_vtx_transform_normal_lock 0
#define uking_enable_wind_vtx_transform_lie 0
#define uking_enable_wind_vtx_transform_height 0
#define uking_edit_enable_wind_vtx_transform_scale 0
#define uking_enable_lumberjack 0
#define uking_enable_lumberjack_trunk 0
#define uking_enable_lumberjack_canopy 0
#define uking_lumberjack_annual_ring_albedo_color 0
#define uking_lumberjack_side_texcoord 0
#define uking_enable_inner_light 0
#define uking_inner_light_mode 0
#define uking_inner_light_source_position 100
#define uking_inner_light_source_radius 0
#define uking_inner_light_diffusivity 0
#define uking_enable_inner_light_line 0
#define uking_inner_light_source_position_end 100
#define uking_inner_light_source_radius_end 0
#define uking_force_export_normal 0
#define uking_force_export_localpos 0
#define uking_force_export_viewpos 0
#define uking_force_export_vtx_color 0
#define gsys_dynamic_force_zprepass 0
#define gsys_gbuffer_xlu 0
#define gsys_force_zprepass 0
#define uking_enable_gbuffer_xlu_blend 1
#define gsys_gbuffer_xlu_opa 0
#define enable_bake_texture 0
#define uking_map_color 0
#define uking_map_enable_fragment_normal 0
#define uking_enable_texcoord0 1
#define uking_texcoord0_mapping 0
#define uking_texcoord0_srt -1
#define uking_texcoord0_offset_srt -1
#define uking_enable_texcoord1 0
#define uking_texcoord1_mapping 0
#define uking_texcoord1_srt -1
#define uking_texcoord1_offset_srt -1
#define uking_enable_texcoord2 0
#define uking_texcoord2_mapping 0
#define uking_texcoord2_srt -1
#define uking_texcoord2_offset_srt -1
#define uking_enable_texcoord3 0
#define uking_texcoord3_mapping 0
#define uking_texcoord3_srt -1
#define uking_texcoord3_offset_srt -1
#define uking_enable_texcoord4 0
#define uking_texcoord4_mapping 0
#define uking_texcoord4_srt -1
#define uking_texcoord4_offset_srt -1
#define uking_enable_texcoord5 0
#define uking_texcoord5_mapping 0
#define uking_texcoord5_srt -1
#define uking_texcoord5_offset_srt -1
#define uking_texcoord_toon_spec_mapping 0
#define uking_texcoord_toon_spec_srt -1
#define uking_texcoord_toon_spec_offset_srt -1
#define uking_enable_proctexcoord0 0
#define uking_proctexcoord0_mapping 0
#define uking_enable_proctexcoord1 0
#define uking_proctexcoord1_mapping 0
#define uking_enable_proctexcoord2 0
#define uking_proctexcoord2_mapping 0
#define uking_common_texcoord 0
#define uking_enable_normalmap 1
#define uking_normal_color 2
#define uking_enable_decode_normalmap 1
#define uking_enable_normalmap2 0
#define uking_enable_backface_modify 0
#define uking_enable_calc_binormal_vertex 0
#define uking_normal_color2 3
#define uking_normalmap_blend_ratio 104
#define uking_normalmap_blend_channel 0
#define uking_normalmap_blend_method 0
#define uking_material_attribute 0
#define uking_material_behave 0
#define uking_chara_size 2
#define uking_metal_color 402
#define uking_metal_channel 0
#define uking_albedo_color 0
#define uking_specmask_scaler 0
#define uking_specular_channel 0
#define uking_specular_hair 0
#define uking_specular_hair_channel 0
#define uking_enable_emission 0
#define uking_emission_color 0
#define uking_enable_transmission 0
#define uking_transmission_color 0
#define uking_transmission_channel 0
#define uking_enable_ao 0
#define uking_ao_color 0
#define uking_ao_channel 10
#define uking_grossy_color 402
#define uking_grossy_channel 0
#define uking_grossy_intensity 2
#define uking_transparency_color 0
#define uking_transparency_channel 0
#define uking_albedo_old 0
#define bake_shadow_type -1
#define bake_light_type -1
#define gsys_renderstate 0
#define gsys_pass 0
#define gsys_alpha_test_func 0
#define gsys_alpha_test_enable 0

//---------------------- uniform blocks ----------------------
struct Context
{
    mat3x4 cView;
    mat4 cViewProj;
    mat4 cProj;
    mat3x4 cViewInv;
    vec4 cNearFar;
    vec4 cScreen;
    vec4 cDist;
    vec4 cFovParams;
    vec4 cf;
    vec4 cc;
    vec4 sf;
    mat3x4 cPrevView;
    mat4 cPrevViewProj;
    mat4 cPrevProj;
    mat3x4 cPrevViewInv;
    mat3x4 cProjectionTexMtx0;
    mat3x4 cProjectionTexMtx1;
    vec4 cProjParams;
    mat4 cCascadeMtx[4];
    vec4 cShadowParam1;
    vec4 cShadowParam2;
};

struct ShpMtx
{
    mat3x4 cTransform;
    vec4 cShapeData3;
    vec4 cShapeData4;
    vec4 cShapeData5;
    vec4 cShapeParam;
};

struct Mat
{
    float uking_wind_vtx_transform_intensity;
    float uking_wind_vtx_transform_lie_intensity;
    float uking_wind_vtx_transform_lie_height;
    float uking_lumberjack_height_eps;
    float uking_lumberjack_canopy_radius;
    float uking_lumberjack_uv_scale;
    float _pad0;
    float _pad1;
    vec4 gsys_bake_st0;
    vec4 gsys_bake_st1;
    vec4 gsys_bake_light_scale;
    mat2x4 tex_srt0;
    mat2x4 tex_srt1;
    mat2x4 tex_srt2;
    mat2x4 tex_srt3;
    mat2x4 tex_srt4;
    mat2x4 tex_srt5;
    vec2 indirect_scale0;
    vec2 texture_array_index0;
    vec2 indirect_scale1;
    vec2 texture_array_index1;
    vec2 indirect_scale2;
    vec2 texture_array_index2;
    vec2 indirect_scale3;
    vec2 texture_array_index3;
    vec2 indirect_scale4;
    vec2 texture_array_index4;
    vec2 indirect_scale5;
    vec2 texture_array_index5;
    vec4 const_color0;
    vec4 const_color1;
    vec4 const_color2;
    vec4 const_color3;
    vec4 const_color4;
    vec4 const_color5;
    vec4 const_vector0;
    vec4 const_vector1;
    float const_value0;
    float const_value1;
    float const_value2;
    float const_value3;
    float const_value4;
    float const_value5;
    float const_value6;
    float const_value7;
    float uk_kari_mottled_ratio;
    float uk_mottled_scale;
    float uk_cook_ratio;
    float uk_kari_chemical_ice_ratio0;
    vec4 uk_damage_color;
    float uk_user_data;
    float uk_user_data1;
    float uk_object_attribute;
    float uk_special_effect_A;
    float uk_special_effect_B;
    float uk_special_effect_C;
    float uk_model_tree_scale;
    float uk_mimicly_ratio;
    float gsys_xlu_zprepass_alpha;
    float uk_bounding_radius;
    float uk_shadow_alpha;
    float uk_proc_discard;
    float uking_edit_proc_discard_scale;
    float gsys_alpha_test_ref_value;
    float _pad2;
    float _pad3;
};

struct _Env
{
    vec4 cAmbientColor;
    vec4 cHemiSkyColor;
    vec4 cHemiGroundColor;
    vec4 cHemiDirection;
    vec4 cLightDirection0;
    vec4 cLightColor;
    vec4 cLightSpecColor;
    vec4 cLightDirection1;
    vec4 cLightColor1;
    vec4 cLightSpecColor1;
    vec4 Fog0_Color;
    vec4 Fog0_Direction;
    vec4 Fog0_Param;
    vec4 Fog1_Color;
    vec4 Fog1_Direction;
    vec4 Fog1_Param;
    vec4 Fog2_Color;
    vec4 Fog2_Direction;
    vec4 Fog2_Param;
    vec4 Fog3_Color;
    vec4 Fog3_Direction;
    vec4 Fog3_Param;
    vec4 LightDirection0World;
    vec4 HemDirection0World;
    vec4 LightDirection1World;
    vec4 data25;
    vec4 cSkyFogDistance;
    vec4 cSkyInscatterParam;
    vec4 cSkyHeightParam;
    vec4 cSkyParam;
    vec4 cSkyColor;
    vec4 data31;
    vec4 cWindParam;
    vec4 cWindParam2;
    vec4 cWindLieMapST;
    vec4 data35;
    vec4 data36;
    vec4 data37;
    vec4 data38;
    vec4 cSkyOcclusionParam;
    vec4 data40;
    vec4 data41;
    vec4 cWorldShadowWeights;
    mat3x4 cViewToWorldPos;
    vec4 data46;
    vec4 data47;
    vec4 data48;
    vec4 data49;
    vec4 data50;
    vec4 data51;
    vec4 cFogHeightBase;
    vec4 cShadowFade;
};

struct SceneMat
{
    vec4 uking_dynamic_toon_light_adjust_for_demo;
    float uking_dynamic_cloud_ratio;
    float uking_dynamic_depth_shadow_off;
    float uking_dynamic_proj_shadow_off;
    float uking_dynamic_rain_ratio;
    float uking_dynamic_rainfall;
    float uking_dynamic_exposure;
    float uking_dynamic_world_shadow_off;
    float uking_dynamic_base_light_change_ratio;
    float uking_dynamic_main_rendering_light;
    float uking_dynamic_skyocclusion_off;
    float uking_dynamic_depth_shadow_scale;
    float uking_dynamic_item_filter_alpha;
    float uking_dynamic_ui_highlight;
    float uking_const_proc_discard_scale1;
    float uking_const_proc_discard_scale2;
    float uking_const_proc_discard_scale3;
    float uking_dynamic_debug0;
    float uking_dynamic_debug1;
    float uking_dynamic_debug2;
    float uking_dynamic_debug3;
};

