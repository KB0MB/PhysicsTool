#include "mc_AttributeCodec.h"

#include "mc_IndexCodec.h" // meshopt::decodeVByte

namespace mc {

namespace detail {

static inline s32 AdjustToByte(s32 size) {
    return ((size + 7 > -1) ? (size + 7) : (size + 0xe)) >> 3;
}

static inline ElementType ToElementType(s32 size) {
    return static_cast<ElementType>(AdjustToByte(size) - 1);
}

// there's probably some way to write this all using variadic templates but idk how
static s32 FUN_7100086b40(AttrStreamInfo* info, u32 maxStreams [[maybe_unused]], const u8*& pos [[maybe_unused]], s32 componentCount, s32 componentBitSize, s32 vertexCount) {
    // 1 decoding table per byte
    info[0].elementType = ElementType::U8;
    info[0].tableCount = AdjustToByte(componentBitSize) * componentCount;
    info[0].elementCount = vertexCount;

    return 1;
}

static s32 FUN_710008b5b0(AttrStreamInfo* info, u32 maxStreams [[maybe_unused]], const u8*& pos [[maybe_unused]], s32 componentCount, s32 componentBitSize, s32 vertexCount) {
    // 1 decoding table per component
    info[0].elementType = ToElementType(componentBitSize);
    info[0].tableCount = componentCount;
    info[0].elementCount = vertexCount;

    return 1;
}

static s32 FUN_7100086b70(AttrStreamInfo* info, u32 maxStreams [[maybe_unused]], const u8*& pos [[maybe_unused]], s32 componentCount, s32 componentBitSize, s32 vertexCount) {
    u32 elementCount = meshopt::decodeVByte(pos);
    s32 tableCount = AdjustToByte(componentBitSize) * componentCount;

    // 1 decoding table per byte
    info[0].elementType = ElementType::U8;
    info[0].tableCount = tableCount;
    info[0].elementCount = elementCount;
    // 1 decoding table per byte
    info[1].elementType = ElementType::U8;
    info[1].tableCount = tableCount;
    info[1].elementCount = vertexCount - elementCount;

    return 2;
}

static s32 FUN_7100085e00(AttrStreamInfo* info, u32 maxStreams [[maybe_unused]], const u8*& pos [[maybe_unused]], s32 componentCount, s32 componentBitSize, s32 vertexCount) {
    ElementType elementType = ToElementType(componentBitSize);

    // 1 decoding table for the first component
    info[0].elementType = elementType;
    info[0].tableCount = 1;
    info[0].elementCount= vertexCount;
    // 1 decoding table per remaining component
    info[1].elementType = elementType;
    info[1].tableCount = componentCount - 1;
    info[1].elementCount = vertexCount;

    return 2;
}

static s32 FUN_7100091590(AttrStreamInfo* info, u32 maxStreams [[maybe_unused]], const u8*& pos [[maybe_unused]], s32 componentCount [[maybe_unused]], s32 componentBitSize, s32 vertexCount) {
    // this could be for something like R10G10B10A2 (no idea if it actually is or not)

    // 3 per component decoding table
    info[0].elementType = ToElementType(componentBitSize);
    info[0].tableCount = 3;
    info[0].elementCount = vertexCount;
    // 1 per byte decoding table
    info[1].elementType = ElementType::U8;
    info[1].tableCount = 1;
    info[1].elementCount = vertexCount;

    return 2;
}

static s32 FUN_710008b5e0(AttrStreamInfo* info, u32 maxStreams [[maybe_unused]], const u8*& pos [[maybe_unused]], s32 componentCount, s32 componentBitSize, s32 vertexCount) {
    u32 elementCount = meshopt::decodeVByte(pos);
    ElementType elementType = ToElementType(componentBitSize);

    info[0].elementType = elementType;
    info[0].tableCount = componentCount;
    info[0].elementCount = elementCount;
    info[1].elementType = elementType;
    info[1].tableCount = componentCount;
    info[1].elementCount = vertexCount - elementCount;

    return 2;
}

static s32 FUN_710008fcb0(AttrStreamInfo* info, u32 maxStreams [[maybe_unused]], const u8*& pos [[maybe_unused]], s32 componentCount [[maybe_unused]], s32 componentBitSize, s32 vertexCount) {
    u32 elementCount = meshopt::decodeVByte(pos);
    ElementType elementType = ToElementType(componentBitSize);

    info[0].elementType = elementType;
    info[0].tableCount = 3;
    info[0].elementCount = elementCount;
    info[1].elementType = elementType;
    info[1].tableCount = 3;
    info[1].elementCount = vertexCount - elementCount;

    return 2;
}

static s32 FUN_7100092470(AttrStreamInfo* info, u32 maxStreams [[maybe_unused]], const u8*& pos [[maybe_unused]], s32 componentCount [[maybe_unused]], s32 componentBitSize [[maybe_unused]], s32 vertexCount) {
    u32 elementCount = meshopt::decodeVByte(pos);

    info[0].elementType = ElementType::U16;
    info[0].tableCount = 2;
    info[0].elementCount = elementCount;
    info[1].elementType = ElementType::U16;
    info[1].tableCount = 2;
    info[1].elementCount = vertexCount - elementCount;
    info[2].elementType = ElementType::U8;
    info[2].tableCount = 1;
    info[2].elementCount = vertexCount - elementCount;

    return 3;
}

static s32 FUN_710008fd10(AttrStreamInfo* info, u32 maxStreams [[maybe_unused]], const u8*& pos [[maybe_unused]], s32 componentCount [[maybe_unused]], s32 componentBitSize, s32 vertexCount) {
    u32 elementCount0 = meshopt::decodeVByte(pos);
    u32 elementCount1 = meshopt::decodeVByte(pos);
    ElementType elementType = ToElementType(componentBitSize);

    info[0].elementType = elementType;
    info[0].tableCount = 3;
    info[0].elementCount = elementCount0;
    info[1].elementType = elementType;
    info[1].tableCount = 3;
    info[1].elementCount = elementCount1;
    info[2].elementType = elementType;
    info[2].tableCount = 3;
    info[2].elementCount = vertexCount - elementCount0 - elementCount1;

    return 3;
}

static s32 FUN_7100094c10(AttrStreamInfo* info, u32 maxStreams [[maybe_unused]], const u8*& pos [[maybe_unused]], s32 componentCount [[maybe_unused]], s32 componentBitSize, s32 vertexCount) {
    ElementType elementType = ToElementType(componentBitSize);

    info[0].elementType = elementType;
    info[0].tableCount = 2;
    info[0].elementCount = vertexCount;
    info[1].elementType = elementType;
    info[1].tableCount = 1;
    info[1].elementCount = vertexCount;
    info[2].elementType = ElementType::U8;
    info[2].tableCount = 1;
    info[2].elementCount = vertexCount;

    return 3;
}

static s32 FUN_7100085e40(AttrStreamInfo* info, u32 maxStreams [[maybe_unused]], const u8*& pos [[maybe_unused]], s32 componentCount, s32 componentBitSize, s32 vertexCount) {
    u32 elementCount = meshopt::decodeVByte(pos);
    ElementType elementType = ToElementType(componentBitSize);

    info[0].elementType = elementType;
    info[0].tableCount = 1;
    info[0].elementCount = elementCount;
    info[1].elementType = elementType;
    info[1].tableCount = componentCount - 1;
    info[1].elementCount = elementCount;
    info[2].elementType = elementType;
    info[2].tableCount = componentCount;
    info[2].elementCount = vertexCount - elementCount;

    return 3;
}

static s32 FUN_7100094c50(AttrStreamInfo* info, u32 maxStreams [[maybe_unused]], const u8*& pos [[maybe_unused]], s32 componentCount [[maybe_unused]], s32 componentBitSize, s32 vertexCount) {
    u32 elementCount = meshopt::decodeVByte(pos);
    ElementType elementType = ToElementType(componentBitSize);

    info[0].elementType = elementType;
    info[0].tableCount = 2;
    info[0].elementCount = elementCount;
    info[1].elementType = elementType;
    info[1].tableCount = 1;
    info[1].elementCount = elementCount;
    info[2].elementType = ElementType::U8;
    info[2].tableCount = 1;
    info[2].elementCount = elementCount;
    info[3].elementType = elementType;
    info[3].tableCount = 3;
    info[3].elementCount = vertexCount - elementCount;

    return 4;
}

static s32 FUN_7100091450(AttrStreamInfo* info, u32 maxStreams [[maybe_unused]], const u8*& pos [[maybe_unused]], s32 componentCount, s32 componentBitSize [[maybe_unused]], s32 vertexCount) {
    u32 elementCount0 = meshopt::decodeVByte(pos);
    u32 elementCount1 = meshopt::decodeVByte(pos);
    s32 totalComponentCount = componentCount * vertexCount;

    info[0].elementType = ElementType::U8;
    info[0].tableCount = 1;
    info[0].elementCount = totalComponentCount;
    info[1].elementType = ElementType::U8;
    info[1].tableCount = 1;
    info[1].elementCount = elementCount0;
    info[2].elementType = ElementType::U8;
    info[2].tableCount = 1;
    info[2].elementCount = totalComponentCount - elementCount0;
    info[3].elementType = ElementType::U16;
    info[3].tableCount = 1;
    info[3].elementCount = elementCount1;
    info[4].elementType = ElementType::U16;
    info[4].tableCount = 1;
    info[4].elementCount = totalComponentCount - elementCount1;

    return 5;
}

static s32 FUN_71000914f0(AttrStreamInfo* info, u32 maxStreams [[maybe_unused]], const u8*& pos [[maybe_unused]], s32 componentCount, s32 componentBitSize [[maybe_unused]], s32 vertexCount) {
    u32 elementCount0 = meshopt::decodeVByte(pos);
    u32 elementCount1 = meshopt::decodeVByte(pos);
    s32 totalComponentCount = componentCount * vertexCount;

    info[0].elementType = ElementType::U8;
    info[0].tableCount = 1;
    info[0].elementCount = totalComponentCount;
    info[1].elementType = ElementType::U8;
    info[1].tableCount = 1;
    info[1].elementCount = elementCount0;
    info[2].elementType = ElementType::U8;
    info[2].tableCount = 1;
    info[2].elementCount = totalComponentCount - elementCount0;
    info[3].elementType = ElementType::U8;
    info[3].tableCount = 3;
    info[3].elementCount = elementCount1;
    info[4].elementType = ElementType::U8;
    info[4].tableCount = 3;
    info[4].elementCount = totalComponentCount - elementCount1;

    return 5;
}

static s32 FUN_71000924d0(AttrStreamInfo* info, u32 maxStreams, const u8*& pos, s32 componentCount, s32 componentBitSize, s32 vertexCount) {
    info[0].elementType = ElementType::U8;
    info[0].tableCount = 1;
    info[0].elementCount = meshopt::decodeVByte(pos);

    return FUN_7100091450(info + 1, maxStreams - 1, pos, componentCount, componentBitSize, vertexCount) + 1;
}

static s32 FUN_7100092520(AttrStreamInfo* info, u32 maxStreams, const u8*& pos, s32 componentCount, s32 componentBitSize, s32 vertexCount) {
    info[0].elementType = ElementType::U8;
    info[0].tableCount = 1;
    info[0].elementCount = meshopt::decodeVByte(pos);

    return FUN_71000914f0(info + 1, maxStreams - 1, pos, componentCount, componentBitSize, vertexCount) + 1;
}

} // namespace detail

GetStreamInfoFunc sAttributeGetStreamInfoFunctions[0x71] = {
    // 2 streams
    detail::FUN_7100085e00, detail::FUN_7100085e00, detail::FUN_7100085e00, detail::FUN_7100085e00, detail::FUN_7100085e00, detail::FUN_7100085e00, detail::FUN_7100085e00,
    // 3 streams
    detail::FUN_7100085e40, detail::FUN_7100085e40, detail::FUN_7100085e40, detail::FUN_7100085e40, detail::FUN_7100085e40, detail::FUN_7100085e40, detail::FUN_7100085e40,
    // 1 stream
    detail::FUN_7100086b40, detail::FUN_7100086b40, detail::FUN_7100086b40, detail::FUN_7100086b40, detail::FUN_7100086b40, detail::FUN_7100086b40, detail::FUN_7100086b40,
    detail::FUN_7100086b40, detail::FUN_7100086b40, detail::FUN_7100086b40,
    // 1 stream
    detail::FUN_7100086b40, detail::FUN_7100086b40, detail::FUN_7100086b40, detail::FUN_7100086b40,
    // 2 streams
    detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70,
    detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70,
    // 2 streams
    detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70,
    // 2 streams
    detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70,
    detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70, detail::FUN_7100086b70,
    // 1 stream
    detail::FUN_710008b5b0, detail::FUN_710008b5b0, detail::FUN_710008b5b0, detail::FUN_710008b5b0, detail::FUN_710008b5b0, detail::FUN_710008b5b0, detail::FUN_710008b5b0,
    detail::FUN_710008b5b0, detail::FUN_710008b5b0, detail::FUN_710008b5b0, detail::FUN_710008b5b0,
    // 1 stream
    detail::FUN_710008b5b0, detail::FUN_710008b5b0, detail::FUN_710008b5b0,
    // 2 streams
    detail::FUN_710008b5e0, detail::FUN_710008b5e0, detail::FUN_710008b5e0, detail::FUN_710008b5e0, detail::FUN_710008b5e0, detail::FUN_710008b5e0, detail::FUN_710008b5e0,
    detail::FUN_710008b5e0, detail::FUN_710008b5e0, detail::FUN_710008b5e0,
    // 2 streams
    detail::FUN_710008b5e0, detail::FUN_710008b5e0, detail::FUN_710008b5e0, detail::FUN_710008b5e0,
    // 2 streams
    detail::FUN_710008fcb0, detail::FUN_710008fcb0, detail::FUN_710008fcb0, detail::FUN_710008fcb0,
    // 5 streams
    detail::FUN_7100091450, detail::FUN_71000914f0,
    // 3 streams
    detail::FUN_710008fd10, detail::FUN_710008fd10, detail::FUN_710008fd10, detail::FUN_710008fd10,
    // 5 streams
    detail::FUN_7100091450, detail::FUN_71000914f0,
    // 2 streams
    detail::FUN_7100091590, detail::FUN_7100091590, detail::FUN_7100091590,
    // 3 streams
    detail::FUN_7100092470, detail::FUN_7100092470,
    // 6 streams
    detail::FUN_71000924d0, detail::FUN_7100092520,
    // 3 streams
    detail::FUN_7100094c10, detail::FUN_7100094c10, detail::FUN_7100094c10,
    // 4 streams
    detail::FUN_7100094c50, detail::FUN_7100094c50, detail::FUN_7100094c50,
};

} // namespace mc