#pragma once

#include "mc_Types.h"

namespace mc {

struct StreamContext {
    u8* stream;
    size_t size;
    u64 alignment;
};

} // namespace mc