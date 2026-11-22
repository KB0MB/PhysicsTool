# OoT Collision Research

## Scope and Evidence

These notes refer to the supplied OoT dump using Havok SDK `20250200`, not the
original Nintendo 64 game. The decoded executable identifies Havok 2025.2 and
build paths containing `2025_2_10`. Its NSO segment hashes were verified before
Ghidra analysis. Addresses below belong only to this executable revision.

Decoded NSO SHA-256:
`CA344998B12A1B9C98444162DDE46F9F6A53326E6107434BC42DFAE2BC4A5EB4`

The source of the new equations is local executable inspection and reflected
native BPHCL layouts. No game executable or decompiled listing is included here.
This is not a claim of runtime parity with the game.

## Virtual Contacts Implemented

`hclVirtualCollisionPointsData` stores edge/triangle fans, section lists,
barycentric dictionaries and reverse fan indices. Array lengths come from
referenced ITEM records; serialized hkArray size fields can be zero. Point counts
are 16-bit fields, not 32-bit values including padding.

An edge sample is `A + (B-A)*u`. A triangle sample adds `(C-A)*v`. The radius is
the owning particle A's radius times `virtualCollisionPointsRadiusScale`.
Samples do not become independent simulated particles.

Relevant routines:

| Address | Observed role |
| --- | --- |
| `0x7101b37a80` | Per-collidable regular/virtual-contact dispatch and enabled flags |
| `0x7101b3a3c0` | Capsule virtual-contact kernel, including fan traversal |
| `0x7101b4c960` | Plane virtual-contact kernel |
| `0x7101b17f00` | Per-collidable particle-filter lookup |
| `0x7101b25080` | Contact-plane construction, including landscape paths |
| `0x7101b305c0` | Accumulated contact-plane normalization |

For the non-pinch path, each collider's ordinary contacts and virtual fans run
together before the next collider. The preview now follows this ordering, rather
than processing every collider's ordinary contacts before all virtual contacts.
The inspected fan kernels process triangles before edges. They write only the
owner particle. They do not divide displacement by its barycentric weight or
spread it among the other vertices by inverse mass. Edge corrections follow the
contact normal. Triangle corrections follow their oriented face normal, flipped
when its dot product with the contact normal is negative. Section edge vectors
are computed before processing that section's samples; the owner's corrected
position feeds subsequent samples.

The reciprocal-square-root zero case has no radial direction. An edge sample
exactly on a sphere center/capsule axis therefore has no correction direction;
a nondegenerate triangle still has its face normal. Degenerate triangle faces
produce no directional correction. Virtual corrections do not add the ordinary
particle-contact friction adjustment.

The preview compiles fan samples once and reuses scratch particles, without
allocating a new particle for each contact. It preserves fixed anchors and the
owner's collision mask. Animated and cloned colliders retain their virtual-point
enable flag. BotW and TotK solver profiles do not use this new path.

Limitations: packed blocks, landscape virtual contacts and nonzero authored
sidedness modes are rejected. Sphere and tapered-capsule geometry reuse existing
preview primitives; their full native virtual kernels have not been independently
verified. Broader state routing and native numerical details still need runtime
comparison. Adding a triangle in the editor does not synthesize new fans.

## Pinch Recovery: Guarded Preview

`0x7101c45600` is the anti-pinch constraint application routine, distinct from
ordinary collider projection and virtual collision. `0x7101c454e8` allocates its
instance data. The application routine reads per-particle pairs identifying the
particle and a reference-buffer vertex, plus per-particle state, elapsed time and
cached distance. Reference vertices are transformed into simulation space.
The allocator was previously identified at `0x7101c45500`, which is inside its
body; disassembly verified the earlier entry point.

The native reflection member table at `0x71048ca6e8` supplies these exact names:

| Set offset | Field |
| --- | --- |
| `0x28` | `perParticleData` |
| `0x38` | `toAnimPeriod` |
| `0x3c` | `toSimPeriod` |
| `0x40` | `toSimMaxDistance` |
| `0x44` | `referenceMeshBufferIdx` |

Observed transitions and actions:

- A newly pinched particle enters state 2 and resets elapsed time and cached distance.
- State 2 caches the initial reference distance, progressively limits displacement
  toward the reference, and snaps to it after the configured interval.
- State 1 directly follows the reference.
- Losing the pinch while in states 1 or 2 enters state 3 and resets elapsed time.
- State 3 initially follows the reference, then progressively releases displacement
  over another configured interval.

### Timer and Solver Ordering

