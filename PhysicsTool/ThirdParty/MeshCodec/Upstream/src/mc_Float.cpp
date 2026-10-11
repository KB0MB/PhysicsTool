#include "mc_Float.h"

#ifdef _MSC_VER
#include <intrin.h>
#elif defined(__GNUC__) && defined(__x86_64__)
#include <xmmintrin.h>
#endif

namespace mc::detail {

u32 InitFPUState() {
#if defined(__x86_64__) || defined(_M_X64)
    u32 csr = _mm_getcsr();
#elif defined(_MSC_VER) && defined(_M_ARM64) && !defined(__clang__)
    u32 csr = static_cast<u32>(_ReadStatusReg(0x5a20)); // ARM64_FPCR (Windows SDK ksarm64.h).
#else
#if defined(__clang__) || !defined(__GNUC__)  
    u32 csr;
    __asm__("mrs %x0, fpcr" : "=r"(csr));
#else
    // only gcc appears to have an intrinsic for this
    u32 csr = __builtin_aarch64_get_fpcr();
#endif
#endif
#if defined(__x86_64__) || defined(_M_X64)
    u32 newCsr = csr & 0xffff9fff; // rounding mode nearest
    newCsr |= 0x8040; // flush denormals (I think nan propagation is always set on x86?)
#elif defined(__aarch64__) || defined(_M_ARM64)
    u32 newCsr = csr & 0xfc3fffff; // rounding mode nearest
    newCsr |= 0x3000000; // nan propagation and flush denormals
#else
    // idk I'm lazy
#endif
#if defined(__x86_64__) || defined(_M_X64)
    _mm_setcsr(newCsr);
#elif defined(_MSC_VER) && defined(_M_ARM64) && !defined(__clang__)
    _WriteStatusReg(0x5a20, newCsr);
#else
#if defined(__clang__) || !defined(__GNUC__)
    __asm__("msr fpcr, %x0" :: "r"(newCsr));
#else
    __builtin_aarch64_set_fpcr(newCsr);
#endif
#endif
    return csr;
}

void SetFPUState(u32 state) {
#if defined(__x86_64__) || defined(_M_X64)
    _mm_setcsr(state);
#elif defined(_MSC_VER) && defined(_M_ARM64) && !defined(__clang__)
    _WriteStatusReg(0x5a20, state);
#else
#if defined(__clang__) || !defined(__GNUC__)
    __asm__("msr fpcr, %x0" :: "r"(state));
#else
    __builtin_aarch64_set_fpcr(state);
#endif
#endif
}

} // namespace mc
