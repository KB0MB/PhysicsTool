using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private sealed record ColliderBoneChoice(int Index, string Name)
    {
        public override string ToString() => Index < 0 ? "None" : $"{Index}: {Name}";
    }

    private void DetailApply(string id, Action action)
    {
        var error = new TextBlock { Name = id + "Error", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
        var apply = new Button { Name = id, Content = "Apply", HorizontalAlignment = HorizontalAlignment.Stretch };
        _details.Children.Add(error); _details.Children.Add(apply);
        apply.Click += (_, _) => { try { action(); } catch (Exception ex) { error.Text = ex.Message; _status.Text = ex.Message; } };
    }

    private void ShowParticleDetails(ParticleEditRow particle)
    {
        var bphcl = Session.Document.IsBphcl;
        var fixedBox = new CheckBox { Name = "EditorFixed", Content = "Fixed", IsChecked = particle.Fixed };
        fixedBox.IsCheckedChanged += (_, _) => Try(() => Edit((doc, cloth) => {
            var row = doc.GetParticleRows(cloth).Single(p => p.Index == particle.Index);
            row.Fixed = fixedBox.IsChecked == true;
            row.Mass = row.Fixed ? 0 : row.Mass > 0 ? row.Mass : .1f;
            row.InverseMass = row.Fixed ? 0 : 1 / row.Mass;
            ParticleEdits.Apply(doc, cloth, [row]);
        }));
        _details.Children.Add(fixedBox);
        var x = DetailNumber(_details, "EditorX", "X", particle.X);
        var y = DetailNumber(_details, "EditorY", "Y", particle.Y);
        var z = DetailNumber(_details, "EditorZ", "Z", particle.Z);
        var mass = DetailNumber(_details, "EditorMass", "Mass", particle.Mass, 0);
        var inverse = DetailNumber(_details, "EditorInverseMass", "Inverse mass", particle.InverseMass, 0);
        mass.IsEnabled = !particle.Fixed; inverse.IsEnabled = !particle.Fixed && !bphcl;
        ToolTip.SetTip(inverse, bphcl ? "BPHCL derives inverse mass from mass when saved." : "Changing mass sets its reciprocal. HKCL also permits a separate inverse-mass edit.");
        mass.ValueChanged += (_, _) => { if (mass.Value is { } value && value > 0) { var reciprocal = 1 / value; inverse.Maximum = Math.Max(inverse.Maximum, reciprocal); inverse.Value = reciprocal; } };
        var radius = DetailNumber(_details, "EditorRadius", "Radius", particle.Radius, 0);
        var friction = DetailNumber(_details, "EditorFriction", "Friction", particle.Friction, 0);
        DetailRow(_details, "Mask", new TextBlock { Name = "EditorCollisionMask", Text = $"0x{particle.CollisionMask:X8}" });
        var choices = Session.Document.GetParticleColliderOptions(Session.ClothIndex);
        var checks = new List<(ParticleColliderOption Option, CheckBox Check)>();
        var slots = new StackPanel { Spacing = 4 };
        foreach (var option in choices)
        {
            var check = new CheckBox { Name = "ColliderSlot" + option.BitIndex,
                Content = new TextBlock { Text = $"{option.BitIndex}: {option.Name}", TextWrapping = TextWrapping.Wrap },
                IsChecked = (particle.CollisionMask & (1u << option.BitIndex)) != 0 };
            ToolTip.SetTip(check, $"Cloth collision slot {option.BitIndex}, collider {option.ColliderIndex}. Repeated colliders can occupy separate mask bits.");
            slots.Children.Add(check); checks.Add((option, check));
        }
        var expander = new Expander { Name = "ParticleCollisionAssignments", Header = $"Colliders ({checks.Count(c => c.Check.IsChecked == true)}/{choices.Count})", Content = slots, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = choices.Count > 0 };
        foreach (var (_, check) in checks) check.IsCheckedChanged += (_, _) => expander.Header = $"Colliders ({checks.Count(c => c.Check.IsChecked == true)}/{choices.Count})";
        _details.Children.Add(expander);
        ToolTip.SetTip(expander, "Select collision slots for this particle. Unlisted mask bits, including landscape collision, are preserved. Apply commits the selection.");
        DetailApply("ApplyParticleDetails", () => {
            var position = new Vector3(DetailValue(x), DetailValue(y), DetailValue(z));
            var m = DetailValue(mass); var inv = DetailValue(inverse); var r = DetailValue(radius); var f = DetailValue(friction);
            if (m < 0 || inv < 0 || r < 0 || f < 0 || (!particle.Fixed && (m <= 0 || !float.IsFinite(1 / m))))
                throw new ArgumentException("A free particle needs positive mass; mass, inverse mass, radius and friction cannot be negative.");
            Edit((doc, cloth) => {
                var row = doc.GetParticleRows(cloth).Single(p => p.Index == particle.Index);
                row.X = position.X; row.Y = position.Y; row.Z = position.Z;
                row.Mass = m; row.InverseMass = bphcl ? row.Fixed ? 0 : 1 / m : inv; row.Radius = r; row.Friction = f;
                foreach (var (option, check) in checks) { var bit = 1u << option.BitIndex; row.CollisionMask = check.IsChecked == true ? row.CollisionMask | bit : row.CollisionMask & ~bit; }
                ParticleEdits.Apply(doc, cloth, [row]);
            });
        });
    }

    private void ShowColliderDetails(ColliderEditRow collider)
    {
        var bphcl = Session.Document.IsBphcl;
        DetailRow(_details, "Shape", new TextBlock { Text = collider.ShapeType, TextWrapping = TextWrapping.Wrap });
        var name = new TextBox { Name = "EditorColliderName", Text = collider.Name }; DetailRow(_details, "Name", name);
        var bones = Session.Document.GetBoneRows(Session.ClothIndex);
        var options = new[] { new ColliderBoneChoice(-1, "") }.Concat(bones.Select(b => new ColliderBoneChoice(b.Index, b.Name))).ToList();
        if (!options.Any(b => b.Index == collider.BoneIndex)) options.Add(new(collider.BoneIndex, collider.BoneName));
        var binding = new ComboBox { Name = "EditorColliderBone", ItemsSource = options, SelectedItem = options.First(b => b.Index == collider.BoneIndex), IsEnabled = !bphcl };
        ToolTip.SetTip(binding, bphcl ? "BPHCL collider rebinding is not supported." : "Reassigns the bone while preserving the collider's current position. Attach to Bone snaps it instead. Native rebinding updates the collider name.");
        DetailRow(_details, "Bone", binding);
        // Havok stores padding in the fourth column of its affine transform.
        var affine = collider.Transform;
        affine.M14 = affine.M24 = affine.M34 = 0; affine.M44 = 1;
        if (!Matrix4x4.Invert(affine, out var inverse))
        {
            _details.Children.Add(new TextBlock { Text = "This collider has a singular transform and cannot be edited.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed });
            return;
        }
        var start = new Vector3(collider.StartX, collider.StartY, collider.StartZ);
        var end = new Vector3(collider.EndX, collider.EndY, collider.EndZ);
        var localStart = collider.IsPlane ? start : Vector3.Transform(start, inverse);
        var localEnd = Vector3.Transform(end, inverse);
        var taper = bphcl && collider.ShapeType.Contains("Tapered", StringComparison.OrdinalIgnoreCase);
        var sx = DetailNumber(_details, "EditorStartX", collider.IsPlane ? "Position X" : "Start X (local)", localStart.X);
        var sy = DetailNumber(_details, "EditorStartY", collider.IsPlane ? "Position Y" : "Start Y (local)", localStart.Y);
        var sz = DetailNumber(_details, "EditorStartZ", collider.IsPlane ? "Position Z" : "Start Z (local)", localStart.Z);
        foreach (var input in new[] { sx, sy, sz }) { input.IsEnabled = !taper; if (taper) ToolTip.SetTip(input, "Native BPHCL tapered shape editing is not supported."); }
        NumericUpDown? ex = null, ey = null, ez = null, radius = null;
        if (collider.IsPlane)
        {
            DetailRow(_details, "Normal X", new TextBlock { Text = collider.PlaneNormalX.ToString("G7") });
            DetailRow(_details, "Normal Y", new TextBlock { Text = collider.PlaneNormalY.ToString("G7") });
            DetailRow(_details, "Normal Z", new TextBlock { Text = collider.PlaneNormalZ.ToString("G7") });
        }
        else
        {
            if (!collider.ShapeType.Contains("Sphere", StringComparison.OrdinalIgnoreCase))
            {
                ex = DetailNumber(_details, "EditorEndX", "End X (local)", localEnd.X);
                ey = DetailNumber(_details, "EditorEndY", "End Y (local)", localEnd.Y);
                ez = DetailNumber(_details, "EditorEndZ", "End Z (local)", localEnd.Z);
                ex.IsEnabled = ey.IsEnabled = ez.IsEnabled = !taper;
            }
            radius = DetailNumber(_details, "EditorRadius", "Radius", collider.Radius, 0); radius.IsEnabled = !taper;
        }
        DetailApply("ApplyColliderDetails", () => {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("A collider name is required.");
            var p = new Vector3(DetailValue(sx), DetailValue(sy), DetailValue(sz));
            var q = ex == null ? localEnd : new Vector3(DetailValue(ex), DetailValue(ey!), DetailValue(ez!));
            var r = radius == null ? collider.Radius : DetailValue(radius);
            if (r < 0) throw new ArgumentException("Radius cannot be negative.");
            if (binding.SelectedItem is not ColliderBoneChoice selected) throw new ArgumentException("Choose a valid bone.");
            Edit((doc, cloth) => {
                var row = doc.GetColliderRows(cloth).Single(c => c.Index == collider.Index);
                row.Name = name.Text.Trim();
                if (!bphcl) { row.BoneIndex = selected.Index; row.BoneName = selected.Name; }
                if (row.IsPlane)
                {
                    var delta = p - start;
                    row = SelectionTransforms.TransformColliders([row], new HashSet<int> { row.Index }, Matrix4x4.CreateTranslation(delta)).Single();
                }
                else if (!taper)
                {
                    var a = p == localStart ? start : Vector3.Transform(p, row.Transform);
                    var b = q == localEnd ? end : Vector3.Transform(q, row.Transform);
                    row.StartX = a.X; row.StartY = a.Y; row.StartZ = a.Z;
                    row.EndX = b.X; row.EndY = b.Y; row.EndZ = b.Z; row.Radius = r;
                }
                doc.UpdateColliderRows([row]);
            });
        });
    }
}
