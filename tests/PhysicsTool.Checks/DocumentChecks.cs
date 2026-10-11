using HKCLTool;
using PhysicsTool.Core;

internal static class DocumentChecks
{
    public static void CheckHelper(string file, string output)
    {
        var current = new EditorSession(); current.Load(file);
        var count = current.Document.GetClothSummaries().Count;
        if (count == 0) return;
        var before = current.Document.CaptureState();
        current.RemoveSelected();
        if (current.Document.GetClothSummaries().Count != count - 1) throw new Exception("Helper group removal failed");
        current.Undo();
        if (current.Document.CaptureState() != before) throw new Exception("Helper group undo failed");
        current.Redo();
        var donor = new EditorSession(); donor.Load(file); current.MergeFrom(donor);
        if (current.Document.GetClothSummaries().Count != count || donor.IsDirty) throw new Exception("Helper group merge failed");
        var path = Path.Combine(output, "managed-" + Path.GetFileName(file)); current.Save(path);
        var reload = new EditorSession(); reload.Load(path);
        if (reload.Document.GetClothSummaries().Count != count) throw new Exception("Helper group save failed");
    }
    public static void Run(string output)
    {
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        for (var format = 0; format < 3; format++)
        {
            var extension = format == 2 ? ".bphcl" : ".hkcl";
            var path = Path.Combine(output, $"manual-{format}" + extension);
            var current = new EditorSession(); current.Load(path);
            var original = current.Document.CaptureState();
            current.RenameSelected("RenamedCloth");
            Require(current.Document.GetClothName(0).Contains("RenamedCloth"), "Rename failed");
            var renamed = current.Document.CaptureState();
            try { current.RenameSelected(" "); throw new Exception("Blank name accepted"); } catch (ArgumentException) { }
            Require(current.Document.CaptureState() == renamed && current.ClothIndex == 0, "Rejected rename changed data");
            current.Undo(); Require(current.Document.CaptureState() == original && !current.IsDirty, "Rename undo failed");
            current.DuplicateSelected();
            Require(current.Document.GetClothSummaries().Count == 2 && current.ClothIndex == 1, "Duplicate did not select new cloth");
            current.RenameSelected("IndependentCopy");
            Require(current.Document.GetClothName(0) != current.Document.GetClothName(1), "Duplicate aliases its source");
            current.RemoveSelected(); Require(current.Document.GetClothSummaries().Count == 1 && current.ClothIndex == 0, "Remove failed");
            current.Undo(); Require(current.Document.GetClothSummaries().Count == 2 && current.ClothIndex == 1, "Remove undo lost selection");
            current.Redo(); Require(current.Document.GetClothSummaries().Count == 1, "Remove redo failed");
            var donor = new EditorSession(); donor.Load(path);
            var donorBefore = donor.Document.CaptureState();
            if (format == 2) Require(!current.PreviewMerge(donor).Allowed, "BPHCL duplicate-name guard missing");
            current.RenameSelected("TargetCloth");
            var preview = current.PreviewMerge(donor);
            Require(preview.Allowed, $"Matching-format merge blocked ({format}): {preview.Description}");
            current.MergeFrom(donor);
            Require(current.Document.GetClothSummaries().Count == 2 && current.ClothIndex == 1, "Merge failed");
            Require(donor.Document.CaptureState() == donorBefore && !donor.IsDirty, "Merge changed donor");
            current.Undo(); Require(current.Document.GetClothSummaries().Count == 1, "Merge undo failed"); current.Redo();
            var state = current.Document.CaptureState(); var dirty = current.IsDirty;
            current.ExportCopy(Path.Combine(output, $"export-{format}" + extension));
            current.ExportCopy(Path.Combine(output, $"export-{format}.json"));
            Require(current.Document.CaptureState() == state && current.Path == path && current.IsDirty == dirty, "Export changed source/session");
            var saved = Path.Combine(output, $"managed-{format}" + extension); current.Save(saved);
            var reload = new EditorSession(); reload.Load(saved);
            Require(reload.Document.GetClothSummaries().Count == 2, "Managed cloths did not save");
        }
        var a = new EditorSession(); a.Load(Path.Combine(output, "manual-0.hkcl"));
        var b = new EditorSession(); b.Load(Path.Combine(output, "manual-1.hkcl"));
        a.RenameSelected("UnsavedCurrent"); var aState = a.Document.CaptureState(); var aPath = a.Path;
        a.SwapWith(b);
        Require(!a.IsDirty && b.IsDirty && b.Path == aPath && b.Document.CaptureState() == aState, "Swap lost dirty document");
        Require(a.Platform == HkclPlatform.Switch && b.Platform == HkclPlatform.WiiU, "Swap lost platform");
        b.Undo(); Require(!b.IsDirty, "Swap lost undo history"); b.Redo(); a.SwapWith(b);
        Require(a.IsDirty && a.Document.CaptureState() == aState, "Double swap lost edits");
        var bphcl = new EditorSession(); bphcl.Load(Path.Combine(output, "manual-2.bphcl"));
        CrossMerge(bphcl, a, Path.Combine(output, "cross-merged.bphcl"));
        var hkcl = new EditorSession(); hkcl.Load(Path.Combine(output, "manual-1.hkcl")); hkcl.RenameSelected("SwitchTarget");
        CrossMerge(hkcl, bphcl, Path.Combine(output, "cross-merged.hkcl"));
        foreach (var fixture in new[] { "fixture-Armor_1106_RaulSkin_Upper.bphcl", "fixture-Npc_Zelda_AncientHyrule.hkcl" })
        {
            if (!File.Exists(Path.Combine(output, fixture))) continue;
            var source = new EditorSession(); source.Load(Path.Combine(output, fixture));
            source.ClothIndex = source.Document.GetClothSummaries().Count - 1;
            var target = new EditorSession(); target.Load(Path.Combine(output, fixture.EndsWith(".bphcl") ? "manual-0.hkcl" : "manual-2.bphcl"));
            target.RenameSelected("CrossFormatTarget");
            CrossMerge(target, source, Path.Combine(output, "cross-real-" + (target.Document.IsBphcl ? "totk.bphcl" : "botw.hkcl")));
        }
        b.RemoveSelected(); Require(b.Document.GetClothSummaries().Count == 0 && !b.CanSimulate, "Empty document state");
        b.Save(Path.Combine(output, "empty.hkcl")); b.Undo(); Require(b.HasSelectedEntry, "Empty removal cannot be undone");
        try { a.Load(Path.Combine(output, "missing.hkcl")); throw new Exception("Missing file accepted"); } catch (FileNotFoundException) { }
        Require(a.Document.CaptureState() == aState, "Failed load replaced unsaved document");
        Console.WriteLine("PASS: overview rename/duplicate/remove/merge, format guards, save/export, selection undo, and lossless current/donor swap.");
    }
    private static void CrossMerge(EditorSession target, EditorSession source, string output)
    {
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var original = target.Document.CaptureState(); var donor = source.Document.CaptureState();
        var count = target.Document.GetClothSummaries().Count;
        var originalParticles = target.Document.GetParticleRows(0).Select(p => (p.X, p.Y, p.Z, p.Mass, p.InverseMass)).ToArray();
        var expected = PhysicsConversion.ConvertSelectedCloth(source.Document, source.ClothIndex, target.Platform);
        var expectedParticles = expected.GetParticleRows(0).Select(p => (p.X, p.Y, p.Z, p.Mass, p.InverseMass)).ToArray();
        var preview = target.PreviewMerge(source); Require(preview.Allowed, "Cross merge blocked: " + preview.Description);
        Require(target.Document.CaptureState() == original && source.Document.CaptureState() == donor, "Merge preview mutated files");
        target.MergeFrom(source);
        Require(target.Document.GetClothSummaries().Count == count + 1, "Cross merge imported more than the selected cloth");
        Require(originalParticles.SequenceEqual(target.Document.GetParticleRows(0).Select(p => (p.X, p.Y, p.Z, p.Mass, p.InverseMass))), "Merge changed current cloth particles");
        Require(expectedParticles.SequenceEqual(target.Document.GetParticleRows(count).Select(p => (p.X, p.Y, p.Z, p.Mass, p.InverseMass))), "Merge used different conversion/mass defaults");
        Require(source.Document.CaptureState() == donor, "Cross merge changed donor");
        var merged = target.Document.CaptureState(); target.Undo(); Require(target.Document.CaptureState() == original, "Cross merge undo not lossless");
        target.Redo(); Require(target.Document.CaptureState() == merged, "Cross merge redo not lossless");
        target.ExportCopy(output); var reload = new EditorSession(); reload.Load(output);
        Require(reload.Document.GetClothSummaries().Count == count + 1, "Cross merge did not survive save/reopen");
        if (reload.Document.IsBphcl)
        {
            var native = NativeBphclDocument.Open(output);
            var storage = BphclSchemaAudit.ValidateArrayStorageTypes(native);
            Require(storage.Count == 0, "Merged BPHCL array storage mismatch: " + string.Join("; ", storage.Take(5)));
            Require(native.Skeletons.Count == native.Cloths.Count && native.AampClothEntryNames.SequenceEqual(native.Cloths.Select(c => c.Name)), "Merged BPHCL root registration mismatch");
            var colliderItems = native.Colliders.Select(c => c.ItemIndex).ToHashSet();
            Require(native.Cloths.SelectMany(c => c.SimCloths).SelectMany(c => c.CollidableItemIndices).All(colliderItems.Contains), "Merged BPHCL has an unregistered collider");
            var alignment = native.TypeDefinitions.GroupBy(t => t.TypeIndex).ToDictionary(g => g.Key, g => g.First().Alignment ?? 1);
            Require(native.Items.All(i => alignment.GetValueOrDefault(i.TypeIndex, 1u) <= 1 || i.DataOffset % alignment[i.TypeIndex] == 0), "Merge broke native alignment");
        }
        Require(expectedParticles.SequenceEqual(reload.Document.GetParticleRows(count).Select(p => (p.X, p.Y, p.Z, p.Mass, p.InverseMass))), "Cross merge changed converted particles after saving");
        target.Undo();
    }
}
