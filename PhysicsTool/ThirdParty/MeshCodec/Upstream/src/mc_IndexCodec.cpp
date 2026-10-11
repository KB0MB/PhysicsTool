#include "mc_IndexCodec.h"

#include <algorithm>

/**
 * meshoptimizer - version 0.22
 *
 * Copyright (C) 2016-2025, by Arseny Kapoulkine (arseny.kapoulkine@gmail.com)
 * Report bugs and download new versions at https://github.com/zeux/meshoptimizer
 *
 * This library is distributed under the MIT License. See notice at the end of this file.
 */

namespace meshopt {

// Nintendo uses a different fifo queue
// TriangleFifo is an array of groups of 4 u32s which are essentially two interlocking arrays
using TriangleFifo = mc::u32[16][4];
using VertexFifo = mc::u32[64];

static const mc::u8 kCodeAuxEncodingTable[16] = {
    0x00, 0x76, 0x87, 0x56, 0x67, 0x78, 0xa9, 0x86, 0x65, 0x89, 0x68, 0x98, 0x01, 0x69,
    0, 0, // last two entries aren't used for encoding
};

static void pushVertexFifo(TriangleFifo fifo, mc::u32 v, size_t& offset, mc::s32 cond = 1) {
	fifo[offset][3] = v;
	offset = (offset + cond) & 0xf;
}

static void pushVertexFifo(VertexFifo fifo, mc::u32 v, size_t& offset) {
	fifo[offset] = v;
	offset = (offset + 1) & 0x3f;
}

static void pushTriangleFifo(TriangleFifo fifo, mc::u32 a, mc::u32 b, mc::u32 c, size_t& offset) {
    fifo[offset][0] = a;
    fifo[offset][1] = b;
    fifo[offset][2] = c;
    offset = (offset + 1) & 0xf;
}

template <typename T>
static void writeTriangle(T* dst, mc::u32 a, mc::u32 b, mc::u32 c) {
    dst[0] = static_cast<T>(a);
    dst[1] = static_cast<T>(b);
    dst[2] = static_cast<T>(c);
}

static mc::u32 decodeVByteFixed(const mc::u8*& data) {
    mc::u32 raw = *reinterpret_cast<const mc::u32*>(data);
    mc::u32 result = raw & 0x7f;

    ++data;
    if (raw >> 7 & 1) {
        ++data;
        result |= raw >> 1 & 0x3f80;
        if (raw >> 0xf & 1) {
            ++data;
            result |= raw >> 2 & 0x3fc000;
        }
    }

    return result;
}

static mc::u32 decodeIndexFixed(const mc::u8*& data, mc::u32 last) {
	mc::u32 v = decodeVByteFixed(data);
	mc::u32 d = (v >> 1) ^ -mc::s32(v & 1);

	return last + d;
}

static mc::u32 decodeIndex(const mc::u8*& data, mc::u32 last) {
    mc::u32 v = decodeVByte(data);
    mc::u32 d = (v >> 1) ^ -mc::s32(v & 1);

    return last + d;
}

} // namespace meshopt

