using HKCLTool;

namespace PhysicsTool.Core;

public enum PhysicsConversionTarget { Hkcl, Bphcl, Bphyssb, Bphhb }
public sealed record PhysicsConversionOptions(HkclPlatform Platform = HkclPlatform.WiiU,
    IReadOnlyDictionary<int, float>? MassScales = null, IReadOnlyDictionary<int, float>? ConstraintScales = null);
public sealed record PhysicsConversionResult(string Format, int Entries);

public sealed class PhysicsConversion
{
    public static HkclService ConvertSelectedCloth(HkclService source, int index, HkclPlatform platform)
    {
        if (source.IsHkrg || source.IsHelperBoneSidecar || (uint)index >= source.GetClothSummaries().Count)
            throw new ArgumentException("Choose a cloth to convert and merge.");
        var copy = source.CopyForConversion();
        var scale = source.IsBphcl ? source.GetBphclConversionScaleSuggestions().Single(s => s.ClothIndex == index) : null;
        for (var i = copy.GetClothSummaries().Count - 1; i >= 0; i--) if (i != index) copy.RemoveCloth(i);
        var conversion = new PhysicsConversion(copy);
        var temporary = Path.Combine(Path.GetTempPath(), "PhysicsTool-merge-" + Guid.NewGuid().ToString("N") + conversion.Extension);
        try
        {
            conversion.Export(temporary, new(platform,
                scale == null ? null : new Dictionary<int, float> { [0] = scale.DefaultMassScale },
                scale == null ? null : new Dictionary<int, float> { [0] = scale.DefaultConstraintScale }));
            var result = new HkclService(); result.Load(temporary); return result;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private readonly HkclService _source;
    private readonly string? _sourcePath;
    private readonly IReadOnlyList<VisualModelBone>? _modelBones;
    public PhysicsConversionTarget Target { get; }
    public string Format => Target.ToString().ToUpperInvariant();
    public string Extension => "." + Target.ToString().ToLowerInvariant();
    public string SuggestedFileName => (string.IsNullOrWhiteSpace(_sourcePath) ? "Physics" : Path.GetFileNameWithoutExtension(_sourcePath)) + Extension;
    public IReadOnlyList<BphclConversionScale> Scales { get; }
    public IReadOnlyList<string> FidelityWarnings { get; }
    public static bool CanConvert(HkclService document) => document.HasDocument && !document.IsHkrg;

    public PhysicsConversion(HkclService source, string? sourcePath = null, IReadOnlyList<VisualModelBone>? modelBones = null)
    {
        if (!CanConvert(source)) throw new InvalidOperationException("Open an HKCL, BPHCL, BPHYSSB or BPHHB file to convert.");
        _source = source.CopyForConversion();
        _sourcePath = sourcePath;
        _modelBones = modelBones?.Select(b => new VisualModelBone { Index = b.Index, ParentIndex = b.ParentIndex,
            Name = b.Name, Sources = b.Sources, Position = b.Position, Rotation = b.Rotation, Scale = b.Scale }).ToArray();
        Target = source.IsBphcl ? PhysicsConversionTarget.Hkcl : source.IsBphyssb ? PhysicsConversionTarget.Bphhb
            : source.IsBphhb ? PhysicsConversionTarget.Bphyssb : PhysicsConversionTarget.Bphcl;
        Scales = Target == PhysicsConversionTarget.Hkcl ? _source.GetBphclConversionScaleSuggestions() : [];
        FidelityWarnings = Target == PhysicsConversionTarget.Hkcl
            ? Enumerable.Range(0, _source.GetClothSummaries().Count).SelectMany(i =>
                _source.GetBendStiffnessConstraintSets(i)
                    .Where(s => s.UseRestPoseConfig && s.ClampBendStiffness && s.Links.Any(l => l.RestCurvature != 0))
                    .Select(s => $"{_source.GetClothName(i)}: TotK's bend-height cutoff cannot be stored in HKCL. " +
                        "Rest curvature and bending weights are preserved, but strongly deformed bends can respond differently in BotW."))
                .Distinct().ToArray()
            : Target == PhysicsConversionTarget.Bphhb ? _source.GetBphhbConversionWarnings() : [];
    }

    public PhysicsConversionResult Export(string path, PhysicsConversionOptions options)
    {
        if (!Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Choose a {Extension} output file.");
        var fullPath = Path.GetFullPath(path);
        if (_sourcePath != null && string.Equals(fullPath, Path.GetFullPath(_sourcePath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Choose an output file different from the source.");
        if (options.Platform is not (HkclPlatform.WiiU or HkclPlatform.Switch))
            throw new ArgumentOutOfRangeException(nameof(options), "Choose Wii U or Switch for HKCL output.");
        var mass = ScaleValues(options.MassScales, s => s.DefaultMassScale);
        var constraints = ScaleValues(options.ConstraintScales, s => s.DefaultConstraintScale);
        var source = _source.CopyForConversion();
        var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, ".conversion-" + Guid.NewGuid().ToString("N") + Extension);
        try
        {
            switch (Target)
            {
                case PhysicsConversionTarget.Hkcl:
                    source.CreateFreshHkclFromCurrentBphclDocument(mass, constraints).SaveHkcl(temporary, options.Platform);
                    break;
                case PhysicsConversionTarget.Bphcl:
                    var graph = HkclToBphclGameReadyGraphBuilder.Build(source);
                    AtomicFile.WriteAllBytes(temporary, NativeBphclStandaloneGraphWriter.WriteGameReady(graph));
                    BphclStandaloneValidation.Validate(NativeBphclDocument.Open(temporary), graph, prunedUnusedColliders: true);
                    break;
                case PhysicsConversionTarget.Bphyssb:
                    source.ExportBphyssb(temporary);
                    break;
                case PhysicsConversionTarget.Bphhb:
                    source.ExportBphhb(temporary, _modelBones);
                    break;
            }
            var check = new HkclService(); check.Load(temporary);
            var expectedType = Target switch { PhysicsConversionTarget.Hkcl => !check.IsReadOnlyExternal,
                PhysicsConversionTarget.Bphcl => check.IsBphcl, PhysicsConversionTarget.Bphyssb => check.IsBphyssb, _ => check.IsBphhb };
            if (!check.HasDocument || !expectedType) throw new InvalidDataException("The converted file could not be reopened in the expected format.");
            var count = check.GetClothSummaries().Count;
            if (Target is PhysicsConversionTarget.Hkcl or PhysicsConversionTarget.Bphcl)
            {
                if (count != source.GetClothSummaries().Count) throw new InvalidDataException("Conversion changed the number of cloths.");
                for (var i = 0; i < count; i++)
                    if (check.GetParticleRows(i).Count != source.GetParticleRows(i).Count)
                        throw new InvalidDataException($"Conversion changed the particle count in cloth {i}.");
            }
            // Validation happens before replacing an existing user output, on the same filesystem.
            File.Move(temporary, fullPath, overwrite: true);
            return new(Format, count);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private Dictionary<int, float> ScaleValues(IReadOnlyDictionary<int, float>? values, Func<BphclConversionScale, float> fallback)
    {
        if (values != null && values.Keys.Any(i => !Scales.Any(s => s.ClothIndex == i)))
            throw new ArgumentException("Conversion scales reference a cloth that is not in this source.");
        var result = Scales.ToDictionary(s => s.ClothIndex, s => values != null && values.TryGetValue(s.ClothIndex, out var v) ? v : fallback(s));
        if (result.Values.Any(v => !float.IsFinite(v) || v <= 0)) throw new ArgumentException("Conversion scales must be positive finite numbers.");
        return result;
    }
}
