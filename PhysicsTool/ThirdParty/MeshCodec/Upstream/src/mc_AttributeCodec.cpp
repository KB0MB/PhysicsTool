#include "mc_AttributeCodec.h"
#include "mc_DecompContext.h"

#include "mc_Float.h"

#include <cmath> // std::sqrt
#include <algorithm>
#include <cstring> // std::memcpy
#include <type_traits> // std::is_same_v

namespace mc {

namespace detail {

template <size_t BitSize, size_t ComponentCount, bool UseTable>
void DecodeInternalDeltas(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert(BitSize == 8 || (BitSize == 10 && ComponentCount == 3) || BitSize == 16, "Invalid bit size");
    static_assert(ComponentCount > 1 && ComponentCount < 5, "Invalid component count");
    u8* baseValueStream = inputStreams[0];
    u8* valueStream = inputStreams[1];
    const u32 stride = ctx.attrFlags[ctx.attrIndex] >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);

    if constexpr (UseTable) {
        u8* refBaseValueStream = inputStreams[2];
        u32 tableIndex = 0;
        for (; numGroups != 0; --numGroups) {
            for (u32 i = groups->GetRawCount(); i != 0; --i) {
                if (u32 offset = ctx.vertexBufferTable[tableIndex++]) {
                    if constexpr (BitSize == 8) {
                        for (u32 j = 0; j < ComponentCount; ++j) {
                            output[j] = *refBaseValueStream++ + (output - (offset >> 3) * stride)[j];
                        }
                    } else if constexpr (BitSize == 10) {
                        const u16 base0 = reinterpret_cast<u16*>(refBaseValueStream)[0];
                        const u16 base1 = reinterpret_cast<u16*>(refBaseValueStream)[1];
                        const u16 base2 = reinterpret_cast<u16*>(refBaseValueStream)[2];
                        const u32 value = reinterpret_cast<u32*>(output - (offset >> 3) * stride)[0];
                        *reinterpret_cast<u32*>(output) = ((value + base0) & 0x3ff) | ((value + base1 * 0x400) & 0xffc00) | ((value + base2 * 0x100000) & 0x3ff00000);
                        refBaseValueStream += 3 * sizeof(u16);
                    } else if constexpr (BitSize == 16) {
                        for (u32 j = 0; j < ComponentCount; ++j) {
                            reinterpret_cast<u16*>(output)[j] = *reinterpret_cast<u16*>(refBaseValueStream) + reinterpret_cast<u16*>(output - (offset >> 3) * stride)[j];
                            refBaseValueStream += sizeof(u16);
                        }
                    } else {
                        static_assert(false, "you shouldn't be here");
                    }
                } else {
                    if constexpr (BitSize == 8) {
                        u8 sum = 0;
                        for (u32 j = 1; j < ComponentCount; ++j) {
                            const u8 value = *valueStream++;
                            output[j] = value;
                            sum += value;
                        }
                        output[0] = *baseValueStream++ + ~sum;
                    } else if constexpr (BitSize == 10) {
                        const u16 value0 = reinterpret_cast<u16*>(valueStream)[0];
                        const u16 value1 = reinterpret_cast<u16*>(valueStream)[1];
                        const u16 base = reinterpret_cast<u16*>(baseValueStream)[0];
                        *reinterpret_cast<u32*>(output) = ((base + ~(value0 + value1)) & 0x3ff) | (value0 << 10) | (static_cast<u32>(value1) << 20);
                        valueStream += 2 * sizeof(u16);
                        baseValueStream += sizeof(u16);
                    } else if constexpr (BitSize == 16) {
                        u16 sum = 0;
                        for (u32 j = 1; j < ComponentCount; ++j) {
                            const u16 value = *reinterpret_cast<u16*>(valueStream);
                            reinterpret_cast<u16*>(output)[j] = value;
                            sum += value;
                            valueStream += sizeof(u16);
                        }
                        output[0] = *reinterpret_cast<u16*>(baseValueStream) + ~sum;
                        baseValueStream += sizeof(u16);
                    } else {
                        static_assert(false, "you shouldn't be here");
                    }
                }
                output += stride;
            }
            if (groups->vertexCount > 0xffff) {
                for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                    if constexpr (BitSize * ComponentCount == 0x1e) {
                        *reinterpret_cast<u32*>(output) = *reinterpret_cast<u32*>(output - groups->backRefOffset) & 0x3fffffff;
                    } else {
                        std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * ComponentCount);
                    }
                    output += stride;
                }
                tableIndex += groups->GetCopyCount();
            }
            ++groups;
        }
    } else {
        for (; numGroups != 0; --numGroups) {
            for (u32 i = groups->GetRawCount(); i != 0; --i) {
                if constexpr (BitSize == 8) {
                    u8 sum = 0;
                    for (u32 j = 1; j < ComponentCount; ++j) {
                        const u8 value = *valueStream++;
                        output[j] = value;
                        sum += value;
                    }
                    output[0] = *baseValueStream++ + ~sum;
                } else if constexpr (BitSize == 10) {
                    const u16 value0 = reinterpret_cast<u16*>(valueStream)[0];
                    const u16 value1 = reinterpret_cast<u16*>(valueStream)[1];
                    const u16 base = reinterpret_cast<u16*>(baseValueStream)[0];
                    *reinterpret_cast<u32*>(output) = ((base + ~(value0 + value1)) & 0x3ff) | (value0 << 10) | (static_cast<u32>(value1) << 20);
                    valueStream += 2 * sizeof(u16);
                    baseValueStream += sizeof(u16);
                } else if constexpr (BitSize == 16) {
                    u16 sum = 0;
                    for (u32 j = 1; j < ComponentCount; ++j) {
                        const u16 value = *reinterpret_cast<u16*>(valueStream);
                        reinterpret_cast<u16*>(output)[j] = value;
                        sum += value;
                        valueStream += sizeof(u16);
                    }
                    output[0] = *reinterpret_cast<u16*>(baseValueStream) + ~sum;
                    baseValueStream += sizeof(u16);
                } else {
                    static_assert(false, "you shouldn't be here");
                }
                output += stride;
            }
            if (groups->vertexCount > 0xffff) {
                for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                    if constexpr (BitSize * ComponentCount == 0x1e) {
                        *reinterpret_cast<u32*>(output) = *reinterpret_cast<u32*>(output - groups->backRefOffset) & 0x3fffffff;
                    } else {
                        std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * ComponentCount);
                    }
                    output += stride;
                }
            }
            ++groups;
        }
    }
}

// these next three could probably be combined but idk how best to do that without making it too messy
template <size_t BitSize>
void DecodeRaw(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert(BitSize == 2 || BitSize == 0x1e || ((BitSize & 7) == 0), "Invalid bit size");
    u8* valueStream = inputStreams[0];
    const u32 stride = ctx.attrFlags[ctx.attrIndex] >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);

    for (; numGroups; --numGroups) {
        for (u32 i = groups->GetRawCount(); i != 0; --i) {
            if constexpr (BitSize == 2) {
                *reinterpret_cast<u32*>(output) = (*reinterpret_cast<u32*>(output) & 0x3fffffff) | (*valueStream++ << 0x1e);
            } else if constexpr (BitSize == 0x1e) {
                *reinterpret_cast<u32*>(output) = reinterpret_cast<u16*>(valueStream)[0] | (reinterpret_cast<u16*>(valueStream)[1] << 10) | (static_cast<u32>(reinterpret_cast<u16*>(valueStream)[2]) << 20);
                valueStream += 3 * sizeof(u16);
            } else {
                std::memcpy(output, valueStream, BitSize >> 3);
                valueStream += (BitSize >> 3);
            }
            output += stride;
        }
        if (groups->vertexCount > 0xffff) {
            for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                if constexpr (BitSize == 2) {
                    *reinterpret_cast<u32*>(output) = (*reinterpret_cast<u32*>(output - groups->backRefOffset) & 0xc0000000) | (*reinterpret_cast<u32*>(output) & 0x3fffffff);
                } else if constexpr (BitSize == 0x1e) {
                    *reinterpret_cast<u32*>(output) = *reinterpret_cast<u32*>(output - groups->backRefOffset) & 0x3fffffff;
                } else {
                    std::memcpy(output, output - groups->backRefOffset, BitSize >> 3);
                }
                output += stride;
            }
        }
        ++groups;
    }
}

template <typename InputT, typename OutputT, bool UseTable>
void DecodeRawCustomSize(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert(std::is_same_v<InputT, u8> || std::is_same_v<InputT, u16>, "Invalid input type");
    static_assert(std::is_same_v<OutputT, u32> || std::is_same_v<OutputT, u64>, "Invalid output type");
    
    #define MASK(VALUE, SHIFT, MASK) ((((VALUE) >> (SHIFT)) & (MASK)) << (SHIFT))
    
    const u32 flags = ctx.attrFlags[ctx.attrIndex];
    const u32 stride = flags >> 0x18;
    const u32 attrShift = flags >> 0x10 & 0xff; // bit offset of the start of the attribute
    const u32 compShift = flags >> 0x8 & 0xff; // size in bits of each individual component (aka shift amount to get from component to component)
    const OutputT copyMask = (2 << (compShift * (flags & 7) - 1)) - 1;
    const OutputT keepMask = ~(copyMask << attrShift);
    InputT* input = reinterpret_cast<InputT*>(inputStreams[0]);
    OutputT* output = reinterpret_cast<OutputT*>(ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride));

    switch (flags & 7) { // component count
        case 1: {
            if constexpr (UseTable) {
                u32 tableIndex = 0;
                InputT* refBaseValueStream = reinterpret_cast<InputT*>(inputStreams[1]);
                const OutputT mask = ~(-1ll << compShift);

                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        if (u32 offset = ctx.vertexBufferTable[tableIndex++]) {
                            *output = ((*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - (offset >> 3) * stride) >> attrShift & mask)+ *refBaseValueStream++) << attrShift | (*output & keepMask);
                        } else {
                            *output = (static_cast<OutputT>(*input++) << attrShift) | (*output & keepMask);
                        }
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                        tableIndex += groups->GetCopyCount();
                    }
                    ++groups;
                }
            } else {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        *output = (static_cast<OutputT>(*input++) << attrShift) | (*output & keepMask);
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                    }
                    ++groups;
                }
            }
            return;
        }
        case 2: {
            if constexpr (UseTable) {
                u32 tableIndex = 0;
                InputT* refBaseValueStream = reinterpret_cast<InputT*>(inputStreams[1]);
                const OutputT mask = ~(-1ll << compShift);

                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        if (u32 offset = ctx.vertexBufferTable[tableIndex++]) {
                            OutputT value = *reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - (offset >> 3) * stride) >> attrShift;
                            OutputT value0 = ((value & mask) + *refBaseValueStream++) & mask;
                            OutputT value1 = ((value >> compShift & mask) + *refBaseValueStream++) & mask;
                            *output = ((value0 | (value1 << compShift)) << attrShift) | (*output & keepMask);
                        } else {
                            OutputT value0 = *input++;
                            OutputT value1 = *input++;
                            *output = ((value0 | (value1 << compShift)) << attrShift) | (*output & keepMask);
                        }
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                        tableIndex += groups->GetCopyCount();
                    }
                    ++groups;
                }
            } else {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        OutputT value0 = *input++;
                        OutputT value1 = *input++;
                        *output = ((value0 | (value1 << compShift)) << attrShift) | (*output & keepMask);
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                    }
                    ++groups;
                }
            }
            return;
        }
        case 3: {
            if constexpr (UseTable) {
                u32 tableIndex = 0;
                InputT* refBaseValueStream = reinterpret_cast<InputT*>(inputStreams[1]);
                const OutputT mask = ~(-1ll << compShift);

                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        if (u32 offset = ctx.vertexBufferTable[tableIndex++]) {
                            OutputT value = *reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - (offset >> 3) * stride) >> attrShift;
                            OutputT value0 = ((value & mask) + *refBaseValueStream++) & mask;
                            OutputT value1 = ((value >> compShift & mask) + *refBaseValueStream++) & mask;
                            OutputT value2 = ((value >> (compShift * 2) & mask) + *refBaseValueStream++) & mask;
                            *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2))) << attrShift) | (*output & keepMask);
                        } else {
                            OutputT value0 = *input++;
                            OutputT value1 = *input++;
                            OutputT value2 = *input++;
                            *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2))) << attrShift) | (*output & keepMask);
                        }
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                        tableIndex += groups->GetCopyCount();
                    }
                    ++groups;
                }
            } else {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        OutputT value0 = *input++;
                        OutputT value1 = *input++;
                        OutputT value2 = *input++;
                        *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2))) << attrShift) | (*output & keepMask);
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                    }
                    ++groups;
                }
            }
            return;
        }
        case 4: {
            if constexpr (UseTable) {
                u32 tableIndex = 0;
                InputT* refBaseValueStream = reinterpret_cast<InputT*>(inputStreams[1]);
                const OutputT mask = ~(-1ll << compShift);

                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        if (u32 offset = ctx.vertexBufferTable[tableIndex++]) {
                            OutputT value = *reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - (offset >> 3) * stride) >> attrShift;
                            OutputT value0 = ((value & mask) + *refBaseValueStream++) & mask;
                            OutputT value1 = ((value >> compShift & mask) + *refBaseValueStream++) & mask;
                            OutputT value2 = ((value >> (compShift * 2) & mask) + *refBaseValueStream++) & mask;
                            OutputT value3 = ((value >> (compShift * 3) & mask) + *refBaseValueStream++) & mask;
                            *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2)) | (value3 << (compShift * 3))) << attrShift) | (*output & keepMask);
                        } else {
                            OutputT value0 = *input++;
                            OutputT value1 = *input++;
                            OutputT value2 = *input++;
                            OutputT value3 = *input++;
                            *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2)) | (value3 << (compShift * 3))) << attrShift) | (*output & keepMask);
                        }
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                        tableIndex += groups->GetCopyCount();
                    }
                    ++groups;
                }
            } else {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        OutputT value0 = *input++;
                        OutputT value1 = *input++;
                        OutputT value2 = *input++;
                        OutputT value3 = *input++;
                        *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2)) | (value3 << (compShift * 3))) << attrShift) | (*output & keepMask);
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                    }
                    ++groups;
                }
            }
            return;
        }
        default:
            UNREACHABLE_DEFAULT_CASE
    }

    #undef MASK
    #undef MASK_ADD
}

