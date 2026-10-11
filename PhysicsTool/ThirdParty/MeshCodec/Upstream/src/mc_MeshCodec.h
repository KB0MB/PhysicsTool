#pragma once

#include "include/mc_StackAllocator.h"

namespace mc {

size_t GetFrameSize(const ResCompressionHeader* header);

// the following is for .bfres.mc files

struct ResMeshCodecHeader {
    u32 magic; // FMSH
    u32 version;
    u32 workMemSize;
    u32 _0c;
    u32 indexOutputSize;
    u32 vertexOutputSize;
    u8 indexAlign;
    u8 vertexAlign;
    ResCompressionHeader compHeader;

    static constexpr u32 cMagic = 0x48534d46;
};

struct ResMeshCodecPackageHeader {
    u32 magic; // MCPK
    u8 versionMicro;
    u8 versionMinor;
    u16 versionMajor;
    u32 flags;

    u32 GetDecompressedSize() const {
        return (flags >> 5) << (flags & 0xf);
    }

    static constexpr u32 cMagic = 0x4b50434d;
};

// 0x7100da3958 on 1.2.1
// src is a pointer to header struct above, decompresses just the vertex + index buffers
u32 DecompressFMSH(void* dst, size_t dstSize [[maybe_unused]], const void* src, size_t srcSize, void* workBuffer);
// this decompresses a full .bfres.mc file
bool DecompressMC(void* dst, size_t dstSize, const void* src, size_t srcSize, void* workBuffer, size_t workBufferSize);

// the following is for .chunk files (note that cave page files can be compressed either using ZStd or MeshCodec which is defined in the .crbin file)
// in the base game, all .chunk files are MC-compressed while all .quad files are ZStd-compressed

struct ResChunkHeader {
    u32 chunkHash;
    u32 vertexOutputSize;
    u32 indexOutputSize;
    u32 decompressedSize;
    u32 workMemSize;
    ResCompressionHeader compHeader;
};

bool DecompressChunk(void* dst, size_t dstSize, const void* src, size_t srcSize, void* workBuffer, size_t workBufferSize);
bool DecompressQuad(void* dst, size_t dstSize, const void* src, size_t srcSize, void* workBuffer, size_t workBufferSize);

} // namespace mc