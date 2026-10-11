# Changelog

## 2.0 - Release Candidate

- Translated all in-app documentation and constraint descriptions into French, Spanish and Japanese, with localized search and live language switching. Shortened the README and moved detailed usage guidance to the in-app documentation.

- Replaced the Toolbox MeshCodec wrapper with direct upstream decoding through a GPL-2.0 helper, with bounds checks, regression tests and matching source/license notices.

- Rebuilt the desktop interface with Avalonia and made it the main PhysicsTool application. Preserved the original WinForms repository separately.
- Expanded cloth, support/helper-bone and ragdoll editing, including response curves, connections, individual helper deletion, multi-selection and undo/redo.
- Improved HKCL/BPHCL conversion, support-bone conversion and mixed-format donor merging, with stricter native graph validation.
- Added BotW/TotK actor viewing, BFRES animation/material previews, wind, TotK cloth preview and a collision generation/preview workspace.
- Refined viewport navigation, transforms, timeline controls, File/donor workflows and offline editing documentation.
- Expanded French, Spanish and Japanese interface translations, including constraint validation. Added portable/per-user settings handling.
- Added self-contained Windows, Linux and macOS packages for x64 and ARM64, source archives, dependency notices and SHA-256 manifests. Native decoder fixes cover Linux linking, libc++ and ARM64. Non-Windows/ARM UI and GPU compatibility still need native verification.

See docs/RELEASE_2.0.md for release notes and docs/RELEASING.md for packaging, testing and licensing.

## Development History

### Documentation Wording - 2026-10-10

- Rephrased the editing guides in a friendlier voice, removed repeated save/check instructions, and replaced the introduction's closing line with a simple backup reminder. Technical explanations and editing limits remain documented.

### File Workflow, Mixed Merging and Editing Help - 2026-10-10

- Moved Open Donor into the donor pane. Merge selected, Clear donor and Swap files replace it while a donor is loaded; donor commands require a current file. Renamed Actor to Open Actor and grouped Save, Save as, Export and Convert under File.
- Mixed HKCL/BPHCL merging now converts only the selected donor cloth through the current conversion backend, retaining per-cloth default mass/constraint tuning and one undo step. Native tests exposed and fixed legal bodyless Havok type aliases being rejected by type-table union, and missing element relocation metadata for empty reference arrays.
- Replaced in-app migration/implementation notes with shorter guides to supported editing, constraints, colliders, helpers, ragdolls, conversion and shortcuts. The initial heading is refreshed explicitly when filtering documentation.
- Added HKRG model-skeleton inspection and local pose edits, plus cross-skeleton world-space alignment. Existing mapper offsets are retargeted on bone edits; physical bone edits also update bodies, motion sweeps and both native joint collections. These actions do not create new mappings, bodies or joints.
- Restored English/French/Spanish/Japanese interface selection using the existing catalogs and new common labels. Language, theme and playback FPS persist in settings.json beside the executable; unrelated settings are retained. Advanced help bodies and some diagnostics remain English.
- Added Ctrl/Command+O, F1, viewport A/Alt+A and retained Save as, undo/redo, transform and numpad shortcuts. Added native fixture, UI layout, language switching, settings persistence and ragdoll alignment regressions.

### Helper Connections and Individual Deletion - 2026-10-10

- Added affected-channel connection editing for BPHYSSB and pose-driven BPHHB, with named driver responses, optional additive second response, BotW output weights, and disconnect/reconnect. Shared outputs and TotK poses are isolated before rewiring. TotK connections enable their native translation/rotation binding when needed.
- Added independent three-key response creation from existing input bones, with input range, scale and linear/smooth initialization. Existing BotW linear responses remain selectable; slope/intercept authoring is still pending.
- Added confirmed, undoable Delete helper for one affected bone. Model-bone references, input roles, shared resources and other helpers are preserved. Disconnected helpers stay accessible in the first group.
- Fixed TotK pose-index assumptions in inspection, pose edits, duplication, support creation, merging, mirroring and conversion to BotW that could target the wrong pose after individual deletion or shared-pose edits.
- Added native and headless UI checks for new curves, connection isolation, disconnect/reconnect, deletion, undo, save/reload, invalid inputs and shared/reordered TotK poses. Added the helper editing guide. Separate TotK driven-solver types and new driver-record authoring remain guarded.

### Helper, Ragdoll and Mirror Migration - 2026-10-10

