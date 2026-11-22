#ifndef UKING_COMMON_GLSL
#define UKING_COMMON_GLSL

// row-vector * matrix stored as rows (mat3x4)
vec3 MulRows(mat3x4 m, vec3 p) { return vec4(p, 1.0) * m; }
vec3 MulRowsDir(mat3x4 m, vec3 d) { return vec4(d, 0.0) * m; }

float Saturate(float x) { return clamp(x, 0.0, 1.0); }
vec3 Saturate(vec3 x) { return clamp(x, 0.0, 1.0); }
vec4 Saturate(vec4 x) { return clamp(x, 0.0, 1.0); }

float Pow5(float x) { float x2 = x * x; return x2 * x2 * x; }

// acos() approximation used by the sky inscatter lookup (Abramowitz-Stegun)
float AcosApprox(float x)
{
    return fma(x, fma(x, fma(x, -0.0187292993, 0.0742609948), -0.212114394), 1.57072878) * sqrt(1.0 - x);
}

// cube map lookup convention used by the game (z flipped)
vec3 CubeDir(vec3 worldDir) { return vec3(worldDir.x, worldDir.y, -worldDir.z); }

float Luminance(vec3 c) { return dot(c, vec3(0.298900008, 0.586600006, 0.114399999)); }

// calc-color operation (uking_colorN_calc_type, uking_vtx_position_modify_calc_type)
vec4 CalcColorOp(int type, vec4 A, vec4 B, vec4 C, vec4 D)
{
    switch (type)
    {
        case 0:  return A;
        case 1:  return A + B;
        case 2:  return A * B;
        case 4:  return mix(A, B, C);
        case 5:  return (A + B) * C;
        case 6:  return A * B + C * D;
        case 7:  return pow(A, B);
        case 19: return (A + B + C) * D;
        case 27: return mix(A * D, B, C);
        case 29: return sin((A * B + C) * 6.28318548) * D;
        case 30: return cos((A * B + C) * 6.28318548) * D;
        case 8:  return A * B + C;
        case 12: return mix(A, B, C) * D;
        case 21: return (A * B + C) * D;
        case 9:  return A * B * C;
        case 10: return B + A;
        case 11: return (B * A) * C * D;
        case 13: return fma(A, vec4(0.498039186), vec4(0.501960754));   // encode a [-1,1] normal to [0,1]
        case 14: return fma(A, vec4(2.00787401), vec4(-1.00787401));     // decode a [0,1] normal to [-1,1]
        case 15: return fma(B - A, C, A) + D;                  // mix(A, B, C) + D
        case 16: return fma(A + B, C, D);                      // (A + B) * C + D
        case 17: return -A + B + C + D;
        case 18: return (B + A) * C * D;
        case 28: return pow(A, B) * C + D;
        // normal-map blends on signed (channel 2000) inputs; z is rebuilt from xy
        case 25: return vec4(normalize(mix(vec3(A.xy, sqrt(1.0 - clamp(dot(A.xy, A.xy), 0.0, 1.0))),
                                           vec3(B.xy, sqrt(1.0 - clamp(dot(B.xy, B.xy), 0.0, 1.0))), C.x)), 1.0);   // normal lerp
        case 26: return vec4(fma(B.xy, C.xx, A.xy), sqrt(1.0 - clamp(dot(A.xy, A.xy), 0.0, 1.0)), 1.0);              // detail add (z of A)
    }
    return A;
}

#endif
