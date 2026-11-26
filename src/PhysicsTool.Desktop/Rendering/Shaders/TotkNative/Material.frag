#version 450 core
// TOTK material shader -- fragment shader; the pass (gsys_assign_type) picks the body
#include "material_options.h"
#include "Uniforms.glsl"
#include "Storage.glsl"
#include "Common.glsl"

#define VARYING in
#include "Varyings.glsl"
#include "Samplers.glsl"
#include "TexCoord.glsl"
#include "Surface.glsl"
#include "Discard.glsl"
#if gsys_assign_type == GSYS_ASSIGN_ZONLY
#include "PassZOnly.glsl"
#elif gsys_assign_type == GSYS_ASSIGN_GBUFFER
#include "PassGBuffer.glsl"
#elif gsys_assign_type == GSYS_ASSIGN_DYNAMIC1
#include "PassShadow.glsl"
#elif gsys_assign_type == GSYS_ASSIGN_DYNAMIC2
#include "PassObjectId.glsl"
#elif gsys_assign_type == GSYS_ASSIGN_DYNAMIC0
#include "PassDynamicForward.glsl"
#elif gsys_assign_type == GSYS_ASSIGN_DYNAMIC3
#include "PassDynamicGBuffer.glsl"
#else
#include "PassMaterial.glsl"
#endif
