#pragma once

#include "mc_Types.h"
#include "mc_DecompContext.h"

#include <type_traits> // std::is_same_v

// unlike the index codec, the stuff here seems to not be based on meshoptimizer with the exception of the unzigzag utility function
// they generate a "decoding table" with pairs of values + index sizes in bits
// then they read the three bit streams in parallel while referencing this table to generate the output
// this output is then decoded into the vertex encoding info (vertex groups each with a certain number of vertices, backrefs, and a backref offset)
// the final output is then derived using this encoding info and a new decoding stream

namespace mc {

const inline u32 cVertexGroupEncodingTable[26][2] = {
    // each entry is { bitCount, baseCount }
    // bitCount is the number of bits to read from the stream to find the number of additional vertices in the group
    // baseCount is the base number of vertices in the group
    { 1, 0 }, { 1, 2 }, { 1, 4 }, { 1, 6 }, { 2, 8 }, { 2, 0xc }, { 3, 0x10 }, { 3, 0x18 },
    { 4, 0x20 }, { 5, 0x30 }, { 6, 0x50 }, { 7, 0x90 }, { 8, 0x110 }, { 9, 0x210 }, { 0xa, 0x410 },
    { 0xb, 0x810 }, { 0xc, 0x1010 }, { 0xd, 0x2010 }, { 0xe, 0x4010 }, { 0xf, 0x8010 }, { 0x10, 0x10010 },
    { 0x12, 0x20010 }, { 0x14, 0x60010 }, { 0x18, 0x160010 }, { 0x1c, 0x116010 }, { 0, 0x11160010 },
};

enum class ByteStreamEncoding : u32 {
    _00, _01, _02 /* repeat */,
};

struct Encoding1Struct {
    u64 indexMasks[4];
    u32 _20;
    u64 _28;
    u64 _30;
    u64 _38;
    u64 _40;
    u64 _48;
    u64 _50;
    u64 _58;
    u64 _60;
    u64 _68;
};

struct alignas(0x40) DecodingContext {
    ByteStreamEncoding encoding;
    u32 maxIndexBitSize;
    u32 repeatEncodingValue;
    Encoding1Struct _10;
    u16 decodingTable[0x1800];
};
static_assert(sizeof(DecodingContext) == 0x3080);

template <typename T>
struct DecodeInfo {
    T* output;
    s32 totalSize;
    s32 count;
    s32 elementSize;
};

struct BufferView {
    const u8* ptr;
    u32 _08;
    u32 offset;
};

void GenDecodingTable(DecodingContext* decodeCtx, DecompContext& decompCtx);

template <typename T>
static void DecodeFunction0(Encoding1Struct& ctx, DecodeInfo<T>& info, BufferView& buffer, const u16* tbl, u32 bitSize) {
    if constexpr (!std::is_same_v<T, u8> && !std::is_same_v<T, u16>)
        static_assert(false, "Only supports u8 and u16");
    const u8* inStream = buffer.ptr + buffer.offset;
    u32 unk = ctx._20 & 0xf;
    if (unk != 0xf) {
        unk ^= 0xf;
        while (unk != 0) {
            u8 b = *inStream++;
            u32 nbytes = b & 0xf;
            u64 value = b >> 4;
            if (nbytes) {
                if (nbytes > 3) {
                    for (s32 i = (b & 3) - nbytes; i != 0; i += 4) {
                        value = (value << 0x10) | (inStream[0] << 8) | inStream[1];
                        value = (value << 0x10) | (inStream[2] << 8) | inStream[3];
                        inStream += 4;
                    }
                }
                if (b & 3) {
                    for (u32 i = (b & 3); i != 0; --i) {
                        value = (value << 8) | *inStream++;
                    }
                }
            }
            // index is 0, 1, 2, or 3
            ctx.indexMasks[Clz(unk & -unk) ^ 0x1f] = value + 0x80000000;
            unk = (unk & -unk) ^ unk;
            
        }
        ctx._20 |= 0xf;
    }

    u32 mask = ~(-1 << (bitSize & 0x1f));
    const u32* inStream32 = reinterpret_cast<const u32*>(inStream);
    u32 stride = info.elementSize << 2;
    T* outPtr = info.output;
    const u32* src = reinterpret_cast<const u32*>(tbl);
    if (info.count > 3) {
        u64 v0 = ctx.indexMasks[0];
        u64 v1 = ctx.indexMasks[1];
        u64 v2 = ctx.indexMasks[2];
        u64 v3 = ctx.indexMasks[3];
        for (u32 i = info.count >> 2; i != 0; --i) {
            u32 index0 = v0 & mask;
            u32 value0 = src[index0];
            outPtr[0] = static_cast<T>(tbl[0x1000 + index0]);
            v0 = (v0 >> (bitSize & 0x3f)) * static_cast<u64>(value0 >> 0x10) + static_cast<u64>(value0 & 0xffff);

            u32 index1 = v1 & mask;
            u32 value1 = src[index1];
            outPtr[info.elementSize] = static_cast<T>(tbl[0x1000 + index1]);
            v1 = (v1 >> (bitSize & 0x3f)) * static_cast<u64>(value1 >> 0x10) + static_cast<u64>(value1 & 0xffff);

            u32 index2 = v2 & mask;
            u32 value2 = src[index2];
            outPtr[info.elementSize * 2] = static_cast<T>(tbl[0x1000 + index2]);
            v2 = (v2 >> (bitSize & 0x3f)) * static_cast<u64>(value2 >> 0x10) + static_cast<u64>(value2 & 0xffff);

            u32 index3 = v3 & mask;
            u32 value3 = src[index3];
            outPtr[info.elementSize * 3] = static_cast<T>(tbl[0x1000 + index3]);
            v3 = (v3 >> (bitSize & 0x3f)) * static_cast<u64>(value3 >> 0x10) + static_cast<u64>(value3 & 0xffff);

            if (v0 >> 0x1f == 0)
                v0 = *inStream32++ | (v0 << 0x20);
            
            if (v1 >> 0x1f == 0)
                v1 = *inStream32++ | (v1 << 0x20);
            
            if (v2 >> 0x1f == 0)
                v2 = *inStream32++ | (v2 << 0x20);
            
            if (v3 >> 0x1f == 0)
                v3 = *inStream32++ | (v3 << 0x20);
            
            outPtr += stride;
        }
        ctx.indexMasks[0] = v0;
        ctx.indexMasks[1] = v1;
        ctx.indexMasks[2] = v2;
        ctx.indexMasks[3] = v3;
    }
    u64* v = ctx.indexMasks;
    for (u32 i = info.count & 3; i != 0; --i) {
        u32 index = *v & mask;
        u32 value = src[index];
        *outPtr = static_cast<T>(tbl[0x1000 + index]);
        outPtr += info.elementSize;
        u64 v0 = (*v >> (bitSize & 0x3f)) * static_cast<u64>(value >> 0x10) + static_cast<u64>(value & 0xffff);
        if (v0 >> 0x1f == 0)
            v0 = *inStream32++ | (v0 << 0x20);
        *v = v0;
        ++v;
    }
    buffer.offset = static_cast<u32>(reinterpret_cast<uintptr_t>(inStream32) - reinterpret_cast<uintptr_t>(buffer.ptr));
}

template <typename T>
static void DecodeFunction1(T* dst, u32 size, u32 count, DecompContext& ctx, const u32* src, u32 bitSize) {
    if constexpr (!std::is_same_v<T, u8> && !std::is_same_v<T, u16>)
        static_assert(false, "Only supports u8 and u16");
    u64 shift = 0x40u - bitSize;
    u32 offset0 = size * 2;
    u32 offset1 = size * 3;
    struct {
        T* outBuf0;
        T* outBuf1;
        T* outBuf2;
    } outBufs;
    outBufs.outBuf1 = dst + size;
    outBufs.outBuf2 = dst + offset0;

    CompositeBitOffsetContext bitOffsets {
        ctx.bitStream0.GetBitOffset(), ctx.bitStream1.GetBitOffset(), ctx.bitStream2.GetBitOffset(),
    };
    CompositeBitRemainderContext remainders {
        ctx.bitStream0.GetRemainder(), ctx.bitStream1.GetRemainder(), ctx.bitStream2.GetRemainder(),
    };

    T* out = dst;
    if (count > 0xb) {
        u64 offset2 = offset1 * 2;
        u64 offset3 = offset1 * 3;
        for (u32 i = count / 0xc; i != 0; --i) {
            u64 value0 = remainders.value0 | ctx.bitStream0.ReadRaw();
            u64 value1 = remainders.value1 | ctx.bitStream1.ReadRawForwards();
            u64 value2 = remainders.value2 | ctx.bitStream2.ReadRaw();

            u32 v0 = src[value0 >> (shift & 0x3f) & 0xffffffff]; // two u16s, bottom one is the value, top one is the number of bits in the index
            u32 v1 = src[value1 >> (shift & 0x3f) & 0xffffffff];
            u32 v2 = src[value2 >> (shift & 0x3f) & 0xffffffff];

            out[0] = static_cast<T>(v0);
            v0 >>= 0x10;
            value0 <<= v0 & 0x3f;

            out[size] = static_cast<T>(v1);
            v1 >>= 0x10;
            value1 <<= v1 & 0x3f;

            out[offset0] = static_cast<T>(v2);
            v2 >>= 0x10;
            value2 <<= v2 & 0x3f;

            u32 v3 = src[value0 >> (shift & 0x3f) & 0xffffffff];
            u32 v4 = src[value1 >> (shift & 0x3f) & 0xffffffff];
            u32 v5 = src[value2 >> (shift & 0x3f) & 0xffffffff];

            out[offset1] = v3;
            v3 >>= 0x10;
            value0 <<= v3 & 0x3f;

            out[size + offset1] = v4;
            v4 >>= 0x10;
            value1 <<= v4 & 0x3f;

            out[offset0 + offset1] = v5;
            v5 >>= 0x10;
            value2 <<= v5 & 0x3f;

            u32 v6 = src[value0 >> (shift & 0x3f) & 0xffffffff];
            u32 v7 = src[value1 >> (shift & 0x3f) & 0xffffffff];
            u32 v8 = src[value2 >> (shift & 0x3f) & 0xffffffff];

            out[offset2] = v6;
            v6 >>= 0x10;
            value0 <<= v6 & 0x3f;

            out[size + offset2] = v7;
            v7 >>= 0x10;
            value1 <<= v7 & 0x3f;

            out[offset0 + offset2] = v8;
            v8 >>= 0x10;
            value2 <<= v8 & 0x3f;

            u32 v9 = src[value0 >> (shift & 0x3f) & 0xffffffff];
            u32 v10 = src[value1 >> (shift & 0x3f) & 0xffffffff];
            u32 v11 = src[value2 >> (shift & 0x3f) & 0xffffffff];

            out[offset3] = v9;
            v9 >>= 0x10;
            value0 <<= v9 & 0x3f;

            out[size + offset3] = v10;
            v10 >>= 0x10;
            value1 <<= v10 & 0x3f;

            out[offset0 + offset3] = v11;
            v11 >>= 0x10;
            value2 <<= v11 & 0x3f;

            bitOffsets.offset0 = (bitOffsets.offset0 | 0x38) - (v0 + v3 + v6 + v9);
            bitOffsets.offset1 = (bitOffsets.offset1 | 0x38) - (v1 + v4 + v7 + v10);
            bitOffsets.offset2 = (bitOffsets.offset2 | 0x38) - (v2 + v5 + v8 + v11);

            out += offset1 * 4;
            ctx.bitStream0.SetBitOffset(bitOffsets.offset0);
            ctx.bitStream1.SetBitOffset(bitOffsets.offset1);
            ctx.bitStream2.SetBitOffset(bitOffsets.offset2);

            remainders.value0 = value0;
            remainders.value1 = value1;
            remainders.value2 = value2;
        }

        outBufs.outBuf1 = out + size;
        outBufs.outBuf2 = out + offset0;
    }

    u32 remaining = count % 0xc;
    if (remaining) {
        u64 value0 = remainders.value0 | ctx.bitStream0.ReadRaw();
        u64 value1 = remainders.value1 | ctx.bitStream1.ReadRawForwards();
        u64 value2 = remainders.value2 | ctx.bitStream2.ReadRaw();

        bitOffsets.offset0 |= 0x38;
        bitOffsets.offset1 |= 0x38;
        bitOffsets.offset2 |= 0x38;

        for (; remaining > 3; remaining -= 3) {
            u32 v0 = src[value0 >> (shift & 0x3f) & 0xffffffff];
            u32 v1 = src[value1 >> (shift & 0x3f) & 0xffffffff];
            u32 v2 = src[value2 >> (shift & 0x3f) & 0xffffffff];

            out[0] = static_cast<T>(v0);
            v0 >>= 0x10;
            value0 <<= v0 & 0x3f;

            outBufs.outBuf1[0] = static_cast<T>(v1);
            v1 >>= 0x10;
            value1 <<= v1 & 0x3f;

            outBufs.outBuf2[0] = static_cast<T>(v2);
            v2 >>= 0x10;
            value2 <<= v2 & 0x3f;

            bitOffsets.offset0 -= v0;
            bitOffsets.offset1 -= v1;
            bitOffsets.offset2 -= v2;

            remainders.value0 = value0;
            remainders.value1 = value1;
            remainders.value2 = value2;

            out += offset1;
            outBufs.outBuf1 += offset1;
            outBufs.outBuf2 += offset1;
        }

        remainders.value0 = value0;
        remainders.value1 = value1;
        remainders.value2 = value2;

        // TODO: this can just be a for loop since remaining is at least 1 here
        u32 v0 = src[value0 >> (shift & 0x3f) & 0xffffffff];
        out[0] = static_cast<T>(v0);
        v0 >>= 0x10;
        value0 <<= v0 & 0x3f;
        remainders.value0 = value0;
        bitOffsets.offset0 -= v0;

        --remaining;
        if (remaining) {
            u32* offset = &bitOffsets.offset0;
            u64* remainder = &remainders.value0;
            T** outBuf = &outBufs.outBuf0;
            for (; remaining != 0; --remaining) {
                ++outBuf;
                ++offset;
                ++remainder;
                
                u32 v = src[*remainder >> (shift & 0x3f) & 0xffffffff];
                (*outBuf)[0] = static_cast<T>(v);
                v >>= 0x10;
                *remainder = *remainder << (v & 0x3f);
                *offset = *offset - v;
            }
        }
    }

    ctx.bitStream0.SetBitOffset(bitOffsets.offset0);
    ctx.bitStream1.SetBitOffset(bitOffsets.offset1);
    ctx.bitStream2.SetBitOffset(bitOffsets.offset2);
    ctx.bitStream0.SetRemainder(remainders.value0);
    ctx.bitStream1.SetRemainder(remainders.value1);
    ctx.bitStream2.SetRemainder(remainders.value2);
}

template <typename T>
static void MemSet(T* dst, u32 value, u32 count) {
    if constexpr (!std::is_same_v<T, u8> && !std::is_same_v<T, u16>)
        static_assert(false, "Only supports u8 and u16");
    *dst = static_cast<T>(value);
    dst[count - 1] = static_cast<T>(value);
    if (count > 2) {
        dst[1] = static_cast<T>(value);
        dst[2] = static_cast<T>(value);
        dst[count - 2] = static_cast<T>(value);
        dst[count - 3] = static_cast<T>(value);
        if (count > 6) {
            if constexpr (std::is_same_v<T, u8>) {
                dst[3] = static_cast<T>(value);
                dst[count - 4] = static_cast<T>(value);
                if (count > 8) {
                    u32 v = static_cast<u8>(value) * 0x1010101;
                    u32 offset = -static_cast<u32>(reinterpret_cast<uintptr_t>(dst)) & 3;
                    u32* ptr = reinterpret_cast<u32*>(dst + offset);
                    u32 num = (count - offset) & 0xfffffffc;
                    u32 index = num >> 2;
                    *ptr = v;
                    ptr[index - 1] = v;
                    if (num > 8) {
                        ptr[1] = v;
                        ptr[2] = v;
                        ptr[index - 2] = v;
                        ptr[index - 3] = v;
                        if (num > 0x18) {
                            ptr[3] = v;
                            ptr[4] = v;
                            ptr[5] = v;
                            ptr[6] = v;
                            ptr[index - 4] = v;
                            ptr[index - 5] = v;
                            ptr[index - 6] = v;
                            ptr[index - 7] = v;
                            if (num > 0x38) {
                                u64 v64 = value * 0x101010101010101;
                                // idk if I'm just crazy but if v = 0xffffffff, this makes v64 0xfffffffeffffffff for some reason???
                                // u64 v64 = v | (static_cast<u64>(v) << 0x20);
                                u32 offset = (reinterpret_cast<uintptr_t>(ptr) & 4) | 0x18;
                                u64* ptr64 = reinterpret_cast<u64*>(reinterpret_cast<uintptr_t>(ptr) + offset);
                                for (s32 i = -((num - offset) >> 5); i != 0; ++i) {
                                    ptr64[0] = v64;
                                    ptr64[1] = v64;
                                    ptr64[2] = v64;
                                    ptr64[3] = v64;
                                    ptr64 += 4;
                                }
                            }
                        }
                    }
                }
            } else if constexpr (std::is_same_v<T, u16>) {
                u32 v = static_cast<u16>(value) * 0x10001;
                u32 offset = -static_cast<u32>(reinterpret_cast<uintptr_t>(dst)) & 3;
                u32* ptr = reinterpret_cast<u32*>(reinterpret_cast<u8*>(dst) + offset);
                u32 num = (count * 2 - offset) & 0xfffffffc;
                u32 index = num >> 2;
                *ptr = v;
                ptr[index - 1] = v;
                if (num > 8) {
                    ptr[1] = v;
                    ptr[2] = v;
                    ptr[index - 2] = v;
                    ptr[index - 3] = v;
                    if (num > 0x18) {
                        ptr[3] = v;
                        ptr[4] = v;
                        ptr[5] = v;
                        ptr[6] = v;
                        ptr[index - 4] = v;
                        ptr[index - 5] = v;
                        ptr[index - 6] = v;
                        ptr[index - 7] = v;
                        if (num > 0x38) {
                            u64 v64 = value * 0x1000100010001;
                            // u64 v64 = v | (static_cast<u64>(v) << 0x20u);
                            u32 offset = (reinterpret_cast<uintptr_t>(ptr) & 4) | 0x18;
                            u64* ptr64 = reinterpret_cast<u64*>(reinterpret_cast<uintptr_t>(ptr) + offset);
                            for (s32 i = -((num - offset) >> 5); i != 0; ++i) {
                                ptr64[0] = v64;
                                ptr64[1] = v64;
                                ptr64[2] = v64;
                                ptr64[3] = v64;
                                ptr64 += 4;
                            }
                        }
                    }
                }
            }
        }
    }
}

template <typename T>
static void DecodeFunction2(T* dst, u32 value, u32 count, u32 stride) {
    if constexpr (!std::is_same_v<T, u8> && !std::is_same_v<T, u16>)
        static_assert(false, "Only supports u8 and u16");
    if (stride != 1) {
        for (u32 i = count & 3; i != 0; --i) {
            *dst = static_cast<T>(value);
            dst += stride;
        }
        if (count > 3) {
            for (u32 i = count >> 2; i != 0; --i) {
                dst[0] = static_cast<T>(value);
                dst[stride] = static_cast<T>(value);
                dst[stride * 2] = static_cast<T>(value);
                dst[stride * 3] = static_cast<T>(value);
                dst += stride * 4;
            }
        }
    } else {
        MemSet(dst, value, count);
    }
}

template <typename T>
static void DecodeTable(T* dst, s32 size, s32 count, DecodingContext* decodeCtx, BufferView& view, DecompContext& decompCtx) {
    if constexpr (!std::is_same_v<T, u8> && !std::is_same_v<T, u16>)
        static_assert(false, "Only supports u8 and u16");
    switch (decodeCtx->encoding) {
        case ByteStreamEncoding::_00: {
            DecodeInfo<T> info;
            info.totalSize = size * count;
            info.output = dst;
            info.count = count;
            info.elementSize = size;
            DecodeFunction0(decodeCtx->_10, info, view, decodeCtx->decodingTable, decodeCtx->maxIndexBitSize);
            break;
        }
        case ByteStreamEncoding::_01:
            DecodeFunction1(dst, size, count, decompCtx, reinterpret_cast<const u32*>(decodeCtx->decodingTable), decodeCtx->maxIndexBitSize);
            break;
        case ByteStreamEncoding::_02:
            DecodeFunction2(dst, decodeCtx->repeatEncodingValue, count, size);
            break;
        default:
            UNREACHABLE_DEFAULT_CASE
    }
}

template <typename T>
static void DecodeByteStream(T* dst, s32 totalSize, s32 tblCount, u32 elementSize, DecodingContext* decodeCtx, DecompContext& decompCtx) {
    if constexpr (!std::is_same_v<T, u8> && !std::is_same_v<T, u16>)
        static_assert(false, "Only supports u8 and u16");
    
    BufferView view;
    view._08 = 0;
    view.offset = 0;
    view.ptr = decompCtx.currentPos;
    
    if (tblCount < 1) {
        decompCtx.currentPos = view.ptr;
    } else {
        u32 bytesPerTable;
        if constexpr (std::is_same_v<T, u8>)
            bytesPerTable = tblCount != 0 ? totalSize / tblCount : 0;
        else
            bytesPerTable = tblCount != 0 ? (totalSize >> 1 & 0x7fffffff) / tblCount : 0;
        u32 alignAdd = ~(-1 << (elementSize & 0x1f)); // 2 ^ elementSize - 1, for alignment
        s32 index = 0;
        u32 size = 0;
        while (index < tblCount) {
            GenDecodingTable(decodeCtx, decompCtx);
            u32 bitOffset = decompCtx.bitStream0.GetBitOffset();
            u64 remainder = decompCtx.bitStream0.GetRemainder();
            u64 value = decompCtx.bitStream0.ReadRaw() | remainder;
            s32 nbits = Clz(value) << 1 | 1;
            decompCtx.bitStream0.SetBitOffset((bitOffset | 0x38) - nbits);
            decompCtx.bitStream0.SetRemainder(value << nbits);
            s32 remaining = value >> (0x40 - nbits);
            // loop through sub tables
            do {
                s32 tblSize = remaining << (elementSize & 0x1f);
                s32 remainingSubTblSize = (bytesPerTable - size + alignAdd) >> (elementSize & 0x1f);
                u32 offset = index + size * tblCount;
                u32 elementCount = remainingSubTblSize > remaining ? tblSize : (bytesPerTable - size);
                if (remaining >= remainingSubTblSize)
                    ++index;
                size = remainingSubTblSize > remaining ? (size + tblSize) : 0;
                remaining -= ((elementCount + alignAdd) >> (elementSize & 0x1f));
                DecodeTable<T>(dst + offset, tblCount, elementCount, decodeCtx, view, decompCtx);
            } while (remaining > 0);
        }
        decompCtx.currentPos = view.ptr + view.offset;
    }
}

} // namespace mc