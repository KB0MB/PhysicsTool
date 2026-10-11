#pragma once

#include "mc_Types.h"
#include "mc_IndexStreamContext.h"

#include "zstd.h"

namespace mc {

struct DecompContext;
class StackAllocator;

struct WorkBuffer {
    u8* addr;
    u32 offset;
    u32 capacity;
    u32 size;
};

class IndexDecompressor {
public:
    IndexDecompressor() = default;

    void Initialize(u32, ZSTD_DCtx*, StackAllocator*);
    void Finalize();

    void SetContext(DecompContext* ctx) {
        mDecompContext = ctx;
    }

    void SetVertexCount(u32 count) {
        mNumVertices = count;
    }

    void SetBaseIndex(u32 idx) {
        mBaseIndex = idx;
    }

    u32 Decompress1(void* dst, IndexFormat indexFormat, u32 count, u32 baseIndex, u64* decodeBuf, u32 numCopied, u32 remaining);
    u32 Decompress2(void* dst, IndexFormat indexFormat, u32 count, u32 baseIndex, u64* decodeBuf, u32 numCopied, u32 remaining);
    u32 Decompress3(void* dst, IndexFormat indexFormat, u32 count, u32 baseIndex, u64* decodeBuf, u32 numCopied, u32 remaining);

private:
    ZSTD_DCtx* mDCtx;
    DecompContext* mDecompContext;
    WorkBuffer mWorkBuffer0;
    WorkBuffer mWorkBuffer1;
    const u8* mInputStream0;
    const u8* mInputStream1;
    s32 mTrianglesRemaining;
    u32 _54;
    u32 mNumVertices;
    u32 mBaseIndex;
    StackAllocator* mStackAllocator;
};

using DecompressIndexFunc = u32 (*)(IndexDecompressor* decompressor, void* dst, IndexFormat indexFormat, u32 count, u32 baseIndex, u64* decodeBuf, u32 numCopied, u32 remaining);

inline u32 DecompressIndexStream1(IndexDecompressor* decompressor, void* dst, IndexFormat indexFormat, u32 count, u32 baseIndex, u64* decodeBuf, u32 numCopied, u32 remaining) {
    return decompressor->Decompress1(dst, indexFormat, count, baseIndex, decodeBuf, numCopied, remaining);
}
inline u32 DecompressIndexStream2(IndexDecompressor* decompressor, void* dst, IndexFormat indexFormat, u32 count, u32 baseIndex, u64* decodeBuf, u32 numCopied, u32 remaining) {
    return decompressor->Decompress2(dst, indexFormat, count, baseIndex, decodeBuf, numCopied, remaining);
}
inline u32 DecompressIndexStream3(IndexDecompressor* decompressor, void* dst, IndexFormat indexFormat, u32 count, u32 baseIndex, u64* decodeBuf, u32 numCopied, u32 remaining) {
    return decompressor->Decompress3(dst, indexFormat, count, baseIndex, decodeBuf, numCopied, remaining);
}

} // namespace mc