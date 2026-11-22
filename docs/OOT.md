# OoT Physics Editing and Conversion

OoT BPHCL files with Havok SDK tag `20250200` open through **Open Physics**.
Double-click a cloth to inspect its rest-pose particles, bones, constraints and
colliders. Particle mass, radius and friction, existing reflected constraint
values, and sphere/capsule radii can be edited with undo/redo. Inverse mass and
aggregate mass/radius are updated together. Scalar edits preserve other native
data without rebuilding the container. Shared constraint/shape records affect
all particles or colliders that reference them.

Select particles in the viewport or list, then use G/R/S or edit XYZ and choose
**Apply position**. Supported layouts rebuild particle poses, normals, link rest
lengths, surface bends, local ranges, radii and skin/bone bindings together. Masses
and bone rest coordinates stay unchanged. A transform is previewed without
rewriting the file; confirming records one undo step, while Escape cancels it.
Volume constraints, bone-plane constraints and unsupported skin mappings block
geometry edits without changing the document.

**Add Particle** creates a free particle at the selected particle's position,
with its own skin reference and the same bone weights. Move it before linking it.
Select three particles and choose **Add triangle**, or press F, to add a face and
edge links. Selection order controls its winding. This extends existing native
initializers and constraint sets; it does not generate new bone weights or new
virtual collision samples for the face.

In **Bones**, edit XYZ and apply, or use G to move the selection. Particle positions
follow when **Particles follow bones** is enabled. Rest-shape bindings are updated
in either case, and colliders follow the bone hierarchy. A bone that moves a
collider shared by another cloth is protected until unique-collider editing is
available. The loaded BFRES stays unchanged while cloth bones are edited.

Particle/triangle deletion, fixed-anchor changes, bone rotation/scale and merging
are still disabled. OoT helper-bone conversion is not included.

## Animation and Simulation

**Import animation** accepts BFRES, SBFRES and compressed `.bfres.zs` skeletal
animations. Keep compressed resources in the dump so external names can be
resolved. The actor loader also reads the actor's referenced animation sets;
retargeted sets are skipped with a notice. Playback, seeking and looping use the
normal timeline. Cloth particles and colliders follow the animated skeleton even
when simulation is stopped.
OoT clips play at 60 animation frames per second. Settings controls the viewport
update rate (30 or 60 FPS), not animation speed or the simulation timestep.

**Run OoT preview** enables an experimental solver for supported native cloths.
It uses the file's masses, constraint schedule, skin inputs, bone deformers and
collider bindings. Volume constraints fit their authored weighted shape frames;
bone-plane constraints follow their bound bones and keep particles on the allowed
side, with native velocity compensation when the substep setup requires it.
Direct skeleton mappings with an empty skin subset are supported; unverified skin
layouts remain blocked instead of being silently ignored. Authored virtual
collision samples on edges and triangles now check the cloth between its real
particles against enabled body colliders. Their owner corrections and triangle
directions follow the inspected native kernels. Packed and landscape
virtual-point layouts remain guarded. Wind now uses the native simple-wind drag
response with the cloth's density, drag factors and particle velocity. Weather,
actor-specific wind inputs and lift behavior remain unverified; the Wind control
supplies a preview air velocity, not the game's weather. This is not validated
Havok 2025 game parity. Playing or simulating does not modify the physics file.
Simulation and preview wind keep running when animation is paused or no animation
is loaded. Stop the simulation separately to freeze the cloth.
Supported authored anti-pinch sets now recover toward their animated reference
vertices, with per-substep timing and sphere/capsule overlap detection. Supported
edge and triangle collision samples now work alongside recovery. Animated references
can pass through ordered scratch-buffer copies and gathers, including chained or
shared position-buffer redirects and shadows that borrow their original positions.
Supported sphere/capsule contacts choose their collision side from the initial
cloth surface and retain that side during simulation. Their edge and triangle
samples use the owning particle's surface normal for this check. Landscape collision,
uninitialized reference storage and simulation-buffer feedback remain guarded.
Without an authored recovery set, overlapping body colliders retain ordinary
contacts. Collision masks and fixed anchors remain respected.
The supplied 1,134 cloth entries all disable pinch detection and contain no
anti-pinch constraint sets; recovery is not automatically added to them.

