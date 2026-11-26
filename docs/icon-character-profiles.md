# Icon Character Profiles

The Icon Tool's Character dropdown defaults to Link. Add another character with
the adjacent plus button, selecting an icon mannequin BFRES and the mod folder
containing its `romfs/TexToGo` textures. Missing mannequin slots remain absent;
they are not filled using Link geometry. Armor models still come from Open actor
or Open mod actor.

Profiles are local JSON files in `%LOCALAPPDATA%/PhysicsTool/IconTool/Characters`.
They reference user-owned assets; the models and mod textures are not included
in the repository or release packages. Each file has `Id`, `Name`, `ModelPath`,
`TextureRoot`, and an optional `TexturePrefixes` dictionary. Prefix mappings are
explicit: for example, a mannequin authored with `Link_Skin_` references can map
that prefix to a mod's `Link_RaulSkin_` textures. Longer prefixes take precedence.
`MaterialTextures` supplies material-specific sampler overrides when a prop was
exported with unrelated placeholder bindings; Riju's bra uses the mod's original
RaulSkin atlas rather than the belt atlas referenced by the mannequin file.

Use an `Armor_Icon` or `Armor_Icon_<character>` model with the existing slot-based
shape/material names. Head eye shapes named `Eye_L__...` / `Eye_R__...` use the
face material and are exposed as head props. Skin and blank eyes use charcoal;
accessories such as belts and `Icon_Head_Bra` retain their original textures.

Character changes retain capture placement, lighting, compatible pose edits,
and the selected dye. Capture preset JSON records the character ID, and opening
it restores the installed character profile. Asset paths stay in the local
character profile, rather than being embedded in shared capture presets.

Riju fixture checks exercise the supplied mod's hood, two upper outfits and
trousers, texture mapping, optional meshes, captures, and Link/Riju preset
round trips. The current supplied Riju mannequin has head pieces and RaulSkin
arms, but no lower waist/belt or separate upper neck/finger geometry. Adding
those shapes will make them available through the existing Props menu.

## Authored Poses and Necklines

A custom mannequin can include a skeletal animation named `Icon_Pose`. Its
first frame drives both the armor and mannequin, replacing the generic idle and
Link pose recovery. Imported BFSKA poses and explicit pose edits still override
it. Characters without this animation retain the existing pose behavior.

`NeckSplices` optionally maps an actor name to an exact armor mesh name. When
that mesh exists, its weighted chest/neck surface replaces `Icon_Upper_Neck` in
Props, and its highest open rim is capped. No source vertices are repositioned;
the cap follows the skinned boundary. This is useful for open collars needing
more chest coverage than a short neck prop. The original source files are not
modified. The named skin mesh is transferred to the prop rather than drawn twice.

Zelda's local RotG profile uses `Mt_Skin_Upper__Mt_Upper_Skin` for
`Armor_001_Upper`. GPU tests cover head, uppers 001/005/009 and lower 005; the
001 splice additionally verifies native collar topology and positions, cap
attachment, dye retention and pose overrides.

`MaterialSources` maps a prop material to `{ "Actor": "Player", "Material":
"Mt_Default" }`, for example. This imports the complete native material from
the character mod, including texture bindings and shader parameters, instead of
retaining the mannequin's placeholder shader. Zelda's default shirt and Riju's
bra use their respective mod player materials.

Skin material imports retain the charcoal albedo and the destination mesh's
vertex colors. Zelda's face uses `Player/Mt_Face`, including its authored
shadow-only vertex displacement. The caster offset is evaluated from the
material constants and painted vertex-color mask before skinning, then follows
the pose and actor rotation. Only shadow-map positions use this offset; the
visible mesh and its normals are unchanged. Constant-vector multiply and
multiply-add face modes are supported; texture-driven displacement is not.
Filled eyes receive independent material copies, so their normal-map settings
cannot leak into the face. GPU regression checks compare adjusted and
unadjusted casters and verify capture-state restoration.

Link's face and skull automatically use `Mt_Face` and `Mt_Head` from the loaded
game's native player actor (`GameROMPlayer` in BotW, `Player` in TotK). Riju's
local profile binds those materials from her mod's `Player` actor. Their face
casters use the authored multiply-only formula, not Zelda's multiply-add
offset. Link's supplied mannequin retains its painted mask; Riju's supplied
mask is entirely white and therefore produces zero shadow displacement.
Material imports preserve each prop's geometry and mask, including filled eyes.

TotK RaulSkin defaults resolve the loaded armor's inherited
`HideMaterialGroupNameList` against `PlayerRaulHand.HiddenMaterialGroupList`,
using mod resources before the dump. This selects A, A/B, all three, or none as
authored, without an armor-number whitelist. Head captures normally include all
three. TotK's ordinary right-finger prop is optional rather than duplicated over
RaulSkin. BotW keeps RaulSkin props optional and hidden by default. Explicit mesh
choices saved in a custom capture preset still override these defaults.

`TextureColors` maps an exact texture name to an eight-digit sRGB RGBA hex base
colour. It replaces RGB in a private decoded texture copy, preserves cutout alpha
(scaled by the supplied alpha), and leaves normal/specular maps and source files
untouched. Zelda's local hair override is `7E5C00FF`; other characters are unchanged.

Ear rotation is always resolved from the currently loaded actor's
`ArmorHead.EarRotate` (BotW) or `ArmorParam.HeadEarBoneOffset` (TotK), including
parameter inheritance. It is not a character or capture-preset setting.
