# Icon Capture Research

## Evidence (2026-11-21)

Addresses refer to the local TotK `main` in the `TotK_Cloth_Runtime_20260830`
Ghidra project. This is schema/default evidence, not a complete reconstruction
of the capture renderer. No game binaries or textures belong in this repository.

- `71014cbd00` enumerates ActorInfo fields: Rot, AS, IsUseSkeletalAnim,
  ASFrame, IsActivateCloth, IsUseDetailSetting, IsUseModelUnitSRTOverride.
- `71026e93ec` enumerates CameraInfo fields with default-value pointers.
  `71014ce560` is the inherited-value accessor enumeration for the same fields.
- CameraPos default at `7103815f44`: (0, 0, 10).
- IsSetCameraAt defaults false; CameraAt defaults (0, 0, 0).
- CameraRotate has a two-component type, distinct from the actor's three-axis
  Rot. Its orbit interpretation must not be inferred from the editor's Euler UI.
- CameraFovy default at `7103815f6c`: 5. Near/Far defaults are 0.01/1000,
  with IsSetNearFar false. IsRotateAroundCameraAt also defaults false.
- `71026ea9cc` enumerates CaptureLight fields. Default Dir at `7103815f98`
  is (0.5, -0.5, -0.5).
- DirectionalLightDiffuseColor defaults (1, 0.92156857, 0.74901962, 0),
  DirectionalLightIntensity 8, HemiSkyColor (0.1, 0.25, 0.65, 1),
  HemiGroundColor (4, 3.7, 1.2, 1), HemiIntensity 1.
- UseDirLightDetailSetting and UseHemiDetailSetting both default false.
  These values are not interchangeable with the BotW renderer's multipliers.

## Applied Behavior

ActorInfo.Rot transforms geometry. CameraInfo.CameraPos and CameraFovy control
the capture camera. Native presets use the schema FOV instead of a geometry-fit
guess; Fit remains an explicit user action. Missing native direction uses the
schema default. The bundled `Presets/TotkIconCapture.json` contains 729 saved
weapon and armor presets. Users can select any preset for any loaded actor;
BotW capture does not require a TotK resource folder at runtime.

Viewport mode clears the fixed camera overrides and uses the interactive
viewport matrix for both preview and PNG capture. Orbit, pan, and zoom do not
modify actor geometry. Lighting remains independently editable. Brightness is
a separate 0-4 linear-light multiplier applied after shading (including emission)
and before display encoding. It does not change alpha, camera, or material data.
Preview and exported PNG use the same multiplier; 1 preserves the original render.
The shared preview brightness default is 3. Capture size defaults to 256 pixels
and stays selected when changing presets.

## Remaining Gaps

Armor_009 Head/Upper/Lower were compared using BotW models, Killz's BotW shaders,
and original BotW inventory textures. TotK counterparts provide rotations and
camera positions. BotW armor defaults to unmodified frame 0 of `Nml_Wait`.
The older, optional custom icon pose uses mirrored 15-degree local X upper-arm adjustments and finger
rotations blended 50% toward the model's rest orientations. Animated translations
and scales are preserved. This is a visual approximation, not an extracted
native capture pose. Untouched `Nml_Wait` and rest pose remain selectable.
User-editable local bone rotations permit
shoulder, arm, finger, and spine adjustments, saved separately as JSON. These
are custom adjustments, not claimed as the native inventory pose. Head armor
still lacks the inventory mannequin; geometry is not hidden to conceal this.

The preset selector is a ComboBox using the equipment picker's native incremental
text search. Numeric IDs lead the labels so typing `070` jumps to the Master Sword.
There is no capture search field. Capture zoom scales clip-space X/Y identically
for preview/export; it does not rescale the actor. Fit and preset selection reset it.

The BotW host previously supplied `context.cFovParams=(1,1,1,1)` regardless of
projection. Perspective captures now supply reciprocal projection scales and
their inverses, restoring the shader's view-size-dependent highlight response.
Killz's shader files are unchanged. This improves highlights on Armor_009 but
does not reproduce the pale edge band visible in the inventory reference. The
archive includes material, G-buffer, depth and light-analysis passes, not a full
deferred lighting/compositing pipeline. The edge band's exact source is still
unconfirmed; do not substitute the older preview shader's synthetic Fresnel term
and claim native accuracy.

