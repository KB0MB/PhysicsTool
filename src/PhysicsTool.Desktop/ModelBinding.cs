using HKCLTool;
using PhysicsTool.Desktop.Rendering;
using System.Runtime.CompilerServices;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private ImportedModelRig? _importedModel;
    private HkclService? _bindingDocument;
    private ClothModelBinding _modelBinding = new();
    private readonly ConditionalWeakTable<HkclService, ClothModelBinding> _documentBindings = new();

    private void UpdateImportedModelPose(bool captureRest = false)
    {
        if (IsActorMode) return;
        if (!ReferenceEquals(_bindingDocument, Session.Document))
        {
            if (!captureRest) return;
            _bindingDocument = Session.Document;
            _modelBinding = _documentBindings.GetValue(Session.Document, _ => new());
        }
        if (captureRest) _modelBinding.Capture(_viewport.Data.Bones);
        if (_importedModel == null) return;
        if (EditorClip != null && !_viewport.TransformActive) { PoseEditorAnimation(0, true, false); return; }
        if (Session.Document.IsHelperBoneSidecar) { PoseHelperModel(_importedModel.Rest); return; }
        var overrides = _modelBinding.Overrides(_importedModel.Rest, _viewport.Data, _viewport.SimulatedBones);
        _viewport.SetModelPose(_importedModel.Sample(overrides));
    }
}
