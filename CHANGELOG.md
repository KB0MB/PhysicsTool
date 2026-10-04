# Changelog

## Unreleased

### Avalonia Overview and Inspector Parity - 2026-10-03

- Restored Remove selected, Duplicate selected, Rename, Merge selected (M), and Swap files. Merge uses the existing BPHCL TYPE/name guards and BPHCL-to-HKCL template preflight rather than bypassing native restrictions.
- Kept dirty state, platform, selected cloth, and undo/redo with each file during swaps. Added dirty-donor replacement/clear/close protection and selection-aware transactional undo for topology commands.
- Restored Save versus Save as, plus JSON and Wii U/Switch HKCL export copies that leave the current session unchanged. Empty HKCL containers can still be saved after removing the last cloth.
- Added helper-sidecar overview loading, group removal/merge, and saving; preserved HKRG platform detection. Detailed helper and ragdoll editing is still pending.
- Matched the original inspector's side-by-side item/value layout and separate resizable relationships panel. Moved Add Particle/Bone/Collider above the viewport and Particles follow bones above the tabs.
- Added native document-management, helper-file, dialog cancellation, dirty-donor, compact-layout, and GPU regressions.

### Familiar Avalonia Navigation and Viewport - 2026-10-03

- Restored a current-file/donor overview as the startup screen instead of opening directly into the viewport. Added Editor/Back navigation and bone inspection for both file lists.
- Reorganized the editor around a large left viewport and right-side cloth selector and particle/bone/collider tabs. Model tools sit above the viewport; simulation and visibility controls sit below it.
- Restored middle-drag orbit and Shift+middle-drag pan. Added cursor-anchored zoom, frame/reset controls, clickable axes, six camera views, and camera context-menu commands.
- Added left-drag particle box selection with Shift-add, Ctrl/Command-toggle, and Escape cancellation. Model, edits, selection, and camera survive a trip through the overview; simulation stops on Back.
- Added startup/compact-layout, pointer-navigation, selection, and GPU context-recreation regressions. This remains a migration preview, not full WinForms feature parity.

### Avalonia Model Viewport - 2026-10-03

- Added asynchronous static FBX/DAE/OBJ/glTF/GLB model import through the cross-platform Assimp runtimes, preserving hierarchy transforms and converting FBX declared units to meters.
- Added embedded/external image texture loading through Skia, UV-slot selection, and the existing Toolbox missing-texture fallback without System.Drawing or the Autodesk FBX bridge.
- Ported the existing material GLSL and seam-aware tangent builder into an Avalonia OpenGL/OpenGL ES renderer, including the complete-model depth prepass and GPU resource cleanup.
- Added Load model, Clear model, Meshes visibility, and physics overlay visibility. Failed imports preserve the current model; model viewing never mutates the physics document.
- Added model import/unit/handedness/texture regressions and real-GPU orbit/hide/resize/clear/reload checks. Windows checks pass with generated fixtures and a real Armor FBX. Linux/macOS validation, BFRES, actor loading, material presets, and animated/skinned cloth binding remain pending.

### Avalonia Migration Preview - 2026-10-03

- Created an independent source repository preserving the current PhysicsTool baseline.
- Added a .NET 10 portable core and Avalonia desktop solution targeting Windows, Linux, and macOS without a WinForms UI dependency.
- Added native cloth loading/saving, manual draft editing, undo/redo, wireframe inspection, HKCL simulation, and searchable documentation to the new interface.
- Added native-format round-trip checks, headless UI/layout checks, and a three-platform CI workflow. Local Windows checks passed; Linux/macOS execution and full feature parity remain pending.
- Documented migration limitations and retained the original README under `docs/WinForms-README.md`.

### Cloth Documentation and Button Recovery - 2026-10-03

