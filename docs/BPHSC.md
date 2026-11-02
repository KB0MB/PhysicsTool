# BPHSC Static Compounds

The initial editor works on existing TotK version-2 static compounds. It patches
known fixed-size fields and preserves embedded shape geometry. The shrine scene
editor can also move, rotate and scale supported baked collision instances and refit their
spatial bounds alongside BANC placements. Arbitrary geometry rebaking is not supported.

## GroundControl Bindings

The following relationships were checked against 70 GroundControl actors in 21
vanilla SmallDungeon static BANC files:

- `Phive.Placement.GroupGUID` matches the body's 64-bit placement group value.
- `Phive.Placement.GroupID` matches the body's table entries and actor-link group.
- The controller's placement ID matches an actor-link hash. Its actor link points
  to the corresponding body-info slot, even when it has no baked shape instances.
- Some groups have no compound shape table entry. Their body info and actor links
  must still be retained and shown.

For example, Dungeon004 has a SimpleMoveTo controller with actor/placement ID
`0x2fb3418a6169d765`, GroupID 8931 and GroupGUID `0x7bd2701ed4afcc28`.
The GUID is in body-info slot 1; the actor-link record is 1336. Group IDs also
occur in the first two bytes of the entity/sensor body table entries, not just
inside BANC. The reader/writer does not assume the IDs are interchangeable.

The standalone BPHSC editor reassigns an **existing** group consistently inside BPHSC. It does not
split shapes into new groups, move shape membership, generate GroundControl
actors, or update BANC. The BANC check reports reference mismatches only; it does
not validate AI, animation, movement settings or merged BANC dependencies.

## Preservation and Validation

- Original source-path overwrite is refused; use a separate output file.
- Raw and dictionary-free Zstandard output are reopened and compared before
  replacing the destination. Compressed inputs can use the dump's dictionary.
- Actor hash changes rebuild the open-addressed lookup and index tables.
- Body transforms, group bindings and water changes preserve unknown bytes.
- Unsupported versions, truncated tables, overlapping sections, invalid numbers
  and duplicate actor hashes are rejected.
- Undo/redo restores complete document snapshots, with bounded history storage.

Run focused tests with:

```text
dotnet run --project tests/PhysicsTool.Checks -c Release -- --bphsc <output-folder> <SmallDungeon-folder>
```

The local survey covered 152 shrine files: parsing, byte-exact unchanged round
trips, reversible transforms, group edits, hash lookups, water changes, embedded
geometry preservation, and unchanged dump sources. Representative compressed and
raw saves, malformed inputs and UI undo/redo are also checked. These are structural
tests, not proof of in-game compatibility for every edit.

## Shrine Placement Editing

The embedded scene catalog lists all BANC files under the configured TotK romfs.
Opening a scene loads its static/dynamic partner, recursively expands merged
placements, reads its matching BPHSC, and renders actor BFRES models with TexToGo
textures. Shared actor assets are decoded once per scene. Placement matrices use
scale, XYZ rotation in radians and translation, composed with merged parents.
Model picking, geometry framing, visibility controls, actor links and GroundControl
parameters are available for inspection. Missing assets and unsupported
texture/material features are reported. Collision surfaces are not rendered yet.

Position, rotation (radians) and scale fields edit the actor's source-local pose.
G/R/S apply viewport transforms, including axis restrictions. Multi-selection
rotation and scaling use the selection center as pivot. Transforming
a merged-scene parent updates its descendants in the preview and collision while
preserving child-local placement values. Shared merged children and GroundControl
origins are guarded. Undo/redo derives the preview and collision from original
snapshots rather than accumulating floating-point edits.

The fixed-reference table's layer collection and the actor link's body-info slot
resolve the compound slot; the instance index is a full 16-bit value. All 98,955
references surveyed across the 152 shrine files resolved to valid instances.
The four-lane SIMD tree stores instance indices in leaves and node indices in
internal nodes. World deltas are composed with each body's transform to update
instance translation, quaternion rotation and scale in body-local space. Cached
transform flags are refreshed. Translation-only changes preserve rotation/scale bytes.
Leaf AABBs use all eight transformed corners; ancestor AABBs, compound bounds and
the conservative bounding radius are refitted. Each edit starts from original
bounds, avoiding cumulative expansion through repeated rotations.
Tree topology, shape bytes, actor links and opaque metadata are retained.

