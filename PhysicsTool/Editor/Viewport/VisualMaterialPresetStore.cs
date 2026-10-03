using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HKCLTool;

public static class VisualMaterialPresetStore
{
    private const int CurrentVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string SharedPresetPath => PortablePresetPath;

    private static string PortablePresetPath => Path.Combine(AppContext.BaseDirectory, "presets.json");

    private static string LegacySharedPresetPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhysicsTool",
        "presets.json");

    public static IReadOnlyDictionary<string, VisualMaterialLayoutEntry> GetMaterialLayouts(
        string modelPath,
        ICollection<string>? warnings = null)
    {
        var database = LoadDatabase(warnings);
        var fileName = Path.GetFileName(modelPath);
        var preset = database.Models
            .Where(candidate => candidate.ModelFile.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.UpdatedUtc)
            .FirstOrDefault();
        if (preset == null)
        {
            preset = ImportLegacySidecar(database, modelPath, warnings);
            if (preset != null)
                SaveDatabase(database);
        }

        return preset?.Materials
            .Where(entry => !string.IsNullOrWhiteSpace(entry.MeshName) && entry.Layout != null)
            .GroupBy(entry => entry.MeshName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Layout!, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, VisualMaterialLayoutEntry>(StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryApply(VisualModelData model)
    {
        var database = LoadDatabase(model.Warnings);
        var modelFile = Path.GetFileName(model.SourcePath);
        var preset = database.Models
            .Where(candidate => candidate.ModelFile.Equals(modelFile, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.UpdatedUtc)
            .FirstOrDefault();
        var signature = CreateSignature(model);
        if (preset == null && TryLoadLegacyHashedPreset(model, out var legacy))
        {
            preset = legacy;
            database.Models.RemoveAll(candidate =>
                candidate.Signature.Equals(signature, StringComparison.Ordinal));
            database.Models.Add(preset);
            SaveDatabase(database);
        }
        if (preset == null)
            return false;

        var entries = preset.Materials
            .Where(entry => entry.Settings != null)
            .GroupBy(entry => entry.MeshName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(entry => entry.Occurrence).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var applied = 0;
        foreach (var mesh in model.Meshes)
        {
            occurrences.TryGetValue(mesh.Name, out var occurrence);
            occurrences[mesh.Name] = occurrence + 1;
            if (!entries.TryGetValue(mesh.Name, out var matching) || occurrence >= matching.Length)
                continue;
            mesh.Material = Sanitize(matching[occurrence].Settings!, mesh);
            applied++;
        }

        return applied > 0;
    }

    public static string Save(
        VisualModelData model,
        IReadOnlyDictionary<int, VisualModelMaterialSettings>? settings = null)
    {
        var database = LoadDatabase(model.Warnings);
        var signature = CreateSignature(model);
        var fileName = Path.GetFileName(model.SourcePath);

        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var materials = new List<PresetMaterial>();
        for (var index = 0; index < model.Meshes.Count; index++)
        {
            var mesh = model.Meshes[index];
            occurrences.TryGetValue(mesh.Name, out var occurrence);
            occurrences[mesh.Name] = occurrence + 1;
            var selected = settings != null && settings.TryGetValue(index, out var edited)
                ? edited
                : mesh.Material;
            materials.Add(new PresetMaterial
            {
                MeshName = mesh.Name,
                MeshIdentity = mesh.MaterialIdentity,
                Occurrence = occurrence,
                Settings = selected.Clone(),
                Layout = CreateLayout(mesh, selected)
            });
        }

        database.Models.RemoveAll(candidate =>
            candidate.Signature.Equals(signature, StringComparison.Ordinal) ||
            candidate.ModelFile.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        database.Models.Add(new ModelPreset
        {
            Name = Path.GetFileNameWithoutExtension(model.SourcePath),
            ModelFile = fileName,
            Signature = signature,
            UpdatedUtc = DateTime.UtcNow,
            Materials = materials
        });
        SaveDatabase(database);
        return SharedPresetPath;
    }

    private static PresetDatabase LoadDatabase(ICollection<string>? warnings)
    {
        var presetPath = File.Exists(PortablePresetPath)
            ? PortablePresetPath
            : File.Exists(LegacySharedPresetPath)
                ? LegacySharedPresetPath
                : PortablePresetPath;
        if (!File.Exists(presetPath))
            return new PresetDatabase();
        try
        {
            var result = JsonSerializer.Deserialize<PresetDatabase>(
                File.ReadAllText(presetPath), JsonOptions);
            var database = result?.Version == CurrentVersion ? result : new PresetDatabase();
            if (!string.Equals(presetPath, PortablePresetPath, StringComparison.OrdinalIgnoreCase))
                SaveDatabase(database);
            return database;
        }
        catch (Exception ex)
        {
            warnings?.Add($"Could not read shared material presets: {ex.Message}");
            return new PresetDatabase();
        }
    }

    private static void SaveDatabase(PresetDatabase database)
    {
        database.Version = CurrentVersion;
        Directory.CreateDirectory(Path.GetDirectoryName(PortablePresetPath)!);
        var temporary = PortablePresetPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(database, JsonOptions));
        File.Move(temporary, PortablePresetPath, true);
    }

    private static ModelPreset? ImportLegacySidecar(
        PresetDatabase database,
        string modelPath,
        ICollection<string>? warnings)
    {
        var sidecarPath = Path.ChangeExtension(modelPath, ".materials.json");
        if (!File.Exists(sidecarPath))
            return null;
        try
        {
            var file = JsonSerializer.Deserialize<LegacyLayoutFile>(
                File.ReadAllText(sidecarPath), JsonOptions);
            if (file?.Materials.Count is not > 0)
                return null;
            var preset = new ModelPreset
            {
                Name = Path.GetFileNameWithoutExtension(modelPath),
                ModelFile = Path.GetFileName(modelPath),
                UpdatedUtc = DateTime.UtcNow,
                Materials = file.Materials.Select(layout => new PresetMaterial
                {
                    MeshName = layout.Mesh,
                    Layout = layout
                }).ToList()
            };
            database.Models.Add(preset);
            return preset;
        }
        catch (Exception ex)
        {
            warnings?.Add($"Could not migrate material layout '{Path.GetFileName(sidecarPath)}': {ex.Message}");
            return null;
        }
    }

    private static bool TryLoadLegacyHashedPreset(
        VisualModelData model,
        out ModelPreset preset)
    {
        preset = new ModelPreset();
        var sourceName = Path.GetFileNameWithoutExtension(model.SourcePath);
        var safeName = string.Concat(sourceName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var signature = CreateSignature(model);
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhysicsTool",
            "MaterialPresets",
            $"{safeName}.{signature[..12]}.json");
        if (!File.Exists(path))
            return false;
        try
        {
            var legacy = JsonSerializer.Deserialize<LegacyPresetFile>(
                File.ReadAllText(path), JsonOptions);
            if (legacy == null || !legacy.Signature.Equals(signature, StringComparison.Ordinal))
                return false;
            preset = new ModelPreset
            {
                Name = sourceName,
                ModelFile = Path.GetFileName(model.SourcePath),
                Signature = signature,
                UpdatedUtc = DateTime.UtcNow,
                Materials = legacy.Materials
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static VisualMaterialLayoutEntry CreateLayout(
        VisualModelMesh mesh,
        VisualModelMaterialSettings settings) => new()
    {
        Mesh = mesh.Name,
        Shader = settings.ShaderModel.ToString(),
        Albedo = TextureReference(mesh.Texture),
        Spm = TextureReference(mesh.SpecularTexture),
        Normal = TextureReference(mesh.NormalTexture),
        AmbientOcclusion = TextureReference(mesh.AmbientOcclusionTexture),
        Auxiliary = TextureReference(mesh.AuxiliaryTexture),
        AlbedoUvSet = settings.AlbedoUvSet,
        SpmUvSet = settings.SpmUvSet,
        NormalUvSet = settings.NormalUvSet,
        AmbientOcclusionUvSet = settings.AmbientOcclusionUvSet,
        AuxiliaryUvSet = settings.AuxiliaryUvSet,
        MetalChannel = settings.MetalChannel.ToString(),
        SmoothnessChannel = settings.SmoothnessChannel.ToString(),
        MetalScale = settings.MetalScale,
        SmoothnessScale = settings.SmoothnessScale,
        NormalStrength = settings.NormalStrength,
        AmbientOcclusionStrength = settings.AmbientOcclusionStrength,
        AuxiliaryStrength = settings.AuxiliaryStrength,
        EyeGlintStrength = settings.EyeGlintStrength
    };

    private static string TextureReference(VisualModelTexture? texture) =>
        texture == null || string.IsNullOrWhiteSpace(texture.SourcePath)
            ? string.Empty
            : MakePortableTextureReference(texture.SourcePath);

    private static string MakePortableTextureReference(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var appRelative = TryMakeRelativePath(AppContext.BaseDirectory, fullPath);
        if (!string.IsNullOrWhiteSpace(appRelative))
            return appRelative;

        return Path.GetFileName(fullPath);
    }

    private static string? TryMakeRelativePath(string rootPath, string fullPath)
    {
        try
        {
            var root = Path.GetFullPath(rootPath);
            if (!root.EndsWith(Path.DirectorySeparatorChar))
                root += Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(fullPath);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return null;
            return Path.GetRelativePath(root, path);
        }
        catch
        {
            return null;
        }
    }

    private static VisualModelMaterialSettings Sanitize(
        VisualModelMaterialSettings settings,
        VisualModelMesh mesh)
    {
        var result = settings.Clone();
        result.AlbedoUvSet = ResolveUv(result.AlbedoUvSet, mesh);
        result.SpmUvSet = ResolveUv(result.SpmUvSet, mesh);
        result.NormalUvSet = ResolveUv(result.NormalUvSet, mesh);
        result.AmbientOcclusionUvSet = ResolveUv(result.AmbientOcclusionUvSet, mesh);
        result.AuxiliaryUvSet = ResolveUv(result.AuxiliaryUvSet, mesh);
        result.UseAlbedo &= mesh.Texture != null;
        result.UseSpm &= mesh.SpecularTexture != null;
        result.UseNormal &= mesh.NormalTexture != null;
        result.UseAmbientOcclusion &= mesh.AmbientOcclusionTexture != null;
        result.UseAuxiliary &= mesh.AuxiliaryTexture != null;
        result.UseEyeGlint &= mesh.EyeGlintTexture != null;
        result.MetalScale = Math.Clamp(result.MetalScale, 0.0f, 2.0f);
        result.SmoothnessScale = Math.Clamp(result.SmoothnessScale, 0.0f, 2.0f);
        result.NormalStrength = Math.Clamp(result.NormalStrength, 0.0f, 2.0f);
        result.AmbientOcclusionStrength = Math.Clamp(result.AmbientOcclusionStrength, 0.0f, 1.0f);
        result.AuxiliaryStrength = Math.Clamp(result.AuxiliaryStrength, 0.0f, 2.0f);
        result.EyeGlintStrength = Math.Clamp(result.EyeGlintStrength, 0.0f, 2.0f);
        return result;
    }

    private static string ResolveUv(string requested, VisualModelMesh mesh) =>
        mesh.UvSets.FirstOrDefault(uv =>
            uv.Name.Equals(requested, StringComparison.OrdinalIgnoreCase))?.Name
        ?? mesh.UvSets.FirstOrDefault()?.Name
        ?? string.Empty;

    private static string CreateSignature(VisualModelData model)
    {
        var identities = model.Meshes
            .Select(mesh => mesh.MaterialIdentity)
            .OrderBy(identity => identity, StringComparer.Ordinal)
            .ToArray();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", identities)));
        return Convert.ToHexString(bytes);
    }

    private sealed class PresetDatabase
    {
        public int Version { get; set; } = CurrentVersion;
        public List<ModelPreset> Models { get; set; } = new();
    }

    private sealed class ModelPreset
    {
        public string Name { get; set; } = string.Empty;
        public string ModelFile { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
        public DateTime UpdatedUtc { get; set; }
        public List<PresetMaterial> Materials { get; set; } = new();
    }

    private sealed class PresetMaterial
    {
        public string MeshName { get; set; } = string.Empty;
        public string MeshIdentity { get; set; } = string.Empty;
        public int Occurrence { get; set; }
        public VisualModelMaterialSettings? Settings { get; set; }
        public VisualMaterialLayoutEntry? Layout { get; set; }
    }

    private sealed class LegacyLayoutFile
    {
        public List<VisualMaterialLayoutEntry> Materials { get; set; } = new();
    }

    private sealed class LegacyPresetFile
    {
        public string Signature { get; set; } = string.Empty;
        public List<PresetMaterial> Materials { get; set; } = new();
    }
}

public sealed class VisualMaterialLayoutEntry
{
    public string Mesh { get; set; } = string.Empty;
    public string Shader { get; set; } = string.Empty;
    public string Albedo { get; set; } = string.Empty;
    public string Spm { get; set; } = string.Empty;
    public string Normal { get; set; } = string.Empty;
    public string AmbientOcclusion { get; set; } = string.Empty;
    public string Auxiliary { get; set; } = string.Empty;
    public string AlbedoUvSet { get; set; } = string.Empty;
    public string SpmUvSet { get; set; } = string.Empty;
    public string NormalUvSet { get; set; } = string.Empty;
    public string AmbientOcclusionUvSet { get; set; } = string.Empty;
    public string AuxiliaryUvSet { get; set; } = string.Empty;
    public string MetalChannel { get; set; } = string.Empty;
    public string SmoothnessChannel { get; set; } = string.Empty;
    public float MetalScale { get; set; } = 1.0f;
    public float SmoothnessScale { get; set; } = 1.0f;
    public float NormalStrength { get; set; } = 1.0f;
    public float AmbientOcclusionStrength { get; set; } = 1.0f;
    public float AuxiliaryStrength { get; set; } = 1.0f;
    public float EyeGlintStrength { get; set; } = 0.65f;
}
