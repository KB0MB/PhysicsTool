#pragma once

#include "mc_VertexDecompContext.h"

namespace mc {

enum class ElementType : u32 {
    U8 = 0,
    U16 = 1,
};

struct VertexStreamContext {
    u32 attrIndex;
    u64* indexBufferTable; // used to reference previously decoded triangles
    u32* vertexBufferTable; // used to reference previously decoded vertices
    u8* outputBuffer;
    DecompContext* decompContext;
    u32 attrFlags[15];
    u32 attrOffsets[15];
    u32 baseVertexIndex;
    u8 _padding[4];
    u32 vertexAlign;
    u32 attrCount;
    u32 totalVertexOutputSize;
    u32 vertexOutputSize;
    u32 maxAttrBitSize;
    u32 vertexBufferFlags[15];
    u8 localAttrOffsets[15];
};

struct AttrStreamInfo {
    ElementType elementType; // decoding table element type
    s32 tableCount; // decoding table count
    s32 elementCount; // decoding table element count (per table)
};

// returns the number of input streams
using GetStreamInfoFunc = s32 (*)(AttrStreamInfo* info, u32 maxStreams, const u8*& pos, s32 componentCount, s32 componentBitSize, s32 vertexCount);
using DecodeAttributeFunc = void (*)(VertexStreamContext& ctx, s32 vertexCount, VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining);

extern GetStreamInfoFunc sAttributeGetStreamInfoFunctions[0x71];
extern DecodeAttributeFunc sAttributeDecodeFunctions[0x71];

void DecodeBackrefs(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups);

} // namespace mc