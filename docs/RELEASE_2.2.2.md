# PhysicsTool 2.2.2

## Icon Tool

- Added an Icon Tool for BotW and TotK actors, including actors without physics.
- Export icons as PNG, BotW `.sbitemico`, or TotK `.bntx` / `.bntx.zs`, directly to a mod or folder.
- Includes item capture presets, armor poses, mannequin props and mesh visibility controls.
- Supports armor dyes, with batch export for all colors.
- Supports custom presets, character mannequins and BFSKA pose imports.
- Includes gravity simulation for cloth and hair.

## Actor Preview

- Equipment dropdowns now use item names, alphabetical sorting and keyboard jumping.
- Removed duplicate armor upgrades that use the same model.
- Added BotW arrow-type selection.
- Added closed states for stowed weapons and synchronized bow/Ancient Arrow animations.
- Added the Ancient Arrow's blue blade effect using native effect data.
- Added automatic playback of `_auto` material animations.
- Switching animations during playback now continues playing.
- Equipped/stowed switching no longer requires rebuilding the entire actor preview.
- Improved timeline scrubbing responsiveness.
- Added hover highlighting to the viewport axis controls.
- Emission is enabled by default; particles and colliders are hidden by default.

## Rendering

- Integrated Killz's reverse-engineered TotK material shaders, with compatibility rendering for unsupported materials.
- Improved animated texture transforms, material bindings and icon shadow rendering.
- Improved TotK icon lighting without changing BotW's palette.

## Credits

Special thanks to **Killz** for reverse-engineering and sharing the BotW and TotK shaders.

