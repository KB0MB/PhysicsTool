#include "mc_MeshCodec.h"
#include <algorithm>
#include <memory>

#include "mc_Zstd.h"

namespace mc {

// nn::util::BinaryFileHeader
struct BinaryFileHeader {
    u64 magic;
    u8 verMicro;
    u8 verMinor;
    u16 verMajor;
    u16 bom;
    u8 align;
    u32 filenameOffset;
    u16 isRelocated;
    u16 firstBlockOffset;
    u32 relocationTableOffset;
    u32 fileSize;
};

// lazy hack
static inline bool HasFMSHSection(const void* ptr) {
    return (*(reinterpret_cast<const u8*>(ptr) + 0xee) >> 3) & 1;
}

size_t GetFrameSize(const ResCompressionHeader* header) {
    return header->sizeInfo.streamOffset.get() + header->sizeInfo.endOffset.get();
}

u32 DecompressFMSH(void* dst, size_t dstSize [[maybe_unused]], const void* src, size_t srcSize, void* workBuffer) {
    const ResMeshCodecHeader* header = reinterpret_cast<const ResMeshCodecHeader*>(src);

    StreamContext indexContext{
        .stream = reinterpret_cast<u8*>(dst),
        .size = header->indexOutputSize,
        .alignment = header->indexAlign,
    };
    StreamContext vertexContext{
        .stream = reinterpret_cast<u8*>((reinterpret_cast<uintptr_t>(dst) + header->vertexAlign + indexContext.size - 1) & -header->vertexAlign),
        .size = header->vertexOutputSize,
        .alignment = header->vertexAlign,
    };
    
    StackAllocator::InitArg initArg{
        .indexStream = &indexContext,
        .vertexStream = &vertexContext,
        .workMemory = workBuffer,
        .workMemorySize = header->workMemSize,
    };

    StackAllocator* allocator;
    s32 result = CreateStackAllocator(&allocator, initArg, &header->compHeader, 8);
    s32 blockSize = result;

    if (result > -1) {
        u32 offset = 0x22;
        const u8* pos = reinterpret_cast<u8*>(reinterpret_cast<uintptr_t>(src) + 0x22);
        
        while (result > -1) {
            if (blockSize == 0) {
                return offset != srcSize ? 0x1c : 0;
            }
            
            offset += blockSize;
            result = allocator->DecompressFrame(pos, blockSize);
            pos += blockSize;
            blockSize = result;
        }
    }

    return ConvertResult(static_cast<u64>(result));
}

bool DecompressMC(void* dst, size_t dstSize, const void* src, size_t srcSize, void* workBuffer, size_t workBufferSize) {
    // PhysicsTool bounds checks around the upstream package decoder (GPL-2.0).
    if (srcSize < sizeof(ResMeshCodecPackageHeader) || src == nullptr || dst == nullptr)
        return false;

    auto header = reinterpret_cast<const ResMeshCodecPackageHeader*>(src);

    if (header->magic != ResMeshCodecPackageHeader::cMagic)
        return false;

    if (header->versionMajor != 0 || header->versionMinor > 1)
        return false;

    const u64 decompressedSize = u64(header->flags >> 5) << (header->flags & 15);

    if (decompressedSize < sizeof(BinaryFileHeader) || dstSize < decompressedSize)
        return false;

    const std::unique_ptr<ZSTD_DCtx, decltype(&ZSTD_freeDCtx)> context(ZSTD_createDCtx(), ZSTD_freeDCtx);
    ZSTD_DCtx* dctx = context.get();
    if (!dctx || ZSTD_isError(ZSTD_DCtx_setParameter(dctx, ZSTD_d_experimentalParam1, 1)) ||
        ZSTD_isError(ZSTD_decompressBegin(dctx)))
        return false;
    size_t size = 1;
    size_t result = 0;
    size_t remaining = srcSize - sizeof(ResMeshCodecPackageHeader);
    size_t remainingOutput = decompressedSize;
    const u8* ptr = reinterpret_cast<const u8*>(src) + sizeof(ResMeshCodecPackageHeader);
    u8* output = reinterpret_cast<u8*>(dst);
    do {
        if (size > remaining)
            return false;
        result = ZSTD_decompressContinue(dctx, output, remainingOutput, ptr, size);
        if (ZSTD_isError(result) || result > remainingOutput)
            return false;
        ptr += size;
        remaining -= size;
        size = ZSTD_nextSrcSizeToDecompress(dctx);
        output += result;
        remainingOutput -= result;
    } while (size != 0);
    const size_t decodedBytes = decompressedSize - remainingOutput;
    if (decodedBytes < sizeof(BinaryFileHeader) || std::memcmp(dst, "FRES", 4) != 0)
        return false;
    if (decodedBytes <= 0xee || !HasFMSHSection(dst))
        return decodedBytes == decompressedSize;

    auto fileHeader = reinterpret_cast<const BinaryFileHeader*>(dst);

    if (fileHeader->fileSize < 0xef || fileHeader->fileSize > decodedBytes)
        return false;
    const size_t padding = (4 - (reinterpret_cast<uintptr_t>(ptr) & 3)) & 3;
    if (padding > remaining || remaining - padding < sizeof(ResMeshCodecHeader))
        return false;
    auto fmshHeader = reinterpret_cast<const ResMeshCodecHeader*>(ptr + padding);

    if (fmshHeader->magic != ResMeshCodecHeader::cMagic)
        return false;
    
    // for whatever reason, with some files, ZSTD_decompressContinue writes way past the end of the buffer (even though it returns the correct size)
    // unsure if this is because Nintendo made changes to the zstd implementation, but we can fix this with a hack by memsetting everything
    // past the end of the bfres file data to 0 - the game itself doesn't do this but it appears to work fine
    // it might also be ZSTD versions (totk uses 1.3.7 or something) but that version doesn't have ZSTD_DCtx's definition in a header file
    // so it'd require some edits to use
    std::memset(reinterpret_cast<u8*>(dst) + fileHeader->fileSize, 0, dstSize - fileHeader->fileSize);
    
    const u32 align = std::max(fmshHeader->vertexAlign, fmshHeader->indexAlign);
    const auto validAlignment = [](u32 value) { return value != 0 && (value & (value - 1)) == 0; };
    if (!validAlignment(fmshHeader->vertexAlign) || !validAlignment(fmshHeader->indexAlign))
        return false;
    const size_t sizeHeaderOffset = (size_t(fileHeader->fileSize) + 7) & ~size_t(7);
    if (sizeHeaderOffset > dstSize || dstSize - sizeHeaderOffset < 0x120 + align - 1)
        return false;
    output = Align(Align(reinterpret_cast<u8*>(dst) + fileHeader->fileSize, 8) + 0x120, align);
    const size_t meshOffset = output - reinterpret_cast<u8*>(dst);
    const size_t vertexOffset = (size_t(fmshHeader->indexOutputSize) + fmshHeader->vertexAlign - 1) &
        ~size_t(fmshHeader->vertexAlign - 1);
    if (meshOffset > dstSize || vertexOffset > dstSize - meshOffset ||
        fmshHeader->vertexOutputSize > dstSize - meshOffset - vertexOffset)
        return false;
    u32* sizeHeader = reinterpret_cast<u32*>(Align(reinterpret_cast<u8*>(dst) + fileHeader->fileSize, 8));
    sizeHeader[0] = reinterpret_cast<uintptr_t>(output) - reinterpret_cast<uintptr_t>(dst);
    sizeHeader[1] = decompressedSize;

    if (workBuffer == nullptr || workBufferSize < fmshHeader->workMemSize)
        return false;
    
    const size_t compressedSize = remaining - static_cast<size_t>(reinterpret_cast<const u8*>(fmshHeader) - ptr);
    if (DecompressFMSH(output, fmshHeader->vertexOutputSize + fmshHeader->indexOutputSize, fmshHeader, compressedSize, workBuffer)) // 0 == success
        return false;

    return true;
}

bool DecompressChunk(void* dst, size_t dstSize, const void* src, size_t srcSize, void* workBuffer, size_t workBufferSize) {
    if (srcSize < 0x1c)
        return false;
    
    auto header = reinterpret_cast<const ResChunkHeader*>(src);

    const size_t decompressedSize = header->decompressedSize;

    if (dstSize < decompressedSize)
        return false;
    
    if (workBufferSize < header->workMemSize)
        return false;

    StreamContext indexContext{
        .stream = reinterpret_cast<u8*>(dst) + header->vertexOutputSize,
        .size = header->indexOutputSize,
        .alignment = 2,
    };

    StreamContext vertexContext{
        .stream = reinterpret_cast<u8*>(dst),
        .size = header->vertexOutputSize,
        .alignment = 4,
    };

    StackAllocator::InitArg initArg{
        .indexStream = &indexContext,
        .vertexStream = &vertexContext,
        .workMemory = workBuffer,
        .workMemorySize = header->workMemSize,
    };

    StackAllocator* allocator;
    s32 result = CreateStackAllocator(&allocator, initArg, &header->compHeader, 8);
    s32 blockSize = result;

    if (result > -1) {
        u32 offset = 0x1c;
        const u8* pos = reinterpret_cast<u8*>(reinterpret_cast<uintptr_t>(src) + 0x1c);
        
        while (result > -1) {
            if (blockSize == 0) {
                return offset == srcSize;
            }
            
            offset += blockSize;
            result = allocator->DecompressFrame(pos, blockSize);
            pos += blockSize;
            blockSize = result;
        }
    }

    return ConvertResult(static_cast<u64>(result)) == 0;
}

bool DecompressQuad(void* dst, size_t dstSize, const void* src, size_t srcSize, void* workBuffer, size_t workBufferSize) {
    if (srcSize < 0x4)
        return false;
    
    // first 4 bytes is the crbin id
    const void* frameHeader = reinterpret_cast<const void*>(reinterpret_cast<uintptr_t>(src) + 4);

    if (workBufferSize < sizeof(ZSTD_DCtx))
        return false;

    const size_t decompressedSize = ZSTD_getFrameContentSize(frameHeader, srcSize - 4);

    if (dstSize < decompressedSize)
        return false;

    ZSTD_DCtx* dctx = ZSTD_initStaticDCtx(workBuffer, workBufferSize);
    const size_t result = ZSTD_decompressDCtx(dctx, dst, dstSize, frameHeader, srcSize - 4);

    return !ZSTD_isError(result);
}

} // namespace mc
