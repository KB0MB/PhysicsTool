# BotW Material Preview

The supplied experimental `Model.frag` is integrated as
`src/PhysicsTool.Desktop/Rendering/Shaders/BOTW.frag`.
Its GLSL equations are retained; only the introductory integration comments
were adjusted. This is a BotW-inspired preview, not recovered game GLSL.

## Selection and UVs

- BFRES materials referencing a `uking_` shader archive use `BOTW.frag`.
  This applies to Actor import and standalone BFRES/SBFRES model import.
- Other materials continue to use `Model.frag`. Both programs share the vertex
  interface but keep independent uniform locations and GL program lifetimes.
- Standard/cloth materials use the supplied preview lighting. The supplied
  shader retains the existing specialized skin, hair, eye and eyelash paths.
- The host reads `uking_texture0_texcoord`, `uking_texture1_texcoord` and
  `uking_texture2_texcoord` for albedo, SPM and normal UV channels, respectively.
  BFRES attribute assignments resolve shader UV names to stored vertex UV sets.
- Normal-map tangents are generated from the selected normal UVs. Missing,
  non-integral and unsupported channel selections retain the previous preview
  fallback and report a warning where the importer has a warning collector.
- Actor texture variations still select their fixed material-animation frame.
  Skeletal animation and simulation do not overwrite those bindings.

## Limits

The fragment file alone does not implement BFRES shader options or reconstruct
the game's material pipeline. Procedural/projected UV generation, UV SRT and
texture animation, extra material layers, emission, shader-parameter variants,
scene shadows/probes and game-specific tone mapping remain outside this pass.
AO and auxiliary UV routing retain their existing settings. SPM sampling keeps
the supplied preview's color-space contract, not a claim about the game's
original sampler format. Specialized material classification still uses the
existing material-name heuristic.

The shader is embedded at build time. Editing `BOTW.frag` requires rebuilding;
there is no dependency on the external source folder at runtime.

## Verification

- Generated tests: independent texture UV channels, BFRES attribute remaps,
  normal-map tangents, unknown-game fallback, material cloning, invalid selectors.
- Native material tests: Switch/Wii U Bokoblins and Zelda BFRES.
- Real GPU tests on Windows: program compilation, standard/BotW program
  switching, textured actors, skeletal animation, simulation and compact views.
- The full migration regression suite also covers the unchanged generic shader.
  Linux/macOS runtime validation remains pending.

Run the UV checks with `--botw-materials` on `PhysicsTool.Checks`; optional
BFRES/SBFRES or actor-pack paths add native fixture checks. Use `--gpu-actor`
or `--gpu-animation` for the corresponding rendered tests.
