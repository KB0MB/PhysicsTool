using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenTK.Graphics.OpenGL4;

namespace HKCLTool;

internal static class ActorViewerChecks
{
    public static void Run(string path, string directory, string? actorEntry = null)
    {
        Directory.CreateDirectory(directory);
        try
        {
            ApplicationConfiguration.Initialize(); AppLocalization.Initialize();
            var clock = Stopwatch.StartNew();
            using var scene = BotwActorScene.Load(path, actorEntry: actorEntry);
            var loadedMs = clock.Elapsed.TotalMilliseconds;
            CheckClothSettings(scene, directory);
            if (actorEntry != null)
            {
                var archive = BotwSarcArchive.OpenIndexed(path);
                var packed = archive.Read(archive.Entries.First(e => e.Name == actorEntry)).ToArray();
                var loosePath = Path.Combine(directory, Path.GetFileName(actorEntry));
                File.WriteAllBytes(loosePath, packed);
                var loose = new BotwActorResources(loosePath, scene.Resources.Roots);
                var resource = scene.Resources.Models[0].Resource;
                Require(loose.ReadModel(resource)!.SequenceEqual(scene.Resources.ReadModel(resource)!), "Loose actor failed to resolve the matching TitleBG model.");
            }
            Require(scene.RestModel.Meshes.Count > 0, "No model meshes loaded.");
            var model = ((List<ToolboxActorModel>)typeof(BotwActorScene).GetField("_models", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!)[0];
            var attachment = Matrix4x4.CreateRotationY(0.4f) * Matrix4x4.CreateTranslation(2, 3, -1);
            var attached = model.Pose(null, 0, attachment: attachment);
            var vertices = model.RestPose.Meshes[0].Vertices;
            var expected = Vector3.Transform(new Vector3(vertices[0], vertices[1], vertices[2]), attachment);
            var actual = attached.Meshes[0].Vertices;
            Require(Vector3.Distance(expected, new Vector3(actual[0], actual[1], actual[2])) < 0.0001f, "Model attachment did not transform its mesh.");
            Require(Vector3.Distance(Vector3.Transform(model.RestPose.Bones[0].Position, attachment), attached.Bones[0].Position) < 0.0001f, "Model attachment did not transform its skeleton.");
            Require(scene.TextureCount > 0, "No actor textures loaded.");
            if (scene.Resources.PhysicsFiles.Any(p => p.EndsWith(".hkcl"))) Require(scene.HasCloth, "Actor cloth was not loaded: " + string.Join("; ", scene.Resources.Warnings));
            if (scene.Resources.PhysicsFiles.Any(p => p.EndsWith(".hkrg"))) Require(scene.RagdollCount > 0, "Actor ragdoll was not loaded.");
            var animation = scene.Animations.FirstOrDefault(a => a.Name == "Default_Wait")
                ?? scene.Animations.FirstOrDefault(a => a.Name.Contains("Wait") && a.FrameCount > 10)
                ?? scene.Animations.FirstOrDefault(a => a.FrameCount > 10);
            var simulationMs = 0.0;
            if (animation != null)
            {
                var first = scene.Update(animation, 0, false, 0, true);
                var middle = scene.Update(animation, animation.FrameCount * 0.5, false, 0);
                Require(!first.Meshes[0].Vertices.SequenceEqual(middle.Meshes[0].Vertices) || first.Bones.Where((b, i) => Vector3.Distance(b.Position, middle.Bones[i].Position) > 0.00001f).Any(), "Animation did not change the pose.");
                if (scene.HasCloth)
                {
                    var layers = ((System.Collections.IEnumerable)typeof(BotwActorScene).GetField("_layers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!).Cast<object>().ToArray();
                    clock.Restart();
                    for (var i = 0; i < 600; i++)
                    {
                        var pose = scene.Update(animation, (i * 0.5) % animation.FrameCount, true, 1f / 60, i == 0);
                        Require(pose.Meshes.All(m => m.Vertices.All(float.IsFinite)), "Simulation produced non-finite mesh vertices.");
                        Require(scene.Overlay.Particles.All(p => float.IsFinite(p.Position.LengthSquared())), "Simulation produced non-finite particle positions.");
                        foreach (var layer in layers)
                            ClothBoneDeformChecks.Measure(
                                (ParticlePreviewData)layer.GetType().GetProperty("Source")!.GetValue(layer)!,
                                (HkclPreviewSimulator)layer.GetType().GetProperty("Solver")!.GetValue(layer)!);
                    }
                    simulationMs = clock.Elapsed.TotalMilliseconds / 600;
                    var diagnostics = layers.Select(layer => {
                        var solver = (HkclPreviewSimulator)layer.GetType().GetProperty("Solver")!.GetValue(layer)!;
                        var source = (ParticlePreviewData)layer.GetType().GetProperty("Source")!.GetValue(layer)!;
                        Require(solver.NumericalResetCount == 0, "Simulation relied on emergency particle resets during the 600-frame actor test.");
                        var positions = solver.GetPositions();
                        var errors = source.Links.Where(l => l.RestLength > 0.0001f && l.Kind.Contains("StandardLink"))
                            .Select(l => MathF.Abs(Vector3.Distance(positions[l.ParticleA], positions[l.ParticleB]) / l.RestLength!.Value - 1)).ToArray();
                        return new { Particles = source.Particles.Count, Resets = solver.NumericalResetCount,
                            BoneDeform = ClothBoneDeformChecks.Measure(source, solver),
                            source.SimulationSubSteps, source.SimulationSolveIterations, source.AdaptConstraintStiffness,
                            source.UseNativeHkclInputSampling,
                            source.ConstraintExecution, ConstraintKinds = source.ConstraintSets.Select(s => s.Kind).ToArray(),
                            WorstLinks = source.Links.Where(l => l.RestLength > 0.0001f && l.Kind.Contains("StandardLink"))
                                .Select(l => new { l.ParticleA, l.ParticleB, l.ConstraintSetIndex, l.RestLength, l.Stiffness,
                                    Length = Vector3.Distance(positions[l.ParticleA], positions[l.ParticleB]) })
                                .OrderByDescending(l => MathF.Abs(l.Length / l.RestLength!.Value - 1)).Take(5).ToArray(),
                            MeanStandardLinkRelativeError = errors.Length > 0 ? errors.Average() : 0,
                            MaximumStandardLinkRelativeError = errors.Length > 0 ? errors.Max() : 0 };
                    }).ToArray();
                    File.WriteAllText(Path.Combine(directory, "simulation-soak.json"), JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true }));
                }
                var stopped = scene.Update(animation, animation.FrameCount * 0.5, false, 0, true);
                Require(middle.Meshes[0].Vertices.SequenceEqual(stopped.Meshes[0].Vertices), "Simulation contaminated animation-only playback.");
            }
            scene.Update(null, 0, false, 0, true);
            using var main = new MainForm { WindowState = FormWindowState.Normal, MinimumSize = new Size(720, 500), ClientSize = new Size(1150, 850) };
            main.Show();
            var form = main.ShowActorView();
            form.ShowScene(scene, path); Application.DoEvents();
            if (animation != null) CheckPlaybackAndWind(form, animation, directory);
            Require(!form.TopLevel && form.Parent == main, "Actor viewer opened as a separate window.");
            Require(Application.OpenForms.Cast<Form>().Count(f => f.TopLevel && f.Visible) == 1, "Actor import created another top-level window.");
            CheckPhysicsPresentation(form.Preview, scene);
            if (animation != null)
            {
                foreach (var simulate in new[] { false, true })
                foreach (var fraction in new[] { 0.0, 0.23, 0.61, 0.89 })
                {
                    form.Preview.SetVisualModelPose(scene.Update(animation, animation.FrameCount * fraction, simulate, simulate ? 1f / 60 : 0, true));
                    CheckPhysicsPresentation(form.Preview, scene);
                }
                form.Preview.SetVisualModelPose(scene.Update(null, 0, false, 0, true));
            }
            File.WriteAllText(Path.Combine(directory, "presentation.txt"),
                "PASS: model, bone axes, particles, and colliders share the same rendered coordinate space at rest, during animation, and during simulation.");
            form.Preview.PhysicsOverlayVisible = false;
            Capture(form, Path.Combine(directory, "rest.png"));
            var transportPanel = Field<Button>(form, "_play").Parent!;
            var playbackPanel = transportPanel.Parent!;
            File.WriteAllText(Path.Combine(directory, "layout.json"), JsonSerializer.Serialize(new {
                Transport = transportPanel.Bounds.ToString(), TransportPreferred = transportPanel.GetPreferredSize(new Size(transportPanel.Width, 0)).ToString(),
                Playback = playbackPanel.Bounds.ToString(), PlaybackPreferred = playbackPanel.GetPreferredSize(new Size(playbackPanel.Width, 0)).ToString(),
                Status = Field<Label>(form, "_status").Bounds.ToString(),
                Rows = ((TableLayoutPanel)playbackPanel).GetRowHeights()
            }));
            form.Preview.PhysicsOverlayVisible = true; Field<CheckBox>(form, "_bones").Checked = true;
            if (animation != null)
            {
                Field<ComboBox>(form, "_animations").SelectedItem = animation;
                Field<NumericUpDown>(form, "_frameInput").Value = (decimal)(animation.FrameCount * 0.5);
                form.Preview.FitCameraToVisualModel();
            }
            Capture(form, Path.Combine(directory, "animated-physics.png"));
            if (animation != null && scene.HasCloth)
            {
                for (var i = 0; i < 240; i++)
                    form.Preview.SetVisualModelPose(scene.Update(animation, (i * 0.5) % animation.FrameCount, true, 1f / 60, i == 0));
                CheckPhysicsPresentation(form.Preview, scene);
                Capture(form, Path.Combine(directory, "simulated-physics.png"));
                form.Preview.PhysicsOverlayVisible = false;
                Capture(form, Path.Combine(directory, "simulated-cloth.png"));
                form.Preview.PhysicsOverlayVisible = true;
                form.Preview.SetVisualModelPose(scene.Update(animation, animation.FrameCount * 0.5, false, 0, true));
            }
            ActorViewerInteractionChecks.Run(form, directory);
            foreach (var size in new[] { new Size(720, 500), new Size(1150, 800) })
            {
                main.ClientSize = size; main.PerformLayout(); Application.DoEvents();
                Require(main.ClientRectangle.Contains(form.Bounds), "Embedded actor page is clipped.");
                Require(form.Bottom <= Field<Control>(main, "_statusStrip").Top || !Field<Control>(main, "_statusStrip").Visible, "Actor view overlaps the editor status strip.");
                var play = Field<Button>(form, "_play"); var timeline = Field<AnimationTimelineControl>(form, "_timeline");
                Require(play.Bottom <= play.Parent!.ClientSize.Height, "Transport button is clipped.");
                Require(timeline.Width > 100 && timeline.Height >= 40, "Timeline is clipped.");
                var transport = play.Parent!;
                Require(transport.Height <= transport.GetPreferredSize(new Size(transport.Width, 0)).Height + 2, "Transport has unused wrapped rows.");
            }
            Require(form.Preview.ReadOnlyPreview, "Actor viewport allows editing.");
            Require(!Field<CheckBox>(form, "_hide").Checked, "Physics is unexpectedly hidden by default.");
            Field<CheckBox>(form, "_hide").Checked = true;
            Require(!form.Preview.PhysicsOverlayVisible, "Hide physics did not hide the overlay.");
            if (actorEntry != null)
            {
                using var select = new System.Windows.Forms.Timer { Interval = 50 };
                select.Tick += (_, _) =>
                {
                    var picker = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f != form && f != main && f.Text == AppLocalization.Translate("Select actor"));
                    if (picker == null) return;
                    select.Stop();
                    var layout = picker.Controls.OfType<TableLayoutPanel>().Single();
                    layout.Controls.OfType<TextBox>().Single().Text = scene.Resources.ActorName;
                    Require(layout.Controls.OfType<ListBox>().Single().Items.Count == 1, "Actor picker search did not filter the list.");
                    using var bitmap = new Bitmap(picker.Width, picker.Height); picker.DrawToBitmap(bitmap, new Rectangle(Point.Empty, picker.Size));
                    bitmap.Save(Path.Combine(directory, "actor-picker.png"));
                    picker.DialogResult = DialogResult.OK;
                };
                select.Start();
                var selected = BotwActorSelectionDialog.Choose(main, new[] { "Actor/Pack/OtherActor.sbactorpack", actorEntry });
                Require(selected == actorEntry, "Actor picker returned the wrong nested actor.");
            }
            var language = AppLocalization.CurrentLanguage;
            try
            {
                foreach (var code in new[] { "en", "fr", "es", "ja" })
                {
                    // This diagnostic must not overwrite the user's settings file.
                    typeof(AppLocalization).GetProperty(nameof(AppLocalization.CurrentLanguage))!.SetValue(null, code);
                    AppLocalization.ApplyTo(form);
                    foreach (var size in new[] { new Size(720, 500), new Size(1150, 800) })
                    {
                        main.ClientSize = size; main.PerformLayout(); Application.DoEvents();
                        foreach (var control in new Control[] { Field<Button>(form, "_open"), Field<Button>(form, "_folders"), Field<Button>(form, "_meshes"), Field<CheckBox>(form, "_simulate"), Field<CheckBox>(form, "_hide"), Field<NumericUpDown>(form, "_windStrength"), Field<Button>(form, "_play") })
                        {
                            Require(control.Parent!.ClientRectangle.Contains(control.Bounds), $"{code}: {control.Text} is outside its toolbar.");
                            if (control is CheckBox) Require(TextRenderer.MeasureText(control.Text, control.Font).Width < control.Width, $"{code}: toggle text is clipped.");
                        }
                        Require(form.Preview.Height >= 150, $"{code}: viewport was squeezed out by controls.");
                    }
                    if (code == "fr") Capture(form, Path.Combine(directory, "french.png"));
                }
            }
            finally { typeof(AppLocalization).GetProperty(nameof(AppLocalization.CurrentLanguage))!.SetValue(null, language); }
            var result = new { Status = "PASS", scene.Resources.ActorName, scene.Resources.IsSwitch, scene.TextureCount,
                Meshes = scene.RestModel.Meshes.Count, Bones = scene.RestModel.Bones.Count, scene.ClothCount, scene.SupportCount,
                AnimationCount = scene.Animations.Count, scene.RagdollCount, LoadMilliseconds = loadedMs, SimulationFrameMilliseconds = simulationMs, scene.Resources.Warnings };
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            var activeScene = form.Scene;
            var frameBeforeBack = form.Preview.VisualModel!.AnimationFrame;
            Field<Button>(main, "_directEditButton").PerformClick(); Application.DoEvents();
            Require(!form.Visible && !Field<System.Windows.Forms.Timer>(form, "_timer").Enabled, "Hidden Actor view is still running.");
            Require(Field<SplitContainer>(main, "_outerSplit").Visible, "Back did not restore the editor view.");
            Require(main.ShowActorView() == form && ReferenceEquals(form.Scene, activeScene), "Returning to Actor reloaded or lost the scene.");
            Require(form.Preview.VisualModel!.AnimationFrame == frameBeforeBack, "Returning to Actor changed the paused frame.");
            main.Close();
            Require(form.IsDisposed, "Closing the main window leaked the actor viewer.");
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "error.txt"), ex.ToString()); Environment.ExitCode = 1; }
    }
    private static void CheckClothSettings(BotwActorScene scene, string directory)
    {
        var temporary = Field<string>(scene, "_temporaryDirectory");
        if (!Directory.Exists(temporary)) return;
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(temporary, "*.hkcl"))
        {
            var original = File.ReadAllBytes(path);
            ClothSkinReferenceChecks.CheckAsset(path, directory, count);
            var service = new HkclService(); service.Load(path);
            CheckColliderOrigins(service);
            var settings = service.GetSimulationSettings(0);
            for (var cloth = 0; cloth < service.GetClothSummaries().Count; cloth++)
                File.WriteAllText(Path.Combine(directory, $"reference-data-{count}-{cloth}.json"), JsonSerializer.Serialize(new {
                    Name = service.GetClothName(cloth), Skins = service.GetSkinOperatorData(cloth),
                    Ranges = service.GetLocalRangeConstraintSets(cloth),
                    Deformers = service.GetSimpleMeshBoneDeformData(cloth),
                    Bindings = service.GetParticlePreview(cloth).BoneBindings,
                    CollisionDiagnostics = service.GetCollisionDiagnostics(cloth),
                    ColliderBindingDiagnostics = service.GetColliderBindingDiagnostics(cloth),
                    Colliders = service.GetParticlePreview(cloth).Colliders,
                    PinchEnabled = service.GetParticlePreview(cloth).PinchDetectionEnabled,
                    PinchParticles = service.GetParticlePreview(cloth).PinchEnabledParticles,
                    PinchColliders = service.GetParticlePreview(cloth).ColliderPinching,
                    Bones = service.GetParticlePreview(cloth).Bones, Particles = service.GetParticlePreview(cloth).Particles,
                    Attachments = service.GetParticlePreview(cloth).ParticleAttachments,
                    PreviewRanges = service.GetParticlePreview(cloth).LocalRanges
                }, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
            settings.DampingPerSecond = 0.8f; settings.CollisionTolerance = 0.002f;
            service.UpdateSimulationSettings(0, settings);
            Require(service.GetSimulationSettings(0).CollisionTolerance == 0.002f, "Collision tolerance edit was ignored.");
            foreach (var invalid in new[] { -1f, 1.01f, float.NaN })
            {
                settings.DampingPerSecond = invalid;
                var rejected = false;
                try { service.UpdateSimulationSettings(0, settings); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Require(rejected, "Invalid HKCL damping was accepted.");
                Require(service.GetSimulationSettings(0).DampingPerSecond == 0.8f, "Rejected settings changed the document.");
            }
            var output = Path.Combine(directory, $"settings-roundtrip-{count++}.hkcl");
            var platform = HkclService.DetectHkclPlatform(path);
            service.SaveHkcl(output, platform);
            var reloaded = new HkclService(); reloaded.Load(output);
            CheckColliderOrigins(reloaded);
            for (var cloth = 0; cloth < service.GetClothSummaries().Count; cloth++)
            {
                Require(service.GetParticlePreview(cloth).UseNativeHkclInputSampling &&
                    reloaded.GetParticlePreview(cloth).UseNativeHkclInputSampling,
                    "Native HKCL input sampling was not enabled after import/save/reload.");
                var expected = service.GetParticlePreview(cloth).Colliders;
                var actual = reloaded.GetParticlePreview(cloth).Colliders;
                Require(actual.Count == expected.Count, "Settings edit changed collider count.");
                foreach (var collider in expected)
                {
                    var saved = actual.Single(c => c.Index == collider.Index);
                    Require(saved.TransformOrigin == collider.TransformOrigin, "Authored collider origin did not round-trip.");
                }
            }
            Require(HkclService.DetectHkclPlatform(output) == platform, "Settings edit changed HKCL platform.");
            Require(reloaded.GetSimulationSettings(0).DampingPerSecond == 0.8f, "Damping edit did not round-trip.");
            Require(reloaded.GetSimulationSettings(0).CollisionTolerance == 0.002f, "Collision tolerance edit did not round-trip.");
            Require(reloaded.GetParticlePreview(0).CollisionTolerance == 0.002f, "Collision tolerance did not reach the preview after saving.");
            Require(reloaded.GetParticlePreview(0).Particles.Count == service.GetParticlePreview(0).Particles.Count, "Settings edit changed cloth topology.");
            Require(File.ReadAllBytes(path).SequenceEqual(original), "Settings test changed the source asset.");
        }
        File.WriteAllText(Path.Combine(directory, "settings.txt"), $"PASS: {count} HKCL settings round trips, platform preservation, rejected edits, preview metadata, and authored collider origins.");
    }

    private static void CheckPlaybackAndWind(BotwActorViewerForm form, ToolboxActorAnimation animation, string directory)
    {
        var timer = Field<System.Windows.Forms.Timer>(form, "_timer");
        timer.Stop();
        var preference = typeof(AppLocalization).GetProperty(nameof(AppLocalization.AnimationFramesPerSecond))!;
        var original = AppLocalization.AnimationFramesPerSecond;
        var play = Field<Button>(form, "_play");
        var frame = Field<NumericUpDown>(form, "_frameInput");
        var simulate = Field<CheckBox>(form, "_simulate");
        var wind = Field<CheckBox>(form, "_wind");
        var strength = Field<NumericUpDown>(form, "_windStrength");
        try
        {
            Field<ComboBox>(form, "_animations").SelectedItem = animation;
            Field<CheckBox>(form, "_loop").Checked = true;
            foreach (var fps in new[] { 30, 60 })
            foreach (var physics in new[] { false, true })
            {
                if (physics && !form.Scene!.HasCloth) continue;
                preference.SetValue(null, fps);
                frame.Value = 1; frame.Value = 0; simulate.Checked = physics;
                play.PerformClick();
                for (var i = 0; i < 60; i++) form.AdvancePlayback(1.0 / 60);
                var expected = AppLocalization.GetAnimationPlaybackFrameRate(ToolboxActorAnimation.BaseFramesPerSecond) % animation.FrameCount;
                Require(Math.Abs(Field<double>(form, "_frame") - expected) < 0.000001, "Actor playback speed depends on physics or timer frequency.");
                play.PerformClick();
            }
            if (form.Scene!.HasCloth)
            {
                preference.SetValue(null, 60); simulate.Checked = true;
                frame.Value = (decimal)(animation.FrameCount - 0.25);
                strength.Value = 20; wind.Checked = true;
                for (var i = 0; i < 6; i++) form.AdvancePlayback(1.0 / 60);
                var layer = ((System.Collections.IEnumerable)Field<object>(form.Scene, "_layers")).Cast<object>().First();
                var solver = (HkclPreviewSimulator)layer.GetType().GetProperty("Solver")!.GetValue(layer)!;
                Require(solver.WindEnabled && solver.WindSpeed == 20, "Actor wind controls did not reach the solver.");
                var beforeTime = Field<float>(solver, "_windTime");
                play.PerformClick();
                form.AdvancePlayback(1.0 / 120);
                Require(Field<double>(form, "_frame") == animation.FrameCount - 0.25, "Animation wrapped before its pending physics step.");
                form.AdvancePlayback(1.0 / 120);
                Require(Math.Abs(Field<double>(form, "_frame") - 0.25) < 0.000001, "Animation did not wrap at the physics boundary.");
                Require(Field<float>(solver, "_windTime") > beforeTime, "Animation loop reset cloth simulation history.");
                form.AdvancePlayback(0.1);
                Require(Math.Abs(Field<double>(form, "_frame") - 3.25) < 0.000001, "Catch-up steps lost time after an animation wrap.");
                wind.Checked = false; form.AdvancePlayback(1.0 / 60);
                Require(!solver.WindEnabled, "Actor wind toggle did not switch off.");
                play.PerformClick();
            }
            File.WriteAllText(Path.Combine(directory, "playback-wind.txt"), "PASS: 30/60 playback rates with/without physics, fractional loop boundary, ordered catch-up, preserved loop history, live wind strength/toggle.");
        }
        finally
        {
            preference.SetValue(null, original); wind.Checked = false;
            strength.Value = (decimal)HkclPreviewSimulator.DefaultWindSpeed;
            Field<Button>(form, "_reset").PerformClick(); timer.Start();
        }
    }

    private static void CheckColliderOrigins(HkclService service)
    {
        for (var cloth = 0; cloth < service.GetClothSummaries().Count; cloth++)
        {
            var rows = service.GetColliderRows(cloth).ToDictionary(c => c.Index);
            foreach (var collider in service.GetParticlePreview(cloth).Colliders)
                Require(collider.TransformOrigin is { } origin && rows.TryGetValue(collider.Index, out var row)
                    && Vector3.Distance(origin, row.Transform.Translation) < 0.000001f,
                    "Preview collider origin differs from its authored HKCL transform.");
        }
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void CheckPhysicsPresentation(ParticlePreviewControl view, BotwActorScene scene)
    {
        // Check absolute handedness as well as alignment: two mirrored outputs
        // agree with each other but still display the actor incorrectly.
        var type = typeof(ParticlePreviewControl);
        var matrix = (OpenTK.Mathematics.Matrix4)type.GetMethod("GetVisualModelMatrix", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, null)!;
        var toGl = type.GetMethod("ToGl", BindingFlags.NonPublic | BindingFlags.Instance)!;
        OpenTK.Mathematics.Vector3 Overlay(Vector3 p) => (OpenTK.Mathematics.Vector3)toGl.Invoke(view, new object[] { p })!;
        OpenTK.Mathematics.Vector3 Model(Vector3 p) => OpenTK.Mathematics.Vector3.TransformPosition(new(p.X, p.Y, p.Z), matrix);
        void Match(Vector3 p, string description) => Require((Model(p) - Overlay(p)).Length < 0.0001f,
            $"Model/physics presentation mismatch for {description}: model={Model(p)}, overlay={Overlay(p)}.");
        foreach (var p in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ }) Match(p, "coordinate axis");
        Require(matrix.Determinant > 0, "Actor presentation must preserve handedness.");
        foreach (var p in new[] { new Vector3(1,2,3), new Vector3(-4,5,-6) })
            Require((Overlay(p)-new OpenTK.Mathematics.Vector3(p.X,p.Y,p.Z)).Length < 0.0001f, "Actor coordinates were reflected.");
        foreach (var p in scene.Overlay.Particles) Match(p.Position, $"particle {p.Index}");
        foreach (var c in scene.Overlay.Colliders) { Match(c.Start, $"collider {c.Index} start"); Match(c.End, $"collider {c.Index} end"); }
        var modelBones = scene.CurrentModel.Bones.GroupBy(b => b.Name).ToDictionary(g => g.Key, g => g.First());
        foreach (var bone in scene.Overlay.Bones)
        {
            if (!modelBones.TryGetValue(bone.Name, out var model)) continue;
            Require((Model(model.Position) - Overlay(bone.Position)).Length < 0.0001f, $"Rendered bone {bone.Name} does not match its model.");
            var modelAxes = new[] { model.AxisX, model.AxisY, model.AxisZ };
            var axes = new[] { bone.AxisX, bone.AxisY, bone.AxisZ };
            for (var i = 0; i < axes.Length; i++)
                Require((Model(model.Position + modelAxes[i]) - Overlay(bone.Position + axes[i])).Length < 0.0001f,
                    $"Rendered bone axis {bone.Name}/{i} does not match its model.");
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Capture(BotwActorViewerForm form, string path)
    {
        var view = form.Preview;
        form.Refresh(); Application.DoEvents(); view.MakeCurrent(); view.Refresh(); GL.Finish();
        var bytes = new byte[view.Width * view.Height * 4];
        GL.ReadBuffer(ReadBufferMode.Front);
        GL.ReadPixels(0, 0, view.Width, view.Height, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, bytes);
        Require(bytes.Where((_, i) => i % 4 != 3).Distinct().Count() > 20, "OpenGL actor viewport rendered blank.");
        using var viewport = new Bitmap(view.Width, view.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bits = viewport.LockBits(new Rectangle(0, 0, viewport.Width, viewport.Height), ImageLockMode.WriteOnly, viewport.PixelFormat);
        Marshal.Copy(bytes, 0, bits.Scan0, bytes.Length); viewport.UnlockBits(bits); viewport.RotateFlip(RotateFlipType.RotateNoneFlipY);
        var host = (Form)(form.TopLevelControl ?? form);
        using var screenshot = new Bitmap(host.Width, host.Height);
        host.DrawToBitmap(screenshot, new Rectangle(Point.Empty, host.Size));
        using var graphics = Graphics.FromImage(screenshot);
        var origin = view.PointToScreen(Point.Empty);
        var point = new Point(origin.X - host.Left, origin.Y - host.Top); graphics.DrawImageUnscaled(viewport, point);
        screenshot.Save(path);
    }
}
