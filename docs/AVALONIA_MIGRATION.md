# Avalonia Migration Status

## Boundaries

The new .NET 10 solution separates portable document/solver logic from the desktop UI. It does not load WinForms controls or depend on the original working checkout. Shared source paths stay inside this repository. The original source snapshot is retained for behavior comparisons and gradual migration.

## Implemented

- Portable compilation of format readers/writers, editor models, and HKCL solver.
- Transactional editor session, bounded undo/redo, dirty tracking, and save/load.
- Avalonia native file dialogs, resizable editor layout, particle/bone/collider inspector, basic topology commands, and searchable documentation.
- Original-style startup overview with current and donor cloth lists, bone inspection, and explicit Editor/Back navigation. Remove, duplicate, rename, donor merge, and file swap are wired to native logic with undo and preflight guards.
- Per-file dirty state, path, platform, selection, and history survive swaps. Replacing or clearing an edited donor warns before discarding it. Save and Save as are separate from non-mutating JSON/platform exports.
- Helper overview group removal/merge/save is available; detailed helper editing is still pending. The file picker accepts helper sidecars and HKRG as well as cloth.
- Large left viewport and right inspector tabs, original middle/Shift-middle camera gestures, cursor-anchored zoom, clickable axis views, frame/reset controls, and additive/toggle particle box selection.
- DrawingContext-based wireframe viewport and HKCL simulation preview.
- Original-style editor footer with Undo/Redo, mass scaling, and HKCL cloth settings. Mass scaling preserves fixed particles and supports both HKCL and native BPHCL particle values.
- Live preview Options with wind direction/strength/gustiness, gravity multiplier, playback speed, and solver pass multiplier. Sliders and precise numeric inputs stay synchronized; Cancel restores previous options. Fixed-step scheduling preserves the requested speed without exceeding the solver's per-call time cap.
- Avalonia-hosted OpenGL/OpenGL ES model rendering with the original material GLSL, UV/tangent builder, and complete-model depth prepass.
- Assimp-based static FBX/DAE/OBJ/glTF/GLB import, hierarchy transforms, FBX meter conversion, Skia image decoding, missing-texture fallback, and mesh visibility.
- Real GPU checks covering textures, orbit, hiding, resizing, and resource clear/reload; asynchronous import failure preserves the previous model and physics data.
- Generated native format round-trips, source-preserving fixture checks, and headless rendered UI checks.

## Next Stages

1. Connect skinning and animation updates to the new GPU renderer. The current model overlay is a static reference pose and is not driven by cloth output.
2. Replace BFRES-specific DirectXTex and tiled-texture decoding dependencies. General model imports no longer use the Windows Autodesk bridge.
3. Port BFRES import and actor services, then animation playback and the timeline. Actor services are currently excluded from the portable core. Restore material presets and game shader-family/packed-texture assignment.
4. Port standalone conversion dialogs, helper/ragdoll detail editors, mirror/attachment workflows, collider box selection and multi-selection gizmos, full relationship inspectors, and collision-generation controls.
5. Restore localization, persistent settings, and feature-specific keyboard/layout parity.
6. Validate on actual Linux and macOS desktops, then package per-platform distributions with dependency/license manifests.

## Preserved Restrictions

Native serializers retain their supported-layout restrictions. A UI migration does not expand BPHCL simulation, bone creation, collider creation, or unsupported packed graph editing. New cloth drafts do not provide automatic model skinning or actor registration. The help guide preserves the full application's documentation, with a migration-status topic explaining unavailable UI features.

## Validation Scope

Local checks were run on Windows with .NET SDK 10.0.401 and Avalonia 12.1.3. They include generated Wii U HKCL, Switch HKCL, and BPHCL drafts, two existing native cloth files, solver sanity checks, and Avalonia screenshots at 1440x900 and 980x640. Headless screenshots verify rendering/layout, not native window-manager interaction or GPU model rendering.

The second migration pass also checks generated OBJ/DAE/FBX/GLB imports, embedded/external texture orientation, hierarchy and units, failed-import recovery, and model/physics isolation. Native Windows GPU checks pass with a generated texture fixture and a local Armor FBX, including orbit, mesh hiding, compact resize, and clear/reload. Captures are ignored build artifacts, not shipped game assets.

The navigation pass adds startup/overview snapshots, actual middle-button orbit and Shift-pan input, anchored zoom, box-selection/cancellation checks, and preservation of camera/model/selection across Back. Native GPU checks cover top/bottom view lighting and graphics-context recreation on returning to the editor.

The overview parity pass checks rename/duplicate/remove/merge and save/reload for generated Wii U HKCL, Switch HKCL, and BPHCL; rejected merges and canceled dialogs; lossless swaps; export isolation; and helper-group removal/undo/merge/save with local BPHYSSB and BPHHB fixtures. Original fixture files are hash-checked and never replaced by the test outputs. Editor layouts use the original side-by-side item/value arrangement and separate relationships panel.

The tuning pass covers fixed/selected/all mass scaling, invalid values, inverse-mass consistency, undo/redo, native cloth-settings round-trips, unsupported BPHCL settings, live preview rollback, dialog validation and selection retention. Clock tests verify 0.5x/1x/2x/4x playback, timer jitter, and bounded catch-up. These are preview controls, not new claims of game-accurate Havok behavior.

The CI workflow defines Windows, Ubuntu, and macOS jobs. It has not yet been executed on a remote repository. Cross-platform architecture is in place; platform certification and feature parity are still pending. On backends without a supported OpenGL context, models remain available as a reduced wireframe preview.

Renderer references: [Avalonia OpenGlControlBase](https://docs.avaloniaui.net/api/avalonia/opengl/controls/openglcontrolbase), [AssimpNetter](https://github.com/Saalvage/AssimpNetter), [Silk.NET.OpenGL](https://www.nuget.org/packages/Silk.NET.OpenGL/2.23.0).
