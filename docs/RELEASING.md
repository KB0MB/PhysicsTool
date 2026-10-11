# Building and Releasing PhysicsTool

## Build

Use .NET 10 and the primary PhysicsTool.slnx solution:

```sh
dotnet build PhysicsTool.slnx -c Release
dotnet run --project tests/PhysicsTool.Checks -c Release -- artifacts/checks
```

Build the standalone MeshCodec helper with CMake for the target OS/CPU, installing it to PhysicsTool/ThirdParty/MeshCodec/runtimes/<rid>. Alternatively, scripts/build-meshcodec-cross.ps1 supports Zig 0.13.0 cross-builds. The portable C++ fixes cover libc++ includes, macOS integer types, ARM64 FPCR access and Zstandard assembly selection. Linux cross-builds link the small helper statically against musl; the desktop app itself still uses glibc.

```powershell
./scripts/collect-licenses.ps1
./scripts/package-release.ps1 -Version 2.0
./scripts/verify-release.ps1 -Version 2.0
```

The packager refuses to omit a required helper. It creates self-contained Windows/Linux/macOS x64 and ARM64 ZIPs, a separate source ZIP and SHA256SUMS.txt under artifacts/releases/2.0. It preserves Unix executable permissions and excludes settings, debug symbols, game fixtures and the legacy proprietary FBX bridge. The macOS app bundle is not signed or notarized.

The manual release workflow builds/tests on matching GitHub runners and uploads artifacts only. It does not publish a GitHub release.

## Settings

Writable Windows folders keep settings.json and actor-folders.json beside the application. Protected Windows folders, Linux and macOS use the .NET LocalApplicationData directory under PhysicsTool. Put an empty portable.txt beside the executable to opt into portable storage on other platforms; the directory must be writable. Existing adjacent settings are imported only when a per-user file does not already exist.

## Platform Checks

Before marking a platform supported, test opening/saving/converting physics, actor scanning, compressed TotK models, texture decoding, animation, simulation, collision import, GPU rendering, file dialogs, input shortcuts, language changes and settings persistence on that OS/architecture.

Windows x64 has local regression coverage. Linux x64 can additionally be checked with WSL; that does not certify every Linux desktop or GPU. ARM64 and macOS cross-builds are provided for testing and still need native UI/GPU validation. The CI matrix is configured, not a claim that a remote run has passed.

The 2.0 preparation run passed Linux x64 headless editing/conversion/collision checks under Ubuntu 26.04 and textured OpenGL viewport checks under WSLg with Mesa llvmpipe. EGL is tried before GLX on Linux. llvmpipe is software rendering, not hardware-GPU certification. Optional local game fixtures were not present in that Linux suite.

Linux prerequisites include ICU, libX11, libICE, libSM, fontconfig and an OpenGL driver. On Ubuntu 26.04 the runtime libraries include libicu78, libx11-6, libice6, libsm6, libfontconfig1 and libgl1; other distributions/versions use their corresponding packages. Japanese text requires a Japanese-capable system font (for example Noto CJK) when the system does not already provide one.

macOS distribution requires signing/notarization for a normal trusted download experience. Windows signing likewise requires a publisher certificate. Neither credentials nor a signing service are configured in this repository. Do not tell users to disable security software.

## Licenses and Source

The uncertain Toolbox MeshCodec wrapper has been removed. The standalone helper now calls dt-12345/MeshCodec's public DecompressMC API through a PhysicsTool GPL-2.0-only stdin/stdout adapter. The helper source archive records the upstream revision and local modifications in LOCAL_CHANGES.md. Keep this corresponding source and all notices with the release; this resolves the identified wrapper-provenance issue, not a blanket legal certification of the project.

Avalonia's open-source desktop framework is MIT-licensed. It does not require buying a commercial Avalonia license to distribute this application. Its copyright/permission notice must accompany distribution; it is included under Licenses.

PhysicsTool retains its existing GPL-3.0 license. It also uses AGPL-3.0-or-later Tkmm.BymlLibrary; its terms apply to the combined work as described in GPLv3 section 13, not only to a standalone DLL. Publish the matching source ZIP alongside binary ZIPs and keep the dependency notices. The exact unmodified BYML source revision is bundled under Licenses. MeshCodec is a separate GPL-2.0 executable with its corresponding source archive. GPL-licensed Toolbox/Ice-Hail adaptations are included in the main source archive.

Licenses/NuGet-inventory.json records restored package versions, upstream repositories and available commit IDs. Package notices include Avalonia, Inter's SIL Open Font License, Lucide, Skia/HarfBuzz, Assimp, .NET and the bundled readers. Cross-built helpers also retain Zig/LLVM, musl and MinGW notices. Native helper source archives retain their upstream notices and local modifications.

This inventory is not a legal certification. Check the provenance and redistribution terms of any dependency added later, especially copied native wrappers and assets. Do not bundle game dumps, Nintendo/Havok SDKs, models, textures, extracted actors or personal settings with a release.
