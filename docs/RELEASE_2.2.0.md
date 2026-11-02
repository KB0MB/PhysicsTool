# PhysicsTool 2.2.0

## What's New

- Added an experimental TotK shrine editor with textured models, scene search, WASD camera controls and matching collision loading.
- Added shrine actor transforms, duplication, deletion and undo/redo, with supported collision updated alongside actors.
- Added editing for existing GroundControl groups and BPHSC collision data.
- Moved collision tools into the main window with Back navigation.
- Added a BPHYSICS editor and improved mass-aware wind exports and BotW wind previews.
- Expanded BPHCL editing with cloth fitting, model-bone matching and bone-branch resizing.
- Added whole-file resizing through All at the top of the cloth dropdown, with safe handling of shared colliders.
- Fixed collider alignment, simulation and deletion on large BPHCL cloths.
- Added actor model/physics exports and animation BFRES/SBFRES imports.
- Fixed Link: bone binding and repeated cloth deletion shortcuts.
- Added remembered window placement and Maximize on startup.
- Updated the editing documentation and translations.

The shrine editor supports existing collision layouts. Creating new BPHSC collision geometry is not supported yet. Edited shrines save to a separate mod folder.

## Running PhysicsTool

Extract the whole ZIP before starting the app. Each build includes the .NET runtime and all four languages.

- Windows: use win-x64 for most PCs, or win-arm64 for Windows on ARM.
- Linux: choose linux-x64 or linux-arm64. A glibc-based desktop with ICU, X11/XWayland, fontconfig and working OpenGL drivers is required.
- macOS 15 or later: use osx-arm64 for Apple silicon or osx-x64 for Intel, then open PhysicsTool.app. The app is not notarized.

Simulation and materials are previews and may differ from the games. Make sure to keep an original copy as a backup!
