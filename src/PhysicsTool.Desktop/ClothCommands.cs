using Avalonia.Controls;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private async Task ShowMassScale()
    {
        if (!_massScale.IsEnabled) return;
        var selection = _items.SelectedItems?.Cast<Item>().Select(item => item.Index).ToArray() ?? [];
        var count = Session.Document.GetParticleRows(Session.ClothIndex).Count(row => !row.Fixed && (selection.Length == 0 || selection.Contains(row.Index)));
        if (count == 0) { _status.Text = "Select at least one dynamic particle before scaling its mass."; return; }
        await new MassScaleWindow(count, selection.Length > 0, scale =>
        {
            var changed = Session.ScaleParticleMass(scale, selection); RefreshKeepingSelection(selection);
            _status.Text = $"Scaled mass for {changed} dynamic particle(s) by {scale:G7}.";
        }).ShowDialog(this);
    }
    private async Task ShowClothSettings()
    {
        if (!_clothSettings.IsEnabled) return;
        var selection = _items.SelectedItems?.Cast<Item>().Select(item => item.Index).ToArray() ?? [];
        await new ClothSettingsWindow(Session.Document.GetSimulationSettings(Session.ClothIndex), settings =>
        {
            Session.UpdateClothSettings(settings); RefreshKeepingSelection(selection); _status.Text = "Updated cloth simulation settings.";
        }).ShowDialog(this);
    }
    private async Task ShowPreviewOptions()
    {
        if (!_options.IsEnabled) return;
        await new PreviewOptionsWindow(SimulationOptions, options =>
        {
            options.Validate(); SimulationOptions = options;
            if (_solver != null) options.ApplyTo(_solver);
        }).ShowDialog(this);
    }
    private void RefreshKeepingSelection(int[] indices)
    {
        Refresh(); _refreshing = true;
        try
        {
            _items.SelectedItems?.Clear();
            foreach (var item in _items.Items.Cast<Item>().Where(item => indices.Contains(item.Index))) _items.SelectedItems?.Add(item);
        }
        finally { _refreshing = false; }
        ShowDetails(); SelectViewport(); UpdateActions();
    }
}