The optional Edge highlights capture setting uses one-sided depth/coverage
boundaries, tinting only foreground coverage without changing alpha. A radial
falloff softens the band inward, weighted by world-space light direction.
Coplanar material/color joins do not produce lines. Width is relative to model
size in world space, so camera distance and zoom change its projected width.
It is off by default and remains an approximation, not a reconstruction of the
native post-processing pass.

The 2026-11-22 comparison used twelve original BotW icons: Armor 009 (all slots),
Upper 001/006/017/020/048, Sword 001/070, Bow 023, and Shield 001. The bright edges
are selective, not uniform outlines, and blend into the adjacent surface. The
96-pixel stock assets also contain downsampling/coverage effects; these images
alone do not prove which native pass produced the highlights.

Armor_009's belt breakup was reproduced with the old 0.01-to-50000 capture depth
range. Fitting near/far to the camera-space model bounds removes the depth fights
without changing vertices, bone transforms, draw order, or visibility. Camera
position, field of view, and XY framing stay unchanged.

Custom capture presets are JSON files saved/opened beside the preset selector.
They include camera/actor transforms, editable Viewport orbit/pan/zoom, lighting,
brightness, emission, material-animation toggle, PNG size, edges, and pose.
Imported Wii U and Switch BFSKA clips use the existing format readers and native
bone-name skinning. A frame slider selects the sampled pose. Presets embed the
BFSKA bytes and frame, so the animation file is not an external dependency.

Shared armor textures resolve through the player and default armor resources
after the actor's own textures. Armor_009's skin resolves to `Link_Skin_Alb`
and its hook to `Link_Belt_A_Alb` on both BotW Wii U and Switch.

The accessor references inspected so far lead to schema enumeration, not the
runtime pose consumer. Trace model-unit overrides, mannequin assembly and
animation evaluation separately. Do not assume IsUseSkeletalAnim=false means
the entire capture pipeline leaves the rest pose untouched.

Still unverified: runtime projection/look-at handling, camera orbit semantics,
actor rotation order, armor pose application, capture-light environment setup,
and output crop/downsampling. A schema-correct FOV alone is not proof of
pixel-identical icon framing. The saved library currently represents rotation,
camera position/FOV, light direction, and output size, not look-at/orbit or
model-unit overrides.

## Regression Coverage

### Recovered Mannequin Pose (2026-11-22)

BotW `Mannequin_001_Upper/Lower` contain baked shop-display armor, not a
skeletal capture animation. Their clothing retains the source Armor_001 UVs
and triangle counts, with extra split vertices. Triangle/UV correspondences
matched 5,881 upper and 1,009 lower vertices. Mirrored UV islands were resolved
by side and iterative geometric correspondence, not by raw vertex order.

Single-bone regions independently establish a 1.2x mannequin display scale.
After removing it, robust rigid-skinning fits recover separate upper/lower
joint transforms. RMS vertex error is 0.001143 world units for upper and
0.000355 for lower; upper's diagonal strap has the largest residual (0.008108).
These are fitted transformations, not recovered original animation keyframes.
Unobserved pose degrees of freedom are not uniquely identifiable from baked
geometry. The profile includes fitted local rotations and translations and
does not alter the original meshes, skin weights, or topology.

`Icon Pose` is the only built-in pose. Uppers use the recovered upper transforms;
lowers use Nml_Wait frame 0. Clothing-specific belt, pouch and skirt joints are
transferred only to Armor_001, not unrelated armor. Imported BFSKA poses and
editable bone adjustments remain available. Standalone pose JSON controls and
the diagnostic Capture details section were removed; custom capture presets
still include pose adjustments and embedded BFSKA data.

`Add belt` first loads `%LOCALAPPDATA%/PhysicsTool/IconTool/Belt.sbfres` when
present. The user's trimmed Armor_001 file contains one belt mesh, 251 vertices
and 290 triangles, retaining native skin weights. It is posed against the same
bone transforms as the trousers. Link_Belt_A textures resolve from local game
resources. Without an override, the native baked mannequin belt remains the
fallback. No belt model is bundled in the repository.

