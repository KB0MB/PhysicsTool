using HKCLTool;

namespace PhysicsTool.Core;

public sealed class EditorSession
{
    private const int UndoLimit = 50;

    public sealed record ModelBoneReference(string ModelPath, IReadOnlyDictionary<string, System.Numerics.Matrix4x4> Bones);
    // Immutable preview references travel with undo, but never enter a physics file.
    public IReadOnlyDictionary<Guid, ModelBoneReference> ModelBoneReferences { get; set; } = new Dictionary<Guid, ModelBoneReference>();
    private sealed record Snapshot(string State, int ClothIndex, Guid[] EntryIds, IReadOnlyDictionary<Guid, ModelBoneReference> ModelReferences);
    private List<Guid> _entryIds = new();
    // UI binding state follows an entry through rename, deletion of neighbors and undo.
    public Guid SelectedEntryId
    {
        get
        {
            SynchronizeEntryIds();
            return HasSelectedEntry ? _entryIds[ClothIndex] : Guid.Empty;
        }
    }
    private void SynchronizeEntryIds()
    {
        var count = Document.GetClothSummaries().Count;
        while (_entryIds.Count < count)
            _entryIds.Add(Guid.NewGuid());
        if (_entryIds.Count > count)
            _entryIds.RemoveRange(count, _entryIds.Count - count);
    }
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
    public bool CanEditClothSettings => HasSelectedEntry && !Document.IsReadOnlyExternal;
    public bool CanScaleMass => CanManageCloth && Document.CanEditParticleValues;
    public bool CanSimulate => HasSelectedEntry &&
        (!Document.IsReadOnlyExternal || Document.IsBphcl && Document.GetTotkSimulationSupport(ClothIndex).Supported) &&
        Document.GetParticleRows(ClothIndex).Any(p => !p.Fixed);

    public HkclPreviewSimulator CreateSimulator()
    {
        if (!CanSimulate)
            throw new InvalidOperationException("The selected cloth cannot be simulated with the available kernels.");
        return new(Document.IsBphcl ? Document.GetTotkSimulationPreview(ClothIndex) : Document.GetParticlePreview(ClothIndex));
    }

    public void New(string name, int format)
    {
        var document = HkclService.CreateBlankCloth(name, format == 2);
        Document = document;
        Platform = format == 0 ? HkclPlatform.WiiU : HkclPlatform.Switch;
        ModelBoneReferences = new Dictionary<Guid, ModelBoneReference>();
        _entryIds = new();
        SynchronizeEntryIds();
        Path = null;
        ClothIndex = 0;
        ResetHistory(savedState: null);
    }

    public void Load(string path)
    {
        var document = new HkclService();
        document.Load(path);
        var platform = document.IsBphcl || document.IsHelperBoneSidecar
            ? HkclPlatform.Switch
            : HkclService.DetectHkclPlatform(path);

        Document = document;
        Platform = platform;
        Path = path;
        ClothIndex = 0;
        ModelBoneReferences = new Dictionary<Guid, ModelBoneReference>();
        _entryIds = new();
        SynchronizeEntryIds();
        ResetHistory(document.CaptureState());
    }

    public void Save(string path)
    {
        Document.SaveHkcl(path, Platform);
        Path = path;
        _savedState = Document.CaptureState();
    }

    public void Clear()
    {
        Document = new();
        Path = null;
        ClothIndex = 0;
        Platform = HkclPlatform.WiiU;
        ModelBoneReferences = new Dictionary<Guid, ModelBoneReference>();
        _entryIds.Clear();
        ResetHistory(savedState: null);
    }

    private void ResetHistory(string? savedState)
    {
        _savedState = savedState;
        _undo.Clear();
        _redo.Clear();
    }

