namespace PhysicsTool.Desktop;

internal static class AppDataPaths
{
    private static readonly Lazy<string> SettingsDirectory = new(() => ResolveDirectory(AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), OperatingSystem.IsWindows()));

    internal static string SettingsFile(string name) => Path.Combine(SettingsDirectory.Value, name);
    internal static string CacheFile(string name) => SettingsFile(name);

    // Windows ZIPs stay portable when writable. Other platforms opt in with portable.txt.
    internal static string ResolveDirectory(string application, string userData, bool windows)
    {
        var portable = windows || File.Exists(Path.Combine(application, "portable.txt"));
        if (portable && CanWrite(application)) return application;
        var destination = Path.Combine(userData, "PhysicsTool");
        Directory.CreateDirectory(destination);
        foreach (var name in new[] { "settings.json", "actor-folders.json" })
        {
            var source = Path.Combine(application, name); var target = Path.Combine(destination, name);
            if (!File.Exists(target) && File.Exists(source))
            {
                try { File.Copy(source, target, overwrite: false); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return destination;
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            using var probe = new FileStream(Path.Combine(directory, ".physicstool-write-" + Guid.NewGuid().ToString("N")),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
