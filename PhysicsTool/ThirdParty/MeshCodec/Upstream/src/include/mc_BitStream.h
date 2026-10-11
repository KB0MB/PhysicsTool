#pragma once

#include "mc_Types.h"

#ifdef _MSC_VER
#include <stdlib.h>
#include <immintrin.h>
#include <intrin.h>
#elif defined(__APPLE__)
#include <libkern/OSByteOrder.h>
#elif defined(__linux__)
#include <byteswap.h>
#endif

namespace mc {

inline u64 Swap(u64 value) {
#ifdef _MSC_VER
    return _byteswap_uint64(value);
#elif defined(__APPLE__)
    return OSSwapInt64(value);
#elif defined(__linux__)
    return bswap_64(value);
#elif defined(__GNUC__) // covers both gcc and clang
    return __builtin_bswap64(value);
#else
    return ((value & 0xff00000000000000u) >> 56u) |
            ((value & 0x00ff000000000000u) >> 40u) |
            ((value & 0x0000ff0000000000u) >> 24u) |
            ((value & 0x000000ff00000000u) >>  8u) |
            ((value & 0x00000000ff000000u) <<  8u) |      
            ((value & 0x0000000000ff0000u) << 24u) |
            ((value & 0x000000000000ff00u) << 40u) |
            ((value & 0x00000000000000ffu) << 56u);
#endif
}

inline u32 Swap(u32 value) {
    #ifdef _MSC_VER
        return _byteswap_ulong(value);
    #elif defined(__APPLE__)
        return OSSwapInt32(value);
    #elif defined(__linux__)
        return bswap_32(value);
    #elif defined(__GNUC__) // covers both gcc and clang
        return __builtin_bswap32(value);
    #else
        value = (value & 0x0000ffffu) << 16 | (value & 0xffff0000) >> 16;
        return (value & 0x00ff00ff) << 8 | (value & 0xff00ff00) >> 8;
    #endif
    }

inline u32 Clz(u64 value) {
#if defined(__x86_64__) || defined(_M_X64)
    // bsr on x86 has undefined behavior when the operand is 0, clz on aarch64 returns 0x20
    if (value == 0) return 0x20;
#endif
#ifdef _MSC_VER
    return _lzcnt_u64(value);
#elif defined(__GNUC__)
    return __builtin_clzll(value);
#endif
}

inline u32 Clz(u32 value) {
#if defined(__x86_64__) || defined(_M_X64)
    if (value == 0) return 0x20;
#endif
#ifdef _MSC_VER
    return _lzcnt_u32(value);
#elif defined(__GNUC__)
    return __builtin_clz(value);
#endif
}

inline u32 Popcount(u32 value) {
#ifdef _MSC_VER
    return __popcnt(value);
#elif defined(__GNUC__)
    return  __builtin_popcount(value);
#else
    value -= (value >> 1 & 0x55555555);
    value = (value & 0x33333333) + (value >> 2 & 0x33333333);
    return (value + (value >> 4) & 0x0F0F0F0F) * 0x01010101 >> 0x18;
#endif
}

struct CompositeBitOffsetContext {
    u32 offset0;
    u32 offset1;
    u32 offset2;
};

struct CompositeBitRemainderContext {
    u64 value0;
    u64 value1;
    u64 value2;
};

// no bounds checking so be careful
// stream should be at least 64 bits long
class BitStreamReader {
public:
enum class Direction : s32 {
    Backwards = -1,
    Forwards = 1,
};

    BitStreamReader() = default;
    BitStreamReader(const u64* stream) : mStream(stream), mRemainder(0), mOffset(0) {}
    BitStreamReader(const u64* stream, Direction dir) : mStream(stream), mRemainder(0), mOffset(0), mDirection(dir) {}

    void SetDirection(Direction dir) {
        mDirection = dir;
    }

    void SetStream(u64* stream) {
        mStream = stream;
    }

    // always backwards
    u64 Read(u32 nbits) {
        u64 value = *mStream >> (mOffset & 0x3fu) | mRemainder;
        
        mStream = reinterpret_cast<u64*>(reinterpret_cast<uintptr_t>(mStream) - (mOffset >> 3 ^ 7));
        mOffset = (mOffset | 0x38u) - nbits;
        mRemainder = value << nbits;

        return value >> (0x40u - nbits);
    }

    // always forwards
    u64 ReadForwards(u32 nbits) {
        u64 value = Swap(*mStream) >> (mOffset & 0x3fu) | mRemainder;

        mStream = reinterpret_cast<u64*>(reinterpret_cast<uintptr_t>(mStream) + (mOffset >> 3 ^ 7));
        mOffset = (mOffset | 0x38u) - nbits;
        mRemainder = value << nbits;

        return value >> (0x40u - nbits);
    }

    u64 ReadRaw() {
        u64 value = *mStream >> (mOffset & 0x3fu);

        mStream = reinterpret_cast<u64*>(reinterpret_cast<uintptr_t>(mStream) - (mOffset >> 3 ^ 7));

        return value;
    }

    u64 ReadRawForwards() {
        u64 value = Swap(*mStream) >> (mOffset & 0x3fu);

        mStream = reinterpret_cast<u64*>(reinterpret_cast<uintptr_t>(mStream) + (mOffset >> 3 ^ 7));

        return value;
    }

    u64 ReadZeroes() {
        u64 value = *mStream >> (mOffset & 0x3fu) | mRemainder;
        u32 numZeroes = Clz(value);

        mStream = reinterpret_cast<u64*>(reinterpret_cast<uintptr_t>(mStream) - (mOffset >> 3 ^ 7));
        mOffset = (mOffset | 0x38u) - (numZeroes + 1);
        mRemainder = value << (numZeroes + 1);

        return numZeroes;
    }

    u64 DirectionalRead(u32 nbits) {
        u64 value = (IsReverse() ? (*mStream) : Swap(*mStream)) >> (mOffset & 0x3fu) | mRemainder;

        mStream = reinterpret_cast<u64*>(reinterpret_cast<uintptr_t>(mStream) + s32(IsReverse() ? -(mOffset >> 3 ^ 7) : (mOffset >> 3 ^ 7)));
        mOffset = (mOffset | 0x38u) - nbits;
        mRemainder = value << nbits;

        return value >> (0x40u - nbits);
    }

    void SetBitOffset(u32 offset) {
        mOffset = offset;
    }

    void SetRemainder(u64 remainder) {
        mRemainder = remainder;
    }

    u32 GetBitOffset() const {
        return mOffset;
    }

    u64 GetRemainder() const {
        return mRemainder;
    }

    const u64* GetStream() const {
        return mStream;
    }

    bool IsReverse() const {
        return mDirection != Direction::Forwards;
    }

protected:
    const u64* mStream;
    u64 mRemainder;
    u32 mOffset;
    Direction mDirection;
};

} // namespace mc