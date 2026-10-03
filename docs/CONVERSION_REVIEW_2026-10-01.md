# Cloth and helper-bone conversion review

Date: 2026-10-01. The findings below describe the pre-fix baseline. A subsequent implementation pass addressed the items listed here.

## Implementation follow-up

- Preserved ordered collision calls and repeated solver passes in BPHCL-to-HKCL conversion.
- Removed automatic mass guesses for unknown cloth and retired the two mismatched counterpart mass profiles. Exact asset profiles remain retuning, not proof of universal equivalence or custom-file topology compatibility.
- Preserved explicit helper reference bones forward/reverse and completed translation-channel mapping. Parent-space reverse conversion now requires actual model hierarchy context; unrepresentable/ambiguous cases are rejected before replacing output.
- Added regression diagnostics: 75 fresh cloth conversions, 150 binary Wii U/Switch save/reloads, and 38 successful helper round trips with unchanged normalized reference names. Synthetic checks cover repeated collision calls, all six channel mappings, actual-parent resolution, and destination preservation on rejected model-space/linear/ambiguous conversions.
- The separately rerun standard-link audit reports source effective coefficients retained at approximately 1.0 for every source-preserving fallback. No preview simulation code was changed by this pass.
- Still open: broader topology-qualified profiles, faithful conversion of unsupported helper modes/curve layouts, full operator/state coverage, and in-game behavior validation. The baseline findings and executable evidence below are retained for traceability.

Run the regression suite using `PhysicsTool.exe --self-test-conversion-fidelity <output-directory> <local-fixture-root>`. The fixture root needs the local Matches/Yiga cloths and helper samples; proprietary fixtures are not included in the repo.

## Scope and evidence

- Reviewed native BPHCL/HKCL mass, link coefficients, constraint execution, and BPHHB/BPHYSSB graph conversion.
- Compared 69 named cloth counterparts across 19 paired files in the local Matches directory.
- Built 75 fresh HKCL cloth graphs in memory from 22 BPHCL files (Matches plus Armor_1300_Head). No graph construction failures. This is not an in-game compatibility test or a complete binary round-trip test.
- Exercised BPHHB -> BPHYSSB -> BPHHB on 38 local helper files. 35 completed; three failed on translation channels. After ignoring namespace prefixes, 29 of the 35 completed round trips changed at least one driver/pose base name. These are loss-detection results, not a claim that every changed reference causes the same visible symptom.
- Re-decompiled BotW Switch and TotK functions from the saved Ghidra projects using read-only headless processing, with analysis disabled. No saved project changes were committed.
- Inspected local SDK content-tool headers without copying SDK implementation into the repository. The inspected headers identify a 2024 build; they are supporting authoring evidence, not proof of the exact SDK build used by either game.
- Tested the existing Release assembly, SHA-256 `2F503428A6894244C635B8C823D74BC9DAADAD5C449F8746AB88A64346DA565F`.

## Findings

### P1: Conversion moves or removes actual collision stages

Location: `PhysicsTool/Formats/Hkcl/HkclService.cs`, `ApplyBphclConstraintExecution` (around lines 6490-6540); `BphclConversionProfileCatalog.ShouldKeepConstraintExecutionTerminator`.

The converter interprets a negative execution entry as a phase separator, moves all later constraints ahead of the remaining primary phase, and appends one final -1. Cloth names containing `_1300` do not receive that final entry at all.

Both executable traces show that -1 invokes collision solving immediately at that position, then execution continues. It is not a terminator. Moving it changes constraint/collision interleaving; collapsing multiple entries loses collision passes.

Reproductions:

- Armor_006_RaulSkin_Upper / Body_Metal_006_Havok: `[2,0,1,-1,3]` becomes `[2,3,0,1,-1]`.
- Armor_1300_Head / SimulationMesh_HairTop_1300: `[2,0,1,-1,3]` becomes `[2,3,0,1]`.
- Armor_1300_Head / SimulationMesh_Earring_1300: `[2,3,0,4,4,1,-1]` becomes `[2,3,0,4,4,1]`.

The two Armor_1300 examples lose every explicit collision invocation from their execution programs. In the Matches batch, 33 of 73 graphs changed execution arrays; some changes also include constraint-index remapping and should not all be counted as equivalent defects.

