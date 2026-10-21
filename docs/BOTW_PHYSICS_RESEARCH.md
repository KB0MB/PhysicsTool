# BotW Physics Research

## Zelda Dress Follow-Up - 2026-10-05

The reported exposed calf reproduces in `Act_Call_Hello` with simulation both enabled and disabled. Isolated renders show that this cannot be attributed solely to collision response. Zelda's skirt helper group drives 18 bones, including parents of cloth-deformed bones and `Skirt_3_C_B`, which is also a driver. The native cloth layer writes only a subset of the skirt bones.

The previous helper evaluator calculated every output against the same initial world-pose dictionary. A later child therefore failed to inherit an earlier helper-parent output, and all driver measurements were taken before the output loop. Preserved Switch analysis of `0x71018f0e90` shows per-output transform writes via `0x7101cb7890`; connection routines `0x7101cb1d10` / `0x7101cb1fb0` request rotation (`0x7101cb2500`, cache flag 0x18) and translation (`0x7101cb2b50`, cache flag 0x19) when first needed. The evaluator now applies outputs to a detached working hierarchy in resource order and caches those two measurements separately on demand. Input/rest bone objects remain untouched.

Actor animation suppression previously included inferred cloth ancestors and reused that set to suppress helper outputs. Those are different responsibilities: helpers on non-deformed skirt ancestors must continue to run even if animation tracks in the cloth chain are suppressed. Helper filtering now uses the explicit cloth-deformer output set; the animation suppression policy itself is unchanged.

Regression coverage includes same-group parent/child output and downstream driver measurement, pose isolation, and native Zelda root preservation with simulation on/off. `--dress-audit OUTPUT ACTOR` captures the greeting at four frames in animation-only/helper/simulation modes. Wii U and Switch renders were checked at the affected calf. This addresses the reproduced pose, not every possible animation intersection; no native weights, collision dimensions, authored physics or game resources were edited.

## Avalonia Collider and Simulation Audit - 2026-10-05

Read-only headless Ghidra analysis of the preserved BotW Switch `main` identifies tapered-capsule registration at `0x7101e0a704`, vtable `0x7102c44178`, transform/setup at `0x7101db3090` / `0x7101db21c8`, and scalar contact at `0x7101db2538` through the `0x7101db3114` thunk. The four-point entry is `0x7101db3118`. These are fresh Switch executable checks; Wii U tapered equations were not independently decoded in this pass.

The previous preview approximated a tapered capsule by projecting onto the centerline and interpolating its radius. Native setup instead derives a tangent cone between endpoint spheres. For center separation L and radii r0/r1, s = (r1-r0)/L and c = sqrt(1-s*s). Given axial coordinate z and radial distance rho relative to the first center, the cone signed distance is `rho*c - z*s - r0`, with normal `radialDirection*c - axis*s`. Cap selection uses `z + rho*s/c`, below zero for the first sphere and above L for the second. The preview now uses those equivalent scalar equations, and surface/wire geometry joins the caps at the same tangent rings. Ordinary capsules remain on their prior constant-radius path.

Native setup reports invalid tapered shapes when radii differ by less than 2 percent or their difference exceeds center separation. The editable preview tolerates these cases: equal radii reduce to a capsule; coincident or nested spheres reduce to the larger sphere. A deterministic perpendicular normal handles points exactly on the cone axis. These are deliberate finite preview fallbacks, not claims about valid authored native assets or their undefined/diagnostic paths.

The regular Avalonia suite now includes the preserved cloth/native-link/moving-contact/friction/skinning/bone-deformation/helper-driver equation checks. New tapered tests cover an analytically verified missed-contact case, sloped projection, cap branches, endpoint reversal, axis points, collapsed/nested shapes and agreement of rendered vertices with the signed-distance surface. `--simulation-audit` can additionally run native actors, including `TitleBG.pack::Actor/Pack/...` entries, without modifying source files.

Eight native runs completed at 600 simulation frames each, 1/60-second steps with the 30 Hz animation clock and repeated clip loops: Switch/Wii U Bokoblin (four cloths, 91 particles, 19 capsules) and Switch/Wii U Zelda (three cloths, 185 particles, 12 helper groups, 18 capsules, nine spheres and one plane), two clips per actor/platform. All had finite particles/model/bones/colliders and zero emergency resets. Source pack hashes were unchanged. These fixtures exercise integration and ordinary collider regressions; they do not contain tapered colliders, whose new equation coverage is synthetic. No new helper-driver equation defect was found. The native equation suite passed before and after the collider change.