Head captures add BotW Link's Mt_Face, Mt_Head and Mt_Upper_Skin only. Their
albedo sampler is replaced by a charcoal texture while retaining other surface
maps. The mannequin and headgear use the recovered upper pose's body joints.
TotK head captures use the configured BotW resource folder for this mannequin.

`Simulate gravity` settles the actor for 180 fixed 1/60-second steps, holding
the selected pose/frame fixed and disabling wind. The regular cloth solver
receives those input bones before helper evaluation and constraints; simulated
bones are not pinned by the input override. Capture awaits the resulting pose.
Changing the pose resets settling, and disabling the toggle restores the
unsimulated pose. Custom presets retain this option.

BotW lighting comparisons include Preview, Warm studio and Sunset test. These
three are explicit renderer light-color presets, not recovered native environments.
Warm studio produces a mild yellow tint; Sunset test is more orange than the
examined Armor_001/009 vanilla references. Icon capture palette is the default, with
brightness 3 and 256-pixel output unchanged.

The baked-pose profile is numeric fit output only; no game meshes or textures
are bundled. Research exporters and the SciPy fitting scripts reproduce the
derivation from local game assets. No executable evidence currently establishes
that the game uses this particular shop-model pose for inventory rendering.

`--icon-tool-gpu <BotW root> <output>` checks Master Sword 070,
Armor_009 Head/Upper/Lower, transparent export, native schema defaults, live
camera/light/brightness edits, unchanged PNG alpha, Viewport orbit/pan/zoom,
Icon Pose defaults, BFSKA frame import, shared textures, and exact preset restoration.
`--workspace-navigation <output>` checks navigation, retained workspace state,
the Icon Tool layout, and exclusion of the unreleased appearance editor.

`--icon-workflow-gpu <root> <output> [BotW mannequin root]` checks Armor_001 and
Armor_009 upper/lower/head defaults, topology, the trimmed belt, charcoal mannequin
material selection, lighting, fixed-pose gravity, deterministic resets, and
custom-preset round trips. Tested with BotW Switch, BotW Wii U update resources
and TotK. The legacy --icon-candidate-gpu --recovered entry runs these checks too.

### Capture Environment And Shadows (2026-11-22)

Both dumps contain `envobj/iconcapture.baglenv`: BotW's Bootup
`Env/env.sgenvb` and TotK's `Env/GameScene.Nin_NX_NVN.genvb.zs`.
The 940-byte AAMP files are byte-identical, SHA-256
`E5A9E6ADB42034638E7FAA7DA9D790CFBE5CC8A645FD522750F3F1124D32DBAF`.
TotK's `System/GameGraphics/LightRigSettingList/IconCapture.game__gfx__LightRigSettingList.bgyml`
selects `EnvSet: IconCapture`.

Decoded environment values:

| Light | Color | Intensity | Direction |
| --- | --- | --- | --- |
| Directional diffuse | 1, 0.921569, 0.74902 | 6 | 0.5, -0.5, -0.5 |
| Directional specular | 1, 1, 1 | 6 | same |
| Hemisphere sky | 0.131, 0.304, 1 | 1 | 0, 1, 0 |
| Hemisphere ground | 1, 0.96, 0.88 | 1 | same |

The archive also contains an enabled shadow projector. These findings establish
a dedicated warm capture environment, not an inferred sunset time of day.
TotK executable functions at 0x71011eaa4c and 0x710185b4d4 recognize IconCapture
in environment/capture enum parsing. They are not the renderer consumers and
do not establish runtime exposure, analyzed-light generation, or tone mapping.

`Icon capture palette` uses the extracted light colors at editor-normalized
direct intensity; it does not copy intensity 6 into the brightness-3 preview.
The character shader uses an editor approximation for the game's analyzed-light
atlas, described below. Hemisphere color uniforms alone do not reproduce that atlas. This remains
a known fidelity gap, so the new palette is a comparison option, not a claim of
pixel-identical native capture. Icon capture palette is the default for new captures;
saved presets retain their lighting selection. Sunset test is more
orange than the references; the dedicated capture diffuse is a restrained yellow.

The missing belt shadow was caused by disabling native depth shadows and binding
a white fallback shadow map. BotW icon captures now render a 2048-square depth
map from the selected light, including alpha-tested materials, and pass it to
the native cascade sampler. The shadow camera fits actual visible model bounds;
no geometry offsets or painted belt shadows are used. Translucent meshes are not
treated as opaque casters. Cast shadows defaults on in the Icon Tool and is saved
in custom presets. Regular actor preview behavior is unchanged. This pass is not
enabled for the separate TotK fallback shader.

