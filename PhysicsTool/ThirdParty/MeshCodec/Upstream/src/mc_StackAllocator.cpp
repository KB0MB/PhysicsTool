#include "mc_StackAllocator.h"
#include "mc_Float.h"
#include "mc_Codec.h"
#include "mc_DecompContext.h"

#include "mc_IndexCodec.h"

#include <algorithm> // std::max
#include <cstdlib> // exit

#ifndef NDEBUG
#include <iostream>
#endif

namespace mc {

namespace detail {

void ExitWithDetail(const char* msg [[maybe_unused]], int line [[maybe_unused]]) {
#ifndef NDEBUG
    std::cerr << "Error in " << msg << " at line " << line << std::endl;
#endif
    exit(0); // they exit with code 0 even though this is a failure path but ok
}

} // detail

void* StackAllocator::Alloc(size_t size, s64 alignment) {
    if (size == 0)
        return nullptr;
    
    u64 start = (alignment + mMemoryOffset + 7) & -alignment;
    u64 end = start + size;
    if (end > mMemorySize)
        detail::ExitWithDetail("mc_StackAllocator.cpp", __LINE__); // Do not embed the developer's absolute build path.
    
    void* ptr = reinterpret_cast<void*>(mMemory + start);
    reinterpret_cast<MemBlock*>(ptr)->GetBlockInfo()->data = (start - mLastAllocationStart) | (start - mMemoryOffset) << 0x1f;
    mMemoryOffset = end;
    mLastAllocationStart = start;
    mPeakMemoryUsage = std::max(mPeakMemoryUsage, static_cast<size_t>(end));

    return ptr;
}

void StackAllocator::Free(void* ptr) {
    if (ptr == nullptr)
        return;
    
    MemBlock* block = reinterpret_cast<MemBlock*>(ptr);
    size_t startOffset = mLastAllocationStart;
    
    // when freeing, check if the allocation is the top of the current "stack"
    if (reinterpret_cast<void*>(mMemory + mLastAllocationStart) != ptr) {
        // mark block for later if not the most recent allocation
        block->GetBlockInfo()->SetNeedsCoalescing();
    } else {
        size_t memOffset = startOffset - block->GetBlockInfo()->GetPrevAllocEndOffset();
        startOffset -= block->GetBlockInfo()->GetPrevAllocStartOffset();
        BlockInfo* block = reinterpret_cast<MemBlock*>(mMemory + startOffset)->GetBlockInfo();
        // check previous blocks for blocks that can be combined
        if (startOffset != 0 && block->NeedsCoalescing()) {
            while (block->NeedsCoalescing()) {
                memOffset = startOffset;
                startOffset -= block->GetPrevAllocStartOffset();
                if (startOffset == 0)
                    break;
                block = reinterpret_cast<MemBlock*>(mMemory + startOffset)->GetBlockInfo();
            }
            memOffset -= block->GetPrevAllocEndOffset();
        }
        mMemoryOffset = memOffset;
        mLastAllocationStart = startOffset;
    }
}

s32 StackAllocator::DecompressFrame(const u8* data, size_t size) {
    u32 bitStreamOffset0 = mStreamOffset;
    u32 bitStreamOffset1 = mFrameEndOffset;
    if (bitStreamOffset1 + bitStreamOffset0 != size)
        return SizeMismatch;

    const u8* ptr = reinterpret_cast<const u8*>(data);
    
    // increments ptr
    mStreamOffset = meshopt::decodeVByteReversed(ptr);
    mFrameEndOffset = meshopt::decodeVByteReversed(ptr);

    DecompContext ctx;
    ctx.currentPos = ptr;
    ctx.bitStream0 = BitStreamReader(reinterpret_cast<const u64*>(data + bitStreamOffset0 - 8), BitStreamReader::Direction::Backwards);
    ctx.bitStream1 = BitStreamReader(reinterpret_cast<const u64*>(data + bitStreamOffset0), BitStreamReader::Direction::Forwards);
    ctx.bitStream2 = BitStreamReader(reinterpret_cast<const u64*>(data + bitStreamOffset0 + bitStreamOffset1 - 8), BitStreamReader::Direction::Backwards);

    mCodec->Decompress(ctx);

    if (mFrameEndOffset + mStreamOffset != 0)
        return mFrameEndOffset + mStreamOffset;
    
    detail::SetFPUState(mPreviousFPUState);

    return 0;
}

namespace detail {

CodecBase* CreateCodec(CodecType type, StackAllocator* allocator) {
    switch (type) {
        case CodecType::MeshCodec:
            return allocator->Create<MeshCodec>();
        case CodecType::ZStandard:
            return allocator->Create<ZStdCodec>();
        case CodecType::Null:
        default:
            return allocator->Create<NullCodec>();
    }
}

inline bool UnkValueIsValid(u32 value) {
    return value < 0x400;
}

} // detail

s32 InitializeStackAllocator(StackAllocator** outPtr, const CompressionFlags& flags, const StackAllocator::InitArg& initArg, const ResFrameSize* sizes, u64 type) {
    if (type != 6)
        return static_cast<s32>(SizeMismatch);
    
    StackAllocator* allocator = std::construct_at(reinterpret_cast<StackAllocator*>(initArg.workMemory),
                                                  reinterpret_cast<void*>(reinterpret_cast<uintptr_t>(initArg.workMemory) + sizeof(StackAllocator)),
                                                  initArg.workMemorySize - sizeof(StackAllocator), 0x40);
    CodecBase* codec = detail::CreateCodec(flags.codec, allocator);
    codec->Initialize(initArg.indexStream, initArg.vertexStream, flags._04, allocator);
    allocator->SetCodec(codec);

    u32 streamOffset = sizes->streamOffset.get();
    u32 endOffset = sizes->endOffset.get();
    
    allocator->SetFPUState(detail::InitFPUState());
    allocator->SetStreamSizes(streamOffset, endOffset);

    *outPtr = allocator;

    return streamOffset + endOffset;
}

s32 CreateStackAllocator(StackAllocator** outPtr, const StackAllocator::InitArg& arg, const ResCompressionHeader* res, u64 a4) {
    // nn::util::ReferSymbol("SDK MW+Nintendo+NintendoWare_Meshoptimizer_For_MeshCodec-0_15_0-Release");
    if (a4 != 8)
        return static_cast<s32>(Error20);
    
    CompressionFlags flags{
        res->GetCodecType(),
        static_cast<u32>(res->flags >> 2),
    };
    if (flags.codec != CodecType::Invalid) {
        if (!detail::UnkValueIsValid(flags._04))
            return static_cast<s32>(Error8);
        return InitializeStackAllocator(outPtr, flags, arg, &res->sizeInfo, 6);
    } else {
        return static_cast<s32>(InvalidCodec);
    }
}

u32 ConvertResult(u64 raw) {
    if (raw >> 0x1f) {
        if (raw == SizeMismatch)
            return 0x1c;
        else if (raw > 0x80000003)
            return static_cast<u32>(raw) + 0x7ffffffcu;
        else
            return 2;
    } else {
        return 0; // success
    }
}

} // namespace mc