Remaining gaps are complete operator/state transitions, local-range reference normals and normal-component limits, virtual collision points, broader anti-pinch recovery, native aerodynamics, and integrated collider angular motion. Existing preview safety clamps also remain. BPHCL simulation is still unsupported. Passing these checks does not establish agreement with captured gameplay, nor validate every native shape or helper rig.

## Scope

The cloth simulator remains a preview solver, not a replacement for the game's Havok runtime. These notes distinguish executable evidence from independently tested corrections. No game executable or decompiled source is distributed here.

## Capsule Contact Evidence

Read-only Ghidra analysis of the supplied BotW Switch `main` used image base `0x7100000000`. The input was 23,223,060 bytes with SHA-256 `15CFCA7B89348956F85D945FADE2E215A6AF5991ED1071E181F97CA72F7AE20B`.

- Registration at `0x7101d9f1e4` identifies `hclCapsuleShape`, size `0x50`.
- The scalar contact routine at `0x7101d9efa0` projects a point onto the capsule segment, clamps the segment parameter, and uses one radius at offset `0x40` for both the surface point and signed distance.
- The four-point routine at `0x7101d9f058` follows the same constant-radius construction.

The preview previously interpolated toward `EndRadius` even for ordinary capsules. That field can be zero when only a single radius is authored, incorrectly narrowing one end. Ordinary capsules now use `Radius` throughout. Tapered capsules retain a separate path; exact parity of that path with Havok has not been established.

## Native Helper-Bone Evaluation

The supplied Switch executable implements support-bone input measurement at `0x7101cb2500` (rotation) and `0x7101cb2b50` (translation), output reconstruction at `0x7101cb6040`, and authored transform application at `0x7101cb1550`. The preview now follows these equations instead of treating horizontal bend, vertical bend, and roll as three successive Euler rotations.

- Driver rotation and translation are measured against the sidecar's authored base pose, not the visual model's rest pose. Space `-1` uses the parent; `-2` uses model space; nonnegative indices name an explicit reference bone.
- Horizontal/vertical angles use the swung aim direction and a half-angle parameterization. Roll is the residual twist around the aim axis. Output reconstruction combines swing and twist, then applies the authored base rotation in the native order.
- Aim/up bases are orthonormalized. Quaternion sign changes do not change the measured roll. Edited base quaternions must be finite and nonzero and are normalized, matching native resource normalization at `0x7101cb5d20`.
- A connection's input attribute chooses its source channel independently of its destination output: `0/1/2` are horizontal/vertical/roll, and `3/4/5` are X/Y/Z translation.
- Packed references distinguish linear connections (type 0) from curves (type 1). Linear evaluation at `0x7101cb1d10` uses slope and intercept. Curve evaluation at `0x7101cb1fb0` uses the authored three keys, Hermite tangents, and endpoint constant/extrapolation flags. Native upper-end extrapolation uses the final key's Z tangent, not W. Double outputs dispatch both connections at `0x7101cb23e0`.

These findings also correct editing and conversion: group merging remaps both connection tables, retains model-space sentinels, and does not collapse drivers with different authored reference poses just because their bone names match. BPHHB-to-BPHYSSB conversion keeps translation attributes as `3/4/5`; older conversions could incorrectly map them to rotation attributes. Reconvert affected translation-driven sidecars from the original BPHHB.

