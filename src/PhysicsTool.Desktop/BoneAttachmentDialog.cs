using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;

namespace PhysicsTool.Desktop;

internal sealed class BoneAttachmentDialog : Window
{
    public BoneAttachmentDialog(IReadOnlyList<BoneEditRow> bones, int preferredBone)
    {
        Title = "Attach to Bone"; Width = 480; Height = 540; MinWidth = 340; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var search = new TextBox { Name = "BoneSearch", Watermark = "Search bones", Margin = new Thickness(0, 0, 0, 12) };
        var list = new ListBox { Name = "AttachmentBones", ItemsSource = bones };
        list.ItemTemplate = new FuncDataTemplate<BoneEditRow>((bone, _) => {
            var text = new TextBlock { Text = $"{bone?.Index}: {bone?.Name}", TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(text, bone?.Name); return text;
        });
        list.SelectedItem = bones.FirstOrDefault(b => b.Index == preferredBone) ?? bones.FirstOrDefault();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var attach = new Button { Content = "Attach", IsDefault = true, IsEnabled = list.SelectedItem != null };
        cancel.Click += (_, _) => Close();
        attach.Click += (_, _) => { if (list.SelectedItem is BoneEditRow bone) Close((int?)bone.Index); };
        list.SelectionChanged += (_, _) => attach.IsEnabled = list.SelectedItem != null;
        search.TextChanged += (_, _) => {
            var selected = list.SelectedItem as BoneEditRow;
            var matches = bones.Where(b => string.IsNullOrWhiteSpace(search.Text) || b.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || b.Index.ToString() == search.Text).ToArray();
            list.ItemsSource = matches; list.SelectedItem = matches.FirstOrDefault(b => b.Index == selected?.Index) ?? matches.FirstOrDefault();
        };
        footer.Children.Add(cancel); footer.Children.Add(attach);
        layout.Children.Add(search); Grid.SetRow(list, 1); layout.Children.Add(list); Grid.SetRow(footer, 2); layout.Children.Add(footer);
        Content = layout;
    }
}
