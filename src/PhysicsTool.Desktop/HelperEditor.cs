using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private readonly WrapPanel _helperActions = new();
    private readonly CheckBox _evaluateHelpers = new() { Name = "EvaluateHelpers", Content = "Evaluate helpers", IsChecked = true };
    private Button _helperCreate = null!, _helperDuplicate = null!, _helperMirror = null!, _helperUp = null!, _helperDown = null!, _helperRemove = null!, _helperDeleteIndividual = null!;
    private bool CanEditHelper => !IsActorMode && !_timer.IsEnabled && Session.Document.IsHelperBoneSidecar && Session.HasSelectedEntry;
    private static string HelperRole(SidecarDriverGroup? group, int index) =>
        (group?.DriverBoneIndices.Contains(index) == true, group?.DrivenBoneIndices.Contains(index) == true) switch
        { (true, true) => "Input + affected", (true, false) => "Input", (false, true) => "Affected", _ => "Referenced" };

    private void ConfigureHelperEditor(WrapPanel footer)
    {
        _helperCreate = Add(_helperActions, "Create Support", CreateHelperSupport); _helperCreate.Name = "HelperCreate";
        _helperDuplicate = Add(_helperActions, "Duplicate", () => EditHelperBone(d => d.DuplicateHelperBone(SelectedHelperIndex))); _helperDuplicate.Name = "HelperDuplicate";
        _helperMirror = Add(_helperActions, "Mirror group X", () => { if (CanEditHelper) Edit((d, c) => d.MirrorHelperGroupAcrossX(c)); }); _helperMirror.Name = "HelperMirror";
        _helperUp = ViewportTools.Icon("ArrowUp", "Move helper record up", () => Try(() => MoveHelper(-1))); _helperUp.Name = "HelperUp"; _helperActions.Children.Add(_helperUp);
        _helperDown = ViewportTools.Icon("ArrowDown", "Move helper record down", () => Try(() => MoveHelper(1))); _helperDown.Name = "HelperDown"; _helperActions.Children.Add(_helperDown);
        _helperRemove = Add(_helperActions, "Delete group", async () => { if (CanEditHelper) await RunCommand(RemoveCloth); }); _helperRemove.Name = "HelperRemoveGroup";
        _helperDeleteIndividual = Add(_helperActions, "Delete helper", async () => await RunCommand(DeleteIndividualHelper)); _helperDeleteIndividual.Name = "HelperDeleteIndividual";
        ToolTip.SetTip(_helperDeleteIndividual, "Remove only the selected bone's affected helper behavior. Keep its model bone, input role and shared graph resources.");
        ToolTip.SetTip(_helperCreate, "Clone the selected affected bone's connected graph for a new or existing target bone.");
        ToolTip.SetTip(_helperDuplicate, "Duplicate the selected native helper record and its supported connections.");
        ToolTip.SetTip(_helperMirror, "Mirror every input and affected base pose in this group across X.");
        ToolTip.SetTip(_helperRemove, "Remove this complete driver group and its dependent curves and outputs. Shared references are preserved.");
        footer.Children.Add(_helperActions);
        ToolTip.SetTip(_evaluateHelpers, "Evaluate the helper graph against the imported model and its current animation. This does not change saved poses.");
        _evaluateHelpers.IsCheckedChanged += (_, _) => { UpdateImportedModelPose(); _viewport.InvalidateVisual(); };
        footer.Children.Add(_evaluateHelpers);
    }

    private int SelectedHelperIndex => (_items.SelectedItem as Item)?.Index ?? -1;
    private void EditHelperBone(Action<HkclService> action)
    {
        if (!CanEditHelper || SelectedHelperIndex < 0) return;
        Edit((d, _) => action(d));
    }
    private async Task CreateHelperSupport()
    {
        if (!CanEditHelper || _items.SelectedItem is not Item { Data: BoneEditRow bone } ||
            Session.Document.GetSidecarDriverGroup(Session.ClothIndex)?.DrivenBoneIndices.Contains(bone.Index) != true) return;
        var name = await Dialogs.Prompt(this, "Create support bone", "Target bone name", bone.Name + "_Support", "Create");
        if (string.IsNullOrWhiteSpace(name)) return;
        var target = -1;
        EditHelperBone(d => target = d.CreateSupportFromBone(bone.Index, name));
        SelectHelper(target);
    }
    private void SelectHelper(int index)
    {
        _items.SelectedItem = _items.ItemsSource?.Cast<Item>().FirstOrDefault(i => i.Index == index);
    }
    private void MoveHelper(int direction)
    {
        if (!CanEditHelper || _items.SelectedItem is not Item selected) return;
        var rows = _items.ItemsSource!.Cast<Item>().ToArray(); var position = Array.IndexOf(rows, selected);
        if (position + direction < 0 || position + direction >= rows.Length) return;
        var destination = rows[position + direction].Index;
        EditHelperBone(d => d.MoveHelperBone(selected.Index, destination)); SelectHelper(destination);
    }
    private void UpdateHelperActions()
    {
        var helper = Session.Document.IsHelperBoneSidecar && !IsActorMode;
        var limited = helper || Session.Document.IsHkrg && !IsActorMode;
        _helperActions.IsVisible = helper;
        _evaluateHelpers.IsVisible = helper;
        _evaluateHelpers.IsEnabled = _importedModel != null;
        _simulate.IsVisible = _wind.IsVisible = _options.IsVisible = !helper;
        _add.IsVisible = !limited && !IsActorMode; _delete.IsVisible = !limited;
        _follow.IsVisible = _massScale.IsVisible = _clothSettings.IsVisible = _attach.IsVisible = !limited;
        _helperActions.IsEnabled = CanEditHelper;
        var selected = SelectedHelperIndex;
        _helperCreate.IsEnabled = CanEditHelper && Session.Document.GetSidecarDriverGroup(Session.ClothIndex)?.DrivenBoneIndices.Contains(selected) == true;
        _helperDeleteIndividual.IsEnabled = _helperCreate.IsEnabled;
        _helperDuplicate.IsEnabled = CanEditHelper && selected >= 0;
        _helperUp.IsEnabled = CanEditHelper && _items.SelectedIndex > 0;
        _helperDown.IsEnabled = CanEditHelper && _items.SelectedIndex >= 0 && _items.SelectedIndex < _items.ItemCount - 1;
        if (helper) _viewport.CanSelect = !_timer.IsEnabled;
    }

    private void ShowHelperDetails(BoneEditRow bone)
    {
        var group = Session.Document.GetSidecarDriverGroup(Session.ClothIndex);
        DetailRow(_details, "Role", new TextBlock { Text = HelperRole(group, bone.Index), TextWrapping = TextWrapping.Wrap });
        DetailRow(_details, "Group", new TextBlock { Text = $"{group?.Index}: {group?.DisplayName}", TextWrapping = TextWrapping.Wrap });
        var name = new TextBox { Name = "HelperName", Text = bone.Name }; DetailRow(_details, "Name", name);
        var specialFrames = Session.Document.IsBphyssb
            ? new[] { new ParentChoice(-1, "Model parent"), new ParentChoice(-2, "Model space") }
            : new[] { new ParentChoice(-1, "Default reference") };
        var choices = specialFrames.Concat(Session.Document.GetSidecarBoneRows().Where(b => b.Index != bone.Index).Select(b => new ParentChoice(b.Index, b.Name))).ToArray();
        var parent = new ComboBox { Name = "HelperBase", ItemsSource = choices, SelectedItem = choices.FirstOrDefault(p => p.Index == bone.ParentIndex) };
        DetailRow(_details, "Base bone", parent); ToolTip.SetTip(parent, "Reference frame used by the helper graph, not skeleton reparenting. BotW Model parent uses the model hierarchy (-1); Model space is absolute (-2). Changing the frame retains the entered local pose.");
        var poseEditable = group?.DriverBoneIndices.Contains(bone.Index) == true || group?.DrivenBoneIndices.Contains(bone.Index) == true;
        var x = DetailNumber(_details, "HelperX", "Base X", bone.X); var y = DetailNumber(_details, "HelperY", "Base Y", bone.Y); var z = DetailNumber(_details, "HelperZ", "Base Z", bone.Z);
        var angles = BoneRotationAngles.ToRadians(new(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW));
        var rx = DetailNumber(_details, "HelperRX", "Rot X (rad)", angles.X); var ry = DetailNumber(_details, "HelperRY", "Rot Y (rad)", angles.Y); var rz = DetailNumber(_details, "HelperRZ", "Rot Z (rad)", angles.Z);
        foreach (var input in new Control[] { parent, x, y, z, rx, ry, rz }) { input.IsEnabled = poseEditable; if (!poseEditable) ToolTip.SetTip(input, "This referenced bone has no authored pose in this group."); }
        DetailApply("ApplyHelperDetails", () => {
            if (!CanEditHelper) throw new InvalidOperationException("Pause playback before editing a helper group.");
            if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("A bone name is required.");
            if (poseEditable && parent.SelectedItem is not ParentChoice) throw new ArgumentException("Choose a valid base bone.");
            Edit((doc, cloth) => {
                var row = doc.GetBoneRows(cloth).Single(b => b.Index == bone.Index); row.Name = name.Text.Trim();
                if (poseEditable)
                {
                    row.ParentIndex = ((ParentChoice)parent.SelectedItem!).Index;
                    row.X = DetailValue(x); row.Y = DetailValue(y); row.Z = DetailValue(z);
                    if (rx.Value != (decimal)angles.X || ry.Value != (decimal)angles.Y || rz.Value != (decimal)angles.Z)
                    {
                        var q = BoneRotationAngles.FromRadians(new(DetailValue(rx), DetailValue(ry), DetailValue(rz)));
                        row.RotationX = q.X; row.RotationY = q.Y; row.RotationZ = q.Z; row.RotationW = q.W;
                    }
                }
                doc.UpdateBoneRows(cloth, [row]);
            });
        });
        if (group?.DrivenBoneIndices.Contains(bone.Index) == true) ShowHelperConnections(bone);
        ShowHelperCurves();
    }

    private void PoseHelperModel(VisualModelData animated)
    {
        if (_importedModel == null) return;
        var evaluation = _evaluateHelpers.IsChecked == true ? HelperPoseEvaluation.Evaluate(Session.Document, animated.Bones, _importedModel.Rest.Bones) : default;
        var poses = evaluation.Poses ?? animated.Bones.Select(b => new VisualBoneOverride(b.Name, b.Position, b.Rotation, b.Scale)).ToArray();
        ToolTip.SetTip(_evaluateHelpers, evaluation.Missing?.Count > 0
            ? "Model is missing helper drivers: " + string.Join(", ", evaluation.Missing)
            : "Evaluate the helper graph against the model's current animation without changing saved poses.");
        _viewport.SetModelPose(_importedModel.Sample(poses));
        var displayed = _viewport.Model!.Bones.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _viewport.Simulated = null; _viewport.SimulatedColliders = null;
        _viewport.SimulatedBones = _viewport.Data.Bones.ToDictionary(b => b.Index, b => displayed.TryGetValue(b.Name, out var v)
            ? new SimulatedBonePreviewPose { Position = v.Position, AxisX = v.AxisX, AxisY = v.AxisY, AxisZ = v.AxisZ }
            : new SimulatedBonePreviewPose { Position = b.Position, AxisX = b.AxisX, AxisY = b.AxisY, AxisZ = b.AxisZ });
    }
}
