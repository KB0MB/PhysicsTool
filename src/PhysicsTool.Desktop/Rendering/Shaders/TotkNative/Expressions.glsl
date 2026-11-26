// Baked expression graphs (TOTK replaces BOTW's calc colours with per-material node graphs compiled into the program).
// o_expression_pre_normal / o_expression_post_normal pick a graph by hash; material sources 200.. read its outputs.
// Graphs are decoded one by one from the programs that use them.
#ifndef TOTK_EXPRESSIONS_GLSL
#define TOTK_EXPRESSIONS_GLSL

// inputs a graph can read besides textures and Mat constants (set by main before the material is evaluated)
float ExprRim;      // sat(1 + V.N) with the mapped normal (post-normal graphs only)

vec4 ExpressionPreOutput(int n)
{
#if o_expression_pre_normal == 2197096204
    // out0 = texture1.g (belt buckle metal mask)
    if (n == 0) return TexSample(1).yyyy;
#endif
#if o_expression_pre_normal == 2583175971 || o_expression_pre_normal == 2679514330 || o_expression_pre_normal == 763730240
    // out0 = albedo pulled towards p_const_color1 by p_const_value1 where texture4 is dark (skin tone mask)
    if (n == 0)
    {
        vec3 base = TexSample(0).rgb - Mat.p_const_color1.rgb;
        return vec4(fma(base, fma(TexSample(4).rgb, vec3(Mat.p_const_value1), vec3(-Mat.p_const_value1)), base) + Mat.p_const_color1.rgb, 1.0);
    }
#endif
#if o_expression_pre_normal == 2207006048
    // eye: out0 = texture0 * texture3 (iris colour), out1 = both highlight discs (proctexture 4 / 5) * p_const_value4
    if (n == 0) return TexSample(0) * TexSample(3);
    if (n == 1) return vec4(fma(TexSample(4).x, Mat.p_const_value4, TexSample(5).x * Mat.p_const_value4));
#endif
    return vec4(0.0);
}

vec4 ExpressionPostOutput(int n)
{
#if o_expression_post_normal == 3965060848
    // Link's upper body: skin tone (as 2583175971, mask lifted by texture6.a) under a texture6 layer;
    // out1 = glowing markings: texture7 plus a rim-facing boost of its green, times p_const_value7
    if (n == 0)
    {
        vec4 layer = TexSample(6);
        vec3 base = TexSample(0).rgb - Mat.p_const_color1.rgb;
        vec3 skin = fma(base, fma(Saturate(TexSample(4).rgb + layer.w), vec3(Mat.p_const_value1), vec3(-Mat.p_const_value1)), base) + Mat.p_const_color1.rgb;
        return vec4(mix(skin, layer.rgb, layer.w), 1.0);
    }
    if (n == 1)
    {
        vec3 mark = TexSample(7).rgb;
        float boost = mark.g * Saturate((ExprRim - Mat.p_const_value6) * 30.0);
        return vec4(fma(vec3(boost), vec3(Mat.p_const_value5), mark) * Mat.p_const_value7, 1.0);
    }
#endif
    return vec4(0.0);
}

// material sources 200.. read the graph's outputs
vec4 ExpressionOutput(int n)
{
#if o_expression_pre_normal != 0
    return ExpressionPreOutput(n);
#else
    return ExpressionPostOutput(n);
#endif
}

#endif