Wind treats the cloth's surface density as authored, rather than rescaling its
particle masses. If density scaling is disabled in the file, the response uses
total cloth mass instead. Normals affect wind exposure when the cloth requests
them; otherwise the native action uses its constant exposure factor. A preview-only
limit prevents extremely light cloth from overshooting air velocity in one step.
The native simple-wind action does not consume the lift coefficient, so this
preview does not invent a lift force from it. Turning Wind off disables this action.

## Actors and Helpers

Choose **Open Actor > OOT** and select the OoT `romfs`. Its path and selected tab
are remembered separately from BotW and TotK. The browser resolves active physics
references and inherited actor parameters without loading cloth contents, models
or animations during scanning. Results are cached between launches; changed packs,
shared parameters and referenced physics resources are checked on the next scan.
Pick an actor, then its cloth or helper
resource. **Load model** also loads the actor's referenced BFRES when available.
The editor's **Load model** button accepts `.bfres.zs` directly. Keep it within the
dump so the matching external string table, `Tex` companions and `UnifyTex` aliases
can be found. Models use Toolbox-derived OoT material graphs, UV routing and ASTC
textures. Lighting is approximate; meshlet-only geometry is not supported yet.

Actors open in a read-only viewer with all referenced cloths together. Each supported
cloth runs its own solver against the shared animation and helper pose; unsupported
layouts are listed in the preview warning. Editing controls are hidden.
Back restores the previous editing document. Export
provides the original BPHCL or helper BGYML; use Open Physics to edit that copy.

Referenced helpers are evaluated before cloth inputs, in the rest pose and during
animation. Supported channels include bend, roll, translation and scale with
Hermit2DSplit response curves. Missing bones and unsupported inputs are reported
and their helper outputs are left unchanged. This evaluator is experimental and
has not been compared against the game's runtime.
Children of a helper, including collider bones, inherit its evaluated pose even
when that helper samples a descendant as its driver.

Helper BGYML files can also be opened with Open Physics. Driven bones and drivers
have separate lists. Edit base position/rotation, bone references, static channel
values, driver connections and response-curve values. Rotation edits use
radians and are saved as quaternions. Unknown parameters and hashed attributes are
preserved. Save As writes the BGYML to replace in the actor pack yourself.

**Connections and curves** adds or removes connections on a driven bone. Choose
its output channel, driver and an input template from an existing connection.
The template preserves a native input-attribute hash; its displayed source bone
and output channel identify the example, not a decoded name for that input.
New connections start with a constant response that you can edit before applying.

Hermit2DSplit curves have a graph and individual input, output and tangent fields.
Insert a key at a new input position or remove an existing key. Insertion inside
the curve preserves its Hermite shape; outside the range it extends the endpoint
tangent. Inputs must increase, and at least two keys must remain. Apply commits
the connection window as one undo step; Cancel discards its changes. Removing the
last connection restores a static zero response (one for scale channels).

New helper groups/drivers and unknown curve formats are not implemented.
Keep an original copy and test changed resources in-game.

## Experimental Conversion

Open a TotK BPHCL or BotW HKCL, then choose **File > Convert to... > OoT BPHCL (experimental)**.
Select the OoT `romfs` folder containing `Phive/Cloth`, then choose a separate
output file outside that dump. No game files are bundled with PhysicsTool.

The converter rebuilds data for the newer reflected layouts, relocates pointers,
maps skeleton types, and derives the new cloth-state operator masks. Source
particle positions, masses, inverse masses, constraints and bone names are
preserved. The target model must contain the required bones; this is not model
retargeting. New aerodynamic controls use neutral defaults, not a tuned OoT
wind profile. Unsupported layouts stop conversion with an explanation.
Neutral aerodynamic data includes the correct particle count and enabled constant
factors for its empty per-particle arrays. Lift and drag remain zero; surface
density does not alter the older simple-wind response. This is a conservative
initialization, not a reconstruction of the game's wind tuning.

