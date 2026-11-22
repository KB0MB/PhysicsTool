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
        _convert = new MenuItem { Header = "Convert to...", Name = "ConvertPhysics" };
        _convert.Click += async (_, e) => { if (_convert.ItemsSource == null) { e.Handled = true; await RunCommand(ConvertDocument); } };
    }
    private void UpdateConversion()
    {
        _convert.IsEnabled = !_conversionBusy && !IsActorMode && !_timer.IsEnabled && !_editorPlaying && !_viewport.TransformActive && PhysicsConversion.CanConvert(Session.Document);
        _convert.Header = !PhysicsConversion.CanConvert(Session.Document) ? "Convert to..." : "Convert to " + (Session.Document.IsBphcl ? "HKCL" : Session.Document.IsBphhb ? "BPHYSSB" : Session.Document.IsBphyssb ? "BPHHB" : "BPHCL");
        if (Session.Document.IsBphcl)
        {
            _convert.Header = "Convert to...";
            _convert.ItemsSource = new[] { MenuCommand("BotW HKCL", ConvertDocument),
                Session.Document.IsOot ? MenuCommand("TotK BPHCL (experimental)", () => ConvertNativeCloth(false)) : MenuCommand("OoT BPHCL (experimental)", ConvertOotDocument) };
        }
        else if (PhysicsConversion.CanConvert(Session.Document) && !Session.Document.IsHelperBoneSidecar)
        {
            _convert.Header = "Convert to...";
            _convert.ItemsSource = new[] { MenuCommand("TotK BPHCL", ConvertDocument), MenuCommand("OoT BPHCL (experimental)", ConvertOotDocument) };
        }
        else _convert.ItemsSource = null;
    }

    private Task ConvertOotDocument() => ConvertNativeCloth(true);

    private async Task ConvertNativeCloth(bool targetOot)
    {
        if (!_convert.IsEnabled || Session.Document.IsHelperBoneSidecar) return;
        string game = targetOot ? "OoT" : "TotK";
        string warning = targetOot ? "Choose your OoT romfs for reference data. Newer aerodynamic settings start at neutral defaults." : OotConversionExport.OlderGameWarning;
        if (!await Dialogs.Confirm(this, "Experimental conversion to " + game,
            "This converts cloth only, not helper bones or model rigs. Unsupported layouts are rejected.\n\n" + warning +
            "\n\nSave a separate test file and keep an original copy as a backup! In-game compatibility still needs testing.")) return;
        string root = "";
        if (targetOot)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select OoT romfs", AllowMultiple = false });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return;
            root = folder;
        }
        var source = Session.Document.CopyForConversion();
        var sourcePath = Session.Path;
        var name = System.IO.Path.GetFileNameWithoutExtension(sourcePath) ?? "Physics";
        name = targetOot ? name.Replace(".Nin_NX_NVN", ".Nin_Ounce_NVN2", StringComparison.Ordinal) : name.Replace(".Nin_Ounce_NVN2", ".Nin_NX_NVN", StringComparison.Ordinal);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            Title = "Export experimental " + game + " cloth", SuggestedFileName = name + ".bphcl", DefaultExtension = "bphcl",
            ShowOverwritePrompt = true, FileTypeChoices = [new FilePickerFileType(game + " cloth") { Patterns = ["*.bphcl"] }] });
        if (file?.TryGetLocalPath() is not { } path) return;
        if (DonorSession.Path is { } donorPath && string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(donorPath),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Choose an output file different from the open donor.");
        _conversionBusy = true; IsEnabled = false;
        _status.Text = "Converting and validating experimental " + game + " cloth...";
        try
        {
            var count = await Task.Run(() => OotConversionExport.Export(source, root, path, sourcePath, targetOot));
            _status.Text = $"Exported {count} cloths as experimental {game} BPHCL. In-game validation is still required.";
        }
        catch (Exception ex)
        {
            _status.Text = "Conversion failed; the source and existing output were left unchanged.";
            var panel = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap });
            await Dialogs.Show(this, game + " conversion unavailable for this file", panel);
        }
        finally { _conversionBusy = false; IsEnabled = true; UpdateActions(); }
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
