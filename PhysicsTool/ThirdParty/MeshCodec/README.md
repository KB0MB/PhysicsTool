# MeshCodec Decoder Helper

PhysicsTool 2.0 portability changes: explicit standard-library includes for
Clang/libc++, consistent size types on macOS, ARM64 FPCR register handling,
and ZSTD_DISABLE_ASM for builds that do not compile the upstream assembly.
scripts/build-meshcodec-cross.ps1 in the main repository reproduces Zig 0.13.0
cross-builds. The Linux helper is statically linked; the desktop app is not.

This separate executable calls dt-12345/MeshCodec's DecompressMC API directly.
Upstream: https://github.com/dt-12345/MeshCodec
Reference revision: b3600954872dbc2893a1bcc9e851c1e364f303ab.
The former Toolbox native wrapper is not used or distributed. Native/main.cpp
is the PhysicsTool GPL-2.0-only stdin/stdout adapter; LOCAL_CHANGES.md records
local changes and their licensing.
The original upstream README and GPL-2.0 license are in `Upstream/`.
Zstandard's license and its source headers, meshoptimizer-derived notices, and
the float16 conversion notice are retained alongside the source.

Changes: portable includes and builds, a bounded binary stdin/stdout CLI,
package bounds checks, and source-filename-only allocator diagnostics.
No SDK or game code is included. PhysicsTool
communicates with the standalone executable through pipes; it does not load the
decoder as a library. A crash, timeout or invalid output rejects the model load.

Build on the destination platform with a C++20 compiler and CMake:

```sh
cmake -S PhysicsTool/ThirdParty/MeshCodec -B artifacts/meshcodec -DCMAKE_BUILD_TYPE=Release
cmake --build artifacts/meshcodec --config Release
cmake --install artifacts/meshcodec --config Release --prefix PhysicsTool/ThirdParty/MeshCodec/runtimes/<rid>
```

Use `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64` or `osx-arm64`
for the runtime directory. Build the .NET solution afterwards to copy the helper
beside the application. Windows x64 and Linux x64 have local decoder regression
coverage; ARM64 and macOS binaries are cross-built and need native validation. Keep the helper's executable
permission on Linux/macOS. The helper and corresponding source/licenses must be
distributed together; publish output includes a source archive.

When building from the standalone MeshCodec-source.zip, its extracted root
contains CMakeLists.txt: use `cmake -S . -B build -DCMAKE_BUILD_TYPE=Release`,
then `cmake --build build --config Release`.