Exposed armor `_Skin` materials now receive the same mannequin albedo as the
head assembly, without replacing gloves or armor materials. The generated
1-pixel sRGB albedo is (40,39,37), not pure black. Native skin normal/specular/AO
inputs remain intact. BotW reference comparisons put the resulting lit mannequin
around RGB (84,81,76), with darker shaded areas; this is a reference-calibrated
preview value, not a recovered native mannequin texture. TotK's material binding
now respects the same physical-sampler overrides instead of restoring its
original texture. Source BFRES and texture assets are never changed.

GPU checks cover BotW Switch/Wii U and TotK Armor_001/009 head/upper/lower;
BotW shadow on/off, unchanged silhouette alpha, light-dependent shadow masks,
exact reset/preset restoration, and charcoal sampler selection. Detailed
1024-pixel captures and vanilla comparison sheets are kept in local research,
not packaged as game assets. Capture reports GPU errors immediately instead of
waiting for a timeout.

### Capture Framing And Model Controls (2026-11-22)

The Icon Tool shares the actor preview's Meshes dialog. Visibility changes affect
both capture and shadow casting without modifying source assets. Hidden mesh
names and duplicate-name occurrence indices survive pose and simulation rebuilds
and are stored in custom presets.

With the preview focused, R rotates and G translates the model. X/Y/Z constrain
the transform, with Shift selecting the complementary plane. Enter or left click
confirms; Escape or right click cancels. These use the editor's existing transform
math, with capture-camera projection supplied for fixed presets. Actor position
and rotation remain editable numerically and are saved with custom presets.

Native capture presets retain their camera position, orientation and field of
view, but initialize zoom from projected model vertices. This removes unused
space intended for fused attachments: BotW Weapon_Sword_070 starts at roughly
197% with the current model. The target extent leaves a 7% margin on each side,
with zoom bounded to 25-400%. Head framing excludes the mannequin upper torso
from the fit only; that geometry remains rendered. Custom presets preserve their
saved zoom exactly, and Viewport mode retains manual camera control.

GPU regression checks exercise the actual shared Meshes dialog, transform
commit/cancel, placement and visibility preset round trips, all-hidden transparent
capture, and restored output. Framing checks cover Master Sword 070 and all three
Armor_009 slots. The shared editor interaction regression suite also passes.

### Lower Waist Skin Override (2026-11-22)

The revised user-supplied Armor_001.sbfres contains the trimmed belt (256
vertices, 290 triangles) and a waist skin strip (50 vertices, 66 triangles).
The local Belt.sbfres override accepts this assembly as well as the older
belt-only file. Skin receives the charcoal mannequin albedo and is included
independently of Add belt; the Meshes dialog can hide it independently.
Both meshes use their original weights and the current armor's named world-bone
overrides, with no geometry offsets or mesh edits. BotW Switch and TotK workflow
checks cover waist deformation, belt toggling and preset restoration. The source
download is unchanged and the previous local override is backed up by SHA-256.

### Lower Lighting And Waist Comparison (2026-11-22)

Matched captures of Armor_001_Lower and Armor_009_Lower confirm that the presets'
key vector (-0.5, 0.5, 0.5) already matches the extracted IconCapture ray direction.
Moving the light forward or higher changes the trouser folds less like the
references; the native direction and brightness 3 remain unchanged.

The old one-pixel analyzed-light fallback supplied 80/255 to both color samples
and slot 0's shadow-control channels. In the character shader the latter weakens
the cast-shadow contribution. Icon capture now uses a 12-texel floating-point
atlas: slot 0 has full direct-shadow participation, and the color slots use the
mean of the extracted sky/ground colors at the previous editor fill strength.
The ambient multiplier now scales this fill instead of only unused hemisphere
uniforms. This gives the shadowed trousers a cooler neutral color closer to the
reference, without tinting albedo textures or changing Killz's shader sources.
This is a host-side hemisphere approximation, NOT recovered native light analysis
or exposure. The original icons still differ in highlights, pose and tonal detail.
Other lighting palettes and regular actor preview keep their previous behavior.

