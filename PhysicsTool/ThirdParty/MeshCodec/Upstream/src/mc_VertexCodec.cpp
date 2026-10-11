#include "mc_VertexCodec.h"

#include <algorithm> // std::min

/**
 * meshoptimizer - version 0.22
 *
 * Copyright (C) 2016-2025, by Arseny Kapoulkine (arseny.kapoulkine@gmail.com)
 * Report bugs and download new versions at https://github.com/zeux/meshoptimizer
 *
 * This library is distributed under the MIT License. See notice at the end of this file.
 */

namespace meshopt {

template <typename T>
inline T unzigzag(T v) {
    return -(v & 1) ^ (v >> 1);
}

} // namespace meshopt

// everything here is scuffed bc I don't actually understand what it's doing

namespace mc {

u64 GenDecodingTableShifts_(u16* dst, u32 count, DecompContext& ctx, u32 a4, u32 a5, u64 bitInfo) {
    u32 bitOffset = ctx.bitStream0.GetBitOffset();
    u64 remainder = ctx.bitStream0.GetRemainder();

    s32 unkValue = 0;

    u32 unk = a4 << 10;
    u32 bits = bitInfo & 0xffffffff;
    u32 shift = bitInfo >> 0x20;
    while (count != 0) { // the control flow in this loop feels weird, there's probably a better way to do this?
        --count;
        u64 value = ctx.bitStream0.ReadRaw() | remainder;
        u32 uVar2 = unk >> 10;
        u32 nbits = uVar2 + Clz(value) * 2 + 1;
        bitOffset = (bitOffset | 0x38) - nbits;
        ctx.bitStream0.SetBitOffset(bitOffset);
        remainder = value << nbits;
        u32 raw = (-1 << (uVar2 & 0x1f)) + unkValue + (value >> (0x40 - nbits));
        bits += meshopt::unzigzag(raw);
        shift -= bits;
        *dst++ = static_cast<u16>(bits);

        if (count == 0) break;

        unkValue = 0;
        unk = std::min(0x8000 - 0x400 * Clz(shift), (unk * (0x10 - a5) + (0x8000 - 0x400 * Clz(raw)) * a5) >> 4);

        if (raw >> (uVar2 & 0x1f)) continue;

        value = ctx.bitStream0.ReadRaw() | remainder;
        nbits = 0x20 - Clz(count);

        u32 i;
        if (Clz(value) >= nbits) {
            remainder = value << nbits;
            bitOffset = (bitOffset | 0x38) - nbits;
            i = count;
            count = 0;
        } else {
            nbits = Clz(value) << 1 | 1;
            remainder = value << nbits;
            bitOffset = (bitOffset | 0x38) - nbits;
            i = (value >> (0x40 - nbits)) - 1;
            count -= i;
        }

        ctx.bitStream0.SetBitOffset(bitOffset);

        for (; i != 0; --i) {
            value = ctx.bitStream0.ReadRaw() | remainder;
            nbits = unk >> 10;
            remainder = value << (nbits & 0x3f);
            bitOffset = (bitOffset | 0x38) - nbits;
            ctx.bitStream0.SetBitOffset(bitOffset);
            raw = (value >> 1) >> (0x3f - nbits);
            bits += meshopt::unzigzag(raw);
            shift -= bits;
            *dst++ = static_cast<u16>(bits);
            unk = std::min(0x8000 - 0x400 * Clz(shift), (unk * (0x10 - a5) + (0x8000 - 0x400 * Clz(raw)) * a5) >> 4);
        }

        unkValue = 1 << (unk >> 10);
    }

    ctx.bitStream0.SetBitOffset(bitOffset);
    ctx.bitStream0.SetRemainder(remainder);

    return bits | static_cast<u64>(shift) << 0x20;
}

u64 GenDecodingTableValues_(u16* dst, u32 count, DecompContext& ctx, u32 a4, u32 a5, u32 base) {
    u32 bitOffset = ctx.bitStream0.GetBitOffset();
    u64 remainder = ctx.bitStream0.GetRemainder();

    s32 unkValue = 0;
    u32 unk = a4 << 10;

    while (count != 0) {
        --count;
        u64 value = ctx.bitStream0.ReadRaw() | remainder;
        u32 uVar3 = unk >> 10;
        u32 nbits = uVar3 + Clz(value) * 2 + 1;
        remainder = value << nbits;
        bitOffset = (bitOffset | 0x38) - nbits;
        ctx.bitStream0.SetBitOffset(bitOffset);
        u32 raw = (-1 << (uVar3 & 0x1f)) + unkValue + (value >> (0x40 - nbits));
        base += raw;
        *dst = static_cast<u16>(base);
        ++base;
        ++dst;

        if (count == 0) break;

        unkValue = 0;
        unk = (unk * (0x10 - a5) + (0x8000 - Clz(raw) * 0x400) * a5) >> 4;

        if (raw >> (uVar3 & 0x1f)) continue;

        value = ctx.bitStream0.ReadRaw() | remainder;
        nbits = 0x20 - Clz(count);

        u32 i;
        if (Clz(value) >= nbits) {
            remainder = value << (nbits & 0x3f);
            bitOffset = (bitOffset | 0x38) - nbits;
            i = count;
            count = 0;
        } else {
            nbits = Clz(value) << 1 | 1;
            remainder = value << (nbits & 0x3f);
            bitOffset = (bitOffset | 0x38) - nbits;
            i = (value >> (0x40 - nbits)) - 1;
            count -= i;
        }

        ctx.bitStream0.SetBitOffset(bitOffset);

        for (; i != 0; --i) {
            value = ctx.bitStream0.ReadRaw() | remainder;
            nbits = unk >> 10;
            remainder = value << (nbits & 0x3f);
            bitOffset = (bitOffset | 0x38) - nbits;
            ctx.bitStream0.SetBitOffset(bitOffset);
            raw = (value >> 1) >> (0x3f - nbits);
            base += raw;
            *dst = base;
            ++base;
            ++dst;
            unk = (unk * (0x10 - a5) + (0x8000 - Clz(raw) * 0x400) * a5) >> 4;
        }

        unkValue = 1 << (unk >> 10 & 0x1f);
    }

    ctx.bitStream0.SetBitOffset(bitOffset);
    ctx.bitStream0.SetRemainder(remainder);

    return base;
}

void GenDecodingTable0(void* dst, s32 count0, s32 maxIndexBitSize, DecompContext& ctx) {
    u16* values = reinterpret_cast<u16*>(dst) + (0x1800 - count0);
    u16* shifts = reinterpret_cast<u16*>(dst) + (0x1000 - count0);

    if (count0 < 0xb) {
        u32 bitOffset = ctx.bitStream0.GetBitOffset();
        u64 remainder = ctx.bitStream0.GetRemainder();
        u32 unk = 0;
        u32 accumulator = 0;
        for (s32 i = 0; i < count0; ++i) {
            u64 value = ctx.bitStream0.ReadRaw() | remainder;
            u32 nbits = (unk >> 10) + Clz(value) * 2 + 1;
            remainder = value << (nbits & 0x3f);
            bitOffset = (bitOffset | 0x38) - nbits;
            u32 delta = (-1 << (unk >> 10 & 0x1f)) + (value >> (0x40 - nbits));
            accumulator += delta;
            values[i] = static_cast<u16>(accumulator);
            ++accumulator;
            unk = (unk * 0xc - Clz(delta) * 0x1000 + 0x20000) >> 4; // what is this
            ctx.bitStream0.SetBitOffset(bitOffset);
            ctx.bitStream0.SetRemainder(remainder);
        }
    } else {
        GenDecodingTableValues_(values, count0, ctx, 0, 3, 0);
    }
    const u32 unk = count0 < 0xb ? 0xf : 0xe;
    s32 max = 1 << maxIndexBitSize;
    const s32 someCount = count0 != 0 ? max / count0 : 0;
    if (maxIndexBitSize < 4)
    maxIndexBitSize = 3;
    GenDecodingTableShifts_(shifts, count0 - 1, ctx, maxIndexBitSize - 2, unk, someCount | (static_cast<u64>(max) << 0x20));

    u16* out0 = reinterpret_cast<u16*>(dst);
    u16* out1 = reinterpret_cast<u16*>(dst) + 0x1000;
    if (count0 > 1) {
        for (s32 i = 0; i != count0 - 1; ++i) {
            u16 shift = *shifts++;
            u16 value = *values++;
            u32 index = 0;
            if (shift > 1) {
                for (u32 j = shift >> 1; j != 0; --j) {
                    out0[0] = index++;
                    out0[1] = shift;
                    out0[2] = index++;
                    out0[3] = shift;
                    out1[0] = value;
                    out1[1] = value;
                    out0 += 4;
                    out1 += 2;
                }
            }
            if (shift & 1) {
                out0[0] = index;
                out0[1] = shift;
                out1[0] = value;
                out0 += 2;
                ++out1;
            }
            max -= shift;
        }
    }

    u16 index;
    u16 value = *values;
    if (max < 2) {
        index = 0;
    } else {
        index = 0;
        for (u32 i = max >> 1; i != 0; --i) {
            out0[0] = index++;
            out0[1] = max;
            out0[2] = index++;
            out0[3] = max;
            out1[0] = value;
            out1[1] = value;
            out0 += 4;
            out1 += 2;
        }
    }
    if (max & 1) {
        out0[0] = index;
        out0[1] = max;
        out1[0] = value;
    }
}

void GenDecodingTable1(void* dst, DecompContext& ctx, s32 count0, s32 maxIndexBitSize) {
    u32 shiftIndex = 0x800 - maxIndexBitSize;
    u32 valueIndex = 0x800 - count0;

    u32 bitOffset = ctx.bitStream0.GetBitOffset() | 0x38;
    u64 remainder = ctx.bitStream0.ReadRaw() | ctx.bitStream0.GetRemainder();

    s32 bitsRemaining = count0;
    u16* out = reinterpret_cast<u16*>(dst);

    u32 endIndex;
    if (maxIndexBitSize < 2) {
        endIndex = shiftIndex;
    } else {
        for (s32 i = 0; i != maxIndexBitSize - 1; ++i) {
            if (bitOffset < 10) { // advance stream
                remainder |= ctx.bitStream0.ReadRaw();
                bitOffset |= 0x38;
                ctx.bitStream0.SetBitOffset(bitOffset);
            }

            u32 mask = -1 << (i + 1);
            s32 max = bitsRemaining - 1;
            mask = max < s32(~mask) ? max : ~mask;
            
            u32 nbits = 0x20 - Clz(mask); // just becomes i + 1 if the condition was met
            u32 value = remainder >> (0x40 - nbits);
            remainder <<= nbits & 0x3f;
            bitOffset -= (nbits & 0x3f);
            ctx.bitStream0.SetBitOffset(bitOffset);
            out[(shiftIndex + i) * 2 + 1] = static_cast<u16>(value); // this becomes the shift when decoding
            bitsRemaining -= value;
        }
        endIndex = 0x7ff;
    }

    ctx.bitStream0.SetBitOffset(bitOffset);

    out[endIndex * 2 + 1] = static_cast<u16>(bitsRemaining);

    u16* shiftBuffer = reinterpret_cast<u16*>(dst) + shiftIndex * 2;
    u16* valueBuffer = reinterpret_cast<u16*>(dst) + valueIndex * 2;

    u32 unk = 0;
    u32 indexBits = 0;
    s32 accumulator = 0;

    for (u32 i = count0; i != 0; --i) {
        if (indexBits == 0) {
            while (indexBits == 0) {
                indexBits = shiftBuffer[1]; // value from the previous step
                shiftBuffer += 2;
            }
            accumulator = 0;
        }

        remainder |= ctx.bitStream0.ReadRaw();
        u32 nbits = (unk >> 10) + Clz(remainder) * 2 + 1;
        u32 delta = (-1 << (unk >> 10 & 0x1f)) + (remainder >> (0x40 - nbits));
        remainder <<= nbits;
        bitOffset = (bitOffset | 0x38) - nbits;
        ctx.bitStream0.SetBitOffset(bitOffset);

        accumulator += delta;
        *valueBuffer = static_cast<u16>(accumulator); // this becomes the value written to the out buffer after decoding
        valueBuffer += 2;
        ++accumulator;
        --indexBits;
        unk = (unk * 0xc - Clz(delta) * 0x1000 + 0x20000) >> 4;
    }

    // propagate values to the rest of the table
    u32 base = 0;
    u32 remainingShift;
    u32* outBuf = reinterpret_cast<u32*>(dst);
    if (maxIndexBitSize < 2) {
        remainingShift = out[shiftIndex * 2 + 1];
    } else {
        u32 valueCount = 1 << ((maxIndexBitSize - 1) & 0x1f);
        for (s32 i = 1; i != maxIndexBitSize; ++i) {
            u32 v = out[shiftIndex * 2 + 1];

            if (v != 0) {
                if (valueCount == 0) {
                    valueIndex += v;
                } else {
                    u32 count = v;
                    if (v & 1) {
                        u32 j = 0;
                        for (; j < valueCount; j += 2) {
                            // these are meant to be 4 consecutive u16 stores but they end up as a single u64 store
                            // maybe the compiler will know to optimize it anyways
                            outBuf[base + j] = (i << 0x10) | out[valueIndex * 2];
                            outBuf[base + j + 1] = (i << 0x10) | out[valueIndex * 2];
                        }
                        ++valueIndex;
                        base += j;
                        count = v - 1;
                    }
                    if (v != 1) {
                        for (; count != 0; count -= 2) {
                            u32 base0 = 0;
                            u32 base1 = 0;
                            for (; base0 < valueCount; base0 += 2) {
                                outBuf[base + base0] = (i << 0x10) | out[valueIndex * 2];
                                outBuf[base + base0 + 1] = (i << 0x10) | out[valueIndex * 2];
                            }
                            ++valueIndex;
                            base += base0;
                            for (; base1 < valueCount; base1 += 2) {
                                outBuf[base + base1] = (i << 0x10) | out[valueIndex * 2];
                                outBuf[base + base1 + 1] = (i << 0x10) | out[valueIndex * 2];
                            }
                            ++valueIndex;
                            base += base1;
                        }
                    }
                }
            }

            ++shiftIndex;
            valueCount >>= 1;
        }
        remainingShift = out[0xfff];
    }

    if (remainingShift) {
        u32 count = remainingShift;
        if (remainingShift & 1) {
            outBuf[base++] = (maxIndexBitSize << 0x10) | out[valueIndex * 2];
            --count;
            ++valueIndex;
        }
        if (remainingShift != 1) {
            for (; count != 0; count -= 2) {
                outBuf[base++] = (maxIndexBitSize << 0x10) | out[valueIndex * 2];
                ++valueIndex;
                outBuf[base++] = (maxIndexBitSize << 0x10) | out[valueIndex * 2];
                ++valueIndex;
            }
        }
    }

    ctx.bitStream0.SetBitOffset(bitOffset);
    ctx.bitStream0.SetRemainder(remainder);
}

void GenDecodingTable(DecodingContext* decodeCtx, DecompContext& decompCtx) {
    u64 remainder = decompCtx.bitStream0.GetRemainder();
    u32 bitOffset = decompCtx.bitStream0.GetBitOffset() | 0x38;
    u64 value = decompCtx.bitStream0.ReadRaw() | remainder;

    /*
    1 bit -> do second read
    4 bits -> count low four bits (+1 for value)
    1 bit -> do third read
    3 bits -> count next three bits (+1 for value)
    1 bit -> do fourth read
    2 bits -> count next two bits (+1 for value)
    7 bits -> count next seven bits (+1 for value)
    */
    u32 bitCount;
    if (value >> 0x3f) {
        bitOffset -= 9;
        remainder = value << 9;
        bitCount = ((value >> 0x3b & 0xf) | (value >> 0x33 & 0x70)) + 0x11;
        if (value >> 0x3a & 1) {
            bitCount += (value >> 0x2d & 0x180) + 0x80;
            bitOffset -= 3;
            remainder <<= 3;
            if (value >> 0x36 & 1) {
                bitCount += (value >> 0x24 & 0xfe00) + 0x200;
                bitOffset -= 7;
                remainder <<= 7;
            }
        }
    } else {
        bitOffset -= 5;
        u32 count = value >> 0x3b & 0xf;
        remainder = value << 5;
        if (count == 0) { // stream encoded using repeat encoding
            u32 repeatValue = 0;
            do { // read varint for repeated value
                value = remainder;
                remainder <<= 8;
                repeatValue = ((value >> 0x38) & 0x7f) | repeatValue << 7;
                bitOffset -= 8;
            } while (value >> 0x3f);
            decodeCtx->repeatEncodingValue = repeatValue;
            decodeCtx->encoding = ByteStreamEncoding::_02;
            decompCtx.bitStream0.SetBitOffset(bitOffset);
            decompCtx.bitStream0.SetRemainder(remainder);
            return;
        } else {
            bitCount = count + 1;
        }
    }

    decodeCtx->encoding = static_cast<ByteStreamEncoding>(remainder >> 0x3f);
    decodeCtx->maxIndexBitSize = remainder >> 0x3b & 0xf;
    decompCtx.bitStream0.SetBitOffset(bitOffset - 5);
    decompCtx.bitStream0.SetRemainder(remainder << 5);

    if (decodeCtx->encoding == ByteStreamEncoding::_00) {
        GenDecodingTable0(decodeCtx->decodingTable, bitCount, decodeCtx->maxIndexBitSize, decompCtx);
    } else {
        GenDecodingTable1(decodeCtx->decodingTable, decompCtx, bitCount, decodeCtx->maxIndexBitSize);
    }
}

} // namespace mc