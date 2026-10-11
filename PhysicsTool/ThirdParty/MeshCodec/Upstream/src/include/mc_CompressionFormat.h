#pragma once

#include "mc_Types.h"

namespace mc {

/*
MeshCodec Stream
- ResCompressionHeader
- frame 1
  - ResFrameSize -> for next frame
  - standard stream -> not a bit stream
  - bit stream 1 (backwards) -> starts at streamOffset and reads backwards
  - bit stream 2 (forwards) -> starts at streamOffset and reads forwards
  - bit stream 3 (backwards) -> starts at streamOffset + endOffset and reads backwards
- frame 2
...
*/

enum class CodecType {
    Null, ZStandard, MeshCodec, Invalid
};

#pragma pack(push, 1) // I hope this isn't a windows only thing
struct ResFrameSize {
    u24 streamOffset;
    u24 endOffset;
};

struct ResCompressionHeader {
    u16 flags;
    ResFrameSize sizeInfo;  

    CodecType GetCodecType() const {
        return static_cast<CodecType>(flags & 3);
    }
};
#pragma pack(pop)
static_assert(sizeof(ResCompressionHeader) == 0x8);

} // namespace mc