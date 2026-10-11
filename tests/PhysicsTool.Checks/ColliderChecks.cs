using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using HKCLTool;
using PhysicsTool.Desktop;
using System.Numerics;

internal static class ColliderChecks
{
    public static void Run(string output)
    {
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        Vector3[] Vertices(ColliderPreviewShape shape) => ColliderGeometry.Create(shape).SelectMany(l => new[] { l.Start, l.End }).ToArray();
        var start = new Vector3(2, 3, 4);
        var direction = Vector3.Normalize(new Vector3(1, 2, 3));
        var capsule = new ColliderPreviewShape { Start = start, End = start + direction * 2, Radius = .4f, Kind = ColliderPreviewKind.Capsule };
        var lines = ColliderGeometry.Create(capsule);
        Require(lines.Count == 196, "Capsule sides or end caps missing");
        var projections = Vertices(capsule).Select(p => Vector3.Dot(p - start, direction)).ToArray();
        Require(Math.Abs(projections.Min() + .4f) < .00001f && Math.Abs(projections.Max() - 2.4f) < .00001f, "Rotated capsule caps have wrong bounds");
        foreach (var p in Vertices(capsule))
        {
            var t = Math.Clamp(Vector3.Dot(p - start, direction), 0, 2);
            Require(Math.Abs(Vector3.Distance(p, start + direction * t) - .4f) < .00001f, "Capsule wire vertex is not on its collision surface");
        }
        Require(ColliderGeometry.Surface(capsule).Count > 100, "Capsule surface is missing");
        foreach (var triangle in ColliderGeometry.Surface(capsule))
            foreach (var p in new[] { triangle.A, triangle.B, triangle.C })
            {
                var t = Math.Clamp(Vector3.Dot(p - start, direction), 0, 2);
                Require(Math.Abs(Vector3.Distance(p, start + direction * t) - .4f) < .00001f, "Collider surface differs from wire volume");
            }
        capsule.Kind = ColliderPreviewKind.TaperedCapsule; capsule.EndRadius = .1f;
        lines = ColliderGeometry.Create(capsule);
        foreach (var rail in lines.Skip(64).Take(4))
        {
            Require(Math.Abs(Vector3.Distance(rail.Start, capsule.Start) - .4f) < .00001f, "Tapered start radius ignored");
            Require(Math.Abs(Vector3.Distance(rail.End, capsule.End) - .1f) < .00001f, "Tapered end radius ignored");
        }
        capsule.End = capsule.Start;
        Require(ColliderGeometry.Create(capsule).Count == 96, "Degenerate capsule must be one sphere");
        capsule.Kind = ColliderPreviewKind.Sphere; capsule.End = new(100);
        Require(Vertices(capsule).All(p => Math.Abs(Vector3.Distance(p, start) - .4f) < .00001f), "Sphere used irrelevant end point");
        capsule.Kind = ColliderPreviewKind.Plane; capsule.PlaneNormal = Vector3.Zero;
        Require(Vertices(capsule).All(p => float.IsFinite(p.LengthSquared())), "Zero plane normal produced invalid geometry");
        capsule.Kind = ColliderPreviewKind.Point;
        Require(ColliderGeometry.Create(capsule).Count == 3, "Point guide disappeared");

        var view = new PhysicsViewport { ShowBones = false };
        view.Data.Colliders.Add(new() { Start = new(-1.4f, .5f, 0), Radius = .3f, Kind = ColliderPreviewKind.Sphere });
        view.Data.Colliders.Add(new() { Start = new(-.5f, .4f, 0), End = new(-.3f, 1.3f, .2f), Radius = .2f });
        view.Data.Colliders.Add(new() { Start = new(.4f, .4f, 0), End = new(.8f, 1.3f, .2f), Radius = .3f, EndRadius = .1f, Kind = ColliderPreviewKind.TaperedCapsule });
        view.Data.Colliders.Add(new() { Start = new(1.7f, .5f, 0), PlaneNormal = Vector3.UnitY, Kind = ColliderPreviewKind.Plane });
        var window = new Window { Width = 980, Height = 640, Content = view };
        window.Show(); Dispatcher.UIThread.RunJobs(); view.ResetView(); Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "collider-guides.png"));
        var worldGeometry = view.Data.Colliders.SelectMany(ColliderGeometry.Create).ToArray();
        view.ZoomAt(4, new(490, 320)); view.Pan(20, 10); view.Fit();
        Require(worldGeometry.SequenceEqual(view.Data.Colliders.SelectMany(ColliderGeometry.Create)), "Camera changed collider geometry");
        window.Close();
        Console.WriteLine("PASS: collider radii, rotated capsule surfaces, end caps, degenerates, plane and point guides.");
    }
}
