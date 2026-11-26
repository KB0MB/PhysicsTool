using System.Numerics;
using BymlLibrary;
using HKCLTool;

namespace PhysicsTool.Core
{
    internal sealed record IconActor(string Name, string Path, string ResourceRoot)
    {
        public override string ToString()
        {
            var folder = new DirectoryInfo(ResourceRoot);
            string source = folder.Name is "content" or "romfs" ? folder.Parent?.Name ?? folder.Name : folder.Name;
            return Name + " [" + source + "]";
        }
    }

    internal sealed record IconCapturePreset(string Name, Vector3 Rotation, Vector3? CameraPosition,
        Vector3? LightDirection, int Size, string Source, bool RequiresPose)
    {
        public float? FieldOfView { get; init; }
        public string ModelKey { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public override string ToString() => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
    }

    internal sealed record IconCaptureAsset(string Name, VisualModelData Model,
        IReadOnlyList<IconCapturePreset> Presets, VisualModelTexture? Reference, IReadOnlyList<string> Warnings,
        IReadOnlyList<ToolboxAutoMaterialAnimation> Materials, string CaptureGame)
    {
        internal VisualModelData? RestModel { get; init; }
        internal string? PoseName { get; init; }
        internal IconArmorPose? ArmorPose { get; init; }
        internal IconDyes? Dyes { get; init; }
        internal Func<IconDye, IconCaptureAsset>? WithDye { get; init; }
        internal string? IconTemplate { get; init; }
        internal string? BntxTemplate { get; init; }
        internal bool IsSwitch { get; init; }
        internal string CharacterId { get; init; } = "Link";
        internal bool HasSimulationData { get; init; }
        internal IReadOnlyList<string> ResourceRoots { get; init; } = [];
        internal IReadOnlySet<string> HiddenRaulMaterials { get; init; } = new HashSet<string>(StringComparer.Ordinal);
        internal bool IsMeshHiddenByDefault(string mesh) => IconArmorProps.HiddenByDefault(Name, CaptureGame == "TotK", mesh, HiddenRaulMaterials);
        internal static string Root(string path)
        {
            path = System.IO.Path.GetFullPath(path);
            if (Directory.Exists(System.IO.Path.Combine(path, "romfs"))) path = System.IO.Path.Combine(path, "romfs");
            if (Directory.Exists(System.IO.Path.Combine(path, "content"))) path = System.IO.Path.Combine(path, "content");
            return path;
        }

        internal static IconActor[] Catalog(string root, List<string>? warnings = null)
        {
            warnings ??= [];
            var roots = BotwActorCatalog.FindResourceRoots([root], warnings, CancellationToken.None);
            var result = new List<IconActor>();
            foreach (var resourceRoot in roots)
            foreach (var directory in new[] { "Pack/Actor", "Actor/Pack", "" })
            {
                var folder = System.IO.Path.Combine(resourceRoot, directory);
                if (!Directory.Exists(folder)) continue;
                try
                {
                    result.AddRange(Directory.EnumerateFiles(folder).Where(BotwActorCatalog.IsActorPack)
                        .Select(p => new IconActor(p.EndsWith(".pack.zs", StringComparison.OrdinalIgnoreCase)
                            ? System.IO.Path.GetFileName(p)[..^8] : System.IO.Path.GetFileNameWithoutExtension(p), p, resourceRoot)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add(folder + ": " + ex.Message); }
            }
            return result.DistinctBy(a => a.Path, StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.ResourceRoot, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal static IconCaptureAsset Load(string path, string root, CancellationToken cancellation = default, string? botwMannequinRoot = null, IEnumerable<string>? fallbackRoots = null, IconCharacterProfile? character = null)
        {
            character ??= IconCharacterProfile.Link;
            root = Root(root);
            var resources = new BotwActorResources(path, new[] { root }.Concat(CompatibleFallbackRoots(path, fallbackRoots ?? [])));
            resources.PrepareTotkStrings();
            var hiddenRaulMaterials = IconArmorProps.HiddenRaulMaterials(resources);
            var textures = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
            var materialAnimations = new List<ToolboxAutoMaterialAnimation>();
            var model = new VisualModelData { SourcePath = path, BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue) };
            var restModel = new VisualModelData { SourcePath = path, BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue) };
            var armorPose = LoadArmorPose(resources, cancellation);
            var armorModels = new List<ToolboxActorModel>();
            var dyes = resources.ActorName.StartsWith("Armor_", StringComparison.Ordinal) ? new IconDyes() : null;
            bool sharedLoaded = false;
            bool posed = false;
            foreach (var group in resources.Models.GroupBy(m => m.Resource))
            {
                cancellation.ThrowIfCancellationRequested();
                var bytes = resources.ReadModel(group.Key) ?? throw new FileNotFoundException("Model not found: " + group.Key);
                var document = new ToolboxBfresDocument(bytes);
                document.ReadTextures(textures, resources.Warnings);
                if (resources.IsTotk) TexToGoTexture.Load(document, resources, textures, cancellation);
                else foreach (var suffix in document.IsSwitch ? new[] { ".Tex" } : new[] { ".Tex1" })
                    if (resources.ReadModel(group.Key, suffix) is { } textureData) new ToolboxBfresDocument(textureData).ReadTextures(textures, resources.Warnings);
                if (!sharedLoaded && !resources.IsTotk && resources.ActorName.StartsWith("Armor_", StringComparison.Ordinal))
                {
                    sharedLoaded = true;
                    LoadSharedArmorTextures(resources, textures, cancellation);
                }
                var appearance = ToolboxActorAppearance.Read(document, resources.VariationMatAnim, resources.VariationMatAnimFrame, resources.Warnings);
                character.ApplyTextureColors(textures);
                if (resources.ActorName.StartsWith("Armor_", StringComparison.Ordinal))
                    IconMannequinMaterial.Apply(document, textures, appearance);
                foreach (var reference in group)
                {
                    var loaded = new ToolboxActorModel(document, reference.Model, textures, appearance, resources.Warnings);
                    dyes?.Add(document, reference.Model, textures, appearance);
                    armorModels.Add(loaded);
                    materialAnimations.Add(loaded.AutoMaterials);
                    foreach (var mesh in loaded.RestPose.Meshes) ShrineSceneAssets.AddMesh(restModel, mesh, Matrix4x4.Identity, mesh.Name);
                    var source = loaded.RestPose;
                    if (armorPose != null && source.Bones.Any(b => armorPose.BoneNames.Contains(b.Name, StringComparer.OrdinalIgnoreCase)))
                    {
                        source = loaded.Pose(armorPose, 0);
                        posed = true;
                    }
                    else if (armorPose != null) resources.Warnings.Add("No Nml_Wait bone tracks match " + reference.Model + "; using its rest pose.");
                    foreach (var mesh in source.Meshes) ShrineSceneAssets.AddMesh(model, mesh, Matrix4x4.Identity, mesh.Name);
                }
            }
            if (model.Meshes.Count == 0) throw new InvalidDataException("This actor has no renderable model.");
            bool headMannequin = false;
            ToolboxActorModel? mannequinModel = null;
            bool headArmor = resources.ActorName.StartsWith("Armor_", StringComparison.Ordinal) && resources.ActorName.EndsWith("_Head", StringComparison.Ordinal);
            var propSource = character.IsLink && resources.ActorName.StartsWith("Armor_", StringComparison.Ordinal)
                ? IconHeadMannequin.ResolveSource(resources, botwMannequinRoot, cancellation) : null;
            var props = IconArmorProps.Load(resources, cancellation, out var characterPose, propSource, character);
            // A custom mannequin's authored pose must drive both the armor and its
            // props. Applying Link's recovered pose afterwards breaks their fit.
            if (characterPose != null) { armorPose = characterPose; posed = true; }
            var eyesModel = character.IsLink && headArmor && props != null && propSource != null ? IconMannequinEyes.Load(propSource) : null;
            if (props != null || headArmor && character.IsLink)
            {
                var mannequin = props ?? IconHeadMannequin.Load(resources, botwMannequinRoot, cancellation);
                if (mannequin != null)
                {
                    armorModels.Add(mannequin); headMannequin = headArmor;
                    mannequinModel = mannequin;
                    foreach (var mesh in mannequin.RestPose.Meshes) ShrineSceneAssets.AddMesh(restModel, mesh, Matrix4x4.Identity, IconArmorProps.MeshName(mannequin, mesh.Name));
                    foreach (var mesh in mannequin.Pose(armorPose, 0).Meshes) ShrineSceneAssets.AddMesh(model, mesh, Matrix4x4.Identity, IconArmorProps.MeshName(mannequin, mesh.Name));
                }
                else resources.Warnings.Add("BotW Link resources are unavailable for the icon mannequin.");
            }
            if (eyesModel != null)
            {
                armorModels.Add(eyesModel);
                foreach (var mesh in eyesModel.RestPose.Meshes) ShrineSceneAssets.AddMesh(restModel, mesh, Matrix4x4.Identity, IconArmorProps.MeshName(eyesModel, mesh.Name));
                foreach (var mesh in eyesModel.Pose(armorPose, 0).Meshes) ShrineSceneAssets.AddMesh(model, mesh, Matrix4x4.Identity, IconArmorProps.MeshName(eyesModel, mesh.Name));
            }
            var presets = new List<IconCapturePreset>();
            VisualModelTexture? referenceIcon = null;
            var captureResources = resources.IsTotk ? resources : null;
            if (captureResources != null)
            {
                try
                {
                    presets.AddRange(ReadCapturePresets(captureResources));
                }
                catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException)
                { resources.Warnings.Add("Capture settings: " + ex.Message); }
                var iconFile = resources.Roots.Select(r => Path.Combine(r, "UI", "Tex", "Icon", resources.ActorName + ".bntx.zs")).FirstOrDefault(File.Exists) ?? "";
                if (resources.IsTotk && File.Exists(iconFile))
                {
                    try { referenceIcon = TexToGoTexture.DecodeBntx(new TotkResourceCompression().Decode(File.ReadAllBytes(iconFile), resources.Roots), resources.ActorName); }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                    { resources.Warnings.Add("Reference icon: " + ex.Message); }
                }
            }
            if (!resources.IsTotk)
            {
                var iconFile = resources.Roots.Select(r => Path.Combine(r, "UI", "StockItem", resources.ActorName + ".sbitemico")).FirstOrDefault(File.Exists) ?? "";
                if (File.Exists(iconFile))
                {
                    try
                    {
                        var icons = new Dictionary<string, VisualModelTexture>();
                        new ToolboxBfresDocument(File.ReadAllBytes(iconFile)).ReadTextures(icons, resources.Warnings);
                        referenceIcon = icons.Values.FirstOrDefault();
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                    { resources.Warnings.Add("Reference icon: " + ex.Message); }
                }
            }
            if (presets.Count == 0) presets.Add(new("Manual", Vector3.Zero, null, null, 576, "", false));
            var editablePose = new IconArmorPose(armorModels, armorPose, headMannequin, resources.ArmorEarRotationDegrees, characterPose != null);
            if (props != null && character.NeckSplices.TryGetValue(resources.ActorName, out var neckSource) &&
                armorModels.Where(m=>!IconArmorProps.IsModel(m)).SelectMany(m=>m.RestPose.Meshes).SingleOrDefault(m=>m.Name==neckSource) is { } neckline)
                editablePose.NeckSplice = new IconNeckSplice(neckline);
            if (characterPose != null) model = editablePose.Apply(new Dictionary<string, Vector3>());
            try { if (props == null && character.IsLink) editablePose.Belt = IconArmorBelt.Load(resources, cancellation); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            { resources.Warnings.Add("Optional icon belt: " + ex.Message); }
            var templateFolder = resources.Roots.Select(r => Path.Combine(r, "UI", "StockItem")).FirstOrDefault(Directory.Exists) ?? "";
            string? template = null;
            if (!resources.IsTotk && Directory.Exists(templateFolder))
                template = new[] { Path.Combine(templateFolder, resources.ActorName + ".sbitemico"), Path.Combine(templateFolder, "Armor_001_Upper.sbitemico") }
                    .FirstOrDefault(File.Exists);
            var result = new IconCaptureAsset(resources.ActorName, model, presets, referenceIcon, resources.Warnings.ToArray(), materialAnimations, captureResources == null ? "Manual" : "TotK")
            { RestModel = posed ? restModel : null, PoseName = posed ? armorPose!.Name : null,
                ArmorPose = editablePose, Dyes = dyes, IconTemplate = template, IsSwitch = resources.IsSwitch, ResourceRoots = resources.Roots, CharacterId = character.Id,
                HiddenRaulMaterials = hiddenRaulMaterials,
                HasSimulationData = resources.PhysicsFiles.Any(file =>
                    Path.GetExtension(file).ToLowerInvariant() is ".hkcl" or ".bphcl" or ".bphyssb" or ".bphhb" && resources.Read(file) is { Length: > 0 }),
                BntxTemplate = resources.IsTotk ? resources.Roots.SelectMany(r => new[] { Path.Combine(r, "UI/Tex/Icon", resources.ActorName + ".bntx.zs"), Path.Combine(r, "UI/Tex/Icon/Armor_001_Head.bntx.zs") }).FirstOrDefault(File.Exists) : resources.IsSwitch ? template : null };
            if (dyes is { Choices.Count: > 1 })
                result = result with { WithDye = dye =>
                {
                    var warnings = new List<string>();
                    var models = dyes.Build(dye, warnings);
                    if (mannequinModel != null) models.Add(mannequinModel);
                    if (eyesModel != null) models.Add(eyesModel);
                    var pose = new IconArmorPose(models, armorPose, headMannequin, resources.ArmorEarRotationDegrees, characterPose != null) { Belt = editablePose.Belt, NeckSplice = editablePose.NeckSplice };
                    return result with { ArmorPose = pose, Materials = models.Select(m => m.AutoMaterials).ToArray(), Warnings = warnings };
                } };
            return result.WithDye != null && dyes != null ? result.WithDye(dyes.Default) : result;
        }

        private static void LoadSharedArmorTextures(BotwActorResources resources, Dictionary<string, VisualModelTexture> textures, CancellationToken cancellation)
        {
            var sources = new PlayerEquipmentSources(resources);
            foreach (var name in new[] { "GameROMPlayer", "Armor_Default_Head", "Armor_Default_Upper", "Armor_Default_Lower" })
            {
                cancellation.ThrowIfCancellationRequested();
                if (sources.Find(name) is not { } source) continue;
                var shared = source.Open(resources.Roots);
                foreach (var model in shared.Models.Select(m => m.Resource).Distinct())
                {
                    var suffix = resources.IsSwitch ? ".Tex" : ".Tex1";
                    if (shared.ReadModel(model, suffix) is { } bytes)
                        new ToolboxBfresDocument(bytes).ReadTextures(textures, resources.Warnings);
                }
            }
        }

        internal static IEnumerable<string> CompatibleFallbackRoots(string path, IEnumerable<string> roots)
        {
            if (path.EndsWith(".pack.zs", StringComparison.OrdinalIgnoreCase)) return roots;
            bool isSwitch = BotwSarcArchive.OpenIndexed(path).LittleEndian;
            return roots.Where(root =>
            {
                var folder = Path.Combine(root, "Actor", "Pack");
                var sample = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.sbactorpack").FirstOrDefault() : null;
                sample ??= File.Exists(Path.Combine(root, "Pack", "TitleBG.pack")) ? Path.Combine(root, "Pack", "TitleBG.pack") : null;
                return sample == null || BotwSarcArchive.OpenIndexed(sample).LittleEndian == isSwitch;
            }).ToArray();
        }

        internal static IReadOnlyList<IconCapturePreset> ReadCapturePresets(BotwActorResources resources)
        {
            var presets = new List<IconCapturePreset>();
            var capture = resources.EquipmentParameters("CaptureRef");
            if (!capture.TryGetValue("CaptureInfo", out var entries)) return presets;
            foreach (var entry in entries.GetArray())
            {
                var data = new Dictionary<string, Byml>(entry.GetMap());
                var actor = Map(data, "ActorInfo");
                var camera = Map(data, "CameraInfo");
                var setting = Map(data, "SettingInfo");
                var target = setting.TryGetValue("Target", out var targetRef) ? resources.IconParameters(targetRef.GetString()) : new();
                var lightPath = Text(data, "LightInfo");
                var light = lightPath.Length > 0 ? resources.IconParameters(lightPath) : new();
                var size = Map(target, "Size");
                string label = Text(setting, "Prefix") + resources.ActorName + Text(setting, "Suffix");
                presets.Add(new(label, Vector(actor, "Rot") ?? Vector3.Zero, Vector(camera, "CameraPos") ?? new(0, 0, 10),
                    Vector(light, "Dir") ?? new(.5f, -.5f, -.5f), Math.Clamp((int)Number(size, "X", 576), 32, 4096), lightPath,
                    actor.TryGetValue("IsUseSkeletalAnim", out var animated) && animated.GetBool() ||
                    actor.TryGetValue("IsActivateCloth", out var cloth) && cloth.GetBool())
                { FieldOfView = Math.Clamp(Number(camera, "CameraFovy", 5), .1f, 120),
                    ModelKey = string.Join("|", resources.Models.Select(m => m.Resource + "/" + m.Model + "/" + m.BindBone).Distinct().Order(StringComparer.Ordinal)) });
            }
            return presets;
        }

        private static ToolboxActorAnimation? LoadArmorPose(BotwActorResources resources, CancellationToken cancellation)
        {
            if (!resources.ActorName.StartsWith("Armor_", StringComparison.Ordinal) ||
                !new[] { "_Head", "_Upper", "_Lower" }.Any(s => resources.ActorName.EndsWith(s, StringComparison.Ordinal))) return null;
            try
            {
                BotwActorResources? player = null;
                foreach (var root in resources.Roots)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var loose = Path.Combine(root, resources.IsTotk ? "Pack/Actor/Player.pack.zs" : "Actor/Pack/GameROMPlayer.sbactorpack");
                    if (File.Exists(loose)) { player = new(loose, resources.Roots); break; }
                    var title = Path.Combine(root, "Pack", "TitleBG.pack");
                    if (!resources.IsTotk && File.Exists(title))
                    { player = new(title, resources.Roots, "Actor/Pack/GameROMPlayer.sbactorpack"); break; }
                }
                if (player != null)
                {
                    player.PrepareTotkStrings();
                    foreach (var resource in player.AnimationResources)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (player.ReadModel(resource) is not { } bytes) continue;
                        var document = new ToolboxBfresDocument(bytes);
                        var clip = document.SkeletalAnimations.FirstOrDefault(a => string.Equals((string)a.Name, "Nml_Wait", StringComparison.OrdinalIgnoreCase));
                        if (clip != null) return new ToolboxActorAnimation((object)clip, resource);
                    }
                }
                resources.Warnings.Add("Nml_Wait is unavailable in the model game's player resources; using rest pose.");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
            { resources.Warnings.Add("Armor capture pose: " + ex.Message); }
            return null;
        }

        internal static VisualModelData Rotate(VisualModelData source, Vector3 degrees, Vector3 position = default)
        {
            var r = degrees * (MathF.PI / 180);
            var matrix = Matrix4x4.CreateRotationX(r.X) * Matrix4x4.CreateRotationY(r.Y) * Matrix4x4.CreateRotationZ(r.Z) * Matrix4x4.CreateTranslation(position);
            var model = new VisualModelData { SourcePath = source.SourcePath, BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue) };
            foreach (var mesh in source.Meshes) ShrineSceneAssets.AddMesh(model, mesh, matrix, mesh.Name);
            return model;
        }

        internal static float DefaultCaptureZoom(VisualModelData model, Matrix4x4 camera, ISet<int>? hidden = null, VisualModelTexture? reference = null, bool head = false)
        {
            var projected = new List<Vector2>();
            for (int meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++)
            {
                if (hidden?.Contains(meshIndex) == true) continue;
                var vertices = model.Meshes[meshIndex].Vertices;
                for (int i = 0; i < vertices.Length; i += 8)
                {
                    var p = Vector4.Transform(new Vector4(vertices[i], vertices[i + 1], vertices[i + 2], 1), camera);
                    if (!float.IsFinite(p.W) || p.W <= .00001f) return 100;
                    projected.Add(new(p.X / p.W, p.Y / p.W));
                }
            }
            if (projected.Count == 0) return 100;
            float top = projected.Max(p => p.Y);
            // Head captures deliberately crop the shoulders/cape below the frame.
            float edge = head ? Math.Max(top, projected.Where(p => p.Y >= -top).Select(p => Math.Abs(p.X)).DefaultIfEmpty(top).Max()) : projected.Max(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
            float coverage = ReferenceCoverage(reference, head);
            return edge > .00001f && float.IsFinite(edge) ? Math.Clamp(coverage * 100 / edge, 25, 400) : 100;
        }

        internal static float ReferenceCoverage(VisualModelTexture? reference, bool head)
        {
            if (reference == null) return .86f;
            float extent = 0; int pixels = 0;
            for (int y = 0; y < reference.Height; y++)
            for (int x = 0; x < reference.Width; x++)
            {
                if (reference.Pixels[(y * reference.Width + x) * 4 + 3] < 32) continue;
                float px = (x + .5f) * 2 / reference.Width - 1, py = 1 - (y + .5f) * 2 / reference.Height;
                extent = Math.Max(extent, head ? py : Math.Max(Math.Abs(px), Math.Abs(py))); pixels++;
            }
            return pixels > 16 && extent > .1f ? Math.Clamp(extent, .25f, .98f) : .86f;
        }

        // Fit the full geometry, not just two AABB corners, with the camera's authored XY offset preserved.
        internal static Matrix4x4 View(Vector3 camera, Vector3 angles)
        {
            var r = angles * (MathF.PI / 180);
            var rotation = Matrix4x4.CreateFromYawPitchRoll(r.Y, r.X, r.Z);
            return Matrix4x4.CreateLookAt(camera, camera + Vector3.TransformNormal(-Vector3.UnitZ, rotation), Vector3.TransformNormal(Vector3.UnitY, rotation));
        }

        internal static (float Near, float Far) DepthRange(VisualModelData model, Matrix4x4 view)
        {
            float nearest = float.MaxValue, farthest = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3((i & 1) == 0 ? model.BoundsMinimum.X : model.BoundsMaximum.X,
                    (i & 2) == 0 ? model.BoundsMinimum.Y : model.BoundsMaximum.Y,
                    (i & 4) == 0 ? model.BoundsMinimum.Z : model.BoundsMaximum.Z);
                float depth = -Vector3.Transform(p, view).Z;
                nearest = Math.Min(nearest, depth); farthest = Math.Max(farthest, depth);
            }
            float margin = Math.Max(.01f, (farthest - nearest) * .1f);
            float near = Math.Max(.001f, nearest - margin);
            return (near, Math.Max(near + .01f, farthest + margin));
        }

        internal static float FitFieldOfView(VisualModelData model, Vector3 camera, Vector3 angles = default)
        {
            float tangent = .001f;
            var view = View(camera, angles);
            foreach (var mesh in model.Meshes)
            foreach (var index in mesh.Indices.Distinct())
            {
                int i = checked((int)index * 8);
                var point = Vector3.Transform(new Vector3(mesh.Vertices[i], mesh.Vertices[i + 1], mesh.Vertices[i + 2]), view);
                float depth = -point.Z;
                if (depth <= .01f) throw new InvalidDataException("Camera must be in front of the model to fit it.");
                tangent = Math.Max(tangent, Math.Max(Math.Abs(point.X), Math.Abs(point.Y)) / depth);
            }
            return Math.Clamp(2 * MathF.Atan(tangent / .9f) * 180 / MathF.PI, .1f, 120);
        }

        private static Dictionary<string, Byml> Map(Dictionary<string, Byml> map, string key) => map.TryGetValue(key, out var value) ? new(value.GetMap()) : new();
        private static string Text(Dictionary<string, Byml> map, string key) => map.TryGetValue(key, out var value) ? value.GetString() : "";
        private static float Number(Dictionary<string, Byml> map, string key, float fallback = 0)
        {
            float result = map.TryGetValue(key, out var value) ? value.Type == BymlNodeType.Int ? value.GetInt() : value.GetFloat() : fallback;
            return float.IsFinite(result) ? result : throw new InvalidDataException("Non-finite capture value: " + key);
        }
        private static Vector3? Vector(Dictionary<string, Byml> map, string key) => map.ContainsKey(key) ? new Vector3(Number(Map(map, key), "X"), Number(Map(map, key), "Y"), Number(Map(map, key), "Z")) : null;
    }
}

namespace HKCLTool
{
    internal sealed partial class BotwActorResources
    {
        internal Dictionary<string, Byml> IconParameters(string path) => TotkParameters(path);
    }
}
