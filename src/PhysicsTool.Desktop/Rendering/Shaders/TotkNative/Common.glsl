// Shared helpers and names for the Context / Env rows (the material archive has no member tables for these two blocks:
// rows are named here from GSYSENV.bt, the AGL shader archive's uniform names, and what the programs do with them).
#ifndef TOTK_COMMON_GLSL
#define TOTK_COMMON_GLSL

// ---------------- gsys_context (members in Uniforms.glsl; matrices are stored as rows, m[i] = row i) ----------------
#define cView0          Context.cView[0]      // world -> view, rows 0..2
#define cView1          Context.cView[1]
#define cView2          Context.cView[2]
#define cUpView         vec3(Context.cView[0].y, Context.cView[1].y, Context.cView[2].y)   // world up in view space
#define cProj0          Context.cProj[0]      // view -> clip, rows 0..3
#define cProj1          Context.cProj[1]
#define cProj2          Context.cProj[2]
#define cProj3          Context.cProj[3]
#define cInvView0       Context.cViewInv[0]   // view -> world, rows 0..2
#define cInvView1       Context.cViewInv[1]
#define cInvView2       Context.cViewInv[2]
#define cNearFarParam   Context.cNearFar      // linear depth = (viewZ + x) * -cNearFarParam2.x
#define cNearFarParam2  Context.cNearFarParam2
#define cFovParams      Context.cFovParams    // .y: projection scale (1 / (-viewZ * y) = size of a pixel)
#define cTime           Context.cTime         // .x: time (vanishing patterns), .y: frame counter (miasma scroll)
#define cProjTexMtx0    Context.cProjectionTexMtx0[0]   // world -> projected shadow texture (cTex_Projection0), rows 0..2
#define cProjTexMtx1    Context.cProjectionTexMtx0[1]
#define cProjTexMtx2    Context.cProjectionTexMtx0[2]
#define cShadowRow(c, r) Context.cCascadeMtx[c][r]       // cascade c: view -> shadow map rows x, y, z, w
#define cCascadeSplit   Context.cShadowParam1  // .xyz: view depth where cascades 1, 2, 3 start

