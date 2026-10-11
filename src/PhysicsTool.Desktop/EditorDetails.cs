using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private readonly StackPanel _relationshipDetails = new() { Spacing = 8, Margin = new Thickness(12, 0, 0, 8) };
    private sealed record ParentChoice(int Index, string Name) { public override string ToString() => Index < 0 ? (Name.Length == 0 ? "None (root)" : Name) : $"{Index}: {Name}"; }

    private static void DetailRow(StackPanel host, string label, Control input)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*") };
        grid.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        input.HorizontalAlignment = HorizontalAlignment.Stretch; Grid.SetColumn(input, 1); grid.Children.Add(input); host.Children.Add(grid);
    }
    private static NumericUpDown DetailNumber(StackPanel host, string id, string label, float value, decimal minimum = -1000000)
    {
        var input = new NumericUpDown { Name = id, Value = (decimal)value, Minimum = Math.Min(minimum, (decimal)value), Maximum = Math.Max(1000000, (decimal)value), Increment = .01m, FormatString = "0.######" };
        DetailRow(host, label, input); return input;
    }
    private static float DetailValue(NumericUpDown input) => input.Value is { } value ? (float)value : throw new InvalidOperationException("Enter a value for every numeric field.");

    private void ShowBoneDetails(BoneEditRow bone)
    {
        var rows = Session.Document.GetBoneRows(Session.ClothIndex);
        var descendants = new HashSet<int> { bone.Index };
        while (true) { var count = descendants.Count; foreach (var row in rows) if (descendants.Contains(row.ParentIndex)) descendants.Add(row.Index); if (count == descendants.Count) break; }
        var name = new TextBox { Name = "EditorBoneName", Text = bone.Name }; DetailRow(_details, "Name", name);
        var choices = new[] { new ParentChoice(-1, "") }.Concat(rows.Where(b => !descendants.Contains(b.Index)).Select(b => new ParentChoice(b.Index, b.Name))).ToArray();
        var parent = new ComboBox { Name = "EditorBoneParent", ItemsSource = choices, SelectedItem = choices.FirstOrDefault(p => p.Index == bone.ParentIndex) };
        ToolTip.SetTip(parent, "Parent changes keep the local pose. Descendant world poses, bound colliders and optionally weighted particles follow it.");
        DetailRow(_details, "Parent", parent);
        var x = DetailNumber(_details, "EditorX", "X", bone.X); var y = DetailNumber(_details, "EditorY", "Y", bone.Y); var z = DetailNumber(_details, "EditorZ", "Z", bone.Z);
        var angles = BoneRotationAngles.ToRadians(new(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW));
        var rx = DetailNumber(_details, "EditorRotationX", "Rot X (rad)", angles.X); var ry = DetailNumber(_details, "EditorRotationY", "Rot Y (rad)", angles.Y); var rz = DetailNumber(_details, "EditorRotationZ", "Rot Z (rad)", angles.Z);
        foreach (var input in new[] { rx, ry, rz }) ToolTip.SetTip(input, "Local Euler XYZ radians; saved and simulated as a quaternion.");
        var sx = DetailNumber(_details, "EditorScaleX", "Scale X", bone.ScaleX); var sy = DetailNumber(_details, "EditorScaleY", "Scale Y", bone.ScaleY); var sz = DetailNumber(_details, "EditorScaleZ", "Scale Z", bone.ScaleZ);
        var error = new TextBlock { Name = "BoneEditError", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed }; _details.Children.Add(error);
        var apply = new Button { Name = "ApplyBoneDetails", Content = "Apply", HorizontalAlignment = HorizontalAlignment.Stretch }; _details.Children.Add(apply);
        apply.Click += (_, _) => {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("A bone name is required.");
                if (parent.SelectedItem is not ParentChoice choice) throw new InvalidOperationException("Choose a valid parent.");
                var position = new Vector3(DetailValue(x), DetailValue(y), DetailValue(z));
                var rotation = new Vector3(DetailValue(rx), DetailValue(ry), DetailValue(rz));
                var scale = new Vector3(DetailValue(sx), DetailValue(sy), DetailValue(sz));
                if (scale.X == 0 || scale.Y == 0 || scale.Z == 0) throw new InvalidOperationException("Bone scale cannot be zero.");
                Edit((doc, cloth) => {
                    var row = doc.GetBoneRows(cloth).Single(b => b.Index == bone.Index);
                    row.Name = name.Text.Trim(); row.ParentIndex = choice.Index;
                    row.X = position.X; row.Y = position.Y; row.Z = position.Z;
                    row.ScaleX = scale.X; row.ScaleY = scale.Y; row.ScaleZ = scale.Z;
                    if (rx.Value != (decimal)angles.X || ry.Value != (decimal)angles.Y || rz.Value != (decimal)angles.Z)
                    {
                        var q = BoneRotationAngles.FromRadians(rotation);
                        row.RotationX = q.X; row.RotationY = q.Y; row.RotationZ = q.Z; row.RotationW = q.W;
                    }
                    doc.UpdateBoneRows(cloth, [row], _follow.IsChecked == true);
                });
            }
            catch (Exception ex) { error.Text = ex.Message; _status.Text = ex.Message; }
        };
    }

    private void ShowRelationshipDetails()
    {
        _relationshipDetails.Children.Clear();
        if (_relationships.SelectedItem is not Relation relation || _items.SelectedItem is not Item particle) return;
        var row = relation.Data;
        _relationshipDetails.Children.Add(new TextBlock { Text = row.Name, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        _relationshipDetails.Children.Add(new TextBlock { Text = "Particles: " + row.Particles, TextWrapping = TextWrapping.Wrap });
        var inputs = new Dictionary<string, NumericUpDown>();
        foreach (var field in RelationshipEdits.Fields(row, Session.Document.IsBphcl))
        {
            if (field.Editable && Session.Document.CanEditConstraintValues)
            {
                var input = DetailNumber(_relationshipDetails, "Relation" + field.Property, field.Label, field.Value, 0);
                if (field.Property.Contains("Stiffness")) ToolTip.SetTip(input, "Native coefficient, not a percentage. Zero disables this response.");
                inputs.Add(field.Property, input);
            }
            else DetailRow(_relationshipDetails, field.Label, new TextBlock { Text = field.Value.ToString("G7", System.Globalization.CultureInfo.InvariantCulture) });
        }
        if (!string.IsNullOrWhiteSpace(row.Details)) _relationshipDetails.Children.Add(new TextBlock { Text = row.Details, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        if (inputs.Count == 0) return;
        var error = new TextBlock { Name = "RelationshipEditError", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed }; _relationshipDetails.Children.Add(error);
        var apply = new Button { Name = "ApplyRelationship", Content = "Apply", HorizontalAlignment = HorizontalAlignment.Stretch }; _relationshipDetails.Children.Add(apply);
        apply.Click += (_, _) => {
            try { var values = inputs.ToDictionary(p => p.Key, p => DetailValue(p.Value)); Edit((doc, cloth) => RelationshipEdits.Apply(doc, cloth, particle.Index, row, values)); }
            catch (Exception ex) { error.Text = ex.Message; _status.Text = ex.Message; }
        };
    }
}
