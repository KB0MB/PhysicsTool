# PhysicsTool 2.2.1

## What's New

- Added pose-based editing for BotW support bones and TotK helper bones, with live previews, captured poses, response testing and undo/redo. Detailed connections and curves remain available under Advanced.
- Added experimental OoT support: actor browsing, textured models, animation playback, read-only all-cloth previews and cloth/helper-bone simulation.
- Added OoT cloth and helper editing, including particles, triangles, bone positions, constraints, colliders, helper connections and response-curve keys.
- Added experimental cloth conversion between OoT, BotW and TotK, with warnings for settings that cannot be carried across.
- Expanded Map Editing with TotK MainField terrain, textured ground, water, caves and wells, plus linked-actor lines when Actor origins is enabled.
- Added placement-only map editing and a searchable Add Actor menu with Static/Dynamic destinations, model previews and undo/redo. Baked collision remains unchanged.
- Separated Map Editing from Collision, filtered map files without actor placements, protected oversized MergeParts geometry and changed shrine scaling to Shift+S.
- Added BotW GameROMPlayer equipment viewing with armor, weapons, sheaths and quivers, using BFRES material-hiding rules.
- Added a BotW/TotK actor physics selection and export menu.
- Integrated Killz's reverse-engineered BotW shaders. Thank you to Killz for sharing the shaders and approving redistribution.
- Corrected BotW material UV selection, skin shading, texture color space and shared armor textures; fixed support-bone deformation on armor such as Snowquill (009).
- Changed the 30/60 FPS setting to control viewport updates while animations retain their game-specific playback rates.

## Preview Limitations

OoT simulation and cross-game conversion remain experimental. Native cloth behavior is not fully reproduced; Malon Adult's sitting skirt is a known remaining mismatch. Test converted files in game and retain original backups.

Placement-only map edits do not create or update baked collision. Cave and well surfaces are read-only previews.

BotW shaders are based on Killz's reverse engineering. The preview uses neutral lighting, not the game's complete weather, shadows, reflections or post-processing.

## Running PhysicsTool

Extract the whole ZIP before starting the app. Each build includes the .NET runtime.

- Windows: use win-x64 for most PCs, or win-arm64 for Windows on ARM.
- Linux: choose linux-x64 or linux-arm64. A glibc desktop with ICU, X11/XWayland, fontconfig and working OpenGL drivers is required.
- macOS 15 or later: use osx-arm64 for Apple silicon or osx-x64 for Intel. The app is not notarized.

ARM64 and macOS cross-builds still need native UI/GPU validation.

## Credits

Special thanks to **Killz** for the reverse-engineered BotW shaders and permission to include them in PhysicsTool's source repository and release ZIPs. See THIRD_PARTY_NOTICES.txt and Licenses/Killz-BotW-Shaders.txt for attribution and provenance.
