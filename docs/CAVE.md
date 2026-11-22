# Cave Surface Preview

MainField scenes load intersecting `Cave_` and `Well_` resources from
`Cave/cave017`. The loader reads `C.crbin`, selects complete caves intersecting
the map cell, and decodes their finest-level chunk pages through the separate MeshCodec
helper. Decoded positions are already in world space; applying the entrance
actor transform again would misplace the shell.

Meshes use `cave:` names, with no editable actor index. Geometry is grouped by
100-metre region and texture layer for culling and reduced draw calls. Cave
files are never rewritten by scene saves. Missing or unsupported resources
produce asset notices rather than partially publishing a cave.

## Materials and Limits

The preview uses the dominant packed material and its UV projection from
`MaterialAlb.txtg`. It does not reproduce the game's three-material blending,
vertex coloration, or cave shader. Sharp material boundaries are expected.
Only cave/well resources are included; SkyIsland resources are not loaded.

Index files, pages, counts, indices and finite coordinates are validated.
Preview limits are 16 MiB per index, 64 MiB per page, 256 MiB decoded pages per
cave, two million triangles per cave, and four million vertices for all caves
in a scene. A cave exceeding these limits is skipped with an asset notice.

## Validation

Run `PhysicsTool.Checks --caves <romfs>` to check native I-4 geometry, the
Lanayru 0045 entrance alignment, malformed input rejection, decoder recovery,
batching, cancellation and source preservation. The native page-table entries
are **decoded size followed by page ID**, both little-endian uint32 values.
The existing `--meshcodec` checks also cover the unchanged BFRES decoder path.

`--cave-geometry <romfs>` checks both native A-2 caves, including every finest-level
vertex against its chunk bounds, triangle spans, and geometry crossing the map-cell
edge. The Hebra 0000 fixture exercises packed triangle deltas with vertex references;
an incorrect 16-bit pointer advance previously produced 78-metre spikes there.
`--gpu-caves <romfs> <output>` captures both A-2 caves and checks orbit/resize rendering.

Native synthetic checks require no game assets: configure MeshCodec with
`-DPHYSICSTOOL_BUILD_TESTS=ON`, build, then run `ctest -C Release --output-on-failure`.
These cover wide attribute masks, shifted backreferences, and consecutive delta triples.

Layout references: [BladesawStudios/TotKCave](https://github.com/BladesawStudios/TotKCave)
and Starlight's `CrbinFile` declarations. These projects are credited for the
format research; no additional cave-decoder library is shipped. The existing
MeshCodec helper retains its own source and license notices.
