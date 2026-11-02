# BotW cloth wind

## Executable evidence

Read-only analysis of the local Switch `main`, SHA-256
`15cfca7b89348956f85d945fade2e215a6af5991ed1071e181f97ca72f7ae20b`:

- `7101ca03e0`: cloth setup creates a custom action, not `hclSimpleWindAction`.
- `7101ca3f60`: caller caps incoming world-wind magnitude at 20; passes shared
  sub-wind speed/direction/frequency, per-cloth main/add factors and a runtime
  drag multiplier. That multiplier starts at 1.
- `7101ca6440`: evaluates BPHYSICS wind, enabling/disabling the action.
- `7101ca6760` / `7101ca6880`: force application, with the latter a this-adjusted
  entry. There is no surface-normal multiplier in this game-specific action.
- `7101e080c4`: generic Havok simple-wind action, a different path. It uses
  normals when present and 0.7 otherwise; do not use it for the BotW sidecar.

The parameter object fields at +48/+68/+88/+A8/+108 are drag, frequency,
minimum speed, maximum speed and enabled. +C8/+E8 hold the two sub-wind factors.
Addresses are hexadecimal and specific to the executable above.

## Recovered calculation

Let `v` be main wind speed, `d` its normalized direction, and
`a = clamp(2 * v / 30 + 0.1, 0, 1)`.

Each update advances two wrapping 32-bit phases, with a shared random factor
uniformly distributed in [0, 2):

- Main: `frequency * dt * a * randomFactor` turns.
- Sub-wind: `subWindFrequency * dt * randomFactor` turns.

Sine is evaluated using a 256-segment interpolated table. Main air speed is
`min + (max - min) * (1 + a * sin(mainPhase)) / 2`.

The sub-wind pulse speed is
`subWindSpeed * (addFactor + a * mainFactor) * max(0, sin(subPhase))^8`.
Its normalized direction is transformed by the actor's orientation in the
game. The pulse velocity is added to the main air velocity.

Effective drag is
`runtimeMultiplier * windDrag * (v / 30 + addFactor * pulseSpeed / 30)`.

For each particle:

`force += (airVelocity - particleVelocity) * effectiveDrag * particleMass / totalMass`

The integrator applies inverse mass to this force. For reciprocal mass/inverse
mass, individual mass cancels, but **total cloth mass does not**. Increasing the
total mass therefore reduces response to a fixed wind profile. Changing mass
also changes mass-weighted constraints, so it is not a wind-only adjustment.

The action is inactive when `wind_enable` is false, or when both main wind and
the additive factor are zero. Main sub-wind alone does not activate wind in
completely calm conditions. The constants 20 and 30 here are wind magnitude
limits/normalizers, not animation frame rates.

## Preview integration and limits

BotW actors use their exact-name BPHYSICS profile automatically. Standalone HKCL
uses **Options > Load BPHYSICS**; selecting another cloth selects its matching
profile. A missing or ambiguous match keeps the generic preview. Loading a
profile does not edit or save the physics document. New/open/clear resets it;
swapping documents carries it with the document.

Wind speed controls the incoming main wind. Profile frequency supplies gusts,
so the generic gustiness slider is disabled for a matched profile. Wind off
removes both main and sub-wind. TotK preview remains unchanged.

This is not a full game-environment reproduction: the preview uses its own RNG,
the selected direction/speed instead of the weather system, a runtime multiplier
of 1, and the loaded actor's model-space orientation. It does not emulate actor
reaction state transitions. Sine-table values are computed mathematically;
bit-identical game random sequences are not expected. The preview additionally
caps drag per integration step to avoid reversing velocity on extremely light
or malformed data. Existing solver excursion/speed safeguards still apply.

Wii U runtime equivalence has not been independently checked in this pass.

## Armor_999_Head audit

The vanilla TotK actor contains 16 cloths. Donor-free conversion preserves their
source masses, and each converted total agrees with the sum of its particles.
Totals range from 0.12 to 3.003117. Before mass-aware export, generic hair exports gave every one
drag 8 and main sub-wind factor 1 with shared sub-wind speed 25. Consequently
`drag / totalMass` ranges from about 2.66 to 66.67 before world-wind normalization.
Those settings were therefore not equal-response settings for these cloths.

TotK's actor contains per-cloth reaction curves and differing drag/sub-wind
values, which cannot be faithfully reduced to that generic constant profile.
This is evidence of a calibration gap, not proof that the user's unidentified
Hair_999 output has corrupt masses. Generated fallback drag now uses
`min(previous default, 4 * totalMass)`, calibrated against 337 matched vanilla
armor profiles. For the 0.12-mass cloth, drag becomes 0.48 instead of 8.
No particle mass scaling is performed, and verified or authored profiles are
unchanged. See [the default calibration](BPHYSICS_WIND_DEFAULTS.md).
The actual converted HKCL and installed BPHYSICS are still needed to diagnose
that specific in-game result.

Regression checks cover the force equation, total-mass response, relative
velocity, fixed particles, enabled flags, zero-wind gating, positive-only pulse,
phase reset, main-wind cap, exact/ambiguous names, session lifecycle and solver
integration. The conversion audit is available as `--inspect-converted-wind`.
