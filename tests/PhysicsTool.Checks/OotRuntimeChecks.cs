using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class OotRuntimeChecks
{
    internal static void Ui(string root, string output, string actorName = "Player_Adult", string clipName = "Bottle_Drink")
    {
        CheckBoneInheritance();
        Directory.CreateDirectory(output);
        var window = new MainWindow(graphics: false) { Width = 1280, Height = 850 };
        window.Show();
        window.Session.New("Keep my cloth", 0);
        var editorSession = window.Session; var editorState = editorSession.Document.CaptureState();
        var entry = new BotwActorCatalogEntry(actorName, Path.Combine(root, "Pack/Actor", actorName + ".pack.zs"), null, root, true, true, true) { IsOot = true };
        var load = window.OpenOotActor(entry);
        PumpUntil(() => window.OwnedWindows.Any() || load.IsCompleted, "OoT actor dialog");
        if (load.IsCompleted) load.GetAwaiter().GetResult();
        var dialog = window.OwnedWindows.Single();
        Dispatcher.UIThread.RunJobs();
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "OpenOotPhysics")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        PumpUntil(() => load.IsCompleted, "OoT actor/model/animation loading"); load.GetAwaiter().GetResult();
        if (window.Viewport.Model is not { Meshes.Count: > 0 }) throw new Exception("Actor command did not load the model");
        if (!window.Session.IsReadOnly || window.Viewport.CanSelect || window.Viewport.CanTransform) throw new Exception("OoT actor allows editing");
        var resources = OotActorResources.Load(entry);
        var expected = new OotActorPreview(resources.Assets);
        if (window.OotActorClothCount != expected.ClothCount || window.OotActorClothCount < 2 || window.Viewport.Data.Particles.Count != expected.Overlay.Particles.Count)
            throw new Exception("Actor viewer did not load every cloth");
        if (window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Add Particle")).IsVisible) throw new Exception("Actor edit tools visible");
        bool edited = false;
        try { window.Session.EditOotGeometry(new Dictionary<int, Vector3> { [0] = Vector3.Zero }); edited = true; } catch (InvalidOperationException) { }
        if (edited) throw new Exception("Read-only actor mutation was accepted");
        var animations = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ActorAnimation");
        if (animations.ItemCount < 3) throw new Exception("Actor animation references not loaded");
        var before = window.Session.Document.CaptureState();
        var rest = window.Viewport.Model.Meshes.SelectMany(m => m.Vertices).ToArray();
        // Avoid the bind/rest animation; choose a real moving clip from the actor set.
        int clipIndex = animations.Items.Cast<string>().ToList().FindIndex(n => n == clipName);
        if (clipName != "Bottle_Drink" && clipIndex < 1) throw new Exception("Requested actor animation missing: " + clipName);
        if (clipIndex < 1) clipIndex = animations.Items.Cast<string>().ToList().FindIndex(n => n.Contains("Bottle", StringComparison.OrdinalIgnoreCase));
        if (clipIndex < 1) clipIndex = 2;
        window.SelectEditorAnimation(clipIndex - 1);
        var timeline = window.GetVisualDescendants().OfType<Slider>().Single(c => c.Name == "ActorTimeline");
        foreach (var fps in new[] { 30, 60 })
        {
            window.ViewportFps = fps;
            window.SeekEditorAnimation(0); window.ToggleEditorPlayback();
            for (int step = 0; step < 12; step++) window.AdvanceEditorPreview(1f / 60);
            window.ToggleEditorPlayback();
            if (Math.Abs(window.EditorAnimationFrame - 12) > .001) throw new Exception("OoT playback speed changed with viewport FPS");
        }
        window.SeekEditorAnimation(Math.Min(10, timeline.Maximum / 2));
        if (rest.SequenceEqual(window.Viewport.Model.Meshes.SelectMany(m => m.Vertices))) throw new Exception("Animation did not move mesh");
        if (window.Viewport.Data.Particles.Count == 0) throw new Exception("Animation did not pose cloth");
        window.ToggleSimulation();
        if (!window.EditorSimulating) throw new Exception("OoT simulation UI still disabled");
        void CheckPausedTimer()
        {
            Vector3[] Positions() => window.Viewport.Simulated is { } simulated
                ? simulated.OrderBy(p => p.Key).Select(p => p.Value).ToArray()
                : window.Viewport.Data.Particles.Select(p => p.Position).ToArray();
            var heldFrame = timeline.Value;
            var heldParticles = Positions();
            var timerFrame = new DispatcherFrame();
            var deadline = DateTime.UtcNow.AddSeconds(8);
            using (DispatcherTimer.Run(() =>
            {
                timerFrame.Continue = heldParticles.SequenceEqual(Positions()) && DateTime.UtcNow < deadline;
                return timerFrame.Continue;
            }, TimeSpan.FromMilliseconds(20))) Dispatcher.UIThread.PushFrame(timerFrame);
            if (heldParticles.SequenceEqual(Positions()))
                throw new Exception("Paused-animation simulation timer did not advance cloth");
            if (timeline.Value != heldFrame) throw new Exception("Simulation advanced the paused animation");
        }
        CheckPausedTimer();
        window.SeekEditorAnimation(Math.Min(10, timeline.Maximum / 2));
        window.ToggleEditorPlayback();
        for (int i = 0; i < 90; i++) window.AdvanceEditorPreview(1f / 60);
        if (window.Viewport.Data.Particles.Any(p => !Finite(p.Position)) || window.Viewport.Model.Meshes.Any(m => m.Vertices.Any(v => !float.IsFinite(v))))
            throw new Exception("Animated simulation contains nonfinite coordinates");
        window.SeekEditorAnimation(0);
        for (int i = 0; i < 12; i++) window.AdvanceEditorPreview(1f / 60);
        var presented = window.Viewport.Data.Particles.ToDictionary(p => p.Index, p => p.Position);
        window.SeekEditorAnimation(0);
        for (int i = 0; i < 12; i++) window.AdvanceEditorPreview(1f / 60, present: i == 11);
        if (window.Viewport.Data.Particles.Any(p => Vector3.Distance(p.Position, presented[p.Index]) > 1e-6f))
            throw new Exception("Skipping intermediate model presentation changed simulation");
        window.SeekEditorAnimation(Math.Min(15, timeline.Maximum));
        if (before != window.Session.Document.CaptureState()) throw new Exception("Preview mutated OoT source");
        window.StopSimulation();
        CheckInvalidPoseStops(window);
        Dispatcher.UIThread.RunJobs();
        using (var shot = window.CaptureRenderedFrame()!) shot.Save(Path.Combine(output, "oot-animated-preview.png"));
        window.SelectEditorAnimation(-1);
        window.ToggleSimulation();
        CheckPausedTimer();
        if (window.Viewport.Model.Meshes.Any(m => m.Vertices.Any(v => !float.IsFinite(v)))) throw new Exception("Rest helpers with cloth produced nonfinite mesh");
        window.StopSimulation(); window.ShowOverview();
        if (!ReferenceEquals(window.Session, editorSession) || window.Session.Document.CaptureState() != editorState) throw new Exception("Leaving actor replaced the editor document");
        window.Session.Clear();
        window.Session.ImportOotCloth(resources.Assets.First(a => a.IsCloth).Bytes);
        window.ShowEditor();
        window.ToggleSimulation();
        CheckPausedTimer();
        window.StopSimulation();
        window.Close();
        Console.WriteLine($"PASS: OoT {actorName} Actor command loads all {expected.ClothCount} cloths ({expected.SimulatedClothCount} simulated); animation, seeking, inherited bones and unchanged source");
    }

    private static void CheckInvalidPoseStops(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_importedModel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var original = (ImportedModelRig)field.GetValue(window)!;
        var bad = new VisualModelData();
        bad.Bones.AddRange(original.Rest.Bones.Select(b => new VisualModelBone { Index = b.Index, ParentIndex = b.ParentIndex,
            Name = b.Name, Position = new(float.NaN, 0, 0), Rotation = b.Rotation, Scale = b.Scale }));
        try
        {
            field.SetValue(window, new ImportedModelRig(original.Rest, original.Sample) { NativeSkeleton = (_, _, _) => bad });
            window.ToggleEditorPlayback();
            window.AdvanceEditorPreview(1f / 60);
            if (window.EditorAnimationPlaying || window.EditorSimulating) throw new Exception("Invalid OoT pose did not stop playback.");
            var status = (TextBlock)typeof(MainWindow).GetField("_status", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
            if (status.Text?.StartsWith("OoT preview stopped:") != true) throw new Exception("Invalid OoT pose did not report its error.");
        }
        finally { field.SetValue(window, original); }
        window.SeekEditorAnimation(0);
    }

    private static void CheckBoneInheritance()
    {
        var pose = new VisualModelData();
        pose.Bones.AddRange([
            new() { Index = 0, ParentIndex = -1, Name = "Root", Position = new(1, 0, 0) },
            new() { Index = 1, ParentIndex = 0, Name = "Cloth", Position = new(1, 1, 0) },
            new() { Index = 2, ParentIndex = 1, Name = "Tip", Position = new(2, 1, 0) },
            new() { Index = 3, ParentIndex = 0, Name = "Unrelated", Position = new(1, -1, 0) }
        ]);
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        var outputs = new Dictionary<string, VisualBoneOverride> { ["Cloth"] = new("Cloth", new(1, 2, 0), turn, Vector3.One) };
        var result = OotActorPreview.InheritOutputs(pose, outputs).ToDictionary(b => b.Name);
        if (Vector3.Distance(result["Tip"].Position, new(1, 3, 0)) > 1e-5f ||
            Vector3.Distance(result["Unrelated"].Position, new(1, -1, 0)) > 1e-5f)
            throw new Exception("Cloth descendants did not inherit the corrected world pose");
        outputs["Tip"] = new("Tip", new(4, 5, 6), Quaternion.Identity, Vector3.One);
        result = OotActorPreview.InheritOutputs(pose, outputs).ToDictionary(b => b.Name);
        if (result["Tip"].Position != new Vector3(4, 5, 6)) throw new Exception("Explicit child output inherited its parent twice");
    }

    private static void PumpUntil(Func<bool> done, string operation)
    {
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (!done() && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        if (!done()) throw new Exception(operation + " timed out");
    }

    internal static void Run(string root)
    {
        int supported = 0, blocked = 0, pinchFallbacks = 0, virtualCloths = 0, virtualPoints = 0, sidedVirtualCloths = 0;
        long virtualCorrections = 0;
        var reasons = new SortedDictionary<string, int>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Phive/Cloth"), "*.bphcl"))
        {
            var session = new EditorSession(); session.Load(path);
            var actor = new OotActorPreview([new(path, File.ReadAllBytes(path), true)]);
            if (actor.ClothCount != session.Document.GetClothSummaries().Count)
                throw new Exception("Actor overlay dropped a native cloth: " + path);
            actor.Update(null, null, true, 1f / 60, true, false, true);
            if (actor.Overlay.Particles.Any(p => !Finite(p.Position)))
                throw new Exception("Unsupported-layout actor fallback produced invalid positions: " + path);
            foreach (var cloth in session.Document.CaptureBphclNativeDocument()!.Cloths)
            {
                session.ClothIndex = cloth.Index;
                var support = session.Document.GetTotkSimulationSupport(cloth.Index);
                if (!support.Supported)
                {
                    blocked++;
                    foreach (var reason in support.Blockers) reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
                    continue;
                }
                try
                {
                    var preview = session.Document.GetTotkSimulationPreview(cloth.Index);
                    if (preview.PinchDetectionEnabled) pinchFallbacks++;
                    var simulator = new HkclPreviewSimulator(preview);
                    simulator.Reset();
                    var initial = simulator.GetPositions();
                    var initialColliders = simulator.GetColliderPoses();
                    for (int i = 0; i < 120; i++) simulator.Step(1f / 60);
                    if (simulator.VirtualCollisionPointCount > 0)
                    {
                        virtualCloths++; virtualPoints += simulator.VirtualCollisionPointCount;
                        virtualCorrections += simulator.VirtualCollisionCorrectionCount;
                    }
                    if (simulator.NumericalResetCount != 0 || simulator.GetPositions().Values.Any(v => !Finite(v)))
                        throw new Exception("Nonfinite or reset simulation");
                    var delta = Matrix4x4.CreateTranslation(.13f, .24f, -.1f);
                    simulator.SetInputBoneDeltas(preview.Bones.ToDictionary(b => b.Index, _ => delta));
                    simulator.Reset();
                    foreach (var p in preview.Particles.Where(p => p.Fixed && preview.ParticleAttachments.Any(a => a.ParticleIndex == p.Index)))
                        if (Vector3.Distance(simulator.GetPositions()[p.Index], initial[p.Index] + delta.Translation) > .003f)
                            throw new Exception("Fixed particle detached from animated skeleton");
                    if (simulator.GetColliderPoses().Values.Any(c => !Finite(c.Start) || !Finite(c.End)))
                        throw new Exception("Nonfinite collider pose");
                    foreach (var c in simulator.GetColliderPoses().Values.Where(c => preview.Bones.Any(b => b.Index == c.BoneIndex)))
                        if (Vector3.Distance(c.Start, initialColliders[c.Index].Start + delta.Translation) > .003f ||
                            Vector3.Distance(c.End, initialColliders[c.Index].End + delta.Translation) > .003f)
                            throw new Exception("Collider detached from animated skeleton");
                    for (int i = 0; i < 30; i++) simulator.Step(1f / 60);
                    if (CheckSidedVirtualVariant(session.Document, cloth.Index, preview)) sidedVirtualCloths++;
                    supported++;
                }
                catch (Exception ex) { throw new Exception(Path.GetFileName(path) + "/" + cloth.Name, ex); }
            }
        }
        foreach (var (reason, count) in reasons) Console.WriteLine($"Guarded {count}: {reason}");
        if (supported == 0) throw new Exception("No OoT cloths supported");
        Console.WriteLine($"PASS: {supported} OoT cloth previews; {blocked} protected layouts; {pinchFallbacks} ordinary-contact pinch fallbacks");
        if (virtualCloths == 0 || virtualCorrections == 0) throw new Exception("Native virtual collision fixtures did not exercise contacts");
        Console.WriteLine($"Virtual contacts: {virtualCloths} cloths, {virtualPoints} authored samples, {virtualCorrections} corrections");
        Console.WriteLine(sidedVirtualCloths == 0
            ? "SKIP: native one-sided variants need authored normal topology and eligible unique collider slots; synthetic paths are tested separately."
            : $"PASS: {sidedVirtualCloths} in-memory one-sided variants of native virtual cloths; source unchanged");
        foreach (var name in new[] { "Player_Adult_01_Animation", "Npc_Hylia_WU_Zelda_Adult_Animation" })
        {
            var file = Path.Combine(root, "Model", name + ".anim.bfres.zs");
            MainWindow.RequireBfres(file);
            var clips = ModelAnimationClip.Load(file);
            if (clips.Count == 0 || clips.Any(c => !double.IsFinite(c.EndFrame) || c.EndFrame <= 0)) throw new Exception("Invalid animations");
            Console.WriteLine($"PASS: {name}: {clips.Count} compressed animations, {clips.Sum(c => c.BoneNames.Count())} tracks");
        }
    }

    private static bool CheckSidedVirtualVariant(HkclService service, int clothIndex, ParticlePreviewData preview)
    {
        if (preview.VirtualCollisionData is not { Points.Count: > 0 }) return false;
        var doc = service.CaptureBphclNativeDocument()!;
        var sim = doc.Cloths[clothIndex].SimCloths.Single();
        if (sim.TriangleFlips.Count == 0) return false;
        var collider = preview.Colliders.FirstOrDefault(c => c.VirtualCollisionEnabled &&
            c.Kind is ColliderPreviewKind.Sphere or ColliderPreviewKind.Capsule &&
            sim.CollidableItemIndices.Count(i => i == sim.CollidableItemIndices[c.CollisionBit]) == 1);
        if (collider == null) return false;
        var type = doc.TypeDefinitions.Single(t => t.TypeIndex == doc.GetItem(sim.ItemIndex).TypeIndex);
        var member = type.Members.Single(m => m.Name == "setupCollidableSidedness");
        if (!doc.TryGetReferencedItem(sim.DataOffset + member.Offset, out int index)) return false;
        var array = doc.GetItem(index);
        if (array.Count != sim.CollidableItemIndices.Count) throw new Exception("Invalid native sidedness slot array");
        int at = checked(doc.DataSection!.PayloadOffset + (int)array.DataOffset + collider.CollisionBit);
        string before = service.CaptureState();
        // Clone and reparse to enable computed normals as well as sidedness.
        // Vanilla virtual cloths may not request normals at all.
        var bytes = doc.Bytes.ToArray();
        bytes[at] = 1;
        bytes[checked(doc.DataSection.PayloadOffset + (int)sim.DataOffset +
            (int)type.Members.Single(m => m.Name == "doNormals").Offset)] = 1;
        var variant = new HkclService();
        variant.RestoreState("BPHCL:" + Convert.ToBase64String(bytes));
        var sided = variant.GetTotkSimulationPreview(clothIndex);
        if (sided.OotContactSurface == null || sided.VirtualCollisionData!.Points.Count != preview.VirtualCollisionData.Points.Count)
            throw new Exception("Native sidedness setup discarded virtual fan data");
        var solver = new HkclPreviewSimulator(sided);
        solver.Reset();
        for (int i = 0; i < 60; i++) solver.Step(1f / 60);
        if (solver.NumericalResetCount != 0 || solver.GetPositions().Values.Any(v => !Finite(v)))
            throw new Exception("One-sided native fixture produced nonfinite/reset simulation");
        if (service.CaptureState() != before) throw new Exception("One-sided fixture modified the source document");
        return true;
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
