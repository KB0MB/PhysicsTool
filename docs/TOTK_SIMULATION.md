# Native TotK Cloth Preview

## Status: Experimental First Runtime

Supported BPHCL files can now run directly in the Avalonia editor using **Run TotK preview**. There is no intermediate HKCL conversion, no mass-profile scaling and no write-back of simulated positions to the source document. The ordinary BotW solver retains its default behavior.

This is a reconstruction of a supported steady-state subset, not the complete Havok 2022.1/game actor runtime. A stable preview does not establish matching gameplay motion.

## Implemented

- Explicit TotK solver profile, preserving authored mass and inverse mass separately.
- Integration: `pNext = p + (p - pPrevious) * damping * timeRatio + (gravity * mass + actionForce) * inverseMass * dt^2`. The force accumulator is consumed once per frame and applied in every authored substep. Fixed particles stay solver-immovable but can be moved by authored skin inputs.
- Fraction-lost-per-second damping and velocity preservation when the timestep changes.
- Object-space packed position decoding and bone-space reference inputs, including full bind transforms. Fixed MoveParticles targets and local-range reference vertices must resolve exact skin inputs, not guessed neighboring bones.
- Authored standard/stretch/bend links, local-range softness and four-particle bending, including TotK's conditional squared-height cutoff. Repeated schedule entries and collision calls retain their order.
- Collision-slot masks and simulate-config instance selection. Duplicate references do not collapse motion records. Native transform-map bone indices and offset matrices drive each collider; names do not select bindings.
- Existing sphere/capsule/tapered-capsule/plane contact kernels, moving-surface friction, and native-style triangle-to-bone output.
- Read-only runtime state, reset, animation input/loop rebasing, and editor Run/Stop/Space recovery.

Preview wind is still a user-controlled acceleration approximation. `SetParticleForces` provides the mass-aware entry point for future reconstructed game actions; it does not itself recreate those actions.

## Actor Preview and Wind

The existing Actor browser has BOTW and TOTK tabs with independent paths, search, selection and scan state. Select the matching romfs in the TOTK tab. The bundled Toolbox MeshCodec helper decodes raw models in a separate process; textures load from TexToGo in the same game folder, with no model override. The loader resolves inherited actor, ModelInfo and AnimationParam BYML references, reads Zstandard dictionaries by frame ID, initializes the external BFRES string table and loads native BPHCL/BPHHB resources. Supported cloths use the TotK profile directly; no HKCL conversion or guessed nearest-bone attachments are inserted.

The existing main-window viewport, meshes list, timeline, helper evaluation and simulation controls are reused. Native Zelda animation sets work; retargeted sets are explicitly skipped. TexToGo textures and static Toolbox-derived material bindings are supported; actor-driven material variations and ASB state-machine evaluation remain pending. Armor model actors without skeletal clips can still simulate at rest. MeshCodec's helper and source/licenses are packaged with the application; missing platform helpers, malformed input or timeout produce a load error without replacing the current actor.

Wind and Options stay available while simulating. Direction reversal now uses an angular blend instead of a normalized linear blend that could remain stuck at exactly opposite directions. Tests cover dynamic/fixed particles, live toggles and opposite-direction changes. Wind remains a controllable acceleration approximation; imported ClothReaction presets are not yet evaluated.

Validated Armor_1141_Upper (5 cloths, 7 helper groups) and Npc_Zelda_Opening (11 cloths, 19 helper groups, 128 direct animation clips), including wind response and unchanged source hashes. Zelda also passes real OpenGL rest/posed/playback/simulation checks. These checks are not game-parity certification.

The local TotK dump catalog scan finds 1,549 cloth/helper actors without read warnings, and a second cached scan returns identical metadata.

## Executable Evidence

The existing TotK `main` Ghidra project was examined with headless Ghidra in read-only/no-analysis mode. No SDK code was copied. Addresses identify this local executable build, not all game revisions.

| Address | Evidence |
| --- | --- |
| `7100490830` | Setup rescales previous positions when the step changes and computes effective damping from `(1 - globalDampingPerSecond)^dt`. |
| `710048f830` | Steady-state dispatcher: transfer motion, drive colliders using skeleton transforms and map offsets, apply per-config collider enablement, substep colliders/fixed particles, integrate, then solve. |
| `710059aee0` | Particle integration consumes per-particle action forces plus gravity times stored mass, then multiplies by stored inverse mass and squared step. |
| `7100495020`, `7100495280` | Implicit versus explicit constraint scheduling, including collision calls. |
| `71003db640`, `71003db8b0` | Clamped versus unclamped rest-bend kernels. The clamped path disables a correction beyond the authored squared-height limit; it does not simply cap curvature. |

Fresh output: `HKCL/Research/totk-native-runtime-20261007.txt`. Prior supporting outputs: `totk-constraint-schedule-20261001.txt` and `totk-ribbon-curvature-20261007.txt` in the same research directory.

## Guarded and Unfinished

The editor rejects unsupported constraint/operator classes, multi-mesh cloth packages, multiple simulation programs/configurations, incomplete collider maps, unsupported skeleton sets, unresolved movement/reference vertices, normal-component local ranges and unverified adaptive multi-substep stiffness. These cases must not silently fall through to unrelated distance-link behavior.

Remaining work includes:

1. Full state/config switching and animation-to-simulation transition blending. This first version runs the supported default steady-state program only.
2. Native adaptive stiffness dispatch and all specialized constraints, including volume, bone-plane, anti-pinch and virtual-collision records.
3. Full TotK contact/pinch recovery, landscape/self/virtual contacts, collision tolerance details and per-shape native parity. The current primitive contact kernels and deformer calibration are shared with the BotW preview and need independent TotK validation.
4. Game actor actions, wind fields, motion-transfer edge cases and helper-bone actor integration. Opening BPHCL is not a complete TotK actor importer.
5. Captured native-game comparisons with matched animation, timing, skeleton, colliders and forces. Solver stability and data preservation alone are insufficient.

The editor still retains its numerical speed/excursion guards. Their activation is exposed by the numerical reset count; the native audit requires zero resets. No assertion of bit-exact or full game-accurate simulation is made.

## Verification

`--totk-simulation <output> <bphcl files...>` runs executable-derived equation tests and native 360-frame animation-input probes with active colliders. Reports include warnings, active collider counts and maximum displacement. Source document snapshots and file hashes must remain unchanged.

Initial corpus: 24 files / 90 cloths, including the correct `Armor_1141_RaulSkin_Upper` / `SimulationMesh_Ribon_1141`, all ten `Item_Parasail` tails, legacy armor matches and Yiga cloth. All ran without numerical resets. Inputs use controlled rigid motion, not captured game animation, and do not exercise every possible pose or contact combination.

The regular suite covers mass/inverse-mass arithmetic, damping, timestep changes, action-force lifetime, collision-slot masks, unchanged BotW integration and the TotK bend cutoff. An optional native fixture checks editor Space/Run/Stop, live positions, constraint-button recovery and source isolation.

Physics-file drag-and-drop is covered separately: native storage transfers, supported extensions, multiple/unrelated file rejection, startup/editor loading, unsaved-change cancel/confirm, reentrant drop protection and corrupt-file recovery. Linux/macOS OS-level dragging still needs testing on those hosts.