- Added standalone BPHYSSB/BPHHB evaluation against imported BFRES models/animations, including ordinary descendants of driven bones. Evaluate helpers compares with animation alone without changing saved physics.
- Helper curves now show driver bone names and a live Hermite plot. Linear, Smooth and Zero response presets plus response scaling edit a draft; Apply commits one undoable change while retaining graph connections and native key counts.
- Fixed the support-frame picker: BotW model-parent (-1) and model-space (-2) modes are both available and named explicitly instead of treating the reference as None.
- Added multi-selection ragdoll move/rotate with preview/commit agreement, ancestor inheritance once, packed-transform preservation and native motion-sweep updates. Ragdoll scaling remains guarded because inertia/mass need separate authoring support.
- Migrated the original approximate ragdoll glide/stress preview with Wind and Options, edit locking and clean Stop. Cached its bone-pose lookup once per collider update instead of rebuilding it per body. This is not a native Havok runtime.
- Added guarded whole-cloth X mirroring. Fixed missing HKCL skin-reference reflection, signed bend-curvature reflection and deformer offset preservation, including TotK target-bone conventions. Triangle winding and side-name tokens are mirrored with atomic rollback and undo. Shared collision/skeleton data and unvalidated volume/bone-plane/operator layouts are rejected.
- Updated regression checks and documentation. Arbitrary helper wiring, new curves/linear connections, individual helper-record deletion, ragdoll retargeting and some mirror layouts still remain to migrate.

### Conversion to TotK Release Audit - 2026-10-10

- HKCL to BPHCL now retains ordered/repeated constraint and collision passes, substeps, solve iterations and adaptive-stiffness settings while regenerating the native runtime buffers. Implicit all-set schedules retain source set order. Unsupported multiple simulation configurations are rejected instead of flattened.
- Preserved bone-plane constraints and their skeleton read access, and independently authored inverse masses (with inverse scaling for existing measured mass profiles). Added non-finite/negative inverse-mass checks and post-write solver/mass validation.
- BPHYSSB to BPHHB now bakes single/double output weights into per-use Hermite curves, retaining zero, fractional and negative weights without changing a curve shared by another output. Added warnings for extrapolated BotW curves whose extreme poses need verification.
- Audited 21 native HKCL files / 71 cloths and six directly resolvable support files. Eleven additional support files correctly require their matching model hierarchy. Added synthetic binary regressions; game-side behavior is not certified by these structural/curve tests.

### TotK Grayscale Material Colors - 2026-10-09

- Fixed BC4 grayscale albedo and eye-shadow maps rendering red, as seen on Purah (Npc_AncientDoctor). Color sampling now replicates luminance across RGB; raw normal/mask samples, alpha, material constants and explicit texture channel selectors are unchanged.
- Verified Purah with emission enabled and disabled, and added BC4 metadata/channel and actual OpenGL color/opacity regressions. The separate procedural glasses/refraction pass remains approximate.

### TotK Animated Particle Poses - 2026-10-09

- Fixed free particles staying at their rest positions during TotK actor animation playback. Preview resets and timeline seeks now use their authored local-range skin weights in addition to fixed-particle MoveParticles bindings.
- Animation-only TotK particle poses now use the visible model's animated cloth tracks. Simulation still uses clean input poses without feeding simulated output back into its inputs; free particles remain dynamic.
- Preserved edited particle offsets and original constraint reference vertices. BotW's existing particle-pose behavior is unchanged.
- Verified all 364 HorseGod particles against their model-relative weighted poses across 48 loaded clips, seeking/restoration, simulation loops/stop, native wind and source-file isolation. Added synthetic weighted-pose, edit-preservation and BotW-isolation regressions.

### TotK Texture Tile Layout - 2026-10-07

- Fixed TexToGo base-mip GOB-height inference. The old power-of-two ceiling could choose 32 instead of 16 for tall maps and also misread intermediate sizes. This rearranged texture tiles, putting Zelda's clothing colors and normal-map details on the wrong body parts despite correct UV bindings.
- Matched Toolbox's native tegra_swizzle rule without adding a native runtime dependency. Added a nonuniform 64x1024 BC1 regression, block-height boundary checks, and an optional independent native comparison covering 4096 heights and 90 byte-exact layouts.
- Visually verified Zelda's restored body atlas and actor render, including blue sleeves and dark trousers. No lighting adjustment or UV flip is used to hide the decoding error.

### TotK Rendering and Actor Preview Performance - 2026-10-07

