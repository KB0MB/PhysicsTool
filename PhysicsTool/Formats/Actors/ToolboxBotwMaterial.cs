using System.Globalization;
using ResS = Syroot.NintenTools.NSW.Bfres;
using ResU = Syroot.NintenTools.Bfres;

namespace HKCLTool;

internal static class ToolboxBotwMaterial
{
    internal static void Apply(object material, VisualModelMesh mesh, List<string>? warnings)
    {
        if (material is ResS.Material nx && nx.ShaderAssign is { } s && IsBotw(s.ShaderArchiveName))
            Apply(s.ShaderArchiveName,
                Enumerable.Range(0, s.ShaderOptions?.Count ?? 0).ToDictionary(i => s.ShaderOptionDict.GetKey(i), i => s.ShaderOptions![i]),
                Enumerable.Range(0, s.AttribAssigns?.Count ?? 0).ToDictionary(i => s.AttribAssignDict.GetKey(i), i => s.AttribAssigns![i]), mesh, warnings);
        else if (material is ResU.Material wii && wii.ShaderAssign is { } u && IsBotw(u.ShaderArchiveName))
            Apply(u.ShaderArchiveName, u.ShaderOptions?.Keys.ToDictionary(k => k, k => u.ShaderOptions[k].String) ?? new(),
                u.AttribAssigns?.Keys.ToDictionary(k => k, k => u.AttribAssigns[k].String) ?? new(), mesh, warnings);
    }

    private static bool IsBotw(string? archive) => archive?.StartsWith("uking_", StringComparison.OrdinalIgnoreCase) == true;

    internal static void Apply(string archive, IReadOnlyDictionary<string, string> options,
        IReadOnlyDictionary<string, string> attributes, VisualModelMesh mesh, List<string>? warnings)
    {
        if (!IsBotw(archive)) return;
        var settings = mesh.Material;
        settings.UseBotwPreviewShader = true;
        settings.AlbedoUvSet = Resolve(0, settings.AlbedoUvSet);
        settings.SpmUvSet = Resolve(1, settings.SpmUvSet);
        settings.NormalUvSet = Resolve(2, settings.NormalUvSet);

        string Resolve(int slot, string fallback)
        {
            var option = $"uking_texture{slot}_texcoord";
            if (!options.TryGetValue(option, out var value)) return fallback;
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var channel)
                && float.IsFinite(channel) && channel == MathF.Truncate(channel) && channel is >= 0 and <= 5)
            {
                var attribute = $"_u{(int)channel}";
                var source = attributes.GetValueOrDefault(attribute, attribute);
                if (mesh.UvSets.Any(uv => uv.Name == source)) return source;
            }
            warnings?.Add($"Material {mesh.Name}: unsupported or missing UV selection {option}={value}; keeping the preview fallback.");
            return fallback;
        }
    }
}
