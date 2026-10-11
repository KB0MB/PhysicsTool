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

#include "mc_Zstd.h"

#include "mc_IndexCodec.h"

namespace mc {

void DecompressBlock(ZSTD_DCtx* dctx, void* dst, size_t dstSize, const void* src, size_t srcSize, u32 notCompressed) {
    if (notCompressed != 0) {
        std::memcpy(dst, src, srcSize & 0xffffffff);
        ZSTD_insertBlock(dctx, dst, srcSize & 0xffffffff);
    } else {
        ZSTD_decompressBlock(dctx, dst, dstSize & 0xffffffff, src, srcSize & 0xffffffff);
    }
}

void InsertUncompressedBlock(ZSTD_DCtx* dctx, void* dst, s32 size) {
    if (size)
        ZSTD_insertBlock(dctx, dst, static_cast<size_t>(size));

    ZSTD_memcpy(dctx->entropy.rep, repStartValue, sizeof(repStartValue));
}

void InsertBlocks(ZSTD_DCtx* dctx, void* buf1, s32 bufSize1, void* buf2, s32 bufSize2) {
    if (bufSize1)
        ZSTD_insertBlock(dctx, buf1, static_cast<size_t>(bufSize1));

    if (bufSize2)
        ZSTD_insertBlock(dctx, buf2, static_cast<size_t>(bufSize2));

    ZSTD_memcpy(dctx->entropy.rep, repStartValue, sizeof(repStartValue));
}

u32 ProcessBlock(ZSTD_DCtx* dctx, void* dst, size_t dstSize, const void* src, size_t srcSize, u32 notCompressed) {
    if (notCompressed != 0) {
        std::memcpy(dst, src, srcSize & 0xffffffff);
        return ZSTD_insertBlock(dctx, dst, srcSize & 0xffffffff);
    } else {
        return ZSTD_decompressBlock(dctx, dst, dstSize & 0xffffffff, src, srcSize & 0xffffffff);
    }
}

u32 DecompressIndexStream(ZSTD_DCtx* dctx, DecompContext* ctx, const u8*& outBuf, WorkBuffer& buffer, s32 numBlocks) {
    u32 offset;
    u32 size;
    const u8* base;
    if (buffer.offset + numBlocks * 0x20000 > buffer.capacity) {
        buffer.size = buffer.offset;
        offset = 0;
        size = buffer.offset;
        base = buffer.addr;
    } else {
        size = buffer.size;
        offset = buffer.offset;
        base = outBuf;
    }

    InsertBlocks(dctx, buffer.addr + offset, size - offset, buffer.addr, offset);

    if (numBlocks) {
        for (s32 i = 0; i < numBlocks; ++i) {
            u32 inSize = meshopt::decodeVByte(ctx->currentPos);
            offset += ProcessBlock(dctx, buffer.addr + offset, 0x20000, ctx->currentPos, inSize, ctx->bitStream0.Read(1));
            ctx->currentPos += inSize;
        }
    }

    outBuf = base;
    buffer.offset = offset;
    return static_cast<u32>(offset + reinterpret_cast<uintptr_t>(buffer.addr) - reinterpret_cast<uintptr_t>(base));
}

void DecompressVertexStream(ZSTD_DCtx* dctx, void* dst, s32 dstSize, DecompContext* ctx, s32 maxBlockSize) {
    u32 isNotCompressed = ctx->bitStream0.Read(1);
    s32 dstCapacity = dstSize;

    if (dstSize > maxBlockSize) {
        while(dstCapacity > maxBlockSize) {
            if (isNotCompressed) {
                std::memcpy(dst, ctx->currentPos, maxBlockSize);
                ZSTD_insertBlock(dctx, dst, maxBlockSize);
                ctx->currentPos += maxBlockSize;
                dstCapacity -= maxBlockSize;
                dst = reinterpret_cast<u8*>(dst) + maxBlockSize;
            } else {
                u32 blockSize = meshopt::decodeVByte(ctx->currentPos);
                ZSTD_decompressBlock(dctx, dst, maxBlockSize, ctx->currentPos, blockSize);
                ctx->currentPos += blockSize;
                dstCapacity -= maxBlockSize;
                dst = reinterpret_cast<u8*>(dst) + maxBlockSize;
            }
            isNotCompressed = ctx->bitStream0.Read(1);
        }
    }

    if (isNotCompressed) {
        std::memcpy(dst, ctx->currentPos, dstCapacity);
        ZSTD_insertBlock(dctx, dst, dstCapacity);
        ctx->currentPos += dstCapacity;
    } else {
        u32 blockSize = meshopt::decodeVByte(ctx->currentPos);
        ZSTD_decompressBlock(dctx, dst, dstCapacity, ctx->currentPos, blockSize);
        ctx->currentPos += blockSize;
    }
}

} // namespace mc

#ifdef _MSC_VER
#pragma warning(pop)
#elif defined(__clang__)
#pragma clang diagnostic pop
#elif defined(__GNUC__)
#pragma GCC diagnostic pop
#endif