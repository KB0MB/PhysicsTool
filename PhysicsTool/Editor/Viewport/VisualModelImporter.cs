using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace HKCLTool;

public static class VisualModelImporter
{
    private const string NativeBridge = "PhysicsToolFbxBridge";
    private const int VertexStride = 8;
    private static readonly VisualModelTexture DefaultTexture = CreateDefaultTexture();
    private static readonly Lazy<VisualModelTexture?> PackagedEyeGlint = new(LoadPackagedEyeGlint);

    public static VisualModelTexture LoadTexture(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("The texture file could not be found.", path);

        var fullPath = Path.GetFullPath(path);
        return DecodeBitmap(File.ReadAllBytes(fullPath), Path.GetFileName(fullPath), fullPath);
    }

    public static VisualModelTexture? GetPackagedEyeGlintTexture() => PackagedEyeGlint.Value;
    public static VisualModelTexture GetDefaultTexture() => DefaultTexture;

    internal static VisualModelTexture? ResolveAlbedoTexture(VisualModelMesh mesh) =>
        mesh.Texture ?? (mesh.UseMissingTextureFallback ? DefaultTexture : null);

    public static PoseSession OpenPose(string modelPath)
    {
        if (IsBfres(modelPath)) return new PoseSession(new BfresModelPose(modelPath));
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            throw new FileNotFoundException("The model file could not be found.", modelPath);
        if (!Path.GetExtension(modelPath).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Visual model overlays accept FBX, BFRES, and SBFRES files.");

        var error = new StringBuilder(1024);
        IntPtr handle;
        try
        {
            handle = NativeMethods.PoseOpen(modelPath, error, (nuint)error.Capacity);
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "PhysicsTool's native Autodesk FBX importer is missing. Reinstall or rebuild PhysicsTool so PhysicsToolFbxBridge.dll is beside PhysicsTool.exe.", ex);
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidOperationException(
                "PhysicsTool's native Autodesk FBX importer does not match this application's architecture.", ex);
        }

        if (handle == IntPtr.Zero)
            throw new InvalidDataException(error.Length > 0 ? error.ToString() : "The FBX pose scene could not be loaded.");
        return new PoseSession(new AnimationSession(handle, Path.GetFullPath(modelPath), string.Empty));
    }

    public sealed class PoseSession : IDisposable
    {
        private readonly AnimationSession? _session;
        private readonly BfresModelPose? _bfres;
        private bool _disposed;

        internal PoseSession(AnimationSession session) => _session = session;
        internal PoseSession(BfresModelPose session) => _bfres = session;

        public IReadOnlyList<VisualModelBone> Bones
        {
            get { ObjectDisposedException.ThrowIf(_disposed, this); return _bfres?.Bones ?? _session!.SampleBones(0.0); }
        }

        public VisualModelData Sample(
            VisualModelData? appearanceSource = null,
            IReadOnlyList<VisualBoneOverride>? boneOverrides = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _bfres?.Sample(appearanceSource, boneOverrides) ?? _session!.Sample(0.0, appearanceSource, boneOverrides);
        }

        public void Dispose() { _disposed = true; _session?.Dispose(); }
    }

    public static bool IsBfres(string path) => Path.GetExtension(path).Equals(".bfres", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".sbfres", StringComparison.OrdinalIgnoreCase);

    public static VisualModelData LoadModel(string path)
    {
        if (!IsBfres(path)) return LoadFbx(path);
        using var pose = OpenPose(path);
        return pose.Sample();
    }