namespace mc {

// note to self: remember to update the table-less version if ever updating this one
u32 DecodeIndexBuffer0_WithTable(void* dst, s32 indexCount, u32 baseIndex, u64* tbl, u32 copied, u32 remaining [[maybe_unused]], const u8*& src0, const u8*& src1, IndexFormat format) {
    constexpr u32 fecmax = 0xf;

    meshopt::TriangleFifo trigfifo;
    size_t trigfifooffset = 0;
    size_t vertexfifooffset = 0;

    u32 next = baseIndex;
    u32 last = baseIndex;

    // this is effectively meshopt_decodeIndexBuffer but with some modifications
    if (format == IndexFormat::U16) {
        u16* outBuf = reinterpret_cast<u16*>(dst);

        if (indexCount > 2) {
            for (u32 i = indexCount / 3; i != 0; --i) {
                u8 codetri = *src0++;

                if (codetri < 0xf0) {
                    s32 fe = codetri >> 4;
                    s32 fec = codetri & 0xf;

                    u32 a = trigfifo[(trigfifooffset - 1 - fe) & 0xf][0];
                    u32 b = trigfifo[(trigfifooffset - 1 - fe) & 0xf][1];
                    u32 unk = trigfifo[(trigfifooffset - 1 - fe) & 0xf][2];

                    if (fec == fecmax) {
                        u32 c = meshopt::decodeIndexFixed(src1, last);
                        last = c;

                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset);

                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);

                        u32 maxAB = std::max(a, b);
                        u32 max = std::max(maxAB, c);
                        if (max >= copied && (tbl[max] & 0x3fffff) == 0) {
                            if (maxAB > c) {
                                tbl[max] = u64(max - (max != a ? a : c)) << 0x2b | u64(max - (max != a ? c : b)) << 0x16;
                            } else {
                                tbl[max] = u64(max >= unk ? max - unk : 0) | u64(max - a) << 0x16 | u64(max - b) << 0x2b;
                            }
                        }
                    } else {
                        u32 cf = trigfifo[(vertexfifooffset - 1 - fec) & 0xf][3];
                        u32 c = (fec == 0) ? next : cf;
                        s32 fec0 = static_cast<s32>(fec == 0);
                        next += fec0;

                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset, fec0);

                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);

                        u32 maxAB = std::max(a, b);
                        u32 max = std::max(maxAB, c);
                        if (max >= copied && (tbl[max] & 0x3fffff) == 0) {
                            if (maxAB > c) {
                                tbl[max] = u64(max - (max != a ? a : c)) << 0x2b | u64(max - (max != a ? c : b)) << 0x16;
                            } else {
                                tbl[max] = u64(max >= unk ? max - unk : 0) | u64(max - a) << 0x16 | u64(max - b) << 0x2b;
                            }
                        }
                    }
                } else {
                    if (codetri < 0xfe) {
                        u8 codeaux = meshopt::kCodeAuxEncodingTable[codetri & 0xf];

                        u32 a = next++;

                        u32 feb = codeaux >> 4;
                        s32 fec = codeaux & 0xf;
                        
                        u32 feb0 = 0xd001u >> (codetri & 0xf) & 1; // idk how this works
                        u32 bf = trigfifo[(vertexfifooffset - feb) & 0xf][3];
                        u32 b = (feb0 != 0) ? next : bf;
                        next += feb0;
                        
                        u32 cf = trigfifo[(vertexfifooffset - fec) & 0xf][3];
                        u32 c = (fec == 0) ? next : cf;

                        s32 fec0 = static_cast<s32>(fec == 0);
                        next += fec0;

                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, a, vertexfifooffset);
                        meshopt::pushVertexFifo(trigfifo, b, vertexfifooffset, feb0);
                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset, fec0);

                        meshopt::pushTriangleFifo(trigfifo, b, a, c, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);

