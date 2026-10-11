# Changelog

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
