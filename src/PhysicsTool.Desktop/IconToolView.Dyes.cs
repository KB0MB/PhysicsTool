using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using PhysicsTool.Core;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace PhysicsTool.Desktop;

internal sealed partial class IconToolView
{
    private readonly ComboBox _dye = new() { Name = "IconDye", HorizontalAlignment = HorizontalAlignment.Stretch, IsTextSearchEnabled = true, MaxDropDownHeight = 360 };
    private readonly Control _dyeRow;
    private readonly Button _cancelExport = new() { Name = "CancelIconBatch", Content = "Cancel export", IsVisible = false };
    private CancellationTokenSource? _batchCancellation;
    private IconDye _selectedDye = IconDye.Original;
    private IconDye? _pendingDye;
    private bool _changingDye;
    internal Task DyeSelection { get; private set; } = Task.CompletedTask;

    internal Task SelectDyeAsync(IconDye dye)
    {
        if (Busy && !_changingDye) return Task.CompletedTask;
        _pendingDye = dye;
        if (_changingDye) return DyeSelection;
        Busy = true; _changingDye = true;
        return DyeSelection = ProcessDyeSelectionAsync();
    }

    private async Task ProcessDyeSelectionAsync()
    {
        // Keep the selector enabled and focused; one worker consumes the newest request.
        try
        {
            while (_pendingDye is { } dye)
            {
                _pendingDye = null;
                try { await ApplyDyeAsync(dye, interactive: true); }
                catch when (_pendingDye != null) { }
            }
        }
        catch
        {
            _updating = true; _dye.SelectedItem = _selectedDye; _updating = false;
            throw;
        }
        finally { _pendingDye = null; _changingDye = false; Busy = false; }
    }

    private void LockCaptureControls(bool locked)
    {
        _settings.IsEnabled = !locked && Asset != null;
        _presets.IsEnabled = !locked;
        _viewport.IsHitTestVisible = !locked && UsesViewport;
        foreach (var control in _toolbar.Children)
            if (control != _cancelExport) control.IsEnabled = !locked;
        _export.IsEnabled = !locked && Asset != null && Surface != null;
    }

    private async Task ApplyDyeAsync(IconDye dye, bool interactive = false)
    {
        if (Asset?.WithDye is not { } build) throw new InvalidOperationException("No texture-pattern variants in this actor.");
        if (dye == IconDye.Original) dye = Asset.Dyes!.Default;
        _viewport.FinishTransform(false);
        await Settlement;
        var asset = await Task.Run(() => build(dye));
        // Pose controls remain usable while materials load. Rebind only after their latest settlement.
        while (true)
        {
            var settlement = Settlement;
            await settlement;
            if (ReferenceEquals(settlement, Settlement)) break;
        }
        if (interactive && _pendingDye != null) return;
        var adjusted = _adjustedModel == null ? null : asset.ArmorPose!.RebindMaterials(_adjustedModel);
        var settled = _settledModel == null ? null : asset.ArmorPose!.RebindMaterials(_settledModel);
        Asset = asset; _selectedDye = dye;
        _adjustedModel = adjusted; _settledModel = settled;
        _updating = true; _dye.SelectedItem = dye; _updating = false;
        foreach (var material in asset.Materials) material.Sample(_materialTime);
        _rotatedModel = null; UpdatePreview();
        _status.Text = asset.Name + " | " + dye + (asset.Warnings.Count > 0 ? " | " + string.Join("; ", asset.Warnings.Take(3)) : "");
    }

    internal Task<byte[]> CaptureIconBytesAsync(string name, bool native) => CaptureIconBytesAsync(name, native ? IconImageFormat.Sbitemico : IconImageFormat.Png);
    internal async Task<byte[]> CaptureIconBytesAsync(string name, IconImageFormat format)
    {
        string? template = format == IconImageFormat.Sbitemico ? Asset?.IconTemplate : Asset?.BntxTemplate;
        if (format != IconImageFormat.Png && template == null) throw new InvalidOperationException("A matching game icon template is required.");
        var png = await CapturePngAsync(CaptureSize);
        if (format == IconImageFormat.Png) return png;
        return await Task.Run(() =>
        {
            using var image = SKImage.FromEncodedData(png);
            using var bitmap = new SKBitmap(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            if (!image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)) throw new InvalidDataException("Cannot read icon pixels.");
            var rgba = new byte[checked(image.Width * image.Height * 4)];
            for (int y = 0; y < image.Height; y++) Marshal.Copy(bitmap.GetPixels() + y * bitmap.RowBytes, rgba, y * image.Width * 4, image.Width * 4);
            return format == IconImageFormat.Sbitemico ? IconItemTexture.Encode(File.ReadAllBytes(template!), name, rgba, image.Width, image.Height) :
                IconItemTexture.EncodeBntx(File.ReadAllBytes(template!), name, rgba, image.Width, image.Height, format == IconImageFormat.BntxZs);
        });
    }