Shadow on/off comparisons confirm contact shadows at the belt/waist without any
change to silhouette alpha. No raster-bias workaround was added: the native
character shader already supplies a -0.0015 receiver-depth bias.

The local waist-skin override was shortened to 65% of its original vertical span,
anchored at its minimum Y. Its top moved from 1.0032854 to 0.98590016 model units
(about 1.7 cm); the bottom remained 0.9536133. This visually calibrated adjustment
reduces the raised band to a narrow rim closer to the vanilla icon. Normals and
tangents were transformed consistently; belt vertices, topology and skeleton
round-trip unchanged. The original Downloads file and a hash-named local backup
are retained. No trim factor is hardcoded into the runtime or applied to other
users' meshes. Tests cover the adjusted assembly, contact shadows, ambient
response and exact reset/preset restoration.

### Native Light Analyzer Recovery (2026-11-22)

Read-only BotW Switch executable analysis identified the missing producer for
`cGSys23_LightAnalyzedData`. The shader registration in function 0x7101805190
binds `light_analyzer` to cGBuffer, cShadingBuffer, cExposure, cCubeMap,
cCubeMap2D and a 144-byte cContext block. Render code in function 0x71017f4300
uses named cubemap/shading targets and progressively reduces shading dimensions
to two pixels. These are native passes absent from the current preview, not a
single fixed ambient-color calculation.

The six STEP variants were extracted from Bootup_Graphics.pack ->
System/KSys/U-King.Nin_NX_NVN.release.ssarc -> uking_pass_shader.sharcb and
decompiled locally. Their control records specify a 48-byte prefix before the
native shader header; respecting this offset removes instruction misalignment.
STEP 5 averages fourteen cubemap samples at LOD 3 (six axes and eight corners).
STEP 4 builds the twelve-texel lighting table using that mean, directional cube
samples, the cExposure input and context controls. Its character shadow control
is `clamp(1.2 * cExposure.r * cContext[1].w, 0, 1)`, stored in texel 0 green.
Character fill uses distinct texels 4 and 5 rather than a constant sky/ground mean.
The exact semantics of the input named cExposure remain under investigation; do
not assume it is the editor brightness control or camera auto-exposure.

The shipped hemi_to_env shader also contains a nonlinear signed-power blend
(exponent 0.330000013). Its presence is verified, but the icon path's exact
environment source and context population are not yet established.

A second pass over the large setup function at 0x710180d560 recovered the
`ksysla` analyzer object and eighteen named parameter defaults, including character
ambient gradient (1, offset 0), saturation (0.5, minimum 0.75), and ambient offset
range (0.1 to 0.5, scale 1.75). These values are initialization defaults, not yet
proven to be the icon-time context. The per-frame writer/overrides remain the next
trace target. The full local report records field offsets and evidence addresses.

This explains a missing dependency of belt-shadow shading, but is not yet a
verified visual fix. The current host-side fill remains explicitly approximate.
Native context values, input producers, dispatch order and target/filter state
must be recovered before replacing it. No renderer changes, guessed context
defaults, material darkening or shadow-strength adjustments were made in this
research pass. Game shader binaries/decompilations remain in local research only,
outside the public repo and release assets; Killz's supplied sources are unchanged.

### Native Writer And Shadow Overrides

The analyzer's per-view context writer is now identified in `0x71017fd570`
(`0x71018002d0..0610`). It copies fields through reflected offsets using
`0x71012bc430`; field indices are not vec4 indices. Inputs include the active
view, directional light, analyzer settings, per-view colors and runtime flags.
Setter `0x7101820610` connects per-view `+0x7e4` directly to the same value as
`uking_dynamic_depth_shadow_scale`, independently of editor brightness.

The CPU context initializer and extracted shader layout still need reconciliation:
the initializer declares 24 fields with three leading four-component values,
while the extracted shader metadata reports a 144-byte context. No combined
CPU/shader mapping or icon-specific override values are claimed yet. Full
evidence and the discrepancy are retained in the local research report. The
runtime analyzer approximation has not been replaced.

### Armor Dyes And Native Item Icons

