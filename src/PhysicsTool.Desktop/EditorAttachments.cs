using HKCLTool;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private async Task AttachSelectionToBone()
    {
        if (!_attach.IsEnabled || _items.SelectedItem is not Item anchor) return;
        var selection = _items.SelectedItems!.Cast<Item>().Select(i => i.Index).ToHashSet();
        var kind = (PhysicsSelectionKind)_mode.SelectedIndex;
        var bones = Session.Document.GetBoneRows(Session.ClothIndex);
        var byIndex = bones.ToDictionary(b => b.Index);
        bool Allowed(BoneEditRow bone)
        {
            if (kind != PhysicsSelectionKind.Bones) return true;
            while (true) {
                if (selection.Contains(bone.Index)) return false;
                if (!byIndex.TryGetValue(bone.ParentIndex, out var parent)) return true;
                bone = parent;
            }
        }
        var choices = bones.Where(Allowed).ToArray();
        if (choices.Length == 0) { _status.Text = "No target bone is available outside the selection."; return; }
        var preferred = anchor.Data switch { ColliderEditRow c => c.BoneIndex, BoneEditRow b => b.ParentIndex, _ => -1 };
        var target = await new BoneAttachmentDialog(choices, preferred).ShowDialog<int?>(this);
        if (target == null) return;
        Try(() => {
            Edit((doc, cloth) => BoneAttachmentEdits.Apply(doc, cloth, kind, selection, anchor.Index, target.Value, _follow.IsChecked == true));
            _items.SelectedItems!.Clear();
            foreach (var item in _items.Items.Cast<Item>().Where(i => selection.Contains(i.Index))) _items.SelectedItems.Add(item);
            _status.Text = kind == PhysicsSelectionKind.Particles
                ? $"Snapped {selection.Count} particle(s) to {byIndex[target.Value].Name}; particle {anchor.Index} is fixed."
                : $"Attached selection to {byIndex[target.Value].Name}.";
        });
    }
}