OoT cloth can also be exported as **TotK BPHCL (experimental)** or **BotW HKCL**
from the same menu. BotW export supports Wii U and Switch. OoT-only aerodynamic
settings are not transferred; the export dialog warns about this. Cloth mass is
not secretly rescaled in the native OoT/TotK conversion. BotW conversion uses the
existing editable mass and constraint conversion settings. Helper bones, actor
wind parameters and model rigs are not included in cloth conversion.

Keep an original copy as a backup! The export is structurally checked and reopened,
but has not been validated in-game. Do not treat a successful export as proof of
identical behavior. A failed conversion does not replace an existing output.

## Validation Notes

The supplied native corpus contains 171 BPHCL files, 1,134 cloth entries and
2,141 colliders. Rest previews and all four state-mask calculations were checked
against this corpus. Scalar edit checks cover 1,134 cloths and 2,095 sphere/capsule
colliders; other collider types remain read-only. The actor catalog resolves 473
actors. All 144 unique helper files (7,014 groups and 10,862 curves) retain their
original bytes after an unchanged Apply. Geometry checks cover 1,107 supported
cloths; the other 27 are guarded. Particle/triangle creation checks cover the same
1,107 cloths. Root-bone edit checks pass on 380 cloths; 727 are protected because
their colliders are shared. Editor checks cover creation, XYZ edits and undo;
GPU checks cover textured Player_Adult, orbit and resize. Native preview checks
cover all 1,134 cloths with finite positions and animated fixed-particle/collider
attachments, including volume, bone-plane and direct skin-mapping layouts.
Their geometry-editing restrictions still apply. These are regression checks,
not an in-game accuracy comparison. Conversion checks include Armor_999_Head, Item_Parasail and
Armor_1141_RaulSkin_Upper, plus source-preservation and read-only UI tests.
Native OoT-to-TotK-to-OoT round trips also cover all 171 cloth files, including
constraint values, rest poses, masses and collider geometry. Player_Adult and
Zelda_Adult additionally cover both Wii U and Switch HKCL export and BotW-to-OoT
conversion. Added reflection layouts are checked against native TotK actors.
These structural checks do not establish matching in-game behavior or wind.

Virtual-contact checks cover 2,329 authored samples across 34 native cloths,
including Player_Adult. Synthetic checks cover owner-only corrections, winding,
scaled radii, animated colliders, fixed anchors, collision masks, center/axis
degeneracies and isolation from BotW/TotK previews. Invalid counts, references,
dictionary weights and unsupported layouts are rejected. The corpus preview run
passes on all 1,134 cloths. See [collision research](OOT_COLLISION_RESEARCH.md)
for executable evidence and the remaining pinch-recovery work.

Wind checks cover all 1,134 cloths for 120 steps without numerical resets or source
changes. Synthetic tests cover density/total-mass response, relative velocity,
surface orientation, per-particle factors, invalid arrays, fixed anchors, reset,
toggle and BotW/TotK isolation. All supplied cloths disable computed normals;
the normal-based wind path is covered synthetically, not by a vanilla fixture.

Animation checks cover 3,102 Player_Adult clips, including 114 bottle clips and
5,492 packed quaternion tracks. BFRES 10.4 can mix Euler and quaternion bones in
one animation; packed quaternion keys are decoded separately from scalar curves.
Actor tests cover all-cloth playback, timeline seeking and inheritance below
cloth-driven bones. These checks validate decoding and preview consistency, not
the game's animation state machine.

### Malon Sitting Investigation

The local `Npc_Hylia_Malon_Adult` ASB uses version `0x41b`. Its command and node
tables were inspected separately from the older ASB reader. BAEV group hashes
match FNV-1a of lowercase node GUID strings; 308 node references resolve to event
groups, including reused GUIDs. Traversing the `Act_SitChair_Wait` command from
node 690 resolves its waiting clips at nodes 696 and 699 and their event nodes
698 and 700. Their events are `FootIK`, `LimitLookIK`, `LookIK` (the `Sit` preset)
and `LookingTargetAreaExpansion`. No `OffHelperBone` or `NoFeedbackControl`
event occurs in this command's reachable branch. Elsewhere in the actor's event
file, `OffHelperBone` targets `LookIK_Ph_OFF_Ch`; that is not evidence for
disabling skirt cloth. The two referenced animation-event files contain only
`FloorContact` events.