The Icon Tool reads armor texture-pattern animation frames from Switch BFMAA
and Wii U texture-pattern animation data. Available bindings are sampled using
the existing appearance reader; duplicate bindings and missing texture variants
are excluded. Automatic material animations remain separate. Changing a dye
rebuilds from cached documents/textures and retains charcoal mannequin skin.

Batch capture exports all available dye variants of the loaded armor with the
current pose, camera, lighting and mesh visibility. It restores the original
selection afterward, supports cancellation and refuses to overwrite existing
batch outputs. Frame zero uses the base filename; subsequent frames use .01,
.02 and so on.

PNG and native BotW `.sbitemico` export are supported. Native export uses an
item-icon template from the selected game's resource folder, preserving its
platform format: Yaz0/BFRES/BNTX on Switch or Yaz0/BFRES/GX2 on Wii U. Templates
and game textures are not bundled. Texture/resource names and dimensions are
updated, with unpremultiplied RGBA pixels encoded in the native layout.

GPU tests cover all 16 Hylian armor dye frames on both platforms, head/upper/lower
captures, unchanged geometry/skin, batch restoration, cancellation, overwrite
protection, and exact native RGBA roundtrips at 96, 144 and 256 pixels. Existing
icon workflow regression checks also pass. Exported files have not been tested
in game.

### Headgear Ear Rotation And Mannequin Shading

BotW headgear now reads `ArmorHead.EarRotate` through the actor's `GParamUser`
link. Hylian Hood (`Armor_001_Head`) supplies (0, 50, 0) degrees; Snowquill
headgear (`Armor_009_Head`) supplies zero. The native constructor at
`0x7101668b00` builds the same XYZ Euler rotation for Ear_L and Ear_R, with no
extra sign inversion. The modifier at `0x710154bf00` composes it on the right
of the native column-vector local pose, equivalent to pre-multiplication in
the preview's row-vector convention. Ear origins are unchanged. This modifier
is retained when rebuilding dye variants and precedes final simulation poses.

Only materials using the icon mannequin charcoal albedo have their specular
mask/hair source set to native constant zero and AO disabled. Their fallback
SPM/AO flags are disabled as well. Armor materials, normal maps, cast shadows,
and source BFRES files are unchanged. The same policy applies to optional waist
skin. GPU checks verify ear matrices for both sides, dye stability, skin material
flags and native icon roundtrips on Switch and Wii U.

### Dye Capture Reuse

Dye changes now rebind materials onto the existing adjusted and gravity-settled
geometry. Vertex/index buffers, tangents, bone matrices and bounds are reused;
they do not call the pose rebuild or queue another simulation. A pending gravity
run completes once before rebinding. Batch export retains the settled geometry
for every dye and restores the original model objects afterward, including on
cancellation. Pose changes still invalidate settlement normally.

The selector has 16 native entries, 0 - Default through 15 - Gray, without the
duplicate Original entry. Names follow the game's dyecolor_01..15 labels.
Icon capture explicitly requests a transparent RGBA target and excludes the
viewport grid. Checks cover transparent PNG/native pixels with edge highlights,
pending/completed settlement reuse, identical settled vertex-buffer references,
and exact default-dye restoration on both BotW platforms.
Edge highlights and the optional belt now default to enabled; saved custom
presets retain their explicitly saved toggle values.

### User Mannequin Props

`%LOCALAPPDATA%/PhysicsTool/IconTool/Armor_Icon.sbfres` supplies the named
`Icon_Head_*`, `Icon_Upper_*` and `Icon_Lower_*` pieces. Shape-to-material
indices are resolved by matching names; this repairs the supplied finger
shapes' face-material indices in memory. The source file is not modified.
RaulSkin A/B/C pieces are available as optional BotW upper-armor props, hidden
by default. Custom presets retain their visibility; older presets leave them
hidden. Armor 001 Upper starts with its original
skin hidden and the replacement neck and fingers visible for comparison.

The armor mesh dialog separates Armor and Props, including the belt. Visibility
is saved in presets, and old belt/waist visibility entries are migrated.
The supplied head uses Link's native skinned eyeball geometry as two optional
charcoal props, with no iris texture, glint, or eye-specific lighting. They use
the face's material settings without sharing mutable shader options.

All charcoal skin bypasses the original damage/color blend chain and disables
sketch/specular and texture AO. The newly modeled neck cap and blank eyes use
geometry normals; existing properly mapped skin retains its normal map.
Cast shadows are retained. This is not yet an exact match for vanilla inventory
lighting or the original neck shape; tested neck insets were not retained.

