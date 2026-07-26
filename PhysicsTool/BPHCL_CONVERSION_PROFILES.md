# BPHCL to HKCL Conversion Profiles

This catalog records vanilla file pairs where the same cloth exists in TotK
(`.bphcl`) and BotW (`.hkcl`). It is reference data for the experimental
BPHCL to HKCL exporter, not a universal formula.

See `BPHYSICS_RUNTIME_PROFILES.md` for the matching BotW attachment and wind
profiles. Solver scale and runtime wind settings are intentionally documented
separately.

## Confirmed relationship

For every dynamic particle in the matched cloths below:

```text
scale = BotW total mass / TotK total mass
BotW mass = TotK mass * scale
BotW inverse mass = TotK inverse mass / scale
```

Standard and bend-link stiffness also follow the same scale in the matched
data. Stretch stiffness remains `1.0`.

Particle count is not a reliable scale predictor. It happens to match some
cloths, but not all of them.

## Armor 020

Source files are in `HKCL/Matches`.

| BPHCL cloth | Dynamic particles | TotK total mass | BotW total mass | Scale | Particle count |
| --- | ---: | ---: | ---: | ---: | ---: |
| Hairtop_020_Havok | 9 | 0.170618 | 1.0 | 5.861 | 11 |
| Hair_2_Havok | 18 | 0.133333 | 4.0 | 30.0 | 30 |
| Hair_3_Havok | 4 | 0.041667 | 0.5 | 12.0 | 12 |
| Belt_A_Havok | 3 | 0.4 | 2.0 | 5.0 | 5 |
| Tunic_001_Havok | 40 | 0.065436 | 2.0 | 30.564 | 70 |

For these five matches, particle positions, skeleton local transforms,
quaternion orientations, active collider counts, and per-particle collision
masks match directly. The mass and stiffness conversion is the outstanding
solver adjustment.

## Current hypothesis

BotW appears to choose a target total mass per cloth, commonly a simple value
such as `0.5`, `1`, `2`, or `4`. The conversion scale then follows from the
source BPHCL total mass. More matched files are needed to determine whether
the target is driven by cloth family, area, particle spacing, constraint
topology, or an unparsed authoring parameter.

## Armor 009

| BPHCL cloth | Dynamic particles | TotK total mass | BotW total mass | Scale | Particle count |
| --- | ---: | ---: | ---: | ---: | ---: |
| Hair_3_009_Havok | 4 | 0.041667 | 0.5 | 12.0 | 12 |
| Hair_2_009_Havok | 6 | 0.584801 | 4.0 | 6.840 | 10 |
| Hair_B_009_Havok | 2 | 0.496649 | 2.0 | 4.027 | 6 |
| Hair_E_009_Havok | 1 | 3.003117 | 2.0 | 0.666 | 3 |
| Hair_F_009_Havok | 1 | 0.666667 | 2.0 | 3.0 | 3 |
| Hair_G_009_Havok | 1 | 0.5 | 1.5 | 3.0 | 3 |
| Hair_H_009_Havok | 1 | 0.666667 | 2.0 | 3.0 | 3 |
| Hair_I_009_Havok | 1 | 1.033936 | 2.0 | 1.934 | 3 |
| Hair_J_009_Havok | 1 | 1.827327 | 2.0 | 1.094 | 3 |
| Hair_D_009_Havok | 1 | 0.666667 | 2.0 | 3.0 | 3 |
| Tunick_009_Havok | 10 | 0.119913 | 1.0 | 8.339 | 15 |
| Apron_009_Havok | 12 | 0.113390 | 1.0 | 8.819 | 17 |

`Hair_1_009_Havok` is intentionally excluded for now. Its corresponding
source/target particle position and active-collider data do not match cleanly,
so it is not yet safe reference material.

The 009 matches strengthen the target-total-mass hypothesis. BotW total mass
again resolves to a small authored set (`0.5`, `1`, `1.5`, `2`, or `4`), while
the TotK source total mass varies continuously.

## Armor 006