Recommendation: translate set indices in place, retain each -1 and every repeated stage. Treat any intentional native-counterpart retuning as an explicit, validated profile, not general cross-version format normalization.

### P1: Unmatched-cloth mass defaults weaken links by particle count

Location: `PhysicsTool/Formats/Hkcl/HkclService.TemplateFree.cs`, `ResolveBphclConstraintScale` and `GetBphclSolverScaleSuggestion` (around lines 229-264).

The fallback multiplies each dynamic mass by total particle count N, but leaves standard/bend coefficients unchanged. Both games' scalar standard/bend kernels multiply the stored coefficient by endpoint inverse mass. Consequently this fallback reduces their endpoint corrections by 1/N. There is no compensating factor in those kernels.

Fresh conversion reproductions, both explicitly labelled "Topology fallback":

- Armor_014_Lower / Rope_Leg_A_L_Havok: mass x3, coefficient x1, effective standard-link response ~0.333333 of source.
- Armor_014_Lower / Rope_Leg_A_R_Havok: mass x6, coefficient x1, effective response ~0.166667 of source.

Seventeen of the 73 Matches graphs use unequal mass/coefficient multipliers. Some are intentional counterpart profiles, so this count alone is not seventeen confirmed bugs. For example, the apron in Armor_009 uses mass x8.819093 and coefficient x1, giving ~0.113390 source response; whether that is desired depends on whether the goal is TotK behavior or BotW's separately authored counterpart.

Recommendation: distinguish behavior-preserving conversion from native-counterpart retuning. For unknown cloth, do not infer physical mass from particle count. Uniform mass normalization of standard/bend links must preserve `coefficient * inverseMass`; other constraint families, forces, and runtime settings require their own dimensional/behavioral checks.

### P1: Reverse helper conversion fabricates reference bones

Location: `PhysicsTool/Formats/Bphhb/BphhbBridge.cs`, `ResolveBase` (around lines 297-319) and `InferParentBoneName` (around lines 485-533). Forward conversion in `BphyssbBridge.Build` writes parent-space -1 and does not preserve all explicit TotK base references.

Reverse conversion infers a parent from a bone name, adds that name to the graph, or picks the first unrelated driver/bone. These guesses can produce a readable but behaviorally incorrect graph.

Reproductions:

- Armor_1106_RaulSkin_Upper: Leg_1_L/R bases change from Waist to Waist_L/Waist_R. The paired cloth skeleton actually parents both legs to Waist; the inferred names are not in that skeleton.
- Armor_1036_Lower: the right pose's base changes from Leg_2_R to Leg_2_L.
- Armor_1036_Head: pose bases change from Hair_Root to Head.

Recommendation: resolve parent-space against an actual imported skeleton, preserve explicit reference spaces where representable, and require a mapping or reject an unresolved conversion instead of silently inventing a bone. A reference-space change also needs a transform conversion, not just a new index.

### P2: Reverse helper conversion rejects valid translation channels

Location: `PhysicsTool/Formats/Bphhb/BphhbBridge.cs`, `UnpackBoneAttribute` (around lines 477-483).

Forward conversion supports translation channels 3, 4, and 5. Reverse conversion only recognizes the three rotation channels. It therefore cannot reverse files produced by the forward converter.

Reproduced failures: Armor_1036_RaulSkin_Upper (channel 3), Npc_Zelda_AncientHyrule (channel 4), Armor_1091_RaulSkin_Upper (channel 3).

Recommendation: complete the inverse channel mapping and test all six channels through an actual export/reload cycle. Linear BotW connections are another unsupported reverse case and should remain explicit rather than silently becoming curves.

### P2: "Verified" mass profiles can compare different particle layouts

Location: `PhysicsTool/Formats/Hkcl/ConversionMatchAnalyzer.cs` (around lines 38-59); `BphclConversionProfileCatalog.cs` profiles at lines 67 and 99.

The analyzer derives a single ratio from total dynamic mass without requiring equal fixed membership, matching particle count, or corresponding topology. Names and equal totals cannot establish an equivalent particle distribution.

