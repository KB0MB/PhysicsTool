using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using System.Numerics;

internal static class EditorUsabilityChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string output)
    {
        var window = new MainWindow(graphics: false); window.Show();
        window.LoadPath(Path.Combine(output, "manual-0.hkcl"));
        window.Session.DuplicateSelected(); window.Refresh(); Dispatcher.UIThread.RunJobs();
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.ContextMenu != null);
        var first = list.ContainerFromIndex(0)!;
        var point = first.TranslatePoint(new Point(35, first.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Require(ReferenceEquals(first, list.ContainerFromIndex(0)), "Selecting a cloth rebuilt its item container");
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(window.IsEditorVisible, "Two clicks on an unselected cloth did not open the editor");
        window.Session.Undo(); window.Refresh();
        window.Width = 1440; window.Height = 900; Dispatcher.UIThread.RunJobs();
        var view = window.Viewport; view.SetView(CameraView.Front, false); view.Fit();
        view.SelectRectangle(new Rect(0, 0, view.Bounds.Width, view.Bounds.Height), SelectionOperation.Replace);
        var before = window.Session.Document.CaptureState();
        var origin = view.TranslatePoint(default, window)!.Value;
        var mouse = origin + new Point(view.Bounds.Width * .5, view.Bounds.Height * .5);
        window.MouseMove(mouse); view.Focus();
        void Press(Key key) => window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        Press(Key.G); Require(view.TransformActive, "G did not start move");
        Press(Key.X); window.MouseMove(mouse + new Point(60, 30));
        Require(window.Session.Document.CaptureState() == before, "Transform preview mutated the document");
        Require(Math.Abs(view.Data.Particles[1].Position.X - window.Session.Document.GetParticleRows(0)[1].X) > .01, "G produced no preview");
        Press(Key.Escape); Require(!view.TransformActive && window.Session.Document.CaptureState() == before, "Escape did not cancel move");
        window.MouseMove(mouse); Press(Key.G); Press(Key.X); window.MouseMove(mouse + new Point(60, 30)); Press(Key.Enter);
        var moved = window.Session.Document.GetParticleRows(0);
        Require(Math.Abs(moved[0].Y - .5f) < .0001 && moved[0].X > .01f, "Axis-constrained move failed");
        var committed = window.Session.Document.CaptureState();
        view.Pan(25, -30); view.ZoomAt(2, new Point(120, 150));
        var camera = view.CameraState; var cameraMatrix = view.CameraMatrix;
        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null);
        Require(window.Session.Document.CaptureState() == before, "Ctrl+Z did not undo move");
        Require(view.CameraState == camera && view.CameraMatrix == cameraMatrix, "Ctrl+Z moved the camera");
        window.KeyPress(Key.Z, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
        Require(window.Session.Document.CaptureState() == committed, "Ctrl+Shift+Z did not redo move");
        Require(view.CameraState == camera && view.CameraMatrix == cameraMatrix, "Ctrl+Shift+Z moved the camera");
        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null);
        window.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.None, null);
        Require(window.Session.Document.CaptureState() == committed && view.CameraMatrix == cameraMatrix, "Ctrl+Y changed the camera or failed redo");
        window.MouseMove(mouse); Press(Key.G); window.MouseMove(mouse + new Point(30, 0));
        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null);
        Require(!view.TransformActive && window.Session.Document.CaptureState() == before && view.CameraMatrix == cameraMatrix,
            "Undo during a modal transform did not cancel its preview and preserve the view");
        Require(!view.TransformGuideAxes.Any(), "Canceled transform retained guides");
        view.Fit();
        view.SetView(CameraView.Front, false); view.Orbit(.8f, .2f);
        window.MouseMove(mouse); view.Focus();
        var screenStart = view.Project(view.Data.Particles[0].Position);
        var projectedAxis = Vector3.TransformNormal(Vector3.UnitX, view.Rotation);
        var travel = new Point(projectedAxis.X * 60, -projectedAxis.Y * 60);
        Press(Key.G); Press(Key.X); window.MouseMove(mouse + travel);
        var screenEnd = view.Project(view.Data.Particles[0].Position);
        Require(Math.Abs(screenEnd.X - screenStart.X - travel.X) + Math.Abs(screenEnd.Y - screenStart.Y - travel.Y) < .01, "Oblique axis dragging lagged behind the pointer");
        Press(Key.Escape); view.SetView(CameraView.Front, false);
        CheckPlaneTransforms(window, mouse, output);
        foreach (var key in new[] { Key.R, Key.S })
        {
            view.SelectRectangle(new Rect(0, 0, view.Bounds.Width, view.Bounds.Height), SelectionOperation.Replace);
            window.MouseMove(mouse); view.Focus(); Press(key); window.MouseMove(mouse + new Point(35, -20)); Press(Key.Enter);
            Require(window.Session.Document.CaptureState() != before, key + " did not change selection");
            window.Session.Undo(); window.Refresh(); Require(window.Session.Document.CaptureState() == before, key + " undo failed");
        }
        var tabs = window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TabStrip>().Single();
        window.Session.Edit((doc, cloth) => {
            doc.AddBone(cloth); doc.AddBone(cloth);
            var bones = doc.GetBoneRows(cloth).ToArray();
            bones[1].ParentIndex = 0; bones[1].X = -.15f; bones[1].Y = .25f;
            bones[2].ParentIndex = 1; bones[2].X = .2f; bones[2].Y = .3f;
            bones[2].Name = "Long_Bone_Name_With_A_Readable_Tooltip";
            doc.UpdateBoneRows(cloth, bones, false);
        });
        window.Refresh();
        tabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        view.ResetView(); view.SelectRectangle(new Rect(0, 0, view.Bounds.Width, view.Bounds.Height), SelectionOperation.Replace);
        Require(view.SelectedBones.Count == 3, "Bone box selection did not follow active tab");
        Require(window.GetVisualDescendants().OfType<NumericUpDown>().All(n => !n.ShowButtonSpinner), "Numeric arrows still visible");
        var buttons = window.GetVisualDescendants().OfType<Button>();
        Require(!buttons.Any(b => Equals(ToolTip.GetTip(b), "Reset view") || Equals(ToolTip.GetTip(b), "Axis views")), "Redundant camera buttons remain");
        var darkGrid = view.GridLines; var cameraBeforeTheme = view.CameraMatrix;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light; Dispatcher.UIThread.RunJobs();
        Require(window.Background is ISolidColorBrush brush && brush.Color.R > 220, "Light theme retained dark background");
        Require(view.ViewportBackground.R > 220 && view.GridLines[1].Color != darkGrid[1].Color, "Viewport did not follow light theme");
        Require(view.CameraMatrix == cameraBeforeTheme && view.GridLines.Zip(darkGrid).All(pair => pair.First.Start == pair.Second.Start && pair.First.End == pair.Second.End), "Theme changed camera or grid geometry");
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "editor-light.png"));
        Application.Current.RequestedThemeVariant = ThemeVariant.Dark; Dispatcher.UIThread.RunJobs();
        Require(view.ViewportBackground.R < 60 && ReferenceEquals(view.GridLines, darkGrid), "Viewport did not restore dark palette");
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "editor-bones-dark.png"));
        var boneList = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Items.Count == 3);
        boneList.SelectedItems!.Clear(); boneList.SelectedItems.Add(boneList.Items[1]); boneList.SelectedItems.Add(boneList.Items[2]);
        var delete = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Delete"));
        delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var confirm = window.OwnedWindows.Single().GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Confirm"));
        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Require(window.Session.Document.GetBoneRows(0).Count == 1 && window.OwnedWindows.Count == 0, "Delete button did not remove all selected bones");
        window.LoadPath(Path.Combine(output, "manual-0.hkcl"));
        window.Close();
        CheckBoneDeletion(output);
        Console.WriteLine("PASS: real double-click, G/R/S preview/commit/cancel/undo, numeric styling, camera toolbar and theme.");
    }
    private static void CheckPlaneTransforms(MainWindow window, Point mouse, string output)
    {
        var view = window.Viewport;
        view.SetView(CameraView.Front, false); view.Orbit(.7f, .5f);
        Matrix4x4 delta = Matrix4x4.Identity;
        void Capture(Matrix4x4 value) => delta = value;
        view.TransformPreview += Capture;
        void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None) => window.KeyPress(key, modifiers, PhysicalKey.None, null);
        float Component(Vector3 v, Key axis) => axis == Key.X ? v.X : axis == Key.Y ? v.Y : v.Z;
        foreach (var operation in new[] { Key.G, Key.R, Key.S })
        foreach (var axis in new[] { Key.X, Key.Y, Key.Z })
        {
            window.MouseMove(mouse); view.Focus(); Press(operation); Press(axis);
            Require(view.TransformGuideAxes.SequenceEqual(new[] { axis }), "Single-axis guide missing");
            Press(axis, RawInputModifiers.Shift);
            Require(view.TransformGuideAxes.Count() == 2 && !view.TransformGuideAxes.Contains(axis), "Plane guides did not exclude the locked axis");
            // A probe away from the pivot exercises rotation/scale even for one selected particle.
            var original = view.Data.Particles[0].Position + new Vector3(.12f, .34f, .56f);
            var start = view.Project(original);
            window.MouseMove(mouse + new Point(45, -30));
            var transformed = Vector3.Transform(original, delta);
            Require(Math.Abs(Component(transformed - original, axis)) < 1e-4, "Plane transform changed the excluded coordinate");
            Require(Vector3.Distance(transformed, original) > .001, $"{operation}/Shift+{axis} plane transform had no effect");
            if (operation == Key.G)
            {
                var end = view.Project(transformed);
                Require(Math.Abs(end.X - start.X - 45) + Math.Abs(end.Y - start.Y + 30) < .01, "Plane move did not track pointer at oblique view");
                if (axis == Key.Z)
                {
                    Dispatcher.UIThread.RunJobs();
                    using var frame = window.CaptureRenderedFrame()!;
                    frame.Save(Path.Combine(output, "transform-plane-guides.png"));
                }
            }
            Press(axis, RawInputModifiers.Shift);
            Require(!view.TransformGuideAxes.Any(), "Repeated plane shortcut did not clear constraint");
            Press(axis); Require(view.TransformGuideAxes.SequenceEqual(new[] { axis }), "Could not switch back to axis constraint");
            Press(Key.Escape); Require(!view.TransformGuideAxes.Any(), "Cancel left transform guides visible");
        }
        view.SetView(CameraView.Front, false);
        window.MouseMove(mouse); Press(Key.G); Press(Key.X, RawInputModifiers.Shift);
        window.MouseMove(mouse + new Point(50, -35));
        Require(float.IsFinite(delta.M41) && delta.M41 == 0 && Math.Abs(delta.M42) < 10, "Edge-on plane drag was unstable");
        Press(Key.Escape); view.TransformPreview -= Capture;
        Console.WriteLine("PASS: undo/redo camera preservation, transform guides, Shift-axis move/rotate/scale, pointer tracking and edge-on fallback.");
    }
    private static void CheckBoneDeletion(string output)
    {
        var session = new EditorSession(); session.New("Bones", 0);
        session.Edit((doc, cloth) => {
            doc.AddBone(cloth); doc.AddBone(cloth); doc.AddBone(cloth);
            var rows = doc.GetBoneRows(cloth).ToArray();
            rows[1].ParentIndex = 0; rows[1].X = 1;
            rows[2].ParentIndex = 1; rows[2].Y = 2;
            rows[3].ParentIndex = 2; rows[3].Z = 3;
            doc.UpdateBoneRows(cloth, rows, false);
        });
        var before = session.Document.CaptureState();
        var world = BoneEditGeometry.WorldTransforms(session.Document.GetBoneRows(0));
        session.Edit((doc, cloth) => doc.DeleteBones(cloth, new[] { 1, 2 }));
        var rowsAfter = session.Document.GetBoneRows(0);
        Require(rowsAfter.Count == 2 && rowsAfter[1].ParentIndex == 0, "Multi-bone deletion did not reparent descendants");
        var after = BoneEditGeometry.WorldTransforms(rowsAfter);
        Require(Vector3.Distance(world[3].Translation, after[1].Translation) < .0001f, "Delete changed surviving child's world position");
        session.Save(Path.Combine(output, "bone-delete.hkcl"));
        var reload = new EditorSession(); reload.Load(Path.Combine(output, "bone-delete.hkcl"));
        Require(reload.Document.GetBoneRows(0).Count == 2, "Deleted bones reappeared on reload");
        session.Undo(); Require(session.Document.CaptureState() == before, "Bone deletion undo failed");
        session.Edit((doc, cloth) => SelectionTransforms.Apply(doc, cloth, PhysicsSelectionKind.Bones, new HashSet<int> { 1, 2 }, Matrix4x4.CreateTranslation(2, 0, 0), false));
        var moved = BoneEditGeometry.WorldTransforms(session.Document.GetBoneRows(0));
        Require(Vector3.Distance(moved[2].Translation - world[2].Translation, new Vector3(2, 0, 0)) < .0001f, "Selected child was transformed twice");
        Console.WriteLine("PASS: bone multi-delete/reparent/save/undo and parent-child transform inheritance.");
    }
}
