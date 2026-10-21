using Avalonia.Input;
using Avalonia.Interactivity;
using HKCLTool;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private void ConfigureTransforms()
    {
        _items.AddHandler(InputElement.KeyDownEvent, ForwardListTransformKey, RoutingStrategies.Tunnel);
        SelectionTransformPreview? preview = null;
        ParticlePreviewData? original = null;
        VisualModelData? originalModel = null;
        IReadOnlyDictionary<int, System.Numerics.Vector3>? originalParticles = null;
        IReadOnlyDictionary<int, SimulatedBonePreviewPose>? originalBones = null;
        IReadOnlyDictionary<int, ColliderPreviewShape>? originalColliders = null;
        HashSet<int> selected = new();
        var kind = PhysicsSelectionKind.Particles;
        var cloth = 0;
        var valid = false;
        var bakeScale = false;
        var resizePivot = System.Numerics.Vector3.Zero;
        _viewport.TransformStarted += () => {
            if (EditingRagdollModel) { _viewport.FinishTransform(false); _status.Text = "Edit model-bone poses in the inspector."; return; }
            if (Session.Document.IsHkrg && _mode.SelectedIndex == 0) { _viewport.FinishTransform(false); _status.Text = "Select bones or rigid bodies to transform."; return; }
            if (Session.Document.IsHelperBoneSidecar) { _viewport.FinishTransform(false); _status.Text = "Edit helper base poses in the inspector."; return; }
            cloth = Session.ClothIndex; original = _viewport.Data;
            originalModel = _viewport.Model; originalParticles = _viewport.Simulated;
            originalBones = _viewport.SimulatedBones; originalColliders = _viewport.SimulatedColliders;
            selected = _items.SelectedItems!.Cast<Item>().Select(i => i.Index).ToHashSet();
            kind = (PhysicsSelectionKind)_mode.SelectedIndex; valid = true;
            bakeScale = kind == PhysicsSelectionKind.Bones && !Session.Document.IsHkrg && _viewport.IsScaleTransform;
            resizePivot = _viewport.TransformPivot;
            try { preview = new SelectionTransformPreview(Session.Document, cloth, original, kind, selected, _follow.IsChecked == true, bakeScale, resizePivot); }
            catch (Exception ex) { valid = false; _viewport.FinishTransform(false); _status.Text = ex.Message; return; }
            _status.Text = "Transform selection";
        };
        _viewport.TransformPreview += delta => {
            if (preview == null) return;
            try
            {
                _viewport.Data = preview.Sample(delta);
                _viewport.Simulated = null; _viewport.SimulatedBones = null; _viewport.SimulatedColliders = null;
                valid = true;
            }
            catch (Exception ex) { valid = false; _status.Text = ex.Message; }
        };
        _viewport.TransformFinished += async delta => {
            preview = null;
            if (original != null) _viewport.Data = original;
            original = null;
            _viewport.Simulated = originalParticles; _viewport.SimulatedBones = originalBones; _viewport.SimulatedColliders = originalColliders;
            if (originalModel != null && kind == PhysicsSelectionKind.Bones) _viewport.SetModelPose(originalModel);
            originalModel = null;
            if (delta is { } matrix && valid && matrix != System.Numerics.Matrix4x4.Identity)
            {
                if (bakeScale)
                {
                    try { await AdjustBoneBranchResize(selected, new(matrix.M11, matrix.M22, matrix.M33), resizePivot); }
                    catch (Exception ex) { _status.Text = ex.Message; }
                }
                else Try(() => Edit((doc, index) => SelectionTransforms.Apply(doc, index, kind, selected, matrix, _follow.IsChecked == true)));
                _refreshing = true;
                try
                {
                    _items.SelectedItems!.Clear();
                    foreach (var item in _items.Items.Cast<Item>().Where(i => selected.Contains(i.Index))) _items.SelectedItems.Add(item);
                }
                finally { _refreshing = false; }
                ShowDetails(); SelectViewport(); UpdateActions();
            }
            else if (valid) _status.Text = "Transform canceled.";
            _viewport.InvalidateVisual();
        };
    }

    private void ForwardListTransformKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || !IsEditorVisible || !_viewport.CanSelect || !_viewport.ShowPhysics || _viewport.TransformActive ||
            e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.G or Key.R or Key.S))
            return;

        // List selection already updates the viewport. Hand off focus only when
        // a transform is requested, preserving list navigation and multi-selection.
        _viewport.Focus();
        e.Handled = _viewport.TransformKey(e);
    }
}
