# Avalonia Migration Status

## Boundaries

The new .NET 10 solution separates portable document/solver logic from the desktop UI. It does not load WinForms controls or depend on the original working checkout. Shared source paths stay inside this repository. The original source snapshot is retained for behavior comparisons and gradual migration.

## Implemented

- Collision workspace with direct actor-pack/HKRB/HKSC/ShapeParam access, Current actor (including embedded packs), source mesh import, material/inclusion controls, scale/axis/offset preview, solid/wireframe rendering and validated generation/export. Native targets: Wii U/Switch HKRB, Wii U HKSC/SHKSC, TotK polytope BGYML; geometry exports: DAE/FBX. Switch HKSC generation and actor registration remain outside this workflow.
- HKRG joint limits/friction, ragdoll bone poses and sphere/capsule rigid-body fields in the main editor. Joint selection highlights linked bodies; ancestor pose changes move bound bodies and synchronize their native motion states. Unchanged coefficients/quaternions and detected save platforms are preserved.
- Portable compilation of format readers/writers, editor models, and HKCL solver.
- Transactional editor session, bounded undo/redo, dirty tracking, and save/load.
- Avalonia native file dialogs, resizable editor layout, particle/bone/collider inspector, basic topology commands, and searchable documentation.
- Existing supported relationship length/stiffness/local-range editing with validation and transactional undo. Bone names, cycle-safe parent selection, local XYZ radians, position and scale are exposed; backend rotations remain quaternions.
- Particle inverse mass, radius/friction, and cloth-local collider assignments; collider names, HKCL bone reassignment preserving position, local shape endpoints/radius, and plane position/normal inspection. Fixed particles lock mass fields; BPHCL derives inverse mass. Unlisted collision bits and repeated slots are preserved.
- Original-style startup overview with current and donor cloth lists, bone inspection, and explicit Editor/Back navigation. Remove, duplicate, rename, donor merge, and file swap are wired to native logic with undo and preflight guards.
- Per-file dirty state, path, platform, selection, and history survive swaps. Replacing or clearing an edited donor warns before discarding it. Save and Save as are separate from non-mutating JSON/platform exports.
- Standalone HKCL/BPHCL and BPHYSSB/BPHHB conversion controls. BPHCL to HKCL has platform selection and independent per-cloth mass/constraint scales; model rest-skeleton parents are passed to support-to-helper export. Detached snapshots retain source profile identity and unsaved edits. Native background conversion validates temporary output before replacing a destination without changing the editor's document/history.
- Helper group removal/merge/save, individual affected-helper deletion, role-aware base poses, support creation from affected records, duplication, ordering and group mirroring. BPHYSSB/BPHHB have plotted curve editing, new three-key curves and one/two-response connection authoring with shared-reference isolation. Existing BotW linear responses can be connected; their coefficient editor, new driver records and separate TotK driven-solver types remain pending. The file picker accepts helper sidecars and HKRG as well as cloth.
- Large left viewport and right inspector tabs, original middle/Shift-middle camera gestures, cursor-anchored zoom, clickable 3D axes, framing controls, and active-tab particle/bone/collider selection.
- G/R/S modal transforms with axis constraints, non-mutating previews, commit/cancel, and single-step undo. Supported HKCL multi-bone deletion preserves surviving child world poses and remaps supported references; native BPHCL bone deletion remains guarded.
- Stable overview double-click navigation, theme-aware window/dialog surfaces, spinner-free numeric fields, and original-style animated bone joints/links.
- Cached geometry-only transform previews with coalesced bone skinning, immediate cancellation, corrected oblique-axis translation and collider origins. Searchable Attach to Bone supports original particle-anchor snapping, bone snapping and HKCL collider rebinding, with undo.
- DrawingContext-based wireframe viewport and HKCL simulation preview.
- Original-style editor footer with Undo/Redo, mass scaling, and HKCL cloth settings. Mass scaling preserves fixed particles and supports both HKCL and native BPHCL particle values.
- Live preview Options with wind direction/strength/gustiness, gravity multiplier, playback speed, and solver pass multiplier. Sliders and precise numeric inputs stay synchronized; Cancel restores previous options. Fixed-step scheduling preserves the requested speed without exceeding the solver's per-call time cap.
- Avalonia-hosted OpenGL/OpenGL ES model rendering with the original material GLSL, UV/tangent builder, and complete-model depth prepass.
- BFRES/SBFRES-only user-facing model loading and mesh visibility. Assimp mesh backends remain for collision source imports/exports and generated rendering tests, not regular editor import.
- Real GPU checks covering textures, orbit, hiding, resizing, and resource clear/reload; asynchronous import failure preserves the previous model and physics data.
- Generated native format round-trips, source-preserving fixture checks, and headless rendered UI checks.
- World-anchored ground grid shared by CPU and GPU previews, with model depth occlusion, fixed 0.25-unit spacing and fixed bounds. Camera navigation never resizes or re-subdivides the grid.
- Shape-specific collider guides include oriented capsule sides/end caps, tapered end radii, spheres, points, and planes, with optional translucent x-ray surfaces. Frame all includes their rendered bounds.
- Smooth axis-view transitions and zooming Frame Selected for particles, bones and colliders. Ground grid footprint is fixed at 20 by 20 units.
- Standalone imported model skinning follows edited and simulated cloth bones, preserves bind offsets, and restores edited poses on Stop. Bone/collider overlays follow solver poses too. No automatic weights are generated for unrigged meshes.
- Wii U/Switch BFRES/SBFRES loading with adjacent Tex1/Tex companions. GX2 and Tegra addressing plus BCn decoding are managed; unsupported signed or uncommon texture formats produce warnings instead of invoking a Windows decoder.
- Searchable cloth/helper actor catalog with remembered resource folders and TitleBG discovery. Actors use the same viewport in read-only mode, with original skeletal animation, support evaluation, cloth-to-model binding, and loop rebasing.
- The Actor browser has separate BOTW/TOTK tabs and remembered paths. TotK packs use dictionary-aware decompression, inherited model/animation references, raw MeshCodec or decoded split BFRES loading, native BPHCL preview and BPHHB helpers. TexToGo decoding and TotK material/UV handling are implemented; the model override was removed. Retargeted clips, complete procedural shader behavior and game cloth-reaction wind remain pending. Wind/Options can be changed while simulating in both the editor and Actor viewer.
- Actor animation timeline, play/pause, Loop, mesh visibility, and 30/60 speed settings. GPU pose updates reuse texture and buffer resources.
- Standalone skeletal animation import from BFRES/SBFRES, shared timeline transport, 30 Hz normalized frames, 30/60 speed settings, animated HKCL inputs, and loop rebasing. Failed imports preserve previous state. FBX/DAE/glTF/GLB animation and Maya .anim import are intentionally excluded from the user-facing workflow.

