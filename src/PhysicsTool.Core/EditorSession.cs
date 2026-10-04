using HKCLTool;

namespace PhysicsTool.Core;

public sealed class EditorSession
{
    private sealed record Snapshot(string State, int ClothIndex);
    private Stack<Snapshot> _undo = new(), _redo = new();
    private string? _savedState;
    public HkclService Document { get; private set; } = new();
    public HkclPlatform Platform { get; set; } = HkclPlatform.WiiU;
    public string? Path { get; private set; }
    public int ClothIndex { get; set; }
    public bool IsDirty => Document.HasDocument && Document.CaptureState() != _savedState;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool HasSelectedEntry => Document.HasDocument && ClothIndex >= 0 && ClothIndex < Document.GetClothSummaries().Count;
    public bool CanManageCloth => HasSelectedEntry && !Document.IsHelperBoneSidecar && !Document.IsHkrg;
    public bool CanRemoveEntry => HasSelectedEntry && !Document.IsHkrg;
    public bool CanSimulate => HasSelectedEntry && !Document.IsReadOnlyExternal && Document.GetParticleRows(ClothIndex).Any(p => !p.Fixed);

    public void New(string name, int format)
    {
        var document = HkclService.CreateBlankCloth(name, format == 2);
        Document = document; Platform = format == 0 ? HkclPlatform.WiiU : HkclPlatform.Switch;
        Path = null; ClothIndex = 0; _savedState = null; _undo.Clear(); _redo.Clear();
    }
    public void Load(string path)
    {
        var document = new HkclService(); document.Load(path);
        var platform = document.IsBphcl || document.IsHelperBoneSidecar ? HkclPlatform.Switch : HkclService.DetectHkclPlatform(path);
        Document = document; Platform = platform; Path = path; ClothIndex = 0;
        _undo.Clear(); _redo.Clear(); _savedState = document.CaptureState();
    }
    public void Save(string path)
    {
        Document.SaveHkcl(path, Platform); Path = path; _savedState = Document.CaptureState();
    }
    public void Clear()
    {
        Document = new(); Path = null; ClothIndex = 0; Platform = HkclPlatform.WiiU;
        _savedState = null; _undo.Clear(); _redo.Clear();
    }
    public void SwapWith(EditorSession other)
    {
        if (ReferenceEquals(this, other) || !Document.HasDocument || !other.Document.HasDocument)
            throw new InvalidOperationException("Load both a current file and a donor before swapping.");
        (Document, other.Document) = (other.Document, Document);
        (Path, other.Path) = (other.Path, Path);
        (Platform, other.Platform) = (other.Platform, Platform);
        (ClothIndex, other.ClothIndex) = (other.ClothIndex, ClothIndex);
        (_savedState, other._savedState) = (other._savedState, _savedState);
        (_undo, other._undo) = (other._undo, _undo);
        (_redo, other._redo) = (other._redo, _redo);
    }
    public void RenameSelected(string name)
    {
        if (!CanManageCloth) throw new InvalidOperationException("Select a cloth to rename.");
        Edit((doc, index) => doc.RenameCloth(index, name));
    }
    public void DuplicateSelected()
    {
        if (!CanManageCloth) throw new InvalidOperationException("Select a cloth to duplicate.");
        Edit((doc, index) => ClothIndex = doc.DuplicateCloth(index));
    }
    public void RemoveSelected()
    {
        if (!CanRemoveEntry) throw new InvalidOperationException("Select a cloth or helper group to remove.");
        Edit((doc, index) =>
        {
            if (doc.IsHelperBoneSidecar) doc.RemoveHelperGroup(index); else doc.RemoveCloth(index);
            ClothIndex = Math.Clamp(index, 0, Math.Max(0, doc.GetClothSummaries().Count - 1));
        });
    }
    public (bool Allowed, string Description) PreviewMerge(EditorSession donor)
    {
        var source = donor.Document;
        if (!Document.HasDocument || !donor.HasSelectedEntry) return (false, "Load a current file and select a donor entry.");
        if (Document.IsHkrg || source.IsHkrg) return (false, "Ragdoll merging is not supported.");
        if (Document.IsHelperBoneSidecar || source.IsHelperBoneSidecar)
            return Document.IsBphhb && source.IsBphhb || Document.IsBphyssb && source.IsBphyssb
                ? (true, "Import this helper group, remapping its bones, curves, outputs, and base poses? The donor is not changed.")
                : (false, "Helper-group merging requires matching sidecar formats. Convert the donor first.");
        if (Document.IsBphcl && !source.IsBphcl) return (false, "HKCL cannot be merged directly into BPHCL. Convert the donor first.");
        if (!Document.IsBphcl && source.IsBphcl)
        {
            if (!HasSelectedEntry) return (false, "Select an HKCL cloth as the conversion template first.");
            var report = Document.GetBphclToHkclConversionPreflight(source, donor.ClothIndex, ClothIndex);
            return (report.IsEligible, report.ToDisplayText());
        }
        if (Document.IsBphcl)
        {
            var report = Document.GetBphclMergePreflight(source, donor.ClothIndex);
            return (report.CanAttemptExperimentalTypeUnion, report.ToDisplayText());
        }
        return (true, "Import this complete cloth package, including its skeleton and referenced colliders? The donor is not changed.");
    }
    public string MergeFrom(EditorSession donor)
    {
        var preview = PreviewMerge(donor);
        if (!preview.Allowed) throw new InvalidOperationException(preview.Description);
        var count = Document.GetClothSummaries().Count;
        var result = "";
        Edit((doc, index) =>
        {
            result = doc.MergeClothFrom(donor.Document, donor.ClothIndex, index);
            if (doc.GetClothSummaries().Count > count) ClothIndex = doc.GetClothSummaries().Count - 1;
        });
        return result;
    }
    public void ExportCopy(string path, HkclPlatform? platform = null)
    {
        var copy = new HkclService(); copy.RestoreState(Document.CaptureState());
        if (System.IO.Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)) copy.ExportReadableJson(path);
        else copy.SaveHkcl(path, platform ?? Platform);
    }
    public void Edit(Action<HkclService, int> action)
    {
        var before = new Snapshot(Document.CaptureState(), ClothIndex);
        try { action(Document, ClothIndex); }
        catch { Document.RestoreState(before.State); ClothIndex = before.ClothIndex; throw; }
        if (Document.CaptureState() == before.State) return;
        _undo.Push(before); _redo.Clear();
        if (_undo.Count > 50)
        {
            var keep = _undo.Take(50).Reverse().ToArray(); _undo.Clear();
            foreach (var snapshot in keep) _undo.Push(snapshot);
        }
    }
    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);
    private void Restore(Stack<Snapshot> source, Stack<Snapshot> target)
    {
        if (source.Count == 0) return;
        var before = new Snapshot(Document.CaptureState(), ClothIndex);
        var snapshot = source.Peek(); Document.RestoreState(snapshot.State);
        source.Pop(); target.Push(before);
        ClothIndex = Math.Clamp(snapshot.ClothIndex, 0, Math.Max(0, Document.GetClothSummaries().Count - 1));
    }
}