                        u32 maxBC = std::max(b, c);
                        u32 max = std::max(maxBC, a);
                        if (max >= copied && (tbl[max] & 0x3fffff) == 0) {
                            if (maxBC >= a) {
                                tbl[max] = u64(max - (c >= b ? b : a)) << 0x2b | u64(max - (c >= b ? a : c)) << 0x16;
                            } else {
                                tbl[max] = u64(max - c) << 0x2b | u64(max - b) << 0x16;
                            }
                        }
                    } else {
                        u8 codeaux = *src1++;

                        if (codeaux == 0) {
                            next = 0;
                            last = 0;
                        }

                        s32 fea = static_cast<s32>(codetri == 0xfe);
                        s32 feb = codeaux >> 4;
                        s32 fec = codeaux & 0xf;

                        u32 a = codeaux ? next : 0;
                        next += fea;

                        u32 b = (feb == 0) ? next++ : trigfifo[(vertexfifooffset - feb) & 0xf][3];
                        u32 c = (fec == 0) ? next++ : trigfifo[(vertexfifooffset - fec) & 0xf][3];

                        if (!fea)
                            last = a = meshopt::decodeIndexFixed(src1, last);
                        
                        if (feb == 0xf)
                            last = b = meshopt::decodeIndexFixed(src1, last);
                        
                        if (fec == 0xf)
                            last = c = meshopt::decodeIndexFixed(src1, last);
                        
                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, a, vertexfifooffset);
                        meshopt::pushVertexFifo(trigfifo, b, vertexfifooffset, (feb == 0) | (feb == 0xf));
                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset, (fec == 0) | (fec == 0xf));

                        meshopt::pushTriangleFifo(trigfifo, b, a, c, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);

                        u32 maxBC = std::max(b, c);
                        u32 max = std::max(maxBC, a);
                        if (max >= copied && (tbl[max] & 0x3fffff) == 0) {
                            if (maxBC >= a) {
                                tbl[max] = u64(max - (c >= b ? b : a)) << 0x2b | u64(max - (c >= b ? a : c)) << 0x16;
                            } else {
                                tbl[max] = u64(max - c) << 0x2b | u64(max - b) << 0x16;
                            }
                        }
                    }
                }
                outBuf += 3;
            }
        }
    } else {
        u32* outBuf = reinterpret_cast<u32*>(dst);

        if (indexCount > 2) {
            for (u32 i = indexCount / 3; i != 0; --i) {
                u8 codetri = *src0++;

                if (codetri < 0xf0) {
                    s32 fe = codetri >> 4;
                    s32 fec = codetri & 0xf;

                    u32 a = trigfifo[(trigfifooffset - 1 - fe) & 0xf][0];
                    u32 b = trigfifo[(trigfifooffset - 1 - fe) & 0xf][1];
                    u32 unk = trigfifo[(trigfifooffset - 1 - fe) & 0xf][2];

                    if (fec == fecmax) {
                        u32 c = meshopt::decodeIndexFixed(src1, last);
                        last = c;

                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset);

                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);

                        u32 maxAB = std::max(a, b);
                        u32 max = std::max(maxAB, c);
                        if (max >= copied && (tbl[max] & 0x3fffff) == 0) {
                            if (maxAB > c) {
                                tbl[max] = u64(max - (max != a ? a : c)) << 0x2b | u64(max - (max != a ? c : b)) << 0x16;
                            } else {
                                tbl[max] = u64(max >= unk ? max - unk : 0) | u64(max - a) << 0x16 | u64(max - b) << 0x2b;
                            }
                        }
                    } else {
                        u32 cf = trigfifo[(vertexfifooffset - 1 - fec) & 0xf][3];
                        u32 c = (fec == 0) ? next : cf;
                        s32 fec0 = static_cast<s32>(fec == 0);
                        next += fec0;

                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset, fec0);

                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);

                        u32 maxAB = std::max(a, b);
                        u32 max = std::max(maxAB, c);
                        if (max >= copied && (tbl[max] & 0x3fffff) == 0) {
                            if (maxAB > c) {
                                tbl[max] = u64(max - (max != a ? a : c)) << 0x2b | u64(max - (max != a ? c : b)) << 0x16;
                            } else {
                                tbl[max] = u64(max >= unk ? max - unk : 0) | u64(max - a) << 0x16 | u64(max - b) << 0x2b;
                            }
                        }
                    }
                } else {
                    if (codetri < 0xfe) {
                        u8 codeaux = meshopt::kCodeAuxEncodingTable[codetri & 0xf];

                        u32 a = next++;

                        u32 feb = codeaux >> 4;
                        s32 fec = codeaux & 0xf;
                        
                        u32 feb0 = 0xd001u >> (codetri & 0xf) & 1; // idk how this works
                        u32 bf = trigfifo[(vertexfifooffset - feb) & 0xf][3];
                        u32 b = (feb0 != 0) ? next : bf;
                        next += feb0;
                        
                        u32 cf = trigfifo[(vertexfifooffset - fec) & 0xf][3];
                        u32 c = (fec == 0) ? next : cf;

                        s32 fec0 = static_cast<s32>(fec == 0);
                        next += fec0;

                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, a, vertexfifooffset);
                        meshopt::pushVertexFifo(trigfifo, b, vertexfifooffset, feb0);
                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset, fec0);

                        meshopt::pushTriangleFifo(trigfifo, b, a, c, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);

                        u32 maxBC = std::max(b, c);
                        u32 max = std::max(maxBC, a);
                        if (max >= copied && (tbl[max] & 0x3fffff) == 0) {
                            if (maxBC >= a) {
                                tbl[max] = u64(max - (c >= b ? b : a)) << 0x2b | u64(max - (c >= b ? a : c)) << 0x16;
                            } else {
                                tbl[max] = u64(max - c) << 0x2b | u64(max - b) << 0x16;
                            }
                        }
                    } else {
                        u8 codeaux = *src1++;

                        if (codeaux == 0) {
                            next = 0;
                            last = 0;
                        }

                        s32 fea = static_cast<s32>(codetri == 0xfe);
                        s32 feb = codeaux >> 4;
                        s32 fec = codeaux & 0xf;

                        u32 a = codeaux ? next : 0;
                        next += fea;

                        u32 b = (feb == 0) ? next++ : trigfifo[(vertexfifooffset - feb) & 0xf][3];
                        u32 c = (fec == 0) ? next++ : trigfifo[(vertexfifooffset - fec) & 0xf][3];

                        if (!fea)
                            last = a = meshopt::decodeIndexFixed(src1, last);
                        
                        if (feb == 0xf)
                            last = b = meshopt::decodeIndexFixed(src1, last);
                        
                        if (fec == 0xf)
                            last = c = meshopt::decodeIndexFixed(src1, last);
                        
                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, a, vertexfifooffset);
                        meshopt::pushVertexFifo(trigfifo, b, vertexfifooffset, (feb == 0) | (feb == 0xf));
                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset, (fec == 0) | (fec == 0xf));

                        meshopt::pushTriangleFifo(trigfifo, b, a, c, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);

                        u32 maxBC = std::max(b, c);
                        u32 max = std::max(maxBC, a);
                        if (max >= copied && (tbl[max] & 0x3fffff) == 0) {
                            if (maxBC >= a) {
                                tbl[max] = u64(max - (c >= b ? b : a)) << 0x2b | u64(max - (c >= b ? a : c)) << 0x16;
                            } else {
                                tbl[max] = u64(max - c) << 0x2b | u64(max - b) << 0x16;
                            }
                        }
                    }
                }
                outBuf += 3;
            }
        }
    }

    return next;
}