Tests moved, rotated, scaled and refitted 77 compounds; the other 75 contain water
or external mesh layouts and are rejected for coordinated scene placement editing.
Instance matrices, transform flags, leaf bounds and unrelated binary data were
checked. Moving, rotating and scaling an actor in Dungeon004 was checked by
reopening both BANC and BPHSC, verifying model transforms, undo/redo and preservation
of unrelated BYML. Parent/child selections and invalid-transform rejection are tested.
These checks do not replace in-game validation.

Save shrine creates a new mod folder with a romfs tree. Both BANC companions and
loaded merged sources are included, along with the edited BPHSC. Sources are never
overwritten. Output is serialized and verified before a staging folder is renamed
to its final name. Unknown BYML fields, version and byte order are preserved.

The perspective fly camera uses WASD, Q/E, right-mouse look and Shift acceleration.
With an actor selected, idle S starts scaling; it remains backwards movement while
holding right-mouse look or another movement key.
Scene rendering groups materials, culls offscreen meshes and uploads only changed
mesh buffers. Normal cloth-editor navigation remains orthographic.

Scales must be positive and nonzero. Non-uniform scales that introduce shear into
an actor's local pose or a collision instance are rejected atomically; the instance
format cannot represent shear. Uniform scaling avoids that restriction.

Still unsupported: mirrored collision, importing new collision geometry,
shared merged-source cloning, new GroundControl groups or membership changes, navigation mesh
rebaking, AI/path edits and creation of a shrine from scratch.

## Actor Duplication and Deletion

Duplicate (Ctrl+D) copies standalone actors in place and selects the copies. Delete
asks for confirmation. Both operate on multi-selection, are undoable, and update
the paired BANC and BPHSC export. Fresh 64-bit actor/placement IDs and matching
SRTHash values are assigned to copies. Existing actor records and unrelated BYML
data are retained. Linked actors, AI/rail references, physics constraints, moving
groups and merged-scene parents are guarded rather than silently disconnected.

The collision writer rebuilds actor links and their hash/index lookup, fixed
instance references, instance arrays and SIMD trees. Instance shape tags index
the fixed-reference table (not the actor table); each fixed reference separately
stores its actor index. Both are remapped. Shape geometry and material tables
are preserved. Relocation moves the whole geometry block together and updates
instance-relative pointers and Havok ITEM offsets, counts and compound bounds.
Empty collision bodies retain an empty tree, without invisible leftover shapes.

Native regression checks cover 77 compounds, including independent duplicate
transforms, deletion, empty bodies, actor ownership, lookup tables and unchanged
geometry. Scene tests cover fresh IDs, undo/redo, confirmation/cancellation and
paired saves. This remains structurally validated, not in-game certified.

Format reference: Starlight-Dev's `PhiveStaticCompoundFile` declarations, with
field relationships independently verified against the local dump.

## GroundControl Scene Editing

Select an existing controller in the shrine scene inspector. The Members list
selects its actors; their model bounds also frame the group. Movement fields are
defined from the actor packs' native BlackboardParamTable names and defaults:
SimpleMoveTo, SimpleMoveAndStop, Rotate, RotateRightAngleOrder, RailLoop and
WaveLoop. Only changed values are written into Dynamic, retaining scalar/int/vector
types and unrelated parameters. Rails and AI activation references are preserved.
This is editing, not an in-editor runtime simulation of the controller.

Apply pivot can relocate the entire supported assembly, or only change its pivot.
The latter keeps all members and collision instances at their existing world poses.
The body's original offset from the controller is retained. Instances are rebased
into the new body frame and their bounds refitted before the BPHSC body pose changes.
GroupID/GroupGUID edits update BANC members, body info, body tables and actor links.
All operations are atomic and derive from original data for exact undo/redo.

Missing members, mixed identifiers, ambiguous collision ownership, reused merged
sources, water and external mesh layouts block group relocation. Rails and physics
constraint records block whole-assembly moves because their embedded poses need
additional handling; pivot-only edits preserve those records and member positions.
Valid movement settings can still be edited without touching collision in guarded
groups. Creating controllers, changing membership and authoring AI/rails remain
unsupported. Existing activation logic is still required in game.

```text
dotnet run --project tests/PhysicsTool.Checks -c Release -- --shrine-ground-control <romfs> <output-folder>
```

Tests check native group parameter types, unchanged collision for parameter edits,
world-space instance poses after pivot/assembly changes, synchronized identities,
exact undo/redo, paired saves and preservation of unknown BANC/AI fields. These
checks do not replace in-game moving-platform testing.
