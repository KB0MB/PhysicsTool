using System.Numerics;

namespace HKCLTool;

internal sealed class BotwActorMesh(int index, string name, int triangles)
{
    public int Index { get; } = index;
    public string Name { get; } = name;
    public int Triangles { get; } = triangles;
    public bool Visible { get; internal set; } = true;
    public bool Deleted { get; internal set; }
}

internal sealed class BotwActorScene : IDisposable
{
    private sealed record Layer(ParticlePreviewData Source, HkclPreviewSimulator Solver, int ParticleOffset, int BoneOffset, int ColliderOffset);
    private readonly List<ToolboxActorModel> _models = new();
    private readonly List<(int Model, string Bone)> _attachments = new();
    private readonly List<HkclService> _supports = new();
    // Actor resources are read-only; editor evaluations still rebuild after edits.
    private readonly Dictionary<HkclService, IReadOnlyList<SidecarDriverGroup>> _supportGroups = new();
    private readonly List<Layer> _layers = new();
    // Only solver outputs replace animation. Their parents may drive the whole actor.
    private readonly HashSet<string> _clothDriven = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "PhysicsTool-ActorViewer", Guid.NewGuid().ToString("N"));
    public BotwActorResources Resources { get; }
    public PlayerArmorCatalog? ArmorCatalog { get; private set; }
    public Dictionary<PlayerArmorSlot, string> EquippedArmor { get; } = new();
    public PlayerWeaponCatalog? WeaponCatalog { get; private set; }
    public Dictionary<PlayerWeaponKind, PlayerWeaponSelection> Weapons { get; } = new();
    internal Dictionary<int, PlayerEquipmentAttachment> EquipmentAttachments { get; } = new();
    private readonly List<BotwActorResources> _outfitResources = new();
    private readonly List<BotwActorModelReference> _modelReferences = new();
    public List<ToolboxActorAnimation> Animations { get; } = new();
    public ParticlePreviewData Overlay { get; } = new();
    public VisualModelData RestModel { get; private set; } = new();
    public VisualModelData CurrentModel { get; private set; } = new();
    private VisualModelData _posedModel = new();
    private readonly List<BotwActorMesh> _meshes = new();
    public IReadOnlyList<BotwActorMesh> Meshes => _meshes;
    public bool HasCloth => _layers.Any(l => l.Source.Particles.Count > 0);
    public int ClothCount => _layers.Count(l => l.Source.Particles.Count > 0);
    public int SupportCount => _supportGroups.Values.Sum(g => g.Count);
    public int RagdollCount => _layers.Count(l => l.Source.RagdollConstraints.Count > 0);
    public int TextureCount { get; private set; }
    public bool WindEnabled { get; set; }
    public float WindSpeed { get; set; } = HkclPreviewSimulator.DefaultWindSpeed;
    private ToolboxActorAnimation? _previousAnimation;
    private double _previousFrame;
    private bool _previousSimulating;

    private BotwActorScene(BotwActorResources resources) { Resources = resources; }
    public void ConfigureSimulation(Action<HkclPreviewSimulator> configure)
    {
        foreach (var layer in _layers) configure(layer.Solver);
    }
    public static BotwActorScene Load(string path, IEnumerable<string>? roots = null, IProgress<string>? progress = null, CancellationToken cancellation = default, string? actorEntry = null,
        IReadOnlyDictionary<PlayerArmorSlot, string>? armor = null, IReadOnlyDictionary<PlayerWeaponKind, PlayerWeaponSelection>? weapons = null)
    {
        var scene = new BotwActorScene(new BotwActorResources(path, roots, actorEntry));
        try
        {
            scene.PrepareOutfit(armor, cancellation);
            if (weapons != null && scene.WeaponCatalog != null)
                foreach (var (kind, choice) in weapons)
                {
                    var entry = scene.WeaponCatalog.Find(kind, choice.Name);
                    scene.Weapons.Add(kind, choice with { Name = entry.Name });
                }
            scene.Load(progress, cancellation); return scene;
        }
        catch { scene.Dispose(); throw; }
    }
    private void Load(IProgress<string>? progress, CancellationToken cancellation)
    {
        var visibility = _outfitResources.Select(r => ArmorCatalog == null ? new ActorEquipmentVisibility() : r.ReadEquipmentVisibility()).ToArray();
        for (int i = 0; i < _outfitResources.Count; i++)
            LoadModels(_outfitResources[i], visibility[i].HiddenBy(visibility.Where((_, n) => n != i)), progress, cancellation);
        LoadWeapons(progress, cancellation);
        for (var i = 0; i < _models.Count; i++)
        {
            var bone = _modelReferences[i].BindBone;
            var owner = string.IsNullOrEmpty(bone) ? -1 : _models.FindIndex(m => m != _models[i] && m.RestPose.Bones.Any(b => b.Name == bone));
            if (owner < 0 && !string.IsNullOrEmpty(bone)) Resources.Warnings.Add($"Model {_models[i].Name}: attachment bone '{bone}' was not found.");
            _attachments.Add((owner, bone));
        }
        RestModel = Pose(null, 0, null, false);
        _meshes.AddRange(RestModel.Meshes.Select((m, i) => new BotwActorMesh(i, m.Name, m.Indices.Length / 3)));
        foreach (var mesh in RestModel.Meshes.Where(m => m.Texture == null)) Resources.Warnings.Add($"No albedo texture found for {mesh.Name}.");
        var referenced = Resources.Animations.Select(a => a.Clip).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Animations.Sort((a, b) => { var order = referenced.Contains(b.Name).CompareTo(referenced.Contains(a.Name)); return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name); });
        Directory.CreateDirectory(_temporaryDirectory);
        foreach (var resources in _outfitResources) LoadPhysics(resources, progress, cancellation);
        foreach (var warning in _outfitResources.Skip(1).SelectMany(r => r.Warnings).Distinct())
            if (!Resources.Warnings.Contains(warning)) Resources.Warnings.Add(warning);
        var displayedNames = Overlay.Bones.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Index, StringComparer.OrdinalIgnoreCase);
        var nextBone = Next(Overlay.Bones.Select(b => b.Index));
        var additional = RestModel.Bones.Where(b => !displayedNames.ContainsKey(b.Name)).DistinctBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var bone in additional) displayedNames.TryAdd(bone.Name, nextBone++);
        var restByIndex = RestModel.Bones.ToDictionary(b => b.Index);
        foreach (var bone in additional)
            Overlay.Bones.Add(new() { Index = displayedNames[bone.Name],
                ParentIndex = restByIndex.TryGetValue(bone.ParentIndex, out var parent) ? displayedNames[parent.Name] : -1, Name = bone.Name,
                Position = bone.Position, AxisX = bone.AxisX, AxisY = bone.AxisY, AxisZ = bone.AxisZ, IsSelectable = false });
        CurrentModel = Update(null, 0, false, 0, true);
    }
    private void PrepareOutfit(IReadOnlyDictionary<PlayerArmorSlot, string>? armor, CancellationToken cancellation)
    {
        _outfitResources.Add(Resources);
        if (!PlayerArmorCatalog.Supports(Resources)) return;
        ArmorCatalog = new(Resources);
        WeaponCatalog = new(Resources);
        if (Resources.Models.Any(m => m.Model == "Link_RaulSkin"))
        {
            if (ArmorCatalog.RightHandPath is { } hand) _outfitResources.Add(new BotwActorResources(hand, Resources.Roots));
            else Resources.Warnings.Add("Missing PlayerRaulHand actor; Link's right arm could not be loaded.");
        }
        foreach (var slot in Enum.GetValues<PlayerArmorSlot>())
        {
            cancellation.ThrowIfCancellationRequested();
            var entry = armor != null && armor.TryGetValue(slot, out var name) ? ArmorCatalog.Find(slot, name) : ArmorCatalog.Default(slot);
            if (entry == null) { Resources.Warnings.Add($"Missing default armor actor {PlayerArmorCatalog.DefaultName(slot)}."); continue; }
            _outfitResources.Add(new BotwActorResources(entry.Path, Resources.Roots));
            EquippedArmor.Add(slot, entry.Name);
        }
    }
    private void LoadWeapons(IProgress<string>? progress, CancellationToken cancellation)
    {
        if (WeaponCatalog == null) return;
        void Add(string actor, string bone, Matrix4x4 offset, BotwActorResources? resources = null)
        {
            cancellation.ThrowIfCancellationRequested();
            var path = WeaponCatalog.ActorPath(actor);
            if (path == null) { Resources.Warnings.Add($"Missing equipment actor {actor}."); return; }
            resources ??= new BotwActorResources(path, Resources.Roots);
            var first = _models.Count;
            LoadModels(resources, new HashSet<string>(), progress, cancellation);
            for (int i = first; i < _models.Count; i++)
            {
                _modelReferences[i] = _modelReferences[i] with { BindBone = bone };
                EquipmentAttachments.Add(i, new(actor, bone, offset));
            }
            foreach (var warning in resources.Warnings.Where(w => !w.StartsWith("TotK preview")))
                if (!Resources.Warnings.Contains(warning)) Resources.Warnings.Add(warning);
        }
        foreach (var (kind, choice) in Weapons.OrderBy(p => p.Key))
        {
            var entry = WeaponCatalog.Find(kind, choice.Name);
            var resources = new BotwActorResources(entry.Path, Resources.Roots);
            var parameters = PlayerWeaponParameters.Read(resources.EquipmentParameters(kind switch
                { PlayerWeaponKind.Bow => "BowRef", PlayerWeaponKind.Shield => "ShieldRef", _ => "WeaponRef" }));
            Add(entry.Name, PlayerWeaponParameters.Bone(kind, choice.Equipped), parameters.Offset(choice.Equipped), resources);
            if (!string.IsNullOrEmpty(parameters.Sheath))
                Add(parameters.Sheath, "Pod_A", parameters.Offset(false));
            if (kind != PlayerWeaponKind.Bow) continue;
            if (!string.IsNullOrEmpty(parameters.Quiver))
            {
                var path = WeaponCatalog.ActorPath(parameters.Quiver);
                if (path == null) Resources.Warnings.Add($"Missing equipment actor {parameters.Quiver}.");
                else
                {
                    var quiver = new BotwActorResources(path, Resources.Roots);
                    var p = quiver.EquipmentParameters("SheathRef");
                    Add(parameters.Quiver, p.TryGetValue("BindBoneName", out var bone) ? bone.GetString() : "Pod_B",
                        PlayerWeaponParameters.Read(p).Offset(false), quiver);
                }
            }
            if (choice.Equipped) Add("NormalArrow", "Weapon_R", Matrix4x4.Identity);
        }
    }
    private void LoadModels(BotwActorResources resources, IReadOnlySet<string> hiddenMaterials, IProgress<string>? progress, CancellationToken cancellation)
    {
        var documents = new Dictionary<string, ToolboxBfresDocument>();
        resources.PrepareTotkStrings();
        var textures = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in resources.Models.Select(m => m.Resource).Distinct())
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report($"Loading {name}");
            var bytes = resources.ReadModel(name) ?? throw new FileNotFoundException(resources.IsTotk
                ? $"Missing Model/{name}.bfres.mc. Select the matching TOTK romfs in the Actor browser."
                : $"Missing Model/{name}.sbfres. Add the matching game resource folder.");
            var document = new ToolboxBfresDocument(bytes); documents[name] = document;
            document.ReadTextures(textures, resources.Warnings);
            if (resources.IsTotk) TexToGoTexture.Load(document, resources, textures, cancellation);
            foreach (var suffix in document.IsSwitch ? new[] { ".Tex" } : new[] { ".Tex1" })
                if (resources.ReadModel(name, suffix) is { } textureData) new ToolboxBfresDocument(textureData).ReadTextures(textures, resources.Warnings);
            if (ReferenceEquals(resources, Resources)) AddAnimations(document, name);
        }
        TextureCount += textures.Count;
        var hasAppearance = false;
        foreach (var reference in resources.Models)
        {
            cancellation.ThrowIfCancellationRequested();
            var document = documents[reference.Resource];
            var appearance = ToolboxActorAppearance.Read(document, resources.VariationMatAnim, resources.VariationMatAnimFrame, resources.Warnings);
            hasAppearance |= appearance.Count > 0;
            _models.Add(new ToolboxActorModel(document, reference.Model, textures, appearance, resources.Warnings, hiddenMaterials));
            _modelReferences.Add(reference);
        }
        if (!hasAppearance && !string.IsNullOrWhiteSpace(resources.VariationMatAnim) && resources.VariationMatAnim != "Dummy")
            resources.Warnings.Add($"Texture variation {resources.VariationMatAnim} could not be resolved; default material textures are shown.");
        if (!ReferenceEquals(resources, Resources)) return;
        foreach (var name in Resources.AnimationResources.Except(documents.Keys))
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report($"Loading animations: {name}");
            var bytes = Resources.ReadModel(name);
            if (bytes == null) { Resources.Warnings.Add($"Missing animation resource Model/{name}.sbfres"); continue; }
            try { AddAnimations(new ToolboxBfresDocument(bytes), name); }
            catch (Exception ex) { Resources.Warnings.Add($"Animation resource {name}: {ex.GetType().Name}"); }
        }
    }
    private void LoadPhysics(BotwActorResources resources, IProgress<string>? progress, CancellationToken cancellation)
    {
        BphysicsDocument? profile = null;
        if (resources.PhysicsParameters != null)
        {
            var temp = Path.Combine(_temporaryDirectory, resources.ActorName + ".bphysics"); File.WriteAllBytes(temp, resources.PhysicsParameters);
            profile = BphysicsService.Load(temp);
        }
        var index = 0;
        foreach (var resource in resources.PhysicsFiles)
        {
            cancellation.ThrowIfCancellationRequested(); progress?.Report($"Loading {Path.GetFileName(resource)}");
            var bytes = resources.Read(resource);
            if (bytes == null) { Resources.Warnings.Add($"Missing {resource}"); continue; }
            var path = Path.Combine(_temporaryDirectory, $"{resources.ActorName}-{index++}{Path.GetExtension(resource)}");
            File.WriteAllBytes(path, bytes);
            try
            {
                var service = new HkclService(); service.Load(path);
                if (service.IsHelperBoneSidecar) { _supports.Add(service); _supportGroups[service] = service.GetSidecarDriverGroups(); continue; }
                var cloths = service.GetClothSummaries();
                for (var c = 0; c < cloths.Count; c++)
                {
                    ParticlePreviewData source;
                    try { source = service.IsBphcl ? service.GetTotkSimulationPreview(c) : service.GetParticlePreview(c); }
                    catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                    { Resources.Warnings.Add($"Cloth {service.GetClothName(c)}: {UserFacingExceptionFormatter.Format(ex)}"); continue; }
                    var clothProfile = profile?.Cloths.FirstOrDefault(p => cloths[c].Contains(p.Name, StringComparison.OrdinalIgnoreCase));
                    if (!service.IsBphcl && !service.IsHkrg)
                        AddParticlePoseReferences(source, service.GetParticleBoneAttachments(c));
                    AddLayer(source, clothProfile);
                }
            }
            catch (Exception ex) { Resources.Warnings.Add($"Physics {Path.GetFileName(resource)}: {UserFacingExceptionFormatter.Format(ex)}"); }
        }
    }
    private void AddAnimations(ToolboxBfresDocument document, string name)
    {
        foreach (var source in document.SkeletalAnimations)
        {
            var animation = new ToolboxActorAnimation(source, name);
            if (!Animations.Any(a => a.Name == animation.Name && a.ResourceName == name)) Animations.Add(animation);
        }
    }
    private void AddLayer(ParticlePreviewData source, BphysicsClothProfile? profile)
    {
        BindPreviewBoneNames(source, RestModel.Bones);
        var ownershipSolver = new HkclPreviewSimulator(source);
        var bones = source.Bones.ToDictionary(b => b.Index);
        var owners = ownershipSolver.DrivenBoneIndices.ToHashSet();
        foreach (var index in owners) _clothDriven.Add(bones[index].Name);
        var baseBone = source.Bones.FirstOrDefault(b => b.Name == profile?.BaseBone);
        var attachments = source.ParticleAttachments.Where(a => a.BoneWeights.Count > 0).ToDictionary(a => a.ParticleIndex, a => a);
        var points = source.Particles.ToDictionary(p => p.Index);
        foreach (var p in source.Particles.Where(p => source.SolverProfile != ClothSolverProfile.TotkExperimental && !attachments.ContainsKey(p.Index)))
        {
            var attachment = new ParticlePreviewAttachment { ParticleIndex = p.Index };
            if (baseBone != null) attachment.BoneWeights.Add(new() { BoneIndex = baseBone.Index, Weight = 1 });
            else if (attachments.Count > 0)
            {
                var nearest = attachments.Values.Where(a => points.ContainsKey(a.ParticleIndex)).OrderBy(a => Vector3.DistanceSquared(points[a.ParticleIndex].Position, p.Position)).FirstOrDefault();
                if (nearest != null) attachment.BoneWeights.AddRange(nearest.BoneWeights.Select(w => new ParticlePreviewBoneWeight { BoneIndex = w.BoneIndex, Weight = w.Weight }));
            }
            if (attachment.BoneWeights.Count > 0) source.ParticleAttachments.Add(attachment);
        }
        var layer = new Layer(source, new HkclPreviewSimulator(source), Next(Overlay.Particles.Select(p => p.Index)), Next(Overlay.Bones.Select(b => b.Index)), Next(Overlay.Colliders.Select(c => c.Index)));
        _layers.Add(layer);
        foreach (var p in source.Particles) Overlay.Particles.Add(new() { Index = p.Index + layer.ParticleOffset, Position = p.Position, Fixed = p.Fixed, Radius = p.Radius });
        foreach (var b in source.Bones) Overlay.Bones.Add(new() { Index = b.Index + layer.BoneOffset, ParentIndex = b.ParentIndex < 0 ? -1 : b.ParentIndex + layer.BoneOffset,
            Name = b.Name, Position = b.Position, AxisX = b.AxisX, AxisY = b.AxisY, AxisZ = b.AxisZ, IsSelectable = false });
        foreach (var l in source.Links) Overlay.Links.Add(new() { ParticleA = l.ParticleA + layer.ParticleOffset, ParticleB = l.ParticleB + layer.ParticleOffset, Kind = l.Kind });
        foreach (var t in source.Triangles) Overlay.Triangles.Add(new() { ParticleA = t.ParticleA + layer.ParticleOffset, ParticleB = t.ParticleB + layer.ParticleOffset, ParticleC = t.ParticleC + layer.ParticleOffset });
        foreach (var c in source.Colliders) Overlay.Colliders.Add(CloneCollider(c, layer));
        foreach (var c in source.RagdollConstraints) Overlay.RagdollConstraints.Add(new() { Index = Overlay.RagdollConstraints.Count,
            BodyA = c.BodyA + layer.ColliderOffset, BodyB = c.BodyB + layer.ColliderOffset, BoneA = c.BoneA < 0 ? -1 : c.BoneA + layer.BoneOffset,
            BoneB = c.BoneB < 0 ? -1 : c.BoneB + layer.BoneOffset, Name = c.Name, Kind = c.Kind });
    }
    private static int Next(IEnumerable<int> values) => values.DefaultIfEmpty(-1).Max() + 1;
    internal static void BindPreviewBoneNames(ParticlePreviewData source, IEnumerable<VisualModelBone> modelBones)
    {
        var names = modelBones.Select(b => b.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var exact = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var aliases = names.GroupBy(n => n[(n.LastIndexOf(':') + 1)..], StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);
        // Resolve only unambiguous aliases; indices and authored bind frames stay intact.
        foreach (var bone in source.Bones)
            if (!exact.Contains(bone.Name) && aliases.TryGetValue(bone.Name[(bone.Name.LastIndexOf(':') + 1)..], out var name))
                bone.Name = name;
    }
    internal static void AddParticlePoseReferences(ParticlePreviewData source, IEnumerable<ParticlePreviewAttachment> authored)
    {
        var attached = source.ParticleAttachments.Select(a => a.ParticleIndex).ToHashSet();
        var particles = source.Particles.ToDictionary(p => p.Index);
        foreach (var attachment in authored)
        {
            if (attached.Contains(attachment.ParticleIndex) || !particles.TryGetValue(attachment.ParticleIndex, out var particle)) continue;
            var weights = attachment.BoneWeights.Where(w => float.IsFinite(w.Weight) && w.Weight > 0).ToArray();
            var total = weights.Sum(w => w.Weight);
            if (total <= .000001f) continue;
            var reference = weights.Aggregate(Vector3.Zero, (sum, w) => sum + (w.ReferencePosition ?? particle.Position) * (w.Weight / total));
            // Free particles retain edited rest offsets; these weights pose/reset them,
            // but do not turn them into fixed anchors during a simulation step.
            var offset = particle.Fixed ? Vector3.Zero : particle.Position - reference;
            var pose = new ParticlePreviewAttachment { ParticleIndex = particle.Index };
            pose.BoneWeights.AddRange(weights.Select(w => new ParticlePreviewBoneWeight {
                BoneIndex = w.BoneIndex, Weight = w.Weight,
                ReferencePosition = (w.ReferencePosition ?? particle.Position) + offset }));
            source.ParticleAttachments.Add(pose);
            attached.Add(particle.Index);
        }
    }
    private VisualModelData Pose(ToolboxActorAnimation? animation, double frame, Dictionary<string, VisualBoneOverride>? overrides, bool suppressCloth) =>
        Assemble(animation, frame, overrides, suppressCloth, false);
    private VisualModelData Skeleton(ToolboxActorAnimation? animation, double frame, Dictionary<string, VisualBoneOverride>? overrides, bool suppressCloth) =>
        Assemble(animation, frame, overrides, suppressCloth, true);
    private VisualModelData Assemble(ToolboxActorAnimation? animation, double frame, Dictionary<string, VisualBoneOverride>? overrides, bool suppressCloth, bool skeletonOnly)
    {
        var poses = new VisualModelData?[_models.Count];
        var visiting = new bool[_models.Count];
        VisualModelData Visit(int index)
        {
            if (poses[index] is { } existing) return existing;
            if (visiting[index]) throw new InvalidDataException("Actor model attachments contain a cycle.");
            visiting[index] = true;
            var attachment = _attachments[index];
            Matrix4x4? parent = attachment.Model >= 0 ? Matrix(Visit(attachment.Model).Bones.First(b => b.Name == attachment.Bone)) : null;
            if (EquipmentAttachments.TryGetValue(index, out var equipment))
            {
                if (parent == null) throw new InvalidDataException($"Equipment {equipment.Actor}: attachment bone '{equipment.Bone}' was not found.");
                var transform = equipment.Offset * parent.Value;
                // Equipment follows the attachment, not same-named character animation
                // tracks or cloth overrides. Keep its skeleton out of Link's name map.
                var pose = skeletonOnly ? _models[index].SkeletonPose(null, 0, null, null, transform)
                    : _models[index].Pose(null, 0, null, null, transform);
                for (int b = 0; b < pose.Bones.Count; b++)
                {
                    var bone = pose.Bones[b];
                    pose.Bones[b] = new() { Index = bone.Index, ParentIndex = bone.ParentIndex, Name = $"Equipment_{index}_{bone.Name.Replace(':', '_').Replace('|', '_')}",
                        Position = bone.Position, Rotation = bone.Rotation, Scale = bone.Scale, Sources = bone.Sources };
                }
                return poses[index] = pose;
            }
            return poses[index] = skeletonOnly
                ? _models[index].SkeletonPose(animation, frame, overrides, suppressCloth ? _clothDriven : null, parent)
                : _models[index].Pose(animation, frame, overrides, suppressCloth ? _clothDriven : null, parent);
        }
        return Merge(Enumerable.Range(0, _models.Count).Select(Visit));
    }

    public VisualModelData Update(ToolboxActorAnimation? animation, double frame, bool simulate, float seconds, bool reset = false)
    {
        var looped = simulate && _previousSimulating && !reset && animation != null &&
            ReferenceEquals(animation, _previousAnimation) && frame < _previousFrame;
        var overrides = new Dictionary<string, VisualBoneOverride>(StringComparer.OrdinalIgnoreCase);
        var clean = Skeleton(animation, frame, null, simulate);
        foreach (var service in _supports)
        {
            var evaluation = service.EvaluateSidecarPose(clean.Bones, RestModel.Bones, _supportGroups[service]);
            // Helpers provide the input pose even for bones later written by
            // cloth. Cloth overrides replace these only after the solve.
            foreach (var pose in evaluation.Poses.Where(p => p.IsDriven))
                overrides[pose.Name] = new(pose.Name, pose.Position, pose.Rotation, pose.Scale);
            if (evaluation.Poses.Any(p => p.IsDriven)) clean = Skeleton(animation, frame, overrides, simulate);
        }
        // Both games use the visible animated pose when simulation is off. During
        // simulation, clean contains input animation/helpers but no cloth feedback.
        var restBones = RestModel.Bones.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var currentBones = clean.Bones.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var layer in _layers)
        {
            var deltas = new Dictionary<int, Matrix4x4>();
            foreach (var bone in layer.Source.Bones)
                if (restBones.TryGetValue(bone.Name, out var rest) && currentBones.TryGetValue(bone.Name, out var current) && Matrix4x4.Invert(Matrix(rest), out var inverse))
                    deltas[bone.Index] = inverse * Matrix(current);
            var sourceBones = layer.Source.Bones.ToDictionary(b => b.Index);
            Matrix4x4 ParentDelta(int index, HashSet<int> visited)
            {
                if (deltas.TryGetValue(index, out var value)) return value;
                if (!sourceBones.TryGetValue(index, out var bone) || !visited.Add(index)) return Matrix4x4.Identity;
                return deltas[index] = ParentDelta(bone.ParentIndex, visited);
            }
            foreach (var bone in layer.Source.Bones) ParentDelta(bone.Index, new());
            layer.Solver.SetInputBoneDeltas(deltas);
            layer.Solver.WindEnabled = WindEnabled;
            layer.Solver.WindSpeed = WindSpeed;
            if (!simulate || reset) layer.Solver.Reset();
            else if (looped) layer.Solver.RebaseInputPose();
            if (simulate && seconds > 0) layer.Solver.Step(seconds);
            var positions = layer.Solver.GetPositions();
            var bones = layer.Solver.GetBonePoses();
            var colliders = layer.Solver.GetColliderPoses();
            var particleEnd = layer.ParticleOffset + Next(layer.Source.Particles.Select(p => p.Index));
            var boneEnd = layer.BoneOffset + Next(layer.Source.Bones.Select(b => b.Index));
            foreach (var p in Overlay.Particles.Where(p => p.Index >= layer.ParticleOffset && p.Index < particleEnd))
                p.Position = positions[p.Index - layer.ParticleOffset];
            foreach (var b in Overlay.Bones.Where(b => b.Index >= layer.BoneOffset && b.Index < boneEnd))
            {
                var pose = bones[b.Index - layer.BoneOffset]; b.Position = pose.Position; b.AxisX = pose.AxisX; b.AxisY = pose.AxisY; b.AxisZ = pose.AxisZ;
            }
            foreach (var c in colliders.Values)
            {
                var target = Overlay.Colliders.First(x => x.Index == c.Index + layer.ColliderOffset);
                target.Start = c.Start; target.End = c.End; target.PlaneNormal = c.PlaneNormal;
                target.TransformOrigin = c.TransformOrigin;
            }
            if (simulate)
            foreach (var bone in layer.Source.Bones.Where(b => layer.Solver.DrivenBoneIndices.Contains(b.Index)))
            {
                if (!bones.TryGetValue(bone.Index, out var pose) || !restBones.TryGetValue(bone.Name, out var rest) || !Matrix4x4.Invert(Matrix(bone), out var inverse)) continue;
                var world = Matrix(rest) * inverse * Matrix(pose);
                if (Matrix4x4.Decompose(world, out var scale, out var rotation, out var position)) overrides[bone.Name] = new(bone.Name, position, rotation, scale);
            }
        }
        _posedModel = Pose(animation, frame, overrides, simulate);
        CurrentModel = FilterMeshes();
        var visibleBones = CurrentModel.Bones.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var b in Overlay.Bones)
            if (visibleBones.TryGetValue(b.Name, out var pose)) { b.Position = pose.Position; b.AxisX = pose.AxisX; b.AxisY = pose.AxisY; b.AxisZ = pose.AxisZ; }
        _previousAnimation = animation; _previousFrame = frame; _previousSimulating = simulate;
        return CurrentModel;
    }
    public void SetMeshVisible(int index, bool visible)
    {
        var mesh = _meshes[index];
        if (mesh.Deleted) return;
        mesh.Visible = visible;
        CurrentModel = FilterMeshes();
    }
    public void DeleteMeshes(IEnumerable<int> indices)
    {
        foreach (var index in indices.Distinct()) { _meshes[index].Deleted = true; _meshes[index].Visible = false; }
        CurrentModel = FilterMeshes();
    }
    private VisualModelData FilterMeshes()
    {
        if (_meshes.All(m => m.Visible && !m.Deleted)) return _posedModel;
        var result = new VisualModelData { SourcePath = _posedModel.SourcePath, AnimationSourcePath = _posedModel.AnimationSourcePath,
            AnimationFrame = _posedModel.AnimationFrame, BoundsMinimum = _posedModel.BoundsMinimum, BoundsMaximum = _posedModel.BoundsMaximum };
        result.Bones.AddRange(_posedModel.Bones);
        result.Meshes.AddRange(_posedModel.Meshes.Where((_, i) => _meshes[i].Visible && !_meshes[i].Deleted));
        result.TriangleCount = result.Meshes.Sum(m => m.Indices.Length / 3);
        if (result.Meshes.Count > 0)
        {
            var minimum = new Vector3(float.MaxValue); var maximum = new Vector3(float.MinValue);
            foreach (var mesh in result.Meshes)
            for (var i = 0; i < mesh.Vertices.Length; i += 8)
            {
                var position = new Vector3(mesh.Vertices[i], mesh.Vertices[i + 1], mesh.Vertices[i + 2]);
                minimum = Vector3.Min(minimum, position); maximum = Vector3.Max(maximum, position);
            }
            result.BoundsMinimum = minimum; result.BoundsMaximum = maximum;
        }
        return result;
    }
    private static ColliderPreviewShape CloneCollider(ColliderPreviewShape c, Layer layer) => new()
    { Index = c.Index + layer.ColliderOffset, BoneIndex = c.BoneIndex + layer.BoneOffset, Name = c.Name, Kind = c.Kind, Start = c.Start, End = c.End, Radius = c.Radius, EndRadius = c.EndRadius, PlaneNormal = c.PlaneNormal, TransformOrigin = c.TransformOrigin };
    internal static Matrix4x4 Matrix(VisualModelBone b) => Matrix4x4.CreateScale(b.Scale) * Matrix4x4.CreateFromQuaternion(b.Rotation) * Matrix4x4.CreateTranslation(b.Position);
    private static Matrix4x4 Matrix(BonePreviewPoint b) => Axes(b.Position, b.AxisX, b.AxisY, b.AxisZ);
    private static Matrix4x4 Matrix(SimulatedBonePreviewPose b) => Axes(b.Position, b.AxisX, b.AxisY, b.AxisZ);
    private static Matrix4x4 Axes(Vector3 p, Vector3 x, Vector3 y, Vector3 z) => new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, p.X, p.Y, p.Z, 1);
    private VisualModelData Merge(IEnumerable<VisualModelData> models)
    {
        var result = new VisualModelData { SourcePath = Resources.SourcePath, BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue) };
        foreach (var model in models)
        {
            if (result.Meshes.Count == 0) { result.AnimationFrame = model.AnimationFrame; result.AnimationSourcePath = model.AnimationSourcePath; }
            var offset = result.Bones.Count;
            result.Meshes.AddRange(model.Meshes);
            result.Bones.AddRange(model.Bones.Select(b => new VisualModelBone { Index = b.Index + offset, ParentIndex = b.ParentIndex < 0 ? -1 : b.ParentIndex + offset, Name = b.Name, Position = b.Position, Rotation = b.Rotation, Scale = b.Scale }));
            result.BoundsMinimum = Vector3.Min(result.BoundsMinimum, model.BoundsMinimum); result.BoundsMaximum = Vector3.Max(result.BoundsMaximum, model.BoundsMaximum); result.TriangleCount += model.TriangleCount;
        }
        return result;
    }
    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PhysicsTool-ActorViewer")) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(_temporaryDirectory);
        if (path.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(path))
        { try { Directory.Delete(path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