template <size_t BitSize, size_t ComponentCount>
void DecodeRawWithTable(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert((BitSize == 2 && ComponentCount == 1) || BitSize == 8 || (BitSize == 10 && ComponentCount == 3) || BitSize == 16 || BitSize == 32, "Invalid bit size");
    static_assert(ComponentCount > 0 && ComponentCount < 5, "Invalid component count");

    u8* valueStream = inputStreams[0];
    u8* refBaseValueStream = inputStreams[1];
    const u32 stride = ctx.attrFlags[ctx.attrIndex] >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);
    
    u32 tableIndex = 0;

    for (; numGroups; --numGroups) {
        for (u32 i = groups->GetRawCount(); i != 0; --i) {
            if (u32 offset = ctx.vertexBufferTable[tableIndex++]) {
                if constexpr (BitSize == 2) {
                    *reinterpret_cast<u32*>(output) = (*reinterpret_cast<u32*>(output) & 0x3fffffff) | ((*reinterpret_cast<u32*>(output - (offset >> 3) * stride) + *refBaseValueStream++ * 0x40000000) & 0xc0000000);
                } else if constexpr (BitSize == 8) {
                    for (u32 j = 0; j < ComponentCount; ++j) {
                        output[j] = *refBaseValueStream++ + (output - (offset >> 3) * stride)[j];
                    }
                } else if constexpr (BitSize == 10) {
                    const u16 base0 = reinterpret_cast<u16*>(refBaseValueStream)[0];
                    const u16 base1 = reinterpret_cast<u16*>(refBaseValueStream)[1];
                    const u16 base2 = reinterpret_cast<u16*>(refBaseValueStream)[2];
                    const u32 value = reinterpret_cast<u32*>(output - (offset >> 3) * stride)[0];
                    *reinterpret_cast<u32*>(output) = ((value + base0) & 0x3ff) | ((value + base1 * 0x400) & 0xffc00) | ((value + base2 * 0x100000) & 0x3ff00000);
                    refBaseValueStream += 3 * sizeof(u16);
                } else if constexpr (BitSize == 16) {
                    for (u32 j = 0; j < ComponentCount; ++j) {
                        reinterpret_cast<u16*>(output)[j] = *reinterpret_cast<u16*>(refBaseValueStream) + reinterpret_cast<u16*>(output - (offset >> 3) * stride)[j];
                        refBaseValueStream += sizeof(u16);
                    }
                } else if constexpr (BitSize == 32) {
                    for (u32 j = 0; j < ComponentCount; ++j) {
                        reinterpret_cast<u32*>(output)[j] = *reinterpret_cast<u32*>(refBaseValueStream) + reinterpret_cast<u32*>(output - (offset >> 3) * stride)[j];
                        refBaseValueStream += sizeof(u32);
                    }
                } 
            } else {
                if constexpr (BitSize == 2) {
                    *reinterpret_cast<u32*>(output) = (*reinterpret_cast<u32*>(output) & 0x3fffffff) | (*valueStream++ << 0x1e);
                } else if constexpr (BitSize == 10) {
                    *reinterpret_cast<u32*>(output) = reinterpret_cast<u16*>(valueStream)[0] | (reinterpret_cast<u16*>(valueStream)[1] << 10) | (static_cast<u32>(reinterpret_cast<u16*>(valueStream)[2]) << 20);
                    valueStream += 3 * sizeof(u16);
                } else {
                    std::memcpy(output, valueStream, (BitSize >> 3) * ComponentCount);
                    valueStream += (BitSize >> 3) * ComponentCount;
                }
            }
            output += stride;
        }
        if (groups->vertexCount > 0xffff) {
            for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                if constexpr (BitSize == 2) {
                    *reinterpret_cast<u32*>(output) = (*reinterpret_cast<u32*>(output - groups->backRefOffset) & 0xc0000000) | (*reinterpret_cast<u32*>(output) & 0x3fffffff);
                } else if constexpr (BitSize == 10) {
                    *reinterpret_cast<u32*>(output) = *reinterpret_cast<u32*>(output - groups->backRefOffset) & 0x3fffffff;
                } else {
                    std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * ComponentCount);
                }
                output += stride;
            }
            tableIndex += groups->GetCopyCount();
        }
        ++groups;
    }
}

template <size_t BitSize>
void DecodeXOR1(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert(BitSize == 8 || BitSize == 10 || BitSize == 16, "Invalid bit size");

    u8* valueStream = inputStreams[0];
    u8* refBaseValueStream = inputStreams[1];
    const u32 stride = ctx.attrFlags[ctx.attrIndex] >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);
    
    u32 tableIndex = 0;

    for (; numGroups != 0; --numGroups) {
        for (u32 i = groups->GetRawCount(); i != 0; --i) {
            if (u32 offset = ctx.vertexBufferTable[tableIndex++]) {
                if constexpr (BitSize == 8) {
                    // if 1 this negates the value, otherwise it does nothing
                    // it doesn't look like the compiler generates this kind of code normally? ig they must've done it explicitly
                    const u8 value0 = offset & 1;
                    const u8 value1 = (offset >> 1) & 1;
                    const u8 value2 = (offset >> 2) & 1;
                    output[0] = *refBaseValueStream++ + ((output - (offset >> 3) * stride)[0] ^ -value0) + value0;
                    output[1] = *refBaseValueStream++ + ((output - (offset >> 3) * stride)[1] ^ -value1) + value1;
                    output[2] = *refBaseValueStream++ + ((output - (offset >> 3) * stride)[2] ^ -value2) + value2;
                } else if constexpr (BitSize == 10) {
                    const u32 value0 = offset & 1;
                    const u32 value1 = (offset >> 1) & 1;
                    const u32 value2 = (offset >> 2) & 1;
                    const u32 value = *reinterpret_cast<u32*>((output - (offset >> 3) * stride));
                    *reinterpret_cast<u32*>(output) = ((reinterpret_cast<u16*>(refBaseValueStream)[0] + (value ^ -value0) + value0) & 0x3ff) | 
                                                      (((reinterpret_cast<u16*>(refBaseValueStream)[1] + ((value >> 10) ^ -value1) + value1) & 0x3ff) << 10) |
                                                      (((reinterpret_cast<u16*>(refBaseValueStream)[2] + ((value >> 20) ^ -value2) + value2) & 0x3ff) << 20);
                    refBaseValueStream += 3 * sizeof(u16);
                } else if constexpr (BitSize == 16) {
                    const u16 value0 = offset & 1;
                    const u16 value1 = (offset >> 1) & 1;
                    const u16 value2 = (offset >> 2) & 1;
                    reinterpret_cast<u16*>(output)[0] = reinterpret_cast<u16*>(refBaseValueStream)[0] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[0] ^ -value0) + value0;
                    reinterpret_cast<u16*>(output)[1] = reinterpret_cast<u16*>(refBaseValueStream)[1] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[1] ^ -value1) + value1;
                    reinterpret_cast<u16*>(output)[2] = reinterpret_cast<u16*>(refBaseValueStream)[2] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[2] ^ -value2) + value2;
                    refBaseValueStream += 3 * sizeof(u16);
                } else {
                    static_assert(false, "you shouldn't be here");
                }
            } else {
                if constexpr (BitSize == 10) {
                    *reinterpret_cast<u32*>(output) = reinterpret_cast<u16*>(valueStream)[0] | (reinterpret_cast<u16*>(valueStream)[1] << 10) | (static_cast<u32>(reinterpret_cast<u16*>(valueStream)[2]) << 20);
                    valueStream += 3 * sizeof(u16);
                } else {
                    std::memcpy(output, valueStream, (BitSize >> 3) * 3);
                    valueStream += (BitSize >> 3) * 3;
                }
            }
            output += stride;
        }
        if (groups->vertexCount > 0xffff) {
            for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                if constexpr (BitSize == 10) {
                    *reinterpret_cast<u32*>(output) = *reinterpret_cast<u32*>(output - groups->backRefOffset) & 0x3fffffff;
                } else {
                    std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * 3);
                }
                output += stride;
            }
            tableIndex += groups->GetCopyCount();
        }
        ++groups;
    }
}

template <size_t BitSize>
void DecodeXOR2(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert(BitSize == 8 || BitSize == 10 || BitSize == 16, "Invalid bit size");

    u8* valueStream = inputStreams[0];
    u8* refBaseValueStream = inputStreams[1];
    const u32 stride = ctx.attrFlags[ctx.attrIndex] >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);
    
    u32 tableIndex = 0;

    for (; numGroups != 0; --numGroups) {
        for (u32 i = groups->GetRawCount(); i != 0; --i) {
            if (u32 offset = ctx.vertexBufferTable[tableIndex++]) {
                if constexpr (BitSize == 8) {
                    const u8 value0 = offset & 1;
                    const u8 value1 = (offset >> 1) & 1;
                    const u8 value2 = (offset >> 2) & 1;
                    output[0] = *refBaseValueStream++ + ((output - (offset >> 3) * stride)[0] ^ -value0);
                    output[1] = *refBaseValueStream++ + ((output - (offset >> 3) * stride)[1] ^ -value1);
                    output[2] = *refBaseValueStream++ + ((output - (offset >> 3) * stride)[2] ^ -value2);
                } else if constexpr (BitSize == 10) {
                    const u32 value0 = offset & 1;
                    const u32 value1 = (offset >> 1) & 1;
                    const u32 value2 = (offset >> 2) & 1;
                    const u32 value = *reinterpret_cast<u32*>((output - (offset >> 3) * stride));
                    *reinterpret_cast<u32*>(output) = ((reinterpret_cast<u16*>(refBaseValueStream)[0] + (value ^ -value0)) & 0x3ff) | 
                                                      (((reinterpret_cast<u16*>(refBaseValueStream)[1] + ((value >> 10) ^ -value1)) & 0x3ff) << 10) |
                                                      (((reinterpret_cast<u16*>(refBaseValueStream)[2] + ((value >> 20) ^ -value2)) & 0x3ff) << 20);
                    refBaseValueStream += 3 * sizeof(u16);
                } else if constexpr (BitSize == 16) {
                    const u16 value0 = offset & 1;
                    const u16 value1 = (offset >> 1) & 1;
                    const u16 value2 = (offset >> 2) & 1;
                    reinterpret_cast<u16*>(output)[0] = reinterpret_cast<u16*>(refBaseValueStream)[0] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[0] ^ -value0);
                    reinterpret_cast<u16*>(output)[1] = reinterpret_cast<u16*>(refBaseValueStream)[1] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[1] ^ -value1);
                    reinterpret_cast<u16*>(output)[2] = reinterpret_cast<u16*>(refBaseValueStream)[2] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[2] ^ -value2);
                    refBaseValueStream += 3 * sizeof(u16);
                } else {
                    static_assert(false, "you shouldn't be here");
                }
            } else {
                if constexpr (BitSize == 10) {
                    *reinterpret_cast<u32*>(output) = reinterpret_cast<u16*>(valueStream)[0] | (reinterpret_cast<u16*>(valueStream)[1] << 10) | (static_cast<u32>(reinterpret_cast<u16*>(valueStream)[2]) << 20);
                    valueStream += 3 * sizeof(u16);
                } else {
                    std::memcpy(output, valueStream, (BitSize >> 3) * 3);
                    valueStream += (BitSize >> 3) * 3;
                }
            }
            output += stride;
        }
        if (groups->vertexCount > 0xffff) {
            for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                if constexpr (BitSize == 10) {
                    *reinterpret_cast<u32*>(output) = *reinterpret_cast<u32*>(output - groups->backRefOffset) & 0x3fffffff;
                } else {
                    std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * 3);
                }
                output += stride;
            }
            tableIndex += groups->GetCopyCount();
        }
        ++groups;
    }
}

template <size_t BitSize>
void DecodeXOR3(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert(BitSize == 8 || BitSize == 16, "Invalid bit size");

    u8* valueStream = inputStreams[0];
    u8* refBaseValueStream = inputStreams[1];
    const u32 stride = ctx.attrFlags[ctx.attrIndex] >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);

    u32 tableIndex = 0;

    for (; numGroups != 0; --numGroups) {
        for (u32 i = groups->GetRawCount(); i != 0; --i) {
            if (u32 offset = ctx.vertexBufferTable[tableIndex++]) {
                if constexpr (BitSize == 8) {
                    s32 value0 = ((output - (offset >> 3) * stride)[0] ^ (offset & 1)) + (offset & 1);
                    s32 value1 = ((output - (offset >> 3) * stride)[1] ^ ((offset >> 1) & 1)) + ((offset >> 1) & 1);
                    if ((offset >> 2) & 1) {
                        const s32 value = ((value0 >> 0x1f) + (value1 >> 0x1f)) - ((value0 >> 0x1f ^ value0) + (value1 >> 0x1f ^ value1));
                        value0 += (value0 < 0) ? (-0x7f - value) : (0x7f + value);
                        value1 += (value1 < 0) ? (-0x7f - value) : (0x7f + value);
                    }
                    output[0] = *refBaseValueStream++ + value0;
                    output[1] = *refBaseValueStream++ + value1;
                } else if constexpr (BitSize == 16) {
                    s32 value0 = (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[0] ^ (offset & 1)) + (offset & 1);
                    s32 value1 = (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[1] ^ ((offset >> 1) & 1)) + ((offset >> 1) & 1);
                    if ((offset >> 2) & 1) {
                        const s32 value = ((value0 >> 0x1f) + (value1 >> 0x1f)) - ((value0 >> 0x1f ^ value0) + (value1 >> 0x1f ^ value1));
                        value0 += (value0 < 0) ? (-0x7fff - value) : (0x7fff + value);
                        value1 += (value1 < 0) ? (-0x7fff - value) : (0x7fff + value);
                    }
                    reinterpret_cast<u16*>(output)[0] = reinterpret_cast<u16*>(refBaseValueStream)[0] + value0;
                    reinterpret_cast<u16*>(output)[1] = reinterpret_cast<u16*>(refBaseValueStream)[1] + value1;
                    refBaseValueStream += 2 * sizeof(u16);
                } else {
                    static_assert(false, "you shouldn't be here");
                }
            } else {
                std::memcpy(output, valueStream, (BitSize >> 3) * 2);
                valueStream += (BitSize >> 3) * 2;
            }
            output += stride;
        }
        if (groups->vertexCount > 0xffff) {
            for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * 2);
                output += stride;
            }
            tableIndex += groups->GetCopyCount();
        }
        ++groups;
    }
}

template <size_t BitSize>
void DecodeXOR4(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert(BitSize == 8 || BitSize == 16, "Invalid bit size");

    u8* valueStream = inputStreams[0];
    u8* refBaseValueStream = inputStreams[1];
    const u32 stride = ctx.attrFlags[ctx.attrIndex] >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);
    
    u32 tableIndex = 0;

    for (; numGroups != 0; --numGroups) {
        for (u32 i = groups->GetRawCount(); i != 0; --i) {
            if (u32 offset = ctx.vertexBufferTable[tableIndex++]) {
                if constexpr (BitSize == 8) {
                    s32 value0 = (((output - (offset >> 3) * stride)[0] * 2 - 0xff) ^ (offset & 1)) + (offset & 1);
                    s32 value1 = (((output - (offset >> 3) * stride)[1] * 2 - 0xff) ^ ((offset >> 1) & 1)) + ((offset >> 1) & 1);
                    if ((offset >> 2) & 1) {
                        s32 value = ((value0 >> 0x1f) + (value1 >> 0x1f)) - ((value0 >> 0x1f ^ value0) + (value1 >> 0x1f ^ value1));
                        value0 += (value0 < 0) ? (-0x100 - value) : (0x100 + value);
                        value1 += (value1 < 0) ? (-0x100 - value) : (0x100 + value);
                    }
                    output[0] = *refBaseValueStream++ + ((static_cast<u8>(value0) >> 1) ^ 0x80);
                    output[1] = *refBaseValueStream++ + ((static_cast<u8>(value1) >> 1) ^ 0x80);
                } else if constexpr (BitSize == 16) {
                    s32 value0 = ((reinterpret_cast<u16*>(output - (offset >> 3) * stride)[0] * 2 - 0xffff) ^ (offset & 1)) + (offset & 1);
                    s32 value1 = ((reinterpret_cast<u16*>(output - (offset >> 3) * stride)[1] * 2 - 0xffff) ^ ((offset >> 1) & 1)) + ((offset >> 1) & 1);
                    if ((offset >> 2) & 1) {
                        const s32 value = ((value0 >> 0x1f) + (value1 >> 0x1f)) - ((value0 >> 0x1f ^ value0) + (value1 >> 0x1f ^ value1));
                        value0 += (value0 < 0) ? (-0x10000 - value) : (0x10000 + value);
                        value1 += (value1 < 0) ? (-0x10000 - value) : (0x10000 + value);
                    }
                    reinterpret_cast<u16*>(output)[0] = reinterpret_cast<u16*>(refBaseValueStream)[0] + ((static_cast<u16>(value0) >> 1) ^ 0x8000);
                    reinterpret_cast<u16*>(output)[1] = reinterpret_cast<u16*>(refBaseValueStream)[1] + ((static_cast<u16>(value1) >> 1) ^ 0x8000);
                    refBaseValueStream += 2 * sizeof(u16);
                } else {
                    static_assert(false, "you shouldn't be here");
                }
            } else {
                std::memcpy(output, valueStream, (BitSize >> 3) * 2);
                valueStream += (BitSize >> 3) * 2;
            }
            output += stride;
        }
        if (groups->vertexCount > 0xffff) {
            for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * 2);
                output += stride;
            }
            tableIndex += groups->GetCopyCount();
        }
        ++groups;
    }
}