## Next Stages

### Parity Audit - 2026-10-04

Compared the current Avalonia entry points and inspectors with `PhysicsTool/UI/MainForm.cs` in the WinForms checkout and its local source snapshot. This is a UI/workflow audit, not a claim that every native Havok branch has been runtime-validated.

| Priority | Remaining gap | Current evidence / boundary |
| --- | --- | --- |
| High | HKRG viewport parity | Both skeletons can be inspected and edited; ragdoll bones/bodies support viewport move/rotate. Full model-bone poses still use the inspector. Model-bone viewport transforms and aligning a rigid body to a chosen bone remain to port. Cross-skeleton bone alignment and existing mapper/joint-frame retargeting are available. |
| Medium | Cross-cloth paste and mirror extensions | Same-cloth copy/paste and selection mirroring work. Whole-cloth X reflection includes native reference geometry, winding, signed curvature and deformer offsets. Shared data, volume/bone-plane constraints and unsupported native layouts are guarded. Cross-cloth binding remapping remains pending. |
| Native limitation | Collision extensions | Generation/preview/export UI is migrated. Switch HKSC generation, mixed/inherited TotK ShapeParams and automatic actor-pack registration remain unsupported native workflows, not missing dialog controls. |
| Medium | Model/material tooling | Mesh visibility works. Material presets, texture replacement, UV/shader controls, model alignment/display controls and animation-hover preview are missing. |
| Medium | Native interaction parity | G/R/S and colored axis/plane guides work. Ctrl/Command+O/S/Shift+S, undo/redo, F1, select all/deselect, copy/paste/mirrored paste, F/Del, timeline Space and numpad views are supported. Draggable on-screen transform handles remain pending. |
| Medium | Settings/localization | Settings persists theme, FPS and language beside the executable. Existing French/Spanish/Japanese catalogs and new common-control labels are wired up with immediate switching. Advanced help bodies and some diagnostic strings still need translation. Actor resource-folder persistence is separate and already exists. |
| Platform / last | Remaining portability verification | Uncommon texture formats, actual Linux/macOS GPU/input testing and per-platform packaging remain pending. Maya `.anim` is removed from scope by request. |

