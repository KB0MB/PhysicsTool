using System.Text.Json;
using HKCLTool;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

internal sealed class IconToolPreferences
{
    public string BotwMod { get; set; } = "";
    public string TotkMod { get; set; } = "";
    public IconImageFormat Format { get; set; } = IconImageFormat.Sbitemico;
    public bool TotkSelected { get; set; }
    internal string Mod(bool totk) => totk ? TotkMod : BotwMod;
    internal void SetMod(bool totk, string folder) { if (totk) TotkMod = folder; else BotwMod = folder; }
    internal static IconToolPreferences Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<IconToolPreferences>(File.ReadAllBytes(path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    internal void Save(string path) => AtomicFile.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(this));

    internal static string ModExportDirectory(string folder, bool totk, bool isSwitch, IconImageFormat format, IEnumerable<string> dumps)
    {
        string root = Path.GetFullPath(folder);
        foreach (string dump in dumps.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            string full = Path.GetFullPath(dump).TrimEnd(Path.DirectorySeparatorChar);
            if (root.Equals(full, StringComparison.OrdinalIgnoreCase) || root.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a mod folder outside your game dumps.");
        }
        string layout = totk || isSwitch ? "romfs" : "content";
        if (Directory.Exists(Path.Combine(root, layout))) root = Path.Combine(root, layout);
        else if (Path.GetFileName(root) is not ("romfs" or "content") && !Directory.Exists(Path.Combine(root, "Actor")) && !Directory.Exists(Path.Combine(root, "Pack"))) root = Path.Combine(root, layout);
        return Path.Combine(root, format is IconImageFormat.Bntx or IconImageFormat.BntxZs || totk ? "UI/Tex/Icon" : "UI/StockItem");
    }
}