template <size_t BitSize>
void DecodeXOR5(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert(BitSize == 16 || BitSize == 32, "Invalid bit size");

    u8* valueStream = inputStreams[0];
    u8* refBaseValueStream = inputStreams[1];
    const u32 stride = ctx.attrFlags[ctx.attrIndex] >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);
    
    u32 tableIndex = 0;

    for (; numGroups != 0; --numGroups) {
        for (u32 i = groups->GetRawCount(); i != 0; --i) {
            if (u32 offset = ctx.vertexBufferTable[tableIndex++]) {
                if constexpr (BitSize == 16) {
                    reinterpret_cast<u16*>(output)[0] = reinterpret_cast<u16*>(refBaseValueStream)[0] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[0] ^ (offset << 0xf));
                    reinterpret_cast<u16*>(output)[1] = reinterpret_cast<u16*>(refBaseValueStream)[1] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[1] ^ ((offset & 2) << 0xe));
                    reinterpret_cast<u16*>(output)[2] = reinterpret_cast<u16*>(refBaseValueStream)[2] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[2] ^ ((offset & 4) << 0xd));
                    refBaseValueStream += 3 * sizeof(u16);
                } else if constexpr (BitSize == 32) {
                    reinterpret_cast<u32*>(output)[0] = reinterpret_cast<u32*>(refBaseValueStream)[0] + (reinterpret_cast<u32*>(output - (offset >> 3) * stride)[0] ^ (offset << 0x1f));
                    reinterpret_cast<u32*>(output)[1] = reinterpret_cast<u32*>(refBaseValueStream)[1] + (reinterpret_cast<u32*>(output - (offset >> 3) * stride)[1] ^ ((offset & 2) << 0x1e));
                    reinterpret_cast<u32*>(output)[2] = reinterpret_cast<u32*>(refBaseValueStream)[2] + (reinterpret_cast<u32*>(output - (offset >> 3) * stride)[2] ^ ((offset & 4) << 0x1d));
                    refBaseValueStream += 3 * sizeof(u32);
                } else {
                    static_assert(false, "you shouldn't be here");
                }
            } else {
                std::memcpy(output, valueStream, (BitSize >> 3) * 3);
                valueStream += (BitSize >> 3) * 3;
            }
            output += stride;
        }
        if (groups->vertexCount > 0xffff) {
            for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * 3);
                output += stride;
            }
            tableIndex += groups->GetCopyCount();
        }
        ++groups;
    }
}

template <bool IsType2>
void DecodeXORCustomSize(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    const u32 flags = ctx.attrFlags[ctx.attrIndex];
    const u32 stride = flags >> 0x18;
    const u32 attrShift = flags >> 0x10 & 0xff;
    const u32 compShift = flags >> 0x8 & 0xff;
    const u64 compMask = ~(-1ll << compShift);
    u64 copyMask;
    if constexpr (IsType2) {
        copyMask = (2 << (compShift * 3 - 1)) - 1;
    } else {
        copyMask = (2 << (compShift * 2 - 1)) - 1;
    }
    const u64 keepMask = ~(copyMask << attrShift);
    u8* valueStream = inputStreams[0];
    u8* refBaseValueStream = inputStreams[1];
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);

    u32 tableIndex = 0;

    for (; numGroups != 0; --numGroups) {
        for (u32 i = groups->GetRawCount(); i != 0; --i) {
            if (u32 offset = ctx.vertexBufferTable[tableIndex++]) {
                if constexpr (IsType2) {
                    const u64 value = *reinterpret_cast<u64*>(output - (offset >> 3) * stride) >> attrShift;
                    const u8 v0 = offset & 1;
                    const u8 v1 = (offset >> 1) & 1;
                    const u8 v2 = (offset >> 2) & 1;
                    const u64 value0 = ((((value & compMask) ^ -v0) & compMask) + reinterpret_cast<u16*>(refBaseValueStream)[0]) & compMask;
                    const u64 value1 = (((((value >> compShift) & compMask) ^ -v1) & compMask) + reinterpret_cast<u16*>(refBaseValueStream)[1]) & compMask;
                    const u64 value2 = (((((value >> (compShift * 2)) & compMask) ^ -v2) & compMask) + reinterpret_cast<u16*>(refBaseValueStream)[2]) & compMask;
                    *reinterpret_cast<u64*>(output) = ((value0 | (value1 << compShift) | (value2 << (compShift * 2))) << attrShift) | (*reinterpret_cast<u64*>(output) & keepMask);
                    refBaseValueStream += 3 * sizeof(u16);
                } else {
                    const s32 add0 = -1 << (compShift - 1);
                    const s32 add1 = 1 << (compShift - 1);
                    const u32 value = *reinterpret_cast<u32*>(output - (offset >> 3) * stride) >> attrShift;
                    s32 value0 = ((((value & compMask) + add0) * 2 | 1) ^ (offset & 1)) + (offset & 1);
                    s32 value1 = (((((value >> compShift) & compMask) + add0) * 2 | 1) ^ ((offset >> 1) & 1)) + ((offset >> 1) & 1);
                    if ((offset >> 2) & 1) {
                        const s32 value = (value0 >> 0x1f) + (value1 >> 0x1f) + (1 << compShift) - ((value0 >> 0x1f ^ value0) + (value1 >> 0x1f ^ value1));
                        value0 += (value0 < 0) ? -value : value;
                        value1 += (value1 < 0) ? -value : value;
                    }
                    const u8 v0 = *refBaseValueStream++;
                    const u8 v1 = *refBaseValueStream++;
                    *reinterpret_cast<u32*>(output) = ((((add1 + (value0 >> 1) + v0) & compMask) | (((add1 + (value1 >> 1) + v1) & compMask) << compShift)) << attrShift) | (*reinterpret_cast<u32*>(output) & keepMask);
                }
            } else {
                if constexpr (IsType2) {
                    const u64 value0 = reinterpret_cast<u16*>(valueStream)[0];
                    const u64 value1 = reinterpret_cast<u16*>(valueStream)[1];
                    const u64 value2 = reinterpret_cast<u16*>(valueStream)[2];
                    *reinterpret_cast<u64*>(output) = ((value0 | (value1 << compShift) | (value2 << (compShift * 2))) << attrShift) | (*reinterpret_cast<u64*>(output) & keepMask);
                    valueStream += 3 * sizeof(u16);
                } else {
                    const u32 value0 = *valueStream++;
                    const u32 value1 = *valueStream++;
                    *reinterpret_cast<u32*>(output) = ((value0 | (value1 << compShift)) << attrShift) | (*reinterpret_cast<u32*>(output) & static_cast<u32>(keepMask));
                }
            }
            output += stride;
        }
        if (groups->vertexCount > 0xffff) {
            for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                if constexpr (IsType2) {
                    *reinterpret_cast<u64*>(output) = (((*reinterpret_cast<u64*>(output - groups->backRefOffset) >> attrShift) & copyMask) << attrShift) | (*reinterpret_cast<u64*>(output) & keepMask);
                } else {
                    *reinterpret_cast<u32*>(output) = (((*reinterpret_cast<u32*>(output - groups->backRefOffset) >> attrShift) & copyMask) << attrShift) | (*reinterpret_cast<u32*>(output) & keepMask);
                }
                output += stride;
            }
            tableIndex += groups->GetCopyCount();
        }
        ++groups;
    }
}

template <size_t BitSize, size_t ComponentCount, bool UseTable>
void DecodeDeltas(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert((BitSize == 2 && ComponentCount == 1) || BitSize == 8 || (BitSize == 10 && ComponentCount == 3) || BitSize == 16, "Invalid bit size");
    static_assert(ComponentCount > 0 && ComponentCount < 5, "Invalid component count");
    
    u8* valueStream = inputStreams[0];
    const u32 stride = ctx.attrFlags[ctx.attrIndex] >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);

    if constexpr (UseTable) {
        u8* refBaseValueStream = inputStreams[1];
        u32 tableIndex = 0;
        for (; numGroups != 0; --numGroups) {
            for (u32 i = groups->GetRawCount(); i != 0; --i) {
                if (u32 offset = ctx.vertexBufferTable[tableIndex]) {
                    if constexpr (BitSize == 2) {
                        *reinterpret_cast<u32*>(output) = ((*refBaseValueStream++ * 0x40000000 + *reinterpret_cast<u32*>(output - (offset >> 3) * stride)) & 0xc0000000) | (*reinterpret_cast<u32*>(output) & 0x3fffffff);
                    } else if constexpr (BitSize == 8) {
                        for (u32 j = 0; j < ComponentCount; ++j) {
                            output[j] = (output - (offset >> 3) * stride)[j] + *refBaseValueStream++;
                        }
                    } else if constexpr (BitSize == 10) {
                        const u32 value = *reinterpret_cast<u32*>(output - (offset >> 3) * stride);
                        *reinterpret_cast<u32*>(output) = ((reinterpret_cast<u16*>(refBaseValueStream)[0] + value) & 0x3ff) |
                                                          ((reinterpret_cast<u16*>(refBaseValueStream)[1] * 0x400 + value) & 0xffc00) |
                                                          ((static_cast<u32>(reinterpret_cast<u16*>(refBaseValueStream)[2]) * 0x100000 + value) & 0x3ff00000);
                        refBaseValueStream += 3 * sizeof(u16);
                    } else if constexpr (BitSize == 16) {
                        for (u32 j = 0; j < ComponentCount; ++j) {
                            reinterpret_cast<u16*>(output)[j] = reinterpret_cast<u16*>(output - (offset >> 3) * stride)[j] + *reinterpret_cast<u16*>(refBaseValueStream);
                            refBaseValueStream += sizeof(u16);
                        }
                    } else if constexpr (BitSize == 32) {
                        for (u32 j = 0; j < ComponentCount; ++j) {
                            reinterpret_cast<u32*>(output)[j] = reinterpret_cast<u32*>(output - (offset >> 3) * stride)[j] + *reinterpret_cast<u32*>(refBaseValueStream);
                            refBaseValueStream += sizeof(u32);
                        }
                    }
                } else {
                    if (tableIndex == 0) {
                        if constexpr (BitSize == 2) {
                            *reinterpret_cast<u32*>(output) = (*reinterpret_cast<u32*>(output) & 0x3fffffff) | (*valueStream++ << 0x1e);
                        } else if constexpr (BitSize == 10) {
                            *reinterpret_cast<u32*>(output) = reinterpret_cast<u16*>(valueStream)[0] | (reinterpret_cast<u16*>(valueStream)[1] << 10) | (static_cast<u32>(reinterpret_cast<u16*>(valueStream)[2]) << 20);
                            valueStream += 3 * sizeof(u16);
                        } else {
                            std::memcpy(output, valueStream, (BitSize >> 3) * ComponentCount);
                            valueStream += (BitSize >> 3) * ComponentCount;
                        }
                    } else {
                        if constexpr (BitSize == 2) {
                            *reinterpret_cast<u32*>(output) = ((*valueStream++ * 0x40000000 + *reinterpret_cast<u32*>(output - stride)) & 0xc0000000) | (*reinterpret_cast<u32*>(output) & 0x3fffffff);
                        } else if constexpr (BitSize == 8) {
                            for (u32 j = 0; j < ComponentCount; ++j) {
                                output[j] = (output - stride)[j] + *valueStream++;
                            }
                        } else if constexpr (BitSize == 10) {
                            const u32 value = *reinterpret_cast<u32*>(output - stride);
                            *reinterpret_cast<u32*>(output) = ((reinterpret_cast<u16*>(valueStream)[0] + value) & 0x3ff) |
                                                              ((reinterpret_cast<u16*>(valueStream)[1] * 0x400 + value) & 0xffc00) |
                                                              ((static_cast<u32>(reinterpret_cast<u16*>(valueStream)[2]) * 0x100000 + value) & 0x3ff00000);
                            valueStream += 3 * sizeof(u16);
                        } else if constexpr (BitSize == 16) {
                            for (u32 j = 0; j < ComponentCount; ++j) {
                                reinterpret_cast<u16*>(output)[j] = reinterpret_cast<u16*>(output - stride)[j] + *reinterpret_cast<u16*>(valueStream);
                                valueStream += sizeof(u16);
                            }
                        } else if constexpr (BitSize == 32) {
                            for (u32 j = 0; j < ComponentCount; ++j) {
                                reinterpret_cast<u32*>(output)[j] = reinterpret_cast<u32*>(output - stride)[j] + *reinterpret_cast<u32*>(valueStream);
                                valueStream += sizeof(u32);
                            }
                        }
                    }
                }
                output += stride;
                ++tableIndex;
            }
            if (groups->vertexCount > 0xffff) {
                for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                    if constexpr (BitSize == 2) {
                        *reinterpret_cast<u32*>(output) = (*reinterpret_cast<u32*>(output - groups->backRefOffset) & 0xc0000000) | (*reinterpret_cast<u32*>(output) & 0x3fffffff);
                    } else if constexpr (BitSize == 10) {
                        *reinterpret_cast<u32*>(output) = *reinterpret_cast<u32*>(output - groups->backRefOffset) & 0x3fffffff;
                    } else {
                        std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * ComponentCount);
                    }
                    output += stride;
                }
                tableIndex += groups->GetCopyCount();
            }
            ++groups;
        }
    } else {
        u32 tableIndex = 0; // this is just used to check if it's the first vertex or not
        for (; numGroups != 0; --numGroups) {
            for (u32 i = groups->GetRawCount(); i != 0; --i) {
                if (tableIndex == 0) {
                    if constexpr (BitSize == 2) {
                        *reinterpret_cast<u32*>(output) = (*reinterpret_cast<u32*>(output) & 0x3fffffff) | (*valueStream++ << 0x1e);
                    } else if constexpr (BitSize == 10) {
                        *reinterpret_cast<u32*>(output) = reinterpret_cast<u16*>(valueStream)[0] | (reinterpret_cast<u16*>(valueStream)[1] << 10) | (static_cast<u32>(reinterpret_cast<u16*>(valueStream)[2]) << 20);
                        valueStream += 3 * sizeof(u16);
                    } else {
                        std::memcpy(output, valueStream, (BitSize >> 3) * ComponentCount);
                        valueStream += (BitSize >> 3) * ComponentCount;
                    }
                } else {
                    if constexpr (BitSize == 2) {
                        *reinterpret_cast<u32*>(output) = ((*valueStream++ * 0x40000000 + *reinterpret_cast<u32*>(output - stride)) & 0xc0000000) | (*reinterpret_cast<u32*>(output) & 0x3fffffff);
                    } else if constexpr (BitSize == 8) {
                        for (u32 j = 0; j < ComponentCount; ++j) {
                            output[j] = (output - stride)[j] + *valueStream++;
                        }
                    } else if constexpr (BitSize == 10) {
                        const u32 value = *reinterpret_cast<u32*>(output - stride);
                        *reinterpret_cast<u32*>(output) = ((reinterpret_cast<u16*>(valueStream)[0] + value) & 0x3ff) |
                                                          ((reinterpret_cast<u16*>(valueStream)[1] * 0x400 + value) & 0xffc00) |
                                                          ((static_cast<u32>(reinterpret_cast<u16*>(valueStream)[2]) * 0x100000 + value) & 0x3ff00000);
                        valueStream += 3 * sizeof(u16);
                    } else if constexpr (BitSize == 16) {
                        for (u32 j = 0; j < ComponentCount; ++j) {
                            reinterpret_cast<u16*>(output)[j] = reinterpret_cast<u16*>(output - stride)[j] + *reinterpret_cast<u16*>(valueStream);
                            valueStream += sizeof(u16);
                        }
                    } else if constexpr (BitSize == 32) {
                        for (u32 j = 0; j < ComponentCount; ++j) {
                            reinterpret_cast<u32*>(output)[j] = reinterpret_cast<u32*>(output - stride)[j] + *reinterpret_cast<u32*>(valueStream);
                            valueStream += sizeof(u32);
                        }
                    }
                }
                output += stride;
                ++tableIndex;
            }
            if (groups->vertexCount > 0xffff) {
                for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                    if constexpr (BitSize == 2) {
                        *reinterpret_cast<u32*>(output) = (*reinterpret_cast<u32*>(output - groups->backRefOffset) & 0xc0000000) | (*reinterpret_cast<u32*>(output) & 0x3fffffff);
                    } else if constexpr (BitSize == 10) {
                        *reinterpret_cast<u32*>(output) = *reinterpret_cast<u32*>(output - groups->backRefOffset) & 0x3fffffff;
                    } else {
                        std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * ComponentCount);
                    }
                    output += stride;
                }
                tableIndex += groups->GetCopyCount();
            }
            ++groups;
        }
    }
}