    public void SwapWith(EditorSession other)
    {
        if (ReferenceEquals(this, other) || !Document.HasDocument || !other.Document.HasDocument)
            throw new InvalidOperationException("Load both a current file and a donor before swapping.");
        (Document, other.Document) = (other.Document, Document);
        (ModelBoneReferences, other.ModelBoneReferences) = (other.ModelBoneReferences, ModelBoneReferences);
        (Path, other.Path) = (other.Path, Path);
        (Platform, other.Platform) = (other.Platform, Platform);
        (ClothIndex, other.ClothIndex) = (other.ClothIndex, ClothIndex);
        (_entryIds, other._entryIds) = (other._entryIds, _entryIds);
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
            if (doc.IsHelperBoneSidecar)
                doc.RemoveHelperGroup(index);
            else
                doc.RemoveCloth(index);
            _entryIds.RemoveAt(index);
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
        if (Document.IsBphcl != source.IsBphcl)
        {
            try
            {
                var converted = PhysicsConversion.ConvertSelectedCloth(source, donor.ClothIndex, Platform);
                if (Document.IsBphcl)
                {
                    var report = Document.GetBphclMergePreflight(converted, 0);
                    if (!report.CanAttemptExperimentalTypeUnion) return (false, report.ToDisplayText());
                }
                return (true, $"Convert the selected donor cloth to {(Document.IsBphcl ? "BPHCL" : "HKCL")} and add it to the current file?\n\nExisting cloths and the donor stay unchanged. Conversion uses the same defaults as File > Convert; game-specific behavior may still need tuning.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or NotSupportedException or ArgumentException)
            { return (false, "This cloth could not be converted for merging. " + ex.Message); }
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
            var crossCloth = !doc.IsHelperBoneSidecar && !donor.Document.IsHelperBoneSidecar && doc.IsBphcl != donor.Document.IsBphcl;
            var source = crossCloth ? PhysicsConversion.ConvertSelectedCloth(donor.Document, donor.ClothIndex, Platform) : donor.Document;
            result = doc.MergeClothFrom(source, crossCloth ? 0 : donor.ClothIndex, index);
            if (doc.GetClothSummaries().Count > count) ClothIndex = doc.GetClothSummaries().Count - 1;
        });
        return result;
    }
    public void ExportCopy(string path, HkclPlatform? platform = null)
    {
        var copy = new HkclService();
        copy.RestoreState(Document.CaptureState());
        if (System.IO.Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            copy.ExportReadableJson(path);
        else
            copy.SaveHkcl(path, platform ?? Platform);
    }
    public int ScaleParticleMass(float scale, IReadOnlyCollection<int> selection)
    {
        if (!CanScaleMass) throw new InvalidOperationException("Particle mass editing is unavailable for this document.");
        if (!float.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale), "Mass scale must be finite and positive.");
        var rows = Document.GetParticleRows(ClothIndex);
        if (selection.Any(index => !rows.Any(row => row.Index == index))) throw new ArgumentOutOfRangeException(nameof(selection));
        var targets = rows.Where(row => !row.Fixed && (selection.Count == 0 || selection.Contains(row.Index))).ToArray();
        if (targets.Length == 0) throw new InvalidOperationException("Select at least one dynamic particle before scaling its mass.");
        foreach (var row in targets)
        {
            row.Mass *= scale;
            row.InverseMass = 1f / row.Mass;
            if (!float.IsFinite(row.Mass) || row.Mass <= float.Epsilon || !float.IsFinite(row.InverseMass) || row.InverseMass <= 0)
                throw new InvalidOperationException("The selected scale produces an invalid mass or inverse mass.");
        }
        Edit((document, cloth) => ParticleEdits.Apply(document, cloth, targets));
        return targets.Length;
    }
    public void UpdateClothSettings(ClothSimulationSettings settings)
    {
        if (!CanEditClothSettings) throw new InvalidOperationException("Cloth settings editing is currently available for HKCL only.");
        Edit((document, cloth) => document.UpdateSimulationSettings(cloth, settings));
    }
    public void Edit(Action<HkclService, int> action)
    {
        SynchronizeEntryIds();
        var before = CaptureSnapshot();
        try
        {
            action(Document, ClothIndex);
        }
        catch
        {
            Document.RestoreState(before.State);
            ClothIndex = before.ClothIndex;
            _entryIds = before.EntryIds.ToList();
            ModelBoneReferences = before.ModelReferences;
            throw;
        }

        SynchronizeEntryIds();
        if (Document.CaptureState() == before.State && ReferenceEquals(ModelBoneReferences, before.ModelReferences))
            return;

        _undo.Push(before);
        _redo.Clear();
        TrimUndoHistory();
    }

    private Snapshot CaptureSnapshot() =>
        new(Document.CaptureState(), ClothIndex, _entryIds.ToArray(), ModelBoneReferences);

    private void TrimUndoHistory()
    {
        if (_undo.Count > UndoLimit)
        {
            var keep = _undo.Take(UndoLimit).Reverse().ToArray();
            _undo.Clear();
            foreach (var snapshot in keep)
                _undo.Push(snapshot);
        }
    }

    public void Undo() => Restore(_undo, _redo);
    public void Redo() => Restore(_redo, _undo);

    private void Restore(Stack<Snapshot> source, Stack<Snapshot> target)
    {
        if (source.Count == 0)
            return;

        SynchronizeEntryIds();
        var before = CaptureSnapshot();
        var snapshot = source.Peek();
        Document.RestoreState(snapshot.State);
        source.Pop();
        target.Push(before);
        _entryIds = snapshot.EntryIds.ToList();
        ModelBoneReferences = snapshot.ModelReferences;
        ClothIndex = Math.Clamp(snapshot.ClothIndex, 0, Math.Max(0, Document.GetClothSummaries().Count - 1));
    }
}
