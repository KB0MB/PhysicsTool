# BPHYSICS Wind Defaults

New generic exports use sub-wind speed 25 instead of 50. This is intentionally gentler, not a change to existing files or the preview solver.

A read-only survey of the local Wii U v208 update's armor actor packs on 2026-10-11 inspected 415 packs without errors. Deduplicating by referenced HKCL resource left 74 resources and 337 cloth profiles, avoiding repeated upgrade aliases. These are sampled armor statistics, not a claim about every game resource or exact force equivalence with TotK.

| Field | Mean | Median | Generic default |
| --- | ---: | ---: | ---: |
| Shared sub-wind speed | 23.27 | 20 | 25 |
| Shared sub-wind frequency | 2.98 | 5 | 5 |
| Cloth wind frequency | 4.13 | 4.5 | 4.5 |
| Cloth wind drag | 7.87 | 8 | min(8, 4 * total mass) |
| Cloth minimum wind speed | -2.31 | -2 | -2 |
| Cloth maximum wind speed | 11.74 | 12 | 12 |
| Main sub-wind factor | 0.87 | 1 | 1 |
| Additional sub-wind factor | 0 | 0 | 0 |

The shared values count each resource once; cloth values count each cloth once. Use medians for a robust central profile, with speed 25 chosen as requested and close to the resource mean. Do not average bone bindings, directions, or boolean flags. Accessory-specific frequencies and speeds and verified native profiles remain intact. Loading preserves authored values and the previous missing-field fallbacks. TotK reaction inspection still applies a frequency only when explicitly requested.

## Mass-aware generated drag

The 2026-10-28 paired survey loaded the HKCL referenced by each of those 74
resources and matched all 337 profiles by cloth name. All had positive total
mass and enabled, positive drag. `wind_drag / totalMass` had median 4, mean
4.92322 and range 0.25-20. Hair alone had median 4 (245 profiles).
The reproducible check is `--survey-wind-mass <actor-pack-directory> <output>`;
it writes `armor-wind-mass.json` without changing the source packs.

BotW's custom wind action divides the force by total cloth mass, as described
in [the runtime notes](BOTW_CLOTH_WIND_RUNTIME.md). Generated fallback profiles
now use `min(previous category drag, 4 * totalMass)`. The previous limit is 5 for
earrings and 8 otherwise. This brings lightweight conversions toward the vanilla
armor median without making heavier cloths windier than before. Zero-mass
cloths get zero drag and disabled wind; negative or nonfinite masses are rejected.
There is no minimum drag floor that could overwhelm an extremely light cloth.

The mass comes from the currently loaded cloth's runtime data, including fixed
particle mass where the file includes it. When exporting directly from BPHCL,
this is the BPHCL mass, not a prediction of later conversion settings. Re-export
from the final HKCL after any conversion or mass edits for matching defaults.
Verified catalog profiles and manually authored/loaded BPHYSICS settings are
not recalibrated. Export does not edit particle masses, constraints or geometry.

This is a conservative armor-based starting point, not an exact translation of
TotK reaction curves. Existing sidecars need to be re-exported to use it, and
individual cloths may still need in-game tuning.