template <typename InputT, typename OutputT, bool UseTable>
void DecodeDeltasCustomSize(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert(std::is_same_v<InputT, u8> || std::is_same_v<InputT, u16>, "Invalid input type");
    static_assert(std::is_same_v<OutputT, u32> || std::is_same_v<OutputT, u64>, "Invalid output type");
    
    #define MASK(VALUE, SHIFT, MASK) ((((VALUE) << (SHIFT)) & (MASK)) >> (SHIFT))
    #define MASK_ADD(VALUE, SHIFT, MASK, ADD) OutputT(OutputT(u32((((VALUE) >> (SHIFT)) & (MASK)) + (ADD)) & u32(MASK)) << (SHIFT))
    
    const u32 flags = ctx.attrFlags[ctx.attrIndex];
    const u32 stride = flags >> 0x18;
    const u32 attrShift = flags >> 0x10 & 0xff;
    const u32 compShift = flags >> 0x8 & 0xff;
    const OutputT copyMask = (2 << (compShift * (flags & 7) - 1)) - 1;
    const OutputT keepMask = ~(copyMask << attrShift);
    InputT* input = reinterpret_cast<InputT*>(inputStreams[0]);
    OutputT* output = reinterpret_cast<OutputT*>(ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride));

    switch (flags & 7) { // component count
        case 1: {
            u32 tableIndex = 0;
            const OutputT mask = ~(-1ll << compShift);
            if constexpr (UseTable) {
                InputT* refBaseValueStream = reinterpret_cast<InputT*>(inputStreams[1]);

                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        if (u32 offset = ctx.vertexBufferTable[tableIndex]) {
                            *output = MASK_ADD(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - (offset >> 3) * stride), attrShift, mask, *refBaseValueStream++) | (*output & keepMask);
                        } else {
                            if (tableIndex == 0) {
                                *output = (static_cast<OutputT>(*input++) << attrShift) | (*output & keepMask);
                            } else {
                                *output = MASK_ADD(*reinterpret_cast<InputT*>(reinterpret_cast<u8*>(output) - stride), attrShift, mask, *input++) | (*output & keepMask);
                            }
                        }
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);;
                        ++tableIndex;
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                        tableIndex += groups->GetCopyCount();
                    }
                    ++groups;
                }
            } else {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        if (tableIndex == 0) {
                            *output = (static_cast<OutputT>(*input++) << attrShift) | (*output & keepMask);
                        } else {
                            *output = MASK_ADD(*reinterpret_cast<InputT*>(reinterpret_cast<u8*>(output) - stride), attrShift, mask, *input++) | (*output & keepMask);
                        }
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        ++tableIndex;
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                        tableIndex += groups->GetCopyCount();
                    }
                    ++groups;
                }
            }
            return;
        }
        case 2: {
            u32 tableIndex = 0;
            const OutputT mask = ~(-1ll << compShift);
            if constexpr (UseTable) {
                InputT* refBaseValueStream = reinterpret_cast<InputT*>(inputStreams[1]);

                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        if (u32 offset = ctx.vertexBufferTable[tableIndex]) {
                            OutputT value = *reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - (offset >> 3) * stride);
                            OutputT value0 = MASK_ADD(value, attrShift, mask, *refBaseValueStream++);
                            OutputT value1 = MASK_ADD(value >> compShift, attrShift, mask, *refBaseValueStream++);
                            *output = ((value0 | (value1 << compShift)) << attrShift) | (*output & keepMask);
                        } else {
                            if (tableIndex == 0) {
                                OutputT value0 = *input++;
                                OutputT value1 = *input++;
                                *output = ((value0 | (value1 << compShift)) << attrShift) | (*output & keepMask);
                            } else {
                                OutputT value = *reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - stride);
                                OutputT value0 = MASK_ADD(value, attrShift, mask, *input++);
                                OutputT value1 = MASK_ADD(value >> compShift, attrShift, mask, *input++);
                                *output = ((value0 | (value1 << compShift)) << attrShift) | (*output & keepMask);
                            }
                        }
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        ++tableIndex;
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                        tableIndex += groups->GetCopyCount();
                    }
                    ++groups;
                }
            } else {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        if (tableIndex == 0) {
                            OutputT value0 = *input++;
                            OutputT value1 = *input++;
                            *output = ((value0 | (value1 << compShift)) << attrShift) | (*output & keepMask);
                        } else {
                            OutputT value = *reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - stride);
                            OutputT value0 = MASK_ADD(value, attrShift, mask, *input++);
                            OutputT value1 = MASK_ADD(value >> compShift, attrShift, mask, *input++);
                            *output = ((value0 | (value1 << compShift)) << attrShift) | (*output & keepMask);
                        }
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        ++tableIndex;
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                        tableIndex += groups->GetCopyCount();
                    }
                    ++groups;
                }
            }
            return;
        }
        case 3: {
            u32 tableIndex = 0;
            const OutputT mask = ~(-1ll << compShift);
            if constexpr (UseTable) {
                InputT* refBaseValueStream = reinterpret_cast<InputT*>(inputStreams[1]);

                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        if (u32 offset = ctx.vertexBufferTable[tableIndex]) {
                            OutputT value = *reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - (offset >> 3) * stride);
                            OutputT value0 = MASK_ADD(value, attrShift, mask, *refBaseValueStream++);
                            OutputT value1 = MASK_ADD(value >> compShift, attrShift, mask, *refBaseValueStream++);
                            OutputT value2 = MASK_ADD(value >> (compShift * 2), attrShift, mask, *refBaseValueStream++);
                            *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2))) << attrShift) | (*output & keepMask);
                        } else {
                            if (tableIndex == 0) {
                                OutputT value0 = *input++;
                                OutputT value1 = *input++;
                                OutputT value2 = *input++;
                                *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2))) << attrShift) | (*output & keepMask);
                            } else {
                                OutputT value = *reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - stride);
                                OutputT value0 = MASK_ADD(value, attrShift, mask, *input++);
                                OutputT value1 = MASK_ADD(value >> compShift, attrShift, mask, *input++);
                                OutputT value2 = MASK_ADD(value >> (compShift * 2), attrShift, mask, *input++);
                                *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2))) << attrShift) | (*output & keepMask);
                            }
                        }
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        ++tableIndex;
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                        tableIndex += groups->GetCopyCount();
                    }
                    ++groups;
                }
            } else {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        if (tableIndex == 0) {
                            OutputT value0 = *input++;
                            OutputT value1 = *input++;
                            OutputT value2 = *input++;
                            *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2))) << attrShift) | (*output & keepMask);
                        } else {
                            OutputT value = *reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - stride);
                            OutputT value0 = MASK_ADD(value, attrShift, mask, *input++);
                            OutputT value1 = MASK_ADD(value >> compShift, attrShift, mask, *input++);
                            OutputT value2 = MASK_ADD(value >> (compShift * 2), attrShift, mask, *input++);
                            *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2))) << attrShift) | (*output & keepMask);
                        }
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        ++tableIndex;
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                        tableIndex += groups->GetCopyCount();
                    }
                    ++groups;
                }
            }
            return;
        }
        case 4: {
            u32 tableIndex = 0;
            const OutputT mask = ~(-1ll << compShift);
            if constexpr (UseTable) {
                InputT* refBaseValueStream = reinterpret_cast<InputT*>(inputStreams[1]);

                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        if (u32 offset = ctx.vertexBufferTable[tableIndex]) {
                            OutputT value = *reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - (offset >> 3) * stride);
                            OutputT value0 = MASK_ADD(value, attrShift, mask, *refBaseValueStream++);
                            OutputT value1 = MASK_ADD(value >> compShift, attrShift, mask, *refBaseValueStream++);
                            OutputT value2 = MASK_ADD(value >> (compShift * 2), attrShift, mask, *refBaseValueStream++);
                            OutputT value3 = MASK_ADD(value >> (compShift * 3), attrShift, mask, *refBaseValueStream++);
                            *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2)) | (value3 << (compShift * 3))) << attrShift) | (*output & keepMask);
                        } else {
                            if (tableIndex == 0) {
                                OutputT value0 = *input++;
                                OutputT value1 = *input++;
                                OutputT value2 = *input++;
                                OutputT value3 = *input++;
                                *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2)) | (value3 << (compShift * 3))) << attrShift) | (*output & keepMask);
                            } else {
                                OutputT value = *reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - stride);
                                OutputT value0 = MASK_ADD(value, attrShift, mask, *input++);
                                OutputT value1 = MASK_ADD(value >> compShift, attrShift, mask, *input++);
                                OutputT value2 = MASK_ADD(value >> (compShift * 2), attrShift, mask, *input++);
                                OutputT value3 = MASK_ADD(value >> (compShift * 3), attrShift, mask, *input++);
                                *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2)) | (value3 << (compShift * 3))) << attrShift) | (*output & keepMask);
                            }
                        }
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        ++tableIndex;
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                        tableIndex += groups->GetCopyCount();
                    }
                    ++groups;
                }
            } else {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetRawCount(); i != 0; --i) {
                        if (tableIndex == 0) {
                            OutputT value0 = *input++;
                            OutputT value1 = *input++;
                            OutputT value2 = *input++;
                            OutputT value3 = *input++;
                            *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2)) | (value3 << (compShift * 3))) << attrShift) | (*output & keepMask);
                        } else {
                            OutputT value = *reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - stride);
                            OutputT value0 = MASK_ADD(value, attrShift, mask, *input++);
                            OutputT value1 = MASK_ADD(value >> compShift, attrShift, mask, *input++);
                            OutputT value2 = MASK_ADD(value >> (compShift * 2), attrShift, mask, *input++);
                            OutputT value3 = MASK_ADD(value >> (compShift * 3), attrShift, mask, *input++);
                            *output = ((value0 | (value1 << compShift) | (value2 << (compShift * 2)) | (value3 << (compShift * 3))) << attrShift) | (*output & keepMask);
                        }
                        output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        ++tableIndex;
                    }
                    if (groups->vertexCount > 0xffff) {
                        for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                            *output = MASK(*reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                            output = reinterpret_cast<OutputT*>(reinterpret_cast<u8*>(output) + stride);
                        }
                        tableIndex += groups->GetCopyCount();
                    }
                    ++groups;
                }
            }
            return;
        }
        default:
            UNREACHABLE_DEFAULT_CASE
    }

    #undef MASK
    #undef MASK_ADD
}

