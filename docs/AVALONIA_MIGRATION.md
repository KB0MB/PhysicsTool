# Avalonia Migration Status

## Boundaries

The new .NET 10 solution separates portable document/solver logic from the desktop UI. It does not load WinForms controls or depend on the original working checkout. Shared source paths stay inside this repository. The original source snapshot is retained for behavior comparisons and gradual migration.

## Implemented

- Portable compilation of format readers/writers, editor models, and HKCL solver.
- Transactional editor session, bounded undo/redo, dirty tracking, and save/load.
- Avalonia native file dialogs, resizable editor layout, particle/bone/collider inspector, basic topology commands, and searchable documentation.
- DrawingContext-based wireframe viewport and HKCL simulation preview.
- Generated native format round-trips, source-preserving fixture checks, and headless rendered UI checks.

## Next Stages

1. Introduce a portable rendering backend for the existing textured/skinned visual-model data. The current viewport is not the original OpenGL material renderer.
2. Replace or isolate Windows-specific FBX, DirectXTex, and texture-decoding dependencies. Assimp-related core code compiles, but native deployment and model UI are not validated.
3. Port BFRES/model import and actor services, then animation playback and the timeline. Actor services are currently excluded from the portable core.
4. Port helper/ragdoll editors, attachment workflows, multi-selection gizmos, full relationship inspectors, and collision-generation controls.
5. Restore localization, persistent settings, and feature-specific keyboard/layout parity.
6. Validate on actual Linux and macOS desktops, then package per-platform distributions with dependency/license manifests.

## Preserved Restrictions

Native serializers retain their supported-layout restrictions. A UI migration does not expand BPHCL simulation, bone creation, collider creation, or unsupported packed graph editing. New cloth drafts do not provide automatic model skinning or actor registration. The help guide preserves the full application's documentation, with a migration-status topic explaining unavailable UI features.

## Validation Scope

Local checks were run on Windows with .NET SDK 10.0.401 and Avalonia 12.1.3. They include generated Wii U HKCL, Switch HKCL, and BPHCL drafts, two existing native cloth files, solver sanity checks, and Avalonia screenshots at 1440x900 and 980x640. Headless screenshots verify rendering/layout, not native window-manager interaction or GPU model rendering.

The CI workflow defines Windows, Ubuntu, and macOS jobs. It has not yet been executed on a remote repository. Cross-platform architecture is in place; platform certification and feature parity are still pending.