`--oot-sit <romfs> <output>` captures frame 70 of `Act_SitChair_Wait` with raw
animation, helper evaluation and simulation separately. The authored animation
already folds the skirt around the knees. Helpers retain that broad shape;
simulation changes the apron/skirt shape considerably. Diagnostic comparisons
also retain animated driven-bone inputs, remove the optional bone-length output
policy, and allow ten seconds to settle. These checks do not establish the
in-game result. The constraint-isolation results below narrow the mechanism
without proving the underlying native/preview discrepancy. No animation-name-based
cloth suppression was added. Actor behavior outside the inspected ASB/BAEV
branch and the native solver's response still need investigation.

The executable's SDK strings independently identify Havok 2025.2, with build
paths naming `2025_2_10`. Ghidra inspection used a separate decoded copy with all
three NSO segment hashes verified. This does not establish full solver parity.

#### Native Actor Cloth Controls

Read-only executable analysis on 2026-11-10 confirmed controls outside the
cloth solver. Addresses below use image base `0x7100000000` for the locally
decoded OoT executable; they are not portable across builds.

- The animation-event handler at `0x710016dba0` recognizes `OffCloth`. Its
  branch at `0x7100171cbc` resolves a cloth name, or selects all cloths for an
  empty name. It combines the event mask with a separate actor mask before
  changing each cloth's enable bit. A second boolean argument also clears
  per-cloth transition state. This handler's existence does not show that
  Malon's sitting branch invokes it; no `OffCloth` was found in her actor BAEV.
- `OneShotClothFreeze` and `OneShotClothUnfreeze` callbacks at `0x71028c2430`
  and `0x71028c2910` set and clear controller flag `0x200000`. These are
  separate from the per-cloth enable mask.
- Malon's BSA references the musician brain. The shared musician PlayMusic
  module references `ExecuteNpcMusicianControlClothWarp`; its native callback
  at `0x7102765850` tests animation timing and requests warp mode 3 through
  `0x7100973b60`, then marks the request pending through `0x7100973d30`.
  Decoding the AINB child links resolves its gate: node 148 runs node 150
  only when `QueryASGetCurrentCommand` equals `MusicASCommand`. Malon's
  musician parameters set that command to `Act_Song`, not `Act_SitChair_Wait`.
- Cloth update at `0x71001465d0` consumes mode 3 by calling `0x7100b0f860`.
  That function clears per-cloth counters and transition values, resets
  controller motion bookkeeping, and calls `0x710014aa00` to enter state 1.
  Update at `0x7100148320` requests state 3 when an enabled cloth leaves
  state 1. `0x7100148250` advances the transition timer and eventually
  changes state 3 to state 0; the disable transition runs from state 2 to 1.
  This establishes reset/reactivation behavior, not its full particle-buffer
  initialization or the exact final animation/physics blend formula.
- `ExecuteNpcHyliaZeldaAdultControlSkirtEnableCloth` has a separate callback
  at `0x7102763ed0` that switches two cloth entries according to actor state.
  This is evidence of actor-specific policy, not a policy to apply to Malon.

Two initial string-search leads were ruled out: `DisableClothList` belongs to
`game__capture__CaptureActor`, while `IsClothDisable` occurs in horse-tack
settings. Neither establishes normal sitting behavior for Malon.

The preview does not yet execute these AI policies or the full native
activation-transition system. They are concrete parity gaps, but are not a
confirmed explanation for the sitting skirt distortion. No production behavior
was changed on the strength of these findings alone.

#### Malon Reset and Reference-Pose Follow-up

