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
            ClothConversionAudit.Synthetic();
            TotkSimulationChecks.Equations();
            ActorParticleChecks.Synthetic();
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
            TransformPreviewChecks.RunUi(output);
            SelectionConstraintChecks.Run(output);
            InspectorChecks.Run(output);
            ParticleColliderInspectorChecks.Run(output);
            HelperEditorChecks.Run(output);
            ClipboardChecks.Run(output);
            WholeClothMirrorChecks.Run(output);
            ConversionChecks.Run(output);
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
            NavigationChecks.Run(window);
            ColliderChecks.Run(output);
            ModelBindingChecks.RunEditor(output);
            EditorAnimationChecks.Run(output);
            TuningWindowChecks.Run(window, output);
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
