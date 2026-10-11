using HKCLTool;
using PhysicsTool.Core;
using Avalonia.Threading;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private void ConfigureTransforms()
    {
        SelectionTransformPreview? preview = null;
        ParticlePreviewData? original = null;
        VisualModelData? originalModel = null;
        IReadOnlyDictionary<int, System.Numerics.Vector3>? originalParticles = null;
        IReadOnlyDictionary<int, SimulatedBonePreviewPose>? originalBones = null;
        IReadOnlyDictionary<int, ColliderPreviewShape>? originalColliders = null;
        var modelPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1d / 60) };
        HashSet<int> selected = new();
        var kind = PhysicsSelectionKind.Particles;
        var cloth = 0;
        var valid = false;
        modelPreviewTimer.Tick += (_, _) => {
            modelPreviewTimer.Stop();
            if (_viewport.TransformActive && preview != null)
                try { UpdateImportedModelPose(); } catch (Exception ex) { valid = false; _status.Text = ex.Message; }
        };
        Closed += (_, _) => modelPreviewTimer.Stop();
        _viewport.TransformStarted += () => {
            if (EditingRagdollModel) { _viewport.FinishTransform(false); _status.Text = "Edit model-bone poses in the inspector."; return; }
            if (Session.Document.IsHkrg && _mode.SelectedIndex == 0) { _viewport.FinishTransform(false); _status.Text = "Select bones or rigid bodies to transform."; return; }
            if (Session.Document.IsHelperBoneSidecar) { _viewport.FinishTransform(false); _status.Text = "Edit helper base poses in the inspector."; return; }
            cloth = Session.ClothIndex; original = _viewport.Data;
            originalModel = _viewport.Model; originalParticles = _viewport.Simulated;
            originalBones = _viewport.SimulatedBones; originalColliders = _viewport.SimulatedColliders;
            selected = _items.SelectedItems!.Cast<Item>().Select(i => i.Index).ToHashSet();
            kind = (PhysicsSelectionKind)_mode.SelectedIndex; valid = true;
            try { preview = new SelectionTransformPreview(Session.Document, cloth, original, kind, selected, _follow.IsChecked == true); }
            catch (Exception ex) { valid = false; _viewport.FinishTransform(false); _status.Text = ex.Message; return; }
            _status.Text = "Transform selection";
        };
        _viewport.TransformPreview += delta => {
            if (preview == null) return;
            try
            {
                _viewport.Data = preview.Sample(delta);
                _viewport.Simulated = null; _viewport.SimulatedBones = null; _viewport.SimulatedColliders = null;
                if (kind == PhysicsSelectionKind.Bones && !modelPreviewTimer.IsEnabled) modelPreviewTimer.Start();
                valid = true;
            }
            catch (Exception ex) { valid = false; _status.Text = ex.Message; }
        };
        _viewport.TransformFinished += delta => {
            modelPreviewTimer.Stop();
            preview = null;
            if (original != null) _viewport.Data = original;
            original = null;
            _viewport.Simulated = originalParticles; _viewport.SimulatedBones = originalBones; _viewport.SimulatedColliders = originalColliders;
            if (originalModel != null && kind == PhysicsSelectionKind.Bones) _viewport.SetModelPose(originalModel);
            originalModel = null;
            if (delta is { } matrix && valid && matrix != System.Numerics.Matrix4x4.Identity)
            {
                Try(() => Edit((doc, index) => SelectionTransforms.Apply(doc, index, kind, selected, matrix, _follow.IsChecked == true)));
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
}
