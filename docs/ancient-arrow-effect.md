# Ancient Arrow Blade Preview

The player-equipment preview reads the blade from the user's BotW dump. No
resident effects or game textures are included in the application.

## Binding and Timing

- ActorLink `ElinkUser` resolves AncientArrow to the `FinishArrow` ELink user.
- Bootup's `ELink2/ELink2DB.sbelnk` supplies `Blade_00`, its
  `AncientArrow_Blade01` asset, `Blade_Base` bone, and local position.
- `BladeOpen` starts the effect at frame 10. The equipment pose and effect use
  the same Link bow-animation sample, including explicit timeline seeks.
- Paused, reverse, and repeated seeks evaluate deterministically. Leaving bow
  charge/draw/release disables the blade; changing equipment removes its mesh.

## Native Emitter

`Effect/GameResident.sesetlist` is VFXB version 20 on Switch and EFTB version 20
on Wii U. The supported emitter is `blade_left_a`, an infinite-life plate with
no particle motion. Its native dimensions, pivot, Euler rotation, translation,
two colors, texture scrolling, and EAC0/EAC1/EAA0 curves are read at runtime.

Texture IDs resolve through GTNT/BNTX on Switch and TEXR/GX2B on Wii U.
The BC5 blade is luminance/alpha; the BC4 scrolling texture is alpha. Decoded
RGBA buffers match byte-for-byte between the tested Switch and Wii U dumps.

The resident shader variation 495 was inspected from the local Switch archive:

```
rgb = mix(color1.rgb, color0.rgb, texture0.rgb)
alpha = clamp((texture0.a * texture1.a - color0.a) * color1.a, 0, 1)
```

EAA0 changes the dissolve threshold, not the final opacity. Quad corners are
`(+/-0.5 + pivot * 0.5) * size`, with flipped texture V. This is not a
camera-facing flare billboard.

The preview is SDR. A hue-preserving peak compression is applied after the
native particle calculation to avoid clipping HDR blue into white. This is a
preview display transform, not a reproduction of the game's full HDR/bloom
pipeline. The surrounding flare, burner, light, projectile trail, and impact
emitters are not part of this narrowly scoped blade implementation.

## References and Checks

Format research used KillzXGaming's [EffectLibrary](https://github.com/KillzXGaming/EffectLibrary)
and [xlink2](https://github.com/dt-12345/xlink2) as external inspection tools.
Neither tool is a runtime dependency or bundled with PhysicsTool.

`--equipment-state-check ROOT` checks native binding, the trigger boundary,
scrubbing, visibility, texture channels, and equipment replacement.
`--auto-materials-gpu ROOT OUTPUT` captures the activated and held blade,
checks that it contributes blue/cyan pixels, and exercises other equipment
material animations. Run both against Switch and Wii U roots.
