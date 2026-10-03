using System.Drawing;
using System.Numerics;
using System.Reflection;

namespace HKCLTool;

internal static class AnimationPreviewChecks
{
    public static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        ApplicationConfiguration.Initialize();
        AppLocalization.Initialize();
        CheckPlaybackRate();
        CheckBoneOverlay();
        CheckAnimatedPhysicsBinding();
        ClothSimulationChecks.Run(outputDirectory);
        SupportBoneSimulationChecks.Run(outputDirectory);
        foreach (var scale in new[] { 1f, 1.5f, 2f })
            CheckTransport(outputDirectory, scale);
        File.WriteAllText(Path.Combine(outputDirectory, "result.txt"),
            "PASS: playback speed, bone positions and axes, namespace matching, animated particle/collider binding, solver input direction, and transport layout at 100/150/200 percent.");
    }

    private static void CheckPlaybackRate()
    {
        var preference = typeof(AppLocalization).GetProperty(nameof(AppLocalization.AnimationFramesPerSecond))!;
        var original = AppLocalization.AnimationFramesPerSecond;
        try
        {
            foreach (var sourceRate in new[] { 24.0, 30.0, 60.0 })
            {
                preference.SetValue(null, 30);
                Require(AppLocalization.GetAnimationPlaybackFrameRate(sourceRate) == sourceRate / 2,
                    "30 FPS must play at half the source rate.");
                preference.SetValue(null, 60);
                Require(AppLocalization.GetAnimationPlaybackFrameRate(sourceRate) == sourceRate,
                    "60 FPS must preserve the source rate.");
            }
        }
        finally
        {
            preference.SetValue(null, original);
        }
    }

    private static void CheckBoneOverlay()
    {
        using var viewport = new ParticlePreviewControl();
        viewport.SetVisualModel(new VisualModelData { SourcePath = "legacy.fbx" }, false);
        var preview = new ParticlePreviewData();
        preview.Bones.Add(new BonePreviewPoint { Index = 31, Name = "Head", ParentIndex = -1 });
        preview.Bones.Add(new BonePreviewPoint { Index = 37, Name = "Hair_Help_1", ParentIndex = 31 });
        viewport.SetData(preview, resetCamera: false);
        foreach (var mirror in new[] { false, true })
        {
            var settings = new VisualModelDisplaySettings
            {
                MirrorX = mirror,
                UniformScale = 1.7f,
                RotationDegrees = new Vector3(10, 35, -15),
                Offset = new Vector3(2, -1, 3)
            };
            viewport.SetVisualModelDisplaySettings(settings);
            foreach (var step in new[] { 0f, 0.7f, -0.4f })
            {
                var bones = new[]
                {
                    new VisualModelBone
                    {
                        Index = 0, Name = "Rig|Zelda:Head", Position = new Vector3(1 + step, 4, -2),
                        Rotation = Quaternion.CreateFromYawPitchRoll(step, 0.4f, -0.2f)
                    },
                    new VisualModelBone
                    {
                        Index = 1, ParentIndex = 0, Name = "Zelda:Hair_Help_1",
                        Position = new Vector3(-2, 3 + step, 1),
                        Rotation = Quaternion.CreateFromYawPitchRoll(0.3f, step, 0.6f)
                    }
                };
                viewport.ApplyVisualSkeletonPreview(bones);
                var presentation = Matrix4x4.CreateScale(mirror ? 1.7f : -1.7f, 1.7f, 1.7f) *
                    Matrix4x4.CreateRotationX(10 * MathF.PI / 180) *
                    Matrix4x4.CreateRotationY(35 * MathF.PI / 180) *
                    Matrix4x4.CreateRotationZ(-15 * MathF.PI / 180) *
                    Matrix4x4.CreateTranslation(settings.Offset);
                for (var index = 0; index < bones.Length; index++)
                {
                    Require(Vector3.Distance(ToDisplay(preview.Bones[index].Position),
                        Vector3.Transform(bones[index].Position, presentation)) < 0.0001f,
                        "Overlay position must match the rendered model.");
                    var expectedAxis = Vector3.Normalize(Vector3.TransformNormal(
                        Vector3.Transform(Vector3.UnitX, bones[index].Rotation), presentation));
                    Require(Vector3.Distance(ToDisplay(preview.Bones[index].AxisX), expectedAxis) < 0.0001f,
                        "Overlay rotation must match the rendered model.");
                }
            }
        }
    }

    private static Vector3 ToDisplay(Vector3 point) => new(-point.X, point.Y, point.Z);

    private static void CheckAnimatedPhysicsBinding()
    {
        using var main = new MainForm();
        using var viewport = new ParticlePreviewControl();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var rest = new VisualModelData();
        rest.Bones.Add(new() { Index = 0, Name = "Rig:Head", Position = new(0.35f, 1.6f, -0.2f),
            Rotation = Quaternion.CreateFromYawPitchRoll(0.2f, -0.1f, 0.3f) });
        rest.Bones.Add(new() { Index = 1, ParentIndex = 0, Name = "Hair", Position = new(0.5f, 1.2f, -0.4f) });
        typeof(MainForm).GetField("_visualRestModel", flags)!.SetValue(main, rest);
        var preview = new ParticlePreviewData();
        foreach (var bone in rest.Bones)
            preview.Bones.Add(new() { Index = bone.Index, ParentIndex = bone.ParentIndex, Name = bone.Name.Split(':').Last(),
                Position = bone.Position, AxisX = bone.AxisX, AxisY = bone.AxisY, AxisZ = bone.AxisZ });
        var points = new[] { new Vector3(0.4f, 1.55f, -0.3f), new Vector3(0.6f, 1.1f, -0.5f), new Vector3(-0.3f, 1.3f, -0.4f) };
        for (var i = 0; i < points.Length; i++)
        {
            preview.Particles.Add(new() { Index = i, Position = points[i], Fixed = i == 0, InverseMass = i == 0 ? 0 : 1 });
            if (i == 2) continue; // Exercise the unattached dynamic-particle fallback too.
            var attachment = new ParticlePreviewAttachment { ParticleIndex = i };
            attachment.BoneWeights.Add(new() { BoneIndex = i, Weight = 1 });
            preview.ParticleAttachments.Add(attachment);
        }
        var start = new Vector3(0.3f, 1.4f, -0.1f); var end = new Vector3(0.45f, 1.8f, -0.1f);
        preview.Colliders.Add(new() { Index = 0, BoneIndex = 0, Start = start, End = end, Radius = 0.1f });
        viewport.SetData(preview, resetCamera: false);
        var solver = new HkclPreviewSimulator(preview);
        foreach (var angle in new[] { 0.7f, -0.4f, 0f })
        {
            var delta = Matrix4x4.CreateRotationY(angle) * Matrix4x4.CreateRotationZ(angle * 0.4f) * Matrix4x4.CreateTranslation(angle, 0.2f, -angle * 0.5f);
            var animated = rest.Bones.Select(b =>
            {
                var world = BotwActorScene.Matrix(b) * delta;
                if (b.Index == 1) world = Matrix4x4.CreateRotationX(0.8f) * world;
                Matrix4x4.Decompose(world, out var scale, out var rotation, out var position);
                return new VisualModelBone { Index = b.Index, ParentIndex = b.ParentIndex, Name = b.Name, Position = position, Rotation = rotation, Scale = scale };
            }).ToArray();
            var deltas = (IReadOnlyDictionary<int, Matrix4x4>)typeof(MainForm).GetMethod("BuildPhysicsInputBoneDeltas", flags)!
                .Invoke(main, new object[] { preview, animated })!;
            viewport.UpdateAnimatedPhysicsPose(deltas, new HashSet<int>(), new HashSet<int> { 1 });
            void Match(Vector3 actual, Vector3 expected, string description) => Require(Vector3.Distance(actual, expected) < 0.0001f,
                $"Animated physics {description} did not follow the model's rotation and translation.");
            foreach (var bone in preview.Bones) Match(bone.Position, animated[bone.Index].Position, "bone");
            for (var i = 0; i < points.Length; i++) Match(preview.Particles[i].Position, Vector3.Transform(points[i], delta), "particle");
            Match(preview.Colliders[0].Start, Vector3.Transform(start, delta), "collider start");
            Match(preview.Colliders[0].End, Vector3.Transform(end, delta), "collider end");
            solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = deltas[0], [1] = deltas[0] });
            solver.Reset();
            Match(solver.GetPositions()[0], Vector3.Transform(points[0], delta), "solver fixed particle");
            Match(solver.GetColliderPoses()[0].Start, Vector3.Transform(start, delta), "solver collider");
        }
    }

    private static void CheckTransport(string outputDirectory, float scale)
    {
        using var main = new MainForm();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var transport = (TableLayoutPanel)typeof(MainForm).GetField("_helperVisualAnimationTransport", flags)!.GetValue(main)!;
        var timeline = (AnimationTimelineControl)typeof(MainForm).GetField("_helperVisualAnimationTimeline", flags)!.GetValue(main)!;
        var viewportLayout = (TableLayoutPanel)transport.Parent!;
        using var host = new Form
        {
            ClientSize = new Size(640, 190), AutoScaleMode = AutoScaleMode.None,
            BackColor = Color.FromArgb(54, 54, 54), ShowInTaskbar = false
        };
        host.Controls.Add(viewportLayout);
        transport.Visible = true;
        foreach (Control control in transport.Controls)
        {
            control.Visible = true;
            control.Enabled = true;
        }
        timeline.Maximum = 120;
        timeline.Value = 45;
        var label = (Label)typeof(MainForm).GetField("_helperVisualAnimationFrameLabel", flags)!.GetValue(main)!;
        label.Text = "Frame 45 / 120";
        host.Scale(new SizeF(scale, scale));
        host.Show();
        Application.DoEvents();
        foreach (Control control in transport.Controls)
        {
            Require(transport.ClientRectangle.Contains(control.Bounds),
                $"Transport control is clipped at scale {scale}.");
            if (control is Button button)
            {
                var textSize = TextRenderer.MeasureText(button.Text, button.Font,
                    Size.Empty, TextFormatFlags.SingleLine);
                Require(button.ClientSize.Height >= textSize.Height &&
                        button.ClientSize.Width >= textSize.Width,
                    $"Transport icon is clipped at scale {scale}.");
            }
        }
        Require(timeline.Width >= 40 * scale, "Timeline must retain usable space.");
        using var bitmap = new Bitmap(host.Width, host.Height);
        host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.Size));
        bitmap.Save(Path.Combine(outputDirectory, $"transport-{scale * 100:0}.png"));
        host.Close();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
