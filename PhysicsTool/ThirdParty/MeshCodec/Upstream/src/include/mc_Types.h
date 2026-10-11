#pragma once

#include <cstdint>
#include <cstddef>

#ifdef _MSC_VER
#include "float16.h"
#endif

// it's kinda clunky to have them in the namespace here but I'd rather they don't cause potential redefinitions when using other libraries
namespace mc {

using u8 = std::uint8_t;
using u16 = std::uint16_t;
using u32 = std::uint32_t;
using u64 = std::uint64_t;

using s8 = std::int8_t;
using s16 = std::int16_t;
using s32 = std::int32_t;
using s64 = std::int64_t;

using size_t = std::size_t;

#ifdef _MSC_VER
struct f16 {
    u16 _value;

    f16() = default;

    f16(float v) {
        _value = encode_flt16(v);
    }
    
    operator float() const {
        return decode_flt16(_value);
    }
};
#else
using f16 = _Float16;
#endif

using f32 = float;
using f64 = double;

#pragma pack(push, 1)
struct u24 {
    u16 _00;
    u8 _02;

    u32 get() const {
        return _00 | _02 << 0x10;
    }
};
#pragma pack(pop)
static_assert(sizeof(u24) == 3);

template <typename T>
T Align(T ptr, u32 align) {
    return reinterpret_cast<T>((reinterpret_cast<uintptr_t>(ptr) + align - 1) & ~(static_cast<u64>(align) - 1));
}

#ifdef _MSC_VER
    #define UNREACHABLE_DEFAULT_CASE __assume(0);
#elif defined(__GNUC__)
    #define UNREACHABLE_DEFAULT_CASE __builtin_unreachable();
#else
    static_assert(false, "Unsupported compiler");
#endif

} // namespace mc
