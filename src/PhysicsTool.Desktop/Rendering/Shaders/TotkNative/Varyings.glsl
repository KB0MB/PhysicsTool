// Vertex -> fragment interface. Locations are packed in this order, skipping the ones a program does not use.
#ifndef TOTK_VARYINGS_GLSL
#define TOTK_VARYINGS_GLSL

#if (o_enable_texcoord2 || o_enable_texcoord3)
#define HAS_TEXCOORD23 1
#else
#define HAS_TEXCOORD23 0
#endif
#if (o_enable_texcoord4 || o_enable_texcoord5)
#define HAS_TEXCOORD45 1
#else
#define HAS_TEXCOORD45 0
#endif
// the eye (behave 104) has no hair / toon spec pattern
#if o_texcoord_toon_spec_srt >= 0 && o_material_behave != 104
#define HAS_TOON_SPEC_COORD 1
#else
#define HAS_TOON_SPEC_COORD 0
#endif
// skin / eye (behave >= 100) keep one location free after the tangent. Unconfirmed: the Link programs where it appears
// also use position modify (skin) or 3 texcoord slots (eye), so the condition may be one of those instead.
#if o_material_behave >= 100
#define HAS_RESERVED 1
#else
#define HAS_RESERVED 0
#endif

// Which varyings each pass allocates a location for (ALLOC_*) and which it actually passes (USE_*).
// Locations are packed in a fixed order over the allocated ones; a pass may allocate a slot it does not fill.
#if gsys_assign_type == GSYS_ASSIGN_ZONLY || gsys_assign_type == GSYS_ASSIGN_DYNAMIC1
  // depth pre-pass / shadow depth: texcoords only for the alpha test
  #define ALLOC_TEXCOORD gsys_alpha_test_enable
  #define ALLOC_SURFACE  0          // normal, tangent (+ reserved slot), toon spec coordinate
  #define ALLOC_VIEW_POS 0
  #if gsys_assign_type == GSYS_ASSIGN_DYNAMIC1
    #define ALLOC_SCREEN gsys_alpha_test_enable
    #define ALLOC_LOCAL  1          // discard effects
  #else
    #define ALLOC_SCREEN 0
    #define ALLOC_LOCAL  0
  #endif
  #define ALLOC_AMBIENT  0          // ambient + shading info
  #define USE_VIEW_POS   0
#elif gsys_assign_type == GSYS_ASSIGN_DYNAMIC2
  // object id: no varyings
  #define ALLOC_TEXCOORD 0
  #define ALLOC_SURFACE  0
  #define ALLOC_VIEW_POS 0
  #define ALLOC_SCREEN   0
  #define ALLOC_LOCAL    0
  #define ALLOC_AMBIENT  0
  #define USE_VIEW_POS   0
#elif gsys_assign_type == GSYS_ASSIGN_GBUFFER
  #define ALLOC_TEXCOORD 1
  #define ALLOC_SURFACE  1
  #define ALLOC_VIEW_POS 1
  #define ALLOC_SCREEN   0
  #define ALLOC_LOCAL    0
  #define ALLOC_AMBIENT  0
  #define USE_VIEW_POS   (o_expression_post_normal != 0)   // only post-normal graphs read the view vector
#elif gsys_assign_type == GSYS_ASSIGN_DYNAMIC0 || gsys_assign_type == GSYS_ASSIGN_DYNAMIC3
  #define ALLOC_TEXCOORD 1
  #define ALLOC_SURFACE  1
  #define ALLOC_VIEW_POS 1
  #define ALLOC_SCREEN   1
  #define ALLOC_LOCAL    1
  // dynamic0's fragment shader reads the ambient slot, but the game's vertex shader never writes it (we write 0)
  #define ALLOC_AMBIENT  (gsys_assign_type == GSYS_ASSIGN_DYNAMIC0)
  #define USE_VIEW_POS   1
  #define USE_SHADING    0
#else
  #define ALLOC_TEXCOORD 1
  #define ALLOC_SURFACE  1
  #define ALLOC_VIEW_POS 1
  #define ALLOC_SCREEN   1
  #define ALLOC_LOCAL    1
  #define ALLOC_AMBIENT  1
  #define USE_VIEW_POS   1
#endif
#define USE_TEXCOORD   ALLOC_TEXCOORD
#define USE_SURFACE    ALLOC_SURFACE
#define USE_SCREEN     ALLOC_SCREEN
#define USE_LOCAL      ALLOC_LOCAL
#define USE_AMBIENT    ALLOC_AMBIENT
#ifndef USE_SHADING
#define USE_SHADING    ALLOC_AMBIENT
#endif

#define LOC_TEXCOORD01  0
#define LOC_TEXCOORD23  ALLOC_TEXCOORD
#define LOC_TEXCOORD45  (ALLOC_TEXCOORD * (1 + HAS_TEXCOORD23))
#define LOC_NORMAL      (ALLOC_TEXCOORD * (1 + HAS_TEXCOORD23 + HAS_TEXCOORD45))
#define LOC_TANGENT     (LOC_NORMAL + 1)
#define LOC_VIEW_POS    (LOC_NORMAL + ALLOC_SURFACE * (2 + HAS_RESERVED))
#define LOC_TOON_SPEC   (LOC_VIEW_POS + ALLOC_VIEW_POS)
#define LOC_SCREEN      (LOC_TOON_SPEC + ALLOC_SURFACE * HAS_TOON_SPEC_COORD)
#define LOC_LOCAL       (LOC_SCREEN + ALLOC_SCREEN)
#define LOC_AMBIENT     (LOC_LOCAL + ALLOC_LOCAL)
#define LOC_SHADING     (LOC_AMBIENT + 1)

#if USE_TEXCOORD
layout (location = LOC_TEXCOORD01) VARYING vec4 fTexCoord01;                // texcoord 0 (xy), 1 (zw)
#if HAS_TEXCOORD23
layout (location = LOC_TEXCOORD23) VARYING vec4 fTexCoord23;   // texcoord 2 (xy), 3 (zw)
#endif
#if HAS_TEXCOORD45
layout (location = LOC_TEXCOORD45) VARYING vec4 fTexCoord45;   // texcoord 4 (xy), 5 (zw)
#endif
#endif
#if USE_SURFACE
layout (location = LOC_NORMAL) VARYING vec4 fViewNormal;       // .xyz view-space vertex normal
layout (location = LOC_TANGENT) VARYING vec4 fViewTangent;     // .xyz view-space tangent, .w bitangent sign
#if HAS_TOON_SPEC_COORD
layout (location = LOC_TOON_SPEC) VARYING vec4 fToonSpecCoord; // .xy hair / toon specular pattern coordinate
#endif
#endif
#if USE_VIEW_POS
layout (location = LOC_VIEW_POS) VARYING vec4 fViewPosition;   // .xyz view-space position
#endif
#if USE_SCREEN
layout (location = LOC_SCREEN) VARYING vec4 fScreenPos;        // .xy / .w = screen uv (y down)
#endif
#if USE_LOCAL
layout (location = LOC_LOCAL) VARYING vec4 fLocalPos;          // .xyz object-space position (procedural noise), .w instance (dynamic0)
#endif
#if USE_AMBIENT
layout (location = LOC_AMBIENT) VARYING vec4 fAmbient;         // .rgb ambient (SceneShadingInfo gradient over screen y), .w shadow strength
#endif
#if USE_SHADING
layout (location = LOC_SHADING) VARYING vec4 fShadingParam;    // .x SceneShadingInfo[20] (inscatter mask)
#endif
#endif
