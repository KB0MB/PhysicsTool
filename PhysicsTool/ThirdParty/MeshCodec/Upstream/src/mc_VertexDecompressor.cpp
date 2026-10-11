#include "mc_VertexDecompressor.h"
#include "mc_VertexCodec.h"
#include "mc_DecompContext.h"
#include "mc_StackAllocator.h"

#include "mc_Zstd.h"

namespace mc {

void VertexDecompressor::Initialize(u32, ZSTD_DCtx* dctx, StackAllocator* allocator) {
    mDCtx = dctx;
    mBuffer = reinterpret_cast<u8*>(allocator->Alloc(0x80000, 8));
    mOffset = 0;
    mBufferSize = 0x80000;
    mDecodingContext = allocator->Create<DecodingContext>();
    mStackAllocator = allocator;
    mDecodingContext->_10._20 = 0;
}

void VertexDecompressor::Finalize() {
    mStackAllocator->Free(mDecodingContext);
    mStackAllocator->Free(mBuffer);
}

void* VertexDecompressor::ProcessBlock(u8*& dst, ElementType elementType, s32 tableCount, s32 elementCount, u32 baseOutSize, DecompContext& ctx) {
    s32 streamSize = elementCount * tableCount;
    s32 outputSize = streamSize << (static_cast<u32>(elementType) & 0x1f);

    u8* output;
    void* allocation;
    switch(ctx.bitStream0.Read(2)) {
        case 0: { // series of decoding tables but each table can be decoded into one or more output streams
            output = reinterpret_cast<u8*>(mStackAllocator->Alloc((baseOutSize + outputSize + 0xf) & 0xfffffff0, 0x10));
            allocation = output;

            u32 elementSize = (streamSize > -1 ? streamSize : streamSize + 0xf) >> 4;
            if (elementSize < 2)
                elementSize = 1;

            s32 temp = 0x20 - Clz(static_cast<u32>(elementSize - 1));
            elementSize = temp >= 5 ? temp : 4;
            elementSize = streamSize < 0x4000 ? elementSize : 10;

            if (elementType == ElementType::U8) {
                DecodeByteStream<u8>(output, outputSize, tableCount, elementSize, mDecodingContext, ctx);
            } else {
                DecodeByteStream<u16>(reinterpret_cast<u16*>(output), outputSize, tableCount, elementSize, mDecodingContext, ctx);
            }
            break;
        }
        case 1: { // series of decoding tables but each table can only be decoded into a single output stream
            output = reinterpret_cast<u8*>(mStackAllocator->Alloc((baseOutSize + outputSize + 0xf) & 0xfffffff0, 0x10));
            allocation = output;

            BufferView view;
            view.ptr = ctx.currentPos;
            view._08 = 0;
            view.offset = 0;
            if (elementType == ElementType::U8) { // maybe I should make this a small function like with case 0
                u8* outPtr = output;
                if (streamSize > 0x7ffff) {
                    for (u32 i = (streamSize - 0x40000) >> 0x12; i != 0; --i) {
                        GenDecodingTable(mDecodingContext, ctx);
                        DecodeTable<u8>(outPtr, 1, 0x40000, mDecodingContext, view, ctx);
                        outPtr += 0x40000;
                        streamSize -= 0x40000;
                    }
                }
                GenDecodingTable(mDecodingContext, ctx);
                DecodeTable<u8>(outPtr, 1, streamSize, mDecodingContext, view, ctx);
            } else {
                u16* outPtr = reinterpret_cast<u16*>(output);
                if (streamSize > 0x7ffff) {
                    for (u32 i = (streamSize - 0x40000) >> 0x12; i != 0; --i) {
                        GenDecodingTable(mDecodingContext, ctx);
                        DecodeTable<u16>(outPtr, 1, 0x40000, mDecodingContext, view, ctx);
                        outPtr += 0x40000;
                        streamSize -= 0x40000;
                    }
                }
                GenDecodingTable(mDecodingContext, ctx);
                DecodeTable<u16>(outPtr, 1, streamSize, mDecodingContext, view, ctx);
            }
            ctx.currentPos = view.ptr + view.offset;
            break;
        }
        case 2: { // zstd compressed stream
            allocation = nullptr;

            u32 offset = mOffset;
            InsertBlocks(mDCtx, mBuffer + offset, mBufferSize - offset, mBuffer, offset);
            if (offset + outputSize > 0x80000) {
                mBufferSize = offset;
                offset = 0;
            }
            DecompressVertexStream(mDCtx, mBuffer + offset, outputSize, &ctx, 0x20000);
            mOffset = offset + outputSize;
            output = mBuffer + offset;
            break;
        }
        case 3: { // uncompressed and unencoded stream
            allocation = nullptr;

            output = const_cast<u8*>(ctx.currentPos);
            ctx.currentPos += outputSize;
            break;
        }
        default:
            UNREACHABLE_DEFAULT_CASE
    }

    dst = output;

    return allocation;
}
    
} // namespace mc