using HKCLTool;
using PhysicsTool.Core;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhysicsTool.Desktop;

internal static class OotClothChecks
{
    internal static void Ui(string path, string output)
    {
        Directory.CreateDirectory(output);
        var window = new MainWindow(graphics: false) { Width = 1280, Height = 850 };
        window.Show(); window.LoadPath(path); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
        var file = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FileMenu");
        Require(file.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Name == "SavePhysics").IsEnabled, "OoT save disabled");
        Require(file.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Name == "ConvertPhysics").IsEnabled, "OoT conversion disabled");
        Require(window.Viewport.CanSelect, "OoT geometry selection disabled");
        foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => b.Name is
            "FitClothToBones" or "MatchBonesToModel" or "MirrorCloth" or "ResizeBoneBranch" or "ResizeAllCloths"))
            Require(!button.IsEnabled, "Preview edit action enabled: " + button.Name);
        using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "oot-preview.png"));
        var before = window.Session.Document.CaptureState();
        window.ToggleSimulation();
        Require(before == window.Session.Document.CaptureState(), "Preview changed source");
        Require(window.EditorSimulating == window.Session.CanSimulate, "Simulation support gate mismatch");
        window.StopSimulation();
        window.Session.ClothIndex = EditorSession.AllClothsIndex; window.Refresh(); Dispatcher.UIThread.RunJobs();
        Require(!window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ResizeAllCloths").IsEnabled, "All-cloth resize bypass");
        window.Close();
        Console.WriteLine("PASS: OoT preview UI, save and conversion/transform/simulation guards.");
    }

    internal static void Run(string root, string totk, string output)
    {
        int cloths = 0, colliders = 0;
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Phive/Cloth"), "*.bphcl"))
        {
            var session = new EditorSession(); session.Load(path);
            Require(session.Document.IsOot && session.Document.IsPreviewOnly, "OoT identification");
            Require(!session.CanManageCloth && !session.CanRemoveEntry, "Cloth management guards");
            Require(PhysicsConversion.CanConvert(session.Document), "Reverse conversion unavailable");
            bool edited = false;
            try { session.Edit((_, _) => edited = true); } catch (InvalidOperationException) { }
            Require(!edited, "Edit callback was reached");
            var doc = session.Document.CaptureBphclNativeDocument()!;
            Require(doc.Colliders.All(c => c.Shape.TypeName != "<none>"), "Missing collider shape: " + path);
            foreach (var cloth in doc.Cloths)
            {
                session.ClothIndex = cloth.Index;
                var preview = session.Document.GetParticlePreview(cloth.Index);
                Require(preview != null, "Missing preview");
                foreach (var state in cloth.States)
                {
                    var expected = OotClothConversion.BuildStateMasks(cloth, state);
                    for (int mask = 0; mask < 4; mask++)
                    {
                        var offset = doc.GetItem(state.ItemIndex).DataOffset + 48u + (uint)mask * 16;
                        var values = doc.TryGetReferencedItem(offset, out var arrayIndex)
                            ? Enumerable.Range(0, (int)doc.GetItem(arrayIndex).Count).Select(i => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                                doc.Bytes.AsSpan(doc.DataSection!.PayloadOffset + (int)doc.GetItem(arrayIndex).DataOffset + i * 4, 4))).ToArray() : [];
                        Require(expected[mask].SequenceEqual(values), $"Native mask mismatch: {path}/{cloth.Name}/{state.Name}/{mask}");
                    }
                }
                foreach (var sim in cloth.SimCloths)
                {
                    Require(sim.TriangleIndices.All(i => i < sim.Particles.Count), "Triangle outside particle range");
                    Require(float.IsFinite(sim.Runtime.TotalMass) && sim.Runtime.TotalMass >= 0, "Invalid total mass");
                }
            }
            cloths += doc.Cloths.Count; colliders += doc.Colliders.Count;
        }
        Console.WriteLine($"OoT read-only preview: {cloths} cloths, {colliders} colliders.");
        var source = NativeBphclDocument.Open(totk);
        var reference = OotClothConversion.ReadReference(source, root);
        Directory.CreateDirectory(output);
        File.WriteAllLines(Path.Combine(output, "source-types.txt"), Enumerable.Range(1, source.TypeNames.Count - 1).SelectMany(i => NativeBphclTypeTable.DescribeTypeIndex(source, (uint)i)));
        File.WriteAllLines(Path.Combine(output, "target-types.txt"), Enumerable.Range(1, reference.TypeNames.Count - 1).SelectMany(i => NativeBphclTypeTable.DescribeTypeIndex(reference, (uint)i)));
        File.WriteAllLines(Path.Combine(output, "state-masks.txt"), reference.Cloths.Take(4).SelectMany(cloth => cloth.States.SelectMany(state =>
            new[] { 48u, 64u, 80u, 96u }.Select(offset => {
                var item = reference.GetItem(state.ItemIndex);
                var values = reference.TryGetReferencedItem(item.DataOffset + offset, out var arrayIndex)
                    ? Enumerable.Range(0, (int)reference.GetItem(arrayIndex).Count).Select(i => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                        reference.Bytes.AsSpan(reference.DataSection!.PayloadOffset + (int)reference.GetItem(arrayIndex).DataOffset + i * 4, 4))).ToArray() : [];
                return $"{cloth.Name}/{state.Name}, operators={string.Join(',', state.OperatorItemIndices)}; mask@{offset}=[{string.Join(',', values)}]; classes={string.Join(',', cloth.Operators.Select(o => o.ClassName))}";
            }))));
        var result = OotClothConversion.Convert(source, reference);
        var converted = NativeBphclDocument.Parse(result);
        Require(converted.IsOot, "Output SDK");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "experimental-oot.bphcl"), result);
        var service = new HkclService(); service.Load(totk);
        var before = service.CaptureState();
        var exported = Path.Combine(output, "export-check.bphcl");
        OotConversionExport.Export(service, root, exported, totk);
        var exportedBefore = File.ReadAllBytes(exported);
        bool blocked = false;
        try { OotConversionExport.Export(service, Path.Combine(output, "not-a-dump"), exported, totk); }
        catch (DirectoryNotFoundException) { blocked = true; }
        Require(blocked && exportedBefore.SequenceEqual(File.ReadAllBytes(exported)), "Failed export replaced an existing file");
        Require(before == service.CaptureState(), "Conversion changed source");
        var readonlySession = new EditorSession(); readonlySession.Load(exported);
        readonlySession.Save(exported);
        Require(exportedBefore.SequenceEqual(File.ReadAllBytes(exported)), "OoT no-edit save changed bytes");
        blocked = false;
        try { OotConversionExport.Export(service, root, totk, totk); } catch (InvalidOperationException) { blocked = true; }
        Require(blocked, "Source overwrite accepted");
        Console.WriteLine($"Converted {source.Cloths.Count} cloths; {result.Length} bytes.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