- Hair_030_B_Havok: both versions have 14 particles, but source has 10 dynamic and target has 8. Particles 2 and 13 differ in fixed state. The total ratio is 14; the ratio for corresponding dynamic particles is 17.5. The current x14 profile produces mass 0.3 where corresponding BotW particles have 0.375. Changing only the scalar cannot reproduce the target's fixed layout.
- Outer_179_Havok: source has 62 particles, target has 54, yet the total-mass ratio 3.21428547 is presented as a verified profile.

Recommendation: qualify profiles by topology/fixed membership and distinguish matched asset tuning from heuristics. Do not automatically change source pins or topology just to force a counterpart match. Validate effective per-edge coefficients as well as per-particle mass, rather than only sorted coefficient values and total mass.

## What the mass evidence actually supports

Across the 69 paired cloths, dynamic `mass * inverseMass` ranges from approximately 0.99999994 to 1.00000006 in both formats. This supports the existing reciprocal calculation for these fixtures. It does not prove every custom or runtime-modified asset obeys the same invariant.

The total-mass ratios range from about 0.126519 to 30.564167. They are not a universal unit conversion. Content-tool headers also expose both density-based and explicitly authored particle-mass inputs. That supports investigating asset authoring/distribution rather than seeking one global multiplier in the executable.

For standard links the relevant runtime correction is proportional to:

`direction * (distance - restLength) * storedStiffness * runtimeAdaptation * inverseMassEndpoint`

BotW and TotK both use this structure. Bend links similarly apply separate bend/stretch coefficients with endpoint inverse mass. BotW's directed stretch link is a different operation and must not inherit the same mass-scaling rule merely because it has a field named stiffness.

## Executable trace map

BotW Switch saved project: `BotW_Switch_Runtime_20260830`, program `main`.

- Standard link: `0x7101e08864`.
- Bend link: `0x7101d7e3f8`.
- Directed stretch link: `0x7101e09a3c`.
- Explicit constraint execution: `0x7101e16124` (existing research; same in-order collision semantics).

TotK saved project: `TotK_Cloth_Runtime_20260830`, program `main`.

- Standard-link reflection constructor `0x71003b2c00` -> constructor `0x71003e3e70` -> global vtable pointer `0x71046234b8` -> table `0x710425ed68` -> solve `0x71003e3eb0`.
- Bend-link constructor `0x71003daba0` -> global vtable pointer `0x7104623440` -> table `0x710425eae8` -> solve `0x71003dabe0`.
- Simulate constructor `0x710048ec7c` -> global vtable pointer `0x71046237a0` -> table `0x710425e3a0` -> main simulate `0x710048f828` / `0x710048f830`.
- Explicit execution loop `0x7100495280` calls collision solve `0x7100495b70` on each -1, then continues to subsequent entries. The latter has the native "Collide And Solve" profiling label.
- Fallback all-sets loop: `0x7100495020`.

The initial string references mostly led to reflection helpers. Scanning raw relocated pointer values located class records not represented by existing Ghidra references; following constructors then located the runtime vtables above.

## Reproduction artifacts and limits

Local research workspace, under `HKCL/Research`:

- `Review-ConversionMass.ps1` and `conversion-mass-review-20261001.json`.
- `Review-ConversionBehavior.ps1` and `conversion-behavior-review-20261001.json`.
- `Review-SidecarConversion.ps1`, `sidecar-review-20261001.json`, and generated files in `sidecar-review-20261001/`.
- `botw-mass-kernels-20261001.txt` and the `totk-*-20261001.txt` decompilation/trace outputs.

Scripts load the existing Release assembly and write only diagnostic outputs, not original dump files. The Ghidra raw-pointer helper is in `HKCL/GhidraScripts/FindRawPointerRefs.py`.

The supplied GhidraMCP ZIP contains the Ghidra-side HTTP extension (metadata targets 11.3.2), not an active MCP connection in this session. The installed 11.0 headless analyzer successfully read both projects; extension compatibility was not tested and no MCP setup was installed.

This review does not establish full in-game equivalence, all SIMD solver variants, every runtime override, or complete HKRG/collision conversion correctness. It identifies reproducible cloth/helper conversion defects and establishes a more reliable starting point for targeted fixes. Existing Release binaries and production conversion code were not changed.
