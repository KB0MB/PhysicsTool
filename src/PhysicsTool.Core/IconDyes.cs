using HKCLTool;
using ResS = Syroot.NintenTools.NSW.Bfres;
using ResU = Syroot.NintenTools.Bfres;

namespace PhysicsTool.Core;

internal sealed record IconDye(string Animation, int Frame)
{
    internal static readonly IconDye Original = new("", 0);
    private static readonly string[] Names = ["Default", "Blue", "Red", "Yellow", "White", "Black", "Purple", "Green", "Light Blue", "Navy", "Orange", "Peach", "Crimson", "Light Yellow", "Brown", "Gray"];
    public override string ToString() => $"{Frame} - {(Frame >= 0 && Frame < Names.Length ? Names[Frame] : Animation)}";
}

internal sealed class IconDyes
{
    private sealed record Source(ToolboxBfresDocument Document, string Model,
        Dictionary<string, VisualModelTexture> Textures, Dictionary<(string Material, string Sampler), string> Appearance);
    private readonly List<Source> _sources = [];
    private readonly List<IconDye> _choices = [];
    internal IReadOnlyList<IconDye> Choices => _choices;
    internal IconDye Default => _choices.FirstOrDefault(d => d.Frame == 0) ?? _choices.FirstOrDefault() ?? IconDye.Original;

    internal void Add(ToolboxBfresDocument document, string modelName, Dictionary<string, VisualModelTexture> textures,
        Dictionary<(string Material, string Sampler), string> appearance)
    {
        _sources.Add(new(document, modelName, textures, new(appearance)));
        dynamic model = document.Models.First(m => (string)m.Name == modelName);
        var names = ToolboxBfresDocument.Items((object)model.Materials).Select(m => (string)m.Name).ToHashSet();
        IEnumerable<(string Name, int Frames)> animations = document.Resource switch
        {
            ResS.ResFile nx => nx.MaterialAnims.Where(a => a.MaterialAnimDataList.Any(m => names.Contains(m.Name) && m.TexturePatternAnimInfos.Count > 0))
                .Select(a => (a.Name, a.FrameCount)),
            ResU.ResFile wii => wii.TexPatternAnims.Values.Where(a => a.TexPatternMatAnims.Any(m => names.Contains(m.Name) && m.PatternAnimInfos.Count > 0))
                .Select(a => (a.Name, a.FrameCount)),
            _ => []
        };
        foreach (var (name, frames) in animations)
        {
            if (ToolboxAutoMaterialAnimation.IsAutomatic(name) || frames is < 0 or > 256) continue;
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            for (int frame = 0; frame <= frames; frame++)
            {
                var warnings = new List<string>();
                var bindings = ToolboxActorAppearance.Read(document, name, frame, warnings)
                    .Where(p => names.Contains(p.Key.Material)).OrderBy(p => p.Key.Material).ThenBy(p => p.Key.Sampler).ToArray();
                if (warnings.Count > 0 || bindings.Length == 0 || bindings.Any(p => !textures.ContainsKey(p.Value))) continue;
                string key = string.Join('\n', bindings.Select(p => $"{p.Key.Material}/{p.Key.Sampler}={p.Value}"));
                var choice = new IconDye(name, frame);
                if (distinct.Add(key) && !_choices.Contains(choice)) _choices.Add(choice);
            }
        }
    }

    internal List<ToolboxActorModel> Build(IconDye dye, List<string> warnings)
    {
        if (dye == IconDye.Original) dye = Default;
        if (!_choices.Contains(dye)) throw new ArgumentException("This texture pattern is not available for the loaded armor.", nameof(dye));
        var result = new List<ToolboxActorModel>();
        foreach (var source in _sources)
        {
            var appearance = new Dictionary<(string Material, string Sampler), string>(source.Appearance);
            if (dye.Animation.Length > 0)
                foreach (var pair in ToolboxActorAppearance.Read(source.Document, dye.Animation, dye.Frame, warnings)) appearance[pair.Key] = pair.Value;
            // Reapply the mannequin skin after the dye's texture bindings.
            IconMannequinMaterial.Apply(source.Document, source.Textures, appearance);
            result.Add(new(source.Document, source.Model, source.Textures, appearance, warnings));
        }
        return result;
    }

    internal static string FileStem(string actor, IconDye dye, bool multipleAnimations = false)
    {
        string suffix = dye.Animation.Length == 0 || dye.Frame == 0 ? "" : $".{dye.Frame:00}";
        if (multipleAnimations && dye.Animation.Length > 0) suffix = "." + dye.Animation + $".{dye.Frame:00}";
        return string.Concat((actor + suffix).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
