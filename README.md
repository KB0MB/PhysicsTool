# PhysicsTool

PhysicsTool is a Windows editor for Nintendo Havok physics files used by *The Legend of Zelda: Breath of the Wild* and *Tears of the Kingdom*.

It works with complete physics graphs rather than isolated values: cloth simulations, skeletons, particles, constraints, colliders, operators, buffers, and the format-specific metadata that connects them.

> **Open beta:** PhysicsTool is actively being tested against real game files. Back up every file, keep a known-good original, and test changes in-game. Please report issues, failed merges, or reproducible crashes on Discord: **`kb0mbyolo`**.

## Supported Files

| Format | Game / purpose | Current support |
| --- | --- | --- |
| `.hkcl` | BotW Havok cloth | Open, save, merge, remove, duplicate, rename, inspect, directly edit, and convert to BPHCL. |
| `.bphcl` | TotK Phive/Havok cloth | Open, save, merge, remove, duplicate, rename, inspect, directly edit, and convert to HKCL. |
| `.bphhb` | TotK helper-bone configuration | Open, save, edit, merge independent driver groups, add, duplicate, rename, mirror, reorder, and convert to BotW `.bphyssb`. This is not a full actor skeleton. |
| `.bphyssb` | BotW support-bone configuration | Open, inspect, save, edit, merge independent driver groups, and convert to TotK `.bphhb`. |
| `.hkrg` | BotW Havok ragdoll | Open, save, inspect, and edit model bones, ragdoll bones, rigid bodies, and supported constraints. |
| `.json` | PhysicsTool diagnostic export | Open and export for inspection. It is not the recommended authoring workflow. |

PhysicsTool can also export a BotW `.bphysics` AAMP sidecar for an HKCL file. That sidecar can reference a `.bphyssb` support-bone path.

## What You Can Do

### HKCL Cloth

- Merge complete cloth units between compatible HKCL files.
- Remove, duplicate, and rename cloths. Duplicating a cloth also duplicates its owned colliders.
- Directly edit particles, cloth bones, and colliders.
- Add particles, bones, capsule/sphere/plane colliders, and particle links.
- Inspect and edit supported particle-relationship values, including rest lengths, standard stiffness, bend ranges, bend stiffness, stretch stiffness, and local range data.
- Select a particle's active cloth colliders through **Colliders** instead of manually constructing its collision bitmask.
- Move, rotate, scale, mirror, copy, paste, and X-flipped paste viewport items.
- Run the built-in **preview** simulation with optional wind. It never writes simulated motion back into the file.
- Export Wii U or Switch HKCL, JSON, or a BotW BPHYSICS sidecar.

### BPHCL Cloth

- Merge complete native BPHCL cloth packages, including their paired skeleton, particles, constraints, referenced colliders, AAMP registration, object references, and required TYPE metadata.
- Remove unused cloth packages and compact their native data when the file layout permits it.
- Directly edit supported particles, bones, colliders, triangles, and numeric constraint values.
- Duplicate, rename, and mirror cloth geometry. Mirroring follows the BPHCL native payloads, not HKCL serialization.
- Use the same viewport editing tools as HKCL for supported BPHCL data.

### Support and Helper Bones

PhysicsTool calls each independent connected motion graph a **driver group**. A driver group contains the driver bone or bones that provide motion input, the curves and outputs that transform that motion, and the driven bones affected by the result. A group can have multiple drivers when their outputs converge on the same driven graph.

- Browse BPHHB and BPHYSSB files as indexed driver groups rather than one indivisible configuration.
- View each group's motion-input bones and affected bones in the right pane.
- Double-click a group, press `Enter`, or use **Editor** to open the existing bone editor scoped to that group.
- Open a same-format donor and merge one selected driver group. PhysicsTool remaps bone references, curves, outputs, poses, and native table indices into the target graph.
- Use **Create Support** on an affected bone to clone its known-good driven setup onto a new or existing target bone name. This retains the current driver relationship instead of creating an empty, disconnected record.
- Edit supported bone names and base transforms, duplicate records, and reorder records.
- Use **Mirror Group** to reflect every input and affected base pose in the selected driver group across X, including its aim/up directions, and swap recognized left/right name tokens.
- Convert BPHHB and BPHYSSB in either direction while preserving the graph structure represented by both formats.

