using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HKCLTool;

internal sealed record AppLanguageOption(string Code, string NativeName)
{
    public override string ToString() => NativeName;
}

internal static class AppLocalization
{
    private sealed class TextBinding
    {
        public string SourceText { get; set; } = string.Empty;
        public bool IsHooked { get; set; }
    }

    private sealed class AppSettings
    {
        public string Language { get; set; } = "en";
        public int AnimationFramesPerSecond { get; set; } = 30;
        public string[] ActorResourceFolders { get; set; } = Array.Empty<string>();
    }

    private static readonly ConditionalWeakTable<Control, TextBinding> ControlBindings = new();
    private static readonly ConditionalWeakTable<ToolStripItem, TextBinding> ItemBindings = new();
    private static readonly ConditionalWeakTable<DataGridViewColumn, TextBinding> ColumnBindings = new();
    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new(StringComparer.OrdinalIgnoreCase);
    private static bool _applying;

    public static IReadOnlyList<AppLanguageOption> Languages { get; } =
    [
        new("en", "English"),
        new("ja", "日本語"),
        new("fr", "Français"),
        new("es", "Español")
    ];

    public static string CurrentLanguage { get; private set; } = "en";
    public static int AnimationFramesPerSecond { get; private set; } = 30;
    public static IReadOnlyList<string> ActorResourceFolders { get; private set; } = Array.Empty<string>();
    public static double GetAnimationPlaybackFrameRate(double sourceFramesPerSecond) =>
        sourceFramesPerSecond * AnimationFramesPerSecond / 60.0;
    public static event EventHandler? LanguageChanged;

    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static void Initialize()
    {
        LoadTranslations();
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (settings != null)
                {
                    if (Languages.Any(language =>
                        string.Equals(language.Code, settings.Language, StringComparison.OrdinalIgnoreCase)))
                    {
                        CurrentLanguage = settings.Language.ToLowerInvariant();
                    }
                    AnimationFramesPerSecond = settings.AnimationFramesPerSecond == 60 ? 60 : 30;
                    ActorResourceFolders = (settings.ActorResourceFolders ?? Array.Empty<string>())
                        .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                }
            }
        }
        catch
        {
            CurrentLanguage = "en";
            AnimationFramesPerSecond = 30;
            ActorResourceFolders = Array.Empty<string>();
        }
        ApplyCulture();
    }

    public static bool TrySetLanguage(string languageCode) =>
        TrySetPreferences(languageCode, AnimationFramesPerSecond);

    public static bool TrySetPreferences(string languageCode, int animationFramesPerSecond)
    {
        var language = Languages.FirstOrDefault(option =>
            string.Equals(option.Code, languageCode, StringComparison.OrdinalIgnoreCase));
        if (language == null || animationFramesPerSecond is not (30 or 60))
            return false;

        try
        {
            var temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(
                new AppSettings
                {
                    Language = language.Code,
                    AnimationFramesPerSecond = animationFramesPerSecond,
                    ActorResourceFolders = ActorResourceFolders.ToArray()
                },
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, SettingsPath, true);
        }
        catch
        {
            return false;
        }

        var languageChanged =
            !string.Equals(CurrentLanguage, language.Code, StringComparison.OrdinalIgnoreCase);
        CurrentLanguage = language.Code;
        AnimationFramesPerSecond = animationFramesPerSecond;
        if (!languageChanged)
            return true;

        ApplyCulture();
        LanguageChanged?.Invoke(null, EventArgs.Empty);
        return true;
    }

    public static bool TrySetActorResourceFolders(IEnumerable<string> folders)
    {
        try
        {
            var paths = folders.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            AtomicFile.WriteAllBytes(SettingsPath, JsonSerializer.SerializeToUtf8Bytes(new AppSettings
            {
                Language = CurrentLanguage, AnimationFramesPerSecond = AnimationFramesPerSecond, ActorResourceFolders = paths
            }, new JsonSerializerOptions { WriteIndented = true }));
            ActorResourceFolders = paths;
            return true;
        }
        catch { return false; }
    }

    public static string Translate(string englishText)
    {
        if (string.IsNullOrEmpty(englishText) || CurrentLanguage == "en")
            return englishText;
        return Translations.TryGetValue(CurrentLanguage, out var language) &&
               language.TryGetValue(englishText, out var translated)
            ? translated
            : englishText;
    }

    public static void ApplyTo(Control root)
    {
        _applying = true;
        try
        {
            ApplyControl(root);
        }
        finally
        {
            _applying = false;
        }
    }

    private static void ApplyControl(Control control)
    {
        var binding = ControlBindings.GetOrCreateValue(control);
        if (!binding.IsHooked)
        {
            binding.SourceText = ResolveSourceText(control.Text);
            control.TextChanged += HandleControlTextChanged;
            binding.IsHooked = true;
        }
        control.Text = Translate(binding.SourceText);

        if (control is DataGridView grid)
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                var columnBinding = ColumnBindings.GetOrCreateValue(column);
                if (string.IsNullOrEmpty(columnBinding.SourceText))
                    columnBinding.SourceText = ResolveSourceText(column.HeaderText);
                column.HeaderText = Translate(columnBinding.SourceText);
            }
        }

        if (control is ToolStrip toolStrip)
        {
            foreach (ToolStripItem item in toolStrip.Items)
                ApplyItem(item);
        }

        foreach (Control child in control.Controls)
            ApplyControl(child);
    }

    private static void ApplyItem(ToolStripItem item)
    {
        var binding = ItemBindings.GetOrCreateValue(item);
        if (!binding.IsHooked)
        {
            binding.SourceText = ResolveSourceText(item.Text);
            item.TextChanged += HandleItemTextChanged;
            binding.IsHooked = true;
        }
        item.Text = Translate(binding.SourceText);
        if (item is ToolStripDropDownItem dropDown)
        {
            foreach (ToolStripItem child in dropDown.DropDownItems)
                ApplyItem(child);
        }
    }

    private static void HandleControlTextChanged(object? sender, EventArgs e)
    {
        if (_applying || sender is not Control control)
            return;
        var binding = ControlBindings.GetOrCreateValue(control);
        binding.SourceText = ResolveSourceText(control.Text);
        var translated = Translate(binding.SourceText);
        if (translated == control.Text)
            return;
        _applying = true;
        try
        {
            control.Text = translated;
        }
        finally
        {
            _applying = false;
        }
    }

    private static void HandleItemTextChanged(object? sender, EventArgs e)
    {
        if (_applying || sender is not ToolStripItem item)
            return;
        var binding = ItemBindings.GetOrCreateValue(item);
        binding.SourceText = ResolveSourceText(item.Text);
        var translated = Translate(binding.SourceText);
        if (translated == item.Text)
            return;
        _applying = true;
        try
        {
            item.Text = translated;
        }
        finally
        {
            _applying = false;
        }
    }

    private static string ResolveSourceText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        foreach (var language in Translations.Values)
        {
            foreach (var entry in language)
            {
                if (entry.Value == text)
                    return entry.Key;
            }
        }
        return text;
    }

    private static void LoadTranslations()
    {
        foreach (var language in Languages.Where(language => language.Code != "en"))
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Languages", language.Code + ".json");
                if (!File.Exists(path))
                    continue;
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (values != null)
                    Translations[language.Code] = new Dictionary<string, string>(values, StringComparer.Ordinal);
            }
            catch
            {
                // A damaged optional language file falls back to English.
            }
        }
    }

    private static void ApplyCulture()
    {
        var culture = CultureInfo.GetCultureInfo(CurrentLanguage switch
        {
            "ja" => "ja-JP",
            "fr" => "fr-FR",
            "es" => "es-ES",
            _ => "en-US"
        });
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }
}

