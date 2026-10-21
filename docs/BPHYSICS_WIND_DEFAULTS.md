# BPHYSICS Wind Defaults

New generic exports use sub-wind speed 25 instead of 50. This is intentionally gentler, not a change to existing files or the preview solver.

A read-only survey of the local Wii U v208 update's armor actor packs on 2026-10-11 inspected 415 packs without errors. Deduplicating by referenced HKCL resource left 74 resources and 337 cloth profiles, avoiding repeated upgrade aliases. These are sampled armor statistics, not a claim about every game resource or exact force equivalence with TotK.

| Field | Mean | Median | Generic default |
| --- | ---: | ---: | ---: |
| Shared sub-wind speed | 23.27 | 20 | 25 |
| Shared sub-wind frequency | 2.98 | 5 | 5 |
| Cloth wind frequency | 4.13 | 4.5 | 4.5 |
| Cloth wind drag | 7.87 | 8 | 8 |
| Cloth minimum wind speed | -2.31 | -2 | -2 |
| Cloth maximum wind speed | 11.74 | 12 | 12 |
| Main sub-wind factor | 0.87 | 1 | 1 |
| Additional sub-wind factor | 0 | 0 | 0 |

The shared values count each resource once; cloth values count each cloth once. Use medians for a robust central profile, with speed 25 chosen as requested and close to the resource mean. Do not average bone bindings, directions, or boolean flags. Existing accessory-specific fallbacks and verified native profiles remain intact. Loading preserves authored values and the previous missing-field fallbacks. TotK reaction inspection still applies a frequency only when explicitly requested.
