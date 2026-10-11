#include "mc_IndexDecompressor.h"
#include <algorithm>
#include "mc_StackAllocator.h"
#include "mc_DecompContext.h"

#include "mc_Zstd.h"
#include "mc_IndexCodec.h"

namespace mc {

void IndexDecompressor::Initialize(u32, ZSTD_DCtx* dctx, StackAllocator* allocator) {
    mStackAllocator = allocator;
    mDCtx = dctx;
    mDecompContext = nullptr;
    mWorkBuffer0.addr = reinterpret_cast<u8*>(allocator->Alloc(0x60010, 8));
    mWorkBuffer0.offset = 0;
    mWorkBuffer0.capacity = 0x60000;
    mWorkBuffer0.size = 0x60000;
    mTrianglesRemaining = 0;
    mWorkBuffer1.addr = reinterpret_cast<u8*>(allocator->Alloc(0x20010, 8));
    mWorkBuffer1.offset = 0;
    mWorkBuffer1.capacity = 0x20000;
    mWorkBuffer1.size = 0x20000;
    mNumVertices = 0;
    mBaseIndex = 0;
    mInputStream0 = mWorkBuffer0.addr;
    mInputStream1 = mWorkBuffer1.addr;
}

void IndexDecompressor::Finalize() {
    mDCtx = nullptr;
    mStackAllocator->Free(mWorkBuffer1.addr);
    mStackAllocator->Free(mWorkBuffer0.addr);
}

// triangles?
u32 IndexDecompressor::Decompress1(void* dst, IndexFormat indexFormat, u32 count, u32 baseIndex, u64* tblBuf, u32 numCopied, u32 remaining) {
    u32 indexCount = (mNumVertices > numCopied + remaining) ? meshopt::decodeVByte(mDecompContext->currentPos) * 3 : count;
    u32 base = std::max(baseIndex, mBaseIndex);
    if (indexCount == 0) {
        mBaseIndex = base;
        return count - indexCount;
    }
    s32 totalTrigs = indexCount / 3;
    s32 trigs = totalTrigs - mTrianglesRemaining;
    if (trigs && totalTrigs >= mTrianglesRemaining) {
        u32 blockCount = ((trigs + 0x1ffff > -1) ? trigs + 0x1ffff : trigs + 0x3fffe) >> 0x11;
        mTrianglesRemaining = DecompressIndexStream(mDCtx, mDecompContext, mInputStream0, mWorkBuffer0, blockCount) - totalTrigs;
    } else {
        mTrianglesRemaining -= totalTrigs;
    }

    u32 compressedBlocks = mDecompContext->bitStream0.ReadZeroes();
    u32 indicesProcessed;
    if (compressedBlocks) {
        DecompressIndexStream(mDCtx, mDecompContext, mInputStream1, mWorkBuffer1, compressedBlocks);
    }

    // this might be an inline since type 2 does a similar thing and that's a single function (except the inner calls got inlined there instead)
    if (tblBuf) {
        u32 idxCount = baseIndex >= numCopied ? baseIndex - numCopied : 0;
        u32 copied = numCopied >= baseIndex ? numCopied - baseIndex : 0;

        indicesProcessed = DecodeIndexBuffer0_WithTable(dst, indexCount, base - baseIndex,
                                                        tblBuf + (static_cast<u64>(baseIndex) - static_cast<u64>(numCopied)), copied, remaining - idxCount,
                                                        mInputStream0, mInputStream1, indexFormat);
    } else {
        indicesProcessed = DecodeIndexBuffer0_WithoutTable(dst, indexCount, base - baseIndex, mInputStream0, mInputStream1, indexFormat);

    }

    mBaseIndex = baseIndex + indicesProcessed;
    return count - indexCount;
}

u32 IndexDecompressor::Decompress2(void* dst, IndexFormat indexFormat, u32 count, u32 baseIndex, u64* decodeBuf, u32 numCopied, u32 remaining) {
    s32 indexCount = (mNumVertices > numCopied + remaining) ? meshopt::decodeVByte(mDecompContext->currentPos) : count;
    u32 indicesProcessed = std::max(baseIndex, mBaseIndex);

    if (indexCount) {
        s32 triangles = mTrianglesRemaining;
        if (indexCount > triangles) {
            s32 size = indexCount - triangles + 0x1ffff;
            size = ((size > -1) ? size : indexCount - triangles + 0x3fffe) >> 0x11;

            triangles = DecompressIndexStream(mDCtx, mDecompContext, mInputStream0, mWorkBuffer0, size);
        }

        mTrianglesRemaining = triangles - indexCount;

        u32 compressedBlocks = mDecompContext->bitStream0.ReadZeroes();
        if (compressedBlocks) {
            DecompressIndexStream(mDCtx, mDecompContext, mInputStream1, mWorkBuffer1, compressedBlocks);
        }

        indicesProcessed = DecodeIndexBuffer2(dst, indexFormat, indexCount, baseIndex, decodeBuf, numCopied, numCopied, indicesProcessed, mInputStream0, mInputStream1);
    }

    mBaseIndex = indicesProcessed;
    return count - indexCount;
}

// points?
u32 IndexDecompressor::Decompress3(void* dst, IndexFormat indexFormat, u32 count, u32 baseIndex, u64* decodeBuf, u32 numCopied, u32 remaining) {
    s32 indexCount = (mNumVertices > numCopied + remaining) ? meshopt::decodeVByte(mDecompContext->currentPos) : count;
    u32 indicesProcessed = std::max(baseIndex, mBaseIndex);

    if (indexCount) {
        s32 triangles = mTrianglesRemaining;
        if (indexCount > triangles) {
            s32 size = indexCount - triangles + 0x1ffff;
            size = ((size > -1) ? size : indexCount - triangles + 0x3fffe) >> 0x11;

            triangles = DecompressIndexStream(mDCtx, mDecompContext, mInputStream0, mWorkBuffer0, size);
        }

        mTrianglesRemaining = triangles - indexCount;

        u32 compressedBlocks = mDecompContext->bitStream0.ReadZeroes();
        if (compressedBlocks) {
            DecompressIndexStream(mDCtx, mDecompContext, mInputStream1, mWorkBuffer1, compressedBlocks);
        }

        indicesProcessed = DecodeIndexBuffer3(dst, indexFormat, indexCount, baseIndex, compressedBlocks, decodeBuf, numCopied, indicesProcessed, mInputStream0, mInputStream1);
    }

    mBaseIndex = indicesProcessed;
    return count - indexCount;
}

} // namespace mc