public class LocalizedForm : Form
{
    private readonly Icon _applicationIcon;

    public LocalizedForm()
    {
        using var stream = typeof(LocalizedForm).Assembly.GetManifestResourceStream("PhysicsTool.Resources.Branding.PhysicsTool.ico")
            ?? throw new InvalidOperationException("The application icon resource is missing.");
        using var resourceIcon = new Icon(stream);
        _applicationIcon = (Icon)resourceIcon.Clone();
        Icon = _applicationIcon;
    }

    private sealed record ControlLayoutBaseline(Size Size, bool FitsSingleLine, int WrappedTextHeight);

    private readonly Dictionary<Control, ControlLayoutBaseline> _controlLayoutBaselines = new();
    private readonly Dictionary<ColumnStyle, float> _columnLayoutBaselines = new();
    private readonly Dictionary<RowStyle, float> _rowLayoutBaselines = new();
    private readonly HashSet<Control> _resizedControls = new();
    private Size _baseClientSize;
    private bool _layoutBaselineCaptured;

    protected override void OnLoad(EventArgs e)
    {
        if (FormBorderStyle == FormBorderStyle.FixedDialog)
            CaptureLayoutBaseline();
        AppLocalization.LanguageChanged += HandleLanguageChanged;
        ApplyLocalization();
        base.OnLoad(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            AppLocalization.LanguageChanged -= HandleLanguageChanged;
        base.Dispose(disposing);
        if (disposing)
            _applicationIcon.Dispose();
    }

    protected virtual void ApplyLocalization()
    {
        AppLocalization.ApplyTo(this);
        if (FormBorderStyle == FormBorderStyle.FixedDialog)
            GrowToFitLocalizedText();
    }

    private void HandleLanguageChanged(object? sender, EventArgs e) => ApplyLocalization();

    private void CaptureLayoutBaseline()
    {
        if (_layoutBaselineCaptured)
            return;

        PerformLayout();
        _baseClientSize = ClientSize;
        CaptureControlLayout(this);
        _layoutBaselineCaptured = true;
    }

    private void CaptureControlLayout(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is TableLayoutPanel table)
            {
                foreach (ColumnStyle style in table.ColumnStyles)
                    _columnLayoutBaselines.TryAdd(style, style.Width);
                foreach (RowStyle style in table.RowStyles)
                    _rowLayoutBaselines.TryAdd(style, style.Height);
            }

            if (IsTextLayoutControl(control) && !string.IsNullOrWhiteSpace(control.Text))
            {
                var requiredWidth = MeasureRequiredWidth(control);
                var wrappedHeight = MeasureWrappedHeight(control);
                _controlLayoutBaselines[control] = new ControlLayoutBaseline(
                    control.Size,
                    requiredWidth <= Math.Max(1, control.ClientSize.Width),
                    wrappedHeight);
            }

            CaptureControlLayout(control);
        }
    }

    private void GrowToFitLocalizedText()
    {
        if (!_layoutBaselineCaptured)
            return;

        var workingArea = Screen.FromControl(this).WorkingArea;
        var center = Owner != null
            ? new Point(Owner.Left + Owner.Width / 2, Owner.Top + Owner.Height / 2)
            : new Point(Left + Width / 2, Top + Height / 2);

        SuspendLayout();
        ClientSize = _baseClientSize;
        foreach (var entry in _columnLayoutBaselines)
            entry.Key.Width = entry.Value;
        foreach (var entry in _rowLayoutBaselines)
            entry.Key.Height = entry.Value;
        foreach (var control in _resizedControls)
        {
            if (_controlLayoutBaselines.TryGetValue(control, out var baseline))
                control.Size = baseline.Size;
        }
        _resizedControls.Clear();
        ResumeLayout(true);

        var nonClientWidth = Width - ClientSize.Width;
        var maximumClientWidth = Math.Max(_baseClientSize.Width, workingArea.Width - nonClientWidth - 48);
        for (var pass = 0; pass < 4; pass++)
        {
            PerformLayoutTree(this);
            var requiredGrowth = 0;
            var columnGrowth = new Dictionary<ColumnStyle, int>();
            foreach (var entry in _controlLayoutBaselines)
            {
                var control = entry.Key;
                if (!entry.Value.FitsSingleLine || string.IsNullOrWhiteSpace(control.Text) || control.Text.Contains('\n'))
                    continue;

                var overflow = MeasureRequiredWidth(control) - Math.Max(1, control.ClientSize.Width);
                if (overflow <= 0)
                    continue;

                var columnStyle = FindAbsoluteTableColumn(control);
                if (columnStyle != null)
                    columnGrowth[columnStyle] = Math.Max(columnGrowth.GetValueOrDefault(columnStyle), overflow);
                requiredGrowth = Math.Max(requiredGrowth, overflow + 12);
            }

            foreach (var entry in columnGrowth)
                entry.Key.Width += entry.Value;

            if (requiredGrowth <= 0 || ClientSize.Width >= maximumClientWidth)
                break;
            ClientSize = new Size(Math.Min(maximumClientWidth, ClientSize.Width + requiredGrowth), ClientSize.Height);
        }

        GrowForWrappedText();
        PerformLayoutTree(this);

        Location = new Point(
            Math.Clamp(center.X - Width / 2, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - Width)),
            Math.Clamp(center.Y - Height / 2, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - Height)));
    }

    private static void PerformLayoutTree(Control parent)
    {
        parent.PerformLayout();
        foreach (Control child in parent.Controls)
            PerformLayoutTree(child);
    }

    private void GrowForWrappedText()
    {
        var rowGrowth = new Dictionary<RowStyle, int>();
        var clientGrowth = 0;

        foreach (var entry in _controlLayoutBaselines)
        {
            if (entry.Value.FitsSingleLine || entry.Key is not Label label || string.IsNullOrWhiteSpace(label.Text))
                continue;

            var wrappedHeight = MeasureWrappedHeight(label);
            var originalPadding = Math.Max(0, entry.Value.Size.Height - entry.Value.WrappedTextHeight);
            var targetHeight = Math.Max(entry.Value.Size.Height, wrappedHeight + originalPadding);
            var overflow = targetHeight - Math.Max(1, label.ClientSize.Height);
            if (overflow <= 0)
                continue;

            if (label.Parent is TableLayoutPanel table && table.GetRowSpan(label) == 1)
            {
                var row = table.GetRow(label);
                if (row >= 0 && row < table.RowStyles.Count && table.RowStyles[row].SizeType == SizeType.Absolute)
                {
                    var style = table.RowStyles[row];
                    rowGrowth[style] = Math.Max(rowGrowth.GetValueOrDefault(style), overflow);
                    continue;
                }
            }

            if (label.Dock is DockStyle.Top or DockStyle.Bottom)
            {
                label.Height += overflow;
                _resizedControls.Add(label);
                clientGrowth += overflow;
            }
        }

        foreach (var entry in rowGrowth)
        {
            entry.Key.Height += entry.Value;
            clientGrowth += entry.Value;
        }
        if (clientGrowth > 0)
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + clientGrowth);
    }

    private static ColumnStyle? FindAbsoluteTableColumn(Control control)
    {
        if (control.Parent is not TableLayoutPanel table || table.GetColumnSpan(control) != 1)
            return null;

        var column = table.GetColumn(control);
        if (column >= 0 && column < table.ColumnStyles.Count && table.ColumnStyles[column].SizeType == SizeType.Absolute)
            return table.ColumnStyles[column];
        return null;
    }

    private static bool IsTextLayoutControl(Control control) =>
        control is Label or CheckBox or RadioButton or GroupBox;

    private static int MeasureRequiredWidth(Control control)
    {
        if (control is CheckBox or RadioButton)
            return control.GetPreferredSize(Size.Empty).Width;

        var width = TextRenderer.MeasureText(
            control.Text,
            control.Font,
            Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        return width + (control is GroupBox ? 24 : 4);
    }

    private static int MeasureWrappedHeight(Control control)
    {
        return TextRenderer.MeasureText(
            control.Text,
            control.Font,
            new Size(Math.Max(1, control.ClientSize.Width), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
    }
}
