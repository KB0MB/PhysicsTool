using Avalonia.Controls;
using HKCLTool;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private Button _playerArmor = null!;

    private async Task ShowPlayerArmor()
    {
        if (_actorScene?.ArmorCatalog is not { } catalog) return;
        await new PlayerArmorWindow(catalog, _actorScene.EquippedArmor, SwapPlayerArmorAsync,
            _actorScene.WeaponCatalog, _actorScene.Weapons, SwapPlayerEquipmentAsync).ShowDialog(this);
    }

    internal Task SwapPlayerArmorAsync(IReadOnlyDictionary<PlayerArmorSlot, string> armor, CancellationToken cancellation) =>
        SwapPlayerEquipmentAsync(armor, _actorScene?.Weapons ?? new(), cancellation);

    internal async Task SwapPlayerEquipmentAsync(IReadOnlyDictionary<PlayerArmorSlot, string> armor,
        IReadOnlyDictionary<PlayerWeaponKind, PlayerWeaponSelection> weapons, CancellationToken cancellation)
    {
        var old = _actorScene;
        if (old?.ArmorCatalog == null || _modelLoading) throw new InvalidOperationException("Player armor is not available while another model is loading.");
        var request = ++_modelRequest;
        var clipName = ActorAnimation?.Name; var clipResource = ActorAnimation?.ResourceName;
        _actorTimer.Stop(); _modelLoading = true; UpdateModelActions();
        BotwActorScene? replacement = null;
        try
        {
            var progress = new Progress<string>(message => { if (request == _modelRequest) _status.Text = message; });
            replacement = await Task.Run(() => BotwActorScene.Load(old.Resources.SourcePath, old.Resources.Roots,
                progress, cancellation, old.Resources.SourceEntry, armor, weapons), cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (request != _modelRequest || !ReferenceEquals(old, _actorScene)) throw new OperationCanceledException();
            // Prepare the new pose before replacing the currently usable outfit.
            var clip = replacement.Animations.FirstOrDefault(a => a.Name == clipName && a.ResourceName == clipResource);
            var frame = Math.Clamp(_actorFrame, 0, clip?.FrameCount ?? 0);
            replacement.ConfigureSimulation(SimulationOptions.ApplyTo);
            replacement.Update(clip, frame, _actorSimulating, 0, true);
            _actorScene = replacement; replacement = null; _actorFrame = frame;
            _updatingTimeline = true;
            try
            {
                _animations.ItemsSource = new[] { "Rest pose" }.Concat(_actorScene.Animations.Select(a => a.Name)).ToArray();
                _animations.SelectedIndex = clip == null ? 0 : _actorScene.Animations.IndexOf(clip) + 1;
            }
            finally { _updatingTimeline = false; }
            _viewport.HiddenMeshes.Clear(); _viewport.Selected.Clear();
            _viewport.Data = _actorScene.Overlay; _viewport.SetModelPose(_actorScene.CurrentModel);
            old.Dispose();
            PoseActor(true, 0); UpdateActions();
            _status.Text = $"Player: {_actorScene.ClothCount} cloths, {_actorScene.SupportCount} helper groups. " +
                string.Join(" / ", _actorScene.EquippedArmor.Values.Concat(_actorScene.Weapons.Values.Select(w => w.Name)));
            var physicsWarnings = _actorScene.Resources.Warnings.Where(w => w.StartsWith("Physics ") || w.StartsWith("Cloth ") || w.StartsWith("Missing "));
            if (physicsWarnings.Any()) _status.Text += " " + string.Join(" ", physicsWarnings.Take(2));
        }
        finally
        {
            replacement?.Dispose();
            if (request == _modelRequest)
            {
                _modelLoading = false; UpdateModelActions(); UpdateActorPlayback();
            }
        }
    }
}
