#pragma once

#include "mc_Types.h"

#include "mc_DecompContext.h"
#include "mc_IndexDecompressor.h"

#ifdef _MSC_VER
#pragma warning(push)
#pragma warning(disable:4996)
#elif defined(__clang__)
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
#elif defined(__GNUC__)
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wdeprecated-declarations"
#endif

#define ZSTD_STATIC_LINKING_ONLY
#include "zstd.h"
#include "decompress/zstd_decompress_block.h"

#include <cstring>

namespace mc {

void DecompressBlock(ZSTD_DCtx* dctx, void* dst, size_t dstSize, const void* src, size_t srcSize, u32 notCompressed);
void InsertUncompressedBlock(ZSTD_DCtx* dctx, void* dst, s32 size);
void InsertBlocks(ZSTD_DCtx* dctx, void* buf1, s32 bufSize1, void* buf2, s32 bufSize2);
// same as decompress block but returns the size
u32 ProcessBlock(ZSTD_DCtx* dctx, void* dst, size_t dstSize, const void* src, size_t srcSize, u32 notCompressed);
u32 DecompressIndexStream(ZSTD_DCtx* dctx, DecompContext* ctx, const u8*& outBuf, WorkBuffer& buffer, s32 numBlocks);
void DecompressVertexStream(ZSTD_DCtx* dctx, void* dst, s32 dstSize, DecompContext* ctx, s32 inputSize);

inline ZSTD_DCtx* SetupDCtx(void* wksp, size_t wkspSize) {
    ZSTD_DCtx* dctx = ZSTD_initStaticDCtx(wksp, wkspSize);
    ZSTD_decompressBegin(dctx);
    return dctx;
}

} // namespace mc

#ifdef _MSC_VER
#pragma warning(pop)
#elif defined(__clang__)
#pragma clang diagnostic pop
#elif defined(__GNUC__)
#pragma GCC diagnostic pop
#endif