`0x7101c45340` advances elapsed time only for states 2 and 3. State 2 becomes
state 1 when elapsed time is **strictly greater** than `toAnimPeriod`; state 3
becomes state 0 when it is strictly greater than `toSimPeriod`. At exact equality,
the application routine already snaps to the reference (state 2) or permits free
position (state 3), but the state byte has not changed yet. States 0 and 1 do not
advance their elapsed-time fields.

The call at `0x7101b37070`, inside `0x7101b36de0`, is outside the constraint
iteration loop. `0x7101b27080` calls that solver once per substep, passing the
instance's float at `+0x190`, also passed to integration. Thus the update belongs
after all iterations of that substep, not after each collision or constraint pass.
The full upstream timestep-initialization policy has not been reconstructed.

`0x7101b38720` handles collision schedule entries. With pinch detection active
and at least two pinch-enabled colliders, it clears per-pass pinch caches, handles
pinch-enabled contacts, projects retained contacts with friction, handles ordinary
colliders, and then applies the anti-pinch constraint sets. If those eligibility
conditions are absent, it takes the ordinary collision path instead.

During every application with elapsed time zero, state 2 refreshes its cached
reference distance. This is not a one-time capture on entering the state. When
time advances, the permitted distance shrinks linearly from that cached value;
particles already within the limit are not pushed outward. A new pinch during
release resets both time and distance.

The inspected state-3 release sequence uses `toSimMaxDistance` in a min operation
followed by multiply/reciprocal-square-root operations. For finite positive normal
values these factors cancel to a time fraction; zero/subnormal handling does not.
The research reference preserves that sequence rather than assuming a conventional
radial clamp from the field's name. This still needs validation against native
runtime outputs.

The recovered state machine now runs in the preview for explicitly authored
anti-pinch sets with verified routing. It does not synthesize recovery parameters
or references for files that lack them. Reset/seeking clears all recovery history.

### Contact Detection

The capsule detector is `0x7101b5d240`, with a scalar tail at `0x7101b6ba8c`.
Sphere dispatch is `0x7101b506a0`, calling detector `0x7101bedd20`.
The `0x7101b3f080` dispatcher inspected during tracing is the tapered shape,
not the sphere; it is not enabled for pinch recovery in this implementation.

For ordinary outward sphere/capsule contacts:

- Detection includes the collider's pinch radius as an extra margin, but physical
  projection does not. A margin-only overlap can mark a pinch without moving a particle.
- Before updating the retained priority, the detector writes the pinch flag from
  whether a nonnegative priority has already been recorded. No opposing-normal
  angle test appears in these kernels.
- A higher-priority contact replaces the cache only when physically penetrating;
  its priority still replaces the old priority for a margin-only overlap.
- Equal priority invalidates the retained contact. A later higher priority can
  restore it. Lower priority leaves the retained contact unchanged.
- Cache state resets at each collision pass. Masked/fixed particles are excluded;
  particles without pinch detection use ordinary collision projection.
- Zero/subnormal radial distance produces a zero normal, not a synthetic axis.

The guarded preview follows the native pass order: collect pinch-enabled contacts,
project retained contacts with friction, resolve ordinary colliders, then apply
authored recovery. Recovery clocks advance after the substep's solve iterations.

### Reference Routing

Reflection at `0x71048ca7e0` identifies `particleIndex` at offset zero and
`referenceVertex` at offset two, both `hkUint16`, with a four-byte stride.
The decoder uses ITEM array counts and checks DATA bounds, indices, duplicates,
buffer existence, and finite nonnegative durations/distances.

`0x7101c45580` first indexes the cloth-instance buffer table with
`referenceMeshBufferIdx`, then resolves its backing index at buffer `+0x110`.
It reads vertex positions through the backing buffer's pointer and stride, then
applies its transform at `+0x90` into simulation space. The reference vertex is
not necessarily the same index or position as the simulated particle.

The supported routes begin with decoded object-space or bone-space skin writers
and execute scratch-buffer copies/gathers in default-state order before simulation.
`hclCopyVerticesOperator` executes at `0x7101db8690`; reflection at `0x71048d0ef8`
identifies input/output buffers, count, input/output start indices and copyNormals.
`hclGatherAllVerticesOperator` executes at `0x7101db910c`; reflection at
`0x71048d1020` identifies the signed output-to-input vertex map. The native loop
skips negative entries, preserving previously written destination vertices.
Copies use their exact offsets; chained gathers preserve that vertex identity.
MoveParticles captures its input at its own execution point, while local ranges
and anti-pinch read the final pre-simulation references.

