using Avalonia.Controls;
using Avalonia.Media;
using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private bool _ragdollTabs;
    private ComboBox _ragdollSkeleton = null!;
    private bool EditingRagdollModel => Session.Document.IsHkrg && _mode.SelectedIndex == 1 && _ragdollSkeleton.SelectedIndex == 1;
    private void ConfigureRagdollSkeletonPicker(DockPanel host)
    {
        _ragdollSkeleton = new ComboBox { Name = "RagdollSkeleton", ItemsSource = new[] { "Ragdoll bones", "Model bones" }, SelectedIndex = 0,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Margin = new Avalonia.Thickness(0, 4, 0, 8), IsVisible = false };
        ToolTip.SetTip(_ragdollSkeleton, "Ragdoll bones drive rigid bodies. Model bones are the full animation skeleton stored in this HKRG.");
        _ragdollSkeleton.SelectionChanged += (_, _) => { if (!_refreshing) { _selectionDocument = null; Refresh(); } };
        DockPanel.SetDock(_ragdollSkeleton, Dock.Top); host.Children.Add(_ragdollSkeleton);
    }
    private HkrgGlidePreviewSimulator? _ragdollSolver;
    internal bool RagdollPreviewRunning => _ragdollSolver != null;
    private void ToggleRagdollPreview()
    {
        if (_ragdollSolver != null) { StopSimulation(); return; }
        if (!Session.HasSelectedEntry || !Session.Document.IsHkrg || IsActorMode) return;
        PauseEditorAnimation(); SelectEditorAnimation(-1);
        try
        {
            _ragdollSolver = new(_viewport.Data, Session.Document.GetColliderRows(0), Session.Document.GetHkrgConstraintRows());
            SimulationOptions.ApplyTo(_ragdollSolver);
            _previewStepClock.Reset(); _clock.Restart(); _lastTick = 0; _timer.Start(); UpdateActions();
            _status.Text = "Ragdoll stress preview: approximate glide forces and joint limits, not a Havok runtime.";
        }
        catch (Exception ex) { _ragdollSolver = null; _status.Text = ex.Message; UpdateActions(); }
    }
    private void UpdateRagdollTabs()
    {
        _ragdollSkeleton.IsVisible = Session.Document.IsHkrg && _mode.SelectedIndex == 1;
        _ragdollSkeleton.IsEnabled = !_timer.IsEnabled;
        if (_ragdollTabs == Session.Document.IsHkrg) return;
        _ragdollTabs = Session.Document.IsHkrg;
        var selected = Math.Max(0, _mode.SelectedIndex);
        _mode.ItemsSource = _ragdollTabs ? new[] { "Constraints", "Bones", "Rigid bodies" } : new[] { "Particles", "Bones", "Colliders" };
        _mode.SelectedIndex = selected;
    }
    private void RagdollText(string label, string value) => DetailRow(_details, label, new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
    private void ShowRagdollDetails(object data)
    {
        switch (data)
        {
            case HkrgConstraintEditRow c: ShowRagdollConstraint(c); break;
            case BoneEditRow b: ShowRagdollBone(b); break;
            case ColliderEditRow b: ShowRagdollBody(b); break;
        }
    }
    private void ShowRagdollConstraint(HkrgConstraintEditRow c)
    {
        var name = new TextBox { Name = "RagdollConstraintName", Text = c.Name }; DetailRow(_details, "Name", name);
        RagdollText("Type", c.ConstraintType); RagdollText("Body A", $"{c.BodyA}: {c.BodyAName}"); RagdollText("Body B", $"{c.BodyB}: {c.BodyBName}");
        var inputs = new Dictionary<string, NumericUpDown>();
        foreach (var field in RagdollEdits.ConstraintFields)
        {
            if (field.Read(c) is not { } value) continue;
            var input = DetailNumber(_details, "Ragdoll" + field.Id, field.Label, value, field.NonNegative ? 0 : -1000000);
            ToolTip.SetTip(input, field.Id.EndsWith("Angle") ? "Native angular limit in radians." : field.Id.EndsWith("Tau") ? "Native angular-limit correction factor; not a mass multiplier."
                : field.Id.EndsWith("Damping") ? "Native angular-limit damping factor." : "Native maximum angular friction torque.");
            inputs.Add(field.Id, input);
        }
        DetailApply("ApplyRagdollConstraint", () => Edit((doc, _) => RagdollEdits.Constraint(doc, c.Index, name.Text ?? "",
            inputs.Where(k => k.Value.Value != (decimal)RagdollEdits.ConstraintFields.Single(f => f.Id == k.Key).Read(c)!.Value).ToDictionary(k => k.Key, k => DetailValue(k.Value)))));
    }
    private (NumericUpDown X, NumericUpDown Y, NumericUpDown Z) RagdollVector(string id, string label, Vector3 value)
        => (DetailNumber(_details, id + "X", label + " X", value.X), DetailNumber(_details, id + "Y", label + " Y", value.Y), DetailNumber(_details, id + "Z", label + " Z", value.Z));
    private static Vector3 RagdollValue((NumericUpDown X, NumericUpDown Y, NumericUpDown Z) input, Vector3 original)
        => new(input.X.Value == (decimal)original.X ? original.X : DetailValue(input.X), input.Y.Value == (decimal)original.Y ? original.Y : DetailValue(input.Y), input.Z.Value == (decimal)original.Z ? original.Z : DetailValue(input.Z));
    private static bool RagdollChanged((NumericUpDown X, NumericUpDown Y, NumericUpDown Z) input, Vector3 value)
        => input.X.Value != (decimal)value.X || input.Y.Value != (decimal)value.Y || input.Z.Value != (decimal)value.Z;
    private void ShowRagdollBone(BoneEditRow bone)
    {
        var name = new TextBox { Name = "RagdollBoneName", Text = bone.Name }; DetailRow(_details, "Name", name);
        var model = EditingRagdollModel;
        var rows = model ? Session.Document.GetHkrgModelBoneRows() : Session.Document.GetBoneRows(0);
        RagdollText("Parent", bone.ParentIndex < 0 ? "None" : $"{bone.ParentIndex}: {rows.FirstOrDefault(b => b.Index == bone.ParentIndex)?.Name}");
        var position = RagdollVector("RagdollBone", "Position", new(bone.X, bone.Y, bone.Z));
        var angles = BoneRotationAngles.ToRadians(new(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW));
        var rotation = RagdollVector("RagdollBoneRotation", "Rotation (rad)", angles);
        RagdollText("Scale", $"{bone.ScaleX:G6}, {bone.ScaleY:G6}, {bone.ScaleZ:G6}");
        DetailApply("ApplyRagdollBone", () => Edit((doc, _) => {
            var p = RagdollValue(position, new(bone.X, bone.Y, bone.Z));
            Vector3? r = RagdollChanged(rotation, angles) ? RagdollValue(rotation, angles) : null;
            if (model) RagdollEdits.ModelBone(doc, bone.Index, name.Text ?? "", p, r); else RagdollEdits.Bone(doc, bone.Index, name.Text ?? "", p, r);
        }));
        var targets = model ? Session.Document.GetBoneRows(0) : Session.Document.GetHkrgModelBoneRows();
        var target = new ComboBox { Name = "RagdollAlignTarget", ItemsSource = targets.Select(b => new RagdollBoneTarget(b.Index, b.Name)).ToArray(),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        target.SelectedIndex = targets.ToList().FindIndex(b => b.Name == bone.Name);
        DetailRow(_details, model ? "Ragdoll target" : "Model target", target);
        var align = new Button { Name = "AlignRagdollBone", Content = "Align to bone", IsEnabled = target.SelectedItem != null };
        ToolTip.SetTip(align, "Match the target's world position and rotation. Existing joint and skeleton-mapper offsets are updated; no new mapping is created.");
        target.SelectionChanged += (_, _) => align.IsEnabled = target.SelectedItem != null;
        align.Click += (_, _) => { if (target.SelectedItem is RagdollBoneTarget t) Try(() => Edit((doc, _) => RagdollEdits.AlignBone(doc, bone.Index, model, t.Index))); };
        _details.Children.Add(align);
    }
    private sealed record RagdollBoneTarget(int Index, string Name) { public override string ToString() => $"{Index}: {Name}"; }
    private void ShowRagdollBody(ColliderEditRow body)
    {
        RagdollText("Shape", body.ShapeType); RagdollText("Bone", $"{body.BoneIndex}: {body.BoneName}");
        if (body.ShapeType is not ("Capsule" or "Sphere")) { RagdollText("Editing", "This rigid-body shape is inspect-only."); return; }
        var affine = RagdollEdits.Affine(body.Transform);
        if (!Matrix4x4.Invert(affine, out var inverse)) { RagdollText("Editing", "Singular body transform."); return; }
        var name = new TextBox { Name = "RagdollBodyName", Text = body.Name }; DetailRow(_details, "Name", name);
        var position = RagdollVector("RagdollBody", "World position", affine.Translation);
        var angles = BoneRotationAngles.ToRadians(Quaternion.CreateFromRotationMatrix(affine));
        var rotation = RagdollVector("RagdollBodyRotation", "Rotation (rad)", angles);
        var radius = DetailNumber(_details, "RagdollRadius", "Radius", body.Radius, 0);
        var startValue = Vector3.Transform(new(body.StartX, body.StartY, body.StartZ), inverse);
        var endValue = Vector3.Transform(new(body.EndX, body.EndY, body.EndZ), inverse);
        (NumericUpDown X, NumericUpDown Y, NumericUpDown Z)? start = null, end = null;
        if (body.ShapeType == "Capsule") { start = RagdollVector("RagdollStart", "Start (local)", startValue); end = RagdollVector("RagdollEnd", "End (local)", endValue); }
        DetailApply("ApplyRagdollBody", () => Edit((doc, _) => RagdollEdits.Body(doc, body.Index, name.Text ?? "", RagdollValue(position, affine.Translation),
            RagdollChanged(rotation, angles) ? RagdollValue(rotation, angles) : null, start.HasValue ? RagdollValue(start.Value, startValue) : startValue, end.HasValue ? RagdollValue(end.Value, endValue) : endValue,
            radius.Value == (decimal)body.Radius ? body.Radius : DetailValue(radius))));
    }
}
