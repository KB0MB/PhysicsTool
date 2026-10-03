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
- Create blank cloth drafts, edit particles, bone positions/scales, and supported collider fields.
- Add/remove supported particles, relationships, and triangles, with atomic edits and undo/redo.
- Inspect cloth in a wireframe 3D viewport, with orbit, pan, zoom, and fit controls.
- Run the existing HKCL solver with wind. BPHCL simulation remains unavailable.
- Read the searchable offline cloth guide. Settings currently provide session-local theme and HKCL save-platform choices.

Right-drag orbits, middle-drag pans, and the wheel zooms. Ctrl-click adds particles to the selection. F opens relationship creation for two particles or creates a triangle for three. Numeric inspector edits use Apply; Fixed commits immediately.

Blank drafts are not automatically game-ready: model bindings and actor registration still need authoring. Unsupported native layouts retain the existing explicit safety restrictions.

## Remaining Migration

Textured FBX/BFRES rendering, actor browsing, animation/timeline, helper and ragdoll editing UI, materials, collision-generation UI, attachment tools, full gizmos/box selection, relationship-value inspection, and localization are not yet ported. The preserved guide describes the full tool; some workflows are not available in this preview.

The Windows FBX/texture bridges are not used by the new app. Portable replacements and native dependency packaging need validation before exposing those workflows. See [migration status](docs/AVALONIA_MIGRATION.md).

## Verification

```sh
dotnet run --project tests/PhysicsTool.Checks -c Release -- artifacts/checks
```

Optional additional arguments are local cloth fixtures. Tests save copies in the output directory and verify that fixture sources remain unchanged. Generated checks cover all three draft formats, topology/save/reload, undo/redo, atomic failures, finite HKCL solver output, simulation button recovery, documentation, and rendered Avalonia layouts at two sizes.

These checks have passed locally on Windows, including two native cloth fixtures. A Windows/Linux/macOS CI matrix is included but has not yet run remotely. Linux/macOS runtime behavior is not yet verified. The inherited core still produces nullable warnings.

## Repository Layout

- `src/PhysicsTool.Core`: portable core and editor session.
- `src/PhysicsTool.Desktop`: Avalonia interface and wireframe viewport.
- `tests/PhysicsTool.Checks`: generated-format and headless UI checks.
- `PhysicsTool`: preserved original source and shared implementations.
- `docs/WinForms-README.md`: original application documentation.

No game dumps or executables are included. Build/test outputs are ignored. No GitHub remote is configured for this new repository yet.

Framework references: [Avalonia package](https://www.nuget.org/packages/Avalonia/12.1.3), [native storage providers](https://docs.avaloniaui.net/docs/services/storage/storage-provider), and [headless testing](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform).