The [BotW decompilation project's support-bone resource reader](https://github.com/zeldaret/botw/blob/master/src/KingSystem/Physics/SupportBone/physSupportBoneResource.cpp) provides additional field-layout context. Addresses above are from the supplied executable, not assumed to match other builds.

## Cloth Damping and Timestep Evidence

The simulate-operator setup at `0x7101e11a10` rescales the previous particle displacement when the timestep changes. Its damping factor is `pow(1 - globalDampingPerSecond, dt)`, with zero damping retaining velocity and damping of one stopping retained velocity. The preview previously used `exp(-damping * dt)`, which gave a different meaning to the same authored value. The preview and editing range now use the native per-second fraction.

This confirms timestep-ratio velocity preservation. The constraint and ordinary-contact paths below were investigated separately; complete runtime parity is not established.

## Native Constraint Scheduling and Projection

Further read-only analysis of the same Switch executable identified these behaviors:

- `0x7101e16124` executes the authored constraint list once per solve iteration. Each `-1` is an in-order collision call, not a terminator. Repeated set indices are intentional. `0x7101e15f30` supplies the empty-list fallback: every static set in order, then collisions. Runtime decoding previously discarded negative entries and the preview appended a collision call at the end; decoding now preserves the authored sequence.
- `0x7101e08864` (standard links) and `0x7101d7e3f8` (bend links) apply the stored coefficient times each endpoint's inverse mass. Their corrections change current positions without adding an artificial fraction to previous positions. The preview no longer clips endpoint coefficients to one or limits each link's correction to an arbitrary distance.
- `0x7101e09a3c` (stretch links) treats endpoint A as an anchor and projects only B when the maximum length is exceeded. It does not split the correction between endpoints by mass. Preview import retains both A-to-B and B-to-A records instead of discarding one as an unordered duplicate.
- `0x7101db3d68` computes adaptive stiffness with exponent `-1.725`, including runtime time/stiffness scales. With those scales at their defaults of one, the preview uses `pow(substeps, -1.725)` for supported link and bending sets, not a solve-count-dependent spring approximation. Local-range sets (native type 5) instead run on the final adaptive substep. Gameplay overrides of these scales are not reproduced.
- `0x7101e03a08` projects ordinary local-range distances and, in its adaptive mode, moves the previous position by the same correction to preserve velocity. The preview follows this behavior for its supported distance-only range path. The separate normal-limited variants still require reference-buffer normal evaluation.

## Current-Frame Bending

The flat bending path at `0x7101d7f638` uses the weighted sum of the four current particle positions. The curved path at `0x7101d7f3c8` adds a rest-curvature term along the current pair of triangle normals, scaled by their double areas and the shared edge's squared length. Both use the signed authored bending coefficient, weights, inverse masses, and adaptive scale.

The previous preview subtracted a fixed world-space rest vector, which could incorrectly resist a rigid rotation of the cloth. It now evaluates curvature in the current surface frame and honors `useRestPoseConfig`. HKCL and BPHCL preview loading preserve that mode. This is a preview correction, not a rewrite of authored constraint data.

## Ordinary Contacts and Moving Colliders

`0x7101e168a4` dispatches ordinary collider contacts in order. The scalar sphere response in `0x7101e5d1dc`, reached through `0x7101e1948c`, projects the current particle fully out of penetration. It then adjusts the previous position by the friction-scaled tangential displacement relative to the collider surface. Surface velocity includes linear motion and `angularVelocity cross (surfacePoint - bodyPivot)`. Projection itself does not translate the previous position, and this path has no separate inward-normal velocity clamp.

The preview now follows that response at each authored collision call, replacing the previous deepest-contact selection, correction caps, and generic velocity damping. It derives linear/angular collider velocity from successive input skeleton poses. Collider and fixed-particle input transforms are interpolated across substeps; the native loop around `0x7101e10c40` likewise advances collider translation and orientation during substeps. Reset clears input-motion history to avoid stale contact impulses. The preview's transform interpolation is not a full implementation of the native animation/operator pipeline.

The native pinch-enabled dispatcher at `0x7101e163bc` includes additional cached-contact stages, separate from the ordinary-contact path above.

## Opt-In Pinch Contacts

Further inspection of `0x7101e163bc`, the sphere cache kernel `0x7101e57898`, and the capsule cache kernel `0x7101e4ba7c` establishes a scoped contact path:

- The simulation flag must be enabled and more than one collider must have pinch detection enabled. Per-particle flags independently select cached versus ordinary handling.
- Enabled colliders are visited first. A particle's cache starts with priority -1. A higher-priority nearby contact replaces the choice; an equal priority invalidates it; a lower priority does not replace it.
- `pinchDetectionRadius` expands the detection region, not the physical collision surface. A higher-priority near contact that is not actually penetrating can invalidate a lower-priority penetrating contact.
- A valid cached contact projects onto the chosen surface and applies tangential friction relative to its linear/angular velocity. Ordinary, non-pinch colliders are processed afterward. Caches are cleared for every authored collision stage, not carried between frames.

The HKCL preview now imports simulation, particle, and per-instance collider pinch metadata and implements this path for sphere/capsule-only pinch configurations without anti-pinch constraint sets. Unsupported shapes or additional anti-pinch recovery sets retain the existing ordinary-contact approximation rather than dropping tied contacts without their required recovery stage. Native anti-pinch constraints and their temporal state remain unimplemented. BPHCL behavior is unchanged; this is based on the supplied BotW executable only.

Known-answer regressions cover sphere/capsule priorities, reversed collider order, equal priorities, detection margins, mask exclusion, disabled/global/particle guards, single-collider fallback, unsupported recovery fallback, moving-surface friction, reset, and HKCL metadata loading. Actor diagnostics now record actual collider geometry and pinch metadata alongside reference data.

Importantly, the tested vanilla Zelda necklace, hair, and skirt all disable pinch detection globally, per particle, and per collider. This new branch is not a fix for their remaining distortion and must not be enabled arbitrarily as a tuning workaround.

The final pinch-contact build passed the equation/animation/helper suites and 600-frame actor checks for Wii U Zelda, Switch Zelda, and Switch TitleBG GameROMPlayer. All had zero emergency particle resets; each layer's recorded mean/maximum standard-link error exactly matched its corresponding pre-change baseline. Enabled pinch behavior is currently validated by synthetic known-answer tests, not an in-game comparison.

The post-solve routine at `0x7101e11030` was also inspected: it rebuilds particle normals from triangle geometry, not transfer motion. The separately labeled transfer routine is `0x7101e7ba60`. No solve-order change was made from that investigation.

## Reference Skinning and Particle Edits

The object-space skin job at `0x7101db55ec` consumes position blocks in `controlBytes` order, with independent cursors into each blend table. Opcodes 0/1/2/3 select four/three/two/one influences; 5/6/7/8 select eight/seven/six/five. Opcode 4 does not consume a position block. It uses packed `localPs` when present, otherwise `localUnpackedPs`. The registration at `0x7101e07314` identifies the associated object-space skin operator type.

The old preview only extracted normalized weights and applied them to each simulation particle's rest point. It now retains the authored reference point per influence, transformed through the skin-to-bone and full skeleton bind matrices, including scale. Bone-space records retain their homogeneous weights. Fixed inputs, local-range centers, and directly attached particles in the stopped editor use those reference points; synthetic nearest-anchor fallback weights deliberately do not borrow another particle's reference point.

This exposed a separate editing bug: the rebase path walked whole blend tables in sequence instead of following the control stream. Zelda's skirt interleaves a one-blend block between four-blend blocks; grouping them pairs waist references with lower-dress positions. Rebasing now uses the same control decoder, transforms world-space edits back into the appropriate skin/bone space, and supports packed as well as unpacked object positions. Repeated padding slots are all updated because later native writes to the same vertex can otherwise restore its old value. Local-range references are rebased alongside MoveParticles and transition references. This changes only the requested document edits, not untouched source assets.

`ClothSkinReferenceChecks` covers interleaved block types, independent cursors, the skipped opcode, packed precedence, bind scale, homogeneous weights, and per-influence animation. Actor checks additionally move an entire cloth and an isolated final anchor, save/reload native HKCL, and compare both changed and unchanged references. The isolated edit exercises repeated padding slots. These checks cover all loaded Zelda and player cloth layers on the tested assets.

These corrections address reference fidelity and edited assets. They do not materially reduce the previously recorded vanilla Zelda skirt strain; it would be misleading to report them as a solution to the remaining contact behavior. The Zelda reference ranges inspected here have `applyNormalComponent` disabled. Native normal-range kernels were inspected (`0x7101e04358` and `0x7101e04038`), but those paths remain unimplemented pending reference-buffer normal evaluation. Transition handling at `0x7101e0ae08` is explicitly runtime-state gated, so blindly treating it as an always-on rest-pose spring would also be incorrect.

## Additional Corrections and Verification

- Zero, negative, and non-finite integration intervals do not advance simulation. Positive intervals below 1/240 second are no longer enlarged.
- Verlet displacement is scaled by the ratio of consecutive integration intervals, preventing an interval change from changing velocity by itself. Reset clears the interval history.
- Animated collider transforms are calculated once per substep and reused by its constraint iterations. Their input poses advance across substeps instead of teleporting immediately to the final pose.
- HKCL `collisionTolerance` belongs to the `hclSimClothDataOverridableSimulationInfo` child, not the outer simulation-cloth object. Settings, details, JSON, and preview metadata now use that owner. Earlier notes incorrectly identified the outer object. Settings edits now survive native save/reload; the preview contact solver does not yet implement Havok's tolerance response. TotK landscape collision tolerance is a separate field and has not been reinterpreted.
- Simulation edits reject non-finite values, invalid damping, negative tolerances/speeds, reversed speed ranges, and motion blends outside 0..1 before mutating the document.

`ClothSimulationChecks` covers timestep no-ops, variable-interval velocity, damping at 30/60/120 Hz with one/four substeps, reset, ordinary and tapered capsule behavior, and collision-mask exclusion. `SupportBoneSimulationChecks` covers native swing/twist equations, quaternion signs, authored poses, model space, independent channels, linear/curve/double outputs, extrapolation, group merging and serialization, translation conversion, and edit validation. The animation-preview diagnostic invokes both suites.

`NativeClothSolverChecks`, invoked by the same diagnostic, adds known-answer cases for directed stretch, mass-scaled standard links, unchanged structural velocity history, adaptive stiffness, flat/rest-curved bending, rigid-transform invariance, full surface projection, translating/rotating surface friction, reset history, fixed-input interpolation, final-substep local ranges, in-order collision calls, repeated constraints, and HKCL import preservation.

Actor diagnostics cover embedded-window navigation, rendering, animation/physics coordinate agreement, and Wii U, Switch, and TitleBG resource loading. They also edit a temporary real HKCL, save/reload it in its original platform format, and verify damping, collision tolerance, particle count, rejected edits, and unchanged source bytes. Zelda was tested on both platforms, and GameROMPlayer was tested from Switch TitleBG. These checks are not in-game validation.

The extended diagnostic runs 600 animation/simulation frames and requires finite mesh/particle data with zero emergency particle resets. It records authored solver settings, remaining standard-link error, and the worst links, then captures a separate sustained-simulation pose. The September 23 runs passed for Zelda on Wii U and Switch and for GameROMPlayer in Switch TitleBG. Matching Zelda assets produced matching particle-error results across platforms. Residual errors remain: Zelda's dress had about 7.9% mean and 66.7% maximum relative standard-link error at the sampled final frame; one small player chain had about 46.8% mean and 84.3% maximum. These are diagnostics of a remaining limitation, not acceptance thresholds or proof of a matching game pose.

## Remaining Work

Full operator-graph/state execution, transition constraints, normal-limited reference ranges, anti-pinch recovery/state, remaining pinch shapes, self/landscape collision, weighted mesh-to-bone deformers, and intermediate-bone hierarchy reconstruction still need work. The simple mesh-to-bone path below now has direct executable evidence. The ordinary contact equations and supported constraint scheduling do not cover all Havok branches or gameplay runtime overrides. Native helper singularities also need broader coverage. Preview speed/excursion safety limits remain, and wind is still an approximation. The supplied GhidraMCP extension was inspected, but this analysis used Ghidra headless; no active MCP server connection was available.

## Native Simple Cloth-to-Bone Reconstruction

The simple deformer kernel at `0x7101e7dea0` is called directly by the dispatch wrapper at `0x7101da48a8`. Its vtable is registered through `0x7101e07ea8`; the setup/access routines do not permute the reconstruction axes. This is distinct from the weighted multi-triangle kernel at `0x7101e7c334`.

For each triangle, the simple kernel builds an unnormalized frame with origin `(a+b+c)/3`, X `a-origin`, Y `b-origin`, and Z `cross(X,Y)`. In System.Numerics row-vector convention it multiplies `localBoneTransform * frame`. It preserves the resulting Z axis, reconstructs Y as `cross(Z,X)`, and reconstructs X as `cross(Y,Z)`, normalizing all three. The authored matrix's second row is not used for reconstruction. Translation comes from its fourth row in the same triangle frame.

BotW's simple operator has no serialized `boneAxis` field. Previously the preview's missing-field fallback selected an axis dynamically from transformed row lengths. A shared continuity helper also reversed projected axes toward the previous pose, which is not equivalent to quaternion sign normalization and could introduce a 180-degree orientation error. The HKCL simple-deformer path now bypasses both heuristics and consumes its authored transform directly, without refitting it to the skeleton rest pose. Missing-transform legacy bindings and BPHCL retain their existing fallback paths. Fully collapsed triangles retain the previous valid frame as an explicit preview safety fallback, not a claim about native degenerate-triangle behavior.

`ClothBoneDeformChecks` independently transcribes the native equations and checks authored transforms even when the skeleton rest pose differs, anisotropic stretch, shear, large rotations, continuous forward/reverse deformation, repeated pose reads, collapse recovery, and reset. The large-rotation case reproduced the old sign helper's 180-degree error before the native path was introduced.

Actor tests compare every authored output bone with these equations throughout 600 animation/simulation frames, in addition to the existing finite-data and zero-emergency-reset checks. Switch Zelda, Wii U Zelda, and Switch TitleBG GameROMPlayer all pass. At the sampled Switch Zelda final pose, the old output differed by up to 5.19 degrees for hair and 13.98 degrees for skirt; the corrected output differs by less than 0.00002 degrees and 0.000001 position units. These compare the same simulated triangles with recovered equations, not with a captured in-game pose.

Particle positions and recorded standard-link strain are unchanged, as expected for an output-only correction. The remaining dress/contact distortion is therefore not solved by this change. Broader helper-bone and skin-reference regressions still pass; their equations were not retuned in this pass.

## Authored Collider Origins and Substep Motion

The collider-velocity routine at `0x7101da3570` measures linear velocity from successive collider transform translations, not from the attached skeleton bone or the shape's geometric center. It derives shortest-arc angular velocity from the previous/target collider orientations. The complete simulation entry at `0x7101e106fc` composes the collider offset with the input transform before calling this routine. Its substep loop around `0x7101e10c40` advances the collider origin by linear velocity times the substep interval. The contact kernel measures the angular surface lever arm from this same collider origin.

HKCL preview colliders now retain their authored transform origins. Motion setup uses the transformed origins for linear velocity, and surface friction uses the current origin for its angular lever arm. Substep geometry is translated so the origin follows the native linear path instead of an arc about the skeleton bone. Preview-only origin metadata is also retained in actor overlays; native file serialization is unchanged. Legacy/synthetic colliders and BPHCL without this metadata retain the previous fallback.

The rotation path still uses quaternion interpolation between input poses. The executable advances orientation using an angular-velocity polynomial approximation and normalization. These are not asserted to be bit-identical, and full native operator/state evaluation remains outstanding.

Known-answer tests include a collider origin offset from its attached bone, moving-contact friction, linear origins at four substep fractions, preservation of the oriented local shape offset, source immutability, and reset history. Real-asset tests compare imported origins with authored collider transforms and check their preservation after native HKCL save/reload on both platforms.

The sampled Switch Zelda strain is effectively unchanged: skirt mean standard-link error remains about 7.9%, with a maximum of 66.7%. The origin correction is a verified behavior fix, not a solution to that distortion. Inspection also ruled out an apparent collider-binding matrix discrepancy in these Zelda assets: the meaningful XYZ basis/translation components agree, while unused homogeneous padding caused the larger full-matrix diagnostic difference. No speculative attachment remapping was applied.

## Preview Wind and Animation Boundaries

The wind toggle was connected, but its default uniform acceleration of 2.2 produced little visible response. The preview default is now 12, with the existing 0..50 editor slider and a new live Actor-viewer strength field. Disabling wind removes the force without resetting existing motion. This is deliberately preview tuning, not a recovered BotW aerodynamic or weather model.

The Actor transport previously wrapped its final UI-tick frame before consuming pending physics steps, clamped earlier catch-up samples to frame zero, and reset cloth on every wrap. A wrap occurring without a full pending physics step could also lose the reset flag. The editor had the opposite problem: an end-to-start input jump could enter the solver as collider/transfer velocity. Actor playback now advances animation at each fixed simulation step and wraps at that step. Ordinary non-simulation playback uses elapsed time without a per-tick 0.1-second truncation.

Loop rebasing transforms current and previous particle positions together through their attachment-pose change, preserving deformation and rotating velocity rather than restoring reference particles. Unattached particles use the nearest available attachment for this boundary-only operation. Fixed inputs, collider-motion history, transfer-motion history, and cached deformer frames are synchronized. Wind phase and solver integration history are not reset. Explicit Reset/Seek still resets intentionally. This is viewer continuity handling, not a claim about native gameplay transitions or complete BAS state execution.

Known-answer tests check identity and translated/rotated boundaries, retained deformed positions and velocity, fixed anchors, no repeated transfer impulse, stronger wind acceleration, live toggling, and zero strength. Actual Actor controls are tested at both speed settings with/without simulation, across a boundary split between two half-ticks and a catch-up tick, and for preservation of simulation history and propagation of wind settings. Wii U Zelda, Switch Zelda, and TitleBG GameROMPlayer pass these checks plus the 600-frame finite-data, zero-emergency-reset and native deformer suites.

## Animation Clock Verification

The public [VFR implementation](https://github.com/zeldaret/botw/blob/master/src/KingSystem/System/VFR.cpp) and [layout](https://github.com/zeldaret/botw/blob/master/src/KingSystem/System/VFR.h) helped identify the game clock. Its published 1.5.0 function addresses do not match the supplied executable and were not treated as local evidence. Following the local clock singleton at `0x7102d1e170` locates setup at `0x710183ec94`: a combined store to offsets `0x150/0x154` writes interval 2 and frame rate 30; the store at `0x710183eca8` writes `0x3d088889` (1/30 second) to offset `0x158`. Local FrameCtrl parameter setup at `0x7101b4d500` initializes Rate to 1 and has separate StartFrame/EndFrame parameters.

This supports the viewer's existing 30-frame/second base clock; it does not justify changing clips to 60 animation frames per second just because the viewport updates at 60 Hz. Settings retain the requested semantics: 60 = 1x and 30 = 0.5x, now visible in the labels. FBX uses its imported source rate. Actor-specific BAS rates, slow-time state, and transition blends can still make the game's playback differ from a raw clip at its base rate.

## Native Input Sampling Across Substeps

Instruction-level inspection of the supplied main at `0x7101e10dd0` confirms a separate fixed-particle path in the simulation entry. The loop computes `(substep + 1) / numSubsteps`, blends the saved previous and evaluated target world positions, and writes the same result to both position arrays at `0x7101e10e24` and `0x7101e10e28` before integration. It does not interpolate a skeleton quaternion and re-skin a fixed vertex for each substep. Those constructions coincide for simple translation but differ under rotation and multi-bone blending.

The preview now snapshots fixed-particle start/target positions before sampling collider input poses. Each substep linearly interpolates those endpoints and synchronizes both Verlet positions. Local-range references are also evaluated once at the frame input pose: `0x7101e03a08` reads its reference mesh buffer from the operator context, rather than re-evaluating a skeleton pose within the simulation loop. Adaptive final-substep projection and its velocity-preserving correction are unchanged. An HKCL-only preview flag selects this behavior; BPHCL keeps its prior input sampling. No native asset bytes or authored substep settings are changed.

A two-bone rotating-anchor regression failed before the solver change (about 0.0032 world units of endpoint error in its first frame) and passes afterward. Independent equation checks cover repeated frame changes, fixed-input velocity history, reset, local-range buffer timing with one/four substeps and adaptation on/off, and legacy sampling. Loop-rebase tests run in both sampling modes. Actor import/save/reload diagnostics require native input sampling to remain enabled for every HKCL layer.

The inspected Zelda necklace, hair, and skirt each author one substep. Their remaining strain is therefore not expected to improve from this multi-substep correction; it must not be described as a dress/contact fix. The broader ordinary-contact, operator/state, and runtime-override limitations above still apply.

The completed Release passes the equation/helper/skinning suites and 600-frame actor tests for Switch Zelda, Wii U Zelda, and TitleBG GameROMPlayer, with zero emergency particle resets. All real HKCL layers retain the native-input flag through save/reload. These sampled player cloths also use one substep; every recorded mean/maximum standard-link strain value matches its prior wind/loop-fix baseline exactly. The rendered Zelda viewport was checked after sustained simulation. These are regression checks, not a comparison against captured gameplay.

Collider angular-update constants were also read directly from the executable: `0x710260d248 = 0.405284702778`, `0x710260d81c = -0.822947978973`, `0x710260d570 = -0.130529001355`, and `0x710260d774 = -0.0444080010056`. The simulate loop uses these in its normalized angular-velocity update. This pass does not replace collider quaternion interpolation: reproducing that path also requires carrying its integrated orientation across frames and testing moving-contact velocity against that history. Merely inserting the polynomial into the current interpolation would be incomplete.

## Wii U Decoder and Review - 2026-10-03

The local Wii U v208 RPX has SHA-256 `BA58DA5B95CE929E005D058CEB08B9B2788D1AB2BBC8A6C189BBADCA0BB34D30`. Its integer registration code can be inspected with ordinary PowerPC, but that language misdecodes paired-single solver instructions. Results produced with that decoder are not evidence for solver equations.

Built and installed the official [GhidraRPXLoader](https://github.com/Maschell/GhidraRPXLoader) v0.9.0 (`ab05f572e4545bc439a40234c79308c1a18bf4c3`) for the local Ghidra 11.0 installation. Only Gradle build compatibility was adjusted. The new, separate project uses `PowerPC:BE:32:Gekko_Broadway_Espresso`; existing Switch and Wii U projects were not modified. Direct RPX loading failed in `Cafe_ElfExtension.processRplFileInfo`, so analysis uses the existing expanded ELF. Some processor-register metadata warnings remain.

Registration and vtable tracing locate standard-link solve at `0x04237354`, stretch-link solve at `0x04238618`, and local-range dispatch at `0x04232c18`. Their respective vtables are `0x10422970`, `0x10422af4`, and `0x104225e8`. Standard/stretch tail paths call `0x0423a084` and `0x0423a2a0`; local range selects among `0x042362b8`, `0x04236440`, `0x042365b4`, and `0x0423674c`.

The Espresso language decodes the paired-single instructions, but automated C output still contains GQR-dependent expansions, unreachable-block warnings, and stack artifacts. These addresses establish research targets, not verified Wii U/Switch equation parity. No mass multiplier or solver-equation changes were inferred from that output.

This review indexes preview constraints by set while preserving iteration order. The equation, helper, skinning, and animation regression suites pass. A fresh 600-frame Wii U Zelda run completed simulation with zero resets for its 18-, 44-, and 123-particle cloths. Mean standard-link relative errors were 1.12%, 1.49%, and 7.90%; maxima were 2.46%, 9.90%, and 66.70%. The final OpenGL nonblank screenshot assertion failed, so this run does not establish visual correctness. These metrics compare simulated lengths with authored rest lengths, not captured gameplay.

Remaining priorities are complete operator/state evaluation (including transitions), local-range normal-component limits and reference normals, virtual collision geometry, broader contact/anti-pinch behavior, and native aerodynamic response. Current wind remains a preview acceleration. Safety clamps and collider angular interpolation also differ from a complete native runtime. Support-bone equations were not changed or newly verified against Wii U in this pass.

BPHCL authoring remains separate from these HKCL preview changes. Link creation respects default-all execution and directed stretch semantics; bend links can be removed without rebuilding or losing surviving record values. Null arrays no longer retain pointer fixups. The initial regression coverage passed on 270 cloths from 55 files. Subsequent topology authoring is described below; model-skin bindings are still not automatic.

## Constraint Authoring Inventory - 2026-10-03

Read-only Ghidra headless scans of the supplied BotW Switch and TotK executables found the following class names. These addresses establish class presence, not complete solver semantics or safe authoring of every family. No active Ghidra MCP connection was used.

| Family | BotW string address | TotK string address | New authoring |
| --- | --- | --- | --- |
| Standard link | `0x710264e36e` | `0x71037dabc2` | Two-particle distance link |
| Stretch link | `0x7102642de8` | `0x7103746a15` | Directed A-to-B limit |
| Bend link | `0x710267d105` | `0x710376ec84` | Two-particle minimum/maximum range |
| Bend stiffness | `0x7102638305` | `0x71037043e1` | Inspect only; ordered four-particle geometry required |
| Local range | `0x710263d991` | `0x71037982fe` | Inspect only; reference-buffer bindings required |
| Bone planes | `0x7102658677` | `0x71037362a1` | Inspect only; transform-set and plane bindings required |
| Transition | `0x7102648812` | `0x710371ce6d` | Inspect only; transition runtime setup required |
| Volume | `0x710262800c` | `0x710376ed50` | Inspect only; weighted frame/apply records required |
| Compressible link | `0x710263840a` | `0x7103766fa4` | Preview supported; creation not exposed |
| Anti-pinch | `0x7102672c48` | `0x710376ec6a` | Inspect only; recovery runtime state required |

The native constraint creation path writes reflected 12-byte standard/stretch records or 20-byte bend records, preserving native coefficients rather than applying a universal scale. Missing TYPE definitions are merged from the existing reflection catalog. A new static set is added to each applicable explicit simulation schedule before its first collision call; an empty schedule retains the all-sets fallback.

Particle deletion remaps supported constraint records, stored positions, triangle indices, fixed bindings, and simple mesh-to-bone triangle offsets transactionally. Nonempty packed virtual-contact arrays reject deletion: their ITEM counts, not just hkArray header counts, determine whether data is present. Unsupported operators and constraint families also reject without changing the source. Ordinary triangle creation does not build virtual-contact points or output-bone bindings. Collider deletion removes all matching slots in the selected cloth while retaining outer objects that may be shared elsewhere.

Manual donor-free drafts serialize in all three cloth formats, but are not game-ready actor resources. One Root bone and simulation buffers are present; model skinning and output bindings remain to be authored. Structural validation and save/reload tests do not establish in-game behavior.

The expanded authoring suite passed across 270 cloths in 55 BPHCL files. It covered 808 successful particle-deletion cases and 100 unsupported-layout rejections with byte-equivalent captured state, along with constraint creation, collider deletion, local-range editing, schema validation, and save/reload. Blank-draft round trips passed for Wii U HKCL, Switch HKCL, and BPHCL. Editor undo/redo, the BPHCL simulation guard, animation/solver regressions, and shared HKCL/BPHCL edit tests passed. The two new dialogs were checked with rendered WinForms captures; this does not replace interactive or in-game validation.
