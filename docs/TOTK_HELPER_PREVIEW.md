# TotK Helper Preview

## Native Findings

The local TotK `main` in the `TotK_Cloth_Runtime_20260830` Ghidra project was inspected read-only on 2026-10-16. Addresses below identify that analyzed executable, not all game revisions.

- `0x71008434c8`: frame dispatch visits driven records in resource order. Editor group boundaries are not execution batches.
- `0x710079d1c4`: connection evaluation lazily samples rotation and translation independently and caches each for the current update. Channels are roll, bend H, bend V, translation X/Y/Z.
- `0x710079ef74`: rotation sampling uses the inverse authored BPHHB base rotation multiplied by the current relative rotation, then extracts stereographic swing and twist. It does not project an axis-angle vector or substitute the BFRES bind rotation.
- `0x710079fbe0`: translation sampling subtracts the authored base translation in the selected space.
- `0x7102a81344`: driver preprocessing normalizes the aim/up frame, constructs aim cross up, and stores the inverse authored base quaternion.
- `0x710079b7fc`: pose output constructs a coupled swing from `tan(bendH/2)` and `tan(bendV/2)`, applies twist around aim, and multiplies the result after the authored base quaternion. Base -1 is world space; -2 is parent-local space; nonnegative values reference an explicit base bone.
- Curve evaluation uses Hermite interpolation and endpoint-slope extrapolation. The endpoint's Z tangent is used outside either end. BotW's constant-end flags are not present in this path.

The shared swing/twist helpers retain their existing BotW names, but the TotK calculations above establish the same convention. This does not establish equivalence for specialized position/rotation/aim-driven tables or all runtime blend controls.

## Preview Changes

Helper evaluation propagates parent outputs through a detached skeleton before evaluating later records. Equipped models can contain duplicate base skeletons: unique armor children are connected to the canonical parent when the evaluation skeleton is indexed by name.

Full-model preview evaluates each whole sidecar in resource order with one driver cache, rather than processing editor groups separately. Selected-group inspection remains available. Actor input posing retains helper outputs even when cloth later owns the same output bone; simulated outputs replace helper outputs after solving. No simulated pose feeds back into the next animation input.

## Verification

- Synthetic tests cover authored rests different from model rests, compound bends and twist, quaternion sign equivalence, local/world/explicit spaces, endpoint extrapolation, both parent/child record orders, duplicate equipped skeletons, UI-group independence, and source-pose isolation.
- Real OpenGL captures compare animation-only, helpers, and simulation using Player with Armor_1151_Head, Armor_116_Upper and Armor_001_Lower in Assassin_DotonCharge, including frame 24.
- Native Player checks cover fixed anchors, animation following, binding completeness, finite simulation and loop stability.
- Ancient Hyrule Zelda was checked using Default_Angry_Hello, not a confirmed reproduction of the user's exact greeting. Some simulated skirt distortion remains. The helper fixes must not be described as a complete collision or dress-simulation fix.

Local screenshots and pose dumps are under `artifacts/helper-dress-audit`; game assets and decompiled executable text are not included in this repository.
