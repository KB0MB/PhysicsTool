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
            if (args.FirstOrDefault() == "--gpu")
            {
                GpuChecks.Output = Path.GetFullPath(args[1]); Directory.CreateDirectory(GpuChecks.Output);
                GpuChecks.ModelPath = args.Length > 2 ? Path.GetFullPath(args[2]) : ModelChecks.Run(GpuChecks.Output);
                AppBuilder.Configure<GpuChecks>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime([]);
                return GpuChecks.Result;
            }
            var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/checks"); Directory.CreateDirectory(output);
            var modelPath = ModelChecks.Run(output);
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
                else Require(!reload.CanSimulate, "BPHCL simulation guard");
            }
            Console.WriteLine("PASS: Wii U/Switch HKCL and BPHCL drafts, topology, save/reload, atomic edits, undo/redo, solver.");
            foreach (var file in args.Skip(1))
            {
                var hash = SHA256.HashData(File.ReadAllBytes(file)); var document = new HkclService(); document.Load(file);
                for (int cloth = 0; cloth < document.GetClothSummaries().Count; cloth++)
                {
                    var preview = document.GetParticlePreview(cloth); Require(preview.Particles.All(p => float.IsFinite(p.Position.X)), "Fixture positions");
                    document.GetParticleRows(cloth); document.GetBoneRows(cloth); document.GetColliderRows(cloth);
                }
                document.SaveHkcl(Path.Combine(output, "fixture-" + Path.GetFileName(file)), document.IsBphcl ? HkclPlatform.Switch : HkclService.DetectHkclPlatform(file));
                Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(file))), "Source fixture changed");
                Console.WriteLine("PASS: native fixture " + Path.GetFileName(file));
            }
            AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
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
            window.ShowEditor(); Dispatcher.UIThread.RunJobs();
            NavigationChecks.Run(window);
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
            var load = window.LoadModelPathAsync(modelPath);
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
            var badLoad = window.LoadModelPathAsync(Path.Combine(output, "missing.obj"));
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