    public static AnimationSession OpenAnimation(string modelPath, string animationPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            throw new FileNotFoundException("The FBX file could not be found.", modelPath);
        if (!Path.GetExtension(modelPath).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Visual model overlays currently accept FBX files.");
        if (string.IsNullOrWhiteSpace(animationPath) || !File.Exists(animationPath))
            throw new FileNotFoundException("The Maya .anim file could not be found.", animationPath);
        if (!Path.GetExtension(animationPath).Equals(".anim", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Visual animation playback accepts Maya ASCII .anim files.");

        var error = new StringBuilder(1024);
        IntPtr handle;
        try
        {
            handle = NativeMethods.AnimationOpen(
                modelPath,
                animationPath,
                error,
                (nuint)error.Capacity);
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "PhysicsTool's native Autodesk FBX importer is missing. Reinstall or rebuild PhysicsTool so PhysicsToolFbxBridge.dll is beside PhysicsTool.exe.", ex);
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidOperationException(
                "PhysicsTool's native Autodesk FBX importer does not match this application's architecture.", ex);
        }

        if (handle == IntPtr.Zero)
            throw new InvalidDataException(error.Length > 0 ? error.ToString() : "The Maya animation could not be loaded.");
        return new AnimationSession(
            handle,
            Path.GetFullPath(modelPath),
            Path.GetFullPath(animationPath));
    }

    public sealed class AnimationSession : IDisposable
    {
        private readonly object _gate = new();
        private Dictionary<string, int>? _boneIndices;
        private readonly HashSet<string> _animatedBoneNames;
        private IntPtr _handle;

        internal AnimationSession(IntPtr handle, string modelPath, string animationPath)
        {
            _handle = handle;
            ModelPath = modelPath;
            AnimationPath = animationPath;
            StartFrame = NativeMethods.AnimationStartFrame(handle);
            EndFrame = NativeMethods.AnimationEndFrame(handle);
            FramesPerSecond = NativeMethods.AnimationFramesPerSecond(handle);
            _animatedBoneNames = Enumerable.Range(
                    0,
                    checked((int)NativeMethods.AnimationTrackBoneCount(handle)))
                .Select(index => Marshal.PtrToStringUTF8(
                    NativeMethods.AnimationTrackBoneName(handle, checked((nuint)index))))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => NormalizeBoneName(name!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public string ModelPath { get; }
        public string AnimationPath { get; }
        public double StartFrame { get; }
        public double EndFrame { get; }
        public double FramesPerSecond { get; }
        public IReadOnlySet<string> AnimatedBoneNames => _animatedBoneNames;

        public bool HasBoneTrack(string name) =>
            _animatedBoneNames.Contains(NormalizeBoneName(name));

        public IReadOnlyList<VisualModelBone> SampleBones(double frame)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
                var clampedFrame = Math.Clamp(frame, StartFrame, EndFrame);
                var error = new StringBuilder(1024);
                var nativeScene = NativeMethods.AnimationSampleBones(
                    _handle,
                    clampedFrame,
                    error,
                    (nuint)error.Capacity);
                if (nativeScene == IntPtr.Zero)
                    throw new InvalidDataException(
                        error.Length > 0 ? error.ToString() : "The animated skeleton could not be sampled.");
                try
                {
                    var result = new VisualModelData();
                    CopyBones(nativeScene, result);
                    _boneIndices ??= result.Bones
                        .GroupBy(bone => NormalizeBoneName(bone.Name), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
                    return result.Bones;
                }
                finally
                {
                    NativeMethods.Free(nativeScene);
                }
            }
        }

        public VisualModelData Sample(
            double frame,
            VisualModelData? appearanceSource = null,
            IReadOnlyList<VisualBoneOverride>? boneOverrides = null)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
                var clampedFrame = Math.Clamp(frame, StartFrame, EndFrame);
                var error = new StringBuilder(1024);
                IntPtr nativeScene;
                var driven = boneOverrides?
                    .GroupBy(pose => NormalizeBoneName(pose.Name), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .ToArray() ?? Array.Empty<VisualBoneOverride>();
                if (driven.Length > 0)
                {
                    _boneIndices ??= SampleBones(clampedFrame)
                        .ToDictionary(
                            bone => NormalizeBoneName(bone.Name),
                            bone => bone.Index,
                            StringComparer.OrdinalIgnoreCase);
                    var indices = new List<int>(driven.Length);
                    var transforms = new List<float>(driven.Length * 10);
                    foreach (var pose in driven)
                    {
                        if (!_boneIndices.TryGetValue(NormalizeBoneName(pose.Name), out var index))
                            continue;
                        var scale = IsFinite(pose.Scale) ? pose.Scale : Vector3.One;
                        indices.Add(index);
                        transforms.AddRange(new[]
                        {
                            pose.Position.X, pose.Position.Y, pose.Position.Z,
                            pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W,
                            scale.X, scale.Y, scale.Z
                        });
                    }
                    nativeScene = indices.Count == 0
                        ? appearanceSource == null
                            ? NativeMethods.AnimationSample(_handle, clampedFrame, error, (nuint)error.Capacity)
                            : NativeMethods.AnimationSamplePose(_handle, clampedFrame, error, (nuint)error.Capacity)
                        : appearanceSource == null
                            ? NativeMethods.AnimationSampleOverrides(
                            _handle,
                            clampedFrame,
                            indices.ToArray(),
                            transforms.ToArray(),
                            (nuint)indices.Count,
                            error,
                            (nuint)error.Capacity)
                            : NativeMethods.AnimationSamplePoseOverrides(
                                _handle,
                                clampedFrame,
                                indices.ToArray(),
                                transforms.ToArray(),
                                (nuint)indices.Count,
                                error,
                                (nuint)error.Capacity);
                }
                else
                {
                    nativeScene = appearanceSource == null
                        ? NativeMethods.AnimationSample(
                            _handle,
                            clampedFrame,
                            error,
                            (nuint)error.Capacity)
                        : NativeMethods.AnimationSamplePose(
                            _handle,
                            clampedFrame,
                            error,
                            (nuint)error.Capacity);
                }
                if (nativeScene == IntPtr.Zero)
                    throw new InvalidDataException(
                        error.Length > 0 ? error.ToString() : "The Maya animation frame could not be sampled.");
                try
                {
                    return CopyScene(ModelPath, AnimationPath, clampedFrame, nativeScene, appearanceSource);
                }
                finally
                {
                    NativeMethods.Free(nativeScene);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_handle == IntPtr.Zero)
                    return;
                NativeMethods.AnimationFree(_handle);
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~AnimationSession()
        {
            if (_handle != IntPtr.Zero)
                NativeMethods.AnimationFree(_handle);
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static string NormalizeBoneName(string name)
    {
        var value = name ?? string.Empty;
        var pipe = value.LastIndexOf('|');
        if (pipe >= 0)
            value = value[(pipe + 1)..];
        var colon = value.LastIndexOf(':');
        return colon >= 0 ? value[(colon + 1)..] : value;
    }
    public static VisualModelData LoadFbx(string path, string? animationPath = null, double animationFrame = 0.0)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("The FBX file could not be found.", path);
        if (!Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Visual model overlays currently accept FBX files.");
        if (!string.IsNullOrWhiteSpace(animationPath) &&
            (!File.Exists(animationPath) ||
             !Path.GetExtension(animationPath).Equals(".anim", StringComparison.OrdinalIgnoreCase)))
            throw new FileNotFoundException("The Maya .anim file could not be found.", animationPath);

        var error = new StringBuilder(1024);
        IntPtr nativeScene;
        try
        {
            nativeScene = string.IsNullOrWhiteSpace(animationPath)
                ? NativeMethods.Load(path, error, (nuint)error.Capacity)
                : NativeMethods.LoadAnimated(
                    path,
                    animationPath,
                    animationFrame,
                    error,
                    (nuint)error.Capacity);
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "PhysicsTool's native Autodesk FBX importer is missing. Reinstall or rebuild PhysicsTool so PhysicsToolFbxBridge.dll is beside PhysicsTool.exe.", ex);
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidOperationException(
                "PhysicsTool's native Autodesk FBX importer does not match this application's architecture.", ex);
        }

        if (nativeScene == IntPtr.Zero)
            throw new InvalidDataException(error.Length > 0 ? error.ToString() : "The FBX could not be decoded.");

        try
        {
            return CopyScene(path, animationPath, animationFrame, nativeScene);
        }
        finally
        {
            NativeMethods.Free(nativeScene);
        }
    }

    private static VisualModelData CopyScene(
        string path,
        string? animationPath,
        double animationFrame,
        IntPtr nativeScene,
        VisualModelData? appearanceSource = null)
    {
        var result = new VisualModelData
        {
            SourcePath = Path.GetFullPath(path),
            AnimationSourcePath = string.IsNullOrWhiteSpace(animationPath)
                ? string.Empty
                : Path.GetFullPath(animationPath),
            AnimationFrame = animationFrame
        };
        var materialLayout = appearanceSource == null
            ? VisualMaterialPresetStore.GetMaterialLayouts(path, result.Warnings)
            : new Dictionary<string, VisualMaterialLayoutEntry>(StringComparer.OrdinalIgnoreCase);
        var textureCache = new Dictionary<string, VisualModelTexture?>(StringComparer.OrdinalIgnoreCase);
        var minimum = new Vector3(float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity);
        var meshCount = ToManagedCount(NativeMethods.GetMeshCount(nativeScene), "mesh count");

        for (var meshIndex = 0; meshIndex < meshCount; meshIndex++)
        {
            var nativeIndex = (nuint)meshIndex;
            var appearanceMesh = appearanceSource != null && meshIndex < appearanceSource.Meshes.Count
                ? appearanceSource.Meshes[meshIndex]
                : null;
            var name = appearanceMesh?.Name ?? ReadUtf8(NativeMethods.GetMeshName(nativeScene, nativeIndex));
            var texturePath = appearanceMesh == null
                ? ReadUtf8(NativeMethods.GetMeshTexturePath(nativeScene, nativeIndex))
                : string.Empty;
            var specularTexturePath = appearanceMesh == null
                ? ReadUtf8(NativeMethods.GetMeshSpecularTexturePath(nativeScene, nativeIndex))
                : string.Empty;
            var normalTexturePath = appearanceMesh == null
                ? ReadUtf8(NativeMethods.GetMeshNormalTexturePath(nativeScene, nativeIndex))
                : string.Empty;
            var ambientTexturePath = appearanceMesh == null
                ? ReadUtf8(NativeMethods.GetMeshAmbientTexturePath(nativeScene, nativeIndex))
                : string.Empty;
            var albedoUvSet = appearanceMesh == null
                ? ReadUtf8(NativeMethods.GetMeshTextureUvSet(nativeScene, nativeIndex))
                : string.Empty;
            var spmUvSet = appearanceMesh == null
                ? ReadUtf8(NativeMethods.GetMeshSpecularUvSet(nativeScene, nativeIndex))
                : string.Empty;
            var normalUvSet = appearanceMesh == null
                ? ReadUtf8(NativeMethods.GetMeshNormalUvSet(nativeScene, nativeIndex))
                : string.Empty;
            var ambientUvSet = appearanceMesh == null
                ? ReadUtf8(NativeMethods.GetMeshAmbientUvSet(nativeScene, nativeIndex))
                : string.Empty;
            materialLayout.TryGetValue(name, out var layout);
            texturePath = LayoutValue(layout?.Albedo, texturePath);
            specularTexturePath = LayoutValue(layout?.Spm, specularTexturePath);
            normalTexturePath = LayoutValue(layout?.Normal, normalTexturePath);
            ambientTexturePath = LayoutValue(layout?.AmbientOcclusion, ambientTexturePath);
            var auxiliaryTexturePath = layout?.Auxiliary ?? string.Empty;
            albedoUvSet = LayoutValue(layout?.AlbedoUvSet, albedoUvSet);
            spmUvSet = LayoutValue(layout?.SpmUvSet, spmUvSet);
            normalUvSet = LayoutValue(layout?.NormalUvSet, normalUvSet);
            ambientUvSet = LayoutValue(layout?.AmbientOcclusionUvSet, ambientUvSet);
            var auxiliaryUvSet = layout?.AuxiliaryUvSet ?? string.Empty;

            var vertexPointer = NativeMethods.GetMeshVertices(nativeScene, nativeIndex, out var nativeVertexCount);
            var vertexCount = ToManagedCount(nativeVertexCount, $"vertex count for '{name}'");
            nuint nativeIndexCount = 0;
            var indexPointer = appearanceMesh == null
                ? NativeMethods.GetMeshIndices(nativeScene, nativeIndex, out nativeIndexCount)
                : IntPtr.Zero;
            var indexCount = appearanceMesh?.Indices.Length ??
                ToManagedCount(nativeIndexCount, $"index count for '{name}'");
            if (vertexPointer == IntPtr.Zero || vertexCount == 0 || indexCount == 0 ||
                (appearanceMesh == null && indexPointer == IntPtr.Zero))
                continue;

            var vertices = new float[checked(vertexCount * VertexStride)];
            Marshal.Copy(vertexPointer, vertices, 0, vertices.Length);
            uint[] indices;
            if (appearanceMesh != null)
            {
                indices = appearanceMesh.Indices;
            }
            else
            {
                var signedIndices = new int[indexCount];
                Marshal.Copy(indexPointer, signedIndices, 0, signedIndices.Length);
                indices = new uint[indexCount];
                Buffer.BlockCopy(signedIndices, 0, indices, 0, checked(indexCount * sizeof(uint)));
            }
            var uvSets = appearanceMesh?.UvSets ??
                CopyUvSets(nativeScene, nativeIndex, vertexCount, vertices, name);

            for (var vertex = 0; vertex < vertexCount; vertex++)
            {
                var offset = vertex * VertexStride;
                var position = new Vector3(vertices[offset], vertices[offset + 1], vertices[offset + 2]);
                minimum = Vector3.Min(minimum, position);
                maximum = Vector3.Max(maximum, position);
            }

            VisualModelTexture? texture;
            VisualModelTexture? specularTexture;
            VisualModelTexture? normalTexture;
            VisualModelTexture? ambientTexture;
            VisualModelTexture? auxiliaryTexture;
            if (appearanceMesh != null)
            {
                texture = appearanceMesh.Texture;
                specularTexture = appearanceMesh.SpecularTexture;
                normalTexture = appearanceMesh.NormalTexture;
                ambientTexture = appearanceMesh.AmbientOcclusionTexture;
                auxiliaryTexture = appearanceMesh.AuxiliaryTexture;
            }
            else
            {
                var embeddedPointer = NativeMethods.GetMeshTextureData(
                    nativeScene, nativeIndex, out var nativeEmbeddedSize);
                var embeddedSize = ToManagedCount(nativeEmbeddedSize, $"embedded texture size for '{name}'");
                byte[]? embedded = null;
                if (embeddedPointer != IntPtr.Zero && embeddedSize > 0)
                {
                    embedded = new byte[embeddedSize];
                    Marshal.Copy(embeddedPointer, embedded, 0, embedded.Length);
                }

                texture = TryLoadBaseColorTexture(
                    path, texturePath, embedded, textureCache, result, name);
                specularTexture = TryLoadBaseColorTexture(
                    path, specularTexturePath, null, textureCache, result, $"{name} SPM");
                normalTexture = TryLoadBaseColorTexture(
                    path, normalTexturePath, null, textureCache, result, $"{name} normal");
                ambientTexture = TryLoadBaseColorTexture(
                    path, ambientTexturePath, null, textureCache, result, $"{name} AO");
                auxiliaryTexture = TryLoadBaseColorTexture(
                    path, auxiliaryTexturePath, null, textureCache, result, $"{name} auxiliary");
                // A matched model/mesh preset is authoritative. If one of its
                // texture files is gone, leave that slot empty instead of
                // silently replacing it with the preview checker.
                if (texture == null && layout == null)
                {
                    texture = DefaultTexture;
                    result.DefaultTextureMeshCount++;
                }
            }

            var color = new float[4];
            NativeMethods.GetMeshBaseColor(nativeScene, nativeIndex, color);
            var cullingMode = NativeMethods.GetMeshCullingMode(nativeScene, nativeIndex) switch
            {
                1 => VisualModelCullingMode.CounterClockwise,
                2 => VisualModelCullingMode.Clockwise,
                _ => DetermineExteriorCulling(vertices, indices)
            };
            var baseColor = appearanceMesh?.BaseColor ?? new Vector4(
                ClampColor(color[0], 0.72f),
                ClampColor(color[1], 0.75f),
                ClampColor(color[2], 0.78f),
                ClampOpacity(color[3]));
            if (appearanceMesh != null)
                cullingMode = appearanceMesh.CullingMode;
            var mapsEnabledByDefault = layout != null || UsesGeneralPreviewMaterial(name);
            var shaderModel = ParseEnum(layout?.Shader, VisualModelShaderModel.Standard);
            var metalChannel = ParseEnum(layout?.MetalChannel, VisualTextureChannel.Green);
            var smoothnessChannel = ParseEnum(layout?.SmoothnessChannel, VisualTextureChannel.Red);
            result.Meshes.Add(new VisualModelMesh
            {
                Name = string.IsNullOrWhiteSpace(name) ? $"Mesh {result.Meshes.Count}" : name,
                Vertices = vertices,
                Indices = indices,
                BaseColor = baseColor,
                Texture = texture,
                SpecularTexture = specularTexture,
                NormalTexture = normalTexture,
                AmbientOcclusionTexture = ambientTexture,
                AuxiliaryTexture = auxiliaryTexture,
                EyeGlintTexture = appearanceMesh?.EyeGlintTexture ??
                    (shaderModel == VisualModelShaderModel.Eye ? PackagedEyeGlint.Value : null),
                UvSets = uvSets,
                Material = appearanceMesh?.Material.Clone() ?? new VisualModelMaterialSettings
                {
                    ShaderModel = shaderModel,
                    UseAlbedo = texture != null,
                    UseSpm = mapsEnabledByDefault && specularTexture != null,
                    UseNormal = mapsEnabledByDefault && normalTexture != null,
                    UseAmbientOcclusion = mapsEnabledByDefault && ambientTexture != null,
                    UseAuxiliary = mapsEnabledByDefault && auxiliaryTexture != null,
                    UseEyeGlint = shaderModel == VisualModelShaderModel.Eye && PackagedEyeGlint.Value != null,
                    AlbedoUvSet = ResolveUvSetName(albedoUvSet, uvSets),
                    SpmUvSet = ResolveUvSetName(spmUvSet, uvSets),
                    NormalUvSet = ResolveUvSetName(normalUvSet, uvSets),
                    AmbientOcclusionUvSet = ResolveUvSetName(ambientUvSet, uvSets),
                    AuxiliaryUvSet = ResolveUvSetName(auxiliaryUvSet, uvSets),
                    MetalChannel = metalChannel,
                    SmoothnessChannel = smoothnessChannel,
                    MetalScale = layout?.MetalScale ?? 1.0f,
                    SmoothnessScale = layout?.SmoothnessScale ?? 1.0f,
                    NormalStrength = layout?.NormalStrength ?? 1.0f,
                    AmbientOcclusionStrength = layout?.AmbientOcclusionStrength ?? 1.0f,
                    AuxiliaryStrength = layout?.AuxiliaryStrength ?? 1.0f,
                    EyeGlintStrength = layout?.EyeGlintStrength ?? 0.65f,
                    Tint = baseColor
                },
                CullingMode = cullingMode
            });
            result.TriangleCount += indexCount / 3;
        }

        if (result.Meshes.Count == 0)
            throw new InvalidDataException("The FBX contained no triangle meshes PhysicsTool could display.");

        CopyBones(nativeScene, result);
        result.BoundsMinimum = minimum;
        result.BoundsMaximum = maximum;
        return result;
    }

    private static void CopyBones(IntPtr nativeScene, VisualModelData result)
    {
        var count = ToManagedCount(NativeMethods.GetBoneCount(nativeScene), "bone count");
        for (var index = 0; index < count; index++)
        {
            var nativeIndex = (nuint)index;
            var position = new float[3];
            var rotation = new float[4];
            var scale = new float[3];
            NativeMethods.GetBoneTransform(nativeScene, nativeIndex, position, rotation, scale);
            var quaternion = new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
            if (quaternion.LengthSquared() < 0.000001f)
                quaternion = Quaternion.Identity;
            else
                quaternion = Quaternion.Normalize(quaternion);
            result.Bones.Add(new VisualModelBone
            {
                Index = index,
                ParentIndex = NativeMethods.GetBoneParent(nativeScene, nativeIndex),
                Name = ReadUtf8(NativeMethods.GetBoneName(nativeScene, nativeIndex)),
                Position = new Vector3(position[0], position[1], position[2]),
                Rotation = quaternion,
                Scale = new Vector3(scale[0], scale[1], scale[2])
            });
        }
    }
    private static List<VisualModelUvSet> CopyUvSets(
        IntPtr nativeScene,
        nuint meshIndex,
        int vertexCount,
        IReadOnlyList<float> vertices,
        string meshName)
    {
        var result = new List<VisualModelUvSet>();
        var count = ToManagedCount(NativeMethods.GetMeshUvSetCount(nativeScene, meshIndex), $"UV set count for '{meshName}'");
        for (var uvIndex = 0; uvIndex < count; uvIndex++)
        {
            var nativeUvIndex = (nuint)uvIndex;
            var name = ReadUtf8(NativeMethods.GetMeshUvSetName(nativeScene, meshIndex, nativeUvIndex));
            var pointer = NativeMethods.GetMeshUvSetCoordinates(
                nativeScene, meshIndex, nativeUvIndex, out var nativeCoordinateCount);
            var coordinateCount = ToManagedCount(nativeCoordinateCount, $"UV coordinate count for '{meshName}'");
            if (pointer == IntPtr.Zero || coordinateCount != vertexCount || string.IsNullOrWhiteSpace(name))
                continue;

            var coordinates = new float[checked(coordinateCount * 2)];
            Marshal.Copy(pointer, coordinates, 0, coordinates.Length);
            result.Add(new VisualModelUvSet { Name = name, Coordinates = coordinates });
        }

        if (result.Count == 0)
        {
            var fallback = new float[checked(vertexCount * 2)];
            for (var vertex = 0; vertex < vertexCount; vertex++)
            {
                fallback[vertex * 2] = vertices[vertex * VertexStride + 6];
                fallback[vertex * 2 + 1] = vertices[vertex * VertexStride + 7];
            }
            result.Add(new VisualModelUvSet { Name = "UVMap", Coordinates = fallback });
        }

        return result;
    }

    private static string ResolveUvSetName(string requested, IReadOnlyList<VisualModelUvSet> uvSets)
    {
        var match = uvSets.FirstOrDefault(uv =>
            uv.Name.Equals(requested, StringComparison.OrdinalIgnoreCase));
        return match?.Name ?? uvSets.FirstOrDefault()?.Name ?? string.Empty;
    }

    private static bool UsesGeneralPreviewMaterial(string meshName)
    {
        string[] specialized = ["Face", "Skin", "Eye", "Hair", "Teeth"];
        return !specialized.Any(name => meshName.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static VisualModelCullingMode DetermineExteriorCulling(
        IReadOnlyList<float> vertices,
        IReadOnlyList<uint> indices)
    {
        double orientation = 0.0;
        var samples = 0;
        for (var index = 0; index + 2 < indices.Count && samples < 4096; index += 3)
        {
            var i0 = checked((int)indices[index] * VertexStride);
            var i1 = checked((int)indices[index + 1] * VertexStride);
            var i2 = checked((int)indices[index + 2] * VertexStride);
            if (i0 + 5 >= vertices.Count || i1 + 5 >= vertices.Count || i2 + 5 >= vertices.Count)
                continue;

            var p0 = new Vector3(vertices[i0], vertices[i0 + 1], vertices[i0 + 2]);
            var p1 = new Vector3(vertices[i1], vertices[i1 + 1], vertices[i1 + 2]);
            var p2 = new Vector3(vertices[i2], vertices[i2 + 1], vertices[i2 + 2]);
            var geometricNormal = Vector3.Cross(p1 - p0, p2 - p0);
            var vertexNormal = new Vector3(
                vertices[i0 + 3] + vertices[i1 + 3] + vertices[i2 + 3],
                vertices[i0 + 4] + vertices[i1 + 4] + vertices[i2 + 4],
                vertices[i0 + 5] + vertices[i1 + 5] + vertices[i2 + 5]);
            if (geometricNormal.LengthSquared() < 1.0e-12f || vertexNormal.LengthSquared() < 1.0e-12f)
                continue;

            orientation += Vector3.Dot(Vector3.Normalize(geometricNormal), Vector3.Normalize(vertexNormal));
            samples++;
        }

        return orientation < 0.0
            ? VisualModelCullingMode.Clockwise
            : VisualModelCullingMode.CounterClockwise;
    }

    private static VisualModelTexture? TryLoadBaseColorTexture(
        string modelPath,
        string texturePath,
        byte[]? embedded,
        IDictionary<string, VisualModelTexture?> cache,
        VisualModelData result,
        string meshName)
    {
        var cacheKey = !string.IsNullOrWhiteSpace(texturePath)
            ? texturePath
            : embedded is { Length: > 0 } ? $"embedded:{meshName}" : string.Empty;
        if (cacheKey.Length == 0)
            return null;
        if (cache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            if (embedded is { Length: > 0 })
            {
                var loaded = DecodeBitmap(embedded, FirstNonEmpty(Path.GetFileName(texturePath), meshName));
                cache[cacheKey] = loaded;
                result.EmbeddedTextureCount++;
                return loaded;
            }

            var externalPath = ResolveExternalTexturePath(modelPath, texturePath);
            if (externalPath == null)
            {
                AddWarning(result, $"Texture not found: {texturePath}");
                cache[cacheKey] = null;
                return null;
            }

            var external = DecodeBitmap(
                File.ReadAllBytes(externalPath),
                Path.GetFileName(externalPath),
                externalPath);
            cache[cacheKey] = external;
            result.ExternalTextureCount++;
            return external;
        }
        catch (Exception ex)
        {
            AddWarning(result, $"Could not decode texture '{texturePath}': {ex.Message}");
            cache[cacheKey] = null;
            return null;
        }
    }

    private static VisualModelTexture DecodeBitmap(
        byte[] bytes,
        string name,
        string sourcePath = "")
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var bitmap = new Bitmap(stream);

        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var locked = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rgba = new byte[checked(bitmap.Width * bitmap.Height * 4)];
            var sourceRow = new byte[Math.Abs(locked.Stride)];
            var hasTransparentPixels = false;
            var hasPartiallyTransparentPixels = false;
            for (var y = 0; y < bitmap.Height; y++)
            {
                var rowPointer = IntPtr.Add(locked.Scan0, y * locked.Stride);
                Marshal.Copy(rowPointer, sourceRow, 0, sourceRow.Length);
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var sourceOffset = x * 4;
                    // Bitmap rows are top-down, while OpenGL texture V coordinates start at the bottom.
                    var targetOffset = ((bitmap.Height - 1 - y) * bitmap.Width + x) * 4;
                    rgba[targetOffset] = sourceRow[sourceOffset + 2];
                    rgba[targetOffset + 1] = sourceRow[sourceOffset + 1];
                    rgba[targetOffset + 2] = sourceRow[sourceOffset];
                    var alpha = sourceRow[sourceOffset + 3];
                    rgba[targetOffset + 3] = alpha;
                    hasTransparentPixels |= alpha < byte.MaxValue;
                    hasPartiallyTransparentPixels |= alpha is > 0 and < byte.MaxValue;
                }
            }

            return new VisualModelTexture
            {
                Name = name,
                SourcePath = sourcePath,
                Width = bitmap.Width,
                Height = bitmap.Height,
                Pixels = rgba,
                AlphaMode = !hasTransparentPixels
                    ? VisualModelAlphaMode.Opaque
                    : hasPartiallyTransparentPixels
                        ? VisualModelAlphaMode.Blend
                        : VisualModelAlphaMode.Cutout
            };
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }
    }

    private static string? ResolveExternalTexturePath(string modelPath, string texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath))
            return null;

        var modelDirectory = Path.GetDirectoryName(Path.GetFullPath(modelPath)) ?? Environment.CurrentDirectory;
        var normalized = texturePath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(normalized) && File.Exists(normalized))
            return normalized;

        try
        {
            var relative = Path.GetFullPath(Path.Combine(modelDirectory, normalized));
            if (relative.StartsWith(modelDirectory, StringComparison.OrdinalIgnoreCase) && File.Exists(relative))
                return relative;
        }
        catch
        {
            // Exporters often preserve a stale absolute path. The basename search below is intentional.
        }

        var fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var besideModel = Path.Combine(modelDirectory, fileName);
        if (File.Exists(besideModel))
            return besideModel;

        try
        {
            var beneathModel = Directory.EnumerateFiles(
                modelDirectory,
                fileName,
                SearchOption.AllDirectories).FirstOrDefault();
            if (beneathModel != null)
                return beneathModel;
        }
        catch
        {
            // Continue with the application directory fallback below.
        }

        var applicationDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var besideApplication = Path.Combine(applicationDirectory, fileName);
        if (File.Exists(besideApplication))
            return besideApplication;

        try
        {
            return Directory.EnumerateFiles(
                applicationDirectory,
                fileName,
                SearchOption.AllDirectories).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string LayoutValue(string? configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured;

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;

    private static int ToManagedCount(nuint value, string displayName)
    {
        var count = value.ToUInt64();
        if (count > int.MaxValue)
            throw new InvalidDataException($"The FBX {displayName} is too large for PhysicsTool.");
        return (int)count;
    }

    private static string ReadUtf8(IntPtr pointer) =>
        pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;

    private static float ClampColor(float value, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, 0.0f, 1.0f) : fallback;

    private static float ClampOpacity(float value) =>
        !float.IsFinite(value) || value <= 0.001f ? 1.0f : Math.Clamp(value, 0.0f, 1.0f);

    private static void AddWarning(VisualModelData result, string warning)
    {
        if (!result.Warnings.Contains(warning, StringComparer.Ordinal))
            result.Warnings.Add(warning);
    }

    private static VisualModelTexture? LoadPackagedEyeGlint()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Resources",
            "VisualModel",
            "EyeGlint.png");
        return File.Exists(path)
            ? DecodeBitmap(File.ReadAllBytes(path), Path.GetFileName(path), path)
            : null;
    }
    private static VisualModelTexture CreateDefaultTexture()
    {
        using var stream = typeof(VisualModelImporter).Assembly.GetManifestResourceStream(
            "PhysicsTool.Resources.VisualModel.ToolboxDefaultTexture.png")
            ?? throw new InvalidDataException("The bundled default model texture is missing.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return DecodeBitmap(bytes.ToArray(), "Toolbox default UV texture");
    }

    private static string FirstNonEmpty(string? first, string fallback) =>
        string.IsNullOrWhiteSpace(first) ? fallback : first;

    private static class NativeMethods
    {
        [DllImport(NativeBridge, EntryPoint = "pt_fbx_animation_open", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr AnimationOpen(
            [MarshalAs(UnmanagedType.LPWStr)] string modelPath,
            [MarshalAs(UnmanagedType.LPWStr)] string animationPath,
            StringBuilder errorBuffer,
            nuint errorCapacity);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_pose_open", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr PoseOpen(
            [MarshalAs(UnmanagedType.LPWStr)] string modelPath,
            StringBuilder errorBuffer,
            nuint errorCapacity);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_animation_sample", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AnimationSample(
            IntPtr animation,
            double frame,
            StringBuilder errorBuffer,
            nuint errorCapacity);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_animation_sample_bones", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AnimationSampleBones(
            IntPtr animation,
            double frame,
            StringBuilder errorBuffer,
            nuint errorCapacity);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_animation_sample_overrides", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AnimationSampleOverrides(
            IntPtr animation,
            double frame,
            [In] int[] boneIndices,
            [In] float[] boneTransforms,
            nuint overrideCount,
            StringBuilder errorBuffer,
            nuint errorCapacity);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_animation_sample_pose", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AnimationSamplePose(
            IntPtr animation,
            double frame,
            StringBuilder errorBuffer,
            nuint errorCapacity);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_animation_sample_pose_overrides", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AnimationSamplePoseOverrides(
            IntPtr animation,
            double frame,
            [In] int[] boneIndices,
            [In] float[] boneTransforms,
            nuint overrideCount,
            StringBuilder errorBuffer,
            nuint errorCapacity);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_animation_start_frame", CallingConvention = CallingConvention.Cdecl)]
        public static extern double AnimationStartFrame(IntPtr animation);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_animation_end_frame", CallingConvention = CallingConvention.Cdecl)]
        public static extern double AnimationEndFrame(IntPtr animation);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_animation_frames_per_second", CallingConvention = CallingConvention.Cdecl)]
        public static extern double AnimationFramesPerSecond(IntPtr animation);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_animation_track_bone_count", CallingConvention = CallingConvention.Cdecl)]
        public static extern nuint AnimationTrackBoneCount(IntPtr animation);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_animation_track_bone_name", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AnimationTrackBoneName(IntPtr animation, nuint index);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_animation_free", CallingConvention = CallingConvention.Cdecl)]
        public static extern void AnimationFree(IntPtr animation);
        [DllImport(NativeBridge, EntryPoint = "pt_fbx_load", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr Load(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            StringBuilder errorBuffer,
            nuint errorCapacity);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_load_animated", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadAnimated(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            [MarshalAs(UnmanagedType.LPWStr)] string animationPath,
            double frame,
            StringBuilder errorBuffer,
            nuint errorCapacity);
        [DllImport(NativeBridge, EntryPoint = "pt_fbx_free", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Free(IntPtr scene);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_count", CallingConvention = CallingConvention.Cdecl)]
        public static extern nuint GetMeshCount(IntPtr scene);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_name", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetMeshName(IntPtr scene, nuint index);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_texture_path", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetMeshTexturePath(IntPtr scene, nuint index);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_specular_texture_path", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetMeshSpecularTexturePath(IntPtr scene, nuint index);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_normal_texture_path", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetMeshNormalTexturePath(IntPtr scene, nuint index);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_ambient_texture_path", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetMeshAmbientTexturePath(IntPtr scene, nuint index);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_texture_uv_set", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetMeshTextureUvSet(IntPtr scene, nuint index);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_specular_uv_set", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetMeshSpecularUvSet(IntPtr scene, nuint index);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_normal_uv_set", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetMeshNormalUvSet(IntPtr scene, nuint index);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_ambient_uv_set", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetMeshAmbientUvSet(IntPtr scene, nuint index);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_uv_set_count", CallingConvention = CallingConvention.Cdecl)]
        public static extern nuint GetMeshUvSetCount(IntPtr scene, nuint index);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_uv_set_name", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetMeshUvSetName(IntPtr scene, nuint meshIndex, nuint uvIndex);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_uv_set_coordinates", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetMeshUvSetCoordinates(
            IntPtr scene, nuint meshIndex, nuint uvIndex, out nuint coordinateCount);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_texture_data", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetMeshTextureData(IntPtr scene, nuint index, out nuint size);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_vertices", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetMeshVertices(IntPtr scene, nuint index, out nuint vertexCount);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_indices", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetMeshIndices(IntPtr scene, nuint index, out nuint indexCount);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_culling_mode", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetMeshCullingMode(IntPtr scene, nuint index);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_bone_count", CallingConvention = CallingConvention.Cdecl)]
        public static extern nuint GetBoneCount(IntPtr scene);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_bone_name", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr GetBoneName(IntPtr scene, nuint index);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_bone_parent", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetBoneParent(IntPtr scene, nuint index);

        [DllImport(NativeBridge, EntryPoint = "pt_fbx_bone_transform", CallingConvention = CallingConvention.Cdecl)]
        public static extern void GetBoneTransform(
            IntPtr scene,
            nuint index,
            [Out] float[] position,
            [Out] float[] rotation,
            [Out] float[] scale);
        [DllImport(NativeBridge, EntryPoint = "pt_fbx_mesh_base_color", CallingConvention = CallingConvention.Cdecl)]
        public static extern void GetMeshBaseColor(IntPtr scene, nuint index, [Out] float[] rgba);
    }
}
