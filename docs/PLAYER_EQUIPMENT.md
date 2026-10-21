# Player Equipment Preview

The TotK Player viewer has Armor and Weapons tabs under Equipment. Armor labels omit the actor prefix and body-part suffix; typing `116` selects `Armor_116_Upper` in the upper-body list.

Weapons include swords, longswords, spears, shields and bows. New selections start unequipped. Only one melee weapon is selected at a time. Drawing a bow stows melee weapons and shields to free both hands; drawing either of those stows the bow.

- Stowed weapons use `Pod_A` and their inherited `SheathTransOffset` / `SheathRotateOffset`.
- Drawn melee weapons use `Weapon_R`; shields and bows use `Weapon_L`, with their drawn offsets.
- `PlayerEquipScale` is applied to preview geometry only. Rotations in actor parameters are degrees.
- A referenced `SheathName` loads a separate sheath that stays stowed when the blade is drawn.
- A bow's `QuiverName` loads the matching quiver, using its own inherited sheath parameters (normally `Pod_B`). Drawing the bow adds the `NormalArrow` actor, whose model is `Weapon_Arrow_001`, to `Weapon_R`.

Equipment bones have isolated names and do not consume character animation tracks or cloth overrides. Attachments are recomputed from the final animated character pose. Armor and weapon changes preserve the camera and selected animation frame; failed or canceled loads retain the previous scene.

This is a read-only attachment preview. It does not implement weapon action state machines, arrow firing, bow-string draw animations, Fuse attachments, or crouching/riding-specific offset switching. Actor and physics files are not modified.

Checks cover actual numeric type-ahead, UI state changes in all four languages, all five native weapon types, animated attachments with and without cloth simulation, missing skin palettes in rigid BFRES models, and front/back GPU captures of drawn and stowed equipment.