- Corrected RGB texture decoding to match Toolbox's preview policy, including UNORM color textures while keeping BC4 masks linear.
- Unevaluated procedural opacity now preserves base alpha instead of discarding dark clothing through an unrelated albedo channel. Verified Ganondorf's Band_A and Botom_Back meshes separately on the GPU; full miasma dissolve expressions remain unimplemented.
- Added an Emission checkbox to the viewport, disabled by default, with live TotK emission switching.
- Cached read-only actor helper-group metadata and skinned authored BFRES tangents instead of rebuilding them per triangle every frame. Simulation steps and solver equations are unchanged. A Ganondorf fixture reduced measured actor-update plus geometry CPU work from approximately 44 ms to 20 ms per frame; actual frame rates vary.
- Added real-shader alpha regressions for both PhysicsTool and Toolbox, emission pixel checks, isolated missing-mesh checks and helper-cache equivalence coverage.

### TotK TexToGo and Material Preview - 2026-10-07

- Removed the Actor browser's model override field and saved override setting. TOTK uses its own selected game folder for models, textures and animations.
- Added portable TexToGo loading for referenced material surfaces: Zstandard, Tegra block-linear layout, BC1/3/4/5/7 and managed ASTC decoding, channel remapping and sRGB metadata. Missing or unsupported textures report warnings without preventing actor viewing.
- Added Toolbox-derived TOTK.frag with ordered shader sampler assignments, four authored UV sets, static SRT transforms, vertex colors, texture-array selection, normal maps, alpha tests, packed masks, hair/skin/eye and emission preview paths. BotW's program remains separate. This is preview lighting, not a full emulation of the game's shader expressions or deferred renderer.
- Added texture-format, malformed-input, material-binding and GPU pixel-difference regressions; verified native Zelda and Armor 1141 resources without modifying game files.

### Actor Game Tabs and MeshCodec - 2026-10-07