The shared schedule module's mode-3 cloth warp (node 115) is gated by
`QueryAIScheduleGetIsWarpTriggeredByOcarinaSunSong` (node 118). It is not a
general reset on entering a sitting animation. Malon's utility graph also
uses `NpcLongHair.ExecuteRequestClothWarp` with `Ph_HairBack_7`. That module
checks bone validity and a head/hair angle condition before requesting mode
3. Its selector lifecycle and actual firing during sitting remain unverified;
the graph alone is not evidence of continuous skirt resets.

Native update at `0x7100148320` selects cloth-data states 2, 1, then 0 on the
initial update counts for three-state assets. Malon's skirt identifies them
as `AnimToSimPrev`, `AnimToSimCurrent`, and `Default`. The first two run the
skin operator followed by separate gather operators into previous/current
simulation buffers. `Default` runs skin, fixed-particle movement, simulation,
then mesh-to-bone deformation. These cloth-data states are distinct from the
actor enable/transition states described above. `0x7100147e00` changes the
cloth-data state; `0x7101b1cf60` updates collider transforms and bookkeeping,
not particle initialization.

Frame-70 diagnostic comparisons now include removing colliders, helpers,
local-range constraints, link constraints, and bending constraints separately.
They are test-only ablations, not supported preview modes or proposed fixes.

- Removing colliders leaves the basic protruding-apron problem.
- Removing local-range constraints lets the apron drape over the knees.
  Removing links or bending does not produce the same correction.
- A fresh preview reset broadly retains the animated shape; the distortion
  develops over subsequent simulation steps. Both animated and excluded-driven
  inputs were tested. All six layers remain finite, with zero numerical resets
  and zero binding fallbacks in the reset series.
- Helpers change the result, but the authored `Ph_Skirt_1_Front.BendV`
  upper limit is not exceeded: evaluated 1.3487632 versus 1.745329 radians.
  Missing helper-channel limit handling is not an explanation for this frame.

The skirt has 144 spherical local-range entries, stiffness 1, normal handling
disabled, and maximum distances from 0.006344388 to 0.14999999. They reference
the object-space skin output directly at buffer 0; there is no nonidentity
shadow-buffer alias in its default state. At frame 70, helper evaluation
moves their reference centers by an average 0.0210571 and maximum 0.0476837
model units relative to raw animation. Seven center shifts exceed the
corresponding allowed distance. This measures sensitivity, not incorrectness:
the helper pose itself still needs comparison against native evaluation.

The native local-range dispatcher begins at `0x7101c47fa0`, with a zero-scale
early-out before the prologue at `0x7101c47fa8`. Its spherical kernel at
`0x7101c5cb40` transforms the referenced vertex through the buffer transform
and projects particles outside their allowed radius. Global stiffness is
multiplied by the supplied solve scale (and local stiffness for that record
layout). When per-instance constraint data exists, its nonnegative value at
offset `0x18` multiplies the authored radius. The source and value of this
runtime multiplier for Malon's sitting action have not been established.
The similarly named `EnableLocalRangeConstraint` property found elsewhere
in the executable has not been tied to this Havok path and must not be used
as evidence for a Malon override.

Current conclusion: local-range enforcement is the strongest isolated
mechanism behind the preview's raised apron, but whether its reference pose,
runtime multiplier, or another interacting native policy differs remains
unresolved. Do not disable cloth or weaken local ranges based on the animation
name. Research outputs and captures are under the local workspace directory
`Research/OOT-Malon-AS-20261110`; no game assets are added to the repository.

#### Local-Range Bind Pose and Instance Defaults

The frame-70 diagnostic now reconstructs all 144 skirt reference centers
twice: once with the model's inverse bind matrices, and once with the cloth
skeleton's inverse bind matrices, using the same posed model transforms.
The maximum difference is `3.8165595e-7` model units (mean `1.2174983e-7`).
The check rejects nonfinite differences or a maximum above `1e-5`.
This rules out a meaningful cloth/model bind-pose mismatch for this asset
and frame. Both paths still use the decoded reference positions and weights;
it is not an independent validation of native skin decoding or helper order.