u32 DecodeIndexBuffer0_WithoutTable(void* dst, s32 indexCount, u32 baseIndex, const u8*& src0, const u8*& src1, IndexFormat format) {
    constexpr u32 fecmax = 0xf;

    meshopt::TriangleFifo trigfifo;
    size_t trigfifooffset = 0;
    size_t vertexfifooffset = 0;

    u32 next = baseIndex;
    u32 last = baseIndex;

    if (format == IndexFormat::U16) {
        u16* outBuf = reinterpret_cast<u16*>(dst);

        if (indexCount > 2) {
            for (u32 i = indexCount / 3; i != 0; --i) {
                u8 codetri = *src0++;

                if (codetri < 0xf0) {
                    s32 fe = codetri >> 4;
                    s32 fec = codetri & 0xf;

                    u32 a = trigfifo[(trigfifooffset - 1 - fe) & 0xf][0];
                    u32 b = trigfifo[(trigfifooffset - 1 - fe) & 0xf][1];

                    if (fec == fecmax) {
                        u32 c = meshopt::decodeIndexFixed(src1, last);
                        last = c;

                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset);

                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);
                    } else {
                        u32 cf = trigfifo[(vertexfifooffset - 1 - fec) & 0xf][3];
                        u32 c = (fec == 0) ? next : cf;
                        s32 fec0 = static_cast<s32>(fec == 0);
                        next += fec0;

                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset, fec0);

                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);
                    }
                } else {
                    if (codetri < 0xfe) {
                        u8 codeaux = meshopt::kCodeAuxEncodingTable[codetri & 0xf];

                        u32 a = next++;

                        u32 feb = codeaux >> 4;
                        s32 fec = codeaux & 0xf;
                        
                        u32 feb0 = 0xd001u >> (codetri & 0xf) & 1; // idk how this works
                        u32 bf = trigfifo[(vertexfifooffset - feb) & 0xf][3];
                        u32 b = (feb0 != 0) ? next : bf;
                        next += feb0;
                        
                        u32 cf = trigfifo[(vertexfifooffset - fec) & 0xf][3];
                        u32 c = (fec == 0) ? next : cf;

                        s32 fec0 = static_cast<s32>(fec == 0);
                        next += fec0;

                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, a, vertexfifooffset);
                        meshopt::pushVertexFifo(trigfifo, b, vertexfifooffset, feb0);
                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset, fec0);

                        meshopt::pushTriangleFifo(trigfifo, b, a, c, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);
                    } else {
                        u8 codeaux = *src1++;

                        if (codeaux == 0) {
                            next = 0;
                            last = 0;
                        }

                        s32 fea = static_cast<s32>(codetri == 0xfe);
                        s32 feb = codeaux >> 4;
                        s32 fec = codeaux & 0xf;

                        u32 a = codeaux ? next : 0;
                        next += fea;

                        u32 b = (feb == 0) ? next++ : trigfifo[(vertexfifooffset - feb) & 0xf][3];
                        u32 c = (fec == 0) ? next++ : trigfifo[(vertexfifooffset - fec) & 0xf][3];

                        if (!fea)
                            last = a = meshopt::decodeIndexFixed(src1, last);
                        
                        if (feb == 0xf)
                            last = b = meshopt::decodeIndexFixed(src1, last);
                        
                        if (fec == 0xf)
                            last = c = meshopt::decodeIndexFixed(src1, last);
                        
                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, a, vertexfifooffset);
                        meshopt::pushVertexFifo(trigfifo, b, vertexfifooffset, (feb == 0) | (feb == 0xf));
                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset, (fec == 0) | (fec == 0xf));

                        meshopt::pushTriangleFifo(trigfifo, b, a, c, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);
                    }
                }
                outBuf += 3;
            }
        }
    } else {
        u32* outBuf = reinterpret_cast<u32*>(dst);

        if (indexCount > 2) {
            for (u32 i = indexCount / 3; i != 0; --i) {
                u8 codetri = *src0++;

                if (codetri < 0xf0) {
                    s32 fe = codetri >> 4;
                    s32 fec = codetri & 0xf;

                    u32 a = trigfifo[(trigfifooffset - 1 - fe) & 0xf][0];
                    u32 b = trigfifo[(trigfifooffset - 1 - fe) & 0xf][1];

                    if (fec == fecmax) {
                        u32 c = meshopt::decodeIndexFixed(src1, last);
                        last = c;

                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset);

                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);
                    } else {
                        u32 cf = trigfifo[(vertexfifooffset - 1 - fec) & 0xf][3];
                        u32 c = (fec == 0) ? next : cf;
                        s32 fec0 = static_cast<s32>(fec == 0);
                        next += fec0;

                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset, fec0);

                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);
                    }
                } else {
                    if (codetri < 0xfe) {
                        u8 codeaux = meshopt::kCodeAuxEncodingTable[codetri & 0xf];

                        u32 a = next++;

                        u32 feb = codeaux >> 4;
                        s32 fec = codeaux & 0xf;
                        
                        u32 feb0 = 0xd001u >> (codetri & 0xf) & 1; // idk how this works
                        u32 bf = trigfifo[(vertexfifooffset - feb) & 0xf][3];
                        u32 b = (feb0 != 0) ? next : bf;
                        next += feb0;
                        
                        u32 cf = trigfifo[(vertexfifooffset - fec) & 0xf][3];
                        u32 c = (fec == 0) ? next : cf;

                        s32 fec0 = static_cast<s32>(fec == 0);
                        next += fec0;

                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, a, vertexfifooffset);
                        meshopt::pushVertexFifo(trigfifo, b, vertexfifooffset, feb0);
                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset, fec0);

                        meshopt::pushTriangleFifo(trigfifo, b, a, c, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);
                    } else {
                        u8 codeaux = *src1++;

                        if (codeaux == 0) {
                            next = 0;
                            last = 0;
                        }

                        s32 fea = static_cast<s32>(codetri == 0xfe);
                        s32 feb = codeaux >> 4;
                        s32 fec = codeaux & 0xf;

                        u32 a = codeaux ? next : 0;
                        next += fea;

                        u32 b = (feb == 0) ? next++ : trigfifo[(vertexfifooffset - feb) & 0xf][3];
                        u32 c = (fec == 0) ? next++ : trigfifo[(vertexfifooffset - fec) & 0xf][3];

                        if (!fea)
                            last = a = meshopt::decodeIndexFixed(src1, last);
                        
                        if (feb == 0xf)
                            last = b = meshopt::decodeIndexFixed(src1, last);
                        
                        if (fec == 0xf)
                            last = c = meshopt::decodeIndexFixed(src1, last);
                        
                        meshopt::writeTriangle(outBuf, a, b, c);

                        meshopt::pushVertexFifo(trigfifo, a, vertexfifooffset);
                        meshopt::pushVertexFifo(trigfifo, b, vertexfifooffset, (feb == 0) | (feb == 0xf));
                        meshopt::pushVertexFifo(trigfifo, c, vertexfifooffset, (fec == 0) | (fec == 0xf));

                        meshopt::pushTriangleFifo(trigfifo, b, a, c, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, c, b, a, trigfifooffset);
                        meshopt::pushTriangleFifo(trigfifo, a, c, b, trigfifooffset);
                    }
                }
                outBuf += 3;
            }
        }
    }
    
    return next;
}

