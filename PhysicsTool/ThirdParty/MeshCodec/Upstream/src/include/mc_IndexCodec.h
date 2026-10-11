#pragma once

#include "mc_Types.h"

#include "mc_IndexStreamContext.h"

/*
Since I'm too lazy to do an entire fork of meshoptimizer and
since we're only doing decoding which uses just a small part
of it, we'll just keep the meshoptimizer stuff here

See https://github.com/zeux/meshoptimizer/blob/master/src/indexcodec.cpp
*/

/**
 * meshoptimizer - version 0.22
 *
 * Copyright (C) 2016-2025, by Arseny Kapoulkine (arseny.kapoulkine@gmail.com)
 * Report bugs and download new versions at https://github.com/zeux/meshoptimizer
 *
 * This library is distributed under the MIT License. See notice at the end of this file.
 */

namespace meshopt {

inline mc::u32 decodeVByte(const mc::u8*& data) {
    mc::u8 lead = *data++;

    if (lead < 0x80)
        return lead;

    mc::u32 result = lead & 0x7f;

    for (;;) {
        mc::u8 group = *data++;
        /*
        Nintendo inverts the byte order here, meshoptimizer normally would do:
        result |= u32(group & 0x7f) << shift;
        where each subsequent byte is more significant than the previous
        */
        result = mc::u32(group & 0x7f) | result << 7;

        if (group < 0x80)
            break;
    }

    return result;
}

// modified so that it returns the read size and the value is now a parameter
inline mc::u32 decodeVByte(const mc::u8*& data, mc::u32* output) {
    mc::u8 lead = *data++;

    if (lead < 0x80)
        return lead;

    mc::u32 result = lead & 0x7f;
    mc::u32 size = 1;

    for (;;) {
        mc::u8 group = *data++;
        result = mc::u32(group & 0x7f) | result << 7;
        ++size;

        if (group < 0x80)
            break;
    }

    *output = result;

    return size;
}

// so tell me why nintendo decided to randomly use the original version of this in one singular place
inline mc::u32 decodeVByteReversed(const mc::u8*& data) {
    mc::u8 lead = *data++;

    if (lead < 0x80)
        return lead;

    mc::u32 result = lead & 0x7f;
    mc::u32 shift = 7;

    for (;;) {
        mc::u8 group = *data++;
        result += (mc::u32(group & 0x7f) << shift);
        shift += 7;

        if (group < 0x80)
            break;
    }

    return result;
}

} // namespace meshopt

namespace mc {

u32 DecodeIndexBuffer0_WithTable(void* dst, s32 indexCount, u32 baseIndex, u64* tbl, u32 copied, u32 remaining, const u8*& src0, const u8*& src1, IndexFormat format);
u32 DecodeIndexBuffer0_WithoutTable(void* dst, s32 indexCount, u32 baseIndex, const u8*& src0, const u8*& src1, IndexFormat format);
u32 DecodeIndexBuffer2(void* dst, IndexFormat indexFormat, s32 indexCount, u32 baseIndex, u64* decodeBuf, u32 a6, u32 numCopied, u32 start, const u8*& src0, const u8*& src1);
u32 DecodeIndexBuffer3(void* dst, IndexFormat indexFormat, s32 indexCount, u32 baseIndex, u32 a5, u64* decodeBuf, u32 numCopied, u32 start, const u8*& src0, const u8*& src1);

} // namespace mc