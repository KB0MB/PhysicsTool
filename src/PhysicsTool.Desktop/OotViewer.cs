using Avalonia.Controls;
using PhysicsTool.Core;
using PhysicsTool.Desktop.Rendering;
using HKCLTool;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private EditorSession? _savedOotSession;
    private ImportedModelRig? _savedOotRig;
    private OotHelperEvaluator[] _ootHelpers = [];
    private OotActorPreview? _ootScene;
    private bool _ootSimulating;
    internal int OotActorClothCount => _ootScene?.ClothCount ?? 0;
    private string _ootViewerName = "";
    private void EnterOotViewer(string name)
    {
        _ootViewerName = name;
        _inspectorWidth = _workspace.ColumnDefinitions[2].Width;
        _inspector.IsVisible = _workspaceSplit.IsVisible = _editorActions.IsVisible = false;
        _workspace.ColumnDefinitions[1].Width = new GridLength(0);
        _workspace.ColumnDefinitions[2].MinWidth = 0; _workspace.ColumnDefinitions[2].Width = new GridLength(0);
        _viewport.ReadOnlyActorView = true;
        Title = "PhysicsTool - " + name;
        UpdateActions();
    }
    private void UpdateOotViewerActions()
    {
        if (!Session.IsReadOnly) return;
        Title = "PhysicsTool - " + _ootViewerName;
        _add.IsVisible = _loadModel.IsVisible = _clearModel.IsVisible = _editorActions.IsVisible = false;
        _save.IsEnabled = _saveAs.IsEnabled = _export.IsEnabled = _convert.IsEnabled = false;
        _undo.IsEnabled = _redo.IsEnabled = _editorUndo.IsEnabled = _editorRedo.IsEnabled = false;
        _add.IsEnabled = _delete.IsEnabled = _constraint.IsEnabled = _removeConstraint.IsEnabled = false;
        _viewport.CanSelect = _viewport.CanTransform = false;
        _viewport.Selected.Clear(); _viewport.SelectedBones.Clear(); _viewport.SelectedColliders.Clear();
        _simulate.IsEnabled = _wind.IsEnabled = _options.IsEnabled = _ootScene?.SimulatedClothCount > 0;
        _simulate.Content = _ootSimulating ? "Stop simulation" : "Run OoT preview";
        ToolTip.SetTip(_simulate, _ootScene == null ? null : string.Join(" ", _ootScene.Warnings));
    }
    private void ExitOotViewer()
    {
        if (_savedOotSession == null) return;
        ++_modelRequest; _modelLoading = false;
        _ootScene = null; _ootSimulating = false; _ootHelpers = []; ClearEditorAnimation();
        Session = _savedOotSession; _savedOotSession = null;
        _importedModel = _savedOotRig; _savedOotRig = null;
        _viewport.ReadOnlyActorView = false;
        _add.IsVisible = _loadModel.IsVisible = _clearModel.IsVisible = true;
        _inspector.IsVisible = _workspaceSplit.IsVisible = _editorActions.IsVisible = true;
        _workspace.ColumnDefinitions[1].Width = new GridLength(5);
        _workspace.ColumnDefinitions[2].MinWidth = 300; _workspace.ColumnDefinitions[2].Width = _inspectorWidth;
        _viewport.SetModel(_importedModel?.Rest); Refresh(); ConfigureEditorTimeline(); UpdateModelActions();
    }
    private VisualModelData EvaluateOotHelpers(VisualModelData skeleton)
    {
        foreach (var helper in _ootHelpers) skeleton = helper.Evaluate(skeleton);
        return skeleton;
    }
    private void PoseOotActor(float seconds = 0, bool reset = false, bool looped = false, bool present = true)
    {
        try { PoseOotActorCore(seconds, reset, looped, present); }
        catch (InvalidDataException ex)
        {
            _ootSimulating = false;
            PauseEditorAnimation();
            UpdateActions();
            _status.Text = "OoT preview stopped: " + ex.Message;
        }
    }
    private void PoseOotActorCore(float seconds, bool reset, bool looped, bool present)
    {
        if (_ootScene == null) return;
        var rig = _importedModel;
        var clean = rig == null ? null : EvaluateOotHelpers(EditorClip == null ? rig.Rest :
            rig.AnimationSkeleton(EditorClip, _editorFrame, _ootSimulating ? _ootScene.DrivenNames : null));
        _ootScene.Configure(s => { SimulationOptions.ApplyTo(s); s.WindEnabled = _wind.IsChecked == true; });
        var overrides = _ootScene.Update(rig?.Rest, clean, _ootSimulating, seconds, reset, looped, present);
        if (!present) return;
        _viewport.Data = _ootScene.Overlay;
        _viewport.Simulated = null; _viewport.SimulatedBones = null; _viewport.SimulatedColliders = null;
        if (rig != null) _viewport.SetModelPose(rig.Sample(overrides));
        _viewport.InvalidateVisual();
    }
}