Separate authoring improvements, not established WinForms parity requirements: new helper driver records, linear slope/intercept editing, separate TotK driven-solver creation, new ragdoll mapper links and replacement skeleton topology. Helpers already support base poses, groups, individual deletion, new response curves, connections and standalone BFRES evaluation.

Fixed during this audit: same-context refresh/undo/redo retains the complete live selection and active row; constraint creation requires two explicitly selected particles (three for triangles), rejects incomplete/fractional/self endpoints, and warns about fixed or zero-stiffness links. Light theme now includes CPU/GPU viewport backgrounds, grid, particles, bones, colliders and transform guides without changing geometry or materials.

Follow-up inspector pass: restored the relationship-value and bone-field gaps. Existing supported lengths, bend limits, stiffness and local ranges can be edited inline; compression length and fields not written by the relevant native serializer remain read-only. Bone name, parent and local radians controls reuse the original quaternion conversion and edit paths. Parent selection excludes descendants, and unrelated edits preserve the stored quaternion. Generated Wii U HKCL, Switch HKCL and BPHCL checks cover validation, undo, save/reload and compact/desktop layouts.

Particle/collider follow-up: restored inverse-mass policy, collision assignments, collider names, HKCL bone reassignment, and shape-specific local-coordinate fields. Shape type and plane normals remain read-only, matching the original inspector. BPHCL rebinding/tapered reshaping remain native restrictions, not missing buttons. Tests cover mask-bit preservation, mass policy, one-click Fixed, validation, native geometry/name round trips, undo and rebind-without-snap. Native HKCL binding comparison now uses the same cloth-local map as the reader; local edits normalize Havok's packed fourth column before inversion.

Validation: the complete headless editor/native-format suite and real Windows OpenGL checks pass, including light/dark background pixels, grid invariance, selection retention and dialog validation. Linux/macOS execution remains unverified.

Selection preservation here means preserving the current selection through refresh/history; it does not add a serialized selection history for resurrecting deleted selections or switching across different cloths/tabs.

Inherited authoring restrictions remain: local-range/bone-plane/transition constraints can reference one particle but are inspect-only for creation. The three authorable link families require two distinct endpoints. Blank HKCL cloths without a collider template cannot currently create their first collider through the native `AddCollider` path; that requires native authoring work, not merely enabling a button. BPHCL now has a guarded experimental steady-state runtime; see [TotK simulation](TOTK_SIMULATION.md) for supported kernels and remaining gaps.

### Work Order

1. Collision workflow is migrated; extend native format support only as separately requested.
2. Complete remaining helper authoring and ragdoll retargeting; extend guarded cloth-mirror layouts, cross-cloth paste and on-screen transform gizmos.
3. Restore remaining actor workflow details such as animation hover previews. Regular model/animation imports stay BFRES-only; Maya .anim is no longer a migration task.
4. Material/preset editing is deferred; existing BFRES shader-family/texture assignments continue to render.
5. Last: persistent settings, localization, uncommon texture/platform checks, actual Linux/macOS GPU/input validation and per-platform packaging with dependency/license manifests.

