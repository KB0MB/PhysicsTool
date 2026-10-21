# MeshCodec Source and Local Changes

Updated 2026-10-20.

The decoder is dt-12345/MeshCodec, licensed under GPL-2.0 as stated in its
upstream README. Reference revision: b3600954872dbc2893a1bcc9e851c1e364f303ab
at https://github.com/dt-12345/MeshCodec. The upstream GPL license and dependency
notices are retained under Upstream. This helper is not covered by the parent
PhysicsTool application's GPL-3.0 license in place of those terms.

Native/main.cpp, CMakeLists.txt, and the PhysicsTool changes described below
are provided under GPL-2.0-only. Native/main.cpp calls the public DecompressMC
API, following the usage demonstrated by upstream's tests/src/main.cpp.
The old SwitchToolbox.MeshCodecNative.cpp wrapper has been removed, not
renamed or relicensed. PhysicsTool exchanges compressed input and decoded
output through the helper's standard streams.

Local changes to upstream files:

- mc_MeshCodec.cpp: package length, output-size, FRES/FMSH header, alignment,
  mesh-output and workspace checks; automatic Zstandard context cleanup.
- mc_Codec.cpp: partial lossless blocks use the remaining buffer size;
  compressed-size varints and input cursor advancement are corrected.
- mc_Float.cpp: compiler/CPU-specific floating-point control handling.
- mc_BitStream.h: standard C++20 bit counts on MSVC ARM64 instead of x86 intrinsics.
- mc_StackAllocator.cpp: portable size comparison and filename-only diagnostics.
- mc_Types.h, mc_AttributeCodec.cpp, mc_Codec.cpp, mc_IndexDecompressor.cpp,
  mc_IndexStreamContext.cpp and mc_MeshCodec.cpp: explicit standard includes.

The build uses bundled Zstandard without its optional assembly and retains
the float16 and meshoptimizer-derived notices. The parent release also
includes cross-compiler runtime notices. No Nintendo SDK or game assets are
included. Build instructions are in README.md; binaries must be accompanied
by the corresponding modified helper source and license notices.