Native copies/gathers transform from the source frame through world space into
the destination frame. The preview carries decoded influences in bind-world space,
so copies retain those influences without adding the buffer transform twice.
Recovery references are sampled at frame input, separately from interpolated
collider/fixed-particle inputs. Missing vertices never fall back to a rest position.
State setup at `0x7101aee600` redirects each used buffer to its `shadowBufferIndex`
at runtime buffer `+0x110`, copying the original transforms into the shadow. An
empty shadow position component borrows the original position pointer/count/stride.
Teardown at `0x7101aefe60` restores the original logical index. The preview supports
scratch redirects, owned shadow positions, and borrowed shadow positions
for a fixed default state. Copy/gather writes execute forward even when the source
and destination share storage; they are not snapshot/memmove operations.
MoveParticles still captures its reference before later writes.

Setup reads the source's physical component before assigning aliases. It does not
resolve that source's logical redirect. An empty destination borrows the current
position pointer/count/stride only; a later borrow by the source cannot retroactively
populate an earlier empty destination. Nonempty destinations keep their storage.
Access records execute in order, and identity records do not clear earlier redirects.
Copies/gathers and reference consumers follow exactly one logical redirect, not a
transitive chain. The preview now models logical redirects separately from physical
position storage, including owned chains/cycles, shared destinations and ordered
borrowed chains. Bounds checks use the effective position component's capacity.

Uninitialized position storage, simulation/static-buffer references, ambiguous skin writers and
post-simulation reference writes remain blocked. Runtime state transitions and full
component aliasing (normals/tangents/triangles) are not implemented.

### One-Sided Contacts

`setupCollidableSidedness` is a setup flag, **not** the runtime side enum. The game
wrapper at `0x7100b7f5a0` initializes positions, optionally builds normals, then
calls `0x7101b191a0` for flagged colliders. The latter caches the classification from
`0x7101b19300`, provided doNormals, triangles and triangle-flip storage are present.
For spheres/capsules the classification counts negative, zero and positive dot
products of particle normals with radial contact normals. It includes fixed and
distant particles but honors the collider mask. Positive wins only when strictly
greater than negative; ties choose mode 1. A zero count strictly greater than both
signed counts disables sidedness. No eligible particles also produces mode 1.

Runtime mode 1 accepts dot < 0; mode 2 accepts dot >= 0. Pinch detection applies
this test before updating priorities/flags, including margin-only contacts. Normal
direction is zero for zero/subnormal radial distance, not a synthetic axis.
The preview applies the test to ordinary, pinch and virtual sphere/capsule contacts.
In the sphere (`0x7101b506a0`) and capsule (`0x7101b3a3c0`) dispatchers, ordinary
edge/triangle fans use the owner's cached surface normal for the dot product with
the sample's radial contact normal. They do not interpolate vertex normals or use
the fan's face normal for sidedness. The test follows penetration detection and
precedes the triangle correction-direction choice. Pinched-owner/priority gates
still run first. The separate landscape fan path is not enabled by this change.
Shared collider identities across slots remain guarded.

Normals are area-weighted triangle accumulations, normalized per particle with
zero/subnormal sums remaining zero (`0x7101b1c1e0`). Four-triangle groups use packed
flip bits/sign masks. The inspected scalar remainder is unusual: instructions
`0x7101b1c510..0x7101b1c5c0` select descending high bits and multiply by `1 - 7 * bit`;
the preview preserves this observed behavior instead of assuming the vector path's
flip convention. This distinction has isolated regression coverage and still needs
a runtime trace. Normals refresh after all substeps (`0x7101b27080` calls
`0x7101b29c5c`), not after every collision. Preview reset refreshes normals without
reclassifying the cached side.

### Virtual Contacts During Recovery

The sphere (`0x7101b506a0`) and capsule (`0x7101b3a3c0`) dispatchers gate virtual
fans on their owner's cached pinch flag and retained signed priority. A pinched
owner skips the fan, as does an owner whose retained priority exceeds the current
collider's priority. Virtual samples move owners but do not add pinch contacts or
replace the retained priority. Triangle fans precede edge fans; ordinary colliders
still execute their virtual fans after their ordinary contacts.