- Fixed documentation headings losing their formatting during first-open localization, with a regression check before any topic selection. Trimmed redundant checkbox/button implementation commentary from the guide.
- Fixed Add/Remove constraint staying disabled after simulation. Button states are captured before disabling parent panels, repeated locking no longer overwrites the saved states, and relationship eligibility/style is recalculated on stop and selection changes.
- Added **Documentation** beside Settings: an offline, searchable guide with manual cloth workflows, all ten cataloged constraint families and their uses, particle mass/Fixed behavior, triangles, colliders/masks, bones/bindings, solver settings, animation, conversion, and current format limitations. It remains accessible during simulation.
- The main toolbar wraps at smaller window widths. The documentation window is resizable and its article text wraps independently of topic navigation.
- Added repeated start/stop and button styling regressions, originally-disabled/no-selection checks, search/empty-result tests, constraint-topic coverage, and documentation/toolbar layout captures.

### Checkbox and Constraint Dialog Fixes - 2026-10-03

- Fixed the particle **Fixed** checkbox requiring a second click. Checkbox clicks now toggle and commit immediately, with one undo step per toggle; Space also toggles a focused detail checkbox without starting simulation.
- Fixed distance/stretch creation being rejected after reducing the length because an inactive minimum-length value was still submitted. Disabled bend-only fields no longer participate in these requests.
- The constraint dialog now explains why Add is disabled, chooses a different second endpoint when opened with one particle selected, and warns about fixed endpoints or zero stiffness that would make a valid constraint ineffective.
- Added regressions for checkbox click/undo/redo, single-selection endpoints, shorter links in all three cloth formats, and unsupported-family/bend-range feedback.

### Manual Cloth Authoring - 2026-10-03

- Added **New cloth** for donor-free Wii U HKCL, Switch HKCL, and TotK BPHCL drafts. Start with one particle and a Root bone, then place particles manually. Drafts are explicitly unbound: model skinning, output-bone bindings, and actor registration still require additional work.
- Added an **Add constraint** dialog with distance, directed stretch, and bend/stretch creation, geometry-based length defaults, native coefficient controls, and hover descriptions. Seven additional game constraint families are described as inspect-only. Both executable class inventories were checked; this is not a claim of complete solver parity.
- Added native BPHCL constraint-set creation with TYPE metadata and execution-schedule updates, plus three-particle triangle creation and automatic distance-link edges.
- Added atomic BPHCL particle deletion for supported layouts, including dependent indices, poses, fixed mappings, buffers, and simple output-bone triangle mappings. Packed virtual-collision and unsupported graph layouts give an explicit no-change error rather than retaining stale indices.
- Added cloth-local collider deletion, including duplicate references, collision masks, and simulation selections. Shared outer collider ownership is preserved.
- Expanded relationship removal to surface-bending and regular/stiffness local ranges, and exposed local-stiffness records for numeric editing.
- Added save/reload, schema, topology, draft-format, duplicate-rejection, and editor undo/redo regression checks. The suite passed on 270 cloths in 55 BPHCL files: 808 particle-deletion cases succeeded and 100 unsupported-layout rejections preserved the source state. Animation and shared HKCL/BPHCL editing regressions also passed. BPHCL bone creation/deletion, collider creation, packed virtual-collision rebuilding, and simulation remain unsupported.

### BPHCL Editing and Simulation Review - 2026-10-03

- Fixed Space incorrectly entering BPHCL simulation and leaving editor controls locked. Keyboard, button, and simulation-start eligibility now share the same guard; BPHCL simulation remains unavailable.
- Fixed BPHCL link creation for cloths with an empty constraint-execution list. Duplicate detection now checks the selected active set, preserving the direction of stretch links instead of suppressing reverse links or unrelated constraints.
- Enabled individual BPHCL bend-link removal with native layout validation and preservation of surviving bend/stretch values. Removing the last array entry now removes its pointer fixup, preventing invalid ITEM 0 references on save/reload.
- Indexed preview constraints by set to avoid repeatedly scanning all links, local ranges, and bend-stiffness records during solver iterations. Authored ordering and solver equations are unchanged.
- Added link-policy and bend-removal regression coverage. Native editing/save/reload checks pass on 270 cloths across 55 BPHCL files, alongside editor undo/redo checks.
- Added Wii U executable research notes and documented remaining simulation gaps. A 600-frame Wii U Zelda simulation completed with zero resets; its automated OpenGL screenshot check failed, so this run is not counted as a full viewport pass.

