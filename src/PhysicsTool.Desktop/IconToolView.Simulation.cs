using System.Numerics;
using Avalonia.Controls;
using HKCLTool;

namespace PhysicsTool.Desktop;

internal sealed partial class IconToolView
{
    private readonly CheckBox _simulateGravity = new() { Name = "IconSimulateGravity", Content = "Simulate gravity", IsVisible = false };
    private CancellationTokenSource? _settlementCancellation;
    private readonly SemaphoreSlim _settlementGate = new(1);
    private VisualModelData? _settledModel;
    private string _simulationPath = "", _simulationRoot = "";
    internal Task Settlement { get; private set; } = Task.CompletedTask;

    private void QueueSettlement()
    {
        _settlementCancellation?.Cancel();
        _settledModel = null;
        if (_simulateGravity.IsChecked != true || Asset?.HasSimulationData != true || Asset.ArmorPose == null || _adjustedModel == null)
        { Settlement = Task.CompletedTask; return; }
        var cancellation = new CancellationTokenSource();
        _settlementCancellation = cancellation;
        var asset = Asset;
        var input = _adjustedModel.Bones.ToDictionary(b => b.Name,
            b => VisualBoneTransform.Override(b.Name, b.WorldMatrix), StringComparer.OrdinalIgnoreCase);
        var adjustments = new Dictionary<string, Vector3>(_poseAdjustments, StringComparer.OrdinalIgnoreCase);
        var imported = UsesImportedPose ? _importedPose : null;
        double frame = imported == null ? 0 : (double)(_poseFrame.Value ?? 0);
        bool recovered = imported == null && asset.ArmorPose.UsesRecoveredIconPose;
        string path = _simulationPath, root = _simulationRoot;
        Settlement = SettleAsync();

        async Task SettleAsync()
        {
            var token = cancellation.Token;
            try
            {
                await Task.Delay(120, token);
                _status.Text = "Settling cloth and hair...";
                var result = await Task.Run(async () =>
                {
                    await _settlementGate.WaitAsync(token);
                    try
                    {
                        using var scene = BotwActorScene.Load(path, asset.ResourceRoots.Count > 0 ? asset.ResourceRoots : [root], cancellation: token);
                        if (!scene.HasCloth && scene.SupportCount == 0) throw new InvalidOperationException("This actor has no cloth or support/helper bone data.");
                        scene.WindEnabled = false; scene.WindSpeed = 0;
                        // Keep both the input pose and animation frame fixed throughout settling.
                        for (int step = 0; step < (scene.HasCloth ? 180 : 1); step++)
                        {
                            token.ThrowIfCancellationRequested();
                            scene.Update(imported, frame, true, 1f / 60, reset: step == 0, inputPose: input);
                        }
                        var outputs = scene.CurrentModel.Bones.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => VisualBoneTransform.Override(g.Key, g.First().WorldMatrix), StringComparer.OrdinalIgnoreCase);
                        var posed = asset.ArmorPose.Apply(adjustments, imported: imported, frame: frame,
                            recoveredPose: recovered, addBelt: true, finalOverrides: outputs);
                        if (posed.Meshes.Any(m => m.Vertices.Any(v => !float.IsFinite(v))))
                            throw new InvalidDataException("Cloth simulation produced invalid geometry.");
                        return posed;
                    }
                    finally { _settlementGate.Release(); }
                }, token);
                if (token.IsCancellationRequested || !ReferenceEquals(Asset, asset)) return;
                _settledModel = result; _rotatedModel = null; UpdatePreview();
                _status.Text = "Gravity settled; pose held still, wind off.";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested && ReferenceEquals(Asset, asset))
                {
                    _updating = true; _simulateGravity.IsChecked = false; _updating = false;
                    _status.Text = "Gravity simulation: " + ex.Message;
                }
            }
            finally
            {
                if (ReferenceEquals(_settlementCancellation, cancellation)) _settlementCancellation = null;
                cancellation.Dispose();
            }
        }
    }
}
