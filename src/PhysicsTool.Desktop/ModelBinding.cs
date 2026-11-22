using HKCLTool;
using PhysicsTool.Desktop.Rendering;
using System.Runtime.CompilerServices;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private ImportedModelRig? _importedModel;
    private HkclService? _bindingDocument;
    private Guid _bindingEntry;
    private ClothModelBinding _modelBinding = new();
    private readonly ConditionalWeakTable<HkclService, Dictionary<Guid, ClothModelBinding>> _documentBindings = new();
    private ImportedModelRig BoundImportedModel => Session.Document.IsHelperBoneSidecar
        ? _importedModel! : _modelBinding.Align(_importedModel!);
    private IReadOnlyDictionary<string, System.Numerics.Matrix4x4>? MatchedModelReferences =>
        Session.ModelBoneReferences.TryGetValue(Session.SelectedEntryId, out var reference) && reference.ModelPath == _importedModel?.Rest.SourcePath
            ? reference.Bones : null;

    private void UpdateImportedModelPose(bool captureRest = false)
    {
        if (_ootScene != null) { PoseOotActor(reset: true); return; }
        if (IsActorMode) return;
        if (Session.AllClothsSelected)
        {
            if (_importedModel != null) _viewport.SetModelPose(_importedModel.Rest);
            return;
        }
        var entry = Session.SelectedEntryId;
        if (!ReferenceEquals(_bindingDocument, Session.Document) || _bindingEntry != entry)
        {
            if (!captureRest) return;
            _bindingDocument = Session.Document;
            _bindingEntry = entry;
            var bindings = _documentBindings.GetValue(Session.Document, _ => new());
            if (!bindings.TryGetValue(entry, out _modelBinding!)) bindings.Add(entry, _modelBinding = new());
        }
        if (captureRest) _modelBinding.Capture(_viewport.Data.Bones);
        if (_importedModel == null) return;
        if (EditorClip != null && !_viewport.TransformActive) { PoseEditorAnimation(0, true, false); return; }
        if (Session.Document.IsHelperBoneSidecar) { PoseHelperModel(_importedModel.Rest); return; }
        var rig = BoundImportedModel;
        if (_solver == null && _ragdollSolver == null)
        {
            _viewport.SetModelPose(_ootHelpers.Length == 0 ? rig.Rest : rig.Sample(EvaluateOotHelpers(rig.Rest).Bones
                .Select(b => new VisualBoneOverride(b.Name, b.Position, b.Rotation, b.Scale)).ToArray()));
            return;
        }
        var overrides = _modelBinding.Overrides(rig.Rest, _viewport.Data, _viewport.SimulatedBones, MatchedModelReferences);
        if (_ootHelpers.Length > 0)
        {
            var combined = EvaluateOotHelpers(rig.Rest).Bones.ToDictionary(b => b.Name,
                b => new VisualBoneOverride(b.Name, b.Position, b.Rotation, b.Scale), StringComparer.OrdinalIgnoreCase);
            foreach (var pose in overrides) combined[pose.Name] = pose;
            _viewport.SetModelPose(rig.Sample(combined.Values.ToArray()));
            return;
        }
        _viewport.SetModelPose(rig.Sample(overrides));
    }
}