## Preserved Restrictions

Native serializers retain their supported-layout restrictions. The new TotK preview does not expand BPHCL bone creation, collider creation, or unsupported packed graph editing. New cloth drafts do not provide automatic model skinning or actor registration. The help guide preserves the full application's documentation, with a migration-status topic explaining unavailable UI features. The main window accepts one physics file by drag-and-drop, including unsaved-change confirmation and failed-load preservation.

## Validation Scope

The helper-connection pass checks new curves, one/two-response wiring, BotW weights, disconnect/reconnect, individual deletion, confirmation/undo and native save/reload on local BPHYSSB/BPHHB fixtures. Reordered/shared TotK poses are exercised through inspection, edits, duplication, deletion and conversion to BotW. Native translation binding activation is checked independently. Headless desktop/compact captures cover the connection controls and curve creation form; no in-game fidelity claim is made.

The 2026-10-10 helper/ragdoll/mirror pass passed the complete Windows Release regression suite and focused helper checks after the final frame-picker change. Native BPHYSSB/BPHHB tests exercise the response graph/presets, model loading, animation seeking and evaluation toggle with source isolation. HKRG tests cover multi-selection preview/commit agreement, ancestor movement once, native sweeps, save/reload and stress-preview start/stop. Whole-cloth tests accepted ten local native HKCL cloths, guarded one volume-constrained cloth and two shared-collider BPHCL cloths, then successfully mirrored an isolated copy of a native BPHCL cloth. Double-mirror checks cover poses, winding, skin references and authored deformer offsets. No game assets are distributed, and these are structural/preview checks rather than in-game or Linux/macOS certification.

The collision pass checks transformed geometry against output bounds, Wii U/Switch HKRB headers, compressed/uncompressed HKSC, TotK hull generation, DAE/FBX exports, source isolation, stale-preview guards, failed-import recovery and compact/desktop UI. Native tree actor packs and terrain fixtures from both BotW platforms were loaded read-only with unchanged hashes. Real GPU tests verify solid collision pixels, orbit and resize. These do not certify game integration or lossless rebuilding of original rigid-body behavior. Earlier Assimp skinning/animation fixtures remain low-level regression data, not enabled user import formats.

The 2026-10-05 ragdoll pass uses a local native Mipha HKRG copied to both Wii U and Switch layouts. Checks cover joint limit edits without precision changes to other coefficients, invalid values/ranges, capsule local geometry and world pose, radians, packed transform preservation, ancestor-driven body movement, motion-sweep consistency, save/reload, undo, keyboard guards and mode-specific controls. Compact headless captures show the inline inspector. No fixture is shipped; missing local fixtures report a skip. Full skeleton mapper/joint-frame retargeting, rigid-body mass/inertia authoring and in-game ragdoll behavior are outside this pass.

Conversion checks cover generated cloth files in both directions and HKCL platform headers, local native BPHCL-to-HKCL and BPHHB/BPHYSSB round trips, source-name profile preservation, unsaved-edit export, editor state/history isolation, invalid-scale destination preservation, dialog cancellation and scale/platform acceptance. Headless screenshots cover the conversion dialog and compact toolbar. This migrates established conversion logic rather than claiming new native Havok fidelity.

Selection clipboard checks cover generated Wii U/Switch HKCL and BPHCL particle copies, repeated/mirrored paste, copied bone hierarchy and unique names, stale-context rejection, transactional undo/redo, shortcut focus and simulation guards. Optional local fixtures verify HKCL collider copies and BPHCL collider mirroring through native save/reload; packed plane-transform reflection is also checked. A 980x640 headless capture checks the compact footer. Paste does not author constraints, triangles or model/output bindings, and requires recopying after an unrelated document edit.

