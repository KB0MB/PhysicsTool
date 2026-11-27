# Built-In Icon Props

`Armor_Icon.sbfres` is the supplied, edited Link icon mannequin. `LinkEyes.bfres`
contains only the two filled-eye shapes and their supporting skeleton, vertex
buffers and materials. `Textures.json.gz` stores the mannequin's 15 referenced
textures as decoded RGBA pixels, with color-space and channel metadata.

`Armor_Icon_Riju.sbfres` and `Armor_Icon_Zelda.sbfres` contain the supplied
character mannequins and their embedded animations. Each character also has a
path-free definition, prop textures, a native material index, trimmed native
material models and their texture dependencies. The material models retain
native shader settings; they are never drawn as additional mannequin geometry.

These resources are embedded into PhysicsTool.Core. No local Downloads,
AppData profiles or character mod folders are needed for the built-in props.
TotK captures do not need a second game's resource folder to load them.

The asset collection can be regenerated with PhysicsTool.Checks:

```
--icon-builtin-export <Armor_Icon.sbfres> <BotW resource root> <output directory>
--icon-character-bundle <authoring profile.json> <TotK resource root> <output directory>
```

The export strips source filesystem paths from texture metadata and includes
only the textures required by the mannequin and its native material sources.
Link's native face and skull materials resolve from the loaded actor's game;
Riju's and Zelda's imported native materials are included in their bundles.

The edited mannequin and game-derived geometry/textures are separate from
PhysicsTool's source-code license; this directory does not grant ownership or
additional rights to the underlying game assets.
