# PhysicsTool 2.0

PhysicsTool has a new interface, built with Avalonia, while keeping the familiar file overview, editor and viewport workflow.

## Highlights

- Cloth editing for BotW and TotK, with particle and constraint tools, bone attachment, multi-selection, undo/redo and guarded whole-cloth mirroring.
- Improved HKCL/BPHCL conversion and cross-format donor merging, including better preservation of mass, constraints and native graph references.
- Support/helper bone editing with response curves, connections, individual deletion and model evaluation. HKRG ragdoll editing is now in the same interface.
- BotW and TotK actor browsing, BFRES models and animations, texture loading, shader previews, wind and cloth simulation.
- A collision workspace for BotW collisions and TotK polytopes, with mesh import, a 3D preview and geometry export.
- A clearer File menu, searchable editing documentation, light/dark themes and English, French, Spanish and Japanese interface options.
- PhysicsTool is now the main application name. The old WinForms application is preserved separately, not bundled into these releases.

## Downloads

Each ZIP contains the .NET runtime and its matching native model decoder. No separate .NET installation is needed. Extract the whole folder before running it.

- Windows: win-x64 for most PCs; win-arm64 for Windows on ARM.
- Linux: linux-x64 or linux-arm64, on a supported glibc-based desktop distribution. ICU, X11/XWayland (including libICE and libSM), fontconfig and functioning OpenGL drivers are needed. These are not Alpine/musl application builds.
- macOS 15 or later: osx-arm64 for Apple silicon; osx-x64 for Intel. Open PhysicsTool.app.

All four languages are included in every package. The in-app documentation and constraint descriptions are fully translated into French, Spanish and Japanese. Some diagnostics remain in English.

## Before Publishing

These are unsigned release candidates. macOS bundles are not notarized. Windows x64 is the primary tested platform; Linux and ARM/macOS packages need the platform checks described in BUILD_AND_LICENSES.md. A successful cross-build is not a native UI/GPU test.

Simulation and shaders are previews, not an exact replacement for the games' Havok runtime or rendering. Unsupported editing layouts are deliberately blocked. Retargeted TotK animation sets and some material effects remain unsupported. Keep an original copy of your files as a backup!

No game assets, executable dumps, SDKs or personal resource paths are included.
