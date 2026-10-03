# PhysicsTool Code Review - 2026-09-28

## Scope and confidence

Repository-wide, risk-focused static review of the first-party editing/service boundary, native BPHCL writes and container parsing, save paths, UI history and asynchronous imports, actor/archive loading, viewport resource lifetime, FBX interop, and simulation hot paths. The collider/BPHCL changes also have executable service and WinForms regression checks. This is not a claim that every line or third-party library has been audited, nor a security certification or in-game compatibility certification. The existing working tree contains earlier uncommitted work; this pass does not revert it.

No solver equations or simulation tuning were changed for this request. Reference-pose editing and display now preserve authored BPHCL scale; scaling a hierarchy with bound colliders is deliberately rejected because an hclCollidable transform is rigid, not a general scale/shear transform.

## Remaining findings

### P1: Imported binary data can allocate excessive memory or exhaust the stack

- `PhysicsTool/Formats/Collision/BotwYaz0.cs:76` allocates the output directly from the advertised 32-bit decoded size, before validating the compressed payload. A tiny invalid file can request a huge allocation.
- `PhysicsTool/Formats/Bphcl/NativeBphclTagFileReader.cs:93` reserves a list from an untrusted type count before checking whether the section can contain that many records. `ReadSection` / `PopulateChildren` at lines 192/212 recurse without a nesting-depth budget.
- Impact: opening malformed files can freeze or terminate the application, including possible loss of unsaved work. This was identified statically; a destructive out-of-memory/stack-overflow test was not run in the user's process.
- Next: enforce decoded-size, element-count, and nesting budgets before allocation/recursion; check counts against remaining bytes; add a malformed-file corpus and fuzz the managed readers outside the UI process. Large valid TitleBG files must remain supported.

### P2: Collision imports can overlap and publish stale results

- `PhysicsTool/UI/BotwCollisionDialog.cs:224` and `:268` assign `_meshes` after awaiting worker tasks without a request-generation check. `SetBusy` at line 476 disables create/preview/export/grid, but not the import entry buttons.
- Impact: start import A, then B; whichever completes last wins, even if it is A. A completion can also re-enable controls while another task is running. Closing the dialog while work is pending is not guarded consistently.
- Next: serialize imports or cancel/supersede them with a generation token; disable all conflicting controls; check disposal after awaits. Use the actor viewer's cancellation pattern as a starting point. Race reproduction remains a UI test gap.

### P2: Some editing failures still leave partial document/cache state

- `PhysicsTool/Formats/Hkcl/HkclService.cs:2904`, HKCL branch of `UpdateColliderRows`, changes names, bindings, and transforms before validating capsule geometry. A later zero-length-capsule exception does not undo these earlier writes.
- `PhysicsTool/UI/MainForm.cs:6423`, `CommitEditorDetailChange`, retains the pending snapshot on failure but does not restore it; cache rows may already have changed. The new bone-edit service transaction protects bone/collider propagation, but does not make all editor commands transactional.
- Next: validate complete proposed edits first, then commit atomically; on failure restore both service state and cached rows. Add a batch test where the second row is invalid and the first must remain unchanged.

### P2: FBX loading lacks a close/supersession guard

- `PhysicsTool/UI/MainForm.cs:4244`, `LoadVisualModelAsync`, publishes its result after `Task.Run` without checking form disposal or whether the current document/model has changed. Its finally block invokes the viewport at line 4323 without checking that the window handle is still valid.
- Impact: completing a slow import after closing or changing the document can attach stale model state or throw during cleanup. Animation imports have a generation guard, but their finally block at line 4422 has the same handle-lifetime issue.
- Next: use an owner-lifetime cancellation token plus generation checks; dispose discarded pose sessions; guard UI cleanup. This is a static finding, not a reproduced shutdown race.

### P2: Animation import caches every deformed frame without a memory budget

- `PhysicsTool/UI/MainForm.cs:4368` allocates and fills one `VisualAnimationSample` per frame. `PhysicsTool/Editor/Viewport/VisualModelImporter.cs:178` samples a complete model via `CopyScene`.
- Impact: memory and initial import time grow with frame count times vertex count. There is no bounded cache/cancellation check inside the frame loop.
- Next: cache bone poses and shared immutable mesh data, or use a bounded frame cache/prefetch window. Measure managed allocations and peak memory for long animations before choosing a cache size. No numeric performance claim is made without profiling.

### P2: Atomic saving is not yet universal across exports

- Normal HKCL/HKRG, BPHCL, BPHHB, BPHYSSB saves and collision generation now use same-directory unique temporary files and replace only after successful writes.
- Other native export/diagnostic entry points still call `File.WriteAllBytes` directly, e.g. `PhysicsTool/Formats/Bphcl/NativeBphclWriter.cs:97` and conversion writers. In-memory structural editing writes to generated temporary paths, but direct CLI/export use may target an existing file.
- Next: adopt the same helper for remaining user-selected output paths, with parse/graph validation before replacement. Do not claim power-loss guarantees beyond flushing the temporary file and same-volume replacement.

### P3: BPHCL editing leaves temporary working files behind

- `PhysicsTool/Formats/Hkcl/HkclService.cs:4287` creates unique files under the temp directory; rename/delete/merge/duplicate load them as the next native source and do not release older ones.
- Impact: repeated editing accumulates files for the lifetime of the install/session history.
- Next: give each document a managed temporary workspace and dispose it, or return native documents directly from structural writers. Keep files alive while any source-path fallback still depends on them.

