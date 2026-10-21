# Player armor simulation checks

TotK armor physics can use namespaced bones (`Link:Head`, `Link:Waist`)
while the BFRES uses unqualified names. Actor preview now resolves exact
names first, then unambiguous namespace aliases. Only preview names change:
bone indices, bind frames, and source files remain untouched. Input animation,
cloth-output animation suppression, collider posing, and output overrides use
the resolved names consistently.

## Compressible links

The TotK executable's constraint registration at `0x710424ca50` leads through
the class vtable at `0x710425ec88` to the solve method at `0x71003dd6c0`.
Its records contain particle indices at offsets 0/2, rest length at 4,
compression length at 8, and stiffness at 12.

The solver leaves separation inside the compression/rest interval unchanged.
Below the compression length it pushes apart; above the rest length it pulls
together. The positional correction is multiplied by stiffness, runtime
adaptation, and each endpoint's inverse mass without normalizing their sum.
Coincident endpoints receive no directional correction. The preview preserves
the compression field for both HKCL and BPHCL and applies this equation.
Other unsupported constraint families remain explicitly blocked.

## Validation

- Synthetic limits, boundary values, zero separation, unequal inverse masses,
  fixed endpoints, numeric armor ordering, and ambiguous name rejection.
- Native `Armor_1151_Head / Armor_116_Upper / Armor_001_Lower`, animated with
  `Assassin_DotonCharge` for 90 steps including loops: all cloth bones matched,
  fixed anchors aligned, moving hair/tunic outputs, no numerical resets.
- Default Player outfit: compressible-link hair loads without being skipped.
- GPU captures of the reported outfit at frame 24, simulation off and on.

These are preview regression checks, not proof of full in-game solver parity.
