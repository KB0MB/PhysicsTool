#pragma once

#include "mc_Types.h"
#include "mc_BitStream.h"

namespace mc {

struct DecompContext {
    BitStreamReader bitStream0;
    BitStreamReader bitStream1;
    BitStreamReader bitStream2;
    const u8* currentPos;
    u64 _50;
};

} // namespace mc