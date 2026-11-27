using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal static class Program
{
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--icon-builtins-check") { IconCharacterBundleChecks.Verify(args[1], args[2]); return 0; }
            if (args.Length == 4 && args[0] == "--icon-character-bundle") { IconCharacterBundleChecks.Export(args[1], args[2], args[3]); return 0; }
            if (args.Length == 4 && args[0] == "--icon-builtin-export") { IconBuiltinPropsChecks.Export(args[1], args[2], args[3]); return 0; }
            if(args.Length>=4 && args[0]=="--native-face-study") { NativeFaceChecks.Inspect(args[1],args[2],args[3]); return 0; }
            if(args.Length>=2 && args[0]=="--raul-hide-study") { IconRaulVisibilityChecks.Inspect(args[1],args.Skip(2).ToArray()); return 0; }
            if(args.Length>=4 && args[0]=="--head-shadow-study")
            {
                IconHeadShadowChecks.Root=args[1]; IconHeadShadowChecks.Output=args[2]; IconHeadShadowChecks.Dump=args[3];
                AppBuilder.Configure<IconHeadShadowChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return IconHeadShadowChecks.Result;
            }
            if(args.Length>=4 && args[0]=="--zelda-pose-study") { ZeldaPoseChecks.Study(args[1],args[2],args[3]); return 0; }
            if (args.Length >= 3 && args[0] == "--icon-character-materials") { IconZeldaChecks.Inspect(args[1],args[2]); return 0; }
            if (args.Length >= 5 && args[0] == "--icon-zelda-gpu")
            {
                IconZeldaChecks.Root=args[1]; IconZeldaChecks.Output=args[2]; IconZeldaChecks.Dump=args[3]; IconZeldaChecks.Botw=args[4];
                AppBuilder.Configure<IconZeldaChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return IconZeldaChecks.Result;
            }
            if (args.Length >= 3 && args[0] == "--icon-ear-checks") { IconEarChecks.Inspect(args[1], args[2]); return 0; }
            if (args.Length >= 3 && args[0] == "--icon-texture-checks") { IconTextureChecks.Run(args[1], args[2], args.Contains("--inspect")); return 0; }
            if (args.Length >= 4 && args[0] == "--icon-capture-export-checks")
            {
                IconCaptureExportChecks.Root = args[1]; IconCaptureExportChecks.Actor = args[2]; IconCaptureExportChecks.Output = args[3];
                AppBuilder.Configure<IconCaptureExportChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return IconCaptureExportChecks.Result;
            }
            if (args.Length >= 2 && args[0] == "--icon-aamp")
            {
                string[] names = ["Name","Group","Enable","Color","DiffuseColor","SpecularColor","Intensity","Direction","SkyColor","GroundColor","HemiSkyColor","HemiGroundColor","Latitude","Longitude","Diffuse","Specular","name","group","enable","color","intensity","direction","diffuse_color","specular_color","sky_color","ground_color","upper_color","lower_color","light_color","DirLight","DirectionalLight","HemisphereLight","HemiLight"];
                var known = names.ToDictionary(BotwAampWriter.Crc32);
                string Label(uint h) => known.GetValueOrDefault(h) ?? h.ToString("X8");
                void Dump(AampReadList list, string indent)
                {
                    Console.WriteLine(indent + "LIST " + Label(list.Hash));
                    foreach (var obj in list.Objects)
                    {
                        Console.WriteLine(indent + " OBJECT " + Label(obj.Hash));
                        foreach (var p in obj.Parameters)
                        {
                            object value = p.Type switch { 0 => p.AsBoolean(), 1 => p.AsSingle(), 2 => p.AsInt32(), 3 => new Vector2(BitConverter.ToSingle(p.Bytes,0),BitConverter.ToSingle(p.Bytes,4)), 4 => p.AsVector3(), 5 or 6 or 16 => p.AsVector4(), 7 or 8 or 15 or 20 => p.AsString(), _ => Convert.ToHexString(p.Bytes) };
                            Console.WriteLine(indent + "  " + Label(p.Hash) + " [" + p.Type + "] = " + value);
                        }
                    }
                    foreach (var child in list.Children) Dump(child, indent + " ");
                }
                Dump(BotwAampReader.Read(args[1]).Root, ""); return 0;
            }
            if (args.Length >= 3 && args[0] == "--icon-env-archive")
            {
                var data = File.ReadAllBytes(args[1]);
                if (args[1].EndsWith(".zs")) data = new TotkResourceCompression().Decode(data, [args[2]]);
                Console.WriteLine("HEADER " + Convert.ToHexString(data.AsSpan(0, 32)));
                var archive = BotwSarcArchive.Parse(data);
                foreach (var entry in archive.Entries)
                {
                    Console.WriteLine("ENTRY " + entry.Name);
                    if (!entry.Name.Contains("capture", StringComparison.OrdinalIgnoreCase)) continue;
                    var raw = archive.Read(entry).ToArray();
                    Console.WriteLine("HEADER " + Convert.ToHexString(raw.AsSpan(0, Math.Min(32,raw.Length))));
                    File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "icon-env-" + Path.GetFileName(entry.Name)), raw);
                    Console.WriteLine(string.Join("\n", System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(raw), "[ -~]{5,}").Select(m=>m.Value)));
                }
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--icon-light-study")
            {
                foreach (string path in args.Skip(1))
                    Console.WriteLine(path + "\n" + BymlLibrary.Byml.FromBinary(File.ReadAllBytes(path)).ToYaml());
                return 0;
            }
            if (args.Length >= 4 && args[0] == "--icon-browser-checks")
            {
                IconBrowserChecks.Botw = args[1]; IconBrowserChecks.Totk = args[2]; IconBrowserChecks.Output = args[3];
                AppBuilder.Configure<IconBrowserChecks>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime([]);
                return IconBrowserChecks.Result;
            }
            if (args.Length >= 3 && args[0] == "--icon-preset-checks")
            {
                IconPresetChecks.Root = args[1]; IconPresetChecks.Output = args[2];
                AppBuilder.Configure<IconPresetChecks>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime([]);
                return IconPresetChecks.Result;
            }
            if (args.Length >= 4 && args[0] == "--icon-library-audit") { IconLibraryAudit.Run(args[1], args[2], args[3]); return 0; }
            if (args.Length >= 3 && args[0] == "--icon-neck-study") { IconNeckStudy.Run(args[1], args[2]); return 0; }
            if (args.Length >= 4 && args[0] == "--icon-neck-inset") { IconNeckStudy.Inset(args[1], args[2], float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture)); return 0; }
            if (args.Length >= 5 && args[0] == "--icon-character-gpu")
            {
                IconCharacterChecks.Root = args[1]; IconCharacterChecks.Output = args[2];
                IconCharacterChecks.Dump = args[3]; IconCharacterChecks.Botw = args[4];
                AppBuilder.Configure<IconCharacterChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return IconCharacterChecks.Result;
            }
            if (args.Length >= 3 && args[0] == "--icon-props-gpu")
            {
                IconPropsChecks.Root = args[1]; IconPropsChecks.Output = args[2];
                IconPropsChecks.BuiltInOnly = args.Contains("--builtin-only");
                IconPropsChecks.BotwRoot = IconPropsChecks.BuiltInOnly ? "" : args.Length > 3 ? args[3] : args[1];
                AppBuilder.Configure<IconPropsChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return IconPropsChecks.Result;
            }
            if (args.Length >= 3 && args[0] == "--icon-dyes-gpu")
            {
                IconDyeChecks.Root = args[1]; IconDyeChecks.Output = args[2];
                AppBuilder.Configure<IconDyeChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return IconDyeChecks.Result;
            }
            if (args.Length >= 3 && (args[0] == "--icon-workflow-gpu" || args[0] == "--icon-candidate-gpu" && args.Contains("--recovered")))
            {
                IconWorkflowChecks.Root = args[1]; IconWorkflowChecks.Output = args[2];
                IconWorkflowChecks.BotwRoot = args.Length > 3 && !args[3].StartsWith("--") ? args[3] : null;
                AppBuilder.Configure<IconWorkflowChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return IconWorkflowChecks.Result;
            }
            if (args.Length >= 3 && args[0] == "--icon-candidate-gpu")
            {
                IconCandidateGpuStudy.Root = args[1]; IconCandidateGpuStudy.Output = args[2];
                AppBuilder.Configure<IconCandidateGpuStudy>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return IconCandidateGpuStudy.Result;
            }
            if (args.Length >= 3 && args[0] == "--icon-lower-study")
            {
                IconLowerStudy.Root = args[1]; IconLowerStudy.Output = args[2];
                IconLowerStudy.Before = args.Length > 3 ? args[3] : null;
                AppBuilder.Configure<IconLowerStudy>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return IconLowerStudy.Result;
            }
            if (args.Length >= 3 && args[0] == "--icon-native-study") { IconNativeAssetStudy.Run(args[1], args[2]); return 0; }
            if (args.Length >= 3 && args[0] == "--icon-pose-export") { IconPoseRecoveryStudy.Export(args[1], args[2]); return 0; }
            if (args.Length >= 2 && args[0] == "--icon-belt-inspect") { IconPoseRecoveryStudy.InspectBelt(args[1]); return 0; }
            if (args.Length >= 4 && args[0] == "--icon-waist-trim") { IconPoseRecoveryStudy.TrimWaist(args[1],args[2],float.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture)); return 0; }
            if (args.Length >= 3 && args[0] == "--icon-reference-study") { IconReferenceStudy.Run(args[1], args[2]); return 0; }
            if (args.Length >= 3 && args[0] == "--icon-presets-export") { IconCaptureLibrary.Extract(args[1], args[2]); return 0; }
            if (args.Length >= 3 && args[0] == "--icon-tool-gpu")
            {
                IconToolChecks.Root = args[1]; IconToolChecks.Output = args[2];
                AppBuilder.Configure<IconToolChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return IconToolChecks.Result;
            }
            if (args.Length >= 2 && args[0] == "--auto-materials") { AutoMaterialChecks.Run(args[1]); return 0; }
            if (args.Length >= 3 && args[0] == "--auto-materials-gpu")
            {
                AutoMaterialGpuChecks.Root = args[1]; AutoMaterialGpuChecks.Output = args[2];
                AppBuilder.Configure<AutoMaterialGpuChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return AutoMaterialGpuChecks.Result;
            }
            if (args.Length >= 4 && args[0] == "--helper-poses-gpu")
            {
                HelperPoseGpuChecks.Physics = args[1]; HelperPoseGpuChecks.Model = args[2]; HelperPoseGpuChecks.Output = args[3];
                AppBuilder.Configure<HelperPoseGpuChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return HelperPoseGpuChecks.Result;
            }
            if (args.Length >= 2 && args[0] == "--helper-poses")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en"); HelperPoseChecks.Run(args[1]); return 0;
            }
            if (args.Length >= 2 && args[0] == "--oot-malon") { OotMalonChecks.Run(args[1]); return 0; }
            if (args.Length >= 1 && args[0] == "--oot-wind") { OotWindChecks.Run(); if (args.Length >= 2) OotWindChecks.Native(args[1]); return 0; }
            if (args.Length >= 1 && args[0] == "--oot-skin-mapping") { OotSkinMappingChecks.Run(); if (args.Length >= 2) OotSkinMappingChecks.Native(args[1]); return 0; }
            if (args.Length >= 1 && args[0] == "--oot-shape-constraints")
            { OotShapeConstraintChecks.Run(); if (args.Length >= 2) OotShapeConstraintChecks.Native(args[1]); return 0; }
            if (args.Length >= 3 && args[0] == "--oot-actor-catalog") { OotActorCatalogChecks.Run(args[1], args[2]); return 0; }
            if (args.Length >= 1 && args[0] == "--oot-virtual-equations") { OotVirtualCollisionChecks.Equations(); return 0; }
            if (args.Length >= 1 && args[0] == "--oot-pinch-reference") { OotPinchRecoveryChecks.Run(); return 0; }
            if (args.Length >= 2 && args[0] == "--oot-virtual-survey") { OotVirtualCollisionChecks.Survey(args[1]); return 0; }
            if (args.Length >= 1 && args[0] == "--oot-helper-simulation") { OotHelperSimulationChecks.Run(); return 0; }
            if (args.Length >= 2 && args[0] == "--oot-aero-survey") { OotCrossGameChecks.SurveyAerodynamics(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--totk-bonespace-schema") { OotCrossGameChecks.Survey(args[1]); return 0; }
            if (args.Length >= 4 && args[0] == "--oot-cross-game") { OotCrossGameChecks.Run(args[1], args[2], args[3]); return 0; }
            if (args.Length >= 3 && args[0] == "--oot-performance") { OotPerformanceChecks.Run(args[1], args[2]); return 0; }
            if (args.Length >= 2 && args[0] == "--oot-playback") { OotPlaybackChecks.Run(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--oot-animation-audit") { OotPlaybackChecks.Audit(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--oot-runtime") { OotRuntimeChecks.Run(args[1]); return 0; }
            if (args.Length >= 3 && args[0] == "--oot-runtime-ui")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en"); OotRuntimeChecks.Ui(args[1], args[2], args.ElementAtOrDefault(3) ?? "Player_Adult", args.ElementAtOrDefault(4) ?? "Bottle_Drink"); return 0;
            }
            if (args.Length >= 3 && args[0] == "--oot-topology") { OotTopologyChecks.Run(args[1], args[2]); return 0; }
            if (args.Length >= 3 && args[0] == "--oot-model") { OotModelChecks.Run(args[1], args[2]); return 0; }
            if (args.Length >= 3 && args[0] == "--oot-shirt")
            {
                OotShirtChecks.Root = args[1]; OotShirtChecks.Output = args[2]; Directory.CreateDirectory(args[2]);
                AppBuilder.Configure<OotShirtChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return OotShirtChecks.Result;
            }
            if (args.Length >= 3 && args[0] == "--oot-sit")
            {
                OotShirtChecks.Root = args[1]; OotShirtChecks.Output = args[2]; OotShirtChecks.MalonSit = true;
                Directory.CreateDirectory(args[2]);
                AppBuilder.Configure<OotShirtChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return OotShirtChecks.Result;
            }
            if (args.Length >= 3 && args[0] == "--oot-gpu")
            {
                OotGpuChecks.Root = args[1]; OotGpuChecks.Output = args[2]; Directory.CreateDirectory(args[2]);
                OotGpuChecks.Actor = args.ElementAtOrDefault(3) ?? "Player_Adult";
                OotGpuChecks.ClipName = args.ElementAtOrDefault(4) ?? "Bottle_Catch";
                AppBuilder.Configure<OotGpuChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return OotGpuChecks.Result;
            }
            if (args.Length >= 2 && args[0] == "--oot-actors") { OotActorChecks.Run(args[1]); return 0; }
            if (args.Length >= 3 && args[0] == "--oot-support-ui")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en"); OotSupportUiChecks.Run(args[1], args[2]); return 0;
            }
            if (args.Length >= 3 && args[0] == "--oot-editing") { OotEditingChecks.Run(args[1], args[2]); return 0; }
            if (args.Length >= 3 && args[0] == "--oot-geometry") { OotGeometryChecks.Run(args[1], args[2]); return 0; }
            if (args.Length >= 3 && args[0] == "--oot-ui")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en"); OotClothChecks.Ui(args[1], args[2]); return 0;
            }
            if (args.Length >= 4 && args[0] == "--oot-cloth") { OotClothChecks.Run(args[1], args[2], args[3]); return 0; }
            if (args.Length >= 3 && args[0] == "--oot-havok-survey") { OotHavokSurvey.Run(args[1], args[2]); return 0; }
            if (args.Length >= 2 && args[0] == "--map-placement-only")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en"); MapPlacementOnlyChecks.Run(args[1], args.ElementAtOrDefault(2)); return 0;
            }
            if (args.Length >= 2 && args[0] == "--map-catalog") { MapCatalogChecks.Run(args[1], args.ElementAtOrDefault(2)); return 0; }
            if (args.Length >= 2 && args[0] == "--caves") { TotkCaveChecks.Run(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--cave-geometry") { TotkCaveChecks.Geometry(args[1]); return 0; }
            if (args.Length >= 3 && args[0] == "--gpu-caves")
            {
                CaveGpuChecks.Root = args[1]; CaveGpuChecks.Output = args[2]; Directory.CreateDirectory(args[2]);
                AppBuilder.Configure<CaveGpuChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return CaveGpuChecks.Result;
            }
            if (args.Length >= 4 && args[0] == "--cave-audit") { MapAssetChecks.Cave(args[1], args[2], args[3]); return 0; }
            if (args.Length >= 2 && args[0] == "--merge-parts") { MergePartsChecks.Run(args[1]); return 0; }
            if (args.FirstOrDefault() == "--actor-connections") { ShrineActorConnectionChecks.Run(args.ElementAtOrDefault(1)); return 0; }
            if (args.Length >= 4 && args[0] == "--map-audit") { MapAssetChecks.Inspect(args[1], args[2], args[3]); return 0; }
            if (args.Length >= 4 && args[0] == "--map-hash") { MapAssetChecks.NearHash(args[1], Convert.ToUInt64(args[2].Replace("0x", ""), 16), args[3]); return 0; }
            if (args.FirstOrDefault() == "--terrain")
            {
                TotkTerrainChecks.Run(args.ElementAtOrDefault(1)); return 0;
            }
            if (args.Length >= 2 && args[0] == "--mainfield-scene")
            {
                MainFieldSceneChecks.Run(args[1], args.ElementAtOrDefault(2) ?? "B-7"); return 0;
            }
            if (args.FirstOrDefault() == "--shrine-shortcuts")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                ShrineSceneChecks.Shortcuts(); return 0;
            }
            if (args.Length >= 3 && args[0] == "--bphcl-delete-colliders")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en");
                BphclColliderDeletionChecks.Run(args[1], args[2]); return 0;
            }
            if (args.Length >= 2 && args[0] == "--bphcl-collision-layout")
            {
                BphclColliderPoseChecks.InspectSlots(args[1]); return 0;
            }
            if (args.Length >= 3 && args[0] == "--bphcl-collider-poses")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en");
                BphclColliderPoseChecks.Run(args[1], args[2]); BphclColliderPoseChecks.Ui(args[1]); return 0;
            }
            if (args.Length >= 3 && args[0] == "--resize-all")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en");
                WholeClothResizeChecks.Run(args[1], args[2]);
                WholeClothResizeUiChecks.Run(args[1], args[2]); return 0;
            }
            if (args.Length >= 3 && args[0] == "--bphcl-fit")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en");
                BphclRestShapeChecks.Run(args[1], args[2]); BphclRestShapeChecks.Ui(args[1], args[2]); return 0;
            }
            if (args.Length >= 2 && args[0] == "--delete-focus")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                DeletionFocusChecks.Run(args[1], args.ElementAtOrDefault(2)); return 0;
            }
            if (args.Length >= 3 && args[0] == "--actor-export-ui")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                ActorExportChecks.Ui(args[1], args[2]); return 0;
            }
            if (args.Length >= 3 && args[0] == "--actor-export-check") { ActorExportChecks.Run(args[1], args[2]); return 0; }
            if (args.Length >= 3 && args[0] == "--bphysics-native") { BphysicsEditorChecks.Native(args[1], args[2]); return 0; }
            if (args.Length >= 2 && args[0] == "--bphysics-editor")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                BphysicsEditorChecks.Run(args[1]); return 0;
            }
            if (args.Length >= 2 && args[0] == "--botw-wind") { BotwWindChecks.Run(args[1]); return 0; }
            if (args.Length >= 3 && args[0] == "--inspect-converted-wind") { BotwWindChecks.InspectConversion(args[1], args[2]); return 0; }
            if (args.Length >= 3 && args[0] == "--export-converted-wind") { BotwWindChecks.ExportConvertedWind(args[1], args[2]); return 0; }
            if (args.Length >= 3 && args[0] == "--survey-wind-mass") { BotwWindChecks.SurveyMass(args[1], args[2]); return 0; }
            if (args.Length >= 2 && args[0] == "--botw-actor-wind") { BotwWindChecks.Actor(args[1]); return 0; }
            if (args.Length >= 1 && args[0] == "--gpu-depth")
            {
                AppBuilder.Configure<ViewportDepthChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]); return ViewportDepthChecks.Result;
            }
            if (args.Length >= 3 && args[0] == "--shrine-textures")
            { ShrineTextureChecks.Run(args[1], args[2]); return 0; }
            if (args.Length >= 3 && args[0] == "--shrine-ground-control")
            {
                ShrineGroundControlChecks.Native(args[1], args[2]); return 0;
            }
            if (args.Length >= 2 && args[0] == "--shrine-population")
            { ShrinePopulationChecks.Native(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--shrine-collision-moves")
            { ShrinePlacementChecks.Compounds(args[1]); return 0; }
            if (args.Length >= 3 && args[0] == "--shrine-scene")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en"); ShrineSceneChecks.Run(args[1], args[2]); return 0;
            }
            if (args.Length >= 1 && args[0] == "--viewport-defaults")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                WorkspaceNavigationChecks.Defaults(); return 0;
            }
            if (args.Length >= 2 && args[0] == "--workspace-navigation")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en"); WorkspaceNavigationChecks.Run(args[1]); return 0;
            }
            if (args.Length >= 2 && args[0] == "--bphsc")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en"); BphscChecks.Run(args[1], args.ElementAtOrDefault(2)); return 0;
            }
            if (args.Length >= 3 && args[0] == "--actor-animation-import")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en");
                for (int i = 2; i < args.Length; i++) ActorAnimationImportChecks.Run(Path.Combine(args[1], (i - 2).ToString()), args[i]);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--collider-shortcuts")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en");
                ParticleColliderInspectorChecks.RunShortcuts(args[1]);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--cloth-fbx") { ClothFbxChecks.Run(args[1], args.ElementAtOrDefault(2)); return 0; }
            if (args.Length >= 3 && args[0] == "--native-bone-edits")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en"); NativeModelAlignmentChecks.CheckBoneEdits(args[1], args[2]); return 0;
            }
            if (args.Length >= 2 && args[0] == "--botw-player-inspect") { BotwPlayerChecks.Inspect(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--botw-player-check") { BotwPlayerChecks.Run(args[1]); return 0; }
            if (args.Length >= 3 && args[0] == "--actor-physics-ui")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                BotwPlayerChecks.PhysicsUi(args[1], args[2]); return 0;
            }
            if (args.Length >= 2 && args[0] == "--player-inspect") { PlayerArmorChecks.Inspect(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--timeline-scrub-check")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en");
                foreach (var path in args.Skip(1)) TimelineScrubbingChecks.Native(path);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--equipment-state-inspect") { EquipmentStateChecks.Inspect(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--equipment-state-check") { EquipmentStateChecks.Check(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--equipment-playback-ui")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                EquipmentStateChecks.Ui(args[1]); return 0;
            }
            if (args.Length >= 2 && args[0] == "--player-weapons-check") { PlayerWeaponChecks.Synthetic(); PlayerWeaponChecks.Native(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--player-armor-check") { PlayerArmorChecks.Native(args[1], args.ElementAtOrDefault(2)); return 0; }
            if (args.Length >= 3 && args[0] == "--player-armor-ui")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                PlayerArmorChecks.Ui(args[1], args[2]); return 0;
            }
            if (args.Length >= 2 && args[0] == "--actor-resource-check") { TotkPhysicsReferenceChecks.Resources(args.Skip(1)); return 0; }
            if (args.Length >= 2 && args[0] == "--orbit-render")
            {
                OverlayOrbitChecks.ActorPath = args[1];
                AppBuilder.Configure<OverlayOrbitChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return OverlayOrbitChecks.Result;
            }
            if (args.Length >= 2 && args[0] == "--actor-physics-check") { TotkPhysicsReferenceChecks.Synthetic(); TotkPhysicsReferenceChecks.Native(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--actor-physics-refs") { GanondorfChecks.References(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--ganondorf-check") { GanondorfChecks.Inspect(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--profile-cloth-render")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en"); ClothRenderingChecks.Profile(args[1], args.ElementAtOrDefault(2)); return 0;
            }
            if (args.Length >= 3 && args[0] == "--resize-branch")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en"); ClothBranchResizeChecks.Run(args[1], args[2]); return 0;
            }
            if (args.Length >= 3 && args[0] == "--audit-simulation-binding")
            { NativeModelAlignmentChecks.AuditSimulation(args[1], args[2]); return 0; }
            if (args.Length >= 3 && args[0] == "--native-model-alignment")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en");
                NativeModelAlignmentChecks.Run(args[1], args[2]); return 0;
            }
            if (args.Length >= 3 && args[0] == "--fit-cloth")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en"); ClothBoneFitChecks.Run(args[1], args[2]); return 0;
            }
            if (args.Length >= 3 && args[0] == "--cloth-reaction-survey") { ClothReactionChecks.Survey(args[1], args[2]); return 0; }
            if (args.Length >= 2 && args[0] == "--inspect-botw-wind") { ClothReactionChecks.InspectBotw(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--cloth-reactions")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                UiLanguage.Initialize("en"); ClothReactionChecks.Run(args[1]);
                if (args.Length > 2) ClothReactionChecks.Native(args[1], args.Skip(2).ToArray()); return 0;
            }
            if (args.Length >= 4 && args[0] == "--actor-animation") { ActorAnimationChecks.Run(args[1], args[2], args[3]); return 0; }
            if (args.Length >= 2 && args[0] == "--documentation")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                DocumentationChecks.Run(Path.GetFullPath(args[1])); return 0;
            }
            if (args.FirstOrDefault() == "--meshcodec") { MeshCodecChecks.Run(); MeshCodecChecks.Fixtures(args.Skip(1).ToArray()); return 0; }
            if (args.Length >= 2 && args[0] == "--audit-to-totk") { TotkConversionAudit.Run(args[1], args.ElementAtOrDefault(2)); return 0; }
            if (args.Length >= 2 && args[0] == "--mirror-cloth") { WholeClothMirrorChecks.Run(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--actor-particles") { ActorParticleChecks.Inspect(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--tegra-layout") { TegraLayoutChecks.Native(args[1]); return 0; }
            if (args.Length >= 3 && args[0] == "--dump-actor-textures") { TotkActorChecks.DumpTextures(args[1], args[2]); return 0; }
            if (args.Length >= 2 && args[0] == "--profile-actor") { TotkActorChecks.Profile(args[1]); return 0; }
            if (args.FirstOrDefault() == "--botw-shader-gpu")
            {
                var app = AppBuilder.Configure<BotwShaderGpuChecks>().UsePlatformDetect().UsePhysicsToolRendering();
                if (OperatingSystem.IsWindows() && args.Contains("--desktop-gl"))
                    app.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Wgl] });
                app.StartWithClassicDesktopLifetime([]);
                return BotwShaderGpuChecks.Result;
            }
            if (args.FirstOrDefault() == "--totk-native-gpu")
            {
                if (args.Length >= 3) { TotkNativeGpuChecks.Root = args[1]; TotkNativeGpuChecks.Output = args[2]; }
                var app = AppBuilder.Configure<TotkNativeGpuChecks>().UsePlatformDetect().UsePhysicsToolRendering();
                if (OperatingSystem.IsWindows() && args.Contains("--desktop-gl"))
                    app.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Wgl] });
                app.StartWithClassicDesktopLifetime([]);
                return TotkNativeGpuChecks.Result;
            }
            if (args.Length >= 2 && args[0] == "--totk-shader-gpu")
            {
                TotkShaderGpuChecks.ToolboxShader = args[1];
                AppBuilder.Configure<TotkShaderGpuChecks>().UsePlatformDetect().UsePhysicsToolRendering().StartWithClassicDesktopLifetime([]);
                return TotkShaderGpuChecks.Result;
            }
            if (args.Length >= 2 && args[0] == "--actor-browser")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                ActorBrowserChecks.Run(Path.GetFullPath(args[1])); return 0;
            }
            if (args.Length >= 2 && args[0] == "--inspect-totk-actor") { TotkActorChecks.Inspect(args[1]); return 0; }
            if (args.Length >= 3 && args[0] == "--totk-actor") { TotkActorChecks.Run(args[1], args[2]); return 0; }
            if (args.Length >= 2 && args[0] == "--totk-simulation")
            {
                TotkSimulationChecks.Run(args[1], args.Skip(2).ToArray()); return 0;
            }
            if (args.Length >= 3 && args[0] == "--dress-audit")
            {
                DressAuditChecks.Output = Path.GetFullPath(args[1]); Directory.CreateDirectory(DressAuditChecks.Output);
                DressAuditChecks.ActorPath = args[2];
                DressAuditChecks.ClipName = args.ElementAtOrDefault(3);
                AppBuilder.Configure<DressAuditChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return DressAuditChecks.Result;
            }
            if (args.Length >= 2 && args[0] == "--simulation-audit")
            {
                ClothSimulationChecks.Run(args[1]);
                SupportBoneSimulationChecks.Run(args[1]);
                SupportHierarchyChecks.Run();
                SimulationAuditChecks.TaperedContacts();
                if (args.Length > 2) SimulationAuditChecks.Actors(args[1], args.Skip(2).ToArray());
                Console.WriteLine("PASS: native cloth, collider, skinning, and helper-bone equations.");
                return 0;
            }
            if (args.Length >= 1 && args[0] == "--botw-materials") { BotwMaterialChecks.Run(args.Skip(1).ToArray()); return 0; }
            if (args.Length >= 2 && args[0] == "--survey-material") { BotwMaterialChecks.Survey(args[1]); return 0; }
            if (args.Length >= 1 && args[0] == "--actor-appearance") { ActorAppearanceChecks.Run(args.Skip(1).ToArray()); return 0; }
            if (args.Length >= 3 && args[0] == "--gpu-shrine")
            {
                ShrineGpuChecks.Output = Path.GetFullPath(args[1]); Directory.CreateDirectory(ShrineGpuChecks.Output); ShrineGpuChecks.Source = args[2];
                if (args.Length >= 6) ShrineGpuChecks.Focus = new(float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), float.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture), float.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture));
                AppBuilder.Configure<ShrineGpuChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]); return ShrineGpuChecks.Result;
            }
            if (args.Length >= 3 && args[0] == "--gpu-collision")
            {
                CollisionGpuChecks.Output = Path.GetFullPath(args[1]); Directory.CreateDirectory(CollisionGpuChecks.Output); CollisionGpuChecks.Source = args[2];
                AppBuilder.Configure<CollisionGpuChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]); return CollisionGpuChecks.Result;
            }
            if (args.Length >= 3 && args[0] == "--native-collision")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                CollisionChecks.Native(Path.GetFullPath(args[1]), args.Skip(2).ToArray()); return 0;
            }
            if (args.Length >= 2 && args[0] == "--collision")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                CollisionChecks.Run(Path.GetFullPath(args[1])); return 0;
            }
            if (args.Length >= 2 && args[0] is "--ragdoll-editor" or "--helper-editor")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                if (args[0] == "--helper-editor") HelperEditorChecks.Run(Path.GetFullPath(args[1]));
                else RagdollChecks.Run(Path.GetFullPath(args[1]));
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--file-workflow")
            {
                AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                DocumentChecks.Run(Path.GetFullPath(args[1]));
                FileWorkflowChecks.Run(Path.GetFullPath(args[1]));
                return 0;
            }
            if (args.Length >= 3 && args[0] == "--audit-cloth-conversion")
            {
                ClothConversionAudit.Run(args[1], args.Skip(2).ToArray()); return 0;
            }
            if (args.Length >= 2 && args[0] == "--collider-poses")
            {
                ColliderPoseChecks.Run(args[1], args.Skip(2).ToArray()); return 0;
            }
            if (args.FirstOrDefault() == "--gpu-animation")
            {
                ActorGpuChecks.Output = Path.GetFullPath(args[1]); Directory.CreateDirectory(ActorGpuChecks.Output);
                ActorGpuChecks.PathToModel = args[2]; ActorGpuChecks.PathToAnimation = args[3];
                ActorGpuChecks.PathToPhysics = args.Length > 4 ? args[4] : "";
                AppBuilder.Configure<ActorGpuChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return ActorGpuChecks.Result;
            }
            if (args.FirstOrDefault() == "--native-animation")
            {
                EditorAnimationChecks.Native(args[1], args[2]); return 0;
            }
            if (args.FirstOrDefault() == "--binding")
            {
                ModelBindingChecks.RunBfres(args[1]); return 0;
            }
            if (args.FirstOrDefault() == "--gpu-actor")
            {
                ActorGpuChecks.Output = Path.GetFullPath(args[1]); Directory.CreateDirectory(ActorGpuChecks.Output);
                ActorGpuChecks.PathToActor = args[2];
                ActorGpuChecks.ResourceRoots = args.Skip(3).ToArray();
                AppBuilder.Configure<ActorGpuChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return ActorGpuChecks.Result;
            }
            if (args.FirstOrDefault() == "--catalog")
            {
                Directory.CreateDirectory(args[1]);
                var catalog = new BotwActorCatalog(Path.Combine(args[1], "catalog-test.json"));
                var result = catalog.Scan(args.Skip(2), null, CancellationToken.None);
                Require(result.Actors.Count > 0 && result.Actors.All(a => a.HasCloth || a.HasSupport), "Catalog filter failed");
                var embedded = result.Actors.Where(a => a.ArchiveEntry != null).ToArray();
                var totk = result.Actors.Where(a => a.IsTotk).ToArray();
                Require(embedded.Length > 0 || totk.Length > 0, "TitleBG/TotK actors missing");
                Require(totk.All(a => !a.Name.EndsWith(".pack") && a.IsSwitch), "TotK catalog name/platform mismatch");
                var cached = catalog.Scan(args.Skip(2), null, CancellationToken.None);
                Require(result.Actors.SequenceEqual(cached.Actors), "Catalog cache changed actor metadata");
                Console.WriteLine($"PASS: {result.Actors.Count} cloth/helper actors, {embedded.Length} archive actors, {totk.Length} TotK actors, {result.Warnings.Count} warnings; cached scan matches.");
                return 0;
            }
            if (args.FirstOrDefault() == "--actors")
            {
                ActorChecks.Run(Path.GetFullPath(args[1]), args.Skip(2).ToArray()); return 0;
            }
            if (args.FirstOrDefault() == "--gpu")
            {
                GpuChecks.Output = Path.GetFullPath(args[1]); Directory.CreateDirectory(GpuChecks.Output);
                GpuChecks.ModelPath = args.Length > 2 ? Path.GetFullPath(args[2]) : ModelChecks.Run(GpuChecks.Output);
                AppBuilder.Configure<GpuChecks>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().StartWithClassicDesktopLifetime([]);
                return GpuChecks.Result;
            }
            var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/checks"); Directory.CreateDirectory(output);
            BotwWindChecks.Run(output);
            ClothFbxChecks.Run(Path.Combine(output, "cloth-fbx"));
            ClothConversionAudit.Synthetic();
            TotkSimulationChecks.Equations();
            OotHelperSimulationChecks.Run();
            OotShirtChecks.CheckOutputPolicy();
            OotVirtualCollisionChecks.Equations();
            OotPinchRecoveryChecks.Run();
            OotShapeConstraintChecks.Run();
            OotSkinMappingChecks.Run();
            OotWindChecks.Run();
            BphclColliderDeletionChecks.Synthetic();
            ActorParticleChecks.Synthetic();
            TotkPhysicsReferenceChecks.Synthetic();
            PlayerArmorChecks.Synthetic();
            PlayerWeaponChecks.Synthetic();
            ActorAnimationChecks.Synthetic();
            MeshCodecChecks.Run();
            TotkMaterialChecks.Run();
            ClothSimulationChecks.Run(Path.Combine(output, "simulation"));
            SupportBoneSimulationChecks.Run(Path.Combine(output, "simulation"));
            SupportHierarchyChecks.Run();
            TotkConversionAudit.Run(Path.Combine(output, "to-totk"));
            SimulationAuditChecks.TaperedContacts();
            var modelPath = ModelChecks.Run(output);
            ActorAppearanceChecks.Run([]);
            BotwMaterialChecks.Run([]);
            ModelBindingChecks.Run(output);
            for (int format = 0; format < 3; format++)
            {
                var session = new EditorSession(); session.New("PortableCloth", format);
                session.Edit((doc, cloth) =>
                {
                    doc.AddParticle(cloth); doc.AddParticle(cloth);
                    var rows = doc.GetParticleRows(cloth).ToArray();
                    rows[0].Fixed = true; rows[0].Y = .5f; rows[1].X = .3f; rows[1].Y = .4f; rows[2].X = -.3f; rows[2].Y = .4f;
                    doc.UpdateParticleRows(cloth, rows);
                    doc.AddClothConstraint(cloth, new[] { 1, 2 }, new ClothLinkOptions("hclStandardLinkConstraintSet", .6f, .05f));
                    doc.LinkParticles(cloth, new[] { 0, 1, 2 });
                });
                var full = session.Document.CaptureState(); session.Undo(); Require(session.Document.GetParticleRows(0).Count == 1, "Undo topology");
                session.Redo(); Require(session.Document.CaptureState() == full, "Redo topology");
                try { session.Edit((doc, cloth) => doc.DeleteParticles(cloth, new[] { 999 })); throw new Exception("Invalid delete accepted"); }
                catch (ArgumentOutOfRangeException) { }
                Require(session.Document.CaptureState() == full, "Rejected edit changed document");
                var path = Path.Combine(output, $"manual-{format}" + session.Document.CurrentExtension); session.Save(path); Require(!session.IsDirty, "Save leaves dirty");
                var reload = new EditorSession(); reload.Load(path); Require(reload.Document.GetParticleRows(0).Count == 3, "Roundtrip count");
                Require(reload.Document.GetParticlePreview(0).Triangles.Count == 1, "Roundtrip topology");
                if (format < 2)
                {
                    Require(reload.Platform == session.Platform, "Platform detection");
                    var simulator = new HkclPreviewSimulator(reload.Document.GetParticlePreview(0));
                    for (int frame = 0; frame < 120; frame++) simulator.Step(1f / 60);
                    Require(simulator.GetPositions().Values.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)), "Solver non-finite");
                }
                else if (reload.CanSimulate) Require(reload.CreateSimulator().Profile == ClothSolverProfile.TotkExperimental, "BPHCL must use the native TotK profile");
                else Require(!reload.Document.GetTotkSimulationSupport(0).Supported, "BPHCL runtime guard must explain unsupported cases");
            }
            Console.WriteLine("PASS: Wii U/Switch HKCL and BPHCL drafts, topology, save/reload, atomic edits, undo/redo, solver.");
            DocumentChecks.Run(output);
            ClothTuningChecks.Run(output);
            TransformPreviewChecks.Run(output);
            ColliderPoseChecks.Run(Path.Combine(output, "collider-poses"), []);
            foreach (var file in args.Skip(1))
            {
                var hash = SHA256.HashData(File.ReadAllBytes(file)); var fixture = new EditorSession(); fixture.Load(file); var document = fixture.Document;
                for (int cloth = 0; cloth < document.GetClothSummaries().Count; cloth++)
                {
                    var preview = document.GetParticlePreview(cloth); Require(preview.Particles.All(p => float.IsFinite(p.Position.X)), "Fixture positions");
                    document.GetParticleRows(cloth); document.GetBoneRows(cloth);
                    if (!document.IsHelperBoneSidecar) document.GetColliderRows(cloth);
                }
                if (document.IsHelperBoneSidecar) DocumentChecks.CheckHelper(file, output);
                document.SaveHkcl(Path.Combine(output, "fixture-" + Path.GetFileName(file)), fixture.Platform);
                Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(file))), "Source fixture changed");
                Console.WriteLine("PASS: native fixture " + Path.GetFileName(file));
            }
            AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            foreach (var helper in args.Skip(1).Where(p => Path.GetExtension(p) is ".bphyssb" or ".bphhb"))
            {
                var helperWindow = new MainWindow(graphics: false); helperWindow.Show(); helperWindow.LoadPath(helper);
                helperWindow.ShowEditor(); Dispatcher.UIThread.RunJobs(); helperWindow.ShowOverview();
                Require(!helperWindow.Session.IsDirty, "Helper inspection changed data"); helperWindow.Close();
            }
            var window = new MainWindow(graphics: false); window.Show(); Dispatcher.UIThread.RunJobs();
            ClothRenderingChecks.Run(output);
            Require(!window.IsEditorVisible && !window.GetVisualDescendants().OfType<PhysicsViewport>().Any(), "Startup opens viewport");
            foreach (var size in new[] { new Size(1440, 900), new Size(980, 640) })
            {
                window.Width = size.Width; window.Height = size.Height; Dispatcher.UIThread.RunJobs();
                using var startup = window.CaptureRenderedFrame()!; startup.Save(Path.Combine(output, $"startup-{size.Width}.png"));
            }
            window.LoadPath(Path.Combine(output, "manual-0.hkcl"));
            Require(!window.IsEditorVisible, "Opening physics bypassed overview");
            window.LoadDonorPath(Path.Combine(output, "manual-1.hkcl")); Dispatcher.UIThread.RunJobs();
            using (var overview = window.CaptureRenderedFrame()!) overview.Save(Path.Combine(output, "overview-980.png"));
            OverviewChecks.Run(window);
            PhysicsFileDropChecks.Run(output);
            ActorBrowserChecks.Run(output);
            TotkSimulationChecks.Editor(output);
            EditorUsabilityChecks.Run(output);
            ModelWireframeChecks.Run();
            ViewportVisibilityChecks.Run(output);
            TransformPreviewChecks.RunUi(output);
            SelectionConstraintChecks.Run(output);
            InspectorChecks.Run(output);
            ParticleColliderInspectorChecks.Run(output);
            HelperEditorChecks.Run(output);
            ClipboardChecks.Run(output);
            WholeClothMirrorChecks.Run(output);
            ConversionChecks.Run(output);
            ClothReactionChecks.Run(Path.Combine(output, "cloth-reactions"));
            FileWorkflowChecks.Run(output);
            RagdollChecks.Run(output);
            CollisionChecks.Run(output);
            var duplicate = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Duplicate selected"));
            Require(duplicate.IsEnabled, "Overview duplicate disabled");
            duplicate.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Require(window.Session.Document.GetClothSummaries().Count == 2, "Overview duplicate not wired");
            window.Session.Undo(); window.Refresh();
            var currentPath = window.Session.Path; window.SwapFiles();
            Require(window.DonorSession.Path == currentPath, "UI swap not wired"); window.SwapFiles();
            window.ShowEditor(); Dispatcher.UIThread.RunJobs();
            Require(!window.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Swap files") && b.IsEffectivelyVisible), "Swap available inside editor");
            foreach (var toggle in window.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Name is "ViewportParticles" or "ViewportColliders"))
                toggle.IsChecked = true;
            NavigationChecks.Run(window);
            ShrineSceneChecks.Shortcuts();
            ColliderChecks.Run(output);
            ModelBindingChecks.RunEditor(output);
            EditorAnimationChecks.Run(output);
            TuningWindowChecks.Run(window, output);
            BotwWindChecks.Ui(output);
            BphysicsEditorChecks.Run(Path.Combine(output, "bphysics-editor"));
            DeletionFocusChecks.Run(Path.Combine(output, "delete-focus"));
            foreach (var fixture in Directory.EnumerateFiles(output, "fixture-*.bphcl"))
                BphclRestShapeChecks.Run(fixture, Path.Combine(output, "bphcl-fitting", Path.GetFileNameWithoutExtension(fixture)));
            window.Viewport.SelectRectangle(new Rect(0, 0, window.Viewport.Bounds.Width, window.Viewport.Bounds.Height), SelectionOperation.Replace);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                window.ToggleSimulation(); Dispatcher.UIThread.RunJobs();
                Require(window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Add constraint")).IsEnabled == false, "Simulation allows add");
                window.StopSimulation(); Dispatcher.UIThread.RunJobs();
                Require(window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Add constraint")).IsEnabled, "Stop leaves add disabled");
            }
            foreach (var size in new[] { new Size(1440, 900), new Size(980, 640) })
            {
                window.Width = size.Width; window.Height = size.Height; Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No rendered frame");
                frame.Save(Path.Combine(output, $"editor-{size.Width}.png"));
                using var pixels = frame.Lock(); var bytes = new byte[pixels.RowBytes * pixels.Size.Height]; Marshal.Copy(pixels.Address, bytes, 0, bytes.Length);
                Require(Enumerable.Range(0, bytes.Length / 4).Select(i => BitConverter.ToUInt32(bytes, i * 4)).Distinct().Take(20).Count() >= 20, "Blank rendered editor");
            }
            var savedState = window.Session.Document.CaptureState();
            var load = FixtureImports.Model(window, modelPath);
            while (!load.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            load.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
            Require(window.Viewport.Model?.Meshes.Count == 2, "Model UI import");
            Require(window.Session.Document.CaptureState() == savedState && !window.Session.IsDirty, "Model changed physics document");
            var oldModel = window.Viewport.Model;
            window.Viewport.Pan(50, 30); window.Viewport.Orbit(.3f, .1f);
            var camera = window.Viewport.CameraState;
            var selected = window.Viewport.Selected.ToArray();
            window.ToggleSimulation(); window.ShowOverview();
            Require(!window.IsEditorVisible && window.Viewport.Simulated == null, "Back did not stop simulation");
            window.ShowEditor(); Dispatcher.UIThread.RunJobs();
            Require(camera == window.Viewport.CameraState && ReferenceEquals(oldModel, window.Viewport.Model), "Back reset model/camera");
            Require(selected.SequenceEqual(window.Viewport.Selected), "Back reset selection");
            var badLoad = window.LoadModelPathAsync(Path.Combine(output, "missing.bfres"));
            while (!badLoad.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            try { badLoad.GetAwaiter().GetResult(); throw new Exception("Missing model accepted"); } catch (FileNotFoundException) { }
            Require(ReferenceEquals(oldModel, window.Viewport.Model), "Failed import lost previous model");
            Require(window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Load model")).IsEnabled, "Import failure left button disabled");
            window.Viewport.SetMeshVisible(0, false); Require(window.Viewport.HiddenMeshes.Contains(0), "Hide mesh");
            using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "model-wireframe-980.png"));
            var docs = new DocumentationWindow(); docs.Show(); Dispatcher.UIThread.RunJobs();
            using (var frame = docs.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "documentation.png"));
            docs.Close(); window.Close();
            Console.WriteLine("PASS: Avalonia renders, simulation button recovery, documentation, desktop and compact sizes.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
