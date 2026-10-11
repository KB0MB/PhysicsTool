# Conversion to TotK release audit

## Verified fixes

The HKCL export path previously discarded its simulation operator and emitted
one default pass with one substep and one solve iteration. Fresh BPHCL output
still regenerates the buffer/operator envelope, but now carries a separate
source simulation configuration through serialization. Constraint indices are
remapped without reordering repetitions or collision calls. Empty schedules
keep the source constraint-array order. Multiple simulation configurations are
rejected rather than silently flattened into one.

Bone-plane sets were filtered out even though the native writer supports them.
They now survive with their equations/bindings and a skeleton-read declaration.
Nonzero transform-set references are rejected because the fresh generated
runtime provides only the primary skeleton set.

HKCL mass and inverse mass are independent serialized fields. Conversion now
preserves both, applying reciprocal scale directions for measured asset mass
profiles instead of overwriting inverse mass with 1 / mass. Fixed particles
remain zero-mass anchors. This does not change the editor's separate BPHCL
mass-editing policy or establish a universal mass conversion factor.

BotW support outputs carry weights; TotK's helper output connections do not.
The old converter dropped these weights. It now scales each used curve's
ordinate and both Hermite tangents, caching by source curve and weight. The
original shared curve stays unchanged. Zero, negative, fractional and dual
weights are covered by evaluation tests and binary save/reload.

## Verification

Run `PhysicsTool.Checks --audit-to-totk OUTPUT FIXTURE_DIRECTORY`. The optional
fixture directory contains local HKCL/BPHYSSB files and is not distributed.

- 21 native HKCL files, 71 cloths: generated native BPHCL, reopened binary,
  reflection/array alignment, solver configuration, particle masses, fixed
  membership and collider-slot validation.
- Six native support files with resolvable explicit/cloth-skeleton references:
  BPHHB export/reload and weighted curve response checks.
- Eleven support files safely refused export without their missing model
  hierarchy. Cloth skeletons are subsets, not substitutes for the full BFRES.
- Synthetic regression: repeated bone-plane/collision passes, nondefault
  solver settings, empty execution, independently authored inverse mass,
  invalid inverse mass, all six output channels and shared weighted curves.

The normal regression suite includes these synthetic checks. Audit outputs
are under ignored `artifacts/checks/to-totk-20261010`; source fixtures are not
modified. Prior executable research is documented in
`CONVERSION_REVIEW_2026-10-01.md`; no new executable analysis is claimed here.

## Release limits

Successful export is not proof of identical in-game motion. The generated
runtime still rebuilds animation-transition/operator states. Existing measured
asset tuning and bend-rest reconstruction remain approximations for custom
topologies. Unsupported skin/operator families and multiple simulation
configurations need further work.

Support export still requires the matching model for parent-space references;
linear connections and unsupported reference spaces are safely rejected.
BotW and TotK rotation parameterizations/composition are not universally
equivalent for large combined-axis poses. BotW nonconstant curve extrapolation
also differs from the current clamped BPHHB preview; the dialog now warns when
nonzero endpoint slopes make that relevant. These cases need targeted in-game
tests, not a claim of perfect conversion.

Before release, reconvert representative cloth/helper assets with this build
and test idle, fast turns, crouching, collision contact and animation loops in
TotK. Include one ribbon/skirt and one helper-driven limb.
