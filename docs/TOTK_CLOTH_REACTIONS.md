# TotK Cloth Reactions and BotW Sidecars

## Local Findings

The 2026-10-11 survey read 15,064 actor/shared packs from the local TotK dump and found 205 distinct ClothReaction resources, with no conflicting resource bodies or read errors. Raw survey output stays under ignored `artifacts/cloth-reaction-research`; game resources are not bundled with the application.

References run from ActorParam.Components.PhysicsRef to PhysicsParam.ControllerSetPath. The controller's Cloth list references ClothParam.MeshList, which supplies each cloth's BaseBone. Its separate ClothReaction list maps a mesh Name to a reaction FilePath. These names, not the actor filename or guessed material family, determine the association.

Parameter resources can inherit via `$parent`, including defaults in ResidentCommon.pack.zs. The reader resolves nested maps, replaces explicitly authored arrays, checks cycles and unsafe paths, and preserves absent scalar values as unknown. It does not substitute guessed engine schema defaults.

Examples validated from the dump:

- Armor_1141_Upper: the ribbon uses Clavicle_R / Link_Muffler_A. Its skirt uses Waist / Muffler_Heavy, its sleeves use Arm_2_Assist_L/R / Skirt, and its ornament uses Waist / String.
- Parasail: the left and right main trails use distinct Parasail_Left / Parasail presets, with base frequencies 2.4 and 2.5. All ten cloths use Skl_Root. These presets use the strong-wind branch instead of a ReactionParam list.
- HorseGod001: seven named reactions resolve to the appropriate Head, Neck or Spine_2 base bones.

## Current Export Behavior

File > Export > BPHYSICS sidecar is available for HKCL and BPHCL. A BPHCL still needs separate conversion to HKCL before BotW can load it; the sidecar contains no particle data. The game paths and wind settings are editable. Export validates finite values, speed bounds and unique names before atomically replacing an output.

Read TotK actor opens the searchable actor browser, restricted to TotK actors with cloth. It shares the saved TotK resource folder and scan cache with Open Actor, without changing that window's last-selected game. The selected actor and shared resources supply the reactions. Only exact cloth-name matches (with an optional Link: prefix removed) are used. The base bone is copied only if it exists in the loaded cloth. Unmatched cloths and unavailable bones keep their existing export settings. Ambiguous names or missing inherited resources cause an explicit error rather than a partial import.

The reaction selector shows the base values, each authored ReactionParam entry, and strong wind when explicitly enabled. Use reaction frequency copies only that sample's authored Frequency as an opt-in starting point. Import does not automatically modify drag, wind speed limits, sub-wind, particle masses, constraints, or preview simulation.

## Why Full Wind Conversion Is Still Open

TotK reactions contain DragForce, Frequency, SwingFactor, vector SubWind, ReactionSpeed breakpoints, separate strong-wind values/thresholds, and fluctuation direction/period settings. A BotW BPHYSICS cloth has one static wind profile and shares a sub-wind direction/frequency/speed with the other cloths.

There is no verified equation here equating TotK DragForce with BotW wind_drag. ReactionSpeed breakpoints are not established equivalents of wind_min_speed/wind_max_speed. In particular, several TotK base reactions explicitly contain zeros but have nonzero speed-dependent samples; copying only the base would incorrectly suggest no response. Missing fields in a speed sample are not assumed to inherit the top-level sample.

The public [BotW cloth parameter definitions](https://github.com/zeldaret/botw/blob/master/src/KingSystem/Physics/Cloth/physClothParam.cpp) confirm the static target fields, but do not implement the runtime force equation. That source spells the minimum-speed key wind_mind_speed; inspected vanilla Zelda files author wind_min_speed. The writer's existing vanilla spelling was retained, not changed on the strength of that discrepancy alone.

Next research needs the TotK reaction interpolation/defaults and both games' runtime force equations, followed by matching in-game response tests. Until then, frequency reuse is a tuning aid, not a certified force-preserving conversion. Do not fit an arbitrary global drag scale or claim equivalent simulation from field names alone.

## Checks

Synthetic tests cover reference normalization, inherited values, unknown defaults, rejected cycles/traversal/missing parents, AAMP round trips, failed-write preservation, reaction selection, and source-document isolation. Native checks cover 1141 Upper, Parasail and HorseGod without modifying input files. Headless UI captures and the portable regression suite cover the export window and conversion preferences.
