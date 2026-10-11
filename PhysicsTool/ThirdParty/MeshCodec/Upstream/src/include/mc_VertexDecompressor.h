#pragma once

#include "mc_AttributeCodec.h"

#include "zstd.h"

namespace mc {

struct DecodingContext;
class StackAllocator;

class VertexDecompressor {
public:
    VertexDecompressor() = default;

    void Initialize(u32, ZSTD_DCtx*, StackAllocator*);
    void Finalize();

    void* ProcessBlock(u8*& dst, ElementType componentType, s32 a3, s32 size, u32 a5, DecompContext& ctx);

private:
    ZSTD_DCtx* mDCtx;
    u8* mBuffer;
    u32 mOffset;
    u32 mBufferSize;
    DecodingContext* mDecodingContext;
    StackAllocator* mStackAllocator;
};



} // namespace mc