## Fixed during this pass

| Area | Problem | Change |
| --- | --- | --- |
| Bone editing | Attached colliders stayed behind; ancestor changes were not propagated. | Shared old-world/new-world hierarchy calculation moves/rotates attached colliders, including descendants. Local shape geometry remains unchanged. Preview uses a stable gesture baseline, including when returning to the original position. |
| Edit atomicity | A failing bone edit could leave related changes applied. | Validate hierarchy before writing and restore the document if a write fails. Reject cycles, invalid parents, nonfinite poses, and unsupported bound scaling. |
| BPHCL structural edits | Rename/delete/merge reopened the original source and discarded unsaved edits. | Feed current native documents into the structural writer, including the current donor state. |
| BPHCL poses | Parent and scale fields were ignored. | Persist parent indices and scale vectors in their native arrays. Parent, quaternion W, and XYZ scale are exposed in the detail grid for HKCL/BPHCL. |
| BPHCL bindings | Collider rows relied only on names. | Prefer authored collider transform-map indices when present; retain name recovery for unmapped files. |
| BPHCL capsules | Endpoint changes did not update direction/inverse-length data. | Recompute both derived fields and reject degenerate capsules. |
| BPHCL tapered capsules | Routine edits could replace both radii with one. | Transform-only edits preserve the entire shape. Unsupported endpoint/radius reshaping is rejected, with detail fields read-only. Whole-collider move/rotation remains available. |
| Planes | Plane-equation edits were dropped. | Persist native plane equations; use the inverse transform when computing local plane data. |
| Collider inverse math | Inverse point conversion assumed unit, orthogonal axes. | Use an actual matrix inverse for native collider edit coordinates. |
| Undo | At entry 51 the history discarded recent edits and reversed the retained order. | Keep the newest 50 snapshots in correct stack order. |
| JSON import | Polymorphic deserialization used unrestricted CLR type resolution. | Allow only named types from the already loaded Havok assembly; do not load arbitrary assemblies from `$type`. |
| Saves | HKCL serialization opened/truncated the destination before finishing. | Write to a unique sibling temporary file, flush, then replace. Apply to standard native saves and collision generation too. |
| Yaz0 output | Reserved header bytes were not explicitly initialized. | Clear the stack-allocated header before encoding. |

## Optimization priorities

1. Pre-index constraints by set while constructing `HkclPreviewSimulator`. At line 1146 and the range/bend passes, every execution filters the complete constraint list again. Preserve authored execution order and duplicate constraints; do not deduplicate them. This pass leaves the solver unchanged.
2. Reduce per-frame allocation in `GetPositions` (line 145), collider-motion dictionaries, and bone-pose reconstruction. Prefer caller-owned buffers or immutable snapshots with explicit lifetimes; do not expose mutable solver state to the UI.
3. Replace full JSON/base64 snapshots only after profiling. Snapshot cost is visible in the edit boundary, but correctness and exact rollback matter more than a premature command-delta rewrite.
4. Reuse hierarchy/name lookup maps within one edit/preview update. The new bone follower memoizes world transforms; other preview helpers still rebuild/search hierarchy data repeatedly.
5. Split MainForm's import lifecycle and document edit transaction responsibilities into focused components as those areas are fixed. Avoid an unrelated whole-file refactor alongside binary-format work.

## Verification

- Normal Release build in place: successful, 0 errors, 183 warnings. Used `/p:RuntimeFrameworkVersion=9.0.19` because that runtime pack is cached; no project dependency versions were changed.
- New `--self-test-editing <output-directory> <fixtures...>` suite covers synthetic parent rotation plus child movement, immutable baselines, Havok padding, invalid hierarchy rejection, actual ancestor/direct edits, file roundtrips, raw-state restore, live WinForms preview, cancel, undo/redo, newest-50 history, JSON roundtrip/type rejection, and injected save failure.
- BPHCL checks cover native parent/scale persistence, in-memory target/donor edits surviving rename/merge/delete, capsule derived data, unchanged shapes on transform-only edits, and plane-equation persistence. Input fixture hashes remain unchanged.
- Passed full editing/UI checks: BotW Zelda Miko HKCL; TotK Zelda AncientHyrule, Armor 1106 upper, Armor 1071 upper, Armor 1300 head, Armor 1043 upper BPHCL.
- Armor 1091 lower has no cloth with a resolved bound collider and is explicitly skipped for binding/UI tests. It is not counted as a binding pass.
- Fixture shape coverage includes capsules, spheres, and a plane. No tapered-capsule fixture was exercised in this run; the guard exists but needs a dedicated taper corpus case.
- Existing animation/cloth/support equation and transport-layout regression suite passes. These tests do not prove parity with either game's full simulation.
- Remaining gaps: in-game loading of edited BPHCLs, a comprehensive malformed-file corpus, manual DPI/localized field-layout checks, import cancellation races, legacy JSON types beyond the tested fixtures, and a measured CPU/GPU/memory profile. Third-party binary internals were not audited or rebuilt.

## Suggested follow-up order

1. Bound input parsing and make all editor commits transactional.
2. Serialize/cancel asynchronous imports and guard window lifetime.
3. Add bounded animation caching and profile representative large actors.
4. Expand BPHCL shape/binding fixtures before enabling tapered reshaping or collider reassignment.
5. Broaden atomic exports and temporary-file lifecycle management.
