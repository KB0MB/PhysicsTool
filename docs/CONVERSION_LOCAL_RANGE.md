# Long Cloth Conversion Audit

## Confirmed Loss

TotK local-range constraints can have a common stiffness and an additional array with per-record stiffness. BotW stores stiffness on the set, not the individual record.

The old converter copied records into BotW's list but lost their individual stiffness. It also returned early for soft-only arrays, so those files did not receive the mixed-stiffness warning. In the supplied Item_Parasail HKCL, all 19 dynamic local ranges in each long tail had stiffness 1.0, whereas the source uses coefficients from 0.00147 to 0.49. Some zero-radius ranges consequently behaved as hard attachments rather than soft positional restraints.

Armor_1141_Head has the same loss in several trailing-cloth sections. Its internal names contain Hair_Back; these labels do not imply the visible garment is hair. The main trailing section has source softness of approximately 0.04 to 0.341.

This is a conversion-data defect, not merely a viewport appearance issue. It is sufficient to make positional restraint substantially harder; it does not establish that every visual difference from TotK has the same cause.

## Correction

Convert the combined regular/soft array in its original order. Consecutive records with the same stiffness share a BotW set; a different stiffness begins another set. Expand each occurrence of the original set in the execution schedule into the resulting ordered sets. Preserve duplicate passes and collision markers. Do not average stiffness, expand radii, or apply a universal mass multiplier.

Only contiguous equal-valued records are combined, so repeated constraints on one particle retain their relative order. Unsupported multiple-source local-range mappings are rejected instead of silently guessing.

Armor_177 exposed a separate forward-conversion omission: bone-plane sets were sent through the ordinary links member. They now use bonePlanes records, including the full plane equation, transform/particle indices and stiffness. The 177 scarf's local-range stiffness is already 1.0 in the source; it is not the same soft-range case as Parasail. Armor_177 currently has no exact measured mass profile, so source masses and general coefficients remain unchanged. Its native runtime behavior is not certified identical to vanilla BotW.

## Validation

- Synthetic mixed regular/soft and soft-only arrays, repeated particle references, contiguous stiffness runs and repeated collision schedules.
- 41 cloths from Item_Parasail, Armor_1141_Head, Armor_177_Head_A and Armor_177_Head_B: local softness/order, bone-plane values, masses/fixed state, Wii U/Switch save/reload and 120 finite preview frames per saved cloth.
- Existing 75 matched-cloth conversions, 150 platform round trips and 38 helper round trips remain passing.
- Game inputs are hash-checked and not modified. Test exports are separate files.

Existing HKCL exports do not contain the discarded per-record softness and need reconversion from BPHCL. The preview is not a complete Havok runtime; in particular, successful bone-plane serialization is not a claim that the preview implements bone-plane solving. An in-game comparison is still required.
