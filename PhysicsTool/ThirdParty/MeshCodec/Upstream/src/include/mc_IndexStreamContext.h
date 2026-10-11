#pragma once

#include "mc_Types.h"

#include "mc_StreamContext.h"

namespace mc {

struct DecompContext;
class IndexDecompressor;
class StackAllocator;

enum class IndexFormat : u32 {
    U32 = 0,
    U16 = 1,
    Invalid = 3,
};

enum class EncodingType : u32 {
    _00, // triangle list?
    _01, // triangle strip?
    _02, // 
    _03, // point list?
    Invalid = 4,
};

struct IndexStreamContext {
    IndexFormat indexFormat;
    EncodingType encodingType;
    u32 blockCount;
    u32 rawCount;
    u32 baseIndex;
    u32 indicesRemaining;
    u32 blocksRemaining;
    u32 streamCount;
    StreamContext streamContext;
    u32 indexOffset;

    bool ParseIndexHeader(DecompContext& ctx);
    void Decompress(IndexDecompressor& decompressor, DecompContext& ctx, u32 numVertices, u64* decodeBuf, u32 verticesCopied, u32 copyCount, StackAllocator* allocator);
};

} // namespace mc