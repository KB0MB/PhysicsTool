#include "mc_Codec.h"
#include <algorithm>
#include "mc_DecompContext.h"
#include "mc_StackAllocator.h"

#include "mc_Zstd.h"
#include "mc_IndexCodec.h"

#include <cstring> // std::memcpy

namespace mc {

void NullCodec::Initialize(const StreamContext* indexStream, const StreamContext* vertexStream, u32, StackAllocator* allocator [[maybe_unused]]) {
    mRemainingIndexSize = indexStream->size;
    mRemainingVertexSize = vertexStream->size;
    mIndexOutputBuffer = indexStream->stream;
    mVertexOutputBuffer = vertexStream->stream;
}

void ZStdCodec::Initialize(const StreamContext* indexStream, const StreamContext* vertexStream, u32, StackAllocator* allocator) {
    void* wksp = allocator->Alloc(sizeof(ZSTD_DCtx) + 0x4d8, 8);
    mDCtx = SetupDCtx(wksp, sizeof(ZSTD_DCtx) + 0x4d8);

    mStackAllocator = allocator;
    mRemainingIndexSize = indexStream->size;
    mRemainingVertexSize = vertexStream->size;
    mIndexOutputBuffer = indexStream->stream;
    mVertexOutputBuffer = vertexStream->stream;
}

void MeshCodec::Initialize(const StreamContext* indexStream, const StreamContext* vertexStream, u32 a3, StackAllocator* allocator) {
    mStackAllocator = allocator;
    
    mIndexStreamContext.baseIndex = 0;
    mIndexStreamContext.indicesRemaining = 0;
    mIndexStreamContext.blocksRemaining = 0;
    mIndexStreamContext.streamCount = 0;
    mIndexStreamContext.indexFormat = IndexFormat::Invalid;
    mIndexStreamContext.encodingType = EncodingType::Invalid;
    mIndexStreamContext.blockCount = 0;
    mIndexStreamContext.rawCount = 0;
    mIndexStreamContext.indexOffset = 0;
    mIndexStreamContext.streamContext.stream = indexStream->stream;
    mIndexStreamContext.streamContext.size = indexStream->size;
    mIndexStreamContext.streamContext.alignment = indexStream->alignment;
    mHasIndexBuffer = false;

    mVertexDecompContext.groupMask = 0;
    mVertexDecompContext.groups = nullptr;
    mVertexDecompContext.numGroups = 0;
    mVertexDecompContext.stage = 2;

    mVertexOutputBuffer = vertexStream->stream;
    mVertexStreamContext.outputBuffer = vertexStream->stream;
    mVertexStreamContext.vertexOutputSize = 0;
    mVertexStreamContext.vertexAlign = vertexStream->alignment - 1;
    mVertexStreamContext.attrCount = 0;
    mVertexStreamContext.totalVertexOutputSize = 0;

    // the dctx in totk is 0x271f8 but here it's 0x176e0
    // they allocate 0x276d0 per dctx for mc though, not sure if there's a reason for that but let's add the extra just in case
    void* wksp = allocator->Alloc(sizeof(ZSTD_DCtx) + 0x4d8, 8);
    mDCtx = SetupDCtx(wksp, sizeof(ZSTD_DCtx) + 0x4d8);

    mVertexDecompressor.Initialize(a3, mDCtx, allocator);
    mIndexDecompressor.Initialize(a3, mDCtx, allocator);

    mStage = 0;
}

void NullCodec::Finalize() {}

void ZStdCodec::Finalize() {
    mStackAllocator->Free(mDCtx);
    mStackAllocator = nullptr;
    mVertexOutputBuffer = nullptr;
    mDCtx = nullptr;
    mIndexOutputBuffer = nullptr;
}

void MeshCodec::Finalize() {
    mVertexDecompressor.Finalize();
    mIndexDecompressor.Finalize();

    mStackAllocator->Free(mDCtx);
}

// untested because I have no test cases
void NullCodec::Decompress(DecompContext& ctx) {
    if (mRemainingIndexSize != 0) {
        u32 size = std::max(mRemainingIndexSize, 0x40000u);
        mRemainingIndexSize -= size;

        std::memcpy(mIndexOutputBuffer, ctx.currentPos, size);

        ctx.currentPos += size;
        mIndexOutputBuffer += size;
    } else {
        u32 size = std::max(mRemainingVertexSize, 0x40000u);
        mRemainingVertexSize -= size;

        std::memcpy(mVertexOutputBuffer, ctx.currentPos, size);

        ctx.currentPos += size;
        mVertexOutputBuffer += size;
    }
}

void ZStdCodec::Decompress(DecompContext& ctx) {
    size_t sizeRead = 0;
    u32 isNotCompressed = ctx.bitStream0.Read(1);
    const u8* currentPos = ctx.currentPos;
    while (sizeRead < 0x25800) {
        size_t outSize;
        void* outputBuffer;
        if (mRemainingIndexSize != 0) {
            u32 size = std::min(mRemainingIndexSize, 0x20000u);
            mRemainingIndexSize -= size;
            outputBuffer = mIndexOutputBuffer;
            mIndexOutputBuffer += size;
            outSize = static_cast<size_t>(size);
        } else if (mRemainingVertexSize != 0) {
            u32 size = std::min(mRemainingVertexSize, 0x20000u);
            mRemainingVertexSize -= size;
            outputBuffer = mVertexOutputBuffer;
            mVertexOutputBuffer += size;
            outSize = static_cast<size_t>(size);
        } else {
            ctx.currentPos = currentPos;
            return;
        }

        u32 inSize;
        if (!isNotCompressed) {
            const u8* sizeStart = currentPos;
            inSize = meshopt::decodeVByte(currentPos);
            sizeRead += static_cast<size_t>(currentPos - sizeStart);
        } else {
            inSize = outSize;
        }

        sizeRead += inSize;
        DecompressBlock(mDCtx, outputBuffer, outSize, currentPos, inSize, isNotCompressed);
        currentPos += inSize;
        isNotCompressed = ctx.bitStream0.Read(1);        
    }
    ctx.currentPos = currentPos;
}

void MeshCodec::Decompress(DecompContext& ctx) {
    u32 numBlocks = meshopt::decodeVByte(ctx.currentPos);

    // TODO: maybe there's a better way to handle this control flow
    switch (mStage) {
        case 0: // Decompression Start
            goto STAGE0;
        case 1: // Raw Vertex Stream Index Buffer Processing
            goto STAGE1;
        case 2: // Raw Vertex Stream Vertex Buffer Processing
            goto STAGE2;
        case 3: // Encoded Vertex Stream Index Buffer Processing
            goto STAGE3;
        case 4: // Encoded Vertex Stream Vertex Buffer Processing 0
            goto STAGE4;
        case 5: // Encoded Vertex Stream Vertex Buffer Processing 1
            goto STAGE5;
        default:
            UNREACHABLE_DEFAULT_CASE
    }

    STAGE0:
    while (numBlocks) {
        {
            --numBlocks;
            mHasIndexBuffer = mIndexStreamContext.ParseIndexHeader(ctx);
            u32 vertexCount = meshopt::decodeVByte(ctx.currentPos);
            u32 attrInfo = ctx.bitStream0.Read(5);
            u32 attrCount = attrInfo & 0xf;
            bool rawAttrs = attrInfo >> 4; // vertex attributes are just compressed and not encoded
            u32 outputOffset = mVertexStreamContext.totalVertexOutputSize;
            if (attrCount != 0) {
                u8 stride = *ctx.currentPos++;
                u8 baseOffset = *ctx.currentPos++;
                u32 attrIdx = 0;
                u32 attrBitSize = 0;
                u32 byteOffset = 0;
                u32 vtxBufIdx = 0;
                u32 maxAttrSize = 0;
                for (u32 i = 0; i < attrCount; ++i) {
                    u32 attrFlags = ctx.bitStream0.Read(11);
                    u32 componentCount = (attrFlags & 3) + 1;
                    u32 componentBitSize = (attrFlags >> 4 & 0x1f) + 1;
                    u32 size = componentCount * componentBitSize;
                    u32 unk = -8 << (attrFlags >> 9);
                    maxAttrSize = std::max(size, maxAttrSize);
                    u32 vertOffset = byteOffset + (static_cast<s32>(unk & attrBitSize) >> 3);
                    mVertexStreamContext.attrFlags[i] = (componentBitSize << 8) | ((attrBitSize & (~unk) & 0x3f) << 0x10) | componentCount | ((attrFlags >> 9) << 3) | (stride << 0x18);
                    mVertexStreamContext.localAttrOffsets[i] = vertOffset;
                    mVertexStreamContext.attrOffsets[i] = vertOffset + outputOffset;
                    
                    if (attrFlags >> 3 & 1) { // start of new vertex buffer
                        mVertexStreamContext.vertexBufferFlags[vtxBufIdx++] = stride | (baseOffset << 8) | (attrIdx << 0x10);
                        outputOffset += (baseOffset + mVertexStreamContext.vertexAlign + stride * vertexCount) & ~mVertexStreamContext.vertexAlign;
                        stride = *ctx.currentPos++;
                        baseOffset = *ctx.currentPos++;
                        attrBitSize = 0;
                        byteOffset = 0;
                    } else { // same vertex buffer as the previous attribute
                        attrBitSize += size;
                        u32 bitOffset = (static_cast<s32>(attrBitSize + 7) > -1) ? attrBitSize + 7 : attrBitSize + 0xe;
                        if (attrFlags >> 2 & 1) { // new attribute
                            attrBitSize = 0;
                            byteOffset += (bitOffset >> 3);
                        }
                    }
                    ++attrIdx;
                }
                outputOffset += (baseOffset + mVertexStreamContext.vertexAlign + stride * vertexCount) & ~mVertexStreamContext.vertexAlign;
                mVertexStreamContext.vertexBufferFlags[vtxBufIdx] = stride | (baseOffset << 8) | ((attrCount - 1) << 0x10);
                mVertexStreamContext.attrCount = attrCount;
                mVertexStreamContext.maxAttrBitSize = maxAttrSize;
            } else { // same vertex format as the previous round
                attrCount = mVertexStreamContext.attrCount;
                if (attrCount > 0) {
                    u32 vtxBufIdx = 0;
                    u32 index = 0;
                    while (index < attrCount) {
                        const u32 vtxFlags = mVertexStreamContext.vertexBufferFlags[vtxBufIdx++];
                        u32 attrIndex = vtxFlags >> 0x10;
                        if (attrIndex >= index) {
                            attrIndex = std::max(attrIndex, index);
                            u32 baseIndex = index;
                            u32 componentCount = attrIndex - baseIndex + 1;
                            // do remainder
                            if (componentCount & 3) {
                                for (u32 i = componentCount & 3; i != 0; --i) {
                                    mVertexStreamContext.attrOffsets[baseIndex] = outputOffset + mVertexStreamContext.localAttrOffsets[baseIndex];
                                    ++baseIndex;
                                }
                            }
                            // do groups of 4
                            if (attrIndex - baseIndex > 2) {
                                for (u32 i = attrIndex - baseIndex + 1; i != 0; i -= 4) {
                                    mVertexStreamContext.attrOffsets[baseIndex] = outputOffset + mVertexStreamContext.localAttrOffsets[baseIndex];
                                    mVertexStreamContext.attrOffsets[baseIndex + 1] = outputOffset + mVertexStreamContext.localAttrOffsets[baseIndex + 1];
                                    mVertexStreamContext.attrOffsets[baseIndex + 2] = outputOffset + mVertexStreamContext.localAttrOffsets[baseIndex + 2];
                                    mVertexStreamContext.attrOffsets[baseIndex + 3] = outputOffset + mVertexStreamContext.localAttrOffsets[baseIndex + 3];
                                    baseIndex += 4;
                                }
                            }
                            index = attrIndex + 1;
                        }
                        outputOffset += ((vtxFlags >> 8 & 0xff) + mVertexStreamContext.vertexAlign + (vtxFlags & 0xff) * vertexCount) & ~mVertexStreamContext.vertexAlign;
                    }
                }
            }

            mVertexStreamContext.vertexOutputSize = outputOffset - mVertexStreamContext.totalVertexOutputSize;
            mVertexStreamContext.totalVertexOutputSize = outputOffset;
            u32 unk = mVertexStreamContext.maxAttrBitSize < 0x60 ? 15 : 14;
            mMaxVertexCopyCount = 1 << unk;
            mVerticesProcessed = 0;
            mNumVertices = vertexCount;
            mIndexBlockCountForUnencoded = (vertexCount + ~(-1 << unk)) >> unk; // should just be 1? idk what this is for
            
            if (rawAttrs) {
                mRemainingVertexSize = mVertexStreamContext.vertexOutputSize;
                mCurrentVertexOffset = mVertexStreamContext.totalVertexOutputSize - mVertexStreamContext.vertexOutputSize;
                goto STAGE1;
            } else {
                mUseVertexTable = ctx.bitStream0.Read(1);
                if (mUseVertexTable == 1) {
                    _148._00 = 0;
                    _148._04 = 0;
                    u32 maxPossibleVertCount = 1 << std::min(0x20 - Clz(vertexCount - 1), 0x10u);
                    _148._08 = maxPossibleVertCount;
                    _148._0c = maxPossibleVertCount - 1;
                    // size aligned up to 8 bytes
                    _148._10 = reinterpret_cast<u32*>(mStackAllocator->Alloc((maxPossibleVertCount * 4 + 7) & 0x7fffffff8u, 8));
                }
                goto STAGE3;
            }
        }

        // no fallthrough

        STAGE1: {
            if (numBlocks == 0) {
                mStage = 1;
                return;
            }
            --numBlocks;

    
            if (mHasIndexBuffer && mIndexBlockCountForUnencoded > 0) {
                mIndexStreamContext.Decompress(mIndexDecompressor, ctx, mNumVertices, nullptr, 0, std::min(mMaxVertexCopyCount, mNumVertices), mStackAllocator);
                if (mIndexBlockCountForUnencoded - 1) {
                    u32 copied = 0;
                    for (u32 i = mIndexBlockCountForUnencoded - 1; i != 0; --i) {
                        copied += mMaxVertexCopyCount;
                        mIndexStreamContext.Decompress(mIndexDecompressor, ctx, mNumVertices, nullptr, copied, std::min(mMaxVertexCopyCount, mNumVertices), mStackAllocator);
                    }
                }
            }
            InsertUncompressedBlock(mDCtx, mVertexOutputBuffer, mCurrentVertexOffset);
        }
        // fallthrough to stage 2
        STAGE2: {
            while (mRemainingVertexSize != 0) {
                if (numBlocks == 0) {
                    mStage = 2;
                    return;
                }
                --numBlocks;
    
                u32 outSize = std::min(mRemainingVertexSize, 0x20000u);
                u32 notCompressed = ctx.bitStream0.Read(1);
                u32 inSize;
                if (notCompressed) {
                    inSize = outSize;
                } else {
                    inSize = meshopt::decodeVByte(ctx.currentPos);
                }
                DecompressBlock(mDCtx, mVertexOutputBuffer + mCurrentVertexOffset, outSize, ctx.currentPos, inSize, notCompressed);
                ctx.currentPos += inSize;
                mRemainingVertexSize -= outSize;
                mCurrentVertexOffset += outSize;
            }
            
            continue;
        }
        
        // no fallthrough

        STAGE3: {
            if (numBlocks == 0) {
                mStage = 3;
                return;
            }

            u64* tbl = nullptr;
            u32 vertexCount = std::min(mMaxVertexCopyCount, mNumVertices - mVerticesProcessed);

            if (ctx.bitStream0.DirectionalRead(1) && vertexCount) {
                tbl = reinterpret_cast<u64*>(mStackAllocator->Alloc(vertexCount * sizeof(u64), 8));
            }
            
            --numBlocks;
            if (mHasIndexBuffer) {
                mIndexStreamContext.Decompress(mIndexDecompressor, ctx, mNumVertices, tbl, mVerticesProcessed, vertexCount, mStackAllocator);
            }
            mVertexStreamContext.indexBufferTable = tbl;
        }
        // fallthrough to stage 4
        STAGE4: {
            if (numBlocks == 0) {
                mStage = 4;
                return;
            }
            --numBlocks;

            u32* tbl;
            if (mUseVertexTable == 1) {
                u32 copyCount = std::min(mNumVertices - mVerticesProcessed, mMaxVertexCopyCount);
                if (copyCount) {
                    tbl = reinterpret_cast<u32*>(mStackAllocator->Alloc((copyCount * 4 + 7) & 0xfffffffffffffff8, 8));
                } else {
                    tbl = nullptr;
                }
                AllocationSet allocations;
                VertexDecodingStreamSet streams;
                VertexDecodingStreamSizes streamSizes;
                ReadVertexInfoTableBlock(streams, streamSizes, allocations, 4, ctx, &mVertexDecompressor);
                DecodeVertexInfoTable(tbl, copyCount, streams, streamSizes, 4, _148, mVerticesProcessed);
                mStackAllocator->Free(allocations.bitStream);
                mStackAllocator->Free(allocations.backrefOffsetStream);
                mStackAllocator->Free(allocations.backrefCountStream);
                mStackAllocator->Free(allocations.vertexCountStream);
            } else {
                tbl = nullptr;
            }
            mVertexStreamContext.vertexBufferTable = tbl;
            mVertexStreamContext.decompContext = &ctx;
            mVertexStreamContext.baseVertexIndex = mVerticesProcessed;
            mVertexStreamContext.outputBuffer = mVertexOutputBuffer;
            mVertexDecompContext.ReadVertexBlockGroup(ctx, &mVertexDecompressor, mVertexStreamContext.attrCount);
            mVertexStreamContext.attrIndex = 0;
        }
        // fallthrough to stage 5 
        STAGE5: {
            if (numBlocks == 0) {
                mStage = 5;
                return;
            }

            for (; mVertexStreamContext.attrIndex < mVertexStreamContext.attrCount; ++mVertexStreamContext.attrIndex) {
                if (numBlocks == 0) {
                    mStage = 5;
                    return;
                }

                u32 vertCount = std::min(mMaxVertexCopyCount, mNumVertices - mVerticesProcessed);
                VertexDecodeGroup* groups = nullptr;
                u32 groupCount = 0;
                s32 count = mVertexDecompContext.ProcessVertexBlockGroup(&groups, &groupCount, ctx, &mVertexDecompressor, mVertexStreamContext, vertCount, mStackAllocator);

                if (count == 0) {
                    DecodeBackrefs(mVertexStreamContext, vertCount, groups, groupCount);
                } else {
                    u32 attrFormat = ctx.bitStream0.Read(7);
                    u32 attrFlags = mVertexStreamContext.attrFlags[mVertexStreamContext.attrIndex];
                    AttrStreamInfo streamInfo[6];

                    s32 streamCount = sAttributeGetStreamInfoFunctions[attrFormat](streamInfo, 6, ctx.currentPos, attrFlags & 7, attrFlags >> 8 & 0xff, count);

                    if (streamCount < 1) { // none of the functions in totk have 0 streams so this shouldn't ever be taken
                        sAttributeDecodeFunctions[attrFormat](mVertexStreamContext, vertCount, groups, groupCount, mEncodedAttributeStreams, streamCount);
                    } else {
                        for (s32 i = 0; i != streamCount; ++i) {
                            if (streamInfo[i].elementCount == 0) {
                                mEncodedAttributeStreams[i] = reinterpret_cast<u8*>(&mEncodedAttributeStreams[i]);
                                mAttributeStreamAllocations[i] = nullptr;
                            } else {
                                mAttributeStreamAllocations[i] = mVertexDecompressor.ProcessBlock(mEncodedAttributeStreams[i], streamInfo[i].elementType, streamInfo[i].tableCount, streamInfo[i].elementCount, 8, ctx);
                            }
                        }
                        sAttributeDecodeFunctions[attrFormat](mVertexStreamContext, vertCount, groups, groupCount, mEncodedAttributeStreams, streamCount);
                        for (u32 i = streamCount; i != 0; --i) {
                            mStackAllocator->Free(mAttributeStreamAllocations[i - 1]);
                        }
                    }
                }

                --numBlocks;
                
                mVertexDecompContext.FinishGroupProcessing(mStackAllocator);
            }

            mVertexDecompContext.Reset(mStackAllocator);
            mVerticesProcessed += std::min(mNumVertices - mVerticesProcessed, mMaxVertexCopyCount);
            if (mVertexStreamContext.vertexBufferTable) {
                mStackAllocator->Free(mVertexStreamContext.vertexBufferTable);
                mVertexStreamContext.vertexBufferTable = nullptr;
            }
            if (mVertexStreamContext.indexBufferTable) {
                mStackAllocator->Free(mVertexStreamContext.indexBufferTable);
                mVertexStreamContext.indexBufferTable = nullptr;
            }
            if (mNumVertices == mVerticesProcessed) {
                if (mUseVertexTable == 1 && _148._10)
                    mStackAllocator->Free(_148._10);
                goto STAGE0;
            } else {
                goto STAGE3;
            }
        }
    }

    mStage = 0;
    return;
}

} // namespace mc