template <size_t BitSize, bool UseTable>
void DecodeTriangleDeltas(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert(BitSize == 8 || BitSize == 10 || BitSize == 16, "Invalid bit size");

    u8* deltaValues = inputStreams[0];
    u8* trigDeltas = inputStreams[1];
    const u32 stride = ctx.attrFlags[ctx.attrIndex] >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);

    u32 tableIndex = 0;
    if constexpr (UseTable) {
        u8* refBaseValueStream = inputStreams[2];
        for (; numGroups != 0; --numGroups) {
            for (u32 i = groups->GetRawCount(); i != 0; --i) {
                if (u32 offset = ctx.vertexBufferTable[tableIndex]) {
                    if constexpr (BitSize == 8) {
                        const u8 value0 = offset & 1;
                        const u8 value1 = (offset >> 1) & 1;
                        const u8 value2 = (offset >> 2) & 1;
                        output[0] = *refBaseValueStream++ + ((output - (offset >> 3) * stride)[0] ^ -value0);
                        output[1] = *refBaseValueStream++ + ((output - (offset >> 3) * stride)[1] ^ -value1);
                        output[2] = *refBaseValueStream++ + ((output - (offset >> 3) * stride)[2] ^ -value2);
                    } else if constexpr (BitSize == 10) {
                        const u32 value0 = offset & 1;
                        const u32 value1 = (offset >> 1) & 1;
                        const u32 value2 = (offset >> 2) & 1;
                        const u32 value = *reinterpret_cast<u32*>((output - (offset >> 3) * stride));
                        *reinterpret_cast<u32*>(output) = ((reinterpret_cast<u16*>(refBaseValueStream)[0] + (value ^ -value0)) & 0x3ff) | 
                                                          (((reinterpret_cast<u16*>(refBaseValueStream)[1] + ((value >> 10) ^ -value1)) & 0x3ff) << 10) |
                                                          (((reinterpret_cast<u16*>(refBaseValueStream)[2] + ((value >> 20) ^ -value2)) & 0x3ff) << 20);
                        refBaseValueStream += 3 * sizeof(u16);
                    } else if constexpr (BitSize == 16) {
                        const u16 value0 = offset & 1;
                        const u16 value1 = (offset >> 1) & 1;
                        const u16 value2 = (offset >> 2) & 1;
                        reinterpret_cast<u16*>(output)[0] = reinterpret_cast<u16*>(refBaseValueStream)[0] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[0] ^ -value0);
                        reinterpret_cast<u16*>(output)[1] = reinterpret_cast<u16*>(refBaseValueStream)[1] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[1] ^ -value1);
                        reinterpret_cast<u16*>(output)[2] = reinterpret_cast<u16*>(refBaseValueStream)[2] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[2] ^ -value2);
                        refBaseValueStream += 3 * sizeof(u16);
                    } else {
                        static_assert(false, "you shouldn't be here");
                    }
                } else {
                    const u64 value = ctx.indexBufferTable[tableIndex];
                    if (value & 0x3fffff) {
                        const u32 index0 = value & 0x3fffff;
                        const u32 index1 = value >> 0x16 & 0x1fffff;
                        const u32 index2 = value >> 0x2b;
                        if constexpr (BitSize == 8) {
                            for (u32 j = 0; j < 3; ++j) {
                                output[j] = *trigDeltas++ + (output - index1 * stride)[j] + (output - index2 * stride)[j] - (output - index0 * stride)[j];
                            }
                        } else if constexpr (BitSize == 10) {
                            const u32 v0 = *reinterpret_cast<u32*>(output - index0 * stride);
                            const u32 v1 = *reinterpret_cast<u32*>(output - index1 * stride);
                            const u32 v2 = *reinterpret_cast<u32*>(output - index2 * stride);
                            const u16 value0 = (reinterpret_cast<u16*>(trigDeltas)[0] + (v1 & 0x3ff) + (v2 & 0x3ff) - (v0 & 0x3ff)) & 0x3ff;
                            const u16 value1 = (reinterpret_cast<u16*>(trigDeltas)[1] + ((v1 >> 10) & 0x3ff) + ((v2 >> 10) & 0x3ff) - ((v0 >> 10) & 0x3ff)) & 0x3ff;
                            const u16 value2 = (reinterpret_cast<u16*>(trigDeltas)[2] + ((v1 >> 20) & 0x3ff) + ((v2 >> 20) & 0x3ff) - ((v0 >> 20) & 0x3ff)) & 0x3ff;
                            *reinterpret_cast<u32*>(output) = value0 | (value1 << 10) | (value2 << 20);
                            trigDeltas += 3 * sizeof(u16);
                        } else if constexpr (BitSize == 16) {
                            for (u32 j = 0; j < 3; ++j) {
                                reinterpret_cast<u16*>(output)[j] = *reinterpret_cast<u16*>(trigDeltas)
                                                                    + reinterpret_cast<u16*>(output - index1 * stride)[j]
                                                                    + reinterpret_cast<u16*>(output - index2 * stride)[j]
                                                                    - reinterpret_cast<u16*>(output - index0 * stride)[j];
                                trigDeltas += sizeof(u16);
                            }
                        } else {
                            static_assert(false, "you shouldn't be here");
                        }
                    } else {
                        if constexpr (BitSize == 8) {
                            if (tableIndex == 0) {
                                for (u32 j = 0; j < 3; ++j) {
                                    output[j] = *deltaValues++;
                                }
                            } else {
                                for (u32 j = 0; j < 3; ++j) {
                                    output[j] = *deltaValues++ + (output - stride)[j];
                                }
                            }
                        } else if constexpr (BitSize == 10) {
                            if (tableIndex == 0) {
                                const u16 value0 = reinterpret_cast<u16*>(deltaValues)[0];
                                const u16 value1 = reinterpret_cast<u16*>(deltaValues)[1];
                                const u16 value2 = reinterpret_cast<u16*>(deltaValues)[2];
                                *reinterpret_cast<u32*>(output) = value0 | (value1 << 10) | (value2 << 20);
                                deltaValues += 3 * sizeof(u16);
                            } else {
                                const u16 value0 = reinterpret_cast<u16*>(deltaValues)[0];
                                const u16 value1 = reinterpret_cast<u16*>(deltaValues)[1];
                                const u16 value2 = reinterpret_cast<u16*>(deltaValues)[2];
                                const u32 base = *reinterpret_cast<u32*>(output - stride);
                                *reinterpret_cast<u32*>(output) = (value0 + (base & 0x3ff)) | ((value1 + (base >> 10 & 0x3ff)) << 10) | ((value2 + (base >> 20 & 0x3ff)) << 20);
                                deltaValues += 3 * sizeof(u16);
                            }
                        } else if constexpr (BitSize == 16) {
                            if (tableIndex == 0) {
                                for (u32 j = 0; j < 3; ++j) {
                                    reinterpret_cast<u16*>(output)[j] = *reinterpret_cast<u16*>(deltaValues);
                                    deltaValues += sizeof(u16);
                                }
                            } else {
                                for (u32 j = 0; j < 3; ++j) {
                                    reinterpret_cast<u16*>(output)[j] = *reinterpret_cast<u16*>(deltaValues) + reinterpret_cast<u16*>(output - stride)[j];
                                    deltaValues += sizeof(u16);
                                }
                            }
                        } else {
                            static_assert(false, "you shouldn't be here");
                        }
                    }
                }
                output += stride;
                ++tableIndex;
            }
            if (groups->vertexCount > 0xffff) {
                for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                    if constexpr (BitSize == 10) {
                        *reinterpret_cast<u32*>(output) = *reinterpret_cast<u32*>(output - groups->backRefOffset); // they copy the entire thing this time
                    } else {
                        std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * 3);
                    }
                    output += stride;
                }
                tableIndex += groups->GetCopyCount();
            }
            ++groups;
        }
    } else {
        for (; numGroups != 0; --numGroups) {
            for (u32 i = groups->GetRawCount(); i != 0; --i) {
                const u64 value = ctx.indexBufferTable[tableIndex];
                if (value & 0x3fffff) {
                    const u32 index0 = value & 0x3fffff;
                    const u32 index1 = value >> 0x16 & 0x1fffff;
                    const u32 index2 = value >> 0x2b;
                    if constexpr (BitSize == 8) {
                        for (u32 j = 0; j < 3; ++j) {
                            output[j] = *trigDeltas++ + (output - index1 * stride)[j] + (output - index2 * stride)[j] - (output - index0 * stride)[j];
                        }
                    } else if constexpr (BitSize == 10) {
                        const u32 v0 = *reinterpret_cast<u32*>(output - index0 * stride);
                        const u32 v1 = *reinterpret_cast<u32*>(output - index1 * stride);
                        const u32 v2 = *reinterpret_cast<u32*>(output - index2 * stride);
                        const u16 value0 = (reinterpret_cast<u16*>(trigDeltas)[0] + (v1 & 0x3ff) + (v2 & 0x3ff) - (v0 & 0x3ff)) & 0x3ff;
                        const u16 value1 = (reinterpret_cast<u16*>(trigDeltas)[1] + ((v1 >> 10) & 0x3ff) + ((v2 >> 10) & 0x3ff) - ((v0 >> 10) & 0x3ff)) & 0x3ff;
                        const u16 value2 = (reinterpret_cast<u16*>(trigDeltas)[2] + ((v1 >> 20) & 0x3ff) + ((v2 >> 20) & 0x3ff) - ((v0 >> 20) & 0x3ff)) & 0x3ff;
                        *reinterpret_cast<u32*>(output) = value0 | (value1 << 10) | (value2 << 20);
                        trigDeltas += 3 * sizeof(u16);
                    } else if constexpr (BitSize == 16) {
                        for (u32 j = 0; j < 3; ++j) {
                            reinterpret_cast<u16*>(output)[j] = *reinterpret_cast<u16*>(trigDeltas)
                                                                + reinterpret_cast<u16*>(output - index1 * stride)[j]
                                                                + reinterpret_cast<u16*>(output - index2 * stride)[j]
                                                                - reinterpret_cast<u16*>(output - index0 * stride)[j];
                            trigDeltas += sizeof(u16);
                        }
                    } else {
                        static_assert(false, "you shouldn't be here");
                    }
                } else {
                    if constexpr (BitSize == 8) {
                        if (tableIndex == 0) {
                            for (u32 j = 0; j < 3; ++j) {
                                output[j] = *deltaValues++;
                            }
                        } else {
                            for (u32 j = 0; j < 3; ++j) {
                                output[j] = *deltaValues++ + (output - stride)[j];
                            }
                        }
                    } else if constexpr (BitSize == 10) {
                        if (tableIndex == 0) {
                            const u16 value0 = reinterpret_cast<u16*>(deltaValues)[0];
                            const u16 value1 = reinterpret_cast<u16*>(deltaValues)[1];
                            const u16 value2 = reinterpret_cast<u16*>(deltaValues)[2];
                            *reinterpret_cast<u32*>(output) = value0 | (value1 << 10) | (value2 << 20);
                            deltaValues += 3 * sizeof(u16);
                        } else {
                            const u16 value0 = reinterpret_cast<u16*>(deltaValues)[0];
                            const u16 value1 = reinterpret_cast<u16*>(deltaValues)[1];
                            const u16 value2 = reinterpret_cast<u16*>(deltaValues)[2];
                            const u32 base = *reinterpret_cast<u32*>(output - stride);
                            *reinterpret_cast<u32*>(output) = (value0 + (base & 0x3ff)) | ((value1 + (base >> 10 & 0x3ff)) << 10) | ((value2 + (base >> 20 & 0x3ff)) << 20);
                            deltaValues += 3 * sizeof(u16);
                        }
                    } else if constexpr (BitSize == 16) {
                        if (tableIndex == 0) {
                            for (u32 j = 0; j < 3; ++j) {
                                reinterpret_cast<u16*>(output)[j] = *reinterpret_cast<u16*>(deltaValues);
                                deltaValues += sizeof(u16);
                            }
                        } else {
                            for (u32 j = 0; j < 3; ++j) {
                                reinterpret_cast<u16*>(output)[j] = *reinterpret_cast<u16*>(deltaValues) + reinterpret_cast<u16*>(output - stride)[j];
                                deltaValues += sizeof(u16);
                            }
                        }
                    } else {
                        static_assert(false, "you shouldn't be here");
                    }
                }
                output += stride;
                ++tableIndex;
            }
            if (groups->vertexCount > 0xffff) {
                for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                    if constexpr (BitSize == 10) {
                        *reinterpret_cast<u32*>(output) = *reinterpret_cast<u32*>(output - groups->backRefOffset); // they copy the entire thing this time
                    } else {
                        std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * 3);
                    }
                    output += stride;
                }
                tableIndex += groups->GetCopyCount();
            }
            ++groups;
        }
    }
}

template <bool UseTable>
void DecodeTriangleDeltasCustomSize(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    u16* deltaValues = reinterpret_cast<u16*>(inputStreams[0]);
    u16* trigDeltas = reinterpret_cast<u16*>(inputStreams[1]);
    const u32 flags = ctx.attrFlags[ctx.attrIndex];
    const u32 stride = flags >> 0x18;
    const u32 attrShift = flags >> 0x10 & 0xff;
    const u32 compSize = flags >> 8 & 0xff;
    const u32 compMask = ~(-1 << compSize);
    const u64 copyMask = ~(-1ll << (compSize * 3));
    const u64 keepMask = ~(copyMask << attrShift);
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);

    u32 tableIndex = 0;
    if constexpr (UseTable) {
        u16* refBaseValueStream = reinterpret_cast<u16*>(inputStreams[2]);
        for (; numGroups != 0; --numGroups) {
            for (u32 i = groups->GetRawCount(); i != 0; --i) {
                if (u32 offset = ctx.vertexBufferTable[tableIndex]) {
                    const u64 value = *reinterpret_cast<u64*>(output - (offset >> 3) * stride) >> attrShift;
                    const u8 v0 = offset & 1;
                    const u8 v1 = (offset >> 1) & 1;
                    const u8 v2 = (offset >> 2) & 1;
                    const u64 value0 = ((((value & compMask) ^ -v0) & compMask) + reinterpret_cast<u16*>(refBaseValueStream)[0]) & compMask;
                    const u64 value1 = (((((value >> compSize) & compMask) ^ -v1) & compMask) + reinterpret_cast<u16*>(refBaseValueStream)[1]) & compMask;
                    const u64 value2 = (((((value >> (compSize * 2)) & compMask) ^ -v2) & compMask) + reinterpret_cast<u16*>(refBaseValueStream)[2]) & compMask;
                    *reinterpret_cast<u64*>(output) = ((value0 | (value1 << compSize) | (value2 << (compSize * 2))) << attrShift) | (*reinterpret_cast<u64*>(output) & keepMask);
                    refBaseValueStream += 3 * sizeof(u16);
                } else {
                    const u64 value = ctx.indexBufferTable[tableIndex];
                    if (value & 0x3fffff) {
                        const u32 index0 = value & 0x3fffff;
                        const u32 index1 = value >> 0x16 & 0x1fffff;
                        const u32 index2 = value >> 0x2b;
                        const u64 v0 = *reinterpret_cast<u64*>(output - index0 * stride) >> attrShift;
                        const u64 v1 = *reinterpret_cast<u64*>(output - index1 * stride) >> attrShift;
                        const u64 v2 = *reinterpret_cast<u64*>(output - index2 * stride) >> attrShift;
                        const u64 value0 = (*trigDeltas++ + ((v1 & compMask) + (v2 & compMask) - (v0 & compMask))) & compMask;
                        const u64 value1 = (*trigDeltas++ + (((v1 >> compSize) & compMask) + ((v2 >> compSize) & compMask) - ((v0 >> compSize) & compMask))) & compMask;
                        const u64 value2 = (*trigDeltas++ + (((v1 >> (compSize * 2)) & compMask) + ((v2 >> (compSize * 2)) & compMask) - ((v0 >> (compSize * 2)) & compMask))) & compMask;
                        *reinterpret_cast<u64*>(output) = ((value0 | (value1 << compSize) | (value2 << (compSize * 2))) << attrShift) | (*reinterpret_cast<u64*>(output) & keepMask);
                    } else {
                        if (tableIndex == 0) {
                            const u64 value0 = *deltaValues++ & compMask;
                            const u64 value1 = *deltaValues++ & compMask;
                            const u64 value2 = *deltaValues++ & compMask;
                            *reinterpret_cast<u64*>(output) = ((value0 | (value1 << compSize) | (value2 << (compSize * 2))) << attrShift) | (*reinterpret_cast<u64*>(output) & keepMask);
                        } else {
                            const u64 prev = *reinterpret_cast<u64*>(output - stride) >> attrShift;
                            const u64 value0 = (*deltaValues++ + (prev & compMask)) & compMask;
                            const u64 value1 = (*deltaValues++ + ((prev >> compSize) & compMask)) & compMask;
                            const u64 value2 = (*deltaValues++ + ((prev >> (compSize * 2)) & compMask)) & compMask;
                            *reinterpret_cast<u64*>(output) = ((value0 | (value1 << compSize) | (value2 << (compSize * 2))) << attrShift) | (*reinterpret_cast<u64*>(output) & keepMask);
                        }
                    }
                }
                output += stride;
                ++tableIndex;
            }
            if (groups->vertexCount > 0xffff) {
                for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                    *reinterpret_cast<u64*>(output) = (((*reinterpret_cast<u64*>(output - groups->backRefOffset) >> attrShift) & copyMask) << attrShift) | (*reinterpret_cast<u64*>(output) & keepMask);
                    output += stride;
                }
                tableIndex += groups->GetCopyCount();
            }
            ++groups;
        }
    } else {
        for (; numGroups != 0; --numGroups) {
            for (u32 i = groups->GetRawCount(); i != 0; --i) {
                const u64 value = ctx.indexBufferTable[tableIndex];
                if (value & 0x3fffff) {
                    const u32 index0 = value & 0x3fffff;
                    const u32 index1 = value >> 0x16 & 0x1fffff;
                    const u32 index2 = value >> 0x2b;
                    const u64 v0 = *reinterpret_cast<u64*>(output - index0 * stride) >> attrShift;
                    const u64 v1 = *reinterpret_cast<u64*>(output - index1 * stride) >> attrShift;
                    const u64 v2 = *reinterpret_cast<u64*>(output - index2 * stride) >> attrShift;
                    const u64 value0 = (*trigDeltas++ + ((v1 & compMask) + (v2 & compMask) - (v0 & compMask))) & compMask;
                    const u64 value1 = (*trigDeltas++ + (((v1 >> compSize) & compMask) + ((v2 >> compSize) & compMask) - ((v0 >> compSize) & compMask))) & compMask;
                    const u64 value2 = (*trigDeltas++ + (((v1 >> (compSize * 2)) & compMask) + ((v2 >> (compSize * 2)) & compMask) - ((v0 >> (compSize * 2)) & compMask))) & compMask;
                    *reinterpret_cast<u64*>(output) = ((value0 | (value1 << compSize) | (value2 << (compSize * 2))) << attrShift) | (*reinterpret_cast<u64*>(output) & keepMask);
                } else {
                    if (tableIndex == 0) {
                        const u64 value0 = *deltaValues++ & compMask;
                        const u64 value1 = *deltaValues++ & compMask;
                        const u64 value2 = *deltaValues++ & compMask;
                        *reinterpret_cast<u64*>(output) = ((value0 | (value1 << compSize) | (value2 << (compSize * 2))) << attrShift) | (*reinterpret_cast<u64*>(output) & keepMask);
                    } else {
                        const u64 prev = *reinterpret_cast<u64*>(output - stride) >> attrShift;
                        const u64 value0 = (*deltaValues++ + (prev & compMask)) & compMask;
                        const u64 value1 = (*deltaValues++ + ((prev >> compSize) & compMask)) & compMask;
                        const u64 value2 = (*deltaValues++ + ((prev >> (compSize * 2)) & compMask)) & compMask;
                        *reinterpret_cast<u64*>(output) = ((value0 | (value1 << compSize) | (value2 << (compSize * 2))) << attrShift) | (*reinterpret_cast<u64*>(output) & keepMask);
                    }
                }
                output += stride;
                ++tableIndex;
            }
            if (groups->vertexCount > 0xffff) {
                for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                    *reinterpret_cast<u64*>(output) = (((*reinterpret_cast<u64*>(output - groups->backRefOffset) >> attrShift) & copyMask) << attrShift) | (*reinterpret_cast<u64*>(output) & keepMask);
                    output += stride;
                }
                tableIndex += groups->GetCopyCount();
            }
            ++groups;
        }
    }
}