u32 DecodeIndexBuffer2(void* dst, IndexFormat indexFormat, s32 indexCount, u32 baseIndex, u64* tbl, u32 a6, u32 numCopied [[maybe_unused]], u32 start, const u8*& src0, const u8*& src1) {
    // this probably should be split into two functions like with the above
    meshopt::VertexFifo vertexfifo;
    size_t vertexfifooffset = 0;
    u32 current = start - baseIndex;

    if (tbl) {
        u32 copied = a6 - baseIndex;
        u32 base = baseIndex - a6;
        if (indexFormat == IndexFormat::U16) {
            u16* outBuf = reinterpret_cast<u16*>(dst);

            u8 codetria = *src0++;
            u32 a;
            if (codetria == 0) {
                a = current++;
                meshopt::pushVertexFifo(vertexfifo, a, vertexfifooffset);
            } else if (codetria > 0x40) {
                a = meshopt::decodeIndex(src1, current);
                meshopt::pushVertexFifo(vertexfifo, a, vertexfifooffset);
            } else {
                a = vertexfifo[-static_cast<u32>(codetria) & 0x3f];
            }

            u8 codetrib = *src0++;
            u32 b;
            if (codetrib == 0) {
                b = current++;
                meshopt::pushVertexFifo(vertexfifo, b, vertexfifooffset);
            } else if (codetrib > 0x40) {
                b = meshopt::decodeIndex(src1, current);
                meshopt::pushVertexFifo(vertexfifo, b, vertexfifooffset);
            } else {
                b = vertexfifo[-static_cast<u32>(codetrib) & 0x3f];
            }

            u8 codetric = *src0++;
            u32 c;
            if (codetric == 0) {
                c = current++;
                meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
            } else if (codetric > 0x40) {
                c = meshopt::decodeIndex(src1, current);
                meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
            } else {
                c = vertexfifo[-static_cast<u32>(codetric) & 0x3f];
            }

            // could just use writeTriangle but I don't think this is actually a triangle
            outBuf[0] = a;
            outBuf[1] = b;
            outBuf[2] = c;

            current = (a != 0 || b != 1 || c != 2) ? current : 3;

            u64 unk = (1 << ((a & 0xf) << 2)) + (1 << ((b & 0xf) << 2)) + (1 << ((c & 0xf) << 2));
            if ((unk & 0x6666666666666666) == 0) {
                u32 maxAC = std::max(a, c);
                u32 max = std::max(maxAC, b);
                u32 val0 = (maxAC >= b) ? ((c >= a) ? c : b) : c;
                u32 val1 = (maxAC >= b) ? ((c >= a) ? a : b) : a;
                u32 unkValue = std::min(val1, val0);

                if (unkValue >= copied) {
                    tbl[base + max] = u64(max - val1) << 0x2b | u64(max - val0) << 0x16;
                }
            }

            if (indexCount < 4)
                return baseIndex + current;

            u32 flip = 0;
            for (s32 i = 3; i < indexCount; ++i) {
                u8 codetri = *src0++;
                u32 value;
                if (codetri == 0) {
                    value = current++;
                    meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
                } else if (codetri > 0x40) {
                    value = meshopt::decodeIndex(src1, current);
                    meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
                } else {
                    value = vertexfifo[-static_cast<u32>(codetri) & 0x3f];
                }
                outBuf[i] = value;

                unk += 1 << ((value & 0xf) << 2);
                u32 v0 = outBuf[i - 1 - flip];
                u32 v1 = outBuf[i - 1 - (flip ^ 1)];
                u32 v2 = outBuf[i - 3];

                bool unkCond = (unk & 0xeeeeeeeeeeeeeeee) != 0;
                unk += (-1 << ((v2 & 0xf) << 2));

                // idk wtf this condition is supposed to mean and I don't want to think through it
                if ((unkCond || v0 >= value || v1 > value) || (!unkCond && value >= v0 && (unkCond || value != v0) && value == v1)
                    || v2 > value || ((((!unkCond && value >= v0 && (unkCond || value != v0)) && value >= v1)
                    && (unkCond || !(value >= v0) || value == v0 || value != v1)) && value == v2)) {
                    if (!unkCond) {
                        u32 maxAB = std::max(value, v0);
                        u32 max = std::max(maxAB, v1);
                        u32 val0 = (maxAB >= v1) ? ((value >= v0) ? v1 : value) : v0;
                        u32 val1 = (maxAB >= v1) ? ((value >= v0) ? v0 : v1) : value;
                        u32 unkValue = std::min(val0, val1);

                        if (unkValue >= copied && (tbl[base + max] & 0x3fffff) == 0) {
                            tbl[base + max] = u64(max - val0) << 0x2b | u64(max - val1) << 0x16;
                        }
                    }
                } else {
                    u32 uVar2 = std::min(std::min(v0, v1), v2);
                    if (copied > uVar2) {
                        if (!unkCond) {
                            u32 maxAB = std::max(value, v0);
                            u32 max = std::max(maxAB, v1);
                            u32 val0 = (maxAB >= v1) ? ((value >= v0) ? v1 : value) : v0;
                            u32 val1 = (maxAB >= v1) ? ((value >= v0) ? v0 : v1) : value;
                            u32 unkValue = std::min(val0, val1);
    
                            if (unkValue >= copied && (tbl[base + max] & 0x3fffff) == 0) {
                                tbl[base + max] = u64(max - val0) << 0x2b | u64(max - val1) << 0x16;
                            }
                        }
                    } else {
                        tbl[base + value] = u64(value - v2) | u64(value - v0) << 0x16 | u64(value - v1) << 0x2b;
                    }
                }

                flip ^= 1;
            }
        } else {
            u32* outBuf = reinterpret_cast<u32*>(dst);

            u8 codetria = *src0++;
            u32 a;
            if (codetria == 0) {
                a = current++;
                meshopt::pushVertexFifo(vertexfifo, a, vertexfifooffset);
            } else if (codetria > 0x40) {
                a = meshopt::decodeIndex(src1, current);
                meshopt::pushVertexFifo(vertexfifo, a, vertexfifooffset);
            } else {
                a = vertexfifo[-static_cast<u32>(codetria) & 0x3f];
            }

            u8 codetrib = *src0++;
            u32 b;
            if (codetrib == 0) {
                b = current++;
                meshopt::pushVertexFifo(vertexfifo, b, vertexfifooffset);
            } else if (codetrib > 0x40) {
                b = meshopt::decodeIndex(src1, current);
                meshopt::pushVertexFifo(vertexfifo, b, vertexfifooffset);
            } else {
                b = vertexfifo[-static_cast<u32>(codetrib) & 0x3f];
            }

            u8 codetric = *src0++;
            u32 c;
            if (codetric == 0) {
                c = current++;
                meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
            } else if (codetric > 0x40) {
                c = meshopt::decodeIndex(src1, current);
                meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
            } else {
                c = vertexfifo[-static_cast<u32>(codetric) & 0x3f];
            }

            // could just use writeTriangle but I don't think this is actually a triangle
            outBuf[0] = a;
            outBuf[1] = b;
            outBuf[2] = c;

            current = (a != 0 || b != 1 || c != 2) ? current : 3;

            u64 unk = (1 << ((a & 0xf) << 2)) + (1 << ((b & 0xf) << 2)) + (1 << ((c & 0xf) << 2));
            if ((unk & 0x6666666666666666) == 0) {
                u32 maxAC = std::max(a, c);
                u32 max = std::max(maxAC, b);
                u32 val0 = (maxAC >= b) ? ((c >= a) ? c : b) : c;
                u32 val1 = (maxAC >= b) ? ((c >= a) ? a : b) : a;
                u32 unkValue = std::min(val1, val0);

                if (unkValue >= copied) {
                    tbl[base + max] = u64(max - val1) << 0x2b | u64(max - val0) << 0x16;
                }
            }

            if (indexCount < 4)
                return baseIndex + current;

            u32 flip = 0;
            for (s32 i = 3; i < indexCount; ++i) {
                u8 codetri = *src0++;
                u32 value;
                if (codetri == 0) {
                    value = current++;
                    meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
                } else if (codetri > 0x40) {
                    value = meshopt::decodeIndex(src1, current);
                    meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
                } else {
                    value = vertexfifo[-static_cast<u32>(codetri) & 0x3f];
                }
                outBuf[i] = value;

                unk += 1 << ((value & 0xf) << 2);
                u32 v0 = outBuf[i - 1 - flip];
                u32 v1 = outBuf[i - 1 - (flip ^ 1)];
                u32 v2 = outBuf[i - 3];

                bool unkCond = (unk & 0xeeeeeeeeeeeeeeee) != 0;
                unk += (-1 << ((v2 & 0xf) << 2));

                if ((unkCond || v0 >= value || v1 > value) || (!unkCond && value >= v0 && (unkCond || value != v0) && value == v1)
                    || v2 > value || ((((!unkCond && value >= v0 && (unkCond || value != v0)) && value >= v1)
                    && (unkCond || !(value >= v0) || value == v0 || value != v1)) && value == v2)) {
                    if (!unkCond) {
                        u32 maxAB = std::max(value, v0);
                        u32 max = std::max(maxAB, v1);
                        u32 val0 = (maxAB >= v1) ? ((value >= v0) ? v1 : value) : v0;
                        u32 val1 = (maxAB >= v1) ? ((value >= v0) ? v0 : v1) : value;
                        u32 unkValue = std::min(val0, val1);

                        if (unkValue >= copied && (tbl[base + max] & 0x3fffff) == 0) {
                            tbl[base + max] = u64(max - val0) << 0x2b | u64(max - val1) << 0x16;
                        }
                    }
                } else {
                    u32 uVar2 = std::min(std::min(v0, v1), v2);
                    if (copied > uVar2) {
                        if (!unkCond) {
                            u32 maxAB = std::max(value, v0);
                            u32 max = std::max(maxAB, v1);
                            u32 val0 = (maxAB >= v1) ? ((value >= v0) ? v1 : value) : v0;
                            u32 val1 = (maxAB >= v1) ? ((value >= v0) ? v0 : v1) : value;
                            u32 unkValue = std::min(val0, val1);
    
                            if (unkValue >= copied && (tbl[base + max] & 0x3fffff) == 0) {
                                tbl[base + max] = u64(max - val0) << 0x2b | u64(max - val1) << 0x16;
                            }
                        }
                    } else {
                        tbl[base + value] = u64(value - v2) | u64(value - v0) << 0x16 | u64(value - v1) << 0x2b;
                    }
                }

                flip ^= 1;
            }
        }
    } else {
        if (indexFormat == IndexFormat::U16) {
            u16* outBuf = reinterpret_cast<u16*>(dst);

            u8 codetria = *src0++;
            u32 a;
            if (codetria == 0) {
                a = current++;
                meshopt::pushVertexFifo(vertexfifo, a, vertexfifooffset);
            } else if (codetria > 0x40) {
                a = meshopt::decodeIndex(src1, current);
                meshopt::pushVertexFifo(vertexfifo, a, vertexfifooffset);
            } else {
                a = vertexfifo[-static_cast<u32>(codetria) & 0x3f];
            }

            u8 codetrib = *src0++;
            u32 b;
            if (codetrib == 0) {
                b = current++;
                meshopt::pushVertexFifo(vertexfifo, b, vertexfifooffset);
            } else if (codetrib > 0x40) {
                b = meshopt::decodeIndex(src1, current);
                meshopt::pushVertexFifo(vertexfifo, b, vertexfifooffset);
            } else {
                b = vertexfifo[-static_cast<u32>(codetrib) & 0x3f];
            }

            u8 codetric = *src0++;
            u32 c;
            if (codetric == 0) {
                c = current++;
                meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
            } else if (codetric > 0x40) {
                c = meshopt::decodeIndex(src1, current);
                meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
            } else {
                c = vertexfifo[-static_cast<u32>(codetric) & 0x3f];
            }

            // could just use writeTriangle but I don't think this is actually a triangle
            outBuf[0] = a;
            outBuf[1] = b;
            outBuf[2] = c;

            current = (a != 0 || b != 1 || c != 2) ? current : 3;

            if (indexCount < 4)
                return baseIndex + current;

            for (s32 i = 3; i < indexCount; ++i) {
                u8 codetri = *src0++;
                u32 value;
                if (codetri == 0) {
                    value = current++;
                    meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
                } else if (codetri > 0x40) {
                    value = meshopt::decodeIndex(src1, current);
                    meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
                } else {
                    value = vertexfifo[-static_cast<u32>(codetri) & 0x3f];
                }
                outBuf[i] = value;
            }
        } else {
            u32* outBuf = reinterpret_cast<u32*>(dst);

            u8 codetria = *src0++;
            u32 a;
            if (codetria == 0) {
                a = current++;
                meshopt::pushVertexFifo(vertexfifo, a, vertexfifooffset);
            } else if (codetria > 0x40) {
                a = meshopt::decodeIndex(src1, current);
                meshopt::pushVertexFifo(vertexfifo, a, vertexfifooffset);
            } else {
                a = vertexfifo[-static_cast<u32>(codetria) & 0x3f];
            }

            u8 codetrib = *src0++;
            u32 b;
            if (codetrib == 0) {
                b = current++;
                meshopt::pushVertexFifo(vertexfifo, b, vertexfifooffset);
            } else if (codetrib > 0x40) {
                b = meshopt::decodeIndex(src1, current);
                meshopt::pushVertexFifo(vertexfifo, b, vertexfifooffset);
            } else {
                b = vertexfifo[-static_cast<u32>(codetrib) & 0x3f];
            }

            u8 codetric = *src0++;
            u32 c;
            if (codetric == 0) {
                c = current++;
                meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
            } else if (codetric > 0x40) {
                c = meshopt::decodeIndex(src1, current);
                meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
            } else {
                c = vertexfifo[-static_cast<u32>(codetric) & 0x3f];
            }

            // could just use writeTriangle but I don't think this is actually a triangle
            outBuf[0] = a;
            outBuf[1] = b;
            outBuf[2] = c;

            current = (a != 0 || b != 1 || c != 2) ? current : 3;

            if (indexCount < 4)
                return baseIndex + current;

            for (s32 i = 3; i < indexCount; ++i) {
                u8 codetri = *src0++;
                u32 value;
                if (codetri == 0) {
                    value = current++;
                    meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
                } else if (codetri > 0x40) {
                    value = meshopt::decodeIndex(src1, current);
                    meshopt::pushVertexFifo(vertexfifo, c, vertexfifooffset);
                } else {
                    value = vertexfifo[-static_cast<u32>(codetri) & 0x3f];
                }
                outBuf[i] = value;
            }
        }
    }

    return baseIndex + current;
}