### Helper Group Deletion - 2026-10-02

- Enabled **Remove selected** for helper/support files and added **Delete group** in the helper editor, with confirmation and full-document undo/redo.
- BPHYSSB and pose-driven BPHHB group deletion removes the selected motion graph, compacts and remaps its driver/connection/output tables, and preserves shared skeleton bone names. Header counts and indexed AAMP object names remain synchronized on save.
- Empty helper graphs save/reload correctly and clear the inspector. Unsupported BPHHB driven tables or cross-group driven channels are rejected without changing the document.
- Added regression checks for independent and repeated deletion, unchanged surviving-group poses, save/reload, invalid indices, and editor undo/redo.

### Model and Bone Editing - 2026-10-02

- Physics-only viewports no longer apply an implicit X reflection. Native cloth left/right remains consistent with BFRES before loading a model and after clearing it; the legacy reflection is now limited to FBX-backed views.
- HKCL particle movement, position-field edits, Attach, and individual-particle mirroring now commit skin references and constraint rest geometry together. Copy/gather initialization sources and all stored simulation poses follow the edit. Full particle-edit snapshots restore these references on cancel/undo/redo, preventing fixed anchors from returning to old positions on simulation reset.
- Added regression checks using edited Armor_005 and converted Ancient Sage cloths for attachment references, simulation reset, dragging, field edits, Attach, and save/reload. Repaired the user's existing Armor_005 nose/earring anchor references separately with a backup, preserving edited geometry and bone bindings.
- Collider box selection now matches particle selection: drag to replace, Shift-drag to add, Ctrl-drag to subtract, and Shift-click to toggle. Selected colliders highlight together and support group translation, rotation, and scaling with cancel/undo/redo. Mirror mode does not override explicitly selected counterparts.
- Added **Particles follow bones**, enabled by default above the editor tabs on the right. Disabling it leaves particle positions unchanged during bone field edits and viewport transforms; colliders still follow their bones. This is an editing option, not a simulation setting.
- Standalone BFRES/SBFRES imports now retain native handedness across the model, physics overlays, culling, and picking. FBX keeps its existing presentation; the optional Mirror X control still works independently.
- Fixed weighted particles appearing stationary after bone position-field edits. The viewport now refreshes their committed positions immediately, including movement inherited from parent bones. Added Armor_005 nose-ring field-edit and undo/redo regression coverage.
- Missing model textures use Toolbox's gray UV-test texture with colored crosses. The asset is embedded for portability; BFRES material slots remain empty, real textures take priority, and collision preview colors are unchanged.
- **Load Model** replaces **Load FBX** and accepts FBX plus Wii U/Switch BFRES and compressed SBFRES. Reuses the actor viewer's native model/texture reader, loads adjacent texture companions, and keeps cloth/helper pose deformation available. Maya `.anim` import remains FBX-only.
- Bone edits move fixed and free particles through their authored skin weights, including child-bone inheritance and initialization/local-range bindings. Unweighted particles are not attached by proximity. Live preview and committed edits use the same transform, with full-document undo/redo snapshots.
- Bone and helper rotation fields use Euler XYZ radians, converted to quaternions internally using the BFRES/Toolbox order. W is no longer exposed as an angle. Non-rotation edits preserve the original quaternion without display-rounding changes; simulation representation is unchanged.
- Added model-editing regression checks for weighted movement, native save/reload, BFRES import/pose and 3,000 rotation round trips including singular poses and quaternion sign changes. Extended viewport tests for weighted-particle drag/cancel/undo/redo.

### Conversion Fidelity - 2026-10-01

