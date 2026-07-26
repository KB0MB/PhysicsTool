# BPHYSICS Runtime Profiles

This catalog accompanies `BPHCL_CONVERSION_PROFILES.md`. The HKCL holds the
Havok simulation. BPHYSICS tells BotW which HKCL to load and supplies the
runtime attachment and wind settings for each cloth.

All entries recorded below use `wind_enable: true` and
`writeback_to_local: false`. Each sub-wind direction is `[0, 1, 0]`.

For an eventual BPHCL-to-HKCL export, the generated BPHYSICS sidecar should:

1. Point `cloth_setup_file_path` at the generated HKCL.
2. Set `cloth_num` to the number of generated cloths.
3. Keep the generated cloth order and exact cloth names aligned with HKCL.
4. Use the matching profile below when one exists.
5. Expose an editable fallback profile when no exact name is known.

## Armor 006

`Armor_006_Head.bphysics`: sub-wind frequency `0.2`, speed `0`.

| Cloth | Base bone | Frequency | Drag | Min speed | Max speed | Main factor | Add factor |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| O_006_Havok | Head | 2.77 | 10 | -2 | 20 | 1 | 0 |
| Hire_006_Havok | Head | 2.52 | 10 | -1 | 7 | 1 | 0 |

`Armor_006_Upper.bphysics`: sub-wind frequency `0.2`, speed `0`.

| Cloth | Base bone | Frequency | Drag | Min speed | Max speed | Main factor | Add factor |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Body_Metal_006_Havok | Spine_2 | 0.65 | 10 | -2 | 12 | 1 | 0 |

## Armor 008

`Armor_008_Head.bphysics`: sub-wind frequency `5`, speed `50`.

| Cloth | Base bone | Frequency | Drag | Min speed | Max speed | Main factor | Add factor |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Hair_1_008_Havok | Hair_Root | 4.33 | 8 | -2 | 13 | 1 | 0 |
| Hair_2_008_Havok | Hair_Root | 2.69 | 10 | -2 | 12 | 1 | 0 |
| Hair_B_008_Havok | Hair_Root | 5.12 | 8 | -2 | 12 | 0.2 | 0 |
| Hair_E_008_Havok | Hair_Root | 5.12 | 8 | -2 | 12 | 1 | 0 |
| Hair_F_008_Havok | Hair_Root | 5.12 | 8 | -2 | 12 | 1 | 0 |
| Hair_H_008_Havok | Hair_Root | 5.12 | 8 | -2 | 12 | 1 | 0 |
| Hair_D_008_Havok | Hair_Root | 5.12 | 8 | -2 | 12 | 1 | 0 |
| Earring_028_Havok | Head | 3.62 | 5 | -2 | 8 | 0 | 0 |
| Add_Hair_008_Hacok | Head | 5.42 | 8 | -2 | 15 | 1 | 0 |

## Armor 009

`Armor_009_Head.bphysics`: sub-wind frequency `5`, speed `30`.

| Cloth | Base bone | Frequency | Drag | Min speed | Max speed | Main factor | Add factor |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Hair_3_009_Havok | Hair_Root | 4 | 10 | -2 | 10 | 1 | 0 |
| Hair_2_009_Havok | Hair_Root | 3.54 | 10 | -2 | 12 | 0.58 | 0 |
| Hair_B_009_Havok | Hair_Root | 5 | 12 | -4 | 12 | 1 | 0 |
| Hair_E_009_Havok | Hair_Root | 5 | 12 | -4 | 18 | 1 | 0 |
| Hair_F_009_Havok | Hair_Root | 5 | 12 | -4 | 10 | 1 | 0 |
| Hair_G_009_Havok | Hair_Root | 5 | 12 | -4 | 10 | 1 | 0 |
| Hair_H_009_Havok | Hair_Root | 5 | 12 | -4 | 10 | 1 | 0 |
| Hair_I_009_Havok | Hair_Root | 5 | 12 | -4 | 10 | 1 | 0 |
| Hair_J_009_Havok | Hair_Root | 5 | 12 | -4 | 10 | 1 | 0 |
| Hair_D_009_Havok | Hair_Root | 5 | 12 | -4 | 10 | 1 | 0 |
| Hair_1_009_Havok | Hair_Root | 4.77 | 12 | -3 | 16 | 1 | 0 |

