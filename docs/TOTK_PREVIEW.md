# TotK Material Preview

Select the game's romfs under Actor > TOTK. The loader reads referenced Model/*.bfres.mc, Shader/ExternalBinaryString.bfres.mc, Pack/ZsDic.pack.zs and TexToGo/*.txtg directly. No decoded-model override is required. Only referenced texture surfaces are decoded and cached for the actor's lifetime; the dump is never modified or bundled.

## Material Bindings

TOTK.frag is adapted from Switch Toolbox's BFRES_Totk.frag. Shader sampler assignment order is kept distinct from physical texture-reference order. It supports raw UV0 through UV3 and attribute remapping, per-input generated-texcoord selection, static SRT parameters including the 3dsMax pivot correction, vertex colors, texture channel selectors and color-space metadata. Authored tangents are skinned with the model when the normal UV mapping is unchanged; modified mappings use tangents derived from the transformed UVs.

Static p_texture_array_index0 selects packed TexToGo surfaces. The reader supports the BC1/3/4/5/7 and ASTC formats recognized by Toolbox, including its 8x5/8x6/8x8 setting overrides. Channel selectors are applied once during decoding. Color outputs follow Toolbox's sRGB preview interpretation, including RGB UNORM storage; BC4 masks remain linear. Normal and scalar samples retain their data values. This is a preview policy, not full emulation of every game shader's decode options.

The viewport Emission checkbox starts disabled. Enabling it restores authored TotK emission contributions without changing the loaded materials.

BC4 maps with default RGB selectors are expanded from their red channel to grayscale only when sampled as color (including albedo and eye shadow). Their raw mask values and opacity channels remain unchanged. Explicit channel remaps are honored. This prevents Purah's white hair and eye whites becoming red without recoloring her authored brown iris texture.

## Limits

Lighting uses PhysicsTool's viewport key light and a simple environment approximation, not the game's deferred passes or Toolbox's cubemap. Generated expression IDs use semantic fallbacks; not every expression is implemented. Unevaluated procedural opacity preserves base alpha rather than interpreting dark albedo RGB as coverage. Ganondorf's clothing is therefore visible, but miasma dissolve effects are not reproduced exactly. Material animation, actor-driven parameter overrides, retargeting and state machines remain separate unfinished work. Missing or unsupported textures remain unbound and produce resource warnings.

## Checks

TXTG base-mip block heights follow [tegra_swizzle's inferred layout](https://github.com/ScanMountGoat/tegra_swizzle/blob/master/src/blockheight.rs), including its 16-GOB cap and non-power-of-two boundaries. The portable tests include a nonuniform tall BC1 texture to detect tile rearrangement. `--tegra-layout <Toolbox tegra_swizzle_x64.dll>` independently compares 4096 inferred heights and 90 byte-exact deswizzled surfaces against the native library. This does not add that DLL as an application dependency. `--dump-actor-textures <pack> <output>` exports diagnostic maps and reports sampler/UV bindings.

Ganondorf currently reports unsupported TexToGo formats 0x609 and 0x709 for its noise/body maps. Several of its cloth programs also require unsupported volume/compressible-link kernels or more than 31 collision slots. The missing-clothing render checks pass independently, but this actor does not yet pass the strict all-resources native validation. These gaps are not silently treated as game-accurate simulation.

The regular suite covers shuffled mip/surface tables, BC1 and ASTC color decoding, channel remaps, sRGB flags, malformed headers, ordered sampler assignments, UV/attribute routing, static SRT, vertex-color buffers and material cloning. Native fixtures validate Zelda and Armor_1141_Upper texture loading and simulation. The real GPU check compares texture-enabled/disabled and normal-enabled/disabled pixels, then checks exact restoration, skeletal animation, simulation and compact layout.
