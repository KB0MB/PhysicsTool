#pragma once

#include "mc_Types.h"
#include "mc_CompressionFormat.h"
#include "mc_StreamContext.h"

#include <memory>

namespace mc {

class CodecBase;

enum Result {
    SizeMismatch = 0x80000002,
    InvalidCodec = 0x80000007,
    Error8 = 0x80000008,
    Error20 = 0x80000020,
};

struct BlockInfo {
    u64 data;

    u32 GetPrevAllocStartOffset() const {
        return data & 0x7fffffff;
    }

    u32 GetPrevAllocEndOffset() const {
        return data >> 0x1f & 0xffff;
    }

    bool NeedsCoalescing() const {
        return data >> 0x2f != 0;
    }

    void SetNeedsCoalescing() {
        data |= (1ull << 0x2f);
    }
};

struct MemBlock {
    BlockInfo* GetBlockInfo() {
        return reinterpret_cast<BlockInfo*>(reinterpret_cast<uintptr_t>(this) - 0x8);
    }
};

class StackAllocator {
public:
    struct InitArg {
        StreamContext* indexStream;
        StreamContext* vertexStream;
        void* workMemory;
        size_t workMemorySize;
    };

    StackAllocator(void* mem, size_t memSize, u64 type) : 
        mMemory(reinterpret_cast<u8*>(mem)), mMemorySize(memSize), mMemoryOffset(0),
        mLastAllocationStart(0), mPeakMemoryUsage(0), mAllocatorType(type) {}

    ~StackAllocator() = default;

    void* Alloc(size_t size, s64 alignment);
    void Free(void* ptr);

    s32 DecompressFrame(const u8* data, size_t size);

    template <typename T, typename... Args>
    T* Create(Args&&... args) {
        return std::construct_at(reinterpret_cast<T*>(Alloc(sizeof(T), alignof(T))), std::forward<Args>(args)...);
    }

    size_t GetPeakMemoryUsage() const {
        return mPeakMemoryUsage;
    }

    void SetFPUState(u32 state) {
        mPreviousFPUState = state;
    }

    u32 GetFPUState() const {
        return mPreviousFPUState;
    }

    void SetCodec(CodecBase* codec) {
        mCodec = codec;
    }

    CodecBase* GetCodec() const {
        return mCodec;
    }

    void SetStreamSizes(u32 stream, u32 end) {
        mStreamOffset = stream;
        mFrameEndOffset = end;
    }

private:
    u8* mMemory;
    size_t mMemorySize;
    size_t mMemoryOffset;           // offset to the top of the last allocation in the stack
    size_t mLastAllocationStart;    // offset to the start of the last allocation in the stack
    size_t mPeakMemoryUsage;
    [[maybe_unused]] u64 mAllocatorType;
    CodecBase* mCodec;
    u32 mStreamOffset;
    u32 mFrameEndOffset;
    u32 mPreviousFPUState;
    [[maybe_unused]] u8 _44[0x80 - 0x44];
};

struct CompressionFlags {
    CodecType codec;
    u32 _04;
};

s32 InitializeStackAllocator(StackAllocator**, const CompressionFlags&, const StackAllocator::InitArg&, const ResFrameSize*, u64);

s32 CreateStackAllocator(StackAllocator**, const StackAllocator::InitArg&, const ResCompressionHeader*, u64);

u32 ConvertResult(u64);

} // namespace mc