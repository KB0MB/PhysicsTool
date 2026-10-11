using Jint;
using Jint.Runtime;
using System.Reflection;

namespace HKCLTool;

internal static class IceHailRuntime
{
    private static readonly string ResourcePrefix = FindResourcePrefix();
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Modules = new(LoadModules);
    private static readonly Lazy<string> Runtime = new(() => Modules.Value["runtime.js"]);

    public static byte[] Generate(string json)
    {
        var engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromMinutes(5)));
        engine.SetValue("loadIceHailModule", (Func<string, string?>)LoadModule);
        engine.Execute(Runtime.Value);
        try
        {
            var encoded = engine.Invoke("generateIceHail", json).AsString();
            return Convert.FromBase64String(encoded);
        }
        catch (JavaScriptException ex)
        {
            throw new InvalidDataException(
                "The embedded collision writer could not create this mesh: " + ex.Message,
                ex);
        }
    }

    public static string ReadCompoundTemplate()
    {
        var assembly = typeof(IceHailRuntime).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + "compound.json")
            ?? throw new InvalidOperationException("The embedded Ice-Hail compound template is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? LoadModule(string id) =>
        Modules.Value.TryGetValue(id.Replace('\\', '/'), out var source) ? source : null;

    private static string FindResourcePrefix()
    {
        const string suffix = ".Formats.Collision.IceHail.runtime.js";
        var resource = typeof(IceHailRuntime).Assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The embedded collision runtime is missing.");
        return resource[..^"runtime.js".Length];
    }

    private static IReadOnlyDictionary<string, string> LoadModules()
    {
        var assembly = typeof(IceHailRuntime).Assembly;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !resourceName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                continue;
            var suffix = resourceName[ResourcePrefix.Length..];
            var id = suffix[..^3].Replace('.', '/') + ".js";
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            result[id] = reader.ReadToEnd();
        }
        return result;
    }
}
