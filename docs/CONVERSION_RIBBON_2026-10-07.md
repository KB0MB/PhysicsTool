# Ribbon and parasail conversion fidelity

## Corrected asset scope

The requested ribbon is `SimulationMesh_Ribon_1141` in
`Armor_1141_RaulSkin_Upper.bphcl`, not the head piece previously investigated.
The local Downloads copy contains five cloths. The ribbon has 56 particles,
four fixed anchors, 52 triangles, 104 standard links, 52 directed stretch
links, 50 bend links and 50 four-particle bend-stiffness records.

Its execution program is `[2,0,0,0,4,5,1,-1,3]`; the three standard-link
passes are intentional and remain intact. The source's 0.29 dynamic mass,
per-particle inverse masses, rest curvature and signed bending coefficients
are preserved. There is no evidence here for a new global mass multiplier.

## Confirmed runtime difference

Read-only headless Ghidra analysis of the saved TotK executable established:

- Reflection class record `0x710424bd70`, constructor `0x71003db35c`,
  vtable `0x710425eb88`, solve dispatch `0x71003db3a0`.
- `useRestPoseConfig` selects rest-pose bending. `clampBendStiffness` selects
  `0x71003db640`; the unclamped path is `0x71003db8b0`.
- The clamped kernel computes a curvature height from the current triangle
  areas, shared-edge squared length and stored rest curvature. When its square
  exceeds `maxRestPoseHeightSq`, the effective bend coefficient becomes zero
  for that solve. This is conditional disabling, not a constant softness scale.
- Constants at `0x71038070c0` and `0x71038070d0` confirm the zero and one
  values used in that branch. The ribbon enables the cutoff with a squared
  height limit of approximately `0.0028929273`.
- The preserved BotW kernel `0x7101d7f3c8` applies rest curvature without this
  conditional cutoff. The supported BotW HKX2 class has `useRestPoseConfig`
  and the same per-link geometric values, but no clamp/height-limit fields.

Consequently a constant HKCL coefficient cannot reproduce the source for every
deformed configuration. The converter now reports this limitation. The BPHCL
diagnostic preview honors the native cutoff; this does not enable BPHCL
simulation in the editor or add a non-native cutoff to HKCL.

Local research traces are retained under `HKCL/Research/totk-ribbon-*20261007.txt`
and `totk-vector-constants-20261007.txt`. Some intermediate traces investigate
the neighboring batched bend-link class; the addresses above are the confirmed
four-particle bend-stiffness class. No SDK implementation was used in this pass.

## Conversion fixes

The converter previously selected an existing constraint set by class name.
Two different standard-link sets could therefore overwrite each other, while
their execution entries both mapped to the surviving set. Local-range splitting
also assumed a single source set. Each source set now retains its identity;
contiguous stiffness runs remain adjacent and schedules expand only that set.
This works even when distinct source sets share the same display name.

Only the first simulate program and first simple mesh-to-bone deformer were
previously remapped. All such operators are now handled. Distinct configurations
inside a single TotK simulate operator are rejected until an explicit state-aware
translation exists, rather than exporting configuration zero silently.

These are general converter defects, not confirmed causes of the reported
ribbon appearance. The ribbon's Wii U export before/after this pass is
byte-identical (SHA-256 `34511B9C57CC9BA8B8B8DCA766C033FFF83CF95537F6714ED45691C839EFB5E0`).
The parasail's split local-range sets have a different physical array order,
with equivalent remapped execution. Neither asset received speculative tuning.

## Verification and limits

The expanded `--audit-cloth-conversion` test covers 90 cloths: five from the upper
armor, ten from the parasail, and 75 existing matched/Yiga fixtures. It checks
Wii U and Switch save/reload, local softness/order, four-particle geometric
values, mass/fixed membership and 120 finite preview frames per saved cloth.
Inputs are hashed and remain unchanged. Synthetic tests cover same-name/type
sets, multiple split local ranges, repeated/collision execution, two simulate
programs, two deformers, rejected distinct configurations and the native cutoff.

Each source/export pair also receives a 600-frame moving-anchor constraint
probe. It is deliberately collision-free: the BPHCL editor preview does not
populate active collider slots and is not a native TotK collision reference.
Both sides use the same input-sampling policy. BPHCL's preview also lacks some
decoded skin-reference precision present in HKCL, so this is diagnostic evidence,
not exact trajectory certification. An earlier unnormalized comparison showed
large differences caused by those preview limitations, not proven export loss.

In this probe, the eight small parasail tails match exactly; the two main tails
have maximum point differences of about `0.00117` and `0.00049` game units.
The ribbon's maximum difference is about `0.00242` units. Its bend cutoff does
not activate in this mild probe, so the cutoff is a verified potential difference,
not a demonstrated explanation of the user's ordinary-motion symptom.

Remaining work for near-game parity is a matched in-game motion comparison,
especially animated collider response, actor-level forces/settings, transitions,
and strongly deformed ribbon poses. Those cannot be certified by successful
serialization or by this preview alone. Original Downloads/mod files were not
replaced. Current test exports are under `artifacts/checks/conversion-final`.
