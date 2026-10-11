#include "mc_IndexStreamContext.h"
#include <algorithm>
#include "mc_IndexDecompressor.h"
#include "mc_DecompContext.h"
#include "mc_StackAllocator.h"

#include "mc_IndexCodec.h"

#include <cstring> // std::memset

namespace mc {

bool IndexStreamContext::ParseIndexHeader(DecompContext& ctx) {
    u32 count = meshopt::decodeVByte(ctx.currentPos);

    if (count) {
        u8 meshoptIdxHeader = *ctx.currentPos++;
        indexFormat = static_cast<IndexFormat>(meshoptIdxHeader & 0xf);
        encodingType = static_cast<EncodingType>(meshoptIdxHeader >> 4);
        blockCount = meshopt::decodeVByte(ctx.currentPos);
        rawCount = meshopt::decodeVByte(ctx.currentPos);
        baseIndex = meshopt::decodeVByte(ctx.currentPos);
        blocksRemaining = blockCount;
        streamCount = count;
        indicesRemaining = rawCount >> (encodingType == EncodingType::_01);
    }

    return count != 0;
}

static inline DecompressIndexFunc GetDecompFunc(EncodingType t) {
    switch (t) {
        case EncodingType::_00:
        case EncodingType::_01:
            return DecompressIndexStream1;
        case EncodingType::_03:
            return DecompressIndexStream3;
        case EncodingType::_02:
        default:
            return DecompressIndexStream2;
    }
}

// this might be for triangle strips? I don't want to have to think through how this works though
template <typename T>
static void PostProcessIndexBuffer(T* outBuf, T* inBuf, const s32 indexCount, const s32 vertexCount [[maybe_unused]], StackAllocator* allocator) {
    u32 indices = static_cast<u32>(indexCount < 0 ? indexCount + 1 : indexCount) >> 1;
    u64 indices64 = static_cast<u64>(indices);

    // search for min + max index values
    u32 min;
    u32 max;
    if (indexCount + 1 < 3) {
        min = 0;
        max = 0;
    } else {
        min = *reinterpret_cast<u32*>(inBuf);
        max = min;
        if (indexCount > 3) {
            u32 index;
            if ((indexCount & 0xfffffffeu) == 4) {
                index = 1;
            } else {
                for (index = 0; index + 2 != ((indices64 - 1) & 0xfffffffffffffffe); index += 2) {
                    min = std::min(static_cast<u32>(inBuf[index + 1]), min);
                    max = std::max(static_cast<u32>(inBuf[index + 1]), max);
                    min = std::min(static_cast<u32>(inBuf[index + 2]), min);
                    max = std::max(static_cast<u32>(inBuf[index + 2]), max);
                }
                ++index;
            }
            if ((indices64 - 1) & 1) {
                min = std::min(static_cast<u32>(inBuf[index]), min);
                max = std::max(static_cast<u32>(inBuf[index]), max);
            }
        }
    }

    s32 range = max - min;
    s32 uniqueCount = range + 1;

    struct IndexInfo {
        u32 bufferPos;
        u32 indexValue;
    };

    s32* occurrences;       // how many of each index value are there
    u32* occurrenceBases;   // how many indices of a value less than that value are there
    IndexInfo* swapTargets;
    if (range == -1) {
        occurrences = nullptr;
        occurrenceBases = nullptr;
    } else {
        // align up size to 8 bytes
        occurrences = reinterpret_cast<s32*>(allocator->Alloc((uniqueCount * sizeof(s32) + 7) & 0xfffffffffffffff8, 8));
        if (static_cast<u32>(range) < 0x7fffffff)
            std::memset(occurrences, 0, uniqueCount * sizeof(s32));
        occurrenceBases = reinterpret_cast<u32*>(allocator->Alloc((uniqueCount * sizeof(u32) + 7) & 0xfffffffffffffff8, 8));
    }
    if (indexCount + 1 > 2) {
        swapTargets = reinterpret_cast<IndexInfo*>(allocator->Alloc(indices * sizeof(IndexInfo), 8));
        if (indexCount > 1) {
            u32 index;
            if ((static_cast<u32>(indexCount) & 0xfffffffeu) == 2) {
                index = 0;
            } else {
                for (index = 0; index != (indices & 0xfffffffe); index += 2) {
                    occurrences[inBuf[index] - min]++;
                    occurrences[inBuf[index + 1] - min]++;
                }
            }
            if (indices & 1) {
                occurrences[inBuf[index] - min]++;
            }
        }
    } else {
        swapTargets = nullptr;
    }

    if (uniqueCount) {
        u32 index = 0;
        u32 sum = 0;
        if (range) {
            for (; index != (uniqueCount & 0xfffffffe); index += 2) {
                occurrenceBases[index] = sum;
                occurrenceBases[index + 1] = sum + occurrences[index];
                sum += occurrences[index + 1] + occurrences[index];
                occurrences[index] = 0;
                occurrences[index + 1] = 0;
            }
        }
        if (uniqueCount & 1) {
            occurrenceBases[index] = sum;
            occurrences[index] = 0;
        }
    }

    if (indexCount >= 6) {
        u32 inIndex = indices;
        u32 outIndex = indexCount;
        for (u32 i = indexCount / 6; i != 0; --i) {
            T val0 = inBuf[inIndex - 3];
            T val1 = inBuf[inIndex - 2];
            T val2 = inBuf[inIndex - 1];
            // invert mesh (clockwise -> counterclockwise or counterclockwise -> clockwise)
            outBuf[outIndex - 6] = val0;
            outBuf[outIndex - 5] = val2;
            outBuf[outIndex - 4] = val1;
            outBuf[outIndex - 3] = val0;
            outBuf[outIndex - 2] = val2;
            outBuf[outIndex - 1] = val1;

            u32 index0 = val0 - min;
            u32 index1 = val1 - min;
            u32 index2 = val2 - min;
            
            // swap other occurrences of the same index value
            s32 count = occurrences[index2];
            bool matched = false;
            if (count > 0) {
                u32 base = occurrenceBases[index2];

                u64 unkValue = (count - 1) * 8;
                for (u32 i = count; i != 0; --i) {
                    if (swapTargets[base].indexValue == index1) { // if match found, do swap
                        outBuf[outIndex - 3] = outBuf[swapTargets[base].bufferPos];
                        outBuf[swapTargets[base].bufferPos] = index0;
                        if (unkValue) { // if not index 0, advance through the list
                            swapTargets[base].bufferPos = swapTargets[base + count - 1].bufferPos;
                        }
                        occurrences[index2]--;
                        matched = true;
                        break;
                    }
                    unkValue -= 8;
                }
            }
            // if no match found, append to list
            if (!matched) {
                swapTargets[occurrences[index1] + occurrenceBases[index1]].bufferPos = outIndex - 3;
                swapTargets[occurrences[index1] + occurrenceBases[index1]].indexValue = index2;
                occurrences[index1]++;
            }

            count = occurrences[index0];
            matched = false;
            if (count > 0) {
                u32 base = occurrenceBases[index0];

                u64 unkValue = (count - 1) * 8;
                for (u32 i = count; i != 0; --i) {
                    if (swapTargets[base].indexValue == index2) {
                        outBuf[outIndex - 3] = outBuf[swapTargets[base].bufferPos];
                        outBuf[swapTargets[base].bufferPos] = index1;
                        if (unkValue) {
                            swapTargets[base].bufferPos = swapTargets[base + count - 1].bufferPos;
                        }
                        occurrences[index0]--;
                        matched = true;
                        break;
                    }
                    unkValue -= 8;
                }
            }
            if (!matched) {
                swapTargets[occurrences[index2] + occurrenceBases[index2]].bufferPos = outIndex - 3;
                swapTargets[occurrences[index2] + occurrenceBases[index2]].indexValue = index0;
                occurrences[index2]++;
            }

            count = occurrences[index1];
            matched = false;
            if (count > 0) {
                u32 base = occurrenceBases[index1];

                u64 unkValue = (count - 1) * 8;
                for (u32 i = count; i != 0; --i) {
                    if (swapTargets[base].indexValue == index0) {
                        outBuf[outIndex - 3] = outBuf[swapTargets[base].bufferPos];
                        outBuf[swapTargets[base].bufferPos] = index2;
                        if (unkValue) {
                            swapTargets[base].bufferPos = swapTargets[base + count - 1].bufferPos;
                        }
                        occurrences[index1]--;
                        matched = true;
                        break;
                    }
                    unkValue -= 8;
                }
            }
            if (!matched) {
                swapTargets[occurrences[index0] + occurrenceBases[index0]].bufferPos = outIndex - 3;
                swapTargets[occurrences[index0] + occurrenceBases[index0]].indexValue = index1;
                occurrences[index0]++;
            }
        }
    }

    if (occurrences)
        allocator->Free(occurrences);
    
    if (occurrenceBases)
        allocator->Free(occurrenceBases);
    
    if (swapTargets)
        allocator->Free(swapTargets);
}

static void PostProcessIndexBuffer(void* outBuf, size_t size0 [[maybe_unused]], void* inBuf, size_t size1 [[maybe_unused]], s32 count, IndexFormat format, s32 numVertices, StackAllocator* allocator) {
    if (format == IndexFormat::U16) {
        PostProcessIndexBuffer(reinterpret_cast<u16*>(outBuf), reinterpret_cast<u16*>(inBuf), count, numVertices, allocator);
    } else {
        PostProcessIndexBuffer(reinterpret_cast<u32*>(outBuf), reinterpret_cast<u32*>(inBuf), count, numVertices, allocator);
    }
}

void IndexStreamContext::Decompress(IndexDecompressor& decompressor, DecompContext& ctx, u32 numVertices, u64* decodeBuf, u32 verticesCopied, u32 copyCount, StackAllocator* allocator) {
    if (decodeBuf)
        std::memset(decodeBuf, 0, copyCount * 8);

    if (streamCount == 0)
        return;

    decompressor.SetContext(&ctx);
    decompressor.SetVertexCount(numVertices);
    if (verticesCopied == 0) {
        decompressor.SetBaseIndex(0);
    }

    u32 offset = indexOffset;
    u32 numBlocks = blocksRemaining;
    u32 streams = streamCount;

    DecompressIndexFunc decompFunc = GetDecompFunc(encodingType);

    u32 remaining = decompFunc(&decompressor, streamContext.stream + offset, indexFormat, indicesRemaining, baseIndex, decodeBuf, verticesCopied, copyCount);
    offset += (indicesRemaining - remaining) << static_cast<u32>(indexFormat);
    while (remaining == 0) {
        if (encodingType == EncodingType::_01) {
            u32 size = (rawCount >> 1) << static_cast<u32>(indexFormat);
            u8* ptr = streamContext.stream + offset - size;
            offset += size;
            PostProcessIndexBuffer(ptr, size << 1, ptr, size, rawCount, indexFormat, numVertices, allocator);
        }

        if (numBlocks == 1) {
            --streams;
            offset = (streamContext.alignment + offset - 1) & -streamContext.alignment;
            if (streams == 0) {
                remaining = 0;
                numBlocks = 0;
                break;
            }
            // read new index header
            u8 meshoptIdxHeader = *ctx.currentPos++;
            indexFormat = static_cast<IndexFormat>(meshoptIdxHeader & 0xf);
            encodingType = static_cast<EncodingType>(meshoptIdxHeader >> 4);
            blockCount = meshopt::decodeVByte(ctx.currentPos);
            numBlocks = blockCount;

            decompFunc = GetDecompFunc(encodingType);
        } else {
            --numBlocks;
        }
        
        rawCount = meshopt::decodeVByte(ctx.currentPos);
        baseIndex = meshopt::decodeVByte(ctx.currentPos);
        
        remaining = decompFunc(&decompressor, streamContext.stream + offset, indexFormat, rawCount, baseIndex, decodeBuf, verticesCopied, copyCount);
        offset += (rawCount - remaining) << static_cast<u32>(indexFormat);
    }

    indicesRemaining = remaining;
    blocksRemaining = numBlocks;
    streamCount = streams;
    indexOffset = offset;
}
    
} // namespace mc