At `0x7101b38720`, retained contacts cache the surface point, normal and surface
velocity at detection. After the virtual corrections, projection is recomputed as
`radius - dot(currentPosition - surfacePoint, normal)`. It is not clamped to zero:
an owner moved past the cached plane is pulled back to it. Friction uses the cached
surface velocity. Reusing penetration from detection would double-correct owners
that virtual samples have already moved. The preview now follows this behavior.
Native broadphase candidate filtering is still replaced by exhaustive candidates;
this does not establish full collision-dispatch parity.

Pinch recovery combined with landscape contacts, tapered/other pinch shapes,
or negative priorities remains blocked.
The native empty-schedule path is not reproduced for recovery: an explicit collision
schedule entry is required. These restrictions avoid silently approximating
unverified native paths.

All 1,134 cloth entries in the supplied 171-file corpus have pinch detection off
and no anti-pinch constraint sets. Consequently those vanilla setups keep their
existing ordinary-contact behavior. Pinch detection without an authored recovery
set still uses ordinary contacts with a warning, rather than dropping tied contacts
without recovery. The new recovery path is tested with synthetic authored layouts,
not a vanilla pinch-enabled fixture or a measured native runtime trace.

## Volume and Bone-Plane Constraints

The native volume solve is `0x7101c6a280`. Its frame center is the sum of current
particle positions times authored FrameData weights (`0x7101c6b180`); weights are
not normalized again. `0x7101c6b220` accumulates the covariance from centered
positions and authored frame vectors with those same weights.

`0x7101c6b2b0` computes a regularized polar frame. It diagonalizes `C^T C` using
a persistent eigenbasis, at most 20 largest-off-diagonal Jacobi rotations and
float epsilon `1.1920928955078125e-7` (`0x7101671540`). The symmetric stretch uses
`sqrt(abs(eigenvalue)) + epsilon`. Its inverse is guarded by determinant magnitude
relative to the product of column L1 norms. The resulting frame is
`C * inverse(stretch)`, preserving reflection rather than forcing a proper rotation.
Rank-deficient frames are not completed with invented axes. An invalid eigensystem
resets its basis without replacing the previously retained rotation. The preview
resets this persistent state when restarting/seeking.

ApplyData records execute in order, moving the referenced particle toward its
frame-transformed local point with authored stiffness. FrameVector W is not used
as a homogeneous weight (`0x710000e930` always adds translation). These kernels
do not multiply by particle inverse mass or normalize authored coefficients.
The preview uses the existing constraint schedule, including repeated passes.

Bone-plane solve `0x7101c63b20` transforms the equation normal with the referenced
transform-set matrix and uses its translation as the plane origin. The constraint
distance is `dot(normal, position - origin) + equation.w`. Only negative distances
receive `normal * -distance * stiffness`. The transformed normal is not normalized
and the offset is not rescaled. Transform inputs are sampled from the frame pose,
not the simulated output bones. Unsupported transform sets, invalid indices,
missing reflected fields and nonfinite data are rejected.

### Bone-Plane Velocity Compensation

The reflected `hclSimClothStepInfo.preserveVelocity` field is at instance `+0x19d`.
Its constructor (`0x7101b17100`) enables it, but step setup (`0x7101b1aa48`)
recomputes it: legacy stiffness model 1 disables preservation; the default model 2
enables it when the total substep count differs from one or a frequency, time or
stiffness scale differs from one. Authored substeps are read by `0x7101b24f80`.
The preview uses its authored substeps and unit runtime scales, following this gate
rather than unconditionally using the constructor default.

For each plane entry, let `before` and `after` be its particle position before and
after correction, and `previous` the stored previous position. With preservation
disabled, history stays unchanged. With preservation enabled, the runtime flag at
configuration `+0x30` selects two branches:

- Direct: `previous = after - (before - previous)`.
- Blended: set `a = before - previous`, `b = after - previous`. If
  `length(a) + length(b)` exceeds float epsilon, set
  `w = length(b) / (length(a) + length(b))` and
  `previous = after - (a*w + b*(1-w))`; otherwise leave history unchanged.

Configuration initialization (`0x7101b1a8a4`) enables the direct branch by default.
Both branches are implemented and tested; the preview uses that native default.
No new serialized setting is added. Game runtime overrides have not been measured.
Volume constraints do not receive this bone-plane-specific history adjustment.
Floating-point instruction parity and game runtime traces remain unverified.
No new editing or conversion support for these constraints is implied.

## Direct Skin Mapping

Object-space skin execution (`0x7101d99a00`) uses the complete transform set in
skeleton order when `transformSubset` is empty. A nonempty subset instead builds
a compact selection through `0x7101cbff40`. The direct path calls `0x7101cbefa0`,
which composes the owner transform, current bone matrix and corresponding bind
inverse without a separate index remap.

