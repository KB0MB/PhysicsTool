# TotK Material Shaders

The GLSL in `Rendering/Shaders/TotkNative` was reverse-engineered and supplied by
**Killz** in `TOTK.zip`. The original shader files are preserved unchanged.

PhysicsTool selects this material path for compatible TotK `material` archives.
OOT and BotW retain their separate renderers. The integration supplies material
options (including unsigned expression hashes), authored parameters, native SRT
matrices, sampler states, and all eight material textures. Automatic material
animation updates the same parameter storage without rebuilding geometry.

`TotkShaderSource` adapts the source to desktop GLSL 150 and OpenGL ES 3.0. The
viewport already skins the vertices, so the shader consumes that geometry rather
than applying the skeleton a second time. Scene-shading storage is represented by
uniforms, and absent world textures use neutral fallbacks. The isolated preview
does not reproduce the game's fog, Depths lighting, terrain buffers, or complete
HDR/post-processing pipeline. Brightness remains the editor's existing control.
The Icon Tool supplies its actor depth map to the native cascade sampler and
exposes the same lighting palettes and shadow toggle as BotW. Mixed BotW props
and TotK armor share that depth pass, including alpha-tested cutouts.

For TotK's icon palette, scene-shading slots 36 and 40 receive integrated ambient
fill rather than raw hemisphere sky/ground colors. The shader interpolates these
slots over screen Y, so passing the raw colors produced a blue/purple screen-space
cast and excessive fill. This host-side fill is an approximation pending native
light-analyzer integration, not recovered game analyzer output. It is scoped to
TotK's native icon-palette path; BotW and other palettes are unchanged.
The TotK icon key uses a reference-calibrated RGB gain of (1.3, 1.3, 1) to bring
the lit cloth closer to the vanilla icons without lifting shadows. This is a
preview calibration, not an extracted native exposure or shader constant.

Color-bearing UNORM TXTG slots retain the earlier preview's sRGB color
interpretation; normal and mask slots remain linear. This is a preview texture
view policy, not a change to the game files or a claim about their NVN descriptors.

## Coverage

The supplied archive contains a subset of decoded expression graphs and
procedural textures. A material that needs a missing graph or procedural type
uses the previous compatibility shader instead of rendering black. For example,
the Master Sword blade's graphs are not present in this archive. The guard checks
whether a graph is actually consumed; an unused graph ID does not disable native
shading. No approximation has been added to Killz's GLSL to invent missing graphs.

The integration uses the forward material pass for previewing compatible actors;
it does not implement a full deferred world renderer. The other supplied passes
remain in the archive for future integration.

## Checks

`--totk-native-gpu [romfs output-folder] [--desktop-gl]` tests six material behaviors,
UV selection, SRT changes and restoration, emission, and alpha cutouts. With a
dump supplied it captures Player, Hylian Tunic, Master Sword, Hylian Shield, and
Bokoblin, and reports native versus compatibility material counts.
