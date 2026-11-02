using System.Numerics;
using HKCLTool;
using PhysicsTool.Core;
using Newtonsoft.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhysicsTool.Desktop;

internal static class BphclRestShapeChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string OtherCloth(HkclService doc, int cloth) => JsonConvert.SerializeObject(new {
        Bones = doc.GetBoneRows(cloth), Particles = doc.GetParticleRows(cloth), Skin = doc.GetSkinOperatorData(cloth),
        Runtime = doc.GetClothRuntimeData(cloth), Constraints = doc.GetConstraintSetSummaries(cloth), Colliders = doc.GetColliderRows(cloth)
    });
    private static ParticlePreviewData Preview(HkclService doc, int cloth)
    {
        var preview = doc.GetParticlePreview(cloth);
        foreach (var binding in preview.BoneBindings) binding.UseNativeSimpleDeformer = true;
        return preview;
    }
    private static void CheckOutput(HkclService doc, int cloth, IReadOnlyDictionary<int, Vector3> target)
    {
        var preview = Preview(doc, cloth); var solver = new HkclPreviewSimulator(preview);
        var poses = solver.GetBonePoses();
        foreach (var binding in preview.BoneBindings)
            Check(Vector3.Distance(poses[binding.BoneIndex].Position, target[binding.BoneIndex]) < 2e-4f,
                $"Fitted bone {binding.BoneIndex} snapped back");
    }
    internal static void Run(string path, string output)
    {
        Directory.CreateDirectory(output);
        var originalBytes = File.ReadAllBytes(path);
        var session = new EditorSession(); session.Load(path);
        var count = session.Document.GetClothSummaries().Count; var tested = 0;
        for (var cloth = 0; cloth < count; cloth++)
        {
            session.Load(path); session.ClothIndex = cloth; var doc = session.Document;
            try { doc.ValidateClothBoneFit(cloth); }
            catch (InvalidOperationException e) { Console.WriteLine($"SKIP {doc.GetClothName(cloth)}: {e.Message}"); continue; }
            var before = doc.GetParticleRows(cloth).ToDictionary(p => p.Index);
            session.Edit((d, i) => {
                var bones = d.GetBoneRows(i);
                foreach (var b in bones) { b.X *= .8f; b.Y *= .8f; b.Z *= .8f; }
                d.UpdateBoneRows(i, bones, false);
            });
            Check(doc.GetParticleRows(cloth).All(p => p.X == before[p.Index].X && p.Y == before[p.Index].Y && p.Z == before[p.Index].Z), "Follow-off moved particles");
            var targets = Preview(doc, cloth).Bones.ToDictionary(b => b.Index, b => b.Position);
            var state = doc.CaptureState(); var colliders = JsonConvert.SerializeObject(doc.GetColliderRows(cloth));
            var oldLinks = Preview(doc, cloth).Links;
            var others = Enumerable.Range(0, count).Where(i => i != cloth).ToDictionary(i => i, i => OtherCloth(doc, i));
            var moved = 0; session.Edit((d, i) => moved = d.FitClothToBones(i));
            Check(moved > 0, "Native fit moved no particles");
            var fitted = doc.CaptureState();
            CheckOutput(doc, cloth, targets);
            Check(doc.GetParticleRows(cloth).All(p => p.Mass == before[p.Index].Mass && p.InverseMass == before[p.Index].InverseMass &&
                p.Fixed == before[p.Index].Fixed && p.CollisionMask == before[p.Index].CollisionMask), "Fit changed particle physics properties");
            Check(JsonConvert.SerializeObject(doc.GetColliderRows(cloth)) == colliders, "Fit changed colliders");
            var newLinks = Preview(doc, cloth).Links;
            Check(oldLinks.Count == newLinks.Count, "Fit changed constraint topology");
            var fittedRows = doc.GetParticleRows(cloth).ToDictionary(p => p.Index);
            foreach (var (a, b) in oldLinks.Zip(newLinks))
            {
                Vector3 Position(IReadOnlyDictionary<int, ParticleEditRow> rows, int i) => new(rows[i].X, rows[i].Y, rows[i].Z);
                var ratio = Vector3.Distance(Position(fittedRows, a.ParticleA), Position(fittedRows, a.ParticleB)) /
                    Vector3.Distance(Position(before, a.ParticleA), Position(before, a.ParticleB));
                void Length(float? old, float? current)
                { if (old.HasValue) Check(current.HasValue && MathF.Abs(current.Value - old.Value * ratio) < 2e-5f, "Fit did not preserve constraint slack"); }
                Length(a.RestLength, b.RestLength); Length(a.BendMinLength, b.BendMinLength);
                Length(a.StretchMaxLength, b.StretchMaxLength); Length(a.CompressionLength, b.CompressionLength);
                Check(a.Stiffness == b.Stiffness && a.BendStiffness == b.BendStiffness && a.StretchStiffness == b.StretchStiffness, "Fit changed stiffness");
            }
            Check(others.All(pair => OtherCloth(doc, pair.Key) == pair.Value), "Fit affected another cloth");
            var newParticles = doc.GetParticleRows(cloth).ToDictionary(p => p.Index, p => new Vector3(p.X, p.Y, p.Z));
            foreach (var attachment in doc.GetParticleBoneAttachments(cloth))
            {
                var weights = attachment.BoneWeights;
                if (weights.Count == 0 || weights.Any(w => !w.ReferencePosition.HasValue || w.Weight <= 0)) continue;
                var reference = weights.Aggregate(Vector3.Zero, (p, w) => p + w.ReferencePosition!.Value * w.Weight) / weights.Sum(w => w.Weight);
                Check(Vector3.Distance(reference, newParticles[attachment.ParticleIndex]) < .002f,
                    $"Fitted animation input still references the old position: particle {attachment.ParticleIndex}");
            }
            var fittedPositions = doc.GetParticleRows(cloth).ToDictionary(p => p.Index, p => new Vector3(p.X, p.Y, p.Z));
            var repeated = doc.FitClothToBones(cloth);
            var drift = doc.GetParticleRows(cloth).Max(p => Vector3.Distance(fittedPositions[p.Index], new(p.X, p.Y, p.Z)));
            Check(repeated == 0 && doc.CaptureState() == fitted, $"Repeated fit drifted: {doc.GetClothName(cloth)}, {repeated} particles, maximum {drift}");
            session.Undo(); Check(doc.CaptureState() == state, "Native fit undo failed");
            session.Redo(); Check(doc.CaptureState() == fitted, "Native fit redo failed");
            var save = Path.Combine(output, $"fit-{cloth}.bphcl"); doc.SaveHkcl(save, HkclPlatform.Switch);
            var reopened = new HkclService(); reopened.Load(save); CheckOutput(reopened, cloth, targets);
            if (reopened.GetTotkSimulationSupport(cloth).Supported)
            {
                var solver = new HkclPreviewSimulator(reopened.GetTotkSimulationPreview(cloth));
                for (var frame = 0; frame < 10; frame++) solver.Step(1f / 60);
                Check(solver.GetPositions().Values.All(p => float.IsFinite(p.LengthSquared())), "Fitted TotK preview became non-finite");
            }
            Console.WriteLine($"PASS {doc.GetClothName(cloth)}: {moved} particles; native round trip, output bindings, undo/redo, idempotence and cloth isolation");
            session.Edit((d, i) => {
                var bones = d.GetBoneRows(i);
                foreach (var b in bones) { b.X *= .92f; b.Y *= .87f; b.Z *= .95f; }
                d.UpdateBoneRows(i, bones, false);
            });
            var nonuniform = Preview(doc, cloth).Bones.ToDictionary(b => b.Index, b => b.Position);
            session.Edit((d, i) => d.FitClothToBones(i)); CheckOutput(doc, cloth, nonuniform);
            Check(doc.FitClothToBones(cloth) == 0, "Repeated nonuniform fit drifted");
            var worlds = BoneEditGeometry.WorldTransforms(doc.GetBoneRows(cloth));
            var root = doc.GetBoneRows(cloth).First(b => b.ParentIndex < 0).Index;
            var offset = new Vector3(.015f, .025f, -.01f);
            var matched = worlds[root] * Matrix4x4.CreateTranslation(offset);
            session.Edit((d, i) => d.MatchClothBonesToModel(i, new Dictionary<int, Matrix4x4> { [root] = matched }));
            CheckOutput(doc, cloth, nonuniform.ToDictionary(p => p.Key, p => p.Value + offset));
            var scaleBefore = doc.GetBoneRows(cloth).ToDictionary(b => b.Index, b => new Vector3(b.ScaleX, b.ScaleY, b.ScaleZ));
            var source = doc.CaptureState();
            var branch = Preview(doc, cloth).BoneBindings.Select(b => b.BoneIndex).Last();
            // Branches containing only an output leaf avoid changing shared body colliders.
            using (var resize = new ClothBranchResizePreview(session, branch))
            {
                resize.Sample(new Vector3(.7f, .8f, .9f), false);
                Check(doc.GetBoneRows(cloth).All(b => new Vector3(b.ScaleX, b.ScaleY, b.ScaleZ) == scaleBefore[b.Index]), "Baked resize changed bone scales");
            }
            Check(doc.CaptureState() == source, "Cancel native resize failed");
            var invalid = worlds.ToDictionary(p => p.Key, _ => new Matrix4x4());
            try { doc.MatchClothBonesToModel(cloth, invalid); throw new Exception("Singular model pose accepted"); }
            catch (InvalidOperationException) { }
            Check(doc.CaptureState() == source, "Failed match changed native data");
            Console.WriteLine("PASS nonuniform fitting, matching, baked branch resize/cancel and invalid-transform rollback");
            tested++;
        }
        Check(tested > 0, "No native cloths were tested");
        Check(File.ReadAllBytes(path).SequenceEqual(originalBytes), "Source file changed");
    }

    internal static void Ui(string path, string output)
    {
        var owner = new MainWindow(graphics: false); owner.Show(); owner.LoadPath(path); owner.ShowEditor(); Dispatcher.UIThread.RunJobs();
        var fit = owner.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FitClothToBones");
        Check(fit.IsEnabled, "BPHCL Fit button disabled");
        fit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Check(owner.OwnedWindows.Count == 1, "Native fit did not open confirmation");
        owner.OwnedWindows.Single().Close(); Dispatcher.UIThread.RunJobs();
        owner.GetVisualDescendants().OfType<TabStrip>().Single(t => t.ItemCount == 3).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var list = owner.GetVisualDescendants().OfType<ListBox>().Single(l => l.SelectionMode == SelectionMode.Multiple);
        list.SelectedIndex = Preview(owner.Session.Document, 0).BoneBindings.Last().BoneIndex;
        var resize = owner.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ResizeBoneBranch");
        Check(resize.IsEnabled, "BPHCL Resize button disabled");
        var original = owner.Session.Document.CaptureState();
        resize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var popup = owner.OwnedWindows.OfType<BoneBranchResizeWindow>().Single();
        foreach (var value in popup.GetVisualDescendants().OfType<NumericUpDown>()) value.Value = .8m;
        Check(popup.PreviewNow(), "BPHCL resize popup could not preview");
        using (var image = popup.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "bphcl-resize.png"));
        popup.Close(); Dispatcher.UIThread.RunJobs();
        Check(owner.Session.Document.CaptureState() == original, "BPHCL popup cancel changed cloth");
        using (var image = owner.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "bphcl-editor.png"));
        owner.Close();
        Console.WriteLine("PASS BPHCL Fit confirmation and XYZ resize popup/preview/cancel");
    }
}
