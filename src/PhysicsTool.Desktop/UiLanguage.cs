using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using PhysicsTool.Core;
using HKCLTool;

namespace PhysicsTool.Desktop;

internal sealed record LanguageChoice(string Code, string Name) { public override string ToString() => Name; }
internal sealed record DesktopPreferences(string Language = "en", bool DarkTheme = true, int AnimationFps = 60,
    HkclPlatform ConversionPlatform = HkclPlatform.WiiU)
{
    internal static string FilePath => AppDataPaths.SettingsFile("settings.json");
    internal static DesktopPreferences Load(string? path = null)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path ?? FilePath));
            var language = root?["Language"]?.GetValue<string>() ?? "en";
            return new(UiLanguage.Languages.Any(l => l.Code == language) ? language : "en", root?["DarkTheme"]?.GetValue<bool>() ?? true,
                root?["AnimationFramesPerSecond"]?.GetValue<int>() == 30 ? 30 : 60,
                root?["ConversionPlatform"]?.GetValue<string>() == "Switch" ? HkclPlatform.Switch : HkclPlatform.WiiU);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return new(); }
    }
    internal void Save(string? path = null)
    {
        var file = path ?? FilePath;
        var root = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? new() : new JsonObject();
        root["Language"] = Language; root["DarkTheme"] = DarkTheme; root["AnimationFramesPerSecond"] = AnimationFps;
        root["ConversionPlatform"] = ConversionPlatform.ToString();
        HKCLTool.AtomicFile.WriteAllBytes(file, JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal static class UiLanguage
{
    internal static readonly LanguageChoice[] Languages = [new("en", "English"), new("fr", "Français"), new("es", "Español"), new("ja", "日本語")];
    private sealed class Binding(AvaloniaObject target, AvaloniaProperty property, string source)
    {
        internal readonly WeakReference<AvaloniaObject> Target = new(target);
        internal readonly AvaloniaProperty Property = property;
        internal string Source = source, Display = source;
    }
    private static readonly List<Binding> Bindings = [];
    private static readonly ConditionalWeakTable<AvaloniaObject, Dictionary<AvaloniaProperty, Binding>> ByTarget = new();
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = new();
    private static bool _initialized, _applying;
    internal static string Current { get; private set; } = "en";
    internal static event Action? Changed;
    internal static string Text(string source) => Current != "en" && Catalogs.TryGetValue(Current, out var values) && values.TryGetValue(source, out var translated) ? translated : source;
    internal static void Initialize(string code)
    {
        if (!_initialized)
        {
            _initialized = true;
            var assembly = typeof(UiLanguage).Assembly;
            foreach (var language in Languages.Where(l => l.Code != "en"))
            {
                var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith("." + language.Code + ".json", StringComparison.Ordinal)).OrderBy(n => n.Contains("Avalonia.") ? 1 : 0))
                {
                    using var stream = assembly.GetManifestResourceStream(resource)!;
                    foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!) catalog[pair.Key] = pair.Value;
                }
                // Stable topic keys avoid repeating entire English articles in each catalog.
                foreach (var topic in ClothHelp.Topics)
                    if (catalog.TryGetValue("Help." + topic.Title, out var body)) catalog[topic.Body] = body;
                foreach (var kind in ClothConstraintCatalog.Kinds)
                    if (catalog.TryGetValue("Constraint." + kind.Name, out var description)) catalog[kind.Description] = description;
                Catalogs[language.Code] = catalog;
            }
            TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((c, e) => Track(c, TextBlock.TextProperty, e.NewValue as string));
            TextBox.PlaceholderTextProperty.Changed.AddClassHandler<TextBox>((c, e) => Track(c, TextBox.PlaceholderTextProperty, e.NewValue as string));
            ContentControl.ContentProperty.Changed.AddClassHandler<ContentControl>((c, e) => Track(c, ContentControl.ContentProperty, e.NewValue as string));
            HeaderedItemsControl.HeaderProperty.Changed.AddClassHandler<HeaderedItemsControl>((c, e) => Track(c, HeaderedItemsControl.HeaderProperty, e.NewValue as string));
            MenuItem.HeaderProperty.Changed.AddClassHandler<MenuItem>((c, e) => Track(c, MenuItem.HeaderProperty, e.NewValue as string));
            HeaderedContentControl.HeaderProperty.Changed.AddClassHandler<HeaderedContentControl>((c, e) => Track(c, HeaderedContentControl.HeaderProperty, e.NewValue as string));
            Window.TitleProperty.Changed.AddClassHandler<Window>((c, e) => Track(c, Window.TitleProperty, e.NewValue as string));
            ToolTip.TipProperty.Changed.AddClassHandler<Control>((c, e) => Track(c, ToolTip.TipProperty, e.NewValue as string));
        }
        Set(code);
    }
    private static void Track(AvaloniaObject target, AvaloniaProperty property, string? text)
    {
        if (_applying || text == null) return;
        var properties = ByTarget.GetOrCreateValue(target);
        if (Bindings.Count % 1024 == 1023) Bindings.RemoveAll(b => !b.Target.TryGetTarget(out _));
        if (!properties.TryGetValue(property, out var binding)) { binding = new(target, property, text); properties.Add(property, binding); Bindings.Add(binding); }
        else if (text != binding.Display) binding.Source = text;
        Apply(binding, target);
    }
    private static void Apply(Binding binding, AvaloniaObject target)
    {
        binding.Display = Text(binding.Source);
        _applying = true;
        try { target.SetCurrentValue(binding.Property, binding.Display); }
        finally { _applying = false; }
    }
    internal static void Set(string code)
    {
        if (!Languages.Any(l => l.Code == code)) throw new ArgumentException("Unknown interface language.");
        Current = code;
        for (var i = Bindings.Count - 1; i >= 0; i--)
            if (Bindings[i].Target.TryGetTarget(out var target)) Apply(Bindings[i], target); else Bindings.RemoveAt(i);
        Changed?.Invoke();
    }
}
