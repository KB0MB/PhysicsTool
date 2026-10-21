using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using HKCLTool;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private MenuItem _convert = null!;
    private bool _conversionBusy;
    private void ConfigureConversion()
    {
        _convert = MenuCommand("Convert to...", ConvertDocument); _convert.Name = "ConvertPhysics";
    }
    private void UpdateConversion()
    {
        _convert.IsEnabled = !_conversionBusy && !IsActorMode && !_timer.IsEnabled && !_editorPlaying && !_viewport.TransformActive && PhysicsConversion.CanConvert(Session.Document);
        _convert.Header = !PhysicsConversion.CanConvert(Session.Document) ? "Convert to..." : "Convert to " + (Session.Document.IsBphcl ? "HKCL" : Session.Document.IsBphhb ? "BPHYSSB" : Session.Document.IsBphyssb ? "BPHHB" : "BPHCL");
    }
    private async Task ConvertDocument()
    {
        if (!_convert.IsEnabled) return;
        try
        {
            var conversion = new PhysicsConversion(Session.Document, Session.Path, _importedModel?.Rest.Bones);
            var options = await new ConversionWindow(conversion, DesktopPreferences.Load().ConversionPlatform).ShowDialog<PhysicsConversionOptions?>(this);
            if (options == null) return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
                Title = "Convert to " + conversion.Format, SuggestedFileName = conversion.SuggestedFileName,
                DefaultExtension = conversion.Extension.TrimStart('.'), ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType(conversion.Format) { Patterns = ["*" + conversion.Extension] }] });
            if (file?.TryGetLocalPath() is not { } path) return;
            if (DonorSession.Path != null && string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(DonorSession.Path), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException("Choose an output file different from the open donor.");
            _conversionBusy = true; UpdateConversion(); IsEnabled = false; _status.Text = "Converting and validating " + conversion.Format + "...";
            try
            {
                var result = await Task.Run(() => conversion.Export(path, options));
                _status.Text = $"Converted and reopened {System.IO.Path.GetFileName(path)} ({result.Entries} {(conversion.Target is PhysicsConversionTarget.Hkcl or PhysicsConversionTarget.Bphcl ? "cloths" : "helper groups")}).";
                if (conversion.Target == PhysicsConversionTarget.Hkcl)
                {
                    try { (DesktopPreferences.Load() with { ConversionPlatform = options.Platform }).Save(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                    { _status.Text += " The platform preference could not be saved."; }
                }
            }
            finally { _conversionBusy = false; IsEnabled = true; UpdateActions(); }
        }
        catch (Exception ex)
        {
            _status.Text = "Conversion failed; the source document is unchanged.";
            var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12 };
            panel.Children.Add(new ScrollViewer { MaxHeight = 380, Content = new TextBlock { Text = UserFacingExceptionFormatter.Format(ex), TextWrapping = TextWrapping.Wrap } });
            await Dialogs.Show(this, "Conversion failed", panel);
        }
    }
}