Capture edge highlights now use only the background boundary of the combined
cutout, not foreground/background depth discontinuities inside it. GPU pixel
checks cover internal material/depth joins, alpha preservation, light direction,
inward fade, and zoom-dependent width.

### Mod Browsing, Export And Framing Audit

Open mod actor uses the same searchable browser as Open actor, with a game
selector. BotW and TotK mod folders persist independently in `icon-tool.json`;
dump paths remain in `actor-folders.json`. Missing mod resources fall back to
the corresponding dump. BotW fallback roots are filtered by archive endianness
to avoid mixing Wii U and Switch resources.

The capture dialog has equal-width Cancel, Export to Folder and Export to Mod
actions. Format and Size remain in the dialog, with 256 as the default size.
The last accepted format persists. Native Switch BNTX and Zstandard-compressed
`.bntx.zs` exports are supported alongside PNG and BotW `.sbitemico`. Export to
Mod chooses the appropriate resource subfolder and refuses game-dump targets.
Existing files require explicit replacement confirmation, including dye batches.
GPU/native decoding checks cover pixel/alpha roundtrips; in-game loading has not
been tested.

The November 2026 audit decoded 1,090 vanilla BotW armor/weapon icons and compared
729 saved camera/rotation/light/FOV presets with their native TotK counterparts:
zero missing entries or value mismatches. Contact sheets include all dye variants.
This validates source-data transcription, not pixel-exact lighting reproduction.
The native ray values are retained; the analyzed-light and shadow differences
noted above remain, including the Hylian Hood's face shadow.

Default zoom now uses the reference icon's alpha coverage when available.
Head framing excludes the mannequin torso and permits the intended shoulder/cape
crop instead of shrinking the whole head to accommodate lower geometry. This
corrects Armor_001_Head's undersized capture. The authored camera is unchanged,
and manual zoom and custom preset framing still take precedence.

### Preset Selection

The dropdown filters built-in presets to weapons or armor for the loaded actor.
Armor upgrade aliases sharing model resources, slot/variant and capture settings
resolve to one entry: 532 armor records yield 131 visible presets. All 197 weapon
presets remain available for weapons. The complete source records remain in the
library so upgraded actor names still select the appropriate retained preset.
Model identity is extracted from actor references; no upgrade-number ranges are
guessed. Hood variants and differing light/camera settings remain separate.

Only the first actor auto-selects its matching preset. Later actor loads preserve
the current capture settings, including manually edited camera, placement, light,
zoom and output settings. Compatible preset selections remain; crossing categories
uses Current settings without resetting the camera. Explicitly selecting a preset
still applies its defaults. Custom presets record their originating actor category;
legacy presets without an identifiable category remain available.

### Icon Texture Packaging And Errors

Preset labels use the English ActorMsg inventory names from the TotK USen 1.2.1
message archive. DisplayName is separate from Name, so matching, upgrade aliases
and saved presets retain their actor IDs. Alternate captures have variant labels;
internal actors without inventory text use model-derived or descriptive names.
The visible list groups armor into headgear, upper armor and lower armor, and
weapons into one-handed, two-handed, spear, bow, shield and other weapon sections.
Each section sorts by display label. Non-selectable headers are skipped by arrow
navigation; keyboard text search still jumps by item name across sections.
Custom presets have their own alphabetized section.

Simulate gravity is shown only when the resolved actor references readable cloth
(.hkcl/.bphcl) or support/helper (.bphyssb/.bphhb) data. A hidden checked preference
does not start a solver for rigid actors. Helper-only actors evaluate their fixed
pose once, while cloth actors retain the existing gravity settling sequence.

The screenshot's RGBA pixels now define the output texture layout, regardless of
the template's compression or mip levels. Wii U export replaces old format/mip
metadata with a single-level sRGB RGBA GX2 surface, and Switch export rebuilds its
BNTX payload. A BC1 eight-mip mod shield template is covered by exact RGBA/alpha
roundtrip checks at 96, 144, 256 and 512 pixels. Source icons are not modified.
Failed Icon Tool commands show a modal error dialog with selectable failure text;
the status message is retained as well.
