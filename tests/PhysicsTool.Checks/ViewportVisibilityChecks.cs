using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;

internal static class ViewportVisibilityChecks
{
    internal static void Run(string output)
    {
        var data = new ParticlePreviewData();
        data.Particles.Add(new() { Index = 0, Position = new(-.8f, 0, 0), Fixed = true });
        data.Particles.Add(new() { Index = 1, Position = new(-.4f, .5f, 0) });
        data.Particles.Add(new() { Index = 2, Position = new(-.1f, 0, 0) });
        data.Links.Add(new() { ParticleA = 0, ParticleB = 1 });
        data.Triangles.Add(new() { ParticleA = 0, ParticleB = 1, ParticleC = 2 });
        data.Bones.Add(new() { Index = 0, ParentIndex = -1, Position = new(.2f, 0, 0) });
        data.Bones.Add(new() { Index = 1, ParentIndex = 0, Position = new(.3f, .6f, 0) });
        data.Colliders.Add(new() { Index = 0, Start = new(.7f, 0, 0), End = new(.7f, .5f, 0), Radius = .1f });
        var viewport = new PhysicsViewport { Data = data, CanSelect = true };
        viewport.Measure(new Size(800, 600));
        viewport.Arrange(new Rect(0, 0, 800, 600));
        viewport.Fit();
        var camera = viewport.CameraMatrix;

        byte[] Capture()
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(800, 600));
            bitmap.Render(viewport);
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return stream.ToArray();
        }

        for (var mask = 0; mask < 8; mask++)
        {
            viewport.Data = data;
            viewport.ShowParticles = (mask & 1) != 0;
            viewport.ShowBones = (mask & 2) != 0;
            viewport.ShowColliders = (mask & 4) != 0;
            var actual = Capture();

            // Hiding one category must render exactly as if only its geometry
            // were absent, without affecting the other two categories.
            var visible = new ParticlePreviewData();
            if (viewport.ShowParticles)
            {
                visible.Particles.AddRange(data.Particles);
                visible.Links.AddRange(data.Links);
                visible.Triangles.AddRange(data.Triangles);
            }
            if (viewport.ShowBones) visible.Bones.AddRange(data.Bones);
            if (viewport.ShowColliders) visible.Colliders.AddRange(data.Colliders);
            viewport.Data = visible;
            viewport.ShowParticles = viewport.ShowBones = viewport.ShowColliders = true;
            if (!actual.SequenceEqual(Capture()))
                throw new Exception($"Visibility combination {mask} affected another overlay category.");
        }

        viewport.Data = data;
        viewport.Selected.Add(0);
        viewport.SelectedBones.Add(0);
        viewport.SelectedColliders.Add(0);
        foreach (var kind in new[] { PhysicsSelectionKind.Particles, PhysicsSelectionKind.Bones, PhysicsSelectionKind.Colliders })
        {
            viewport.SelectionKind = kind;
            viewport.ShowParticles = kind != PhysicsSelectionKind.Particles;
            viewport.ShowBones = kind != PhysicsSelectionKind.Bones;
            viewport.ShowColliders = kind != PhysicsSelectionKind.Colliders;
            if (viewport.TransformKey(new KeyEventArgs { Key = Key.G }))
                throw new Exception($"Hidden {kind} can still be transformed in the viewport.");
        }
        if (viewport.CameraMatrix != camera)
            throw new Exception("Visibility changes moved the camera.");

        var window = new MainWindow(graphics: false);
        window.Show();
        try
        {
            window.LoadPath(Path.Combine(output, "manual-0.hkcl"));
            window.ShowEditor();
            Dispatcher.UIThread.RunJobs();
            CheckBox Toggle(string name) => window.GetVisualDescendants().OfType<CheckBox>().Single(control => control.Name == name);
            Toggle("ViewportParticles").IsChecked = false;
            if (window.Viewport.ShowParticles || !window.Viewport.ShowBones || !window.Viewport.ShowColliders)
                throw new Exception("Particles checkbox still hides the other overlays.");
            Toggle("ViewportBones").IsChecked = false;
            if (window.Viewport.ShowBones || !window.Viewport.ShowColliders)
                throw new Exception("Bones checkbox hides colliders.");
            Toggle("ViewportColliders").IsChecked = false;
            Toggle("ViewportParticles").IsChecked = true;
            if (!window.Viewport.ShowParticles || window.Viewport.ShowBones || window.Viewport.ShowColliders)
                throw new Exception("Enabling particles restored other hidden overlays.");
        }
        finally
        {
            window.Session.Clear();
            window.Close();
        }
        Console.WriteLine("PASS: all eight independent particle/bone/collider visibility combinations, UI controls, hidden-item transform guards and camera preservation.");
    }
}
