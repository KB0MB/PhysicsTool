BotW uking_mat shader reconstruction by Killz.
Supplied by the user as Downloads/BOTW Shader.zip on 2026-11-18.
The original 20 shader files are preserved verbatim.

PhysicsTool integration:
- BotwShaderSource expands includes and the two token-pasting macros, adapts
  uniform blocks to named uniforms, and selects the forward material pass.
- Geometry is already CPU skinned, so gsys_weight is zero at draw time.
- BFRES static options, material parameters, texture assignments, UV attributes
  and texture SRTs feed the original material program.
- sRGB textures decode on upload/sampling and the final linear output is encoded
  to sRGB for the viewport. Original shader calculations remain linear.
- Scene lighting is a neutral preview rig, not a reproduction of in-game time,
  weather, scene depth, shadow maps, light analysis or reflection captures.
  Neutral textures satisfy those scene sampler bindings. Texture-array terrain
  data and scene-dependent refraction are not reconstructed by this integration.
- Non-uking_mat archives retain the previous renderer. TotK/OoT are unchanged.

No license text was included in the supplied archive. On 2026-11-21, the
project maintainer confirmed Killz approved inclusion of these shader sources
in the public PhysicsTool repository and release ZIPs. Credit belongs to Killz;
this notice does not assign an additional license to the supplied shader files.
