using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Desktop;
using System.Numerics;

internal static class ClothDropdownCameraChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static void Run()
    {
        foreach (var format in new[] { 0, 2 })
        foreach (var withModel in new[] { false, true })
            Check(format, withModel);
        Console.WriteLine("PASS: cloth dropdown preserves camera center, rotation, zoom and projection with/without a model; simulation stops, cloth data changes and explicit framing still works.");
    }

    private static void Check(int format, bool withModel)
    {
        var window = new MainWindow(graphics: false); window.Show();
        try
        {
            window.Session.New("Camera cloth A", format);
            window.Session.Edit((doc, cloth) =>
            {
                doc.AddParticle(cloth);
                var rows = doc.GetParticleRows(cloth).ToArray();
                rows[0].X = -1; rows[0].Y = 1;
                rows[1].X = 1; rows[1].Y = 2;
                doc.UpdateParticleRows(cloth, rows);
            });
            window.Session.DuplicateSelected();
            window.Session.RenameSelected("Camera cloth B");
            window.Session.Edit((doc, cloth) =>
            {
                var rows = doc.GetParticleRows(cloth).ToArray();
                foreach (var row in rows) { row.X = row.X * 5 + 20; row.Y += 8; }
                doc.UpdateParticleRows(cloth, rows);
            });
            window.Session.ClothIndex = 0;
            window.Refresh(true); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
            var view = window.Viewport;
            VisualModelData? model = withModel ? new() { BoundsMinimum = new(-12, -3, -4), BoundsMaximum = new(12, 3, 4) } : null;
            if (model != null) view.SetModel(model);
            var names = new[] { "Camera cloth A", "Camera cloth B" };
            var dropdown = window.GetVisualDescendants().OfType<ComboBox>().Single(c =>
                c.ItemsSource?.Cast<object>().OfType<string>().SequenceEqual(names) == true);
            view.Orbit(.4f, -.2f); view.Pan(75, -45); view.ZoomAt(3, new(120, 160));
            var camera = view.CameraState; var matrix = view.CameraMatrix;
            var anchor = view.Project(new Vector3(1, 2, 3));
            var state = window.Session.Document.CaptureState();

            void CheckSelection(int index)
            {
                dropdown.SelectedIndex = index;
                Dispatcher.UIThread.RunJobs();
                Require(window.Session.ClothIndex == index, "Dropdown did not switch cloth");
                var expected = window.Session.Document.GetParticleRows(index)[0];
                Require(Vector3.Distance(view.Data.Particles[0].Position, new(expected.X, expected.Y, expected.Z)) < .0001f,
                    "Dropdown preserved stale cloth preview data");
                Require(view.CameraState == camera && view.CameraMatrix == matrix && view.Project(new Vector3(1, 2, 3)) == anchor,
                    $"Cloth dropdown reframed the camera (format={format}, model={withModel}, cloth={index})");
                Require(ReferenceEquals(view.Model, model), "Cloth dropdown replaced the displayed model");
                Require(window.Session.Document.CaptureState() == state, "Cloth selection changed authored physics");
            }

            CheckSelection(1); CheckSelection(0); CheckSelection(1);
            window.ToggleSimulation();
            Require(window.EditorSimulating, "Camera fixture did not start simulation");
            CheckSelection(0);
            Require(!window.EditorSimulating && view.Simulated == null, "Cloth dropdown did not stop simulation");
            view.Fit();
            Require(view.CameraMatrix != matrix, "Explicit Frame all no longer reframes the camera");
            view.Pan(35, 20); matrix = view.CameraMatrix;
            window.Refresh(true);
            Require(view.CameraMatrix != matrix, "Explicit fitted refresh no longer frames the camera");
        }
        finally { window.StopSimulation(); window.Close(); }
    }
}
