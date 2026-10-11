#pragma once

#include "mc_Types.h"
#include "mc_BitStream.h"

namespace mc {

struct DecompContext;
class VertexDecompressor;
class StackAllocator;
struct VertexStreamContext;

// I'm reusing these same structs for the vertex table since there's also four streams there but the data in them is different
struct AllocationSet {
    void* vertexCountStream;
    void* backrefCountStream;
    void* backrefOffsetStream;
    void* bitStream;
};

struct VertexDecodingStreamSet {
    u8* vertexCountStream; // array of u8s -> if < 0x10, then represents the number of vertices, else an index into the vertex decoding table
    u8* backrefCountStream; // array of u8s -> if < 0x10, then represents the number of backreferences, else an index into the vertex decoding table
    u8* backrefOffsetStream;
    u8* bitStream;
};

struct VertexDecodingStreamSizes {
    s32 vertexCountStreamSize;
    s32 backrefCountStreamSize;
    s32 backrefOffsetStreamSize;
    s32 bitStreamSize;
};

struct VertexDecodeGroup {
    u32 vertexCount;
    u32 backRefOffset;

    u16 GetRawCount() const {
        return vertexCount & 0xffff;
    }

    u16 GetCopyCount() const {
        return vertexCount >> 0x10;
    }
};

struct VertexInfoTableInfo {
    u32 _00;
    u32 _04;
    u32 _08;
    u32 _0c;
    u32* _10;
};

struct VertexDecompContext {
    VertexDecodeGroup* groups;
    u32 numGroups;
    u32 stage; // 0 = reading group encoding info, 1 = decompressing group block, 2 = invalid
    u32 groupMask;
    VertexDecodingStreamSizes blockSizes;
    AllocationSet allocations;
    VertexDecodingStreamSet decodeStreams;
    BitStreamReader bitStream;
    VertexDecodeGroup currentGroup;

    // decompresses and/or decodes the input stream, after this, the stream needs to be parsed into vertex encoding info which is then decoded based on attribute format
    void ReadVertexBlockGroup(DecompContext& ctx, VertexDecompressor* decompressor, s32 attrCount);
    u32 ProcessVertexBlockGroup(VertexDecodeGroup** groups, u32* numGroups, DecompContext& ctx, VertexDecompressor* decompressor,
                                 VertexStreamContext& streamCtx, s32 vertexCount, StackAllocator* allocator);
    void FinishGroupProcessing(StackAllocator* allocator);
    void Reset(StackAllocator* allocator);
};

void ReadVertexInfoTableBlock(VertexDecodingStreamSet& streams, VertexDecodingStreamSizes& sizes, AllocationSet& allocations, u32 a4, DecompContext& ctx, VertexDecompressor* decompressor);
void DecodeVertexInfoTable(u32* tbl, s32 numVertices, VertexDecodingStreamSet& inputStreams, VertexDecodingStreamSizes& inputStreamSizes, u32 a5, VertexInfoTableInfo& a6, s32 baseVertex);
u32 Parse(VertexDecodeGroup* groups, s32 count, VertexDecodingStreamSet& inputStreams, BitStreamReader& bitStream, u32 stride, u32 a6, u32 format, u32 a8);

} // namespace mc