template <size_t ComponentCount>
static void WriteHalfFloatDeltas(u16* output, u8** inputStreams, u32 streamsRemaining [[maybe_unused]], const Vec4h& value) {
    static_assert(ComponentCount > 0 && ComponentCount < 5, "Invalid component count");
    
    u8* signBits = inputStreams[0];
    u8* exponentBitsPos = inputStreams[1];
    u8* exponentBitsNeg = inputStreams[2];
    u16* mantissaBits0 = reinterpret_cast<u16*>(inputStreams[3]);
    u16* mantissaBits1 = reinterpret_cast<u16*>(inputStreams[4]);

    for (u32 i = 0; i < ComponentCount; ++i) {
        const u8 sign = *signBits++;
        const u8 exponent = sign ? (*exponentBitsNeg++) : (*exponentBitsPos++);
        const bool cond = exponent + sign == 0;
        const u16 v = cond ? (*mantissaBits0++) : (*mantissaBits1++);
        const u16 mantissa = cond ? (((-(v & 1) ^ v >> 1) + value.raw[i]) & 0x3ff) : v;

        output[i] = ((value.raw[i] + exponent * 0x400) & 0x7c00) | ((value.raw[i] & 0x8000) ^ sign << 0xf) | mantissa;
    }

    inputStreams[0] = signBits;
    inputStreams[1] = exponentBitsPos;
    inputStreams[2] = exponentBitsNeg;
    inputStreams[3] = reinterpret_cast<u8*>(mantissaBits0);
    inputStreams[4] = reinterpret_cast<u8*>(mantissaBits1);
}

template <size_t ComponentCount>
static void WriteFloatDeltas(u32* output, u8** inputStreams, u32 streamsRemaining [[maybe_unused]], const Vec4f& value) {
    static_assert(ComponentCount > 0 && ComponentCount < 5, "Invalid component count");
    
    u8* signBits = inputStreams[0];
    u8* exponentBitsPos = inputStreams[1];
    u8* exponentBitsNeg = inputStreams[2];
    u8* mantissaBits0 = inputStreams[3];
    u8* mantissaBits1 = inputStreams[4];

    for (u32 i = 0; i < ComponentCount; ++i) {
        const u8 sign = *signBits++;
        const u8 exponent = sign ? (*exponentBitsNeg++) : (*exponentBitsPos++);
        const bool cond = exponent + sign == 0;
        u32 v;
        if (cond) {
            v = Swap(*reinterpret_cast<u32*>(mantissaBits0));
            mantissaBits0 += 3;
        } else {
            v = Swap(*reinterpret_cast<u32*>(mantissaBits1));
            mantissaBits1 += 3;
        }
        const u32 mantissa = cond ? (((-(v >> 8 & 1) ^ v >> 9) + value.raw[i]) & 0x7fffff) : (v >> 8);

        output[i] = ((value.raw[i] + exponent * 0x800000) & 0x7f800000) | ((value.raw[i] & 0x80000000) ^ sign << 0x1f) | mantissa;
    }

    inputStreams[0] = signBits;
    inputStreams[1] = exponentBitsPos;
    inputStreams[2] = exponentBitsNeg;
    inputStreams[3] = mantissaBits0;
    inputStreams[4] = mantissaBits1;
}

static inline void HalfToSingle(const Vec4h& value, Vec4f& out) {
    out.f[0] = static_cast<f32>(value.f[0]);
    out.f[1] = static_cast<f32>(value.f[1]);
    out.f[2] = static_cast<f32>(value.f[2]);
}

static inline void SingleToHalf(const Vec4f& value, Vec4h& out) {
    out.f[0] = static_cast<f16>(value.f[0]);
    out.f[1] = static_cast<f16>(value.f[1]);
    out.f[2] = static_cast<f16>(value.f[2]);
}

template <size_t BitSize, bool UseTable>
void DecodeTriangleFloatDeltas(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining) {
    static_assert(BitSize == 16 || BitSize == 32, "Invalid bit size");
    const u32 stride = ctx.attrFlags[ctx.attrIndex] >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);

    u32 tableIndex = 0;
    if constexpr (UseTable) {
        for (; numGroups != 0; --numGroups) {
            for (u32 i = groups->GetRawCount(); i != 0; --i) {
                if (u32 offset = ctx.vertexBufferTable[tableIndex]) {
                    if constexpr (BitSize == 16) {
                        Vec4h values;
                        std::memcpy(&values, output - (offset >> 3) * stride, sizeof(f16x4));
                        if (offset & 7) {
                            values.raw[0] ^= (offset & 1) << 0xf;
                            values.raw[1] ^= (offset & 2) << 0xe;
                            values.raw[2] ^= (offset & 4) << 0xd;
                        }
                        WriteHalfFloatDeltas<3>(reinterpret_cast<u16*>(output), inputStreams, streamsRemaining, values);
                    } else if constexpr (BitSize == 32) {
                        Vec4f values;
                        std::memcpy(&values, output - (offset >> 3) * stride, sizeof(f32x4));
                        if (offset & 7) {
                            values.raw[0] ^= (offset & 1) << 0x1f;
                            values.raw[1] ^= (offset & 2) << 0x1e;
                            values.raw[2] ^= (offset & 4) << 0x1d;
                        }
                        WriteFloatDeltas<3>(reinterpret_cast<u32*>(output), inputStreams, streamsRemaining, values);
                    } else {
                        static_assert(false, "you shouldn't be here");
                    }
                } else {
                    const u64 value = ctx.indexBufferTable[tableIndex];
                    if (value & 0x3fffff) {
                        const u32 index0 = value & 0x3fffff;
                        const u32 index1 = value >> 0x16 & 0x1fffff;
                        const u32 index2 = value >> 0x2b;
                        if constexpr (BitSize == 16) {
                            Vec4h value0, value1, value2;
                            std::memcpy(&value0, output - index0 * stride, sizeof(Vec4h));
                            std::memcpy(&value1, output - index1 * stride, sizeof(Vec4h));
                            std::memcpy(&value2, output - index2 * stride, sizeof(Vec4h));
                            Vec4h values;
                            // intermediates are kept as floats instead of converting between every step
                            // there might be a way to tell gcc to do this automatically, but I couldn't figure it out
                            Vec4f temp0, temp1, temp2, temp;
                            HalfToSingle(value0, temp0); HalfToSingle(value1, temp1); HalfToSingle(value2, temp2);
                            temp.v = temp1.v + temp2.v - temp0.v;
                            SingleToHalf(temp, values);
                            WriteHalfFloatDeltas<3>(reinterpret_cast<u16*>(output), inputStreams, streamsRemaining, values);
                        } else if constexpr (BitSize == 32) {
                            f32x4 value0, value1, value2;
                            std::memcpy(&value0, output - index0 * stride, sizeof(f32x4));
                            std::memcpy(&value1, output - index1 * stride, sizeof(f32x4));
                            std::memcpy(&value2, output - index2 * stride, sizeof(f32x4));
                            Vec4f values;
                            values.v = value1 + value2 - value0;
                            WriteFloatDeltas<3>(reinterpret_cast<u32*>(output), inputStreams, streamsRemaining, values);
                        } else {
                            static_assert(false, "you shouldn't be here");
                        }
                    } else {
                        if (tableIndex == 0) {
                            if constexpr (BitSize == 16) {
                                Vec4h values = {};
                                WriteHalfFloatDeltas<3>(reinterpret_cast<u16*>(output), inputStreams, streamsRemaining, values);
                            } else if constexpr (BitSize == 32) {
                                Vec4f values = {};
                                WriteFloatDeltas<3>(reinterpret_cast<u32*>(output), inputStreams, streamsRemaining, values);
                            } else {
                                static_assert(false, "you shouldn't be here");
                            }
                        } else {
                            if constexpr (BitSize == 16) {
                                Vec4h values;
                                std::memcpy(&values, output - stride, sizeof(u16) * 3);
                                WriteHalfFloatDeltas<3>(reinterpret_cast<u16*>(output), inputStreams, streamsRemaining, values);
                            } else if constexpr (BitSize == 32) {
                                Vec4f values;
                                std::memcpy(&values, output - stride, sizeof(u32) * 3);
                                WriteFloatDeltas<3>(reinterpret_cast<u32*>(output), inputStreams, streamsRemaining, values);
                            } else {
                                static_assert(false, "you shouldn't be here");
                            }
                        }
                    }
                }
                output += stride;
                ++tableIndex;
            }
            if (groups->vertexCount > 0xffff) {
                for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                    std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * 3);
                    output += stride;
                }
                tableIndex += groups->GetCopyCount();
            }
            ++groups;
        }
    } else {
        for (; numGroups != 0; --numGroups) {
            for (u32 i = groups->GetRawCount(); i != 0; --i) {
                const u64 value = ctx.indexBufferTable[tableIndex];
                if (value & 0x3fffff) {
                    const u32 index0 = value & 0x3fffff;
                    const u32 index1 = value >> 0x16 & 0x1fffff;
                    const u32 index2 = value >> 0x2b;
                    if constexpr (BitSize == 16) {
                        Vec4h value0, value1, value2;
                        std::memcpy(&value0, output - index0 * stride, sizeof(Vec4h));
                        std::memcpy(&value1, output - index1 * stride, sizeof(Vec4h));
                        std::memcpy(&value2, output - index2 * stride, sizeof(Vec4h));
                        Vec4h values;
                        Vec4f temp0, temp1, temp2, temp;
                        HalfToSingle(value0, temp0); HalfToSingle(value1, temp1); HalfToSingle(value2, temp2);
                        temp.v = temp1.v + temp2.v - temp0.v;
                        SingleToHalf(temp, values);
                        WriteHalfFloatDeltas<3>(reinterpret_cast<u16*>(output), inputStreams, streamsRemaining, values);
                    } else if constexpr (BitSize == 32) {
                        f32x4 value0, value1, value2;
                        std::memcpy(&value0, output - index0 * stride, sizeof(f32x4));
                        std::memcpy(&value1, output - index1 * stride, sizeof(f32x4));
                        std::memcpy(&value2, output - index2 * stride, sizeof(f32x4));
                        Vec4f values;
                        values.v = value1 + value2 - value0;
                        WriteFloatDeltas<3>(reinterpret_cast<u32*>(output), inputStreams, streamsRemaining, values);
                    } else {
                        static_assert(false, "you shouldn't be here");
                    }
                } else {
                    if (tableIndex == 0) {
                        if constexpr (BitSize == 16) {
                            Vec4h values = {};
                            WriteHalfFloatDeltas<3>(reinterpret_cast<u16*>(output), inputStreams, streamsRemaining, values);
                        } else if constexpr (BitSize == 32) {
                            Vec4f values = {};
                            WriteFloatDeltas<3>(reinterpret_cast<u32*>(output), inputStreams, streamsRemaining, values);
                        } else {
                            static_assert(false, "you shouldn't be here");
                        }
                    } else {
                        if constexpr (BitSize == 16) {
                            Vec4h values;
                            std::memcpy(&values, output - stride, (BitSize >> 3) * 3);
                            WriteHalfFloatDeltas<3>(reinterpret_cast<u16*>(output), inputStreams, streamsRemaining, values);
                        } else if constexpr (BitSize == 32) {
                            Vec4f values;
                            std::memcpy(&values, output - stride, (BitSize >> 3) * 3);
                            WriteFloatDeltas<3>(reinterpret_cast<u32*>(output), inputStreams, streamsRemaining, values);
                        } else {
                            static_assert(false, "you shouldn't be here");
                        }
                    }
                }
                output += stride;
                ++tableIndex;
            }
            if (groups->vertexCount > 0xffff) {
                for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                    std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * 3);
                    output += stride;
                }
                tableIndex += groups->GetCopyCount();
            }
            ++groups;
        }
    }
}

template <size_t BitSize>
void DecodeCrossProduct(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert(BitSize == 8 || BitSize == 10 || BitSize == 16, "Invalid bit size");

    const u8 index0 = *ctx.decompContext->currentPos++;
    const u8 index1 = *ctx.decompContext->currentPos++;
    const u32 flags0 = ctx.attrFlags[index0];
    const u32 stride0 = flags0 >> 0x18;
    const u32 bitSize0 = flags0 >> 8 & 0xff;
    const u32 offset0 = ctx.attrOffsets[index0] + ctx.baseVertexIndex * stride0;
    const u32 flags1 = ctx.attrFlags[index1];
    const u32 stride1 = flags1 >> 0x18;
    const u32 bitSize1 = flags1 >> 8 & 0xff;
    const u32 offset1 = ctx.attrOffsets[index1] + ctx.baseVertexIndex * stride1;

    u8* baseValues = inputStreams[0];
    u8* flipValues = inputStreams[1];
    const u32 stride = ctx.attrFlags[ctx.attrIndex] >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);

    constexpr auto convert8Bit = [](const u8* ptr) -> Vec3 {
        return {
            static_cast<f32>(reinterpret_cast<const s8*>(ptr)[0]) / 127.f,
            static_cast<f32>(reinterpret_cast<const s8*>(ptr)[1]) / 127.f,
            static_cast<f32>(reinterpret_cast<const s8*>(ptr)[2]) / 127.f,
        };
    };
    constexpr auto convert10Bit = [](const u8* ptr) -> Vec3 {
        const s32 value = *reinterpret_cast<const s32*>(ptr);
        return {
            static_cast<f32>(value << 0x16 >> 0x16) / 511.f,
            static_cast<f32>(value << 0xc >> 0x16) / 511.f,
            static_cast<f32>(value << 2 >> 0x16) / 511.f,
        };
    };
    constexpr auto convert16Bit = [](const u8* ptr) -> Vec3 {
        return {
            static_cast<f32>(reinterpret_cast<const s16*>(ptr)[0]) / 32767.f,
            static_cast<f32>(reinterpret_cast<const s16*>(ptr)[1]) / 32767.f,
            static_cast<f32>(reinterpret_cast<const s16*>(ptr)[2]) / 32767.f,
        };
    };

    using ConvertFunc = Vec3 (*)(const u8* ptr);

    ConvertFunc convertFunc0;
    ConvertFunc convertFunc1;
    switch (bitSize0) {
        case 8:
            convertFunc0 = convert8Bit;
            break;
        case 10:
            convertFunc0 = convert10Bit;
            break;
        case 16:
            convertFunc0 = convert16Bit;
            break;
        default:
            UNREACHABLE_DEFAULT_CASE
    }
    switch (bitSize1) {
        case 8:
            convertFunc1 = convert8Bit;
            break;
        case 10:
            convertFunc1 = convert10Bit;
            break;
        case 16:
            convertFunc1 = convert16Bit;
            break;
        default:
            UNREACHABLE_DEFAULT_CASE
    }

    u32 index = 0;
    for (; numGroups != 0; --numGroups){
        for (u32 i = groups->GetRawCount(); i != 0; --i) {
            const Vec3 pos0 = convertFunc0(ctx.outputBuffer + index * stride0 + offset0);
            const Vec3 pos1 = convertFunc1(ctx.outputBuffer + index * stride1 + offset1);
            Vec3 cross = pos0.Cross(pos1);
            cross.Normalize();
            if constexpr (BitSize == 8) {
                const u8 flip = *flipValues++;
                output[0] = *baseValues++ + (static_cast<s32>(cross.x * 127.f) ^ -flip) + flip;
                output[1] = *baseValues++ + (static_cast<s32>(cross.y * 127.f) ^ -flip) + flip;
                output[2] = *baseValues++ + (static_cast<s32>(cross.z * 127.f) ^ -flip) + flip;
            } else if constexpr (BitSize == 10) {
                const u8 flip = *flipValues++;
                const u16 value0 = (reinterpret_cast<u16*>(baseValues)[0] + (static_cast<s32>(cross.x * 511.f) ^ -flip) + flip) & 0x3ff;
                const u16 value1 = (reinterpret_cast<u16*>(baseValues)[1] + (static_cast<s32>(cross.y * 511.f) ^ -flip) + flip) & 0x3ff;
                const u16 value2 = (reinterpret_cast<u16*>(baseValues)[2] + (static_cast<s32>(cross.z * 511.f) ^ -flip) + flip) & 0x3ff;
                *reinterpret_cast<u32*>(output) = value0 | (value1 << 10) | (value2 << 20);
                baseValues += 3 * sizeof(u16);
            } else if constexpr (BitSize == 16) {
                const u8 flip = *flipValues++;
                reinterpret_cast<u16*>(output)[0] = reinterpret_cast<u16*>(baseValues)[0] + (static_cast<s32>(cross.x * 32767.f) ^ -flip) + flip;
                reinterpret_cast<u16*>(output)[1] = reinterpret_cast<u16*>(baseValues)[1] + (static_cast<s32>(cross.y * 32767.f) ^ -flip) + flip;
                reinterpret_cast<u16*>(output)[2] = reinterpret_cast<u16*>(baseValues)[2] + (static_cast<s32>(cross.z * 32767.f) ^ -flip) + flip;
                baseValues += 3 * sizeof(u16);
            } else {
                static_assert(false, "you shouldn't be here");
            }
            output += stride;
            ++index;
        }
        if (groups->vertexCount > 0xffff) {
            for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                if constexpr (BitSize == 10) {
                    *reinterpret_cast<u32*>(output) = *reinterpret_cast<u32*>(output - groups->backRefOffset);
                } else {
                    std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * 3);
                }
                output += stride;
            }
            index += groups->GetCopyCount();
        }
        ++groups;
    }
}

using DecodeTexCoordFunc = Vec2 (*)(const u8* input, const u8* output, u32 stride, const u8* refStream, u32 refStreamStride, u32 indexA, u32 indexB, u32 indexC);

template <size_t BitSize, bool IsFloat>
static inline Vec3 ReadVec(const u8* input) {
    if constexpr (BitSize == 8) {
        return {
            static_cast<f32>(input[0]),
            static_cast<f32>(input[1]),
            static_cast<f32>(input[2]),
        };
    } else if constexpr (BitSize == 10) {
        const u32 value = *reinterpret_cast<const u32*>(input);
        return {
            static_cast<f32>(value & 0x3ff),
            static_cast<f32>((value >> 10) & 0x3ff),
            static_cast<f32>((value >> 20) & 0x3ff),
        };
    } else if constexpr (BitSize == 16) {
        if constexpr (IsFloat) {
            return {
                static_cast<f32>(reinterpret_cast<const f16*>(input)[0]),
                static_cast<f32>(reinterpret_cast<const f16*>(input)[1]),
                static_cast<f32>(reinterpret_cast<const f16*>(input)[2]),
            };
        } else {
            return {
                static_cast<f32>(reinterpret_cast<const u16*>(input)[0]),
                static_cast<f32>(reinterpret_cast<const u16*>(input)[1]),
                static_cast<f32>(reinterpret_cast<const u16*>(input)[2]),
            };
        }
    } else if constexpr (BitSize == 32 && IsFloat) {
        return {
            reinterpret_cast<const f32*>(input)[0],
            reinterpret_cast<const f32*>(input)[1],
            reinterpret_cast<const f32*>(input)[2],
        };
    } else {
        static_assert(false, "you shouldn't be here");
    }
}

template <size_t BitSize, bool IsFloat, typename T>
static Vec2 DecodeTexCoords_(const u8* input, const u8* output, u32 stride, const u8* refStream, u32 refStreamStride, u32 indexA, u32 indexB, u32 index) {
    static_assert(BitSize == 8 || BitSize == 10 || BitSize == 16 || (BitSize == 32 && IsFloat), "Invalid bit size");
    static_assert(std::is_same_v<T, s16> || std::is_same_v<T, u16> || std::is_same_v<T, f16> || std::is_same_v<T, f32>, "Invalid type");

    const Vec3 a = ReadVec<BitSize, IsFloat>(refStream + index * refStreamStride - indexA * refStreamStride);
    const Vec3 b = ReadVec<BitSize, IsFloat>(refStream + index * refStreamStride - indexB * refStreamStride);
    const Vec3 c = ReadVec<BitSize, IsFloat>(refStream + index * refStreamStride);
    const Vec3 diffBA = b - a;
    const Vec3 diffCA = c - a;

    const f32 squaredLen = std::max(diffBA.SquaredLength(), Vec3::cEpsilon);
    const f32 ratio = diffBA.Dot(diffCA) / squaredLen;

    // floats be weird sometimes but yeah this makes a difference
    const Vec3 normalized = (a + diffBA * ratio) - a;

    const T* stream0 = reinterpret_cast<const T*>(output - indexA * stride);
    const T* stream1 = reinterpret_cast<const T*>(output - indexB * stride);

    const f32 dist = std::max((diffCA.SquaredLength() - normalized.SquaredLength()) / squaredLen, 0.f);

    const f32 range0 = static_cast<f32>(stream1[0]) - static_cast<f32>(stream0[0]);
    const f32 range1 = static_cast<f32>(stream1[1]) - static_cast<f32>(stream0[1]);

    const f32 value0 = (*input != 0) ? -(std::sqrt(dist) * -range1) : (std::sqrt(dist) * -range1);
    const f32 value1 = (*input != 0) ? -(std::sqrt(dist) * range0) : (std::sqrt(dist) * range0);

    // this is super delicate, if you do value0 + range0 * ratio + static_cast<f32>(stream0[0]) it won't match
    // don't you love floating point numbers
    return {
        range0 * ratio + static_cast<f32>(stream0[0]) + value0,
        range1 * ratio + static_cast<f32>(stream0[1]) + value1,
    };
}

template <typename T>
void DecodeTriangleTexCoords(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert(std::is_same_v<T, s16> || std::is_same_v<T, u16>, "Invalid type");

    const u32 v = ctx.decompContext->bitStream0.Read(5);
    
    T* deltaValues = reinterpret_cast<T*>(inputStreams[0]);
    T* baseValues = reinterpret_cast<T*>(inputStreams[1]);
    u8* flipValues = inputStreams[2];

    const u32 flags = ctx.attrFlags[(v >> 1) & 0xf];
    const u32 stride = flags >> 0x18;
    const u32 offset = ctx.attrOffsets[(v >> 1) & 0xf];

    const u32 attrFlags = ctx.attrFlags[ctx.attrIndex];
    const u32 vertStride = attrFlags >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * vertStride);

    static DecodeTexCoordFunc sDecodingFuncs[5] = {
        DecodeTexCoords_<8, false, T>,
        DecodeTexCoords_<10, false, T>,
        DecodeTexCoords_<16, false, T>,
        DecodeTexCoords_<16, true, T>,
        DecodeTexCoords_<32, true, T>,
    };
    DecodeTexCoordFunc decodeFunc = sDecodingFuncs[(flags >> 3 & 3) + ((v & 1) << 1)];

    u32 tableIndex = 0;
    for (; numGroups != 0; --numGroups) {
        for (u32 i = groups->GetRawCount(); i != 0; --i) {
            const u64 value = ctx.indexBufferTable[tableIndex];
            if (value) {
                const Vec2 vec = decodeFunc(flipValues, output, vertStride, ctx.outputBuffer + (offset + ctx.baseVertexIndex * stride),
                                            stride, value >> 0x16 & 0x1fffff, value >> 0x2b, tableIndex);
                // technically this is supposed to be an unconditional round to nearest with ties to even, but I'm too lazy to create a helper function to do that
                // I don't think c++ has any builtin function that does that so instead we'll use std::rint which uses the current rounding mode
                // this shouldn't be an issue since we set the rounding mode before decoding starts
                reinterpret_cast<T*>(output)[0] = static_cast<T>(std::rint(vec.x)) + *baseValues++;
                reinterpret_cast<T*>(output)[1] = static_cast<T>(std::rint(vec.y)) + *baseValues++;
                ++flipValues;
            } else {
                if (tableIndex == 0) {
                    std::memcpy(output, deltaValues, sizeof(T) * 2);
                    deltaValues += 2;
                } else {
                    for (u32 j = 0; j < 2; ++j) {
                        reinterpret_cast<T*>(output)[j] = reinterpret_cast<T*>(output - vertStride)[j] + *deltaValues++;
                    }
                }
            }
            output += vertStride;
            ++tableIndex;
        }
        if (groups->vertexCount > 0xffff) {
            for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                std::memcpy(output, output - groups->backRefOffset, sizeof(T) * 2);
                output += vertStride;
            }
            tableIndex += groups->GetCopyCount();
        }
        ++groups;
    }
}

template <typename T>
void DecodeTriangleTexCoordsFloat(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining) {
    static_assert(std::is_same_v<T, f16> || std::is_same_v<T, f32>, "Invalid type");

    const u32 v = ctx.decompContext->bitStream0.Read(5);
    
    u8* flipValues = inputStreams[0];

    const u32 flags = ctx.attrFlags[(v >> 1) & 0xf];
    const u32 stride = flags >> 0x18;
    const u32 offset = ctx.attrOffsets[(v >> 1) & 0xf];

    const u32 attrFlags = ctx.attrFlags[ctx.attrIndex];
    const u32 vertStride = attrFlags >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * vertStride);

    static DecodeTexCoordFunc sDecodingFuncs[5] = {
        DecodeTexCoords_<8, false, T>,
        DecodeTexCoords_<10, false, T>,
        DecodeTexCoords_<16, false, T>,
        DecodeTexCoords_<16, true, T>,
        DecodeTexCoords_<32, true, T>,
    };
    DecodeTexCoordFunc decodeFunc = sDecodingFuncs[(flags >> 3 & 3) + ((v & 1) << 1)];
    
    u32 tableIndex = 0;
    for (; numGroups != 0; --numGroups) {
        for (u32 i = groups->GetRawCount(); i != 0; --i) {
            const u64 value = ctx.indexBufferTable[tableIndex];
            if (value) {
                const Vec2 vec = decodeFunc(flipValues, output, vertStride, ctx.outputBuffer + (offset + ctx.baseVertexIndex * stride),
                                            stride, value >> 0x16 & 0x1fffff, value >> 0x2b, tableIndex);
                if constexpr (std::is_same_v<T, f16>) {
                    Vec4h computed;
                    computed.f[0] = static_cast<f16>(vec.x);
                    computed.f[1] = static_cast<f16>(vec.y);
                    WriteHalfFloatDeltas<2>(reinterpret_cast<u16*>(output), &inputStreams[1], streamsRemaining - 1, computed);
                } else if constexpr (std::is_same_v<T, f32>) {
                    Vec4f computed;
                    computed.f[0] = vec.x;
                    computed.f[1] = vec.y;
                    WriteFloatDeltas<2>(reinterpret_cast<u32*>(output), &inputStreams[1], streamsRemaining - 1, computed);
                } else {
                    static_assert(false, "you shouldn't be here");
                }
                ++flipValues;
            } else {
                if (tableIndex == 0) {
                    if constexpr (std::is_same_v<T, f16>) {
                        Vec4h computed = {};
                        WriteHalfFloatDeltas<2>(reinterpret_cast<u16*>(output), &inputStreams[1], streamsRemaining - 1, computed);
                    } else if constexpr (std::is_same_v<T, f32>) {
                        Vec4f computed = {};
                        WriteFloatDeltas<2>(reinterpret_cast<u32*>(output), &inputStreams[1], streamsRemaining - 1, computed);
                    } else {
                        static_assert(false, "you shouldn't be here");
                    }
                } else {
                    if constexpr (std::is_same_v<T, f16>) {
                        Vec4h computed;
                        std::memcpy(&computed, output - vertStride, sizeof(T) * 2);
                        WriteHalfFloatDeltas<2>(reinterpret_cast<u16*>(output), &inputStreams[1], streamsRemaining - 1, computed);
                    } else if constexpr (std::is_same_v<T, f32>) {
                        Vec4f computed;
                        std::memcpy(&computed, output - vertStride, sizeof(T) * 2);
                        WriteFloatDeltas<2>(reinterpret_cast<u32*>(output), &inputStreams[1], streamsRemaining - 1, computed);
                    } else {
                        static_assert(false, "you shouldn't be here");
                    }
                }
            }
            output += vertStride;
            ++tableIndex;
        }
        if (groups->vertexCount > 0xffff) {
            for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                std::memcpy(output, output - groups->backRefOffset, sizeof(T) * 2);
                output += vertStride;
            }
            tableIndex += groups->GetCopyCount();
        }
        ++groups;
    }
}