`0x7101c47edc` creates `hclLocalRangeConstraintInstanceData` for spherical
local ranges. Reflection identifies the three scalars at offsets `0x18`,
`0x1c`, and `0x20` as `radiusScale`, `minNormalDistanceScale`, and
`maxNormalDistanceScale`. The constructor initializes all three to **1.0**.
Pointers at `0x28` and `0x30` retain the simulation instance and constraint
set. The instance's radius scale is clamped nonnegative in the spherical
kernel before multiplying the authored radius. No Malon-specific writer
or live sitting-action value has been established. The default alone does
not justify changing the preview's radius.

Actor-cloth initialization at `0x710014cb20` also constructs a 32-bit mask
in each 32-byte simulation record. It starts at `0xffffffff` and clears
bits for constraint type 5. The local-range constructor at `0x7101c47d30`
passes type 5 to its base constructor, confirming which constraints are
excluded from this mask. **Its consumer and purpose remain unverified.**
It may select constraints for a particular operation rather than disable
them in ordinary simulation; do not turn this initialization observation
into a solver rule. The adjacent record fields point to transition instance
data and the simulation data's transform-set definition, not local-range
sphere scaling.

The `LocalRangeDistance` configuration string resolves to the same separate
configuration family as `EnableLocalRangeConstraint` (visitors at
`0x7103805830` and `0x7103806ef0`, accessor `0x71009fb1c0`). It has not been
connected to Malon's Havok local-range instance data.

Production simulation remains unchanged. The remaining high-value checks
are the mask consumer, native skin/helper input ordering, and any actor-side
writer of the instance's sphere scaling. Static analysis and isolated
preview ablations do not yet establish the exact in-game sitting result.

#### Native Transition Constraint Follow-up (2026-11-17)

The executable now identifies the reset/reactivation particle operation, not
just the actor state setters. Constructor `0x7101b1f694` passes native type 8
to the constraint base constructor. Instance constructor `0x7101b1f4e0`
allocates per-particle captured distances, sets its timer to -1 and state to
0. Actor reset `0x710014aa00` changes that state to 1; it is not the
local-range radius multiplier.

Dispatcher `0x7101b1f820` selects six transition kernel variants beginning at
`0x7101b1f8e0`, `0x7101b20100`, `0x7101b209e0`, `0x7101b211e0`,
`0x7101b21a00`, and `0x7101b222e0`. Their common state behavior is:

- State 0: no transition correction.
- State 1: write the mapped animation-reference positions to particles.
- State 2: capture starting distances and shrink the permitted distance over
  each particle's authored animation-transition delay/period.
- State 3: hold at the animation reference until the particle's simulation
  delay, then expand its permitted distance using `toSimMaxDistance`.

For state 3 and positive solve scale, the intermediate radius is
`toSimMaxDistance * (timer - toSimDelay) / (toSimPeriod * solveScale)`.
At/after the period the native kernel exits the particle loop. This is not
a linear interpolation between the simulated and animated positions.
Partial projection has variants that also adjust the previous-position
buffer, preserving displacement/velocity, and a position-only variant.
The dispatch depends on `simInstance+0x19d`, a setup byte at
`*(simInstance+0x158)+0x30`, and the reference buffer flag at `+0x28`.
Do not assume every variant preserves velocity.

The actor timer updater `0x7100148250` converts -1 to 0 on its first tick,
then adds the supplied delta on subsequent ticks. State 3 becomes state 0
only when the timer is strictly greater than `toSimPlusDelayPeriod`;
state 2 similarly becomes state 1 after `toAnimPlusDelayPeriod`.
These states remain separate from cloth-data states 2/1/0 used to seed
previous/current simulation buffers and run the default operator program.

A read-only fixture probe of Malon's six native cloths finds periods and
period-plus-delay values of 1, simulation delays of 0, and simulation maximum
distances of 1. The skirt transition maps 160 particles. All 144 local-range
records have exactly matching particle/reference-vertex pairs in that
transition, and both use reference buffer 0.