`Armor_009_Upper.bphysics`: sub-wind frequency `0.2`, speed `0`.

| Cloth | Base bone | Frequency | Drag | Min speed | Max speed | Main factor | Add factor |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Tunick_009_Havok | Waist | 3.5 | 8 | -2 | 5 | 0 | 0 |
| Apron_009_Havok | Waist | 3.5 | 8 | -2 | 5 | 0 | 0 |

## Armor 020

`Armor_020_Head.bphysics`: sub-wind frequency `5`, speed `50`.

| Cloth | Base bone | Frequency | Drag | Min speed | Max speed | Main factor | Add factor |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Hairtop_020_Havok | Head | 3.04 | 10 | -3 | 13 | 0.32 | 0 |
| Hair_2_Havok | Hair_Root | 2.69 | 10 | -2 | 12 | 1 | 0 |
| Hair_3_Havok | Hair_Root_Armor | 6.5 | 4 | -2 | 6 | 0 | 0 |

`Armor_020_Upper.bphysics`: sub-wind frequency `0.2`, speed `0`.

| Cloth | Base bone | Frequency | Drag | Min speed | Max speed | Main factor | Add factor |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Belt_A_Havok | Waist | 2.57 | 8 | -2 | 10 | 1 | 0 |
| Tunic_001_Havok | Waist | 3.41 | 10 | -3 | 15 | 1 | 0 |

## Armor 046

`Armor_046_Head.bphysics`: sub-wind frequency `0.4`, speed `5`.

| Cloth | Base bone | Frequency | Drag | Min speed | Max speed | Main factor | Add factor |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Caudal_Fin_046_Havok | Head | 3.41 | 8 | -1 | 10 | 0.26 | 0 |
| Beard_046_Havok | Head | 3.46 | 7 | -2 | 9 | 1 | 0 |

`Fin_046_Havok` exists in the BPHCL but has no BPHYSICS entry. This agrees
with the supplied BotW HKCL, which only has two cloths. It should not receive
a generated runtime profile until its intended BotW behavior is known.

## Actor 011 uses Armor 030 resources

`Armor_011_Head.bphysics` points to `Armor_030/Armor_030_Head.hkcl`, not an
Armor 011 HKCL. This is an intentional resource reuse case, so exporters
must use the declared path rather than deriving a path from the actor ID.

`Armor_030_Upper.bphysics` has no cloth section. It disables cloth and uses
the support-bone resource `Armor_030/Armor_030_Upper.bphyssb` instead.

## Armor 179

`Armor_179_Head.bphysics`: sub-wind frequency `5`, speed `50`.

| Cloth | Base bone | Frequency | Drag | Min speed | Max speed | Main factor | Add factor |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Hat_179_Havok | Head | 3 | 4 | -5 | 10 | 1 | 0 |
| Hair_1_179_Havok | Head | 3 | 8 | -2 | 12 | 1 | 0 |
| Hair_3_179_Havok | Hair_Root | 2.63 | 5 | -2 | 10 | 1 | 0 |
| Hair_2_179_Havok | Hair_Root | 2.69 | 10 | -2 | 12 | 1 | 0 |
| Hair_D_179_Havok | Hair_Root | 5.12 | 8 | -2 | 12 | 1 | 0 |
| Hair_B_179_Havok | Hair_Root | 5.12 | 8 | -2 | 12 | 1 | 0 |

## Armor 182

`Armor_182_Head.bphysics`: sub-wind frequency `0.2`, speed `0`.

| Cloth | Base bone | Frequency | Drag | Min speed | Max speed | Main factor | Add factor |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Hair_Back_Havok | Head | 5 | 5 | -4 | 10 | 1 | 0 |
| Hair_2_Havok | Head | 5 | 5 | -4 | 10 | 1 | 0 |
