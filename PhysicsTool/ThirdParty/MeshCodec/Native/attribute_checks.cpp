// SPDX-License-Identifier: GPL-2.0-only
#include "mc_AttributeCodec.h"
#include <cstdio>

using namespace mc;

static u64 Position(u64 x, u64 y, u64 z) {
    return (x | y << 13 | z << 26) << 6;
}

int main() {
    constexpr u64 mask = ((u64(1) << 39) - 1) << 6;
    constexpr u64 surrounding = 0xbad000000000003full & ~mask;
    u64 output[] = { Position(7000, 6000, 5000), surrounding, surrounding };
    VertexStreamContext ctx{};
    ctx.outputBuffer = reinterpret_cast<u8*>(output);
    ctx.attrFlags[0] = (8u << 24) | (6u << 16) | (13u << 8) | 3;
    ctx.baseVertexIndex = 1;
    VertexDecodeGroup copies{ 1u << 16, 8 };
    DecodeBackrefs(ctx, 1, &copies, 1);
    if (output[1] != (output[0] | surrounding)) return 1;

    u8* streams[6]{};
    output[1] = surrounding;
    // Custom-width raw copies must preserve all 39 position bits.
    sAttributeDecodeFunctions[27](ctx, 1, &copies, 1, streams, 0);
    if (output[1] != (output[0] | surrounding)) return 2;

    output[0] = Position(100, 200, 300);
    output[1] = output[2] = surrounding;
    u32 references[] = { 8, 8 };
    u64 triangles[] = { 0, 0 };
    // Padding makes an incorrect six-element advance deterministic, not an OOB read.
    u16 deltas[] = { 1, 2, 3, 4, 5, 6, 99, 99, 99, 99, 99, 99 };
    ctx.vertexBufferTable = references; ctx.indexBufferTable = triangles;
    streams[2] = reinterpret_cast<u8*>(deltas);
    VertexDecodeGroup raw{ 2, 0 };
    sAttributeDecodeFunctions[97](ctx, 2, &raw, 1, streams, 3);
    if (output[1] != (Position(101, 202, 303) | surrounding)) return 3;
    if (output[2] != (Position(105, 207, 309) | surrounding)) return 4;
    std::puts("PASS: packed cave backreferences, 39-bit masks and consecutive delta triples.");
    return 0;
}