- The existing Actor button now opens BOTW and TOTK tabs. Each tab keeps its own game path, search, actor selection, scan results and cache; old folder settings migrate to the separate fields. Background scan completion cannot replace the other game's list.
- TOTK actors can load raw Model/*.bfres.mc directly from romfs using the Toolbox MeshCodec decompressor. A decoded model folder is now an optional override, shown only in the TOTK tab.
- Bundled a standalone, bounded MeshCodec helper with corresponding source and licenses. Decoder errors/timeouts do not crash the editor or replace the current actor. Windows x64 is built and tested; platform-native CMake build steps are configured for Linux/macOS CI.
- Added tab persistence/cancellation/layout regressions and isolated decoder round-trip/malformed-input tests. TexToGo textures and retargeted animation sets remain pending.

### TotK Actors and Live Wind - 2026-10-07

- Actor browser now indexes TotK Pack/Actor/*.pack.zs alongside BotW actors, with game/platform labels and cached metadata. Resolves the matching game's compression dictionaries and inherited actor/model/animation BYML references.
- TotK actors load decoded split BFRES models, the external string table, supported animation resources, BPHHB helpers and native BPHCL simulation in the existing read-only viewport. Use Model folder to add decoded meshes. Unsupported cloth programs are skipped individually with warnings, never converted silently to the BotW solver.
- Wind and preview Options remain enabled during native cloth simulation. Fixed particles remain fixed. Corrected a shared wind-direction blend that could stick when reversing to the exactly opposite axis.
- Verified Armor_1141_Upper and Npc_Zelda_Opening actor simulation, wind on/off response and source isolation; Zelda animation playback and simulation also pass the OpenGL viewport check.
- First-pass limitations: raw MeshCodec meshes, TexToGo textures, retargeted animation sets, model-variation shaders, game state machines and native cloth-reaction wind are not implemented. Decoded BFRES and the matching romfs are required; missing textures use the existing fallback.

### Native TotK Preview and Physics File Drops - 2026-10-07

- Added an explicitly experimental, native BPHCL steady-state simulation path. It does not convert the document to HKCL. Supported cloths expose Run TotK preview; Stop and Space restore editing without changing authored data.
- Decode packed skin reference positions, use full bind transforms, preserve authored collision-slot order/masks and selected instance slots, and drive collider transforms from the native bone/offset map rather than names. Repeated collider references retain independent slot motion.
- Added TotK-profile mass/inverse-mass integration and a one-frame action-force accumulator, retaining the native rest-bend cutoff. BotW integration defaults remain unchanged. Verified setup, damping, timestep rescaling and collider driving against a read-only TotK executable analysis.
- Explicitly block unsupported kernels, ambiguous/default-state programs, missing movement inputs, multiple simulation configurations and unverified adaptive multi-substep behavior. State transitions, game actions/wind, world collision, virtual contacts and full TotK pinch recovery remain unfinished. Shared contact/deformer kernels are still preview approximations, not game-parity claims.
- Audited 90 native cloths, including Armor_1141_RaulSkin_Upper's SimulationMesh_Ribon_1141 and all Item_Parasail tails, with 360 animated-input frames each and no numerical resets. See `docs/TOTK_SIMULATION.md`.
- Added cross-platform file drag-and-drop for HKCL, BPHCL, BPHYSSB, BPHHB and HKRG in the main window. Uses the normal loader and unsaved-change confirmation; unrelated/multiple files, active transforms and busy conversion are rejected. Canceled/failed drops retain the current document.

### Cloth Conversion Fidelity - 2026-10-07

- Audited the correct `Armor_1141_RaulSkin_Upper` / `SimulationMesh_Ribon_1141`, plus `Item_Parasail`. Verified original masses, pins, local softness, four-particle bend weights/curvature, and repeated execution passes on Wii U and Switch exports.
- Fixed distinct same-family constraint sets overwriting one another. Local-stiffness runs stay adjacent to their source set, and each execution entry maps only to its own runs, including repeated/collision passes.
- Preserve every simulation program and mesh-to-bone output operator instead of only the first. Distinct per-state simulation configurations are now rejected rather than silently flattened.
- Confirmed TotK's runtime bend-height cutoff from the executable. The BPHCL diagnostic preview now honors it; conversion displays a per-cloth warning because BotW's HKCL bending class cannot encode that conditional behavior. No speculative stiffness/mass retuning is applied.
- Expanded synthetic and native audits: 90 cloths, both output platforms, moving-anchor constraint probes, source-file preservation, and UI warning coverage. This is not native-game parity certification; see `docs/CONVERSION_RIBBON_2026-10-07.md`.

### Zelda Dress Helper Evaluation - 2026-10-05

- Fixed BotW helper outputs within a group using stale parent poses. Outputs now update their working hierarchy in resource order, and dependent drivers sample/cache rotation and translation on first use instead of before all outputs.
- Distinguished cloth animation suppression from actual cloth-deformer outputs. Helper-driven ancestors of cloth bones remain active during actor simulation instead of being incorrectly suppressed with the simulated bones.
- Added a chained-helper known-answer regression and a native `Act_Call_Hello` GPU comparison (animation only, helpers, simulation) with skirt-root preservation assertions. The reproduced exposed-calf problem improves without changing actor files, weights, particle masses, or collider sizes.

### Simulation and Collider Audit - 2026-10-05

- Corrected tapered-capsule contacts using BotW Switch's tangent-cone and spherical-end equations. The previous centerline/radius interpolation could miss contact and push in the wrong direction. Ordinary capsule behavior is unchanged.
- Tapered translucent surfaces and wireframes now meet at tangent rings and match the contact shape. Collapsed or nested endpoint spheres use the enclosing sphere as a safe preview fallback.
- Migrated the preserved native cloth, moving-contact/friction, skin-reference, bone-deformation and helper-driver equation checks into the regular Avalonia suite. Added tapered side/cap/endpoint-order/degenerate and display-surface regressions.
- Added optional 600-frame actor simulation audits, including actors embedded in TitleBG. Wii U/Switch Bokoblin and Zelda each passed two animation runs with zero numerical resets; Zelda includes 12 helper groups. These are stability/regression checks, not captured-gameplay parity. See docs/BOTW_PHYSICS_RESEARCH.md for remaining gaps.

### BotW Preview Shader and UV Binding - 2026-10-05

- Integrated the supplied experimental fragment shader as BOTW.frag for BFRES materials referencing uking shader archives. Other materials retain Model.frag; the supplied specialized hair/skin/eye paths remain intact.
- Read authored albedo/SPM/normal UV channel options and BFRES attribute remaps instead of always assigning UV1 to SPM/normal. Tangent generation follows the resolved normal UVs; unavailable selectors retain a guarded fallback.
- Added independent uniform caches for the two GL programs, native Wii U/Switch material tests, UV/tangent regression coverage and GPU program-switching checks. See docs/BOTW_PREVIEW.md for scope and unimplemented material features.

### Actor Texture Variants - 2026-10-05

- Actor import now reads ModelList ControllerInfo.VariationMatAnim and VariationMatAnimFrame instead of displaying only the BFRES material defaults. Shared-model actors such as Bokoblin variants select their own body, eye, clothing and SPM textures.
- Added Wii U FTXP and Switch material texture-pattern sampling, including integer key offsets, held frames, constant tracks and platform-specific sampler dictionaries. Variant selection stays fixed during skeletal animation and cloth simulation; standalone BFRES imports keep their defaults.
- Missing selected textures remain empty with a warning; unavailable variation animations retain default materials. Source actor packs, models and shared texture data are not modified. Shader-parameter variations, such as authored eye-emission colors, are not implemented by this texture-binding fix.
- Verified all four shared-model Bokoblin variants on Wii U and Switch, source hashes, pose/simulation persistence, constant tracks, missing textures and real GPU rendering of the red and blue variants.

### Avalonia Collision Workspace - 2026-10-05

- Added Collision to the main toolbar: open BotW actor packs/HKRB/HKSC or TotK polytope ShapeParam, or import source meshes. Current actor accesses embedded actor packs without manual extraction.
- Combined solid/wireframe 3D preview, mesh inclusion/materials, output selection and transform controls in one window. Meshes and transforms have separate tabs; source scale/axis/offset changes must be previewed before export. New source meshes default to Wood.
- Added validated Wii U/Switch HKRB, Wii U HKSC/SHKSC, TotK polytope BGYML, and DAE/FBX geometry output. Polytope preview shows the actual generated convex hulls. Output uses a temporary file and must reopen successfully before replacing a destination; source paths are protected.
- Fixed portable collision generator discovery when the assembly name differs from its embedded resource namespace. Imported collision platform selects the matching actor target; Switch HKSC remains preview/export-only, with an explicit message.
- Restricted regular model and animation file loading to BFRES/SBFRES. FBX/DAE/OBJ/glTF/GLB remain collision sources; Maya .anim is removed from the migration scope. Existing BFRES material rendering remains unchanged.
- Verified generated output bounds/platforms, compression, mesh exports, source isolation, invalidation and compact UI. Native Wii U/Switch tree actor packs and terrain collision load without source changes; real GPU collision rendering/orbit/resize checks pass. Generation rebuilds standalone geometry, not actor-pack registration or all original rigid-body behavior.

### Avalonia Ragdoll Inspectors - 2026-10-05

- Added HKRG Constraints, Bones and Rigid bodies tabs in the existing editor. Joint selection highlights the connected bodies and supports Frame Selected; cloth-only controls are hidden.
- Exposed existing native angular limits in radians, correction/damping factors and friction. Only changed fields are written, preserving untouched native coefficients exactly. Invalid limits and non-finite values are rejected transactionally.
- Added ragdoll bone names/local poses and sphere/capsule body names, world poses, radii and capsule-local endpoints. Bodies inherit ancestor bone edits, with scoped center-of-mass/motion-state synchronization and preserved packed transform values.
- Added Wii U/Switch native save/reload, undo, validation, rotation, inheritance, shortcut guards and compact-layout checks. Sources are hash-checked and unchanged.
- Full model-skeleton binding/retargeting, ragdoll viewport transforms and stress/glide preview remain pending. Joint/body topology and unsupported shapes remain guarded; this pass does not add a ragdoll solver or certify in-game retargeting.

### Avalonia Cloth and Helper Conversion - 2026-10-04

- Added the format-aware Convert button for HKCL to BPHCL, BPHCL to Wii U/Switch HKCL, and BPHYSSB/BPHHB in both directions. HKRG conversion is not offered.
- Ported independent per-cloth mass/constraint scale inputs, measured-profile provenance, output mass/inverse-mass diagnostics, suggested-value reset and input validation. Unmatched cloths retain source defaults; no universal scaling is introduced.
- Conversion uses detached unsaved document snapshots while preserving source identity for profile lookup. Support-to-helper export can use the imported model's rest skeleton to resolve parent spaces; unsupported native layouts remain guarded.
- Run native export off the UI thread and reopen/validate a temporary output before replacing the destination. Current/donor source paths are protected, and cancellation or failure preserves the editor's data and history.
- Added generated cloth conversions in both directions/platforms, native cloth/helper round trips, filename-profile retention, platform headers, failure preservation, dialog cancellation/validation, and compact layout checks. Linux/macOS runtime verification remains pending.

### Avalonia Selection Clipboard and Mirroring - 2026-10-04

- Added multi-selection Copy, Paste and mirrored X paste through footer icons, item/viewport menus and Ctrl/Command+C, V and Shift+V. Pasted items stay selected; each operation is one undo step and does not move the camera.
- Added X/Y/Z mirroring about the world origin or selection center for particles, collider geometry and bone positions. Bone orientations are retained; selected parent/child positions are resolved together. Collider bone bindings are not swapped automatically.
- HKCL supports particle, bone and collider copies; BPHCL supports particle copies. Bone copies preserve selected hierarchy and receive unique names. Collider copies preserve world geometry and native bindings, with collision-slot capacity checks.
- Clipboard use is restricted to the source document, cloth and item type. Recopy after another document edit; stale source indices are rejected. Constraints, triangles and new model/output mappings are not created by paste. Cross-cloth paste and whole-cloth mirroring remain pending.
- Added generated-format and local native-fixture round trips, undo/redo, packed-transform reflection, stale/context guards, shortcut focus and simulation-state checks, plus a compact layout capture.

### Avalonia Helper Editor - 2026-10-04

- Added driver-group names and input/affected roles, editable helper names, whole-sidecar base-bone choices, positions and local rotations in radians. Unrelated edits preserve authored quaternions.
- Ported Create Support, Duplicate, Move Up/Down, Mirror Group X and confirmed Delete Group with transactional undo/redo. Create Support starts from an affected graph; individual helper deletion and arbitrary graph authoring remain unavailable.
- Added bounded BPHYSSB/BPHHB curve-key editing for existing supported layouts. Input/output/tangent values can change; key counts, driver/output wiring, extrapolation flags and other records are retained. BotW linear connections are not edited by this control.
- Fixed BPHHB bone reordering incorrectly remapping curve driver-record indices as bone-table indices. Driver records and their curve references now remain stable when reordering bone names.
- Added native sidecar UI, invalid-edit, cancellation, undo, save/reload, curve-isolation and source-preservation checks with compact/desktop captures. This does not add standalone helper animation evaluation or claim new in-game Havok fidelity.

### Avalonia Particle and Collider Inspectors - 2026-10-04

- Restored explicit particle inverse mass, radius/friction editing and per-particle collider assignments. HKCL allows an independent inverse-mass value; BPHCL derives it from mass. Collision choices preserve cloth-local slot order, repeated references and unlisted mask bits.
- Added collider names, HKCL bone reassignment without snapping, shape-specific local endpoints/radius, and plane position/normal inspection. Unsupported BPHCL rebinding and tapered-shape resizing stay disabled.
- Fixed collider renaming incorrectly detecting a bone reassignment when the cloth-local binding differs from the stored collider index. Corrected local-coordinate editing for Havok transforms with packed fourth-column values.
- Added validation, simulation-lock recovery, undo, native save/reload and compact/desktop inspector checks. The full headless suite passes; Linux/macOS runtime checks remain pending.

### Long Cloth Conversion Softness - 2026-10-04

- Fixed BPHCL-to-HKCL local-range conversion discarding per-particle stiffness. Soft-only arrays previously bypassed the warning and inherited the set-wide value, often 1.0. Contiguous stiffness runs now become ordered HKCL sets, preserving each record's softness and every repeated execution/collision pass.
- Fixed Armor_177 scarf bone-plane conversion using an ordinary link array. Bone-plane equations, particle/transform references and stiffness now use the native HKCL bone-plane records.
- Applied the fixes to fresh exports and compatible template replacements. Existing HKCL files must be reconverted from their BPHCL source to recover discarded per-record values.
- Validated Parasail, Armor_1141 and both Armor_177 variants: 41 cloths, both HKCL platforms, exact local softness/order, bone-plane fields, mass/fixed preservation and finite 120-frame previews. Existing matched-file tests also pass 75 cloth conversions and 38 helper round trips. In-game motion equivalence remains unverified.

### Avalonia Constraint and Bone Inspectors - 2026-10-04

- Added inline editing for existing supported constraint rest/min/max lengths, stiffness and local-range values. Unsupported native fields remain read-only. Apply validates complete values and bend ranges, uses one undo step, and retains the selected relationship.
- Restored bone names, hierarchy selection and local Euler XYZ rotations in radians alongside position and scale. Rotations remain quaternions in saved data and simulation; unrelated edits preserve the original quaternion exactly. Parent choices exclude the selected bone and its descendants.
- Added inspector UI, validation, undo and save/reload checks for Wii U HKCL, Switch HKCL and BPHCL, plus desktop and compact-window captures. Native format authoring and simulation restrictions remain unchanged.

### Repeated Collider Simulation Fix - 2026-10-04

- Fixed simulation pose export throwing a duplicate-key exception when several cloth-local collision slots reference the same native collider ID. The viewport receives one animated pose per native ID while the solver retains every ordered slot and collision-mask bit.
- Applied the same display-only deduplication to ragdoll constraint guides. WinForms simulation timer failures now stop simulation and use the sanitized error dialog instead of escaping into the framework JIT dialog.
- Added a shared synthetic regression for masks selecting either repeated slot, disabled masks, animation and reset. Validated 120 simulation frames per cloth for 30 cloths across Armor_1006_RaulSkin_Upper, Npc_Zora_AncientSage, Priest_Boss_Normal and Armor_005_Head in both implementations. Authored state and input file hashes remain unchanged; no reconversion is needed for this preview crash.

### Avalonia Selection, Constraints and Viewport Theme - 2026-10-04

- Preserve all selected particles, bones or colliders and the active row across same-context refresh/undo/redo. Empty selections stay empty, and selections do not leak into another document, cloth or tab.
- Require two explicitly selected particles for a link, or three for a triangle; single-particle and oversized selections no longer silently pick/ignore endpoints. Constraint dialogs reject missing, fractional or identical endpoints and restore fixed-endpoint/zero-stiffness warnings plus a Cancel button.
- Extend light theme to both CPU and GPU viewport backgrounds and grids, with contrasting particle, bone, collider and transform-guide colors. Theme changes do not alter the camera, geometry or material shaders.
- Add multi-selection and constraint-dialog regression checks, theme geometry invariance checks, and GPU light/dark pixel assertions. Record the remaining WinForms parity gaps in `docs/AVALONIA_MIGRATION.md`.

### Avalonia Transform Constraints - 2026-10-04

- Undo/redo shortcuts no longer frame all or change the camera. Using undo/redo during a transform cancels its uncommitted preview before applying history.
- Added red X, green Y and blue Z transform guides through the selection pivot for move, rotation and scale. View-aligned axes use a small ring instead of an invisible line.
- Shift+X/Y/Z excludes that axis for planar movement and scaling; rotation stays within the plane about its normal. Two guides show the unlocked axes. Repeating the same constraint clears it.
- Planar movement follows the pointer at oblique angles, with a bounded fallback when the plane is viewed edge-on. Added real-input regression checks for all constraints and undo/redo camera preservation.

### Avalonia Smooth Transforms and Attach - 2026-10-04

- Replaced native document restore/write/preview rebuilding on every pointer event with a cached geometry-only transform preview. Native edits and constraint rebasing now happen once on confirmation; cancellation restores cached visuals without reparsing.
- Particle/collider dragging no longer re-skins the visual model. Bone skinning requests are coalesced to at most 60 updates per second. Removed the duplicate document refresh at transform completion.
- Corrected axis-constrained translation at oblique camera angles and moved collider rigid origins along with their endpoints, preserving rotation pivots and local shape coordinates.
- Restored Attach to Bone with a searchable picker, selection retention and undo. Particle groups snap to the chosen bone using the active item as a fixed anchor; existing skin weights are retained. Bone groups snap without reparenting. HKCL colliders snap and rebind while preserving group spacing. Native BPHCL collider rebinding remains unavailable.
- Added cached-preview/native-commit parity tests, immutable baseline checks, native attachment roundtrips, oblique-axis dragging, picker cancellation/search, and a local old/new preview-cost comparison.

### Avalonia Editor Usability - 2026-10-04

- Fixed cloth double-click navigation by preserving list item containers during selection refresh.
- Removed Reset view and Axis views toolbar buttons; Frame All, Frame Selected, zoom, and the clickable 3D axes remain. Reset is still available in the context menu.
- Added theme-aware window/dialog surfaces and lighter outlined buttons. Numeric fields no longer show spinner arrows; bone names use wider, resizable rows with ellipsis and full-name tooltips.
- Restored Toolbox/WinForms-style bone joints and tapered links, using animated position/axes and selected-bone highlights.
- Added active-tab bone/collider picking and box selection. G/R/S preview move/rotate/scale, X/Y/Z constrain, Enter/left-click confirm, and Escape/right-click cancel. Each committed transform is one undo step and particle edits update simulation reference geometry.
- Fixed multi-item bone/collider deletion. Supported HKCL bone deletion reparents surviving children without changing world poses and remaps supported remaining references. Referenced, shared, or unsupported skeleton layouts are rejected with a visible reason. Native BPHCL bone deletion remains unavailable.
- Added real double-click and keyboard/mouse tests, transform isolation/undo checks, parent-child inheritance, bone deletion save/reload, and light/dark rendered layout checks.

### Avalonia Standalone Animation Playback - 2026-10-04

- Added Import animation for BFRES/SBFRES skeletal resources and FBX/DAE/glTF/GLB clips. The editor now reuses the actor timeline, clip selector, play/pause, first-frame control and Loop checkbox.
- Normalized imported time units to a 30 Hz transport. Settings retains 30 FPS half-speed and 60 FPS normal-speed playback. Space plays/pauses when an editor animation is selected.
- Animated input bones drive HKCL particles and colliders. Cloth-owned tracks are suppressed for simulation inputs, and solver output overrides the visual cloth bones without feeding back into the next animation sample.
- Scrubbing and clip changes reset the preview pose; animation loops rebase simulation inputs. Stop simulation preserves animation playback; Back pauses both. Failed animation imports preserve the current clip and model.
- Added generated keyframe, speed, transport, loop-rebase, model/physics isolation, native BFRES and live GPU playback checks.
- Maya ASCII .anim remains pending: its original importer requires the Windows Autodesk bridge. This pass does not enable native BPHCL simulation or standalone helper graph evaluation.

### Avalonia Smooth Navigation and Model Binding - 2026-10-04

- Halved the fixed ground grid's width and depth, retaining its 0.25-unit cells.
- Axis view changes now ease over 240 ms along the shortest yaw path. Manual navigation interrupts the transition. Frame Selected smoothly centers and zooms to particles, bones, or colliders, including single items.
- Added translucent, lightly shaded collider surfaces alongside the existing outlines. The viewport context menu can switch back to wire-only guides. These are x-ray preview surfaces; collision solver behavior is unchanged.
- Retained skin weights for FBX/DAE/glTF imports and reused native BFRES posing. Matching model bones now follow cloth bone edits and HKCL simulation output, preserving imported bind offsets and child inheritance.
- Standalone simulation now updates visible bone/collider poses as well as particles. Stopping restores edited poses; undo, redo, Back, and document swaps retain the binding baseline. Visual posing never changes saved physics.
- Added weighted-format, real BFRES, full editor binding, transition, frame-selection zoom, and collider surface checks.

### Avalonia Grid and Collider Guides - 2026-10-04

- Removed zoom-dependent grid spacing and camera-dependent extents. The ground grid now has fixed 0.25-unit cells, fixed world bounds, and constant-width lines.
- Replaced the two-sphere capsule placeholder with oriented sides and rounded end caps. Tapered capsules respect both radii; spheres render once; point and plane guides are handled separately.
- Fixed collider framing to include both endpoint radii and plane/point guides. Degenerate capsules and zero plane normals remain drawable.
- Added zoom/framing grid-invariance checks and collider geometry/rendering regressions.

### Avalonia Actor Viewer and World Grid - 2026-10-04

- Fixed the camera-relative grid. Ground lines now stay on the world XZ plane, share the model/physics camera transform, and remain visible with GPU models. Adaptive world-unit spacing keeps navigation readable.
- Added Wii U/Switch BFRES and SBFRES model loading, including adjacent Tex1/Tex companions and packaged eye glint. Managed GX2, Tegra surface addressing, and BCn decoding replace the Windows-only texture bridge.
- Added the Actor catalog with search, remembered resource folders, cloth/helper filtering, and TitleBG archive discovery. Selected actors open in the main viewport, not another viewer window.
- Actor mode reuses original resource resolution, skeletal animation, helper evaluation, cloth binding, and loop-rebase logic. Editing controls are hidden, model bones start hidden, mesh visibility remains available, and Back preserves the separate editor document.
- Added actor play/pause, timeline scrubbing, Loop, and 30/60 playback settings. Animated GPU poses reuse textures and buffers.
- Added world-grid projection checks, real Wii U/Switch actor tests, catalog/TitleBG checks, and live animated GPU regression checks. Linux/macOS runtime verification remains pending.

### Avalonia Cloth Tuning and Preview Options - 2026-10-03

- Restored the editor footer with Undo/Redo, Mass scale, and Cloth settings. Mass scaling targets selected dynamic particles or all dynamic particles when selection is empty, updates inverse mass, preserves fixed particles, and supports undo for HKCL/BPHCL.
- Added HKCL gravity, damping, collision tolerance, and motion-transfer editing using the existing native validation and writer. Invalid ranges remain in the dialog without partial writes; BPHCL cloth settings remain unavailable.
- Added live preview Options beside Wind: direction, strength, gustiness, gravity scale, playback speed, and solver passes, with sliders, numeric values, and reset. Close retains options; Cancel/title-bar close restores them. Preview options never dirty the native document.
- Added fixed 60 Hz preview scheduling so accelerated playback is not truncated by the solver's per-call time cap. Catch-up work is bounded after a stalled UI.
- Fixed partial particle commits in the Avalonia inspector: changing one particle no longer discards other fixed anchors or corrupts aggregate mass/radius. Position edits also synchronize HKCL constraint rest lengths and animation references before saving.
- Added tuning round-trip, mass/inverse-mass, timing, dialog cancellation, selection-retention, and simulation-control recovery tests.

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