| BPHCL cloth | Dynamic particles | TotK total mass | BotW total mass | Scale | Particle count |
| --- | ---: | ---: | ---: | ---: | ---: |
| O_006_Havok | 3 | 0.4 | 2.0 | 5.0 | 5 |
| Hire_006_Havok | 6 | 0.2 | 2.0 | 10.0 | 10 |
| Body_Metal_006_Havok | 1 | 1.666667 | 5.0 | 3.0 | 3 |

This is another clean set: particle positions, skeleton local transforms,
quaternion orientations, active collider counts, and per-particle collision
masks match directly. It adds BotW target total masses of `2`, `3`, and `5`,
further confirming that a single global scale is not appropriate.

## Armor 008

| BPHCL cloth | Dynamic particles | TotK total mass | BotW total mass | Scale | Particle count |
| --- | ---: | ---: | ---: | ---: | ---: |
| Hair_1_008_Havok | 8 | 0.233431 | 1.5 | 6.426 | 17 |
| Hair_2_008_Havok | 18 | 0.133333 | 4.0 | 30.0 | 30 |
| Hair_B_008_Havok | 2 | 0.496649 | 2.0 | 4.027 | 6 |
| Hair_E_008_Havok | 1 | 3.003117 | 2.0 | 0.666 | 3 |
| Hair_F_008_Havok | 1 | 0.666667 | 2.0 | 3.0 | 3 |
| Hair_H_008_Havok | 1 | 0.666667 | 2.0 | 3.0 | 3 |
| Hair_D_008_Havok | 1 | 0.666667 | 2.0 | 3.0 | 3 |
| Earring_028_Havok | 6 | 0.25 | 2.0 | 8.0 | 8 |
| Add_Hair_008_Hacok | 5 | 0.227755 | 1.0 | 4.391 | 7 |

All nine matches are clean, including `Earring_028_Havok` and the
zero-collider `Add_Hair_008_Hacok`. Particle positions, local skeleton
transforms, rotations, active collider counts, and collision masks transfer
directly in each case.

## Armor 046

| BPHCL cloth | Dynamic particles | TotK total mass | BotW total mass | Scale | Particle count |
| --- | ---: | ---: | ---: | ---: | ---: |
| Caudal_Fin_046_Havok | 12 | 0.066667 | 1.0 | 15.0 | 15 |

`Caudal_Fin_046_Havok` is a clean match: particle positions, skeleton local
transforms, rotations, active colliders, and masks match directly.

`Beard_046_Havok` is intentionally excluded. The BotW counterpart has extra
bones and colliders and uses a different collision-mask layout (`0,255`
instead of `0,63`). `Fin_046_Havok` has no BotW counterpart in the supplied
HKCL. Neither should inform automatic conversion settings yet.

## Armor 179

| BPHCL cloth | Dynamic particles | TotK total mass | BotW total mass | Scale |
| --- | ---: | ---: | ---: | ---: |
| Hair_1_179_Havok | 1 | 0.622483 | 4.0 | 6.426 |
| Hair_2_179_Havok | 18 | 0.083333 | 2.5 | 30.0 |
| Hair_3_179_Havok | 4 | 0.041667 | 0.5 | 12.0 |
| Hair_B_179_Havok | 2 | 0.993298 | 4.0 | 4.027 |
| Hair_D_179_Havok | 1 | 0.666667 | 2.0 | 3.0 |
| Hat_179_Havok | 8 | 0.25 | 4.0 | 16.0 |
| Sode_179_Havok | 10 | 0.066667 | 2.0 | 30.0 |

`Outer_179_Havok` is deliberately not used as an automatic profile: its
source and BotW matches have different dynamic-particle counts (`48` versus
`42`), so it is not a safe direct comparison.

## Armor 182

| BPHCL cloth | Dynamic particles | TotK total mass | BotW total mass | Scale |
| --- | ---: | ---: | ---: | ---: |
| Hair_2_Havok | 18 | 0.133333 | 4.0 | 30.0 |
| Hair_Back_Havok | 9 | 0.133263 | 1.0 | 7.504 |

Both 182 matches have aligned particle, skeleton, collider, and mask layouts,
so they are included in the automatic conversion profile catalog.
