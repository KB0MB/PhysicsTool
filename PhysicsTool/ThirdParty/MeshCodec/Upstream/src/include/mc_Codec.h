#pragma once

#include "mc_Types.h"

#include "mc_IndexDecompressor.h"
#include "mc_IndexStreamContext.h"
#include "mc_StreamContext.h"
#include "mc_VertexDecompContext.h"
#include "mc_VertexDecompressor.h"

#include "zstd.h"

namespace mc {

class StackAllocator;

class CodecBase {
public:
    virtual ~CodecBase() = default;

    virtual void Initialize(const StreamContext* indexStream, const StreamContext* vertexStream, u32, StackAllocator* allocator) = 0;
    virtual void Decompress(DecompContext&) = 0;
    virtual void Finalize() = 0;
};

class NullCodec : public CodecBase {
public:
    NullCodec() = default;

    ~NullCodec() override = default;

    void Initialize(const StreamContext* indexStream, const StreamContext* vertexStream, u32, StackAllocator* allocator) override;
    void Decompress(DecompContext&) override;
    void Finalize() override;

private:
    u8* mVertexOutputBuffer;
    u8* mIndexOutputBuffer;
    u32 mRemainingVertexSize;
    u32 mRemainingIndexSize;
    StackAllocator* mStackAllocator;
};

class ZStdCodec : public CodecBase {
public:
    ZStdCodec() = default;

    ~ZStdCodec() override = default;
    
    void Initialize(const StreamContext* indexStream, const StreamContext* vertexStream, u32, StackAllocator* allocator) override;
    void Decompress(DecompContext&) override;
    void Finalize() override;

private:
    ZSTD_DCtx* mDCtx = nullptr;
    u8* mIndexOutputBuffer;
    u8* mVertexOutputBuffer;
    u32 mRemainingIndexSize;
    u32 mRemainingVertexSize;
    StackAllocator* mStackAllocator;
};

class MeshCodec : public CodecBase {
public:
    MeshCodec() = default;

    ~MeshCodec() override = default;

    void Initialize(const StreamContext* indexStream, const StreamContext* vertexStream, u32, StackAllocator* allocator) override;
    void Decompress(DecompContext&) override;
    void Finalize() override;

private:
    StackAllocator* mStackAllocator;
    u8* mEncodedAttributeStreams[6];
    void* mAttributeStreamAllocations[6];
    VertexDecompContext mVertexDecompContext;
    u8* mVertexOutputBuffer;
    ZSTD_DCtx* mDCtx;
    u32 mVerticesProcessed;
    u32 mNumVertices;
    u32 mMaxVertexCopyCount;
    u32 mIndexBlockCountForUnencoded;
    s32 mUseVertexTable;
    u32 _11c;
    VertexDecompressor mVertexDecompressor;
    VertexInfoTableInfo _148;
    u32 mRemainingVertexSize;
    u32 mCurrentVertexOffset;
    IndexDecompressor mIndexDecompressor;
    IndexStreamContext mIndexStreamContext;
    bool mHasIndexBuffer;
    VertexStreamContext mVertexStreamContext;
    u32 mStage;
    u8 _324[0x420 - 0x324];
};

} // namespace mc