// SPDX-License-Identifier: GPL-2.0-only
// PhysicsTool stdin/stdout adapter for dt-12345/MeshCodec's public DecompressMC API.
#include "mc_MeshCodec.h"
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <memory>
#include <vector>
#ifdef _WIN32
#include <fcntl.h>
#include <io.h>
#endif

int main()
{
#ifdef _WIN32
    _setmode(_fileno(stdin), _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);
#endif
    try
    {
        std::vector<std::uint8_t> input;
        std::uint8_t chunk[65536];
        while (auto size = std::fread(chunk, 1, sizeof(chunk), stdin))
        {
            if (input.size() + size > 256 * 1024 * 1024) return 2;
            input.insert(input.end(), chunk, chunk + size);
        }
        if (std::ferror(stdin) || input.size() < sizeof(mc::ResMeshCodecPackageHeader)) return 2;
        mc::ResMeshCodecPackageHeader header{};
        std::memcpy(&header, input.data(), sizeof(header));
        if (header.magic != mc::ResMeshCodecPackageHeader::cMagic || header.versionMajor != 0 || header.versionMinor > 1) return 2;
        const std::uint64_t size = std::uint64_t(header.flags >> 5) << (header.flags & 15);
        if (size < 32 || size > 512 * 1024 * 1024) return 2;
        std::vector<std::uint8_t> output(size);
        // Upstream's CLI reserves this workspace; leave it uninitialized like malloc.
        constexpr std::size_t workspaceSize = 0x10000000;
        auto workspace = std::unique_ptr<std::uint8_t[]>(new std::uint8_t[workspaceSize]);
        if (!mc::DecompressMC(output.data(), output.size(), input.data(), input.size(), workspace.get(), workspaceSize)) return 3;
        return std::fwrite(output.data(), 1, output.size(), stdout) == output.size() ? 0 : 4;
    }
    catch (...) { return 5; }
}
