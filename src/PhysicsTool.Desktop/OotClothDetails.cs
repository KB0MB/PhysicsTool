using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private void ShowOotDetails(object data, int index)
    {
        if (data is BoneEditRow bone)
        {
            var x = DetailNumber(_details, "OotBoneX", "X", bone.X);
            var y = DetailNumber(_details, "OotBoneY", "Y", bone.Y);
            var z = DetailNumber(_details, "OotBoneZ", "Z", bone.Z);
            DetailApply("ApplyOotBonePosition", () =>
            {
                Session.EditOotBonePositions(new Dictionary<int, System.Numerics.Vector3> { [bone.Index] = new(DetailValue(x), DetailValue(y), DetailValue(z)) }, _follow.IsChecked == true);
                Refresh(); _status.Text = "Updated bone position and cloth rest shape.";
            }, "Apply position");
            return;
        }
        if (data is ParticleEditRow particle)
        {
            var triangle = new Button { Content = "Add triangle", Name = "AddOotTriangle", IsEnabled = SelectionIndices.Count == 3 };
            ToolTip.SetTip(triangle, "Select three particles to create a face and its edge constraints. The selection order sets the face direction.");
            triangle.Click += (_, _) => Try(() => { Session.AddOotTriangle(_items.SelectedItems!.Cast<Item>().Select(i => i.Index).ToArray()); Refresh(); });
            _details.Children.Add(triangle);
            string? unavailable = null;
            try { Session.Document.ValidateOotGeometry(Session.ClothIndex); }
            catch (Exception ex) { unavailable = ex.Message; }
            if (unavailable == null)
            {
                var x = DetailNumber(_details, "OotParticleX", "X", particle.X);
                var y = DetailNumber(_details, "OotParticleY", "Y", particle.Y);
                var z = DetailNumber(_details, "OotParticleZ", "Z", particle.Z);
                DetailApply("ApplyOotGeometry", () =>
                {
                    Session.EditOotGeometry(new Dictionary<int, System.Numerics.Vector3> { [particle.Index] = new(DetailValue(x), DetailValue(y), DetailValue(z)) });
                    Refresh(); _status.Text = "Updated particle position and cloth rest shape.";
                }, "Apply position");
            }
            else
            {
                DetailRow(_details, "Position", new TextBlock { Text = $"{particle.X:G6}, {particle.Y:G6}, {particle.Z:G6}", TextWrapping = TextWrapping.Wrap });
                _details.Children.Add(new TextBlock { Text = unavailable, TextWrapping = TextWrapping.Wrap });
            }
            DetailRow(_details, "Fixed", new CheckBox { IsChecked = particle.Fixed, IsEnabled = false });
        }
        var collider = data is ColliderEditRow;
        var properties = Session.Document.GetOotProperties(Session.ClothIndex, index, collider);
        var inputs = new Dictionary<uint, NumericUpDown>();
        if (properties.Count > 0)
            DetailApply("ApplyOotProperties", () =>
            {
                Session.EditOotProperties(index, collider, inputs.ToDictionary(p => p.Key, p => DetailValue(p.Value)));
                Refresh(); _status.Text = "Updated OoT properties.";
            }, "Apply properties");
        foreach (var group in properties.GroupBy(p => p.Group))
        {
            var panel = new StackPanel { Spacing = 8 };
            foreach (var property in group)
            {
                var input = DetailNumber(panel, "OotProperty" + property.Offset, property.Label, property.Value, (decimal)property.Minimum);
                ToolTip.SetTip(input, group.Key == "Particle" ? property.Label == "Mass" ? "Inverse mass is updated automatically. Fixed particles remain fixed." : property.Label
                    : "Changes the existing native value without rebuilding geometry. Shared records affect every particle or collider that uses them.");
                inputs[property.Offset] = input;
            }
            if (group.Key is "Particle" or "Shape") _details.Children.Add(panel);
            else _details.Children.Add(new Expander { Header = group.Key, Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        if (inputs.Count == 0) { _details.Children.Add(new TextBlock { Text = "This shape has no supported editable values yet.", TextWrapping = TextWrapping.Wrap }); return; }
    }
}
