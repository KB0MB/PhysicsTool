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
    private readonly List<Layer> _layers = new();
    private readonly HashSet<string> _clothOwned = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "PhysicsTool-ActorViewer", Guid.NewGuid().ToString("N"));
    public BotwActorResources Resources { get; }
    public List<ToolboxActorAnimation> Animations { get; } = new();
    public ParticlePreviewData Overlay { get; } = new();
    public VisualModelData RestModel { get; private set; } = new();
    public VisualModelData CurrentModel { get; private set; } = new();
    private VisualModelData _posedModel = new();
    private readonly List<BotwActorMesh> _meshes = new();
    public IReadOnlyList<BotwActorMesh> Meshes => _meshes;
    public bool HasCloth => _layers.Any(l => l.Source.Particles.Count > 0);
    public int ClothCount => _layers.Count(l => l.Source.Particles.Count > 0);
    public int SupportCount => _supports.Sum(s => s.GetSidecarDriverGroups().Count);
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
    public static BotwActorScene Load(string path, IEnumerable<string>? roots = null, IProgress<string>? progress = null, CancellationToken cancellation = default, string? actorEntry = null)
    {
        var scene = new BotwActorScene(new BotwActorResources(path, roots, actorEntry));
        try { scene.Load(progress, cancellation); return scene; }
        catch { scene.Dispose(); throw; }
    }
    private void Load(IProgress<string>? progress, CancellationToken cancellation)
    {
        var documents = new Dictionary<string, ToolboxBfresDocument>();
        var textures = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Resources.Models.Select(m => m.Resource).Distinct())
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report($"Loading {name}");
            var bytes = Resources.ReadModel(name) ?? throw new FileNotFoundException($"Missing Model/{name}.sbfres. Add the matching game resource folder.");
            var document = new ToolboxBfresDocument(bytes); documents[name] = document;
            document.ReadTextures(textures, Resources.Warnings);
            foreach (var suffix in document.IsSwitch ? new[] { ".Tex" } : new[] { ".Tex1" })
                if (Resources.ReadModel(name, suffix) is { } textureData) new ToolboxBfresDocument(textureData).ReadTextures(textures, Resources.Warnings);
            AddAnimations(document, name);
        }
        TextureCount = textures.Count;
        foreach (var reference in Resources.Models)
        {
            cancellation.ThrowIfCancellationRequested();
            _models.Add(new ToolboxActorModel(documents[reference.Resource], reference.Model, textures));
        }
        for (var i = 0; i < _models.Count; i++)
        {
            var bone = Resources.Models[i].BindBone;
            var owner = string.IsNullOrEmpty(bone) ? -1 : _models.FindIndex(m => m != _models[i] && m.RestPose.Bones.Any(b => b.Name == bone));
            if (owner < 0 && !string.IsNullOrEmpty(bone)) Resources.Warnings.Add($"Model {_models[i].Name}: attachment bone '{bone}' was not found.");
            _attachments.Add((owner, bone));
        }
        RestModel = Pose(null, 0, null, false);
        _meshes.AddRange(RestModel.Meshes.Select((m, i) => new BotwActorMesh(i, m.Name, m.Indices.Length / 3)));
        foreach (var mesh in RestModel.Meshes.Where(m => m.Texture == null)) Resources.Warnings.Add($"No albedo texture found for {mesh.Name}.");
        foreach (var name in Resources.AnimationResources.Except(documents.Keys))
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report($"Loading animations: {name}");
            var bytes = Resources.ReadModel(name);
            if (bytes == null) { Resources.Warnings.Add($"Missing animation resource Model/{name}.sbfres"); continue; }
            try { AddAnimations(new ToolboxBfresDocument(bytes), name); }
            catch (Exception ex) { Resources.Warnings.Add($"Animation resource {name}: {ex.GetType().Name}"); }
        }
        // BAS states can reference the same clip many times. Keep each playable
        // clip once, with referenced clips first and names stable across reloads.
        var referenced = Resources.Animations.Select(a => a.Clip).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Animations.Sort((a, b) => { var order = referenced.Contains(b.Name).CompareTo(referenced.Contains(a.Name)); return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name); });
        Directory.CreateDirectory(_temporaryDirectory);
        BphysicsDocument? profile = null;
        if (Resources.PhysicsParameters != null)
        {
            var temp = Path.Combine(_temporaryDirectory, "actor.bphysics"); File.WriteAllBytes(temp, Resources.PhysicsParameters);
            profile = BphysicsService.Load(temp);
        }
        var index = 0;
        foreach (var resource in Resources.PhysicsFiles)
        {
            cancellation.ThrowIfCancellationRequested(); progress?.Report($"Loading {Path.GetFileName(resource)}");
            var bytes = Resources.Read(resource);
            if (bytes == null) { Resources.Warnings.Add($"Missing {resource}"); continue; }
            var path = Path.Combine(_temporaryDirectory, $"physics-{index++}{Path.GetExtension(resource)}");
            File.WriteAllBytes(path, bytes);
            try
            {
                var service = new HkclService(); service.Load(path);
                if (service.IsHelperBoneSidecar) { _supports.Add(service); continue; }
                var cloths = service.GetClothSummaries();
                for (var c = 0; c < cloths.Count; c++)
                {
                    var source = service.GetParticlePreview(c);
                    var clothProfile = profile?.Cloths.FirstOrDefault(p => cloths[c].Contains(p.Name, StringComparison.OrdinalIgnoreCase));
                    AddLayer(source, clothProfile);
                }
            }
            catch (Exception ex) { Resources.Warnings.Add($"Physics {Path.GetFileName(resource)}: {UserFacingExceptionFormatter.Format(ex)}"); }
        }
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
        var ownershipSolver = new HkclPreviewSimulator(source);
        var bones = source.Bones.ToDictionary(b => b.Index);
        var owners = ownershipSolver.DrivenBoneIndices.ToHashSet();
        var anchors = source.ParticleAttachments.SelectMany(a => a.BoneWeights).Select(w => w.BoneIndex)
            .Concat(source.Colliders.Select(c => c.BoneIndex)).Concat(source.LocalRanges.SelectMany(r => r.ReferenceBoneWeights).Select(w => w.BoneIndex)).ToHashSet();
        foreach (var index in owners.ToArray())
        {
            var parent = bones[index].ParentIndex;
            var seen = new HashSet<int>();
            while (parent >= 0 && !anchors.Contains(parent) && bones.TryGetValue(parent, out var bone) && seen.Add(parent))
            { owners.Add(parent); parent = bone.ParentIndex; }
        }
        foreach (var index in owners) _clothOwned.Add(bones[index].Name);
        var baseBone = source.Bones.FirstOrDefault(b => b.Name == profile?.BaseBone);
        var attachments = source.ParticleAttachments.Where(a => a.BoneWeights.Count > 0).ToDictionary(a => a.ParticleIndex, a => a);
        var points = source.Particles.ToDictionary(p => p.Index);
        foreach (var p in source.Particles.Where(p => !attachments.ContainsKey(p.Index)))
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
            return poses[index] = skeletonOnly
                ? _models[index].SkeletonPose(animation, frame, overrides, suppressCloth ? _clothOwned : null, parent)
                : _models[index].Pose(animation, frame, overrides, suppressCloth ? _clothOwned : null, parent);
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
        for (var g = 0; g < service.GetSidecarDriverGroups().Count; g++)
        {
            var evaluation = service.EvaluateSidecarDriverGroup(g, clean.Bones, RestModel.Bones);
            foreach (var pose in evaluation.Poses.Where(p => p.IsDriven && !(simulate && _clothOwned.Contains(p.Name))))
                overrides[pose.Name] = new(pose.Name, pose.Position, pose.Rotation, pose.Scale);
            if (evaluation.Poses.Any(p => p.IsDriven)) clean = Skeleton(animation, frame, overrides, simulate);
        }
        // Particle inputs never receive animated cloth tracks or last frame's
        // simulated output. Only the surrounding actor skeleton drives them.
        var input = simulate ? clean : Skeleton(animation, frame, overrides.Where(o => !_clothOwned.Contains(o.Key)).ToDictionary(o => o.Key, o => o.Value), true);
        var restBones = RestModel.Bones.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var currentBones = input.Bones.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
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