Driver-group merging is currently same-format: BPHHB into BPHHB or BPHYSSB into BPHYSSB. Convert a donor first when moving a group between games.

### Visual FBX Reference Models

- Use **Load FBX** in any direct editor to place a visual-only model behind cloth, skeleton, collider, ragdoll, or support/helper-bone data.
- The loaded model remains available while switching physics tabs and driver groups.
- Autodesk FBX SDK bakes node and geometric transforms into the displayed geometry, converts the file's declared units to meters, and normalizes its axis system for the Havok view.
- Albedo, SPM, normal, and ambient-occlusion textures can come from embedded FBX media or external image files. For stale absolute paths, PhysicsTool also looks for the same texture filename beside the FBX and in its subfolders.
- Meshes without a readable texture use a neutral checker material and still load normally.
- **Model** controls visibility, textures, wireframe display, opacity, X mirroring, uniform scale, XYZ offset, and XYZ rotation.
- **Materials** edits each mesh independently. Every texture slot can use its own FBX UV set; shader family, SPM metal and smoothness channels, map strengths, AO, normal strength, auxiliary maps, and tint can also be adjusted. The preview decodes color and packed material maps in the same color spaces used by the reference Blender shaders and detects hard alpha cutouts automatically.
- **Import .anim** loads a Maya ASCII `.anim` file into memory for visual playback on the FBX skeleton. **Play/Pause** loops the source frame range at its declared frame rate, and **Reset Pose** restores the original FBX pose. Animation remains visual-only.
- In a support/helper-bone driver-group editor, the preview bones snap to the animated FBX skeleton when every driver and affected bone in that group can be matched by name. Partial matches are rejected so the graph cannot be shown in a misleading half-snapped pose.
- Eye materials use the packaged glint overlay by default. **Materials** can disable it or adjust its strength. Eye tint and sketch-line resources are shipped beside the Release executable for future/custom material layouts.
- Legacy neighboring `<model>.materials.json` layouts and older per-model presets are migrated automatically. **Save Preset** now stores every model profile in one shared `%LocalAppData%\PhysicsTool\presets.json` database; matching mesh and texture names load automatically.
- The FBX and animation are session-only reference data. They are never written into HKCL, BPHCL, HKRG, BPHHB, or BPHYSSB files.

### HKRG Ragdolls

- Inspect the full reference-pose skeleton and the smaller ragdoll skeleton separately.
- Edit supported model bones, ragdoll bones, and rigid-body capsule transforms/radii in the viewport.
- Bind a selected bone or rigid body to the position of another supported bone without changing its ownership mapping.
- Inspect and edit supported ragdoll and hinge constraints.
- Preview the ragdoll simulation without modifying the saved pose.

## Conversion

### BPHCL to HKCL

The **Convert** button on a BPHCL file creates one new Wii U HKCL document containing every BPHCL cloth unit. It does not need an open HKCL donor or template. This path has been tested against matched vanilla files and custom in-game conversions, but unusual layouts should still be verified in-game.

The conversion-scale dialog is important:

- The selected scale multiplies dynamic particle mass.
- Inverse mass is regenerated as `1 / mass`; TotK's stored inverse mass is not copied directly.
- `Constraint Scale` starts at the same suggested value and controls the soft coefficients verified in matched files: standard-link stiffness, bend-link bend/stretch stiffness, and bend-stiffness coefficients. It can be lowered independently for an experimental cloth. Hard stretch-link stiffness, rest lengths, particle positions, masks, and local ranges are preserved rather than scaled. PhysicsTool also applies a few narrow, verified BotW constraint profiles: the repeated `Hair_2` and muffler families use an 80% bend minimum and omit TotK's bend-stiffness set; `Belt_A` uses a 0.3 hard-stretch coefficient. Unknown cloths keep their source constraint layout.

Exact mass-scale suggestions come from matched vanilla BPHCL/HKCL profiles when available. Otherwise PhysicsTool uses a structural fallback based on the cloth's simulation layout. This is a useful starting point, not a guarantee: a per-cloth in-game test is still required.