    private async Task ExportDyes(IconImageFormat format, string directory, bool overwrite)
    {
        if (Asset?.Dyes is not { Choices.Count: > 1 }) return;
        _batchCancellation = new(); _cancelExport.IsVisible = true;
        try { await ExportDyesAsync(directory, format, _batchCancellation.Token, overwrite); }
        finally { _batchCancellation.Dispose(); _batchCancellation = null; _cancelExport.IsVisible = false; }
    }

    internal Task ExportDyesAsync(string directory, bool native, CancellationToken token = default) => ExportDyesAsync(directory, native ? IconImageFormat.Sbitemico : IconImageFormat.Png, token);
    internal async Task ExportDyesAsync(string directory, IconImageFormat format, CancellationToken token = default, bool overwrite = false)
    {
        if (Busy || Asset?.Dyes is not { Choices.Count: > 1 } dyes) throw new InvalidOperationException("No dye variants are ready for export.");
        var choices = dyes.Choices.Where(d => d.Animation.Length > 0).ToArray();
        bool multiple = choices.Select(d => d.Animation).Distinct().Skip(1).Any();
        string actor = Asset.Name;
        var entries = choices.Select(d => (Dye: d, Stem: IconDyes.FileStem(actor, d, multiple))).ToArray();
        string extension = format.Extension();
        if (entries.Select(e => e.Stem).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
            throw new InvalidDataException("Dye output filenames are not unique.");
        if (!overwrite && entries.Any(e => File.Exists(Path.Combine(directory, e.Stem + extension)))) throw new IOException("The export folder already contains these icons. Choose a new folder.");
        if (format == IconImageFormat.Sbitemico && Asset.IconTemplate == null || format is IconImageFormat.Bntx or IconImageFormat.BntxZs && Asset.BntxTemplate == null)
            throw new InvalidOperationException("No matching icon template is available.");
        var previous = Asset; var selected = _selectedDye; double time = _materialTime;
        HKCLTool.VisualModelData? adjusted = null, settled = null;
        bool savedPose = false;
        bool hitTest = _viewport.IsHitTestVisible;
        Busy = true; LockCaptureControls(true);
        _materialTime = 0;
        int written = 0;
        try
        {
            token.ThrowIfCancellationRequested();
            await Settlement;
            adjusted = _adjustedModel; settled = _settledModel; savedPose = true;
            token.ThrowIfCancellationRequested(); Directory.CreateDirectory(directory);
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                await ApplyDyeAsync(entry.Dye);
                _viewport.IsHitTestVisible = false;
                _status.Text = $"Capturing {written + 1}/{entries.Length}: {entry.Stem}";
                byte[] bytes = await CaptureIconBytesAsync(entry.Stem, format);
                token.ThrowIfCancellationRequested();
                string output = Path.Combine(directory, entry.Stem + extension);
                if (overwrite) HKCLTool.AtomicFile.WriteAllBytes(output, bytes);
                else { using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write); await stream.WriteAsync(bytes); }
                written++;
            }
            _status.Text = $"Exported {written} icons to {directory}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { _status.Text = $"Export canceled; {written} completed icons kept in {directory}"; }
        finally
        {
            Asset = previous; _selectedDye = selected; _materialTime = time;
            _updating = true; _dye.SelectedItem = selected; _updating = false;
            if (savedPose) { _adjustedModel = adjusted; _settledModel = settled; }
            _rotatedModel = null; UpdatePreview();
            _viewport.IsHitTestVisible = hitTest;
            Busy = false; LockCaptureControls(false);
        }
    }
}