The helper-editor pass uses local BPHYSSB and BPHHB fixtures for role/pose/radians controls, Create cancellation/commit, duplication, ordering, mirroring, Delete Group cancellation/commit, curves, undo and save/reload. Source hashes are preserved. A whole-bone-table reorder regression verifies that curve driver-record references do not change. Existing key edits retain neighboring curves and wiring. These tests require the optional local sidecar fixtures and report a skip when unavailable; no game assets are shipped. Standalone helper viewport transforms remain guarded because base graph frames are not a complete actor hierarchy.

Local checks were run on Windows with .NET SDK 10.0.401 and Avalonia 12.1.3. They include generated Wii U HKCL, Switch HKCL, and BPHCL drafts, two existing native cloth files, solver sanity checks, and Avalonia screenshots at 1440x900 and 980x640. Headless screenshots verify rendering/layout, not native window-manager interaction or GPU model rendering.

The second migration pass also checks generated OBJ/DAE/FBX/GLB imports, embedded/external texture orientation, hierarchy and units, failed-import recovery, and model/physics isolation. Native Windows GPU checks pass with a generated texture fixture and a local Armor FBX, including orbit, mesh hiding, compact resize, and clear/reload. Captures are ignored build artifacts, not shipped game assets.

The navigation pass adds startup/overview snapshots, actual middle-button orbit and Shift-pan input, anchored zoom, box-selection/cancellation checks, and preservation of camera/model/selection across Back. Native GPU checks cover top/bottom view lighting and graphics-context recreation on returning to the editor.

The overview parity pass checks rename/duplicate/remove/merge and save/reload for generated Wii U HKCL, Switch HKCL, and BPHCL; rejected merges and canceled dialogs; lossless swaps; export isolation; and helper-group removal/undo/merge/save with local BPHYSSB and BPHHB fixtures. Original fixture files are hash-checked and never replaced by the test outputs. Editor layouts use the original side-by-side item/value arrangement and separate relationships panel.

The tuning pass covers fixed/selected/all mass scaling, invalid values, inverse-mass consistency, undo/redo, native cloth-settings round-trips, unsupported BPHCL settings, live preview rollback, dialog validation and selection retention. Clock tests verify 0.5x/1x/2x/4x playback, timer jitter, and bounded catch-up. These are preview controls, not new claims of game-accurate Havok behavior.

The actor pass tests Wii U/Switch Armor_005_Head (eight cloths and one helper group each), Switch Zelda (three cloths, twelve helper groups, 254 animations), BFRES texture/GPU loading, timeline sampling, live playback/simulation, and read-only editor-state isolation. The Switch catalog scan found 999 cloth/helper actors including an embedded archive actor, with no scan warnings. Grid tests check world-plane anchoring and matching CPU/GPU projection during camera movement. Game resources are read-only fixtures, not included in the repository or release.

The standalone binding pass verifies weighted FBX/DAE/GLB vertices, mixed influences, child inheritance, physics/model bind offsets, simulation pose consumption, and a real multi-model Armor_005 BFRES. Editor checks cover bone edits, repeated refresh, stop, undo/redo, Back, and unchanged saved physics. Camera checks cover intermediate/final easing, interruption, single-particle zoom, and grid invariance. Surface vertices are checked against the capsule wire volume.

Standalone animation checks cover imported keyframes and transport-unit conversion, animation-owned versus cloth-owned tracks, seeking, looping, half/normal speed, animated colliders, simulation loop rebasing, failed imports, and authored-state isolation. Native checks load 254 Zelda BFRES clips. Windows GPU checks run standalone Zelda animation alongside its HKCL and retest Actor-mode playback after sharing the transport.

The CI workflow defines Windows, Ubuntu, and macOS jobs. It has not yet been executed on a remote repository. Cross-platform architecture is in place; platform certification and feature parity are still pending. On backends without a supported OpenGL context, models remain available as a reduced wireframe preview.

Renderer references: [Avalonia OpenGlControlBase](https://docs.avaloniaui.net/api/avalonia/opengl/controls/openglcontrolbase), [AssimpNetter](https://github.com/Saalvage/AssimpNetter), [Silk.NET.OpenGL](https://www.nuget.org/packages/Silk.NET.OpenGL/2.23.0).