The matched files currently show the same gravity `(0, -9.81, 0)` and damping `0.001` in both games. Conversion differences observed so far are cloth-specific mass and soft-constraint normalization, not a universal gravity conversion.

### HKCL to BPHCL

The **Convert** button on an HKCL file creates a complete BPHCL through PhysicsTool's native writer. It is donor-free and writes the Havok graph, blend data, constraints, colliders, AAMP registration, and reflected TYPE metadata required by TotK. Multi-blend cloths are supported. Reopen and test unmatched layouts in-game because the two games can still use cloth-specific tuning.

### BPHHB and BPHYSSB

The **Convert** button translates helper/support-bone sidecars in either direction. BPHHB to BPHYSSB writes BotW's separate main-bone, support-bone, curve, and output tables. BPHYSSB to BPHHB rebuilds TotK's driver, driven, pose, curve, and output graph. Both exporters reparse the saved file before reporting success.

Both directions are structurally verified against matched files, and the reverse BPHYSSB to BPHHB path has been confirmed in-game. Retain the original and configure BotW output through the appropriate BPHYSICS support-bone reference.

## Recommended Workflow

### Merge a Cloth or Driver Group

1. Use **Open Physics** to load the target file.
2. Use **Open Donor** to load the source file.
3. Select the donor cloth or driver group.
4. Choose **Merge Selected** or press `M` in merge mode.
5. Read the preflight message, save, reopen the result, then test it in-game.

For BPHCL, preflight reports whether a merge can reuse existing TYPE layouts or needs source-only layouts. Both are supported beta paths; unusual reflected types can still require compatibility work.

### Edit in the Viewport

1. Select a cloth and press `Enter`, double-click it, or use **Editor**.
2. Choose the **Particles**, **Bones**, or **Colliders** tab. HKRG has **Bones**, **Ragdoll Bones**, and **Rigid Bodies** instead.
3. Select items in the viewport or list, then use `G`, `R`, or `S` to transform them.
4. Confirm a transform with left click. Cancel with right click or `Esc`.
5. Save with `Ctrl+S`; keep the original until the changed file is confirmed in-game.

## Shortcuts

Shortcuts intentionally do not override an active text cell. Finish or cancel text editing first when a shortcut appears not to respond.

### File and Cloth List

| Shortcut | Action |
| --- | --- |
| `Enter` | Open the selected cloth or driver group in the direct editor. |
| `Ctrl+S` | Save to the current path. |
| `Ctrl+Shift+S` | Save As. |
| `M` | Merge the selected donor cloth or same-format driver group in merge mode. |
| `Delete` | Remove the selected cloth in merge mode. |
| Right click a cloth | Rename or duplicate the selected cloth. |

### Editor and Viewport

| Shortcut / control | Action |
| --- | --- |
| `Ctrl+Z` | Undo the latest editor change. |
| `Ctrl+Shift+Z` | Redo the latest editor change. |
| `Ctrl+C` | Copy the selected editable HKCL item. |
| `Ctrl+V` | Paste the copied item in the active HKCL editor tab. |
| `Delete` | Delete the selected viewport item in direct-edit mode. A confirmation is shown. |
| `G` | Move the current selection. |
| `R` | Rotate the current selection. |
| `S` | Scale the current selection. Bone scaling changes bone length/position rather than the stored scale field. |
| `X`, `Y`, `Z` during a transform | Constrain movement, rotation, or scale to a world axis. |
| Press `X`, `Y`, or `Z` twice during a bone/collider transform | Use the selected bone's parent/bound local frame for that axis. |
| Press `S` again while scaling a collider | Scale its radius rather than its full shape. |
| Left click | Select an item or confirm an active transform. |
| Left-drag in empty viewport space | Box select. |
| `Shift` + click | Toggle an item in the selection. |
| `Shift` + box select | Add box contents to the selection. |
| `Ctrl` + box select | Remove box contents from the selection. |
| Middle mouse | Orbit the camera. |
| `Shift` + middle mouse | Pan the camera. |
| Mouse wheel | Zoom. |
| `Esc` or right click during a transform | Cancel the active transform. |
| Right click in the viewport | Mirror Global/Local, Copy, Paste, Paste X-Flipped, and Delete Selected. |
| `F` | Link exactly two or three selected HKCL particles. |
| `Space` | Run or stop HKCL preview simulation when the viewport has focus. |
| **Load FBX** | Load a session-only visual reference model into the active viewport. |
| **Import .anim** | Load a Maya ASCII `.anim` into memory for playback on the visual FBX. |
| **Play/Pause** | Start or pause the imported visual animation. |
| **Reset Pose** | Restore the original FBX pose without unloading the imported animation. |
| **Clear Model** | Remove the visual reference without changing physics data. |
| **Model** | Align the reference model and adjust texture, opacity, mirror, and wireframe display. |
| **Materials** | Assign per-texture UV sets and tune the visual material or save a reusable preset. |