At unit solve scale and unit local-range radius scale, the transition's
permitted sphere therefore contains every skirt local-range sphere by
timer value `0.149999991`. This compares allowed regions, not trajectories:
other constraints run between the two projections, startup history can
matter, and the actual actor state sequence is not a live-game capture.
It does not justify disabling local ranges or prove transitions cannot
affect the visual result.

The mask reader remains unresolved. A broader decompilation of the actor
cloth controller and adjacent control APIs did not establish a connection
from that mask to either these kernels or the local-range distance scalar.
The mask is still not an ordinary-solver enable rule. The production preview
remains unchanged, pending evidence for the live actor policy/reference pose.
Scripts, native traces and the repeatable fixture probe are stored outside
the repository in `C:/Users/kaede/Documents/Codex/PhysicsTool/Research/OOT-Malon-Native-20261117`.

#### Native Helper Channel Gating (2026-11-17)

Helper evaluator `0x71001b99a0` gates rotation, translation and scale
independently on the total connection count in each three-channel group
and runtime enable flags. An unconnected channel contributes its static
value when a sibling channel is connected; a wholly unconnected group
does not replace the incoming animated component. Active scale is written
to the skeleton's local scale, not directly to its world scale.

The preview now preserves undriven animated TRS components and uses static
sibling values, including channels with an empty `InConnections` array.
Previously a rotation-only helper could replace animated translation with
its base translation. On Malon's `Act_SitChair_Wait` frame 70, several skirt
bones changed local translation by approximately 0.0131 to 0.0287 units
despite having no translation connections. After the correction, their
translation differences are below 1.2e-7. No constraint stiffness or radius
was changed. Tests cover independent TRS gating, static siblings, input
isolation, helper colliders and reordered skeletons. Malon's Song of Storms
and song animation checks also pass.

The sitting simulation still shows a skirt-shape mismatch in the GPU
capture; this is a confirmed helper bug, not proof of complete game parity.
Native `0x71001b7ac0` also honors `UseBoneNamesOrderAsTopology` and lazy
driver caches. Exact equivalence of that ordering and the preview's
recursive evaluation remains to be established. Live local-range scalar
writes, mask use and actor reset/reactivation timing remain unresolved.
Additional traces and sitting captures are under `HelperReference` in the
research directory above.

#### Native Helper Traversal and Driver Caches (2026-11-17)

`0x71001b7ac0` selects authored `BoneNames` traversal when
`UseBoneNamesOrderAsTopology` is true, otherwise the driven-parameter list
order. It evaluates and commits each helper before advancing. Driver
sampling does not recursively evaluate a later helper. The preview now
uses that order independently of the model skeleton's storage order.

The driver cache at helper object `+0x68` has separate valid bits for
rotation (1), translation (2) and scale (4). The first use of a group
captures that group for the remainder of the helper update, while another
group may be sampled later after an intervening helper write. Translation
sampler `0x710030d210` sets bit 2. Scale sampler `0x7100903280` sets bit 4
and reads the model's local scale directly, without its base-bone transform.
The preview previously captured all nine channels at the first use. It now
samples groups independently, commits active rotation before translation
and scale, and retains undriven components. Focused tests cover both native
orders, forward driver references, first-use caching, local scale under a
scaled parent, and reversed model bone storage.

On Malon's sitting frame 70, old/new helper transforms differ by less than
2.7e-7 when tested at the origin and unit-axis points, with and without
cloth-driven animation suppression. This rules out these particular order
and caching differences as the visible skirt cause at that frame. It does
not validate the complete animated/helper reference pipeline.

A further scan for loads of simulation instance `+0x1c8` (instance lookup)
and `+0x310` (constraint strength array), followed by decompilation of
candidate consumers, did not identify a Malon-specific override writer.
The candidates at `0x710274df50` and `0x7103a97970` access unrelated object
layouts; matching offsets alone are not evidence of cloth overrides.
This bounded search does not prove the runtime never changes the values.
Local-range radii and stiffness remain unchanged; the sitting skirt
mismatch remains unresolved. New native traces, the comparison probe and
captures are stored under `HelperOrder` in the research directory above.

#### Outer Helper Order and Cloth Pose Handoff (2026-11-17)

