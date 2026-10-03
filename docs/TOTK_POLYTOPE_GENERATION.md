# TotK Polytope Generation

## Evidence

The local 2026-09-30 survey read 15,062 actor packs and loose Phive/ShapeParam resources without modifying the dump. Deduplication by SHA-256 found 11,198 unique ShapeParams, all little-endian BYML version 7. Of these, 1,465 contain a Polytope array (some empty); the arrays contain 6,739 shapes.

Every inspected polytope AutoCalc used zero Axis, volume equal to its cached bounding-box dimensions multiplied together, and unit-mass tensor components `(dy^2 + dz^2)/12`, `(dx^2 + dz^2)/12`, `(dx^2 + dy^2)/12`. Comparisons used 0.1% relative tolerance with a 0.00001 absolute floor. This is an authored approximation, not solid-hull integration. Some vanilla cached bounds differ from raw vertex extrema, so generation uses the generated hull's extrema; it does not claim to reproduce Nintendo's hull conditioning.

Observed optional fields include ConvexRadius (0.025, 0.425, 0.56), ShrinkByRadius (explicit false), offsets, names, and MassDistributionFactor. No observed polytope contained OffsetScale, although the importer accepts it for add-on interoperability. The generator bakes transforms into vertices and emits identity rotation/translation, with no invented OffsetScale field. Optional margin/shrink fields are omitted to use game defaults.

The supplied Polytope IO add-on was examined as a format reference, not copied into the application. Its fixed volume/tensor values, bounds/vertex axis inconsistency, per-object reset of duplicate-name tracking, and lack of convex-hull construction are not carried into the generator.

## Workflow

1. Open Collision and choose TotK polytope (.bgyml).
2. Import a collision model. The existing Assimp importer bakes scene-node transforms. Choose Y-up for imported game-space geometry, or Z-up for raw Blender-space coordinates. Do not convert twice if the exporter already produced Y-up geometry.
3. Set mesh inclusion and comma-separated material preset names, plus scale and game-space XYZ offset. Material_Wood remains the default. Preset identifiers are not validated against an installed dump; custom presets must exist in the target mod.
4. Preview the actual convex hulls or export them to DAE/FBX, then create the ShapeParam. Save as .bgyml for binary output or .yaml for editable text.
5. Add/replace the file at the intended Phive/ShapeParam path in the actor pack or mod, and wire the actor's rigid-body/controller references and resource table with the appropriate modding tools. PhysicsTool does not modify the dump or install the file for you.

Blender conversion is the right-handed rotation `(x, y, z) -> (x, z, -y)`, followed by positive uniform scale and game-space offset. All bounds and properties derive from these same transformed points. Equal-position seam vertices are welded for connectivity; disconnected triangle components get independent convex hulls. A connected concave surface becomes a convex hull, not an automatically decomposed solid. One hull fills holes and recesses.

MIConvexHull handles hull construction in normalized coordinates. Non-finite input, invalid topology, no selection, coplanar/degenerate solids, numeric overflow, and oversized inputs are rejected before writing. Limits of 100,000 points/component, 1,024 hull vertices, and 4,096 shapes are tool safety limits, not claims about engine limits. The importer only accepts polytope-only files with no unresolved $parent; it deliberately refuses partial mixed-shape imports.

Each shape uses the measured box AutoCalc convention. Compound center and diagonal inertia are combined using box-volume weights, MassDistributionFactor=1, and the parallel-axis theorem. Compound off-diagonal terms are not encoded. This aggregation and the game's convex margin/shrink behavior still require in-game verification. Existing special flags, collision margins, mass factors, and tracking metadata are not retained when importing geometry and generating a new file.

## Verification

`PhysicsTool.exe --self-test-totk-polytopes <output-folder>` tests boxes, disconnected parts, duplicate names, transforms, compound inertia, binary/YAML round trips, rejection cases, and dialog rendering.

`PhysicsTool.exe --survey-totk-polytopes <romfs-folder> <output-folder>` reads the dump's Zstandard dictionaries and actor packs, reports schema/material counts, compares cached AutoCalc fields, and validates/regenerates supported polytope-only files. Reports and a small sample set stay in the specified local folder; no game assets are bundled in the repository or application.

Tests validate serialization, geometry, and known schema conventions. They do not establish full game-engine compatibility. PhshMesh/TmbMesh generation and automatic convex decomposition remain separate work.

The vanilla sweep reconstructed 6,727 of 6,739 individual hulls and successfully regenerated 1,320 polytope-only ShapeParams. Twelve shapes (eight otherwise polytope-only files) were rejected by the solid-hull checks, including three-point and flat/thin shapes. These special cases remain unsupported; survey.json records their names rather than silently dropping them.
