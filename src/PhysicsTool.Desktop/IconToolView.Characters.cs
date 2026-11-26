using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using PhysicsTool.Core;
using System.Text.Json;

namespace PhysicsTool.Desktop;

internal sealed partial class IconToolView
{
    private readonly ComboBox _character = new() { Name = "IconCharacter", HorizontalAlignment = HorizontalAlignment.Stretch, IsTextSearchEnabled = true };
    private IconCharacterProfile _selectedCharacter = IconCharacterProfile.Link;

    private Control CharacterSelector()
    {
        try { _character.ItemsSource = IconCharacterProfile.LoadAll(); }
        catch (Exception ex) { _character.ItemsSource = new[] { IconCharacterProfile.Link }; _status.Text = "Character profiles: " + ex.Message; }
        _character.SelectedIndex = 0;
        _character.SelectionChanged += async (_, _) =>
        {
            if (_updating || Busy || _character.SelectedItem is not IconCharacterProfile next || next.Id == _selectedCharacter.Id) return;
            await SelectCharacterAsync(next);
        };
        var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
        row.Children.Add(_character);
        var add = ViewportTools.Icon("Plus", "Add character mannequin", async () =>
        {
            try
            {
                if (Busy) return;
                var files = await _owner.StorageProvider.OpenFilePickerAsync(new() { Title = "Character mannequin", AllowMultiple = false,
                    FileTypeFilter = [new("Icon mannequin BFRES") { Patterns = ["*.sbfres", "*.bfres"] }] });
                if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new() { Title = "Character mod texture folder", AllowMultiple = false });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } root) return;
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("Armor_Icon_", StringComparison.Ordinal)) name = name[11..];
                var existing = IconCharacterProfile.LoadAll().FirstOrDefault(p => p.Id == name);
                var profile = new IconCharacterProfile(name, name, path, root)
                { TexturePrefixes = existing?.TexturePrefixes ?? new(), MaterialTextures = existing?.MaterialTextures ?? new(), NeckSplices = existing?.NeckSplices ?? new(), MaterialSources = existing?.MaterialSources ?? new(), TextureColors = existing?.TextureColors ?? new() };
                profile.Save();
                _updating = true; _character.ItemsSource = IconCharacterProfile.LoadAll(); _character.SelectedItem = _character.Items.OfType<IconCharacterProfile>().Single(p => p.Id == profile.Id); _updating = false;
                await SelectCharacterAsync(profile);
            }
            catch (Exception ex) { _updating = false; await ShowErrorAsync("Character mannequin", ex); }
        });
        Grid.SetColumn(add, 1); row.Children.Add(add);
        return Row("Character", row);
    }

    internal async Task SelectCharacterAsync(IconCharacterProfile next)
    {
        if (Busy) return;
        var previous = _selectedCharacter;
        _selectedCharacter = next;
        if (Asset != null && _simulationPath != null && _simulationRoot != null)
            await LoadPathAsync(_simulationPath, _simulationRoot, showErrors: true, preserveCapture: true);
        if (Asset != null && Asset.CharacterId != next.Id) _selectedCharacter = previous;
        _updating = true;
        _character.SelectedItem = _character.Items.OfType<IconCharacterProfile>().FirstOrDefault(p => p.Id == _selectedCharacter.Id);
        _updating = false;
    }

    private async Task EnsurePresetCharacter(CustomPreset preset)
    {
        if (preset.CharacterId == _selectedCharacter.Id) return;
        var profile = _character.Items.OfType<IconCharacterProfile>().FirstOrDefault(p => p.Id == preset.CharacterId)
            ?? throw new InvalidDataException("Character profile is not installed: " + preset.CharacterId);
        await SelectCharacterAsync(profile);
        if (_selectedCharacter.Id != profile.Id) throw new InvalidOperationException("Could not load the preset's character mannequin.");
    }

    internal async Task LoadPresetBytesAsync(byte[] bytes)
    {
        var preset = JsonSerializer.Deserialize<CustomPreset>(bytes, PresetJson) ?? throw new InvalidDataException("Empty capture preset.");
        await EnsurePresetCharacter(preset);
        LoadPresetBytes(bytes);
    }
}