The outer update paths at `0x71002aa820` and `0x71002eb398` perform model
pose work before calling helper evaluator `0x71001b7ac0`. Its alternate
mode-1 call fades helper weights down when disabled; it is not a second
normal helper pass. The evaluator refreshes its pose context from model
components with `0x71001b7040` before processing the ordered helper list.
Conditional driven-bone resets and per-driver overrides also exist, but
their activation for Malon's sitting state has not been established.
The helper constructor starts with those override modes off and initializes
per-driver override flags to zero; later actor writes remain possible.

Native cloth input transfer `0x7100141ad0` resolves model/bone bindings and
reads model world matrices, with physics-origin and optional frame-transform
adjustments. The existing sitting capture has 42 distinct local-range
reference bones; 32 are also cloth output bones. Of 144 local ranges, 141
have a positive reference influence on a bone written by the skirt's
deformer. This establishes substantial input/output overlap, not feedback:
the exact scheduling of pose refresh and cloth output writeback still needs
to be established before changing the preview's clean-reference policy.

Simulation construction `0x7101b170e0` creates per-constraint instance data
and initializes the separate strength array to 1. Both default-order
`0x7101b36740` and explicit-order `0x7101b36de0` solve loops read those
strengths; the latter executes collision solving at an order entry of -1.
This pass did not identify a sitting-specific local-range multiplier writer
or the actor mask's consumer. No radii, stiffness, pose feedback, or helper
pass count was changed. The sitting skirt mismatch remains unresolved.

Static traces and findings are preserved under `OuterOrder` in the research
directory above. This pass used executable analysis and existing capture
data, not live-game measurements; no production code or executable changed.

## Twinrova Preview Performance (2026-11-18)

`--oot-performance <romfs> <actor>` now measures individual cloth solves,
helper evaluation, all-cloth input/output, model skinning and GPU stream packing.
It also compares every indexed GPU vertex against an uncompacted import in rest,
cloth/helper-driven and animated poses. Timings are local diagnostics, not a
claim about the native game's frame rate or simulation fidelity.

Twinrova's displayed LOD references 97,560 of the 593,290 imported vertices.
The old preview processed the unused vertices in the BFRES shape buffers on
every pose and upload. OoT imports now remap the selected LOD's referenced
vertices consistently across skin weights, normals, tangents, UVs and colors.
No vertices are welded, no displayed triangles are removed, and no lower LOD is
selected. All 34 meshes and 120,171 triangles are retained. BotW/TotK imports
retain their previous vertex layout.

In a same-process Release comparison (tiered compilation disabled for the
benchmark), median Twinrova skinning fell from 30.32 ms to 5.18 ms and allocation
from 28.26 MB to 4.76 MB per pose. Exact numbers depend on runtime warmup and
machine load. The seven-cloth update with output measured 1.41 ms separately;
the original screenshot's slowdown was primarily model processing, not evidence
that the native constraint detail should be reduced.

Twinrova uses 595 particles and 246 virtual collision samples across seven cloths.
Its decoded setup uses one substep and one solve iteration, with standard,
stretch, local-range, transition, bend-link and bend-stiffness sets. Existing
executable research identifies explicit constraint ordering and the -1 collision
entry at 0x7101b36de0, and per-shape real/virtual contact dispatch at
0x7101b37a80. None of those solver paths, iteration counts, collision masks,
reference-buffer routing or recovery rules were modified for this optimization.
This is not a resolution of the remaining Malon runtime-override questions above.

Twinrova also has an animation resource with no skeletal clips before its resource
containing 44 skeletal clips. Actor browsing now accepts the empty skeletal set
and continues loading; explicit single-file animation import still reports an
empty file. Invalid individual actor resources report a notice without aborting
the rest of the animation list.

Validation includes indexed-vertex equivalence, Twinrova actor playback/seeking,
paused-animation simulation, all seven cloths, GPU renders and orbit pacing,
and the 1,134-cloth runtime corpus with no protected layouts or ordinary-contact
pinch fallbacks. Source files are not rewritten by preview compaction.
