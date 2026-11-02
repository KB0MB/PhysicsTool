using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

internal sealed partial class ShrineSceneView
{
    private void ShowController(int index)
    {
        _search.Text = ""; _groundOnly.IsChecked = false;
        SetSelection([index]);
        if (_actors.SelectedItem != null) _actors.ScrollIntoView(_actors.SelectedItem);
    }

    private void GroundControlInspector(ShrineActor actor)
    {
        if (_editing?.GroundControl(actor.Index) is not { } group) return;
        Label($"Group {actor.GroupId} / collision body {group.Body}");
        if (group.Restriction != null) Label(group.Restriction);
        var bounds = group.Members.Select(i => _picking?.Bounds(i)).Where(b => b != null).Select(b => b!.Value).ToArray();
        if (bounds.Length > 0) _view.ModelSelectionBounds = (bounds.Select(b => b.Minimum).Aggregate(Vector3.Min), bounds.Select(b => b.Maximum).Aggregate(Vector3.Max));

        var memberPanel = new StackPanel { Spacing = 5 };
        foreach (int index in group.Members.Where(i => i != actor.Index))
        {
            var member = _editing.Scene.Actors[index];
            var button = Command(member.ToString(), () => { ShowController(index); return Task.CompletedTask; });
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            ToolTip.SetTip(button, $"Select member / 0x{member.Hash:x16}"); memberPanel.Children.Add(button);
        }
        var selectMembers = Command("Select members", () =>
        {
            _search.Text = ""; _groundOnly.IsChecked = false;
            SetSelection(group.Members.Where(i => i != actor.Index)); return Task.CompletedTask;
        });
        selectMembers.IsEnabled = group.Members.Count > 1; memberPanel.Children.Add(selectMembers);
        _details.Children.Add(new Expander { Header = $"Members ({Math.Max(0, group.Members.Count - 1)})", Content = memberPanel,
            HorizontalAlignment = HorizontalAlignment.Stretch });

        NumericUpDown[] Fields(Panel parent, string name, string label, IReadOnlyList<float> values, string tip, bool integer = false)
        {
            var heading = new TextBlock { Text = label }; ToolTip.SetTip(heading, tip); parent.Children.Add(heading);
            var fields = new NumericUpDown[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                var row = new Grid { ColumnDefinitions = new(values.Count == 3 ? "24,*" : "0,*") };
                if (values.Count == 3) row.Children.Add(new TextBlock { Text = "XYZ"[i].ToString(), VerticalAlignment = VerticalAlignment.Center });
                var field = new NumericUpDown { Name = name + (values.Count == 3 ? "XYZ"[i] : ""), Value = (decimal)values[i],
                    FormatString = integer ? "0" : "0.#####", Increment = integer ? 1 : .1m, ShowButtonSpinner = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch };
                ToolTip.SetTip(field, tip); fields[i] = field; Grid.SetColumn(field, 1); row.Children.Add(field); parent.Children.Add(row);
            }
            return fields;
        }
        float[] Read(NumericUpDown[] fields)
        {
            if (fields.Any(f => f.Value == null)) throw new ArgumentException("Enter a value in every GroundControl field.");
            return fields.Select(f => (float)f.Value!.Value).ToArray();
        }

        Label("Movement", 17);
        var parameterFields = new Dictionary<string, NumericUpDown[]>();
        var values = _editing.GroundControlValues(actor.Index);
        foreach (var parameter in ShrinePlacementEditor.GroundControlParameters(actor.Name))
            parameterFields[parameter.Key] = Fields(_details, "Ground" + parameter.Key, parameter.Label,
                values[parameter.Key], parameter.Hint, parameter.Integer);
        if (parameterFields.Count == 0) Label("This controller's movement parameters are not editable yet.");
        Label("Existing triggers and rails are preserved. Movement that depends on scene logic still needs its original activation.");
        var motion = Command("Apply movement", () =>
        {
            _editing.SetGroundControl(actor.Index, ShrineTransform.Local(actor), false, checked((ushort)actor.GroupId), actor.GroupGuid,
                parameterFields.ToDictionary(p => p.Key, p => Read(p.Value)));
            RefreshEdit(); _status.Text = "GroundControl movement settings updated. Existing activation logic was preserved.";
            return Task.CompletedTask;
        });
        motion.Name = "GroundMovementApply"; motion.IsEnabled = _editing.GroundControlParameterRestriction(actor.Index) == null && parameterFields.Count > 0; _details.Children.Add(motion);

        Label("Pivot", 17);
        var position = Fields(_details, "GroundPivot", "Position", [actor.LocalPosition.X, actor.LocalPosition.Y, actor.LocalPosition.Z], "Starting position of the controller's collision frame.");
        var rotation = Fields(_details, "GroundRotation", "Rotation (radians)", [actor.Rotation.X, actor.Rotation.Y, actor.Rotation.Z], "Orients the controller's local axes. Travel and turning use those axes.");
        var moveMembers = new CheckBox { Name = "GroundMoveMembers", Content = "Move members with pivot", IsChecked = group.MoveRestriction == null,
            IsEnabled = group.MoveRestriction == null };
        ToolTip.SetTip(moveMembers, group.MoveRestriction ?? "Checked: reposition the whole platform. Unchecked: change the pivot while keeping models and collision in their current world positions.");
        ToolTip.SetShowOnDisabled(moveMembers, true); _details.Children.Add(moveMembers);
        if (group.MoveRestriction != null && group.Restriction == null) Label(group.MoveRestriction);
        var pivot = Command("Apply pivot", () =>
        {
            var p = Read(position); var r = Read(rotation);
            _editing.SetGroundControl(actor.Index, new(new(p[0], p[1], p[2]), new(r[0], r[1], r[2]), actor.Scale),
                moveMembers.IsChecked == true, checked((ushort)actor.GroupId), actor.GroupGuid, new Dictionary<string, float[]>());
            RefreshEdit(); _status.Text = "GroundControl pivot and collision frame updated."; return Task.CompletedTask;
        });
        pivot.Name = "GroundPivotApply"; pivot.IsEnabled = group.Restriction == null; _details.Children.Add(pivot);

        var identity = new StackPanel { Spacing = 8 };
        identity.Children.Add(new TextBlock { Text = "GroupID" });
        var id = new NumericUpDown { Name = "GroundGroupId", Value = actor.GroupId, Minimum = 1, Maximum = ushort.MaxValue, Increment = 1,
            FormatString = "0", ShowButtonSpinner = false }; identity.Children.Add(id);
        identity.Children.Add(new TextBlock { Text = "GroupGUID (hex)" });
        var guid = new TextBox { Name = "GroundGroupGuid", Text = $"{actor.GroupGuid:x16}" }; identity.Children.Add(guid);
        var reassign = Command("Apply group IDs", () =>
        {
            string text = guid.Text?.Trim() ?? ""; if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            if (!ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong value) || id.Value == null || id.Value != decimal.Truncate(id.Value.Value))
                throw new ArgumentException("Enter a whole GroupID and a valid hexadecimal GroupGUID.");
            _editing.SetGroundControl(actor.Index, ShrineTransform.Local(actor), false, checked((ushort)id.Value.Value), value, new Dictionary<string, float[]>());
            RefreshEdit(); _status.Text = "Group identifiers updated in BANC members and BPHSC collision links."; return Task.CompletedTask;
        });
        reassign.Name = "GroundIdsApply"; reassign.IsEnabled = group.Restriction == null; identity.Children.Add(reassign);
        _details.Children.Add(new Expander { Header = "Group identifiers", Content = identity, HorizontalAlignment = HorizontalAlignment.Stretch });
    }
}
