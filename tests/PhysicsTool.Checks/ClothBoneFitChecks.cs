using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless;

internal static class ClothBoneFitChecks
{
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); }
    public static void Run(string path, string output)
    {
        Directory.CreateDirectory(output);
        var session = new EditorSession(); session.Load(path);
        for (var cloth = 0; cloth < session.Document.GetClothSummaries().Count; cloth++)
        {
            session.Load(path); session.ClothIndex = cloth;
            var doc = session.Document;
            var before = doc.GetParticleRows(cloth).ToDictionary(p => p.Index);
            session.Edit((d, i) => {
                var bones = d.GetBoneRows(i);
                foreach (var bone in bones) { bone.X *= .7f; bone.Y *= .7f; bone.Z *= .7f; }
                d.UpdateBoneRows(i, bones, false);
            });
            Require(doc.GetParticleRows(cloth).All(p => p.X == before[p.Index].X && p.Y == before[p.Index].Y && p.Z == before[p.Index].Z), "Bone edits moved particles with follow off");
            var editedBones = doc.GetParticlePreview(cloth).Bones.ToDictionary(b => b.Index, b => b.Position);
            var oldLinks = doc.GetParticlePreview(cloth).Links;
            var state = doc.CaptureState();
            var colliderState = doc.GetColliderRows(cloth).Select(c => c.Transform).ToArray();
            var moved = 0; session.Edit((d, i) => moved = d.FitClothToBones(i));
            Require(moved > 0, "Fit did not move particles");
            var fit = doc.CaptureState();
            Require(doc.GetColliderRows(cloth).Select(c => c.Transform).SequenceEqual(colliderState), "Fit changed colliders");
            Require(doc.GetParticleRows(cloth).All(p => p.Fixed == before[p.Index].Fixed && p.Mass == before[p.Index].Mass && p.InverseMass == before[p.Index].InverseMass), "Fit changed masses/fixed flags");
            var fitted = doc.GetParticleRows(cloth).ToDictionary(p => p.Index);
            Require(fitted.Values.Any(p => MathF.Abs(p.Radius - before[p.Index].Radius) > 1e-6f), "Fit did not resize particle radii");
            var newLinks = doc.GetParticlePreview(cloth).Links;
            Require(oldLinks.Count == newLinks.Count, "Fit changed constraint topology");
            for (var linkIndex = 0; linkIndex < oldLinks.Count; linkIndex++)
            {
                var a = oldLinks[linkIndex]; var b = newLinks[linkIndex];
                Vector3 Old(int i) => new(before[i].X, before[i].Y, before[i].Z);
                Vector3 New(int i) => new(fitted[i].X, fitted[i].Y, fitted[i].Z);
                var ratio = Vector3.Distance(New(a.ParticleA), New(a.ParticleB)) / Vector3.Distance(Old(a.ParticleA), Old(a.ParticleB));
                void CheckLength(float? originalLength, float? newLength)
                {
                    if (originalLength.HasValue) Require(newLength.HasValue && MathF.Abs(newLength.Value - originalLength.Value * ratio) < 2e-5f, "Fit did not preserve relative constraint slack");
                }
                CheckLength(a.RestLength, b.RestLength); CheckLength(a.BendMinLength, b.BendMinLength); CheckLength(a.StretchMaxLength, b.StretchMaxLength);
                Require(a.Stiffness == b.Stiffness && a.BendStiffness == b.BendStiffness && a.StretchStiffness == b.StretchStiffness, "Fit changed stiffness");
            }
            CheckOutput(doc, cloth, editedBones);
            Require(doc.FitClothToBones(cloth) == 0 && doc.CaptureState() == fit, "Repeated fit drifted the cloth");
            session.Undo(); Require(doc.CaptureState() == state, "Fit undo did not preserve edited bones and restore particles");
            session.Redo(); Require(doc.CaptureState() == fit, "Fit redo changed the result");
            foreach (var platform in new[] { HkclPlatform.WiiU, HkclPlatform.Switch })
            {
                var file = Path.Combine(output, $"fit-{cloth}-{platform}.hkcl"); doc.SaveHkcl(file, platform);
                var loaded = new HkclService(); loaded.Load(file); CheckOutput(loaded, cloth, editedBones);
            }
            Console.WriteLine($"PASS fit: {doc.GetClothName(cloth)}; {moved} particles, undo/redo, idempotence, both platform round trips.");
            session.Edit((d, i) => {
                var bones = d.GetBoneRows(i);
                foreach (var bone in bones) { bone.X *= .93f; bone.Y *= .85f; bone.Z *= .96f; }
                d.UpdateBoneRows(i, bones, false);
            });
            var targets = doc.GetParticlePreview(cloth).Bones.ToDictionary(b => b.Index, b => b.Position);
            session.Edit((d, i) => d.FitClothToBones(i)); CheckOutput(doc, cloth, targets);
            Require(doc.FitClothToBones(cloth) == 0, "Nonuniform fit was not idempotent");
            Console.WriteLine("PASS: nonuniform bone spacing and surface-bend rebuilding.");
        }
        var blank = HkclService.CreateBlankCloth("No bindings", false); var original = blank.CaptureState();
        try { blank.FitClothToBones(0); throw new Exception("Unbound cloth accepted"); } catch (InvalidOperationException) { }
        Require(blank.CaptureState() == original, "Rejected fit changed document");
        session.Load(path);
        session.Edit((d, i) => { var bones = d.GetBoneRows(i); foreach (var bone in bones) { bone.X = bone.Y = bone.Z = 0; } d.UpdateBoneRows(i, bones, false); });
        var collapsed = session.Document.CaptureState();
        try { session.Document.FitClothToBones(0); throw new Exception("Collapsed bones accepted"); } catch (InvalidOperationException) { }
        Require(session.Document.CaptureState() == collapsed, "Failed fit changed the document");
        var window = new MainWindow(graphics: false); window.Show(); window.LoadPath(path); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FitClothToBones");
        Require(button.IsEnabled, "Fit action unavailable while paused");
        window.Width = 1280; window.Height = 800; Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "fit-cloth-editor.png"));
        var snapshot = window.Session.Document.CaptureState();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Require(window.OwnedWindows.Count == 1, "Fit did not request confirmation");
        window.OwnedWindows.Single().Close(); Dispatcher.UIThread.RunJobs();
        Require(window.Session.Document.CaptureState() == snapshot, "Cancel mutated cloth");
        window.Close();
    }
    private static void CheckOutput(HkclService doc, int cloth, IReadOnlyDictionary<int, Vector3> target)
    {
        var preview = doc.GetParticlePreview(cloth); var solver = new HkclPreviewSimulator(preview);
        var poses = solver.GetBonePoses();
        foreach (var b in preview.BoneBindings)
            Require(Vector3.Distance(poses[b.BoneIndex].Position, target[b.BoneIndex]) < 2e-4f,
                $"Bone {b.BoneIndex} snapped back after rest-shape rebuild ({Vector3.Distance(poses[b.BoneIndex].Position, target[b.BoneIndex])})");
        solver.Step(1f / 60);
        Require(solver.GetPositions().Values.All(p => float.IsFinite(p.LengthSquared())), "Fitted simulation became non-finite");
    }
}
