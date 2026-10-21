# Changelog

## 2.1.0

- Added Textured, Wireframe and Edged Faces display modes for imported models and actor previews.
- Replaced Hide cloth with independent Particles, Bones and Colliders visibility controls.
- Fixed G/R/S shortcuts after selecting particles, bones or colliders from the editor list, without needing another viewport click.
- Added FBX export for individual HKCL cloths, including the skeleton, particle mesh and separate bone-attached colliders.
- Added Match bones to model for HKCL, with selectable bones, particle and constraint fitting, and undo/redo. Cloth bone edits now leave the loaded BFRES unchanged outside simulation.
- Fixed cloth jitter and unstable simulation while dragging the animation timeline. Playback and simulation now resume cleanly when scrubbing ends.
- Added a Weapons tab for Link, with swords, longswords, spears, shields and bows, drawn or stowed placement, matching sheaths, quivers and arrows.
- Simplified armor choices to their numbers, sorted them numerically, and made typing a number jump to that armor.
- Improved TotK helper-bone motion and cloth alignment in bent-leg poses, including Link's equipped tunic. Fixed helper ordering and preserved helper poses when starting cloth simulation.
- Fixed equipped Player hair and tunics failing to follow animation or simulate correctly when cloth bone names include a namespace.
- Added compressible-link simulation support, fixing skipped hair cloth on the default Player head.
- Added default armor when opening TotK's Player actor, with separate head, upper-body and lower-body armor choices. Equipped pieces bring their textures and physics, respect hidden body parts, and keep the current camera and animation frame when swapped.
- Fixed TotK actors loading unrelated cloth and helper-bone variants together, which could pull accessories away from their attachment points and slow down the viewport.
- Improved cloth and collider drawing performance in actor viewing and editing, including smoother camera orbiting with many cloths visible.
- Added bone-branch resizing with a live XYZ scale popup, optional collider resizing, and undo. Bone positions, particles and constraints resize together without changing bone scale values.
- Fixed body meshes crumpling during simulation when a BFRES contains multiple skeletons with different resting poses.
- Fixed switching cloths moving the model when their skeletons have different proportions, and kept the camera in place when using the cloth dropdown.
- Fixed imported models appearing below or offset from their cloth when their skeletons use different origins.
- Added Fit cloth to bones to reshape HKCL particles and rest constraints after editing simulation bones.
- Reduced default BPHYSICS sub-wind speed and balanced general wind settings against vanilla armor profiles.
- Kept the original filename when converting between physics formats.
- Made Wii U the initial HKCL conversion target and remembered the last successfully used platform.
- Added BPHYSICS export with editable wind settings and TotK actor reaction inspection, including matching base bones and selectable reaction frequencies.
- Used the searchable actor browser to choose TotK cloth reactions instead of a file picker.
- Updated the editing documentation and translations, and moved Shortcuts directly after Start here.

## 2.0

### Interface

- Updated the UI to Avalonia while keeping the familiar editor layout.
- Improved the light and dark themes, window sizing and readability.
- Grouped Save, Export and Convert under File, and simplified the donor controls.
- Added drag-and-drop support for physics files.
- Expanded French, Spanish and Japanese translations, including all in-app documentation and constraint descriptions.
- Added searchable documentation covering cloth editing, constraints, colliders, helper bones and shortcuts.

### Cloth and Bone Editing

- Added blank cloth creation for BotW and TotK, with tools to place particles and add constraints and triangles.
- Expanded BPHCL particle editing, including adding, deleting, copying and pasting particles in supported cloths.
- Added editing for supported constraint lengths, stiffness and movement limits, with descriptions of each constraint type.
- Improved multi-selection, copy/paste, mirrored paste and undo/redo for particles, bones and colliders.
- Added whole-cloth mirroring for supported cloths and corrected several left/right alignment issues.
- Added Attach to Bone and a Particles follow bones toggle. Colliders also follow edits to their bones and parent bones.
- Added bone rotation editing in radians.
- Fixed moved particles returning to their old positions when simulation started.
- Fixed the Fixed checkbox needing two clicks and constraint buttons staying disabled after simulation stopped.

### Helper Bones and Ragdolls

- Expanded support/helper-bone editing with group creation, duplication, mirroring, reordering and deletion.
- Added individual helper deletion, connection editing and new response curves for supported helper types.
- Added a live curve graph, named driver bones and response presets to make helper behavior easier to adjust.
- Added helper previews alongside model animations.
- Expanded ragdoll editing with bone pose changes, multi-selection transforms and skeleton alignment.
- Added ragdoll motion previews with wind controls.

### Simulation and Conversion

- Fixed actor animations changing when simulation was enabled, including Kass's body and foot movement.
- Fixed BotW cloth particles and colliders drifting out of alignment with animated models when simulation was off.
- Greatly improved cloth and helper-bone simulation, bringing their behavior closer to the games.
- Added TotK cloth simulation previews for supported BPHCL files.
- Improved capsule collisions and fixed gaps between the displayed collider shape and its simulated surface.
- Improved wind controls and animation-loop handling, and reduced actor-preview lag.
- Improved cloth conversion between HKCL and BPHCL, including particle mass, cloth softness, bending and collision behavior.
- Improved conversion of long cloth pieces such as scarves, ribbons and parasail trails.
- Improved conversion between BotW support bones and TotK helper bones.
- Added mixed-format donor merging between HKCL and BPHCL.

### Models and Actors

- Added BotW and TotK actor browsing with separate game folders, search and cloth/helper-bone filtering.
- Opened actors in the main viewport, with mesh visibility controls and animation playback.
- Added BFRES/SBFRES model and animation loading, including companion textures and TotK TexToGo textures.
- Improved BotW and TotK material previews, texture placement and actor-specific appearances.
- Fixed missing clothing on some TotK actors and red grayscale textures, including Purah's hair.
- Added an Emission toggle, disabled by default.
- Fixed TotK particles staying behind during model animations.

### Viewport and Collision Tools

- Added a timeline with scrubbing, play/pause, looping and 30/60 FPS playback settings.
- Improved camera navigation, smooth axis-view transitions and Frame Selected zooming.
- Fixed the grid moving or changing spacing with the camera.
- Added translucent collider surfaces and clearer bone rendering.
- Improved move, rotate and scale controls, with colored axis guides and Shift+axis plane constraints.
- Fixed undo/redo moving the camera or losing multi-selection.
- Added a collision workspace for importing meshes, generating collision, previewing it in 3D and exporting geometry.
- Added TotK polytope collision generation with material choices, separate shapes and scale/offset controls.