template <size_t BitSize, bool UseTable>
void DecodeFixedDistance(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups, u8* (&inputStreams)[6], s32 streamsRemaining [[maybe_unused]]) {
    static_assert(BitSize == 8 || BitSize == 10 || BitSize == 16, "Invalid bit size");
    
    u8* valueStream = inputStreams[0];
    u8* addValues = inputStreams[1];
    u8* flipValues = inputStreams[2];
    const u32 stride = ctx.attrFlags[ctx.attrIndex] >> 0x18;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);

    if constexpr (UseTable) {
        u8* refBaseValueStream = inputStreams[3];
        u32 tableIndex = 0;
        for (; numGroups; --numGroups) {
            for (u32 i = groups->GetRawCount(); i != 0; --i) {
                if (u32 offset = ctx.vertexBufferTable[tableIndex++]) {
                    if constexpr (BitSize == 8) {
                        const u8 value0 = offset & 1;
                        const u8 value1 = (offset >> 1) & 1;
                        const u8 value2 = (offset >> 2) & 1;
                        output[0] = *refBaseValueStream++ + ((output - (offset >> 3) * stride)[0] ^ -value0) + value0;
                        output[1] = *refBaseValueStream++ + ((output - (offset >> 3) * stride)[1] ^ -value1) + value1;
                        output[2] = *refBaseValueStream++ + ((output - (offset >> 3) * stride)[2] ^ -value2) + value2;
                    } else if constexpr (BitSize == 10) {
                        const u32 value0 = offset & 1;
                        const u32 value1 = (offset >> 1) & 1;
                        const u32 value2 = (offset >> 2) & 1;
                        const u32 value = *reinterpret_cast<u32*>(output - (offset >> 3) * stride);
                        *reinterpret_cast<u32*>(output) = ((reinterpret_cast<u16*>(refBaseValueStream)[0] + (value ^ -value0) + value0) & 0x3ff) | 
                                                          (((reinterpret_cast<u16*>(refBaseValueStream)[1] + ((value >> 10) ^ -value1) + value1) & 0x3ff) << 10) |
                                                          (((reinterpret_cast<u16*>(refBaseValueStream)[2] + ((value >> 20) ^ -value2) + value2) & 0x3ff) << 20);
                        refBaseValueStream += 3 * sizeof(u16);
                    } else if constexpr (BitSize == 16) {
                        const u16 value0 = offset & 1;
                        const u16 value1 = (offset >> 1) & 1;
                        const u16 value2 = (offset >> 2) & 1;
                        reinterpret_cast<u16*>(output)[0] = reinterpret_cast<u16*>(refBaseValueStream)[0] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[0] ^ -value0) + value0;
                        reinterpret_cast<u16*>(output)[1] = reinterpret_cast<u16*>(refBaseValueStream)[1] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[1] ^ -value1) + value1;
                        reinterpret_cast<u16*>(output)[2] = reinterpret_cast<u16*>(refBaseValueStream)[2] + (reinterpret_cast<u16*>(output - (offset >> 3) * stride)[2] ^ -value2) + value2;
                        refBaseValueStream += 3 * sizeof(u16);
                    } else {
                        static_assert(false, "you shouldn't be here");
                    }
                } else {
                    if constexpr (BitSize == 8) {
                        const s8 value0 = *valueStream++;
                        const s8 value1 = *valueStream++;
                        const s64 sqrDist = 0x7f * 0x7f - (value0 * value0 + value1 * value1);
                        const u8 dist = static_cast<u8>(std::rint(std::sqrt(static_cast<f32>(sqrDist < 0 ? 0 : sqrDist)))) + *addValues++;
                        output[0] = value0;
                        output[1] = value1;
                        output[2] = *flipValues++ == 1 ? -dist : dist;
                    } else if constexpr (BitSize == 10) {
                        const s32 value0 = static_cast<s32>(reinterpret_cast<u16*>(valueStream)[0] << 0x16) >> 0x16;
                        const s32 value1 = static_cast<s32>(reinterpret_cast<u16*>(valueStream)[1] << 0x16) >> 0x16;
                        const s64 sqrDist = 0x1ff * 0x1ff - (value0 * value0 + value1 * value1);
                        const u32 dist = static_cast<u32>(std::rint(std::sqrt(static_cast<f32>(sqrDist < 0 ? 0 : sqrDist)))) + *reinterpret_cast<u16*>(addValues);
                        *reinterpret_cast<u32*>(output) = reinterpret_cast<u16*>(valueStream)[0] |
                                                          (reinterpret_cast<u16*>(valueStream)[1] << 10) |
                                                          (((*flipValues++ == 1 ? -dist : dist) & 0x3ff) << 20);
                        valueStream += 2 * sizeof(u16);
                        addValues += sizeof(u16);
                    } else if constexpr (BitSize == 16) {
                        const s16 value0 = reinterpret_cast<s16*>(valueStream)[0];
                        const s16 value1 = reinterpret_cast<s16*>(valueStream)[1];
                        const s64 sqrDist = 0x1ff * 0x1ff - (value0 * value0 + value1 * value1);
                        const u16 dist = static_cast<u16>(std::rint(std::sqrt(static_cast<f32>(sqrDist < 0 ? 0 : sqrDist)))) + *reinterpret_cast<u16*>(addValues);
                        reinterpret_cast<u16*>(output)[0] = value0;
                        reinterpret_cast<u16*>(output)[1] = value1;
                        reinterpret_cast<u16*>(output)[2] = *flipValues++ == 1 ? -dist : dist;
                        valueStream += 2 * sizeof(u16);
                        addValues += sizeof(u16);
                    } else {
                        static_assert(false, "you shouldn't be here");
                    }
                }
                output += stride;
            }
            if (groups->vertexCount > 0xffff) {
                for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                    if constexpr (BitSize == 10) {
                        *reinterpret_cast<u32*>(output) = *reinterpret_cast<u32*>(output - groups->backRefOffset);
                    } else {
                        std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * 3);
                    }
                    output += stride;
                }
                tableIndex += groups->GetCopyCount();
            }
            ++groups;
        }
    } else {
        for (; numGroups; --numGroups) {
            for (u32 i = groups->GetRawCount(); i != 0; --i) {
                if constexpr (BitSize == 8) {
                    const s8 value0 = *valueStream++;
                    const s8 value1 = *valueStream++;
                    const s64 sqrDist = 0x7f * 0x7f - (value0 * value0 + value1 * value1);
                    const u8 dist = static_cast<u8>(std::rint(std::sqrt(static_cast<f32>(sqrDist < 0 ? 0 : sqrDist)))) + *addValues++;
                    output[0] = value0;
                    output[1] = value1;
                    output[2] = *flipValues++ == 1 ? -dist : dist;
                } else if constexpr (BitSize == 10) {
                    const s32 value0 = static_cast<s32>(reinterpret_cast<u16*>(valueStream)[0] << 0x16) >> 0x16;
                    const s32 value1 = static_cast<s32>(reinterpret_cast<u16*>(valueStream)[1] << 0x16) >> 0x16;
                    const s64 sqrDist = 0x1ff * 0x1ff - (value0 * value0 + value1 * value1);
                    const u32 dist = static_cast<u32>(std::rint(std::sqrt(static_cast<f32>(sqrDist < 0 ? 0 : sqrDist)))) + *reinterpret_cast<u16*>(addValues);
                    *reinterpret_cast<u32*>(output) = reinterpret_cast<u16*>(valueStream)[0] |
                                                      (reinterpret_cast<u16*>(valueStream)[1] << 10) |
                                                      (((*flipValues++ == 1 ? -dist : dist) & 0x3ff) << 20);
                    valueStream += 2 * sizeof(u16);
                    addValues += sizeof(u16);
                } else if constexpr (BitSize == 16) {
                    const s16 value0 = reinterpret_cast<s16*>(valueStream)[0];
                    const s16 value1 = reinterpret_cast<s16*>(valueStream)[1];
                    const s64 sqrDist = 0x1ff * 0x1ff - (value0 * value0 + value1 * value1);
                    const u16 dist = static_cast<u16>(std::rint(std::sqrt(static_cast<f32>(sqrDist < 0 ? 0 : sqrDist)))) + *reinterpret_cast<u16*>(addValues);
                    reinterpret_cast<u16*>(output)[0] = value0;
                    reinterpret_cast<u16*>(output)[1] = value1;
                    reinterpret_cast<u16*>(output)[2] = *flipValues++ == 1 ? -dist : dist;
                    valueStream += 2 * sizeof(u16);
                    addValues += sizeof(u16);
                } else {
                    static_assert(false, "you shouldn't be here");
                }
                output += stride;
            }
            if (groups->vertexCount > 0xffff) {
                for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                    if constexpr (BitSize == 10) {
                        *reinterpret_cast<u32*>(output) = *reinterpret_cast<u32*>(output - groups->backRefOffset);
                    } else {
                        std::memcpy(output, output - groups->backRefOffset, (BitSize >> 3) * 3);
                    }
                    output += stride;
                }
            }
            ++groups;
        }
    }
}

} // namespace detail

DecodeAttributeFunc sAttributeDecodeFunctions[0x71] = {
    detail::DecodeInternalDeltas<8, 2, false>, detail::DecodeInternalDeltas<8, 3, false>, detail::DecodeInternalDeltas<8, 4, false>,
    detail::DecodeInternalDeltas<10, 3, false>, detail::DecodeInternalDeltas<16, 2, false>, detail::DecodeInternalDeltas<16, 3, false>,
    detail::DecodeInternalDeltas<16, 4, false>,
    detail::DecodeInternalDeltas<8, 2, true>, detail::DecodeInternalDeltas<8, 3, true>, detail::DecodeInternalDeltas<8, 4, true>,
    detail::DecodeInternalDeltas<10, 3, true>, detail::DecodeInternalDeltas<16, 2, true>, detail::DecodeInternalDeltas<16, 3, true>,
    detail::DecodeInternalDeltas<16, 4, true>,
    
    detail::DecodeRaw<2>, detail::DecodeRaw<8>, detail::DecodeRaw<16>, detail::DecodeRaw<24>, detail::DecodeRaw<32>,
    detail::DecodeRaw<48>, detail::DecodeRaw<64>, detail::DecodeRaw<96>, detail::DecodeRaw<128>, detail::DecodeRaw<30>,

    detail::DecodeRawCustomSize<u8, u32, false>, detail::DecodeRawCustomSize<u16, u32, false>,
    detail::DecodeRawCustomSize<u8, u64, false>, detail::DecodeRawCustomSize<u16, u64, false>,

    detail::DecodeRawWithTable<2, 1>, detail::DecodeRawWithTable<8, 1>, detail::DecodeRawWithTable<8, 2>, detail::DecodeRawWithTable<8, 3>,
    detail::DecodeRawWithTable<8, 4>, detail::DecodeRawWithTable<10, 3>, detail::DecodeRawWithTable<16, 1>, detail::DecodeRawWithTable<16, 2>,
    detail::DecodeRawWithTable<16, 3>, detail::DecodeRawWithTable<16, 4>, detail::DecodeRawWithTable<32, 1>, detail::DecodeRawWithTable<32, 2>,
    detail::DecodeRawWithTable<32, 3>, detail::DecodeRawWithTable<32, 4>,
    
    detail::DecodeRawCustomSize<u8, u32, true>, detail::DecodeRawCustomSize<u16, u32, true>,
    detail::DecodeRawCustomSize<u8, u64, true>, detail::DecodeRawCustomSize<u16, u64, true>,

    detail::DecodeXOR1<8>, detail::DecodeXOR1<10>, detail::DecodeXOR1<16>,
    detail::DecodeXOR2<8>, detail::DecodeXOR2<10>, detail::DecodeXOR2<16>, detail::DecodeXORCustomSize<true>,
    detail::DecodeXOR3<8>, detail::DecodeXOR3<16>,
    detail::DecodeXOR4<8>, detail::DecodeXOR4<16>, detail::DecodeXORCustomSize<false>,
    detail::DecodeXOR5<16>, detail::DecodeXOR5<32>,

    detail::DecodeDeltas<2, 1, false>, detail::DecodeDeltas<8, 1, false>, detail::DecodeDeltas<8, 2, false>, detail::DecodeDeltas<8, 3, false>,
    detail::DecodeDeltas<8, 4, false>, detail::DecodeDeltas<10, 3, false>, detail::DecodeDeltas<16, 1, false>, detail::DecodeDeltas<16, 2, false>,
    detail::DecodeDeltas<16, 3, false>, detail::DecodeDeltas<16, 4, false>,
    detail::DecodeDeltasCustomSize<u8, u32, false>, detail::DecodeDeltasCustomSize<u16, u32, false>,
    detail::DecodeDeltasCustomSize<u8, u64, false>, detail::DecodeDeltasCustomSize<u16, u64, false>,

    detail::DecodeDeltas<2, 1, true>, detail::DecodeDeltas<8, 1, true>, detail::DecodeDeltas<8, 2, true>, detail::DecodeDeltas<8, 3, true>,
    detail::DecodeDeltas<8, 4, true>, detail::DecodeDeltas<10, 3, true>, detail::DecodeDeltas<16, 1, true>, detail::DecodeDeltas<16, 2, true>,
    detail::DecodeDeltas<16, 3, true>, detail::DecodeDeltas<16, 4, true>,
    detail::DecodeDeltasCustomSize<u8, u32, true>, detail::DecodeDeltasCustomSize<u16, u32, true>,
    detail::DecodeDeltasCustomSize<u8, u64, true>, detail::DecodeDeltasCustomSize<u16, u64, true>,

    detail::DecodeTriangleDeltas<8, false>, detail::DecodeTriangleDeltas<10, false>, 
    detail::DecodeTriangleDeltas<16, false>, detail::DecodeTriangleDeltasCustomSize<false>,
    detail::DecodeTriangleFloatDeltas<16, false>, detail::DecodeTriangleFloatDeltas<32, false>,
    
    detail::DecodeTriangleDeltas<8, true>, detail::DecodeTriangleDeltas<10, true>,
    detail::DecodeTriangleDeltas<16, true>, detail::DecodeTriangleDeltasCustomSize<true>,
    detail::DecodeTriangleFloatDeltas<16, true>, detail::DecodeTriangleFloatDeltas<32, true>,
    
    detail::DecodeCrossProduct<8>, detail::DecodeCrossProduct<10>, detail::DecodeCrossProduct<16>,

    detail::DecodeTriangleTexCoords<s16>, detail::DecodeTriangleTexCoords<u16>,
    detail::DecodeTriangleTexCoordsFloat<f16>, detail::DecodeTriangleTexCoordsFloat<f32>,

    detail::DecodeFixedDistance<8, false>, detail::DecodeFixedDistance<10, false>, detail::DecodeFixedDistance<16, false>,
    detail::DecodeFixedDistance<8, true>, detail::DecodeFixedDistance<10, true>, detail::DecodeFixedDistance<16, true>,
};

// this just copies old data, nothing too fancy
void DecodeBackrefs(VertexStreamContext& ctx, s32 vertexCount [[maybe_unused]], VertexDecodeGroup* groups, u32 numGroups) {
    #define MASK(VALUE, SHIFT, MASK) ((((VALUE) << (SHIFT)) & (MASK)) >> (SHIFT))

    const u32 flags = ctx.attrFlags[ctx.attrIndex];
    const u32 stride = flags >> 0x18;
    const u32 compSize = flags >> 0x8 & 0xff;
    const u32 attrShift = flags >> 0x10 & 0xff;
    const u32 compCount = flags & 7;
    u8* output = ctx.outputBuffer + (ctx.attrOffsets[ctx.attrIndex] + ctx.baseVertexIndex * stride);

    if (compSize & 7 || attrShift) { // components don't fit into byte boundaries
        const bool is10Bit = attrShift ? false : (compSize == 10);
        if (is10Bit) {
            for (; numGroups != 0; --numGroups) {
                for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                    *reinterpret_cast<u32*>(output) = *reinterpret_cast<u32*>(output - groups->backRefOffset) & 0x3fffffff;
                    output += stride;
                }
                ++groups;
            }
        } else if (attrShift == 0x1e && compSize == 2) {
            for (; numGroups != 0; --numGroups) {
                for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                    *reinterpret_cast<u32*>(output) = (*reinterpret_cast<u32*>(output - groups->backRefOffset) & 0xc0000000) | (*reinterpret_cast<u32*>(output) & 0x3fffffff);
                    output += stride;
                }
                ++groups;
            }
        } else {
            const u32 bitSize = compSize * compCount;
            const u32 endOffset = bitSize + attrShift;
            if (endOffset <= 8) {
                const u8 copyMask = (2 << (bitSize - 1)) - 1;
                const u8 keepMask = ~(copyMask << attrShift);
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                        *reinterpret_cast<u8*>(output) = MASK(*(output - groups->backRefOffset), attrShift, copyMask) | (*output & keepMask);
                        output += stride;
                    }
                    ++groups;
                }
            } else if (endOffset <= 0x10) {
                const u16 copyMask = (2 << (bitSize - 1)) - 1;
                const u16 keepMask = ~(copyMask << attrShift);
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                        *reinterpret_cast<u16*>(output) = MASK(*reinterpret_cast<u16*>(output - groups->backRefOffset), attrShift, copyMask) | (*reinterpret_cast<u16*>(output) & keepMask);
                        output += stride;
                    }
                    ++groups;
                }
            } else if (endOffset <= 0x20) {
                const u32 copyMask = (2 << (bitSize - 1)) - 1;
                const u32 keepMask = ~(copyMask << attrShift);
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                        *reinterpret_cast<u32*>(output) = MASK(*reinterpret_cast<u32*>(output - groups->backRefOffset), attrShift, copyMask) | (*reinterpret_cast<u32*>(output) & keepMask);
                        output += stride;
                    }
                    ++groups;
                }
            } else if (endOffset <= 0x40) {
                const u64 copyMask = (2 << (bitSize - 1)) - 1;
                const u64 keepMask = ~(copyMask << attrShift);
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                        *reinterpret_cast<u64*>(output) = MASK(*reinterpret_cast<u64*>(output - groups->backRefOffset), attrShift, copyMask) | (*reinterpret_cast<u64*>(output) & keepMask);
                        output += stride;
                    }
                    ++groups;
                }
            }
        }
    } else {
        switch ((compSize >> 3) * compCount) { // total size of components in bytes
            case 1: {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                        std::memcpy(output, output - groups->backRefOffset, 1);
                        output += stride;
                    }
                    ++groups;
                }
                return;
            }
            case 2: {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                        std::memcpy(output, output - groups->backRefOffset, 2);
                        output += stride;
                    }
                    ++groups;
                }
                return;
            }
            case 3: {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                        std::memcpy(output, output - groups->backRefOffset, 3);
                        output += stride;
                    }
                    ++groups;
                }
                return;
            }
            case 4: {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                        std::memcpy(output, output - groups->backRefOffset, 4);
                        output += stride;
                    }
                    ++groups;
                }
                return;
            }
            case 6: {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                        std::memcpy(output, output - groups->backRefOffset, 6);
                        output += stride;
                    }
                    ++groups;
                }
                return;
            }
            case 8: {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                        std::memcpy(output, output - groups->backRefOffset, 8);
                        output += stride;
                    }
                    ++groups;
                }
                return;
            }
            case 12: {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                        std::memcpy(output, output - groups->backRefOffset, 12);
                        output += stride;
                    }
                    ++groups;
                }
                return;
            }
            case 16: {
                for (; numGroups != 0; --numGroups) {
                    for (u32 i = groups->GetCopyCount(); i != 0; --i) {
                        std::memcpy(output, output - groups->backRefOffset, 16);
                        output += stride;
                    }
                    ++groups;
                }
                return;
            }
            default:
                UNREACHABLE_DEFAULT_CASE
        }
    }
    
    #undef MASK
}

} // namespace mc
