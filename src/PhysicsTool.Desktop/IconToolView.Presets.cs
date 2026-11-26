using System.Numerics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop.Rendering;

namespace PhysicsTool.Desktop;

internal sealed partial class IconToolView
{
    private static readonly JsonSerializerOptions PresetJson = new() { IncludeFields = true, WriteIndented = true };
    private readonly List<CustomPreset> _customPresets = [];
    private ToolboxActorAnimation? _importedPose;
    private byte[]? _importedBfska;
    private readonly NumericUpDown _poseFrame = Number(0, 0, 1);
    private readonly Slider _poseFrameSlider = new() { Minimum = 0, Maximum = 0, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly Control _poseFrameRow;
    private readonly CheckBox _emission = new() { Content = "Emission", IsChecked = true };
    private readonly CheckBox _castShadows = new() { Name = "IconCastShadows", Content = "Cast shadows", IsChecked = true };
    private string ImportedPoseLabel => "Imported: " + _importedPose?.Name;
    private bool UsesImportedPose => _importedPose != null && Equals(_pose.SelectedItem, ImportedPoseLabel);

    private sealed record CustomPreset(int Version, string Name, Vector3 Rotation, Vector3 Camera, Vector3 Angles,
        float Fov, Vector3 Light, float Direct, float Ambient, float Brightness, float Zoom, int Size,
        bool Edges, bool Emission, bool AnimateMaterials, SavedPose Pose, ViewportCaptureState? Viewport)
    {
        public string Lighting { get; init; } = "Preview";
        public bool SimulateGravity { get; init; }
        public bool CastShadows { get; init; } = true;
        public Vector3 Position { get; init; }
        public MeshId[] HiddenMeshes { get; init; } = [];
        public bool HasRaulSkinVisibility { get; init; }
        public bool HasAllPropsVisibility { get; init; }
        public string ActorName { get; init; } = "";
        public string CharacterId { get; init; } = "Link";
        public override string ToString() => "Custom: " + Name;
    }

    private SavedPose CurrentPose() => new(UsesImportedPose ? "Imported" : "IconPose",
        new(_poseAdjustments, StringComparer.OrdinalIgnoreCase), UsesImportedPose ? _importedBfska : null, UsesImportedPose ? (double)(_poseFrame.Value ?? 0) : 0, BeltVisible);

    private List<string> PoseChoices()
    {
        return [IconPoseLabel];
    }

    private async Task ImportBfska()
    {
        if (Asset == null) return;
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new() { Title = "Import BFSKA pose", AllowMultiple = false,
            FileTypeFilter = [new("Skeletal animation") { Patterns = ["*.bfska"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) ImportPoseBytes(File.ReadAllBytes(path));
    }

    internal void ImportPoseBytes(byte[] bytes) => ApplySavedPose(new("Imported", new(), bytes, AddBelt: BeltVisible));

    private ToolboxActorAnimation? ValidatePose(SavedPose saved)
    {
        var pose = Asset?.ArmorPose ?? throw new InvalidOperationException("Open an actor first.");
        if (saved.BasePose is not ("Imported" or "IconPose" or "Rest" or "Nml_Wait" or "Icon" or "Recovered") || saved.Rotations == null)
            throw new InvalidDataException("Unknown pose type.");
        if (saved.AddBelt && !pose.HasBelt) throw new InvalidDataException("The native icon belt is not available for this actor.");
        foreach (var (bone, angle) in saved.Rotations)
            if (!pose.Bones.Contains(bone, StringComparer.OrdinalIgnoreCase) || !float.IsFinite(angle.LengthSquared()) ||
                Math.Max(Math.Abs(angle.X), Math.Max(Math.Abs(angle.Y), Math.Abs(angle.Z))) > 90)
                throw new InvalidDataException("Unknown bone or invalid rotation: " + bone);
        if (saved.BasePose != "Imported") return null;
        var animation = IconBfska.Load(saved.Bfska ?? throw new InvalidDataException("Preset has no BFSKA data."));
        if (!animation.BoneNames.Intersect(pose.Bones, StringComparer.OrdinalIgnoreCase).Any())
            throw new InvalidDataException("The BFSKA has no tracks matching this actor's bones.");
        if (!double.IsFinite(saved.Frame) || saved.Frame < 0 || saved.Frame > animation.FrameCount)
            throw new InvalidDataException("Pose frame is outside the animation.");
        return animation;
    }

    private void ApplySavedPose(SavedPose saved)
    {
        var imported = ValidatePose(saved);
        _updating = true;
        try
        {
            _poseAdjustments.Clear(); foreach (var item in saved.Rotations) _poseAdjustments.Add(item.Key, item.Value);
            if (imported != null)
            {
                _importedPose = imported; _importedBfska = saved.Bfska;
                var choices = PoseChoices();
                choices.Add(ImportedPoseLabel); _pose.ItemsSource = choices;
                _poseFrame.Maximum = imported.FrameCount; _poseFrameSlider.Maximum = imported.FrameCount;
                _poseFrame.Value = (decimal)saved.Frame; _pose.SelectedItem = ImportedPoseLabel; _pose.IsVisible = true;
            }
            else _pose.SelectedItem = IconPoseLabel;
            RebuildPose(); _rotatedModel = null;
        }
        finally { _updating = false; }
        SyncPoseAngles(); UpdatePreview(); SetBeltVisible(saved.AddBelt);
    }

    internal byte[] SavePresetBytes(string name)
    {
        if (Asset == null) throw new InvalidOperationException("Open an actor first.");
        var preset = new CustomPreset(1, name, Get(_rotation), Get(_camera), Get(_angles), (float)(_fov.Value ?? 5), Get(_light),
            (float)(_direct.Value ?? 1), (float)(_ambient.Value ?? 1), (float)(_brightness.Value ?? 3), (float)(_zoom.Value ?? 100),
            CaptureSize, _edges.IsChecked == true, _emission.IsChecked == true, _auto.IsChecked == true,
            CurrentPose(), UsesViewport ? _viewport.SaveCaptureCamera() : null)
            { Lighting = (_environment.SelectedItem as BotwLightingEnvironment)?.Name ?? "Preview", SimulateGravity = _simulateGravity.IsChecked == true, CastShadows = _castShadows.IsChecked == true,
                Position = Get(_position), HiddenMeshes = _hiddenMeshes.ToArray(), HasRaulSkinVisibility = true, HasAllPropsVisibility = true, ActorName = Asset.Name, CharacterId = _selectedCharacter.Id };
        return JsonSerializer.SerializeToUtf8Bytes(preset, PresetJson);
    }

    internal void LoadPresetBytes(byte[] bytes)
    {
        var preset = JsonSerializer.Deserialize<CustomPreset>(bytes, PresetJson) ?? throw new InvalidDataException("Empty capture preset.");
        ValidatePreset(preset); ApplyCustomPreset(preset);
        _customPresets.RemoveAll(p => p.Name == preset.Name); _customPresets.Add(preset);
        FilterPresets(); _updating = true; _presets.SelectedItem = preset; _updating = false;
        _cameraSettings.IsEnabled = !UsesViewport; _viewport.IsHitTestVisible = UsesViewport; UpdatePreview();
        if (preset.Viewport != null) _viewport.RestoreCaptureCamera(preset.Viewport);
    }

    private void ValidatePreset(CustomPreset preset)
    {
        if (preset.CharacterId != _selectedCharacter.Id) throw new InvalidDataException("Select the " + preset.CharacterId + " character before applying this preset.");
        static bool Range(float x, float min, float max) => float.IsFinite(x) && x >= min && x <= max;
        static bool VectorRange(Vector3 v, float max) => float.IsFinite(v.LengthSquared()) && Math.Max(Math.Abs(v.X), Math.Max(Math.Abs(v.Y), Math.Abs(v.Z))) <= max;
        if (preset.Version != 1 || string.IsNullOrWhiteSpace(preset.Name) || !VectorRange(preset.Rotation, 360) ||
            !VectorRange(preset.Camera, 10000) || !VectorRange(preset.Angles, 360) || !VectorRange(preset.Light, 1) ||
            !Range(preset.Fov, .1f, 120) || !Range(preset.Direct, 0, 10) || !Range(preset.Ambient, 0, 10) ||
            !Range(preset.Brightness, 0, 4) || !Range(preset.Zoom, 25, 400) ||
            !new[] { 144, 256, 512, 576, 1024, 2048 }.Contains(preset.Size) || preset.Pose == null)
            throw new InvalidDataException("Invalid capture preset settings.");
        preset.Viewport?.Validate(); ValidatePose(preset.Pose);
        if (!VectorRange(preset.Position, 10000) || preset.HiddenMeshes == null || preset.HiddenMeshes.Length > 10000 ||
            preset.HiddenMeshes.Any(m => m == null || string.IsNullOrEmpty(m.Name) || m.Name.Length > 4096 || m.Occurrence < 0))
            throw new InvalidDataException("Invalid model placement or mesh visibility.");
        if (!BotwLightingEnvironment.All.Any(e => e.Name == preset.Lighting)) throw new InvalidDataException("Unknown lighting setup.");
    }

    private void ApplyCustomPreset(CustomPreset preset)
    {
        ValidatePreset(preset); _viewport.FinishTransform(false); ApplySavedPose(preset.Pose);
        _updating = true;
        try
        {
            Set(_rotation, preset.Rotation); Set(_camera, preset.Camera); Set(_angles, preset.Angles); Set(_light, preset.Light);
            Set(_position, preset.Position); _hiddenMeshes.Clear();
            foreach (var hidden in preset.HiddenMeshes)
            {
                string replacement = hidden.Name.StartsWith("Icon belt", StringComparison.Ordinal) ? IconArmorProps.Prefix + "Icon_Lower_Belt"
                    : hidden.Name.StartsWith("Icon waist skin", StringComparison.Ordinal) ? IconArmorProps.Prefix + "Icon_Lower_Skin" : hidden.Name;
                _hiddenMeshes.Add(CaptureModel?.Meshes.Any(m => m.Name == replacement) == true ? new(replacement, hidden.Occurrence) : hidden);
            }
            if (!preset.HasRaulSkinVisibility)
                foreach (var optionalSkin in CaptureModel!.Meshes.Where(m => IconArmorProps.IsRaulSkin(m.Name) &&
                    Asset!.IsMeshHiddenByDefault(m.Name)))
                    _hiddenMeshes.Add(new(optionalSkin.Name, 0));
            if (!preset.HasAllPropsVisibility)
                foreach (var prop in CaptureModel!.Meshes.Where(m => IconArmorProps.IsProp(m.Name) &&
                    !IconArmorProps.MatchesArmorSlot(Asset!.Name, m.Name)))
                    _hiddenMeshes.Add(new(prop.Name, 0));
            _fov.Value = (decimal)preset.Fov; _direct.Value = (decimal)preset.Direct; _ambient.Value = (decimal)preset.Ambient;
            _brightness.Value = (decimal)preset.Brightness; _zoom.Value = (decimal)preset.Zoom; CaptureSize = preset.Size;
            _environment.SelectedItem = BotwLightingEnvironment.All.Single(e => e.Name == preset.Lighting);
            _simulateGravity.IsChecked = preset.SimulateGravity;
            _castShadows.IsChecked = preset.CastShadows;
            _edges.IsChecked = preset.Edges; _emission.IsChecked = preset.Emission; _auto.IsChecked = preset.AnimateMaterials;
            _rotatedModel = null;
        }
        finally { _updating = false; }
        QueueSettlement();
        _cameraSettings.IsEnabled = preset.Viewport == null; _viewport.IsHitTestVisible = preset.Viewport != null;
        UpdatePreview(); if (preset.Viewport != null) _viewport.RestoreCaptureCamera(preset.Viewport);
        if (!preset.Pose.AddBelt) SetBeltVisible(false);
        _status.Text = "Loaded custom preset: " + preset.Name;
    }

    private async Task SaveCustomPreset()
    {
        if (Asset == null) { _status.Text = "Open an actor first."; return; }
        var file = await _owner.StorageProvider.SaveFilePickerAsync(new() { Title = "Save capture preset", DefaultExtension = "json",
            SuggestedFileName = Asset.Name + "-capture.json", FileTypeChoices = [new("Capture preset") { Patterns = ["*.json"] }] });
        if (file?.TryGetLocalPath() is not { } path) return;
        var bytes = SavePresetBytes(Path.GetFileNameWithoutExtension(path)); AtomicFile.WriteAllBytes(path, bytes);
        LoadPresetBytes(bytes); _status.Text = "Saved preset: " + path;
    }

    private async Task OpenCustomPreset()
    {
        if (Asset == null) { _status.Text = "Open an actor first."; return; }
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new() { Title = "Open capture preset", AllowMultiple = false,
            FileTypeFilter = [new("Capture preset") { Patterns = ["*.json"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await LoadPresetBytesAsync(File.ReadAllBytes(path));
    }
}