The preview now accepts this direct mapping after checking transform/bind counts
and every referenced bone index. This enables `SimulationMesh_Cloth_03` in
`Land_ShadowTemple_Cloth_A_03`. The original subset stays empty; loading and
simulating do not rewrite it. Geometry editing of this layout remains guarded
because its rest-shape rebuilding has not been verified.

## Simple Wind and Aerodynamic Data

`hclSimpleWindAction::apply` at `0x7101dbbda0` accumulates forces before integration
(`0x7101c46a60`). Its input velocity is `(position - previousPosition) / dt`.
For a particle, the verified force is:

```
denominator = useSurfaceDensityWithSimpleWind ? surfaceDensity : totalMass
exposure = hasNormals ? abs(dot(normal, windDirection)) : 0.7
force = (airVelocity - particleVelocity)
      * (dragCoefficient / denominator) * actionDrag * actionScale
      * particleMass * particleDragFactor * exposure
```

Integration multiplies this force by inverse mass and `dt * dt`. The action returns
early when authored total mass is zero, even when density scaling is enabled.
The direction used for exposure is the action direction, not normalized relative
air velocity. A present but zero normal gives zero exposure, not the missing-normal
fallback. Normal updates follow the existing end-of-frame timing.

Initialization at `0x7101b1a880`, called from `0x7101b17100`, copies authored drag,
lift, density and the density flag into instance state at `+0xf8`. The simple-wind
action reads drag at `+0xf8`, density at `+0x100`, and its selector at `+0x108`.
It selects constant drag factor at simulation data `+0x144` when `+0x149` is true;
otherwise it reads the particle's entry from the array at `+0x130`.
Array decoding uses ITEM counts with bounds/type/reference checks.

`0x7101dbbd00` advances the action's phase and computes air speed as
`minSpeed + (maxSpeed-minSpeed) * (sin(2*pi*frequency*time)+1)/2`.
The preview still uses its user-controlled direction, speed and gust envelope,
with unit action multipliers. Game weather, actor reaction settings, shared timing
and runtime coefficient overrides have not been traced to these inputs.

The preview now uses this verified force response for OoT. It retains a
**preview-only** acceleration cap that prevents a drag step from passing the air
velocity; the inspected native action has no such cap. Other solver profiles are
unchanged. Source files are never retuned or rewritten by this preview.

The lift coefficient and lift factors are decoded and validated, but the inspected
simple-wind action never reads them. A separate lift-consuming/game aerodynamic
path is not established by this research. Do not interpret successful decoding as
an implementation of full native aerodynamics.

The supplied corpus has 1,134 aerodynamic records: drag coefficient 1 throughout,
lift coefficient 0.2 in 1,126 records and 1 in eight. Every record enables surface
density, with densities from 0.002259142 to 4368.455, constant lift/drag factors 1,
and empty factor arrays. All disable computed particle normals.

## Actor Bone-Length Policy

Player_Adult's resolved ClothParam requests `KeepBoneLength` for Sleeve,
HoodFringe_B, Skirt, Porch and Hood. The resource loader previously discarded
MeshList after reading HktPath. It now retains this per-mesh flag for actor previews.
The output pass preserves incoming local bone offsets between bones driven by the
same flagged cloth, while retaining simulated rotations and the chain's root pose.
Particle positions, collision solving and serialized physics data are unchanged.

Bottle_Drink at frame 181 reproduces the inner-shirt overlap without this policy.
Animation alone and animation plus helpers do not reproduce it. Preserving only
the distance along the simulated child direction does not remove the overlap;
preserving the local offset does. The latter is the current preview policy, not
yet a claim of an instruction-identical native bone-correction pass.

Executable checks confirm the KeepBoneLength property in the parameter visitor
at `0x71038fd2b0` (value at object +0xa1, override flag +0xac; inherited getter
`0x71038fe150`). Native legacy simple mesh-to-bone deformation at `0x7101cc6500`
also matches the existing centroid/frame reconstruction used by this skirt.
The runtime consumer of the actor correction flag still needs to be traced fully.

`--oot-shirt <romfs> <output>` captures the same pose with and without the actor
policy and checks preserved lengths, hierarchy ordering, parent scale, opt-out,
unchanged rotations and source-pose immutability. No private dump assets are
included in the repository.

## Checks

