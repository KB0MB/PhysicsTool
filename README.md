# PhysicsTool

PhysicsTool is a Windows editor for Breath of the Wild HKCL and Tears of the Kingdom BPHCL cloth physics. It is built around complete cloth packages: simulation data, skeletons, particles, constraints, colliders, and the metadata required to keep those pieces connected.

> **Open beta:** This project is actively being tested against real game files. Always back up your files and test changes in-game. Please report issues, failed merges, and reproducible crashes on Discord: **`kb0mbyolo`**.

## Current Features

- Open, inspect, save, rename, remove, and merge BotW `.hkcl` cloth entries.
- Open and save TotK `.bphcl` files with the native C# BPHCL reader/writer.
- Merge a complete BPHCL cloth package into another BPHCL: cloth data, paired skeleton, particles, constraints, referenced colliders, AAMP registration, object references, and required Havok TYPE layouts.
- Physically remove BPHCL cloth data that is no longer used by the remaining file.
- Inspect BPHHB helper-bone files.
- Use the OpenGL editor viewport to inspect bones, particles, colliders, and particle relationships.
- Edit HKCL particles, bones, and colliders directly in the editor, with undo/redo and viewport transforms.

## Important Limits

- BPHCL merging is functional but remains beta. A source can use an unusual Havok reflected type that needs additional compatibility work.
- BPHCL direct editing and creating new BPHCL physics are not yet validated to the same level as complete-cloth merging.
- BPHCL to HKCL conversion is experimental and should not be relied on for release work.
- A successful open/save check is useful, but an in-game test is the final validation.

## Build and Run

Requires the .NET 9 SDK.

```powershell
dotnet build -c Release
```

Run:

```text
PhysicsTool\bin\Release\PhysicsTool.exe
```

For Visual Studio, open [PhysicsTool.sln](PhysicsTool.sln) and press `F5`.

## Merge Workflow

1. Open the target physics file with **Open Physics**.
2. Open the donor file with **Open Reference**.
3. Select the donor cloth entry.
4. Choose **Merge selected** or press `M`.
5. Review the preflight dialog, save the result, and test it in-game.

For BPHCL, the preflight identifies whether the import can use existing Havok TYPE layouts or must add source-only layouts. The latter is supported, but should be treated as a beta path.

## Shortcuts

### File and Merge Mode

| Shortcut | Action |
| --- | --- |
| `Ctrl+S` | Save |
| `Ctrl+Shift+S` | Save As |
| `M` | Merge the selected reference cloth |
| `Delete` | Remove the selected cloth in merge mode |

### Editor Viewport

| Shortcut / Control | Action |
| --- | --- |
| `Ctrl+Z` | Undo an editor change |
| `Ctrl+Shift+Z` | Redo an editor change |
| `G` | Move selected particles |
| `R` | Rotate selected particles |
| `S` | Scale selected particles |
| `X`, `Y`, `Z` during a transform | Constrain the active transform to an axis |
| `F` | Link two or three selected particles |
| Left click / drag | Select or box-select viewport items |
| `Shift` + selection | Add to the current selection |
| Middle mouse | Orbit the camera |
| `Shift` + middle mouse | Pan the camera |
| Mouse wheel | Zoom |
| Right click in viewport | Mirror and other context actions |

## Project Layout

```text
PhysicsTool/
  UI/                 Main Windows Forms interface
  Editor/             Viewport and editor-facing data models
  Formats/Hkcl/       HKCL reader, writer, and authoring support
  Formats/Bphcl/      Native BPHCL reader, writer, merge, TYPE, and AAMP code
  Formats/Bphhb/      BPHHB helper-bone inspection support
```

## Credits

- [banan039 / totkbits](https://github.com/banan039/totkbits) for foundational BPHCL reverse-engineering work.
- [VelouriasMoon / HKCLTool](https://github.com/VelouriasMoon/HKCLTool) for the original HKCLTool project this work grew from.
- [HKX2](https://github.com/krenyy/HKX2) and [Json.NET](https://www.newtonsoft.com/json) for the supporting libraries.

The original GUI notes are preserved in `Docs/Archive` for reference.
