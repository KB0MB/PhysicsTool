#include "mc_VertexDecompContext.h"
#include "mc_VertexDecompressor.h"
#include "mc_VertexCodec.h"
#include "mc_DecompContext.h"
#include "mc_StackAllocator.h"

#include "mc_IndexCodec.h"

#include <cstring> // std::memset

namespace mc {

void VertexDecompContext::ReadVertexBlockGroup(DecompContext& ctx, VertexDecompressor* decompressor, s32 attrCount) {
    groupMask = ctx.bitStream0.Read(attrCount);

    if (groupMask == 0) {
        stage = 1;
        return;
    }

    stage = ctx.bitStream0.Read(1);
    if (stage == 1)
        return;
    
    u32 count = Popcount(groupMask);
    blockSizes.vertexCountStreamSize = meshopt::decodeVByte(ctx.currentPos);
    blockSizes.bitStreamSize = meshopt::decodeVByte(ctx.currentPos);
    blockSizes.backrefCountStreamSize = blockSizes.vertexCountStreamSize - count;
    blockSizes.backrefOffsetStreamSize = blockSizes.vertexCountStreamSize - ctx.bitStream0.Read(0x20 - Clz(count));

    if (blockSizes.vertexCountStreamSize < 1) {
        decodeStreams.vertexCountStream = reinterpret_cast<u8*>(&decodeStreams.vertexCountStream);
    } else {
        allocations.vertexCountStream = decompressor->ProcessBlock(decodeStreams.vertexCountStream, ElementType::U8, 1, blockSizes.vertexCountStreamSize, 0, ctx);
    }
    if (blockSizes.backrefCountStreamSize < 1) {
        decodeStreams.backrefCountStream = reinterpret_cast<u8*>(&decodeStreams.backrefCountStream);
    } else {
        allocations.backrefCountStream = decompressor->ProcessBlock(decodeStreams.backrefCountStream, ElementType::U8, 1, blockSizes.backrefCountStreamSize, 0, ctx);
    }
    if (blockSizes.backrefOffsetStreamSize < 1) {
        decodeStreams.backrefOffsetStream = reinterpret_cast<u8*>(&decodeStreams.backrefOffsetStream);
    } else {
        allocations.backrefOffsetStream = decompressor->ProcessBlock(decodeStreams.backrefOffsetStream, ElementType::U16, 1, blockSizes.backrefOffsetStreamSize, 0, ctx);
    }
    if (blockSizes.bitStreamSize < 1) {
        decodeStreams.bitStream = reinterpret_cast<u8*>(&decodeStreams.bitStream);
    } else {
        allocations.bitStream = decompressor->ProcessBlock(decodeStreams.bitStream, ElementType::U8, 1, blockSizes.bitStreamSize, 0xf, ctx);
    }

    bitStream.SetBitOffset(0);
    bitStream.SetStream(reinterpret_cast<u64*>(decodeStreams.bitStream));
    bitStream.SetRemainder(0);
    bitStream.SetDirection(BitStreamReader::Direction::Forwards);
}

u32 VertexDecompContext::ProcessVertexBlockGroup(VertexDecodeGroup** outGroups, u32* groupCount, DecompContext& ctx, VertexDecompressor* decompressor,
                                                    VertexStreamContext& streamCtx, s32 vertexCount, StackAllocator* allocator) {
    u32 oldMask = groupMask;
    groupMask >>= 1;
    if ((oldMask & 1) == 0) {
        currentGroup.vertexCount = vertexCount;
        currentGroup.backRefOffset = 0;
        *outGroups = &currentGroup;
        *groupCount = 1;
        return vertexCount;
    }

    /*
    struct AttributeFlags {
        u32 componentCount      : 3;
        u32 unk                 : 2; // some scale thing? becomes a left shift
        u32 padding             : 3;
        u32 componentbitSize    : 8;
        u32 attributeBitSize    : 8;
        u32 stride              : 8;
    };
    */
    u32 attrFlags = streamCtx.attrFlags[streamCtx.attrIndex];
    u32 unk = attrFlags >> 3 & 3;
    u32 sizeBytes = (((attrFlags >> 8 & 0xff) + 7) >> 3) * (attrFlags & 7);
    u32 format = sizeBytes > 5 ? 1 : (sizeBytes < 3 ? 3 : 2);

    if (stage == 0) {
        if (oldMask < 2) {
            numGroups = blockSizes.vertexCountStreamSize;
        } else {
            numGroups = meshopt::decodeVByte(ctx.currentPos);
            blockSizes.vertexCountStreamSize -= numGroups;
        }
        
        if (numGroups == 0) {
            groups = nullptr;
        } else {
            groups = reinterpret_cast<VertexDecodeGroup*>(allocator->Alloc(numGroups * sizeof(VertexDecodeGroup), 8));
        }

        u32 vertCount = Parse(groups, numGroups, decodeStreams, bitStream, attrFlags >> 0x18, unk, format, vertexCount);

        *outGroups = groups;
        *groupCount = numGroups;
        return vertCount;
    } else {
        if (stage != 1)
            return 0;
        blockSizes.vertexCountStreamSize = meshopt::decodeVByte(ctx.currentPos);
        numGroups = blockSizes.vertexCountStreamSize;
        blockSizes.bitStreamSize = meshopt::decodeVByte(ctx.currentPos);
        blockSizes.backrefCountStreamSize = blockSizes.vertexCountStreamSize - 1;
        blockSizes.backrefOffsetStreamSize = blockSizes.vertexCountStreamSize - ctx.bitStream0.Read(1);

        if (blockSizes.vertexCountStreamSize < 1) {
            decodeStreams.vertexCountStream = reinterpret_cast<u8*>(&decodeStreams.vertexCountStream);
            allocations.vertexCountStream = nullptr;
        } else {
            groups = reinterpret_cast<VertexDecodeGroup*>(allocator->Alloc(numGroups * sizeof(VertexDecodeGroup), 8));
            allocations.vertexCountStream = decompressor->ProcessBlock(decodeStreams.vertexCountStream, ElementType::U8, 1, blockSizes.vertexCountStreamSize, 0, ctx);
        }
        if (blockSizes.backrefCountStreamSize < 1) {
            decodeStreams.backrefCountStream = reinterpret_cast<u8*>(&decodeStreams.backrefCountStream);
            allocations.backrefCountStream = nullptr;
        } else {
            allocations.backrefCountStream = decompressor->ProcessBlock(decodeStreams.backrefCountStream, ElementType::U8, 1, blockSizes.backrefCountStreamSize, 0, ctx);
        }
        if (blockSizes.backrefOffsetStreamSize < 1) {
            decodeStreams.backrefOffsetStream = reinterpret_cast<u8*>(&decodeStreams.backrefOffsetStream);
            allocations.backrefOffsetStream = nullptr;
        } else {
            allocations.backrefOffsetStream = decompressor->ProcessBlock(decodeStreams.backrefOffsetStream, ElementType::U16, 1, blockSizes.backrefOffsetStreamSize, 0, ctx);
        }
        if (blockSizes.bitStreamSize < 1) {
            decodeStreams.bitStream = reinterpret_cast<u8*>(&decodeStreams.bitStream);
            allocations.bitStream = nullptr;
        } else {
            allocations.bitStream = decompressor->ProcessBlock(decodeStreams.bitStream, ElementType::U8, 1, blockSizes.bitStreamSize, 0xf, ctx);
        }

        bitStream.SetStream(reinterpret_cast<u64*>(decodeStreams.bitStream));
        bitStream.SetRemainder(0);
        bitStream.SetBitOffset(0);
        bitStream.SetDirection(BitStreamReader::Direction::Forwards);

        u32 vertCount = Parse(groups, numGroups, decodeStreams, bitStream, attrFlags >> 0x18, unk, format, vertexCount);

        allocator->Free(allocations.bitStream);
        allocator->Free(allocations.backrefOffsetStream);
        allocator->Free(allocations.backrefCountStream);
        allocator->Free(allocations.vertexCountStream);

        *outGroups = groups;
        *groupCount = numGroups;
        return vertCount;
    }
}

void ReadVertexInfoTableBlock(VertexDecodingStreamSet& streams, VertexDecodingStreamSizes& sizes, AllocationSet& allocations, u32 a4 [[maybe_unused]], DecompContext& ctx, VertexDecompressor* decompressor) {
    s32 count = meshopt::decodeVByte(ctx.currentPos);
    if (count < 1) {
        sizes.vertexCountStreamSize = 0;
        sizes.backrefCountStreamSize = count;
        sizes.backrefOffsetStreamSize = count;
        sizes.bitStreamSize = 0;
        allocations.vertexCountStream = nullptr;
        streams.vertexCountStream = nullptr;
    } else {
        sizes.vertexCountStreamSize = meshopt::decodeVByte(ctx.currentPos);
        sizes.backrefCountStreamSize = count;
        sizes.backrefOffsetStreamSize = count;
        sizes.bitStreamSize = meshopt::decodeVByte(ctx.currentPos);
    }

    if (sizes.vertexCountStreamSize < 1) {
        streams.vertexCountStream = nullptr;
        allocations.vertexCountStream = nullptr;
    } else {
        allocations.vertexCountStream = decompressor->ProcessBlock(streams.vertexCountStream, ElementType::U8, 1, sizes.vertexCountStreamSize, 0, ctx);
    }
    if (sizes.backrefCountStreamSize < 1) {
        streams.backrefCountStream = nullptr;
        allocations.backrefCountStream = nullptr;
    } else {
        allocations.backrefCountStream = decompressor->ProcessBlock(streams.backrefCountStream, ElementType::U8, 1, sizes.backrefCountStreamSize, 0, ctx);
    }
    if (sizes.backrefOffsetStreamSize < 1) {
        streams.backrefOffsetStream = nullptr;
        allocations.backrefOffsetStream = nullptr;
    } else {
        allocations.backrefOffsetStream = decompressor->ProcessBlock(streams.backrefOffsetStream, ElementType::U8, 1, sizes.backrefOffsetStreamSize, 0, ctx);
    }
    if (sizes.bitStreamSize < 1) {
        streams.bitStream = nullptr;
        allocations.bitStream = nullptr;
    } else {
        allocations.bitStream = decompressor->ProcessBlock(streams.bitStream, ElementType::U8, 1, sizes.bitStreamSize, 0xf, ctx);
    }
}

void DecodeVertexInfoTable(u32* tbl, s32 numVertices, VertexDecodingStreamSet& inputStreams, VertexDecodingStreamSizes& inputStreamSizes, u32 a5 [[maybe_unused]], VertexInfoTableInfo& a6, s32 baseVertex) {
    u32 backRefOffsetCount = inputStreamSizes.backrefOffsetStreamSize;
    if (backRefOffsetCount == 0) {
        if (numVertices != 0) {
            std::memset(tbl, 0, numVertices * sizeof(u32));
            return;
        }
    } else {
        u32 indexAccumulator = a6._00;
        u32 unkCounter = a6._04;
        s32 copied = 0;
        if (backRefOffsetCount > 0) {
            u8* vertexCountStream = inputStreams.vertexCountStream;
            u8* indexStream = inputStreams.backrefCountStream;
            u8* fifoIndexStream = inputStreams.backrefOffsetStream;
            BitStreamReader reader(reinterpret_cast<u64*>(inputStreams.bitStream), BitStreamReader::Direction::Forwards);
            s32 lastIndex = -1;
            u32 mask = a6._0c;

            for (u32 i = backRefOffsetCount; i != 0; --i) {
                u8 codepoint = *indexStream++;
                s32 index = static_cast<s32>(codepoint);
                if (codepoint > 0xf)
                    index = cVertexGroupEncodingTable[codepoint - 0x10][1] + reader.ReadForwards(cVertexGroupEncodingTable[codepoint - 0x10][0]) + 0x10;
                
                codepoint = *fifoIndexStream++;
                s32 packedValue = static_cast<s32>(codepoint);
                if (codepoint > 0xf)
                    packedValue = cVertexGroupEncodingTable[codepoint - 0x10][1] + reader.ReadForwards(cVertexGroupEncodingTable[codepoint - 0x10][0]) + 0x10;

                index += lastIndex;
                lastIndex = index;
                s32 unkIndexValue = (-(packedValue & 1) ^ packedValue >> 1) + indexAccumulator;
                unkIndexValue += ((unkCounter + 1) & unkIndexValue >> 0x1f);
                indexAccumulator = unkIndexValue - (static_cast<s32>(unkCounter) >= unkIndexValue ? 0 : unkCounter + 1);
                u32 fifoIndex = indexAccumulator & mask;

                if (indexAccumulator == unkCounter) {
                    a6._10[fifoIndex] = ((index + baseVertex) * -8) | (*vertexCountStream++);
                    ++unkCounter;
                } else {
                    u32 count = index - copied;
                    if (count != 0) {
                        u32 extra = count & 7;
                        if (extra) {
                            for (; extra != 0; --extra)
                                tbl[copied++] = 0;
                            count = index - copied;
                        }
                        if (count > 7) {
                            for (u32 j = 0; j != count; j += 8) {
                                tbl[copied + j + 0] = 0;
                                tbl[copied + j + 1] = 0;
                                tbl[copied + j + 2] = 0;
                                tbl[copied + j + 3] = 0;
                                tbl[copied + j + 4] = 0;
                                tbl[copied + j + 5] = 0;
                                tbl[copied + j + 6] = 0;
                                tbl[copied + j + 7] = 0;
                            }
                        }
                    }
                    copied = index + 1;
                    tbl[lastIndex] = a6._10[fifoIndex] + (index + baseVertex) * 8;
                    a6._10[fifoIndex] = (index + baseVertex) * -8;
                }
            }
        }
        if (copied < numVertices) {
            std::memset(tbl + copied, 0, (numVertices - copied) * sizeof(u32));
        }
        a6._00 = indexAccumulator;
        a6._04 = unkCounter;
    }
}

u32 Parse(VertexDecodeGroup* groups, s32 count, VertexDecodingStreamSet& inputStreams, BitStreamReader& bitStream, u32 stride, u32 a6, u32 format, u32 totalVertexCount) {
    u8* vertexCountStream = inputStreams.vertexCountStream;
    u8* backrefCountStream = inputStreams.backrefCountStream;
    u16* backrefOffsetStream = reinterpret_cast<u16*>(inputStreams.backrefOffsetStream);

    VertexDecodeGroup* baseGroup = groups - 1;

    s32 totalRemaining = 0;
    s32 vertexCount = 0;
    if (count > 1) {
        u32 advanceIndex = 0;
        for (u32 i = count; i > 1; --i) {
            u8 codepoint = *vertexCountStream++;
            u32 vertCount = static_cast<u32>(codepoint);
            if (codepoint > 0xf)
                vertCount = cVertexGroupEncodingTable[codepoint - 0x10][1] + bitStream.ReadForwards(cVertexGroupEncodingTable[codepoint - 0x10][0]) + 0x10;
            
            codepoint = *backrefCountStream++;
            u32 backrefs = static_cast<u32>(codepoint);
            if (codepoint > 0xf)
                backrefs = cVertexGroupEncodingTable[codepoint - 0x10][1] + bitStream.ReadForwards(cVertexGroupEncodingTable[codepoint - 0x10][0]) + 0x10;

            u16 codepointo = *backrefOffsetStream++;
            u32 backrefIndex = static_cast<u32>(codepointo);
            s32 backrefOffset;
            if (codepointo > 2) {
                const u32 nbits = (codepointo - 3) & 0x1f;
                const u64 value = bitStream.ReadForwards(nbits);
                backrefOffset = (((codepointo - 3) >> 5) << a6) + ((nbits == 0 ? 0 : value) + ~(-1 << nbits)) * stride;
                baseGroup += advanceIndex + 1;
                advanceIndex = 0;
            } else {
                if (vertCount == 0)
                    ++backrefIndex;

                backrefOffset = (baseGroup - backrefIndex)->backRefOffset;
                baseGroup += (advanceIndex + 1) & -static_cast<u32>(backrefIndex != 0);
                advanceIndex = (advanceIndex + 1) & -static_cast<u32>(backrefIndex == 0);
            }

            groups->vertexCount = vertCount | (backrefs + format) << 0x10;
            groups->backRefOffset = backrefOffset;
            ++groups;

            totalRemaining += vertCount + backrefs + format;
            vertexCount += vertCount;
        }
    }

    u8 codepoint = *vertexCountStream++;
    u32 vertCount = static_cast<u32>(codepoint);
    if (codepoint > 0xf)
        vertCount = cVertexGroupEncodingTable[codepoint - 0x10][1] + bitStream.ReadForwards(cVertexGroupEncodingTable[codepoint - 0x10][0]) + 0x10;

    u32 backrefCount = 0;
    u32 backrefOffset = 0;
    if (vertCount + totalRemaining < totalVertexCount) {
        backrefCount = totalVertexCount - (vertCount + totalRemaining);
        u16 codepointo = *backrefOffsetStream++;
        u32 backrefIndex = static_cast<u32>(codepointo);
        if (codepointo < 3) {
            if (vertCount == 0)
                ++backrefIndex;
            
            backrefOffset = (baseGroup - backrefIndex)->backRefOffset;
        } else {
            const u32 nbits = (codepointo - 3) & 0x1f;
            const u64 value = bitStream.ReadForwards(nbits);
            backrefOffset = (((codepointo - 3) >> 5) << a6) + ((nbits == 0 ? 0 : value) + ~(-1 << nbits)) * stride;
        }
    }

    groups->vertexCount = vertCount | backrefCount << 0x10;
    groups->backRefOffset = backrefOffset;
    inputStreams.vertexCountStream = vertexCountStream;
    inputStreams.backrefCountStream = backrefCountStream;
    inputStreams.backrefOffsetStream = reinterpret_cast<u8*>(backrefOffsetStream);

    return vertCount + vertexCount;
}

void VertexDecompContext::FinishGroupProcessing(StackAllocator* allocator) {
    if ((stage == 0 || stage == 1) && groups) {
        allocator->Free(groups);
        groups = nullptr;
        numGroups = 0;
    }
}

void VertexDecompContext::Reset(StackAllocator* allocator) {
    if (stage == 0) {
        allocator->Free(allocations.bitStream);
        allocator->Free(allocations.backrefOffsetStream);
        allocator->Free(allocations.backrefCountStream);
        allocator->Free(allocations.vertexCountStream);   
    }
    
    stage = 2;
    groupMask = 0;
}

} // namespace mc