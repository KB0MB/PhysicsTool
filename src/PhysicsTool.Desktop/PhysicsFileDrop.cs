using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private bool _openingDroppedPhysics;

    private void ConfigurePhysicsFileDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = !_openingDroppedPhysics && !_conversionBusy && IsEnabled && !_viewport.TransformActive && GetPhysicsDropPath(e.DataTransfer) != null
                ? e.DragEffects & DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var path = GetPhysicsDropPath(e.DataTransfer);
            e.Handled = true;
            e.DragEffects = path != null ? DragDropEffects.Copy : DragDropEffects.None;
            if (path != null) await RunCommand(() => OpenDroppedPhysicsAsync(path));
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    internal static bool IsPhysicsFilePath(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() is ".hkcl" or ".bphcl" or ".bphyssb" or ".bphhb" or ".hkrg";

    internal static string? GetPhysicsDropPath(IDataTransfer data)
    {
        var files = data.TryGetFiles()?.Take(2).ToArray();
        return files is { Length: 1 } && files[0] is IStorageFile &&
            files[0].TryGetLocalPath() is { } path && IsPhysicsFilePath(path) ? path : null;
    }

    internal async Task OpenDroppedPhysicsAsync(string path)
    {
        if (_openingDroppedPhysics || _conversionBusy || !IsEnabled || _viewport.TransformActive || !IsPhysicsFilePath(path)) return;
        _openingDroppedPhysics = true;
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException("The dropped physics file is no longer available.");
            if (!await CanReplace()) return;
            LoadPath(path);
        }
        finally { _openingDroppedPhysics = false; }
    }
}
