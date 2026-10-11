using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class TotkSimulationChecks
{
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    public static void Equations()
    {
        var source = new ParticlePreviewData { SolverProfile = ClothSolverProfile.TotkExperimental, Gravity = new(0, -10, 0) };
        source.Particles.Add(new() { Index = 0, Mass = 2, InverseMass = .25f, Position = Vector3.Zero });
        var solver = new HkclPreviewSimulator(source);
        var dt = 1f / 60;
        solver.Step(dt);
        Require(MathF.Abs(solver.GetPositions()[0].Y + 5 * dt * dt) < 1e-7f, "TotK mass * inverse mass integration");
        solver.Step(dt * .5f);
        var expected = -5 * dt * dt * 1.5f - 5 * dt * dt * .25f;
        Require(MathF.Abs(solver.GetPositions()[0].Y - expected) < 1e-7f, "TotK variable-step velocity preservation");
        source.SolverProfile = ClothSolverProfile.BotwPreview;
        var botw = new HkclPreviewSimulator(source); botw.Step(dt);
        Require(MathF.Abs(botw.GetPositions()[0].Y + 10 * dt * dt) < 1e-7f, "BotW integration unchanged");
        source.SolverProfile = ClothSolverProfile.TotkExperimental;
        source.DampingPerSecond = .5f;
        solver = new(source); solver.Step(dt); solver.Step(dt);
        expected = -5 * dt * dt * (2 + MathF.Pow(.5f, dt));
        Require(MathF.Abs(solver.GetPositions()[0].Y - expected) < 1e-7f, "Native damping is fraction lost per second");
        source.Gravity = Vector3.Zero; source.DampingPerSecond = 1;
        solver = new(source); solver.SetParticleForces(new Dictionary<int, Vector3> { [0] = new(8, 0, 0) }); solver.Step(dt);
        Require(MathF.Abs(solver.GetPositions()[0].X - 2 * dt * dt) < 1e-7f, "TotK action force multiplied by inverse mass");
        solver.Step(dt);
        Require(MathF.Abs(solver.GetPositions()[0].X - 2 * dt * dt) < 1e-7f, "Action force leaked into the following frame");

        source.Particles.Add(new() { Index = 1, Fixed = true, Position = Vector3.UnitY });
        solver = new(source) { WindEnabled = true, WindSpeed = 12, RandomWindDirections = false, WindDirection = Vector3.UnitX, WindGustiness = 0 };
        solver.Step(dt);
        Require(MathF.Abs(solver.GetPositions()[0].X - 12 * dt * dt) < 1e-7f, "TotK preview wind must affect a dynamic particle");
        Require(solver.GetPositions()[1] == Vector3.UnitY, "Wind moved a fixed particle");
        solver.WindEnabled = false; var windPosition = solver.GetPositions()[0]; solver.Step(dt);
        Require(solver.GetPositions()[0] == windPosition, "Wind toggle did not stop new acceleration");
        solver.WindEnabled = true; solver.WindDirection = -Vector3.UnitX;
        for (var frame = 0; frame < 120; frame++) solver.Step(dt);
        windPosition = solver.GetPositions()[0]; solver.Step(dt);
        Require(solver.GetPositions()[0].X < windPosition.X, "Opposite live wind direction remained stuck");

        var contact = new ParticlePreviewData { SolverProfile = ClothSolverProfile.TotkExperimental, Gravity = Vector3.Zero };
        contact.Particles.Add(new() { Index = 0, Mass = 1, InverseMass = 1, Position = new(.5f, 0, 0), CollisionMask = 1u << 2 });
        contact.Colliders.Add(new() { Index = 7, CollisionBit = 0, Kind = ColliderPreviewKind.Sphere, Radius = 2, Start = Vector3.Zero });
        contact.Colliders.Add(new() { Index = 7, CollisionBit = 2, Kind = ColliderPreviewKind.Sphere, Radius = 1, Start = Vector3.Zero });
        solver = new(contact); solver.Step(dt);
        Require(MathF.Abs(solver.GetPositions()[0].X - 1) < 1e-5f, "Collision masks select slots, not global collider IDs");
        contact.Particles[0].CollisionMask = 0;
        solver = new(contact); solver.Step(dt);
        Require(solver.GetPositions()[0] == contact.Particles[0].Position, "Disabled collision mask");
        Console.WriteLine("PASS: TotK mass/inverse-mass, damping, variable timestep, native slot masks, and BotW isolation.");
    }

    public static void Run(string output, string[] paths)
    {
        Directory.CreateDirectory(output);
        Equations();
        ConversionFidelityChecks.Run();
        var report = new List<object>();
        var simulated = 0;
        foreach (var path in paths)
        {
            var hash = SHA256.HashData(File.ReadAllBytes(path));
            var session = new EditorSession(); session.Load(path);
            var before = session.Document.CaptureState();
            var native = session.Document.CaptureBphclNativeDocument()!;
            foreach (var cloth in native.Cloths)
            {
                session.ClothIndex = cloth.Index;
                var support = session.Document.GetTotkSimulationSupport(cloth.Index);
                if (!support.Supported)
                {
                    report.Add(new { File = Path.GetFileName(path), cloth.Name, Status = "Blocked", support.Blockers });
                    Console.WriteLine($"BLOCKED: {cloth.Name}: {string.Join(" ", support.Blockers)}");
                    continue;
                }
                var preview = session.Document.GetTotkSimulationPreview(cloth.Index);
                Require(preview.SolverProfile == ClothSolverProfile.TotkExperimental, "Native solver profile");
                Require(preview.ParticleAttachments.All(a => a.BoneWeights.All(w => w.ReferencePosition.HasValue)), "Decoded native skin positions");
                var config = cloth.Operators.Single(op => op.ClassName == "hclSimulateOperator").SimulateConfigs[0];
                Require(preview.ConstraintExecution.SequenceEqual(config.ConstraintExecution), "Native execution sequence preserved");
                var simulation = cloth.SimCloths[0];
                var expectedSlots = simulation.CollidableItemIndices.Select((item, slot) => (item, slot))
                    .Where(pair => native.Colliders.Single(c => c.ItemIndex == pair.item).Enabled &&
                        (config.UseAllInstanceCollidables || config.InstanceCollidablesUsed.Contains(pair.slot)))
                    .Select(pair => pair.slot).ToArray();
                Require(preview.Colliders.Select(c => c.CollisionBit).SequenceEqual(expectedSlots), "Native collision slot ordering");
                foreach (var collider in preview.Colliders)
                    if (simulation.CollidableTransformMap.TransformSetIndex >= 0)
                        Require(collider.BoneIndex == simulation.CollidableTransformMap.TransformIndices[collider.CollisionBit], "Authored collider bone map");
                var solver = session.CreateSimulator();
                var peak = 0f;
                for (var frame = 0; frame < 360; frame++)
                {
                    var time = frame / 60f;
                    var motion = Matrix4x4.CreateRotationZ(.12f * MathF.Sin(time * 2)) *
                        Matrix4x4.CreateTranslation(.06f * MathF.Sin(time * 3), .02f * MathF.Sin(time * 5), 0);
                    solver.SetInputBoneDeltas(preview.Bones.ToDictionary(b => b.Index, _ => motion));
                    solver.Step(1f / 60);
                    foreach (var p in solver.GetPositions())
                    {
                        Require(float.IsFinite(p.Value.LengthSquared()), cloth.Name + " finite particle positions");
                        peak = MathF.Max(peak, Vector3.Distance(p.Value, preview.Particles.Single(v => v.Index == p.Key).Position));
                    }
                    foreach (var pose in solver.GetBonePoses().Values)
                        Require(float.IsFinite(pose.Position.LengthSquared()), cloth.Name + " finite output bones");
                }
                Require(solver.NumericalResetCount == 0, cloth.Name + " native preview required numerical reset");
                solver.Reset();
                Require(solver.NumericalResetCount == 0, "Reset clears runtime diagnostics");
                simulated++;
                report.Add(new { File = Path.GetFileName(path), cloth.Name, Status = "Simulated", Frames = 360,
                    Particles = preview.Particles.Count, Colliders = preview.Colliders.Count, PeakDisplacement = peak,
                    support.Warnings });
                Console.WriteLine($"PASS: {cloth.Name}: {preview.Particles.Count} particles, {preview.Colliders.Count} active collider slots; 360 frames without reset.");
            }
            Require(session.Document.CaptureState() == before && !session.IsDirty, "Simulation must not edit the source document");
            Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Source file preserved");
        }
        File.WriteAllText(Path.Combine(output, "totk-simulation.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {simulated} native TotK cloths; finite/stability checks are not a claim of game parity.");
    }

    public static void Editor(string output)
    {
        var fixture = Path.Combine(output, "fixture-Armor_1106_RaulSkin_Upper.bphcl");
        if (!File.Exists(fixture)) return;
        var window = new MainWindow(graphics: false); window.Show(); window.LoadPath(fixture); window.ShowEditor();
        Dispatcher.UIThread.RunJobs();
        var run = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Run TotK preview"));
        Require(run.IsEnabled, "Native BPHCL Run button disabled");
        var state = window.Session.Document.CaptureState();
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.SelectionMode.HasFlag(SelectionMode.Multiple));
        list.SelectedItems!.Add(list.Items[0]); list.SelectedItems.Add(list.Items[1]);
        var add = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Add constraint"));
        Require(add.IsEnabled, "Native selection did not enable constraint editing");
        window.Viewport.Focus(); window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        Require(Equals(run.Content, "Stop simulation") && run.IsEnabled && !add.IsEnabled, "Native Run did not lock editing or preserve Stop");
        var wind = window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Wind"));
        Require(wind.IsEnabled, "Wind disabled during native simulation");
        wind.IsChecked = true;
        Require(window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Options")).IsEnabled, "Live wind options disabled");
        for (var frame = 0; frame < 60; frame++) window.AdvanceEditorPreview(1f / 60);
        Require(window.Viewport.Simulated is { Count: > 0 }, "Native UI produced no simulated positions");
        window.Width = 1100; window.Height = 760; Dispatcher.UIThread.RunJobs();
        using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "totk-preview.png"));
        window.Viewport.Focus(); window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        Require(Equals(run.Content, "Run TotK preview") && run.IsEnabled && add.IsEnabled && window.Viewport.Simulated == null, "Native Stop did not restore editing");
        Require(window.Session.Document.CaptureState() == state && !window.Session.IsDirty, "Native UI simulation modified source data");
        window.Close();
        Console.WriteLine("PASS: native TotK UI Space/Run/Stop, live positions, constraint-button recovery and source isolation.");
    }
}
