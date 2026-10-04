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
- Load Wii U/Switch BFRES and SBFRES reference models, including adjacent Tex1/Tex texture resources.
- Browse cloth/helper actors with Actor, choose a game resource folder, and open a read-only animated actor in the main viewport. TitleBG resources, helper bones, cloth simulation, timeline scrubbing, play/pause and Loop are supported.
- Render models through an Avalonia-hosted OpenGL/OpenGL ES surface using the original material shaders and tangent generation. Missing textures use the Toolbox fallback.
- Inspect cloth overlays alongside models, with orbit, pan, zoom, fit, physics visibility, and a Meshes visibility window. A bounded wireframe fallback is available without GPU rendering.
- Run the existing HKCL solver with wind. BPHCL simulation remains unavailable.
- Scale selected or all dynamic particle masses with matching inverse-mass updates and undo. Fixed particles are unchanged; BPHCL uses its existing native particle writer.
- Edit HKCL gravity, damping, collision tolerance, and translation/rotation motion-transfer values through Cloth settings. BPHCL cloth settings remain read-only.
- Adjust preview wind direction/strength/gustiness, gravity multiplier, speed, and solver passes through Options. These values are session-local and never written into the cloth file. Close retains them; Cancel or the title-bar close restores the previous options, not the already-simulated pose.
- Read the searchable offline cloth guide. Settings currently provide session-local theme and HKCL save-platform choices.

The application starts on the current-file/donor overview, with the bone list on the right. Open a physics file, select a cloth, then choose **Editor** or double-click the cloth. **Back** returns to the overview without discarding edits, the reference model, or the camera position; it stops a running preview. Remove/Duplicate are below the current list; Rename is in its context menu (F2). Merge is below the donor list (M). Unsupported merges display the native preflight reason without changing either document. Swapped donor edits are protected by unsaved-change prompts.

**Save** writes the current file, prompting for a path on a new draft. **Export** offers Save as, JSON, and Wii U/Switch HKCL copies. Export copies do not replace the open document or mark its edits saved. Ctrl/Command+S saves; Shift adds Save as. Ctrl/Command+Z undoes, and Ctrl/Command+Shift+Z or Ctrl/Command+Y redoes.

The editor keeps the large viewport on the left and the cloth selector plus particle/bone/collider tabs on the right. The item list sits beside its values, with a resizable relationships panel below. Add Particle/Bone/Collider sits above the viewport; Particles follow bones sits above the tabs. Middle-drag orbits, Shift+middle-drag pans, and the wheel zooms around the pointer. Alt+left-drag and Alt+Shift+left-drag provide alternatives without a middle button. Left-click selects; left-drag box-selects particles. Shift adds and Ctrl/Command toggles selection. Escape cancels a selection drag.

The viewport has frame/reset/zoom buttons and clickable axis views. Home frames all, period frames selected particles, and numpad 1/3/7 selects front/right/top (Ctrl selects the opposite side). Right-click opens camera commands. F opens relationship creation for two particles or creates a triangle for three. Numeric inspector edits use Apply; Fixed commits immediately.

Undo/Redo, Mass scale, and Cloth settings sit in the editor footer, as in the original layout. Select dynamic particles to limit mass scaling, or clear the selection to scale all dynamic particles. Native edits are disabled during simulation; preview Options remain live. Preview speed uses fixed 60 Hz solver steps, with bounded catch-up after a UI stall.

Blank drafts are not automatically game-ready: model bindings and actor registration still need authoring. Unsupported native layouts retain the existing explicit safety restrictions.

## Remaining Migration

Helper and ragdoll editing UI, material presets/editing, collision-generation UI, attachment tools, full gizmos/box selection, relationship-value inspection, and localization are not yet ported. Separately imported editor models remain static references; actor mode has animation and cloth-driven model deformation. The preserved guide describes the full tool; some workflows are not available in this preview.

The Windows Autodesk FBX/DirectXTex bridges are not used by the new app. General model imports use AssimpNetter's platform-native runtimes; image textures use Skia. BFRES texture decoding uses managed GX2/Tegra addressing and BCnEncoder; unsupported texture formats are reported. BFRES material slots use the original actor loader's shader-family/packed-texture assignments; material preset editing is still pending. Model visibility is session-local and never changes a physics save. See [migration status](docs/AVALONIA_MIGRATION.md).

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