- Malon Adult's StormSong crashes were caused by rejecting finite sheared world
  matrices as invalid SRT transforms. Animated nonuniform skirt scales combined
  with child rotations exceed System.Numerics decomposition's orthogonality test.
  BFRES skeletons, helper evaluation, output inheritance and final skinning now
  retain the full affine matrix. A stable orthogonal frame is used only where
  rotation/scale channels or display axes are required. The game's precise helper
  decomposition policy has not been established by this fix.
- Malon's song/bucket/face clips are exercised at every integer frame without
  simulation and at fractional frames with simulation. Native BFRES skinning is
  compared with the skeleton/override round-trip, and GPU/actor-UI tests cover
  StormSong selection, playback, seeking and simulation. Invalid numerical poses
  stop the preview with an error rather than escaping the UI event handler.

- Native fan decoding: 34 cloths, 2,329 samples; all authored sidedness values zero.
- Native wind: 1,134 cloths complete 120 steps with no numerical resets or source
  changes; fixed anchors are compared against their skin-resolved no-wind positions.
  Synthetic tests cover both exposure paths, relative velocity, density and mass,
  zero-mass gating, per-particle factors, runtime multipliers, reset, toggling,
  malformed DATA/ITEM arrays, preview safety and other-game isolation.
- 120-step corpus preview: all 1,134 cloths pass, with 40,384 virtual corrections
  after correcting per-collider ordering.
- The direct skin-mapping layout, 23 volume cloths and three bone-plane cloths now
  run (23 volume sets, six plane sets). This describes the supplied corpus, not all
  possible native layouts.
- Direct mapping tests check decoded weights, animated fixed anchors, unchanged
  source data and retained geometry-editing guards. Velocity tests cover the native
  enable gate, both compensation equations, zero motion, repeated plane projection,
  two/four-substep stability and reset.
- Shape constraints have analytic tests for rigid, sheared, reflected, planar and
  collapsed frames, unnormalized weights, ordered/repeated stiffness, plane offset
  signs and animated transforms, mass independence, reset and invalid inputs.
  All newly supported native cloths pass static and animated-input runs without
  numerical resets or source changes. Twelve differ from a skipped-kernel baseline
  in the static gravity test; that comparison is not a game-accuracy measurement.
- Player_Adult actor playback: all 22 cloths simulate, with seeking, inherited bones
  and source-preservation checks passing.
- Npc_Hylia_Zelda_Adult actor playback: all 15 cloths simulate, including its newly
  enabled volume constraints; animation, seeking, paused-animation simulation,
  inherited bones and source-preservation checks pass.
- Synthetic tests cover edge/triangle equations, winding, radius scale, animation,
  mask/enable/fixed gating, zero-distance cases, per-collider order and other-game isolation.
- Recovery kernel tests cover exact timer boundaries, held/moving references,
  repeated zero-time captures, re-pinch, zero-duration transitions and independence
  from the number of collision applications per substep.
- Integrated sphere/capsule tests cover overlapping priorities, margin-only detection,
  animated reference vertices, reset/seeking, and unchanged source data. Synthetic
  DATA/ITEM tests exercise decoding and malformed array/index/period rejection.
- Virtual-pinch tests cover owner priority/flag gating, edge and triangle fans,
  retained-plane reprojection (including negative corrections), and non-pinch fans.
- Reference-routing tests cover copy offsets/chains, reordered and negative gather
  entries, partial writes, attachment snapshots, source preservation, owned/borrowed
  shadows, one-hop chains/cycles, shared destinations, setup-order pointer borrowing,
  repeated redirects, overlapping aliases, bounds and rejection of unreadable routes.
- One-sided tests cover setup votes/ties, zero-dot boundaries, packed/tail flips,
  degenerate normals, animated sphere/capsule crossing, ordinary projection and
  rejection before pinch recovery. DATA/ITEM tests cover setup flags and shadow
  allocation metadata. These are synthetic cases, not vanilla one-sided fixtures.
- One-sided virtual tests cover 48 sphere/capsule, edge/triangle and pinch/non-pinch
  combinations, including both signs, zero-dot boundaries, owner-vs-face normals,
  fixed neighbors and source preservation. The optional native-fixture variant test
  skips when no virtual cloth has the required authored normal topology and unique
  eligible collider slots; none in the supplied corpus met those requirements.
- Malformed-input tests cover counts, fan/dictionary indices, nonfinite weights,
  negative radius scale, unsupported landscape samples and count-field padding.

These tests establish decoding, stability and selected equation behavior. They do
not replace a measured comparison against the game's simulation output.
