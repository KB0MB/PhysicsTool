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
        var beforeRejectedMerge = bphcl.Document.CaptureState();
        Require(!bphcl.PreviewMerge(a).Allowed, "Invalid cross-format merge enabled");
        try { bphcl.MergeFrom(a); throw new Exception("Invalid merge accepted"); }
        catch (InvalidOperationException) { }
        Require(bphcl.Document.CaptureState() == beforeRejectedMerge && !bphcl.CanUndo, "Blocked merge changed history");
        b.RemoveSelected(); Require(b.Document.GetClothSummaries().Count == 0 && !b.CanSimulate, "Empty document state");
        b.Save(Path.Combine(output, "empty.hkcl")); b.Undo(); Require(b.HasSelectedEntry, "Empty removal cannot be undone");
        try { a.Load(Path.Combine(output, "missing.hkcl")); throw new Exception("Missing file accepted"); } catch (FileNotFoundException) { }
        Require(a.Document.CaptureState() == aState, "Failed load replaced unsaved document");
        Console.WriteLine("PASS: overview rename/duplicate/remove/merge, format guards, save/export, selection undo, and lossless current/donor swap.");
    }
}