- BPHCL to HKCL retains collision calls in their authored positions and preserves repeated solver passes. Collision calls are no longer moved to the end or removed for Armor_1300.
- Unknown cloth defaults to source mass and coefficients (scales of 1), removing particle-count/structural/family guesses. Exact counterpart profiles remain available; invalid Hair_030_B and Outer_179 mass profiles were retired because their paired particle layouts differ.
- Helper conversion preserves explicit driver/pose reference bones in both directions, including reference-only bones. Reverse conversion supports translation channels X/Y/Z as well as rotation channels.
- Parent-space BPHYSSB export resolves parents using the loaded model instead of bone-name heuristics. Unsupported model-space/linear connections and unresolved or ambiguous references fail explicitly; reverse export validates before atomic replacement.
- Added `--self-test-conversion-fidelity <output-directory> <local-fixture-root>`. Verified 75 cloth conversions, 150 Wii U/Switch binary save/reloads, and 38 helper round trips, plus repeated-stage and invalid-reference checks. Local fixtures are not bundled. No preview-simulation changes are part of this pass; in-game validation remains necessary.

### Added - 2026-09-30

- Native BPHCL **Add Particle**, copy/paste, and mirrored paste. The append-only writer extends particle records, all simulation poses, masks, pinch flags, virtual-collision lookup tables, simulation buffers, and initialization mappings together. Missing gather-operator metadata is imported from the embedded reflection catalog.
- Native BPHCL fixed/free particle editing and collider selection. Fixed indices and skinned reference-vertex bindings stay synchronized; total mass, inverse mass, maximum radius, and landscape-collision counts are refreshed.
- BPHCL two-particle links can extend an existing active standard/stretch constraint set, and individual standard/stretch links can be removed. Other constraint families and virtual-surface topology are preserved.
- Particle editing regression checks cover append/copy, fixed/free bindings, collision masks, links, schema validation, untouched-object preservation, native save/reload, and UI undo/redo.

- TotK polytope collision generation in **Collision > Type > TotK polytope (.bgyml)**, using imported FBX, DAE, OBJ, glTF and other supported model formats.
- Native little-endian BYML v7 `*.phive__ShapeParam.bgyml` output, plus optional structured YAML output. Files are parsed back before atomic replacement; existing destinations require confirmation.
- Convex hull construction, disconnected-component separation, unique shape names, material presets, scale/offset, and explicit Y-up or Blender Z-up coordinates. Preview and model export use the generated hull geometry.
- Bounds and unit-mass box inertia computed from hull bounds, matching the AutoCalc convention measured in all 6,739 polytopes found during a 15,062-actor-pack dump survey. Compound properties use volume weighting and the parallel-axis theorem.
- Import/preview of standalone polytope-only ShapeParams, including offset rotation/translation/scale. Mixed shapes and unresolved parent files are rejected rather than partially imported.
- Automated geometry, binary/YAML round-trip, transform, invalid-input, and UI regression checks; reproducible vanilla-dump survey command.

### Scope

- At this stage, BPHCL particle authoring was limited to append/edit operations. See **Manual Cloth Authoring** above for subsequent deletion, constraint-set, and ordinary-triangle support. Native editing remains beta and requires in-game validation; model-skin bindings and packed virtual-collision rebuilding are not automatic.

- This generates polytope ShapeParams, not PhshMesh, TmbMesh, terrain collision, actor packs, rigid-body/controller links, or resource-size-table updates. Convex radius/shrink settings remain at game defaults.
- Concave connected meshes are convexified; split them into convex pieces before import to preserve holes/recesses. Flat/degenerate solids are rejected. Native Havok margin/shrink behavior and in-game dynamics have not been verified.
- Opening an existing ShapeParam imports geometry/materials for new generation; it is not a lossless metadata editor.

### Fixed - 2026-09-30

- Removed blanket external-format restrictions from supported BPHCL particle creation, copy/paste, and collider-selection controls. Undo/Redo remains enabled on other editable tabs rather than only the Particles tab.

- Collision previews use their intended material tint instead of rendering white, and output labels expand for translated text.
- Actor viewer no longer reflects the entire model and physics scene on X. Native actor coordinates now pass through unchanged; culling, picking, bones, colliders, and particles share that presentation. Legacy editing-view behavior is preserved. Regression checks now verify absolute handedness, not just model/overlay agreement.