The axis widget in the top-right corner of the viewport changes camera direction; its reset control restores the default camera framing.

## Preview Simulation

**Run Simulation** is an editor-only approximation of HKCL cloth behavior. It is useful for checking particle relationships, wind, collisions, and bone-following visually, but it is not Nintendo's Havok runtime and does not change the loaded file.

- **Wind** toggles preview wind without stopping the preview.
- **Options** exposes wind direction, strength, gustiness, gravity scale, playback speed, and solver iterations.
- Leaving wind direction unselected enables a changing random breeze; selecting the active direction again returns it to random.
- Stopping the preview restores the loaded file pose.

## Important Limits

- BPHCL merge, delete/compaction, direct editing, duplication, and conversion are working beta features. Keep original files and test every output in-game.
- BPHCL and HKCL use related Havok cloth concepts but do not share an identical binary layout or solver tuning. PhysicsTool keeps their native graphs and serializers separate.
- A successful open/save or reopen test does **not** prove a file works in-game.
- The preview simulation is not an authoritative replacement for Havok.
- JSON is retained as a diagnostic export, not as the primary authoring format.
- FBX textures appear only when the exporter embedded the image data or the referenced external image can be found. An FBX can contain material paths without containing the image bytes.

## Build and Run

Requires the .NET 9 SDK.

```powershell
dotnet build -c Release
```

Run:

```text
PhysicsTool\bin\Release\PhysicsTool.exe
```

In Visual Studio, open [PhysicsTool.sln](PhysicsTool.sln) and press `F5`.

## Project Layout

```text
PhysicsTool/
  Editor/             Viewport, preview simulation, and editor-facing models
  Formats/Bphcl/      Native BPHCL reader, writer, merge, compaction, TYPE, and AAMP code
  Formats/Bphhb/      Native BPHHB helper-bone support
  Formats/Bphyssb/    Native BPHYSSB support and BPHHB/BPHYSSB conversion
  Formats/Bphysics/   BotW BPHYSICS sidecar export and runtime-profile catalog
  Formats/Hkcl/       HKCL/HKRG reader, writer, conversion, and authoring support
  Formats/Hkrg/       HKRG ragdoll editor support
  UI/                 Windows Forms interface and conversion dialogs
```

### Rebuilding the Native FBX Bridge

The checked-in `PhysicsToolFbxBridge.dll` is a self-contained x64 bridge built with Autodesk FBX SDK. To rebuild it, install Autodesk FBX SDK 2020.3.7 or newer for Windows and run:

```powershell
PhysicsTool\NativeFbx\build-native.ps1 -Configuration Release
```

The script uses the newest installed SDK by default. Pass `-FbxSdkRoot` to select a different compatible installation.
## Credits

- [banan039 / totkbits](https://github.com/banan039/totkbits) for foundational BPHCL reverse-engineering work.
- [VelouriasMoon / HKCLTool](https://github.com/VelouriasMoon/HKCLTool) for the original HKCLTool project this work grew from.
- [HKX2](https://github.com/krenyy/HKX2) and [Json.NET](https://www.newtonsoft.com/json) for supporting libraries.
- [Autodesk FBX SDK](https://aps.autodesk.com/developer/overview/fbx-sdk) for native FBX geometry, material, UV-set, texture, transform, and triangulation support.