// ---------------- gsys_environment ----------------
// Members are generated into Uniforms.glsl from this table (#define eName Env.Name // row N: ...).
// Rows 0-24: layout from GSYSENV.bt; [names] from AglShader.sharcb; the rest named as decoded from the programs.
#define eAmbientColor        Env.AmbientColor        // row 0
#define eHemiSkyColor        Env.HemiSkyColor        // row 1: [cLightHemiSkyColor]
#define eHemiGroundColor     Env.HemiGroundColor     // row 2
#define eHemiDirection       Env.HemiDirection       // row 3: view space
#define eLightDir            Env.LightDirection0     // row 4: [cLightDir0] .xyz: view-space direction the main light travels
#define eLightColor          Env.LightColor0         // row 5: [cLightColor0] .rgb: main (toon) light colour
#define eLightSpecColor      Env.LightSpecColor0     // row 6
#define eLightDir1           Env.LightDirection1     // row 7
#define eLightColor1         Env.LightColor1         // row 8
#define eLightSpecColor1     Env.LightSpecColor1     // row 9
#define eFog0Color           Env.Fog0Color           // row 10: fog[0] colour, w = density
#define eFog0Dir             Env.Fog0Dir             // row 11: fog[0] dir, w
#define eFog0Param           Env.Fog0Param           // row 12: fog[0] start / end / damp
#define eFog1Color           Env.Fog1Color           // row 13
#define eFog1Dir             Env.Fog1Dir             // row 14
#define eFog1Param           Env.Fog1Param           // row 15
#define eFogA_Color          Env.Fog2Color           // row 16: fog[2] (distance fog A): rgb, w = density
#define eFogA_Start          Env.Fog2Dir             // row 17: fog[2] dir; .w used as the distance offset
#define eFogA_Param          Env.Fog2Param           // row 18: fog[2] .x: distance scale, .y: exponent
#define eFogB_Color          Env.Fog3Color           // row 19: fog[3] (volume-mask fog B): rgb, w = density
#define eFogB_Start          Env.Fog3Dir             // row 20: fog[3] dir; .w used as the distance offset
#define eFogB_Param          Env.Fog3Param           // row 21: fog[3] .x: distance scale, .y: exponent
#define eHemiDirWorld        Env.HemiDirectionWorld  // row 22: world space
#define eLightDirWorld       Env.LightDirection0World // row 23: [cLightDir0World] (.xz scrolls the sky island shadow)
#define eDir2                Env.Dir2                // row 24
#define eInscatterDist       Env.InscatterDist       // row 26: .x: distance scale, .y: offset (GSYSENV.bt: vec[1])
#define eInscatterParam      Env.InscatterParam      // row 27: .x/.y: exponents, .z: strength, .w: v floor (bt: vec[2])
#define eHazeDist            Env.HazeDist            // row 28: haze distance ramp: sat(dist * x - y) (bt: vec[3])
#define eHazeParam           Env.HazeParam           // row 29: .x distance exponent, .z/.w horizon exponent / strength (bt: vec[4])
#define eHazeColor           Env.HazeColor           // row 30: rgb, w = strength (bt: vec[5])
#define eWorldShadowDir      Env.WorldShadowWeight   // row 43: [cEnvWorldShadowWeight] .xyz: weights of the world shadow height texel
#define eMinusFieldFadeY     Env.EnvironmentMaskParam0 // row 44: [cEnvironmentMaskParam0] Depths: height fades (.xy / .zw = scale, offset)
#define eMinusFieldDarkUV    Env.EnvironmentMaskParam1 // row 45: [cEnvironmentMaskParam1] Depths darkness texture: uv = world.xz * xy + zw
#define eMinusFieldDark      Env.EnvironmentMaskParam2 // row 46: [cEnvironmentMaskParam2] Depths darkness: .x floor, .y/.z texture range
#define eMinusFieldTint      Env.MinusFieldTint      // row 47: .xy: height ramp, .z: tint at full ramp
#define eCascadeFade         Env.DepthShadowFade0    // row 48: [cEnvDepthShadowFade0] .xy: fade of cascades 0..2, .zw: fade of cascade 3
#define eWorldShadowFade     Env.DepthShadowFade1    // row 49: [cEnvDepthShadowFade1] .xy: distance fade of the world shadow
#define eShadowBias          Env.ShadowBias          // row 50: .x: normal offset scale, .y: depth bias
#define eShadowOffsetField   Env.DepthShadowOffsetField // row 51: [cEnvDepthShadowOffsetField]
#define eCascadeLayout       Env.DepthShadowCascadeInfo // row 52: [cEnvDepthShadowCascadeInfo] ints: .x layers per lod, .y cascades per lod
#define eShadowTexel         Env.DepthShadowPcf      // row 56: [cEnvDepthShadowPcf] .xy: cascade texel size
#define eShadowNormal        Env.DepthShadowNormalCoeff // row 57: [cEnvDepthShadowNormalCoeff] .x/.y: weight of shading / vertex normal, .z: exponent
#define eSkyFogParam0        Env.SkyFogParam0        // row 64: [cSkyFogParam0]
#define eSkyFogParam1        Env.SkyFogParam1        // row 65: [cSkyFogParam1]
#define eSkyFogParam2        Env.SkyFogParam2        // row 66: [cSkyFogParam2]
#define eGroundFog           Env.GroundFog           // row 68: rgb, w = density
#define eGroundFogY          Env.GroundFogY          // row 69: height ramp: sat(x + (z - worldY) * y)
#define eUnknown70           Env.Unknown70           // row 70: emission dim (1 - pow(0, z) * w); meaning unknown
#define eAmbientTint         Env.CaveLightColor      // row 81: [cCaveLightColor] rgb tint applied by the volume mask (.w)
#define eMinusFieldRect      Env.MinusFieldRect      // row 82: Depths light map: uv = (world.xz - xy) * z

// SceneShadingInfo storage buffer (uint words, read as float)
#define SceneShadingF(i) uintBitsToFloat(SceneShadingInfo.data[i])

float Saturate(float x) { return clamp(x, 0.0, 1.0); }
vec3  Saturate(vec3 x)  { return clamp(x, 0.0, 1.0); }
float Pow(float x, float e) { return exp2(log2(x) * e); }   // the game's pow (NaN / inf exactly like the GPU)
float Luminance(vec3 c) { return dot(c, vec3(0.298900008, 0.586600006, 0.114399999)); }

// acos approximation used by the sky inscatter lookup (Abramowitz-Stegun 4.4.45)
float FastAcos(float x)
{
    return fma(x, fma(x, fma(x, -0.0187292993, 0.0742609948), -0.212114394), 1.57072878) * sqrt(1.0 - x);
}

// integer division / remainder of the GPU (divide by zero gives -1)
int DivU(int a, int b) { return b == 0 ? -1 : int(uint(a) / uint(b)); }
int RemU(int a, int b) { return b == 0 ? -1 : a - DivU(a, b) * b; }
int DivS(int a, int b) { return b == 0 ? -1 : a / b; }

// row-vector helpers for the matrices stored as rows
float Row3(vec4 r, vec3 p) { return dot(r.xyz, p) + r.w; }

#endif
