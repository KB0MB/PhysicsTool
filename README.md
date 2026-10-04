# PhysicsTool Avalonia

A cross-platform migration of PhysicsTool using Avalonia 12.1.3 and .NET 10, targeting Windows, Linux, and macOS.

**Migration preview, not feature-complete.** This is an independent repository. The original WinForms source is preserved in `PhysicsTool/` as the migration baseline; the Avalonia application does not reference its Windows UI project. The portable core reuses the serializers, editor models, and cloth solver from this repository.

## Build and Run

Install the .NET 10 SDK, then run:

```sh
dotnet build PhysicsTool.Avalonia.slnx -c Release
dotnet run --project src/PhysicsTool.Desktop -c Release
```

An optional first argument opens a cloth file:

```sh
dotnet run --project src/PhysicsTool.Desktop -c Release -- /path/to/cloth.hkcl
```

On Windows, the framework-dependent executable is `src/PhysicsTool.Desktop/bin/Release/net10.0/PhysicsTool.Desktop.exe`. Keep its output folder together; it needs the .NET 10 runtime. Linux and macOS also need the normal Avalonia desktop native dependencies. This is not yet a packaged, self-contained release.

Use `PhysicsTool.Avalonia.slnx`, not the preserved Windows-only `PhysicsTool.sln`.

## Available in This Preview

- Open and save Wii U/Switch HKCL and TotK BPHCL through the existing native format logic.
- Rename, duplicate, and remove cloths; merge complete donor cloth packages with native preflight checks; swap current/donor files without losing either file's dirty state or undo history.
- Open helper sidecars for overview inspection, supported group removal/merge, and native saving. HKRG files can be inspected and saved without changing their detected platform.
- Create blank cloth drafts, edit particles, bone positions/scales, and supported collider fields.
- Add/remove supported particles, relationships, and triangles, with atomic edits and undo/redo.
- Load static FBX, DAE, OBJ, glTF, and GLB reference models, including supported embedded/external textures. FBX declared units are converted to meters.
- Render models through an Avalonia-hosted OpenGL/OpenGL ES surface using the original material shaders and tangent generation. Missing textures use the Toolbox fallback.
- Inspect cloth overlays alongside models, with orbit, pan, zoom, fit, physics visibility, and a Meshes visibility window. A bounded wireframe fallback is available without GPU rendering.
- Run the existing HKCL solver with wind. BPHCL simulation remains unavailable.
- Read the searchable offline cloth guide. Settings currently provide session-local theme and HKCL save-platform choices.

The application starts on the current-file/donor overview, with the bone list on the right. Open a physics file, select a cloth, then choose **Editor** or double-click the cloth. **Back** returns to the overview without discarding edits, the reference model, or the camera position; it stops a running preview. Remove/Duplicate are below the current list; Rename is in its context menu (F2). Merge is below the donor list (M). Unsupported merges display the native preflight reason without changing either document. Swapped donor edits are protected by unsaved-change prompts.

**Save** writes the current file, prompting for a path on a new draft. **Export** offers Save as, JSON, and Wii U/Switch HKCL copies. Export copies do not replace the open document or mark its edits saved. Ctrl/Command+S saves; Shift adds Save as. Ctrl/Command+Z undoes, and Ctrl/Command+Shift+Z or Ctrl/Command+Y redoes.

The editor keeps the large viewport on the left and the cloth selector plus particle/bone/collider tabs on the right. The item list sits beside its values, with a resizable relationships panel below. Add Particle/Bone/Collider sits above the viewport; Particles follow bones sits above the tabs. Middle-drag orbits, Shift+middle-drag pans, and the wheel zooms around the pointer. Alt+left-drag and Alt+Shift+left-drag provide alternatives without a middle button. Left-click selects; left-drag box-selects particles. Shift adds and Ctrl/Command toggles selection. Escape cancels a selection drag.

The viewport has frame/reset/zoom buttons and clickable axis views. Home frames all, period frames selected particles, and numpad 1/3/7 selects front/right/top (Ctrl selects the opposite side). Right-click opens camera commands. F opens relationship creation for two particles or creates a triangle for three. Numeric inspector edits use Apply; Fixed commits immediately.

Blank drafts are not automatically game-ready: model bindings and actor registration still need authoring. Unsupported native layouts retain the existing explicit safety restrictions.

## Remaining Migration

BFRES import/texture decoding, actor browsing, animation/timeline, helper and ragdoll editing UI, material presets/editing, collision-generation UI, attachment tools, full gizmos/box selection, relationship-value inspection, and localization are not yet ported. Imported models currently show a static reference pose; cloth simulation does not deform their meshes. The preserved guide describes the full tool; some workflows are not available in this preview.

The Windows Autodesk FBX/DirectXTex bridges are not used by the new app. General model imports use AssimpNetter's platform-native runtimes; image textures use Skia. BFRES-specific decoding still needs migration. Albedo and normal slots load from imported materials; game-specific presets, packed SPM interpretation, and shader-family assignment are not yet restored. Model visibility is session-local and never changes a physics save. See [migration status](docs/AVALONIA_MIGRATION.md).

## Verification

```sh
dotnet run --project tests/PhysicsTool.Checks -c Release -- artifacts/checks
```

Real graphics-context checks (requires a desktop/display and supported GPU backend):

```sh
dotnet run --project tests/PhysicsTool.Checks -c Release -- --gpu artifacts/checks/gpu
```

An optional final model path runs the GPU checks on a local model. These checks open a short-lived test window, capture framebuffer pixels, verify orbit/visibility/resize/clear/reload, and close automatically. The ordinary suite also checks generated model imports, hierarchy, handedness, units, embedded textures, UV orientation, missing textures, and physics-document isolation.

Optional additional arguments are local cloth fixtures. Tests save copies in the output directory and verify that fixture sources remain unchanged. Generated checks cover all three draft formats, topology/save/reload, undo/redo, atomic failures, finite HKCL solver output, simulation button recovery, documentation, and rendered Avalonia layouts at two sizes.

These checks have passed locally on Windows, including two native cloth fixtures, generated model fixtures, and a real Armor FBX GPU preview. A Windows/Linux/macOS CI matrix is included but has not yet run remotely; its ordinary suite does not certify GPU behavior. Linux/macOS runtime behavior is not yet verified. The inherited core still produces nullable warnings.

## Repository Layout

- `src/PhysicsTool.Core`: portable core and editor session.
- `src/PhysicsTool.Desktop`: Avalonia interface, GPU model viewport, and physics overlays.
- `tests/PhysicsTool.Checks`: generated-format and headless UI checks.
- `PhysicsTool`: preserved original source and shared implementations.
- `docs/WinForms-README.md`: original application documentation.

No game dumps or executables are included. Build/test outputs are ignored. No GitHub remote is configured for this new repository yet.

Framework references: [Avalonia package](https://www.nuget.org/packages/Avalonia/12.1.3), [native storage providers](https://docs.avaloniaui.net/docs/services/storage/storage-provider), and [headless testing](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform).