u32 DecodeIndexBuffer3(void* dst, IndexFormat indexFormat, s32 indexCount, u32 baseIndex, u32 a5 [[maybe_unused]], u64* decodeBuf [[maybe_unused]], u32 numCopied [[maybe_unused]], u32 start, const u8*& src0, const u8*& src1) {
    meshopt::VertexFifo vertexfifo;
    size_t vertexfifooffset = 0;
    u32 current = start - baseIndex;
    
    if (indexFormat == IndexFormat::U16) {
        u16* outBuf = reinterpret_cast<u16*>(dst);
        
        if (indexCount > 0) {
            for (s32 i = 0; i < indexCount; ++i) {
                u8 codetri = *src0++;

                u32 value;
                if (codetri == 0) {
                    meshopt::pushVertexFifo(vertexfifo, current, vertexfifooffset);
                    value = current++;
                } else if (codetri < 0x41) {
                    value = vertexfifo[(vertexfifooffset - codetri) & 0x3f];
                } else {
                    value = meshopt::decodeIndex(src1, current);
                    meshopt::pushVertexFifo(vertexfifo, value, vertexfifooffset);
                }

                outBuf[i] = value;
            }
        }
    } else {
        u32* outBuf = reinterpret_cast<u32*>(dst);
        
        if (indexCount > 0) {
            for (s32 i = 0; i < indexCount; ++i) {
                u8 codetri = *src0++;

                u32 value;
                if (codetri == 0) {
                    meshopt::pushVertexFifo(vertexfifo, current, vertexfifooffset);
                    value = current++;
                } else if (codetri < 0x41) {
                    value = vertexfifo[(vertexfifooffset - codetri) & 0x3f];
                } else {
                    value = meshopt::decodeIndex(src1, current);
                    meshopt::pushVertexFifo(vertexfifo, value, vertexfifooffset);
                }

                outBuf[i] = value;
            }
        }
    }

    return baseIndex + current;
}

} // namespace mc