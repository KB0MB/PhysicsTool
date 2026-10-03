using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.WinForms;
using SysMat4 = System.Numerics.Matrix4x4;
using SysQuat = System.Numerics.Quaternion;
using SysVec3 = System.Numerics.Vector3;

namespace HKCLTool;

public enum PreviewPickKind
{
    Particle,
    Bone,
    Collider
}

public enum ParticleSelectionOperation
{
    Replace,
    Add,
    Toggle,
    Remove
}

public sealed class PreviewPickEventArgs : EventArgs
{
    public PreviewPickEventArgs(PreviewPickKind kind, int index, ParticleSelectionOperation selectionOperation)
    {
        Kind = kind;
        Index = index;
        SelectionOperation = selectionOperation;
    }

    public PreviewPickKind Kind { get; }
    public int Index { get; }
    public ParticleSelectionOperation SelectionOperation { get; }
    public bool AddToSelection => SelectionOperation != ParticleSelectionOperation.Replace;
}

public sealed class ParticleSelectionEventArgs : EventArgs
{
    public ParticleSelectionEventArgs(IReadOnlyList<int> particleIndices, ParticleSelectionOperation selectionOperation)
    {
        ParticleIndices = particleIndices;
        SelectionOperation = selectionOperation;
    }

    public IReadOnlyList<int> ParticleIndices { get; }
    public ParticleSelectionOperation SelectionOperation { get; }
}

public sealed class ColliderSelectionEventArgs(IReadOnlyList<int> colliderIndices, ParticleSelectionOperation selectionOperation) : EventArgs
{
    public IReadOnlyList<int> ColliderIndices { get; } = colliderIndices;
    public ParticleSelectionOperation SelectionOperation { get; } = selectionOperation;
}

public sealed class ParticleMoveEventArgs : EventArgs
{
    public ParticleMoveEventArgs(SysVec3 delta, bool localAxis)
    {
        Delta = delta;
        LocalAxis = localAxis;
    }

    public SysVec3 Delta { get; }
    public bool LocalAxis { get; }
}

public sealed class ParticleScaleEventArgs : EventArgs
{
    public ParticleScaleEventArgs(float factor, SysVec3? axis, bool localAxis, bool radiusOnly)
    {
        Factor = factor;
        Axis = axis;
        LocalAxis = localAxis;
        RadiusOnly = radiusOnly;
    }

    public float Factor { get; }
    public SysVec3? Axis { get; }
    public bool LocalAxis { get; }
    public bool RadiusOnly { get; }
}

public sealed class ParticleRotateEventArgs : EventArgs
{
    public ParticleRotateEventArgs(float radians, SysVec3 axis, bool localAxis)
    {
        Radians = radians;
        Axis = axis;
        LocalAxis = localAxis;
    }

    public float Radians { get; }
    public SysVec3 Axis { get; }
    public bool LocalAxis { get; }
}

public sealed class MirrorRequestedEventArgs : EventArgs
{
    public MirrorRequestedEventArgs(char axis, bool local)
    {
        Axis = axis;
        Local = local;
    }

    public char Axis { get; }
    public bool Local { get; }
}

public sealed class ViewportCameraState
{
    public float Yaw { get; init; }
    public float Pitch { get; init; }
    public float OrthoHeight { get; init; }
    public SysVec3 Target { get; init; }
}

public sealed class ParticlePreviewControl : GLControl
{
    private const float DefaultYaw = -2.5915927f;
    private const float DefaultPitch = -0.35f;
    private enum ParticleTransformMode
    {
        None,
        Move,
        Scale,
        Rotate
    }

    public event EventHandler<PreviewPickEventArgs>? ItemPicked;
    public event EventHandler<ParticleSelectionEventArgs>? ParticlesSelected;
    public event EventHandler<ColliderSelectionEventArgs>? CollidersSelected;
    public event EventHandler? ParticleMoveStarted;
    public event EventHandler<ParticleMoveEventArgs>? ParticlesMoved;
    public event EventHandler<ParticleScaleEventArgs>? ParticlesScaled;
    public event EventHandler<ParticleRotateEventArgs>? ParticlesRotated;
    public event EventHandler<MirrorRequestedEventArgs>? MirrorRequested;
    public event EventHandler? MirrorModeChanged;
    public event EventHandler? CopyRequested;
    public event EventHandler? PasteRequested;
    public event EventHandler? PasteMirroredRequested;
    public event EventHandler? LinkRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? ParticleMoveEnded;
    public event EventHandler? ParticleMoveCanceled;

    private ParticlePreviewData? _data;
    private readonly Dictionary<int, SysVec3> _physicsBindParticles = new();
    private readonly Dictionary<int, SimulatedBonePreviewPose> _physicsBindBones = new();
    private readonly Dictionary<int, ColliderPreviewShape> _physicsBindColliders = new();
    private Point _lastMouse;
    private Point _mouseDown;
    private bool _rotating;
    private bool _panning;
    private bool _boxSelecting;
    private bool _movingParticles;
    private ParticleTransformMode _armedTransformMode = ParticleTransformMode.None;
    private ParticleTransformMode _activeTransformMode = ParticleTransformMode.None;
    private Vector3? _axisConstraint;
    private Keys _axisConstraintKey = Keys.None;
    private bool _localAxisConstraint;
    private bool _colliderRadiusScale;
    private Rectangle _selectionRectangle;
    private readonly HashSet<int> _selectedParticleIndices = new();
    private readonly HashSet<int> _selectedColliderIndices = new();
    private float _yaw = DefaultYaw;
    private float _pitch = DefaultPitch;
    private float _orthoHeight = 1.0f;
    private Vector3 _target = Vector3.Zero;
    private Vector3 _baseCenter = Vector3.Zero;
    private Vector3 _viewRoot = Vector3.Zero;
    private float _sceneRadius = 0.5f;
    private float _gridStep = 0.05f;
    private float _gridExtent = 1.0f;
    private bool _glReady;
    private bool _renderFailed;
    private string? _renderError;
    private int _program;
    private int _vao;
    private int _vbo;
    private int _uMvp;
    private int _uColor;
    private int _uPointSize;
    private int _uRoundPoints;
    private VisualModelData? _visualModel;
    private VisualModelDisplaySettings _visualModelSettings = new();
    private readonly List<VisualModelGpuMesh> _visualModelGpuMeshes = new();
    private int _visualModelProgram;
    private int _visualModelMvpLocation;
    private int _visualModelModelLocation;
    private int _visualModelColorLocation;
    private int _visualModelTextureLocation;
    private int _visualModelUseTextureLocation;
    private int _visualModelSpecularTextureLocation;
    private int _visualModelUseSpecularTextureLocation;
    private int _visualModelNormalTextureLocation;
    private int _visualModelUseNormalTextureLocation;
    private int _visualModelAmbientOcclusionTextureLocation;
    private int _visualModelUseAmbientOcclusionTextureLocation;
    private int _visualModelAuxiliaryTextureLocation;
    private int _visualModelUseAuxiliaryTextureLocation;
    private int _visualModelEyeGlintTextureLocation;
    private int _visualModelUseEyeGlintTextureLocation;
    private int _visualModelAlphaModeLocation;
    private int _visualModelShaderModelLocation;
    private int _visualModelAuxiliaryStrengthLocation;
    private int _visualModelEyeGlintStrengthLocation;
    private int _visualModelMetalChannelLocation;
    private int _visualModelSmoothnessChannelLocation;
    private int _visualModelMetalScaleLocation;
    private int _visualModelSmoothnessScaleLocation;
    private int _visualModelNormalStrengthLocation;
    private int _visualModelAmbientOcclusionStrengthLocation;
    private int _visualModelOpacityLocation;
    private int _visualModelKeyLightLocation;
    private int _visualModelFillLightLocation;
    private int _visualModelViewDirectionLocation;
    private bool _visualModelGpuDirty;
    private readonly ContextMenuStrip _viewportMenu;
    private bool _mirrorModeEnabled;
    private bool _physicsOverlayVisible = true;

    private sealed class VisualModelGpuMesh
    {
        public int VertexArray { get; init; }
        public int VertexBuffer { get; init; }
        public int IndexBuffer { get; init; }
        public int Texture { get; init; }
        public int SpecularTexture { get; init; }
        public int NormalTexture { get; init; }
        public int AmbientOcclusionTexture { get; init; }
        public int AuxiliaryTexture { get; init; }
        public int EyeGlintTexture { get; init; }
        public VisualModelAlphaMode AlphaMode { get; init; }
        public VisualModelMaterialSettings Material { get; init; } = new();
        public int IndexCount { get; init; }
        public Vector4 BaseColor { get; init; }
        public VisualModelCullingMode CullingMode { get; init; }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedParticleIndex { get; set; } = -1;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedBoneIndex { get; set; } = -1;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedColliderIndex { get; set; } = -1;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PreviewPickKind PickKind { get; set; } = PreviewPickKind.Particle;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool MirrorModeEnabled
    {
        get => _mirrorModeEnabled;
        set
        {
            if (_mirrorModeEnabled == value)
                return;

            _mirrorModeEnabled = value;
            MirrorModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SysVec3 ViewRoot => new(_viewRoot.X, _viewRoot.Y, _viewRoot.Z);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool HasVisualModel => _visualModel != null;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool PhysicsOverlayVisible
    {
        get => _physicsOverlayVisible;
        set
        {
            if (_physicsOverlayVisible == value)
                return;

            _physicsOverlayVisible = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? VisualModelPath => _visualModel?.SourcePath;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public VisualModelData? VisualModel => _visualModel;

    public VisualModelDisplaySettings GetVisualModelDisplaySettings() => _visualModelSettings.Clone();

    public void RefreshVisualModelMaterials()
    {
        if (_glReady)
        {
            MakeCurrent();
            ReleaseVisualModelGpu();
        }
        else
        {
            _visualModelGpuMeshes.Clear();
        }

        _visualModelGpuDirty = _visualModel != null;
        Invalidate();
    }

    public void SetVisualModel(VisualModelData? model, bool resetCamera = true)
    {
        if (_glReady)
        {
            MakeCurrent();
            ReleaseVisualModelGpu();
        }
        else
        {
            _visualModelGpuMeshes.Clear();
        }

        _visualModel = model;
        _visualModelGpuDirty = model != null;
        _visualModelSettings = new VisualModelDisplaySettings();
        if (resetCamera)
            ResetCameraToData();
        else
            UpdateSceneBounds(updateGrid: false, adjustCameraForGrowth: false);
        Invalidate();
    }

    public void SetVisualModelPose(VisualModelData model)
    {
        if (_visualModel == null ||
            _visualModelGpuMeshes.Count != model.Meshes.Count ||
            _visualModelGpuMeshes.Where((gpu, index) =>
                gpu.IndexCount != model.Meshes[index].Indices.Length).Any())
        {
            SetVisualModel(model, resetCamera: false);
            return;
        }

        _visualModel = model;
        if (_glReady)
        {
            MakeCurrent();
            for (var index = 0; index < model.Meshes.Count; index++)
            {
                var vertices = BuildVisualModelGpuVertices(model.Meshes[index]);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _visualModelGpuMeshes[index].VertexBuffer);
                GL.BufferData(
                    BufferTarget.ArrayBuffer,
                    vertices.Length * sizeof(float),
                    vertices,
                    BufferUsageHint.DynamicDraw);
            }
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        UpdateSceneBounds(updateGrid: false, adjustCameraForGrowth: false);
        Invalidate();
    }
    public void ApplyVisualSkeletonPreview(IReadOnlyList<VisualModelBone> bones) =>
        ApplyBonePreviewPoses(bones.Select(bone => new SidecarBonePreviewPose(
            bone.Name, bone.Position, bone.Rotation, bone.Scale, false)));

    public void ApplyBonePreviewPoses(IEnumerable<SidecarBonePreviewPose> poses)
    {
        if (_data == null)
            return;

        var byName = poses
            .GroupBy(pose => NormalizeVisualBoneName(pose.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        if (byName.Count == 0)
            return;

        var scale = Math.Max(0.0001f, _visualModelSettings.UniformScale);
        var rotation = _visualModelSettings.RotationDegrees;
        var model = SysMat4.CreateScale(
                ReflectSceneX != _visualModelSettings.MirrorX ? -scale : scale,
                scale,
                scale)
            * SysMat4.CreateRotationX(MathHelper.DegreesToRadians(rotation.X))
            * SysMat4.CreateRotationY(MathHelper.DegreesToRadians(rotation.Y))
            * SysMat4.CreateRotationZ(MathHelper.DegreesToRadians(rotation.Z))
            * SysMat4.CreateTranslation(_visualModelSettings.Offset)
            // Undo the presentation conversion before storing physics-space poses.
            * SysMat4.CreateScale(ReflectSceneX ? -1 : 1, 1, 1);

        foreach (var previewBone in _data.Bones)
        {
            if (!byName.TryGetValue(NormalizeVisualBoneName(previewBone.Name), out var pose))
                continue;
            previewBone.Position = SysVec3.Transform(pose.Position, model);
            previewBone.AxisX = NormalizeOrDefault(
                SysVec3.TransformNormal(SysVec3.Transform(SysVec3.UnitX, pose.Rotation), model),
                SysVec3.UnitX);
            previewBone.AxisY = NormalizeOrDefault(
                SysVec3.TransformNormal(SysVec3.Transform(SysVec3.UnitY, pose.Rotation), model),
                SysVec3.UnitY);
            previewBone.AxisZ = NormalizeOrDefault(
                SysVec3.TransformNormal(SysVec3.Transform(SysVec3.UnitZ, pose.Rotation), model),
                SysVec3.UnitZ);
        }

        Invalidate();
    }

    private static string NormalizeVisualBoneName(string name)
    {
        var result = name ?? string.Empty;
        var pipe = result.LastIndexOf('|');
        if (pipe >= 0)
            result = result[(pipe + 1)..];
        var colon = result.LastIndexOf(':');
        return colon >= 0 ? result[(colon + 1)..] : result;
    }
    public void ClearVisualModel() => SetVisualModel(null, resetCamera: false);

    public void FitCameraToVisualModel()
    {
        if (_visualModel == null)
        {
            ResetCameraToData();
            return;
        }

        var modelMatrix = GetVisualModelMatrix();
        var points = GetBoundsCorners(_visualModel.BoundsMinimum, _visualModel.BoundsMaximum)
            .Select(corner => Vector3.TransformPosition(corner, modelMatrix))
            .Where(IsFinite)
            .ToArray();
        if (points.Length == 0)
            return;

        var minimum = points.Aggregate(Vector3.ComponentMin);
        var maximum = points.Aggregate(Vector3.ComponentMax);
        _baseCenter = (minimum + maximum) * 0.5f;
        _viewRoot = ReadOnlyPreview ? new Vector3(_baseCenter.X, minimum.Y, _baseCenter.Z) : _baseCenter;
        _sceneRadius = Math.Max(0.05f, points.Max(point => (point - _baseCenter).Length));
        _gridStep = FixedGridStep(_sceneRadius) * 4.0f;
        _gridExtent = Math.Max(_sceneRadius * 3.5f, _gridStep * 20.0f);
        _target = _baseCenter;
        _yaw = DefaultYaw;
        _pitch = DefaultPitch;
        _orthoHeight = Math.Max(0.2f, _sceneRadius * 2.4f);
        Invalidate();
    }

    public void SetVisualModelDisplaySettings(VisualModelDisplaySettings settings, bool fitCamera = false)
    {
        _visualModelSettings = settings.Clone();
        if (fitCamera)
            ResetCameraToData();
        else
            UpdateSceneBounds(updateGrid: false, adjustCameraForGrowth: false);
        Invalidate();
    }
    public ViewportCameraState CaptureCameraState()
    {
        return new ViewportCameraState
        {
            Yaw = _yaw,
            Pitch = _pitch,
            OrthoHeight = _orthoHeight,
            Target = new SysVec3(_target.X, _target.Y, _target.Z)
        };
    }

    public void RestoreCameraState(ViewportCameraState? state)
    {
        if (state == null)
            return;

        _yaw = state.Yaw;
        _pitch = state.Pitch;
        _orthoHeight = state.OrthoHeight;
        _target = new Vector3(state.Target.X, state.Target.Y, state.Target.Z);
        Invalidate();
    }

    public void SetSelectedParticleIndices(IEnumerable<int> indices)
    {
        _selectedParticleIndices.Clear();
        foreach (var index in indices)
            _selectedParticleIndices.Add(index);

        SelectedParticleIndex = _selectedParticleIndices.Count == 1 ? _selectedParticleIndices.First() : -1;
        Invalidate();
    }

    public void SetSelectedColliderIndices(IEnumerable<int> indices)
    {
        _selectedColliderIndices.Clear();
        _selectedColliderIndices.UnionWith(indices);
        Invalidate();
    }

    public ParticlePreviewControl()
        : base(new GLControlSettings())
    {
        BackColor = Color.FromArgb(56, 56, 56);
        TabStop = true;
        _viewportMenu = BuildViewportMenu();
    }

    public void SetData(ParticlePreviewData? data, bool resetCamera = true)
    {
        var hadSceneData = _data != null;
        _data = data;
        _physicsBindParticles.Clear();
        _physicsBindBones.Clear();
        _physicsBindColliders.Clear();
        if (data != null)
        {
            foreach (var particle in data.Particles)
                _physicsBindParticles[particle.Index] = particle.Position;
            foreach (var bone in data.Bones)
            {
                _physicsBindBones[bone.Index] = new SimulatedBonePreviewPose
                {
                    Position = bone.Position,
                    AxisX = bone.AxisX,
                    AxisY = bone.AxisY,
                    AxisZ = bone.AxisZ,
                    StretchScale = bone.StretchScale
                };
            }
            foreach (var collider in data.Colliders)
                _physicsBindColliders[collider.Index] = CloneCollider(collider);
        }
        if (resetCamera || data == null)
            ResetCameraToData();
        else
            // Opening Editor preserves the camera, but the first real scene
            // still needs to establish a grid scale. Otherwise it keeps the
            // tiny empty-viewport default until a later camera reset.
            UpdateSceneBounds(updateGrid: !hadSceneData);
        Invalidate();
    }

    [System.ComponentModel.DefaultValue(false)]
    public bool ReadOnlyPreview { get; set; }

    [System.ComponentModel.DefaultValue(true)]
    public bool BoneOverlayVisible { get; set; } = true;

    // HKRG stores a complete visual skeleton as well as the smaller ragdoll
    // skeleton. Only one layer should answer bone picks at a time because the
    // two skeletons both use compact 0-based editor indices.
    public void SetReferenceBonesSelectable(bool selectReferenceBones)
    {
        if (_data == null || !_data.Bones.Any(bone => bone.IsReference))
            return;

        foreach (var bone in _data.Bones)
            bone.IsSelectable = bone.IsReference == selectReferenceBones;

        SelectedBoneIndex = -1;
        Invalidate();
    }

    public void UpdateParticlePreviewRows(IEnumerable<ParticleEditRow> rows)
    {
        if (_data == null)
            return;

        var previewRows = _data.Particles.ToDictionary(p => p.Index);
        foreach (var row in rows)
        {
            if (!previewRows.TryGetValue(row.Index, out var particle))
                continue;

            particle.Position = new SysVec3(row.X, row.Y, row.Z);
            particle.Fixed = row.Fixed;
            particle.Radius = row.Radius;
        }

        Invalidate();
    }

    public void UpdateSimulatedParticlePositions(IReadOnlyDictionary<int, SysVec3> positions)
    {
        if (_data == null)
            return;

        foreach (var particle in _data.Particles)
        {
            if (positions.TryGetValue(particle.Index, out var position))
                particle.Position = position;
        }

        Invalidate();
    }

    public void UpdateSimulatedBonePositions(IReadOnlyDictionary<int, SysVec3> positions)
    {
        if (_data == null)
            return;

        foreach (var bone in _data.Bones)
        {
            if (positions.TryGetValue(bone.Index, out var position))
                bone.Position = position;
        }

        Invalidate();
    }

    public void UpdateSimulatedPose(
        IReadOnlyDictionary<int, SysVec3> particlePositions,
        IReadOnlyDictionary<int, SimulatedBonePreviewPose> bonePoses,
        IReadOnlyDictionary<int, ColliderPreviewShape>? colliderPoses = null)
    {
        if (_data == null)
            return;

        foreach (var particle in _data.Particles)
        {
            if (particlePositions.TryGetValue(particle.Index, out var position))
                particle.Position = position;
        }

        foreach (var bone in _data.Bones)
        {
            if (!bonePoses.TryGetValue(bone.Index, out var pose))
                continue;

            bone.Position = pose.Position;
            bone.AxisX = pose.AxisX;
            bone.AxisY = pose.AxisY;
            bone.AxisZ = pose.AxisZ;
            bone.StretchScale = pose.StretchScale;
        }

        if (colliderPoses != null)
        {
            foreach (var collider in _data.Colliders)
            {
                if (colliderPoses.TryGetValue(collider.Index, out var pose))
                    CopyColliderPose(collider, pose);
            }
        }

        Invalidate();
    }

    public void UpdateAnimatedPhysicsPose(
        IReadOnlyDictionary<int, SysMat4> boneDeltas,
        IReadOnlySet<int> clothDrivenBoneIndices,
        IReadOnlySet<int>? particleAnimationExclusions = null)
    {
        if (_data == null)
            return;

        var bonesByIndex = _data.Bones
            .GroupBy(bone => bone.Index)
            .ToDictionary(group => group.Key, group => group.First());
        var resolvedWorld = new Dictionary<int, SysMat4>();
        var resolving = new HashSet<int>();

        SysMat4 GetBindWorld(int boneIndex)
        {
            if (!_physicsBindBones.TryGetValue(boneIndex, out var bind))
                return SysMat4.Identity;
            return new SysMat4(
                bind.AxisX.X, bind.AxisX.Y, bind.AxisX.Z, 0.0f,
                bind.AxisY.X, bind.AxisY.Y, bind.AxisY.Z, 0.0f,
                bind.AxisZ.X, bind.AxisZ.Y, bind.AxisZ.Z, 0.0f,
                bind.Position.X, bind.Position.Y, bind.Position.Z, 1.0f);
        }

        SysMat4 ResolveWorld(int boneIndex)
        {
            if (resolvedWorld.TryGetValue(boneIndex, out var resolved))
                return resolved;

            var bindWorld = GetBindWorld(boneIndex);
            if (!resolving.Add(boneIndex))
                return bindWorld;

            try
            {
                // Body and attachment bones consume their animation track.
                // Cloth output bones deliberately ignore theirs, but retain
                // their bind-local transform beneath the resolved parent.
                if (!clothDrivenBoneIndices.Contains(boneIndex) &&
                    boneDeltas.TryGetValue(boneIndex, out var animatedDelta))
                {
                    resolved = bindWorld * animatedDelta;
                }
                else if (bonesByIndex.TryGetValue(boneIndex, out var bone) &&
                         bone.ParentIndex >= 0 &&
                         _physicsBindBones.ContainsKey(bone.ParentIndex) &&
                         SysMat4.Invert(GetBindWorld(bone.ParentIndex), out var inverseParentBind))
                {
                    var bindLocal = bindWorld * inverseParentBind;
                    resolved = bindLocal * ResolveWorld(bone.ParentIndex);
                }
                else
                {
                    resolved = bindWorld;
                }

                resolvedWorld[boneIndex] = resolved;
                return resolved;
            }
            finally
            {
                resolving.Remove(boneIndex);
            }
        }

        var particleResolvedWorld = new Dictionary<int, SysMat4>();
        var particleResolving = new HashSet<int>();
        var particleExclusions = particleAnimationExclusions ?? clothDrivenBoneIndices;
        SysMat4 ResolveParticleWorld(int boneIndex)
        {
            if (particleResolvedWorld.TryGetValue(boneIndex, out var resolved))
                return resolved;
            var bindWorld = GetBindWorld(boneIndex);
            if (!particleResolving.Add(boneIndex))
                return bindWorld;

            try
            {
                if (!particleExclusions.Contains(boneIndex) &&
                    boneDeltas.TryGetValue(boneIndex, out var animatedDelta))
                {
                    resolved = bindWorld * animatedDelta;
                }
                else if (bonesByIndex.TryGetValue(boneIndex, out var bone) &&
                         bone.ParentIndex >= 0 &&
                         _physicsBindBones.ContainsKey(bone.ParentIndex) &&
                         SysMat4.Invert(GetBindWorld(bone.ParentIndex), out var inverseParentBind))
                {
                    resolved = bindWorld * inverseParentBind * ResolveParticleWorld(bone.ParentIndex);
                }
                else
                {
                    resolved = bindWorld;
                }

                particleResolvedWorld[boneIndex] = resolved;
                return resolved;
            }
            finally
            {
                particleResolving.Remove(boneIndex);
            }
        }

        foreach (var bone in _data.Bones)
        {
            if (!_physicsBindBones.TryGetValue(bone.Index, out var bind))
                continue;

            var world = ResolveWorld(bone.Index);
            bone.Position = new SysVec3(world.M41, world.M42, world.M43);
            bone.AxisX = NormalizeOrDefault(new SysVec3(world.M11, world.M12, world.M13), bind.AxisX);
            bone.AxisY = NormalizeOrDefault(new SysVec3(world.M21, world.M22, world.M23), bind.AxisY);
            bone.AxisZ = NormalizeOrDefault(new SysVec3(world.M31, world.M32, world.M33), bind.AxisZ);
            bone.StretchScale = bind.StretchScale;
        }

        var attachmentsByParticle = _data.ParticleAttachments
            .Where(attachment => attachment.BoneWeights.Count > 0)
            .GroupBy(attachment => attachment.ParticleIndex)
            .ToDictionary(group => group.Key, group => group.Last().BoneWeights);
        var weightedAnchors = attachmentsByParticle
            .Where(entry => _physicsBindParticles.ContainsKey(entry.Key))
            .Select(entry => (
                BindPosition: _physicsBindParticles[entry.Key],
                Attachments: (IReadOnlyList<ParticlePreviewBoneWeight>)entry.Value))
            .ToArray();

        bool TryTransformParticlePoint(
            SysVec3 bindPosition,
            IReadOnlyList<ParticlePreviewBoneWeight> attachments,
            out SysVec3 transformed,
            bool useReferencePosition = true)
        {
            transformed = SysVec3.Zero;
            var totalWeight = 0.0f;
            foreach (var attachment in attachments)
            {
                var weight = Math.Max(0.0f, attachment.Weight);
                if (weight <= 0.0f ||
                    !_physicsBindBones.ContainsKey(attachment.BoneIndex) ||
                    !SysMat4.Invert(GetBindWorld(attachment.BoneIndex), out var inverseBindBone))
                {
                    continue;
                }

                var delta = inverseBindBone * ResolveParticleWorld(attachment.BoneIndex);
                transformed += SysVec3.Transform(useReferencePosition ? attachment.ReferencePosition ?? bindPosition : bindPosition, delta) * weight;
                totalWeight += weight;
            }
            if (totalWeight <= 0.000001f)
                return false;
            transformed /= totalWeight;
            return true;
        }

        foreach (var particle in _data.Particles)
        {
            if (!_physicsBindParticles.TryGetValue(particle.Index, out var bindPosition))
                continue;
            if (attachmentsByParticle.TryGetValue(particle.Index, out var attachments) &&
                TryTransformParticlePoint(bindPosition, attachments, out var attachedPosition))
            {
                particle.Position = attachedPosition;
                continue;
            }

            // Dynamic vertices usually have no direct transform-set weights.
            // In the stopped preview, carry their authored rest shape with a
            // smooth blend of the nearest weighted/fixed anchor transforms.
            var position = SysVec3.Zero;
            var totalWeight = 0.0f;
            foreach (var anchor in weightedAnchors
                         .OrderBy(anchor => SysVec3.DistanceSquared(bindPosition, anchor.BindPosition))
                         .Take(4))
            {
                if (!TryTransformParticlePoint(bindPosition, anchor.Attachments, out var anchorPosition, useReferencePosition: false))
                    continue;
                var distanceSquared = SysVec3.DistanceSquared(bindPosition, anchor.BindPosition);
                var weight = 1.0f / Math.Max(0.000001f, distanceSquared);
                position += anchorPosition * weight;
                totalWeight += weight;
            }
            if (totalWeight > 0.000001f)
            {
                particle.Position = position / totalWeight;
                continue;
            }

            if (_physicsBindBones.ContainsKey(_data.TransferMotionTransformIndex) &&
                SysMat4.Invert(GetBindWorld(_data.TransferMotionTransformIndex), out var inverseTransferBind))
            {
                particle.Position = SysVec3.Transform(
                    bindPosition,
                    inverseTransferBind * ResolveParticleWorld(_data.TransferMotionTransformIndex));
            }
            else
            {
                particle.Position = bindPosition;
            }
        }

        foreach (var collider in _data.Colliders)
        {
            if (!_physicsBindColliders.TryGetValue(collider.Index, out var bind))
                continue;
            var bindBoneWorld = GetBindWorld(bind.BoneIndex);
            var delta = SysMat4.Invert(bindBoneWorld, out var inverseBindBone)
                ? inverseBindBone * ResolveWorld(bind.BoneIndex)
                : SysMat4.Identity;
            collider.Start = SysVec3.Transform(bind.Start, delta);
            collider.End = SysVec3.Transform(bind.End, delta);
            collider.PlaneNormal = NormalizeOrDefault(
                SysVec3.TransformNormal(bind.PlaneNormal, delta),
                bind.PlaneNormal);
            collider.Radius = bind.Radius;
            collider.EndRadius = bind.EndRadius;
        }

        Invalidate();
    }

    private static ColliderPreviewShape CloneCollider(ColliderPreviewShape source) => new()
    {
        Index = source.Index,
        CollisionBit = source.CollisionBit,
        Name = source.Name,
        BoneIndex = source.BoneIndex,
        Start = source.Start,
        End = source.End,
        Radius = source.Radius,
        EndRadius = source.EndRadius,
        PlaneNormal = source.PlaneNormal,
        Kind = source.Kind
    };

    private static void CopyColliderPose(ColliderPreviewShape target, ColliderPreviewShape source)
    {
        target.Start = source.Start;
        target.End = source.End;
        target.Radius = source.Radius;
        target.EndRadius = source.EndRadius;
        target.PlaneNormal = source.PlaneNormal;
    }

    public void UpdateSimulatedRagdollPose(
        IReadOnlyDictionary<int, SimulatedBonePreviewPose> bonePoses,
        IReadOnlyDictionary<int, ColliderPreviewShape> colliderPoses)
    {
        if (_data == null)
            return;

        foreach (var bone in _data.Bones)
        {
            if (bone.IsReference || !bonePoses.TryGetValue(GetBoneEditorIndex(bone), out var pose))
                continue;

            bone.Position = pose.Position;
            bone.AxisX = pose.AxisX;
            bone.AxisY = pose.AxisY;
            bone.AxisZ = pose.AxisZ;
            bone.StretchScale = pose.StretchScale;
        }

        foreach (var collider in _data.Colliders)
        {
            if (!colliderPoses.TryGetValue(collider.Index, out var pose))
                continue;

            collider.Start = pose.Start;
            collider.End = pose.End;
            collider.Radius = pose.Radius;
            collider.EndRadius = pose.EndRadius;
            collider.PlaneNormal = pose.PlaneNormal;
        }

        Invalidate();
    }

    // Bone edits happen repeatedly while a modal G/R/S transform is active.
    // Rebuilding the entire HKCL/BPHCL preview here made those transforms crawl,
    // so update the viewport's already-loaded skeleton instead.
    public void UpdateBonePreviewRows(IReadOnlyList<BoneEditRow> rows)
    {
        if (_data == null)
            return;

        var rowsByIndex = rows.ToDictionary(row => row.Index);
        var worldMatrices = new Dictionary<int, SysMat4>();
        foreach (var bone in _data.Bones)
        {
            var editorIndex = GetBoneEditorIndex(bone);
            if (!bone.IsSelectable || !rowsByIndex.ContainsKey(editorIndex))
                continue;

            var world = GetBoneWorldMatrix(editorIndex, rowsByIndex, worldMatrices, new HashSet<int>());
            bone.Position = new SysVec3(world.M41, world.M42, world.M43);
            bone.AxisX = NormalizeOrDefault(SysVec3.TransformNormal(SysVec3.UnitX, world), SysVec3.UnitX);
            bone.AxisY = NormalizeOrDefault(SysVec3.TransformNormal(SysVec3.UnitY, world), SysVec3.UnitY);
            bone.AxisZ = NormalizeOrDefault(SysVec3.TransformNormal(SysVec3.UnitZ, world), SysVec3.UnitZ);
        }

        Invalidate();
    }

    public void UpdateColliderPreviewRows(IEnumerable<ColliderEditRow> rows)
    {
        if (_data == null)
            return;

        foreach (var row in rows)
        {
            // The same global hclCollidable can deliberately occupy more than
            // one per-instance slot. Update every slot, not only the first.
            foreach (var collider in _data.Colliders.Where(candidate => candidate.Index == row.Index))
            {
                collider.Start = new SysVec3(row.StartX, row.StartY, row.StartZ);
                collider.TransformOrigin = new SysVec3(row.Transform.M41, row.Transform.M42, row.Transform.M43);
                collider.End = new SysVec3(row.EndX, row.EndY, row.EndZ);
                collider.Radius = row.Radius;
                if (collider.Kind is ColliderPreviewKind.Capsule or ColliderPreviewKind.Sphere)
                    collider.EndRadius = row.Radius;
                if (collider.Kind == ColliderPreviewKind.Plane)
                    collider.PlaneNormal = NormalizeOrDefault(
                        new SysVec3(row.PlaneNormalX, row.PlaneNormalY, row.PlaneNormalZ),
                        SysVec3.UnitY);
            }
        }

        Invalidate();
    }

    private static SysMat4 GetBoneWorldMatrix(
        int boneIndex,
        IReadOnlyDictionary<int, BoneEditRow> rows,
        IDictionary<int, SysMat4> cache,
        ISet<int> visiting)
    {
        if (boneIndex < 0 || !rows.TryGetValue(boneIndex, out var bone))
            return SysMat4.Identity;
        if (cache.TryGetValue(boneIndex, out var cached))
            return cached;
        if (!visiting.Add(boneIndex))
            return SysMat4.Identity;

        var parentWorld = GetBoneWorldMatrix(bone.ParentIndex, rows, cache, visiting);
        visiting.Remove(boneIndex);

        var rotation = new SysQuat(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW);
        rotation = rotation.LengthSquared() < 0.000001f ? SysQuat.Identity : SysQuat.Normalize(rotation);
        var local = SysMat4.CreateScale(bone.ScaleX, bone.ScaleY, bone.ScaleZ)
            * SysMat4.CreateFromQuaternion(rotation)
            * SysMat4.CreateTranslation(bone.X, bone.Y, bone.Z);
        var world = local * parentWorld;
        cache[boneIndex] = world;
        return world;
    }

    private static SysVec3 NormalizeOrDefault(SysVec3 value, SysVec3 fallback)
    {
        return value.LengthSquared() < 0.000001f ? fallback : SysVec3.Normalize(value);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        try
        {
            MakeCurrent();
            GL.ClearColor(0.22f, 0.22f, 0.22f, 1.0f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.ProgramPointSize);

            CreateShaderProgram();
            CreateVisualModelShaderProgram();
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
            _glReady = true;
        }
        catch (Exception ex)
        {
            _renderFailed = true;
            _renderError = ex.Message;
            _glReady = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_glReady && !IsDisposed)
        {
            MakeCurrent();
            ReleaseVisualModelGpu();
            if (_vbo != 0)
                GL.DeleteBuffer(_vbo);
            if (_vao != 0)
                GL.DeleteVertexArray(_vao);
            if (_program != 0)
                GL.DeleteProgram(_program);
            if (_visualModelProgram != 0)
                GL.DeleteProgram(_visualModelProgram);
            _glReady = false;
        }

        base.Dispose(disposing);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_activeTransformMode != ParticleTransformMode.None)
        {
            if (e.Button == MouseButtons.Left)
                FinishModalTransform(commit: true);
            else if (e.Button == MouseButtons.Right)
                FinishModalTransform(commit: false);
            return;
        }

        if (e.Button == MouseButtons.Left && TryHandleAxisWidgetClick(e.Location))
        {
            Focus();
            return;
        }

        _lastMouse = e.Location;
        _mouseDown = e.Location;
        var shiftDown = (ModifierKeys & Keys.Shift) == Keys.Shift;
        _rotating = (e.Button == MouseButtons.Middle || (ReadOnlyPreview && e.Button == MouseButtons.Left)) && !shiftDown;
        _panning = e.Button == MouseButtons.Middle && shiftDown;
        _boxSelecting = false;
        _movingParticles = false;

        if (e.Button == MouseButtons.Left && !ReadOnlyPreview)
        {
            _boxSelecting = true;
            _selectionRectangle = Rectangle.Empty;
        }

        Capture = _rotating || _panning || _boxSelecting || _movingParticles;
        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_activeTransformMode != ParticleTransformMode.None && _armedTransformMode != ParticleTransformMode.None)
        {
            ApplyModalTransform(e.Location);
            _lastMouse = e.Location;
            Invalidate();
            return;
        }

        if (!_rotating && !_panning && !_boxSelecting && !_movingParticles)
            return;

        var dx = e.X - _lastMouse.X;
        var dy = e.Y - _lastMouse.Y;

        if (_rotating)
        {
            _yaw -= dx * 0.01f;
            _pitch = Math.Clamp(_pitch - dy * 0.01f, -1.45f, 1.45f);
        }
        else if (_panning)
        {
            var (_, right, up) = GetCameraBasis();
            var worldPerPixel = _orthoHeight / Math.Max(1, ClientSize.Height);
            _target -= right * (dx * worldPerPixel);
            _target += up * (dy * worldPerPixel);
        }
        else if (_movingParticles)
        {
            var (_, right, up) = GetCameraBasis();
            var worldPerPixel = _orthoHeight / Math.Max(1, ClientSize.Height);
            if (_activeTransformMode == ParticleTransformMode.Scale)
            {
                var factor = MathF.Exp((dy - dx) * 0.01f);
                ParticlesScaled?.Invoke(this, new ParticleScaleEventArgs(factor, ToSysAxis(_axisConstraint), _localAxisConstraint, _colliderRadiusScale));
            }
            else if (_activeTransformMode == ParticleTransformMode.Rotate)
            {
                var axis = _axisConstraint ?? GetCameraBasis().Forward;
                ParticlesRotated?.Invoke(this, new ParticleRotateEventArgs(dx * 0.012f, ToSysVector(axis), _localAxisConstraint));
            }
            else
            {
                var delta = right * (dx * worldPerPixel) - up * (dy * worldPerPixel);
                if (_axisConstraint.HasValue)
                    delta = _axisConstraint.Value * Vector3.Dot(delta, _axisConstraint.Value);
                ParticlesMoved?.Invoke(this, new ParticleMoveEventArgs(ToSysVector(delta), _localAxisConstraint));
            }
        }
        else if (_boxSelecting)
        {
            _selectionRectangle = MakeRectangle(_mouseDown, e.Location);
        }

        _lastMouse = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (ReadOnlyPreview)
        {
            _rotating = _panning = _boxSelecting = _movingParticles = false;
            Capture = false;
            return;
        }
        var wasMoving = _movingParticles;
        var wasBoxSelecting = _boxSelecting;
        var wasPanning = _panning;
        var moved = Math.Abs(e.X - _mouseDown.X) + Math.Abs(e.Y - _mouseDown.Y);
        _rotating = false;
        _panning = false;
        _boxSelecting = false;
        _movingParticles = false;
        if (wasMoving)
        {
            _activeTransformMode = ParticleTransformMode.None;
            _armedTransformMode = ParticleTransformMode.None;
            _axisConstraint = null;
            _axisConstraintKey = Keys.None;
            _localAxisConstraint = false;
        }
        Capture = false;

        if (wasMoving)
        {
            if (e.Button == MouseButtons.Right && moved <= 3)
            {
                ParticleMoveCanceled?.Invoke(this, EventArgs.Empty);
                _viewportMenu.Show(this, e.Location);
            }
            else
            {
                ParticleMoveEnded?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (wasBoxSelecting && e.Button == MouseButtons.Left)
        {
            if (moved <= 3)
                PickAt(e.Location);
            else
                SelectParticlesInRectangle(_selectionRectangle);
            _selectionRectangle = Rectangle.Empty;
        }
        else if (!wasPanning && e.Button == MouseButtons.Right && moved <= 3)
        {
            _viewportMenu.Show(this, e.Location);
        }
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var minimumHeight = Math.Max(0.0001f, _sceneRadius * 0.0001f);
        var maximumHeight = Math.Max(50.0f, _sceneRadius * 100.0f);
        _orthoHeight = Math.Clamp(_orthoHeight * (e.Delta > 0 ? 0.88f : 1.14f), minimumHeight, maximumHeight);
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (ReadOnlyPreview)
        {
            if (e.KeyCode == Keys.Home) FitCameraToVisualModel();
            return;
        }
        if (_activeTransformMode != ParticleTransformMode.None)
        {
            if (e.KeyCode == Keys.X)
            {
                SetAxisConstraint(Keys.X, Vector3.UnitX);
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Y)
            {
                SetAxisConstraint(Keys.Y, Vector3.UnitY);
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Z)
            {
                SetAxisConstraint(Keys.Z, Vector3.UnitZ);
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.S &&
                     PickKind == PreviewPickKind.Collider &&
                     _activeTransformMode == ParticleTransformMode.Scale)
            {
                _colliderRadiusScale = true;
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                FinishModalTransform(commit: false);
                e.Handled = true;
            }
            return;
        }

        if (e.KeyCode == Keys.F)
        {
            LinkRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Delete)
        {
            DeleteRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (!HasTransformSelection())
            return;

        if (e.KeyCode == Keys.G)
        {
            StartModalTransform(ParticleTransformMode.Move);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.S)
        {
            StartModalTransform(ParticleTransformMode.Scale);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.R)
        {
            StartModalTransform(ParticleTransformMode.Rotate);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _armedTransformMode = ParticleTransformMode.None;
            e.Handled = true;
        }
    }

    private void StartModalTransform(ParticleTransformMode mode)
    {
        _armedTransformMode = mode;
        _activeTransformMode = mode;
        _axisConstraint = null;
        _axisConstraintKey = Keys.None;
        _localAxisConstraint = false;
        _colliderRadiusScale = false;
        _lastMouse = PointToClient(Cursor.Position);
        _mouseDown = _lastMouse;
        _rotating = false;
        _panning = false;
        _boxSelecting = false;
        _movingParticles = false;
        Capture = true;
        ParticleMoveStarted?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyModalTransform(Point location)
    {
        var dx = location.X - _lastMouse.X;
        var dy = location.Y - _lastMouse.Y;
        if (dx == 0 && dy == 0)
            return;

        var worldPerPixel = _orthoHeight / Math.Max(1, ClientSize.Height);
        if (_activeTransformMode == ParticleTransformMode.Scale)
        {
            var factor = MathF.Exp((dy - dx) * 0.01f);
            ParticlesScaled?.Invoke(this, new ParticleScaleEventArgs(factor, ToSysAxis(_axisConstraint), _localAxisConstraint, _colliderRadiusScale));
        }
        else if (_activeTransformMode == ParticleTransformMode.Rotate)
        {
            var axis = _axisConstraint ?? GetCameraBasis().Forward;
            ParticlesRotated?.Invoke(this, new ParticleRotateEventArgs(dx * 0.012f, ToSysVector(axis), _localAxisConstraint));
        }
        else
        {
            var (_, right, up) = GetCameraBasis();
            var delta = right * (dx * worldPerPixel) - up * (dy * worldPerPixel);
            if (_axisConstraint.HasValue)
                delta = _axisConstraint.Value * Vector3.Dot(delta, _axisConstraint.Value);
            ParticlesMoved?.Invoke(this, new ParticleMoveEventArgs(ToSysVector(delta), _localAxisConstraint));
        }
    }

    private void FinishModalTransform(bool commit)
    {
        if (_activeTransformMode == ParticleTransformMode.None)
            return;

        _activeTransformMode = ParticleTransformMode.None;
        _armedTransformMode = ParticleTransformMode.None;
        _axisConstraint = null;
        _axisConstraintKey = Keys.None;
        _localAxisConstraint = false;
        _colliderRadiusScale = false;
        Capture = false;
        if (commit)
            ParticleMoveEnded?.Invoke(this, EventArgs.Empty);
        else
            ParticleMoveCanceled?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private ContextMenuStrip BuildViewportMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(48, 48, 48),
            ForeColor = Color.Gainsboro,
            Renderer = new DarkToolStripRenderer()
        };
        var global = new ToolStripMenuItem("Mirror Global");
        StyleMenuItem(global);
        global.DropDownItems.Add(MakeMirrorMenuItem("X", 'X', local: false));
        global.DropDownItems.Add(MakeMirrorMenuItem("Y", 'Y', local: false));
        global.DropDownItems.Add(MakeMirrorMenuItem("Z", 'Z', local: false));

        var local = new ToolStripMenuItem("Mirror Local");
        StyleMenuItem(local);
        local.DropDownItems.Add(MakeMirrorMenuItem("X", 'X', local: true));
        local.DropDownItems.Add(MakeMirrorMenuItem("Y", 'Y', local: true));
        local.DropDownItems.Add(MakeMirrorMenuItem("Z", 'Z', local: true));

        var delete = new ToolStripMenuItem("Delete Selected");
        StyleMenuItem(delete);
        delete.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);

        var copy = new ToolStripMenuItem("Copy");
        StyleMenuItem(copy);
        copy.Click += (_, _) => CopyRequested?.Invoke(this, EventArgs.Empty);

        var paste = new ToolStripMenuItem("Paste");
        StyleMenuItem(paste);
        paste.Click += (_, _) => PasteRequested?.Invoke(this, EventArgs.Empty);

        var pasteMirrored = new ToolStripMenuItem("Paste X-Flipped");
        StyleMenuItem(pasteMirrored);
        pasteMirrored.Click += (_, _) => PasteMirroredRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(global);
        menu.Items.Add(local);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(pasteMirrored);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(delete);
        return menu;
    }

    private ToolStripMenuItem MakeMirrorMenuItem(string text, char axis, bool local)
    {
        var item = new ToolStripMenuItem(text);
        StyleMenuItem(item);
        item.Click += (_, _) => MirrorRequested?.Invoke(this, new MirrorRequestedEventArgs(axis, local));
        return item;
    }

    private static void StyleMenuItem(ToolStripMenuItem item)
    {
        item.BackColor = Color.FromArgb(48, 48, 48);
        item.ForeColor = Color.Gainsboro;
    }

    private sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer() : base(new DarkColorTable())
        {
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(48, 48, 48);
        public override Color ImageMarginGradientBegin => Color.FromArgb(48, 48, 48);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(48, 48, 48);
        public override Color ImageMarginGradientEnd => Color.FromArgb(48, 48, 48);
        public override Color MenuItemSelected => Color.FromArgb(70, 70, 70);
        public override Color MenuItemBorder => Color.FromArgb(110, 110, 110);
        public override Color MenuBorder => Color.FromArgb(90, 90, 90);
    }

    private bool TryHandleAxisWidgetClick(Point location)
    {
        var reset = GetAxisResetRectangle();
        if (reset.Contains(location))
        {
            ResetCameraToData();
            Invalidate();
            return true;
        }

        var points = GetAxisWidgetPoints();
        foreach (var item in points)
        {
            if (DistanceSquared(item.Screen, location) > 14.0f * 14.0f)
                continue;

            SetCameraForward(-item.Axis);
            Invalidate();
            return true;
        }

        return false;
    }

    private Rectangle GetAxisResetRectangle()
    {
        return new Rectangle(Math.Max(8, Width - 138), 12, 34, 24);
    }

    private IReadOnlyList<(Vector3 Axis, PointF Screen, Vector4 Color)> GetAxisWidgetPoints()
    {
        var center = new PointF(Width - 58, 58);
        var size = 36.0f;
        var (_, right, up) = GetCameraBasis();
        return new[]
        {
            MakeAxisPoint(Vector3.UnitX, center, size, right, up, new Vector4(1.0f, 0.16f, 0.16f, 1.0f)),
            MakeAxisPoint(-Vector3.UnitX, center, size, right, up, new Vector4(0.65f, 0.10f, 0.10f, 1.0f)),
            MakeAxisPoint(Vector3.UnitY, center, size, right, up, new Vector4(0.20f, 0.95f, 0.34f, 1.0f)),
            MakeAxisPoint(-Vector3.UnitY, center, size, right, up, new Vector4(0.10f, 0.55f, 0.20f, 1.0f)),
            MakeAxisPoint(Vector3.UnitZ, center, size, right, up, new Vector4(0.25f, 0.42f, 1.0f, 1.0f)),
            MakeAxisPoint(-Vector3.UnitZ, center, size, right, up, new Vector4(0.12f, 0.22f, 0.65f, 1.0f))
        };
    }

    private static (Vector3 Axis, PointF Screen, Vector4 Color) MakeAxisPoint(Vector3 axis, PointF center, float size, Vector3 right, Vector3 up, Vector4 color)
    {
        var x = Vector3.Dot(axis, right);
        var y = -Vector3.Dot(axis, up);
        return (axis, new PointF(center.X + x * size, center.Y + y * size), color);
    }

    private void SetCameraForward(Vector3 forward)
    {
        if (forward.LengthSquared < 0.000001f)
            return;

        forward.Normalize();
        _pitch = MathF.Asin(Math.Clamp(forward.Y, -1.0f, 1.0f));
        var cosPitch = MathF.Max(0.0001f, MathF.Cos(_pitch));
        _yaw = MathF.Atan2(forward.X / cosPitch, forward.Z / cosPitch);
    }

    private void DrawAxisWidget()
    {
        if (Width <= 0 || Height <= 0)
            return;

        var points = GetAxisWidgetPoints();
        var center = new PointF(Width - 58, 58);
        var centerNdc = ScreenToNdc(center);
        foreach (var item in points)
        {
            DrawSingleColor(
                PrimitiveType.Lines,
                new[] { centerNdc, ScreenToNdc(item.Screen) },
                item.Color,
                Matrix4.Identity,
                2.4f,
                1.0f,
                false);
            DrawSingleColor(
                PrimitiveType.Points,
                new[] { ScreenToNdc(item.Screen) },
                item.Color,
                Matrix4.Identity,
                1.0f,
                8.0f,
                false);
        }

        var reset = GetAxisResetRectangle();
        var resetLines = new[]
        {
            ScreenToNdc(new PointF(reset.Left, reset.Top)), ScreenToNdc(new PointF(reset.Right, reset.Top)),
            ScreenToNdc(new PointF(reset.Right, reset.Top)), ScreenToNdc(new PointF(reset.Right, reset.Bottom)),
            ScreenToNdc(new PointF(reset.Right, reset.Bottom)), ScreenToNdc(new PointF(reset.Left, reset.Bottom)),
            ScreenToNdc(new PointF(reset.Left, reset.Bottom)), ScreenToNdc(new PointF(reset.Left, reset.Top)),
            ScreenToNdc(new PointF(reset.Left + 9, reset.Top + 12)), ScreenToNdc(new PointF(reset.Right - 9, reset.Top + 12)),
            ScreenToNdc(new PointF(reset.Left + 17, reset.Top + 6)), ScreenToNdc(new PointF(reset.Left + 9, reset.Top + 12)),
            ScreenToNdc(new PointF(reset.Left + 17, reset.Bottom - 6)), ScreenToNdc(new PointF(reset.Left + 9, reset.Top + 12))
        };
        DrawSingleColor(PrimitiveType.Lines, resetLines, new Vector4(0.92f, 0.92f, 0.92f, 1.0f), Matrix4.Identity, 1.6f, 1.0f, false);
    }

    private Vector3 ScreenToNdc(PointF point)
    {
        return new Vector3(
            (point.X / Math.Max(1.0f, Width)) * 2.0f - 1.0f,
            1.0f - (point.Y / Math.Max(1.0f, Height)) * 2.0f,
            0.0f);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_renderFailed)
        {
            DrawFallbackMessage(e.Graphics, "OpenGL viewport failed to initialize.", _renderError);
            return;
        }

        if (!_glReady)
        {
            base.OnPaint(e);
            return;
        }

        try
        {
            MakeCurrent();
            GL.Viewport(0, 0, Math.Max(1, Width), Math.Max(1, Height));
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            var mvp = GetMvp();
            DrawGridAndAxes(mvp);
            DrawVisualModel(mvp);
            if (_physicsOverlayVisible)
            {
                DrawColliders(mvp);
                DrawRagdollConstraints(mvp);

                if (_data != null && (_data.Particles.Count > 0 || _data.Bones.Count > 0))
                {
                    DrawTriangles(mvp);
                    if (BoneOverlayVisible) DrawSkeleton(mvp);
                    DrawLinks(mvp);
                    DrawParticles(mvp);
                    DrawTransformAxis(mvp);
                }
            }

            DrawSelectionRectangle();
            DrawAxisWidget();
            SwapBuffers();
        }
        catch (Exception ex)
        {
            _renderFailed = true;
            _renderError = ex.Message;
            DrawFallbackMessage(e.Graphics, "OpenGL viewport render failed.", _renderError);
        }
    }

    private void ResetCameraToData()
    {
        if (_data == null && _visualModel == null)
        {
            _baseCenter = Vector3.Zero;
            _viewRoot = Vector3.Zero;
            _target = Vector3.Zero;
            _yaw = DefaultYaw;
            _pitch = DefaultPitch;
            _sceneRadius = 0.5f;
            _orthoHeight = 1.0f;
            return;
        }

        UpdateSceneBounds();
        _target = _viewRoot;
        _yaw = DefaultYaw;
        _pitch = DefaultPitch;
        _orthoHeight = Math.Max(0.2f, _sceneRadius * 2.4f);
    }

    private void UpdateSceneBounds(bool updateGrid = true, bool adjustCameraForGrowth = true)
    {
        var previousRadius = _sceneRadius;
        var points = new List<Vector3>();
        if (_data != null)
        {
            points.AddRange(_data.Particles.Select(p => ToGl(p.Position)));
            points.AddRange(_data.Bones.Select(b => ToGl(b.Position)));
            points.AddRange(_data.Colliders.Select(c => ToGl(c.Start)));
            points.AddRange(_data.Colliders.Select(c => ToGl(c.End)));
        }

        if (_visualModel != null && _visualModelSettings.Visible)
        {
            var modelMatrix = GetVisualModelMatrix();
            foreach (var corner in GetBoundsCorners(_visualModel.BoundsMinimum, _visualModel.BoundsMaximum))
                points.Add(Vector3.TransformPosition(corner, modelMatrix));
        }

        points = points.Where(IsFinite).ToList();
        if (points.Count == 0)
            return;

        _baseCenter = new Vector3(points.Average(p => p.X), points.Average(p => p.Y), points.Average(p => p.Z));
        // The viewport has its own fixed world origin. It must not follow a
        // skeleton's Root bone, otherwise moving that bone drags the grid and
        // axes along with the armature.
        _viewRoot = Vector3.Zero;
        _sceneRadius = Math.Max(0.05f, points.Max(p => (p - _viewRoot).Length));
        if (updateGrid)
        {
            _gridStep = FixedGridStep(_sceneRadius) * 4.0f;
            _gridExtent = Math.Max(_sceneRadius * 3.5f, _gridStep * 20.0f);
        }

        // A large manual coordinate edit should remain visible instead of
        // leaving the camera framed around the old, much smaller scene.
        if (adjustCameraForGrowth && _sceneRadius > previousRadius * 1.5f)
            _orthoHeight = Math.Max(_orthoHeight, _sceneRadius * 2.4f);
    }

    private void PickAt(Point location)
    {
        if (_data == null || Width <= 0 || Height <= 0)
            return;

        var mvp = GetMvp();
        var bestDistance = 14.0f * 14.0f;
        PreviewPickKind? bestKind = null;
        var bestIndex = -1;

        if (PickKind == PreviewPickKind.Particle)
        {
            foreach (var particle in _data.Particles)
                ConsiderPoint(PreviewPickKind.Particle, particle.Index, ToGl(particle.Position), location, mvp, ref bestDistance, ref bestKind, ref bestIndex);
        }
        else if (PickKind == PreviewPickKind.Bone)
        {
            foreach (var bone in _data.Bones.Where(bone => bone.IsSelectable))
                ConsiderPoint(PreviewPickKind.Bone, GetBoneEditorIndex(bone), ToGl(bone.Position), location, mvp, ref bestDistance, ref bestKind, ref bestIndex);
        }
        else if (PickKind == PreviewPickKind.Collider)
        {
            foreach (var collider in _data.Colliders)
                ConsiderSegment(PreviewPickKind.Collider, collider.Index, ToGl(collider.Start), ToGl(collider.End), location, mvp, ref bestDistance, ref bestKind, ref bestIndex);
        }

        var selectionOperation = GetParticleSelectionOperation(rectangleSelection: false);
        if (bestKind.HasValue)
            ItemPicked?.Invoke(this, new PreviewPickEventArgs(bestKind.Value, bestIndex, selectionOperation));
        else
            ItemPicked?.Invoke(this, new PreviewPickEventArgs(PickKind, -1, selectionOperation));
    }

    private bool TryPickParticleAt(Point location, out int particleIndex)
    {
        particleIndex = -1;
        if (_data == null || Width <= 0 || Height <= 0)
            return false;

        var mvp = GetMvp();
        var bestDistance = 14.0f * 14.0f;
        foreach (var particle in _data.Particles)
        {
            if (!TryProject(ToGl(particle.Position), mvp, out var screen))
                continue;

            var distance = DistanceSquared(screen, location);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            particleIndex = particle.Index;
        }

        return particleIndex >= 0;
    }

    private void SelectParticlesInRectangle(Rectangle rectangle)
    {
        if (PickKind == PreviewPickKind.Collider)
        {
            var colliders = new HashSet<int>();
            if (_data != null && rectangle.Width > 2 && rectangle.Height > 2)
            {
                var matrix = GetMvp();
                foreach (var collider in _data.Colliders)
                    if (TryProject(ToGl(collider.Start), matrix, out var start) &&
                        TryProject(ToGl(collider.End), matrix, out var end) &&
                        SegmentIntersectsRectangle(start, end, rectangle))
                        colliders.Add(collider.Index);
            }
            CollidersSelected?.Invoke(this, new ColliderSelectionEventArgs(colliders.ToArray(), GetParticleSelectionOperation(rectangleSelection: true)));
            return;
        }
        if (PickKind != PreviewPickKind.Particle)
            return;
        if (_data == null || rectangle.Width <= 2 || rectangle.Height <= 2)
        {
            ParticlesSelected?.Invoke(this, new ParticleSelectionEventArgs(Array.Empty<int>(), GetParticleSelectionOperation(rectangleSelection: true)));
            return;
        }

        var mvp = GetMvp();
        var selected = new List<int>();
        foreach (var particle in _data.Particles)
        {
            if (TryProject(ToGl(particle.Position), mvp, out var screen) && rectangle.Contains(Point.Round(screen)))
                selected.Add(particle.Index);
        }

        ParticlesSelected?.Invoke(this, new ParticleSelectionEventArgs(selected, GetParticleSelectionOperation(rectangleSelection: true)));
    }

    private static bool SegmentIntersectsRectangle(PointF start, PointF end, Rectangle rectangle)
    {
        // Clip the projected centerline, including capsules whose endpoints lie
        // outside the box. A sphere's zero-length centerline works identically.
        var minimum = 0f;
        var maximum = 1f;
        bool Clip(float origin, float delta, float low, float high)
        {
            if (MathF.Abs(delta) < 0.000001f) return origin >= low && origin <= high;
            var a = (low - origin) / delta;
            var b = (high - origin) / delta;
            minimum = Math.Max(minimum, Math.Min(a, b));
            maximum = Math.Min(maximum, Math.Max(a, b));
            return minimum <= maximum;
        }
        return Clip(start.X, end.X - start.X, rectangle.Left, rectangle.Right) &&
            Clip(start.Y, end.Y - start.Y, rectangle.Top, rectangle.Bottom);
    }

    private ParticleSelectionOperation GetParticleSelectionOperation(bool rectangleSelection)
    {
        if ((ModifierKeys & Keys.Control) == Keys.Control)
            return ParticleSelectionOperation.Remove;
        if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            return rectangleSelection ? ParticleSelectionOperation.Add : ParticleSelectionOperation.Toggle;
        return ParticleSelectionOperation.Replace;
    }

    private void ConsiderPoint(
        PreviewPickKind kind,
        int index,
        Vector3 world,
        Point mouse,
        Matrix4 mvp,
        ref float bestDistance,
        ref PreviewPickKind? bestKind,
        ref int bestIndex)
    {
        if (!TryProject(world, mvp, out var screen))
            return;

        var distance = DistanceSquared(screen, mouse);
        if (distance >= bestDistance)
            return;

        bestDistance = distance;
        bestKind = kind;
        bestIndex = index;
    }

    private void ConsiderSegment(
        PreviewPickKind kind,
        int index,
        Vector3 start,
        Vector3 end,
        Point mouse,
        Matrix4 mvp,
        ref float bestDistance,
        ref PreviewPickKind? bestKind,
        ref int bestIndex)
    {
        if (!TryProject(start, mvp, out var a) || !TryProject(end, mvp, out var b))
            return;

        var distance = DistanceToSegmentSquared(mouse, a, b);
        if (distance >= bestDistance)
            return;

        bestDistance = distance;
        bestKind = kind;
        bestIndex = index;
    }

    private bool TryProject(Vector3 world, Matrix4 mvp, out PointF screen)
    {
        var clip = Vector4.TransformRow(new Vector4(world, 1.0f), mvp);
        if (MathF.Abs(clip.W) < 0.000001f)
        {
            screen = PointF.Empty;
            return false;
        }

        var ndcX = clip.X / clip.W;
        var ndcY = clip.Y / clip.W;
        screen = new PointF(
            (ndcX * 0.5f + 0.5f) * Width,
            (1.0f - (ndcY * 0.5f + 0.5f)) * Height);
        return ndcX >= -1.2f && ndcX <= 1.2f && ndcY >= -1.2f && ndcY <= 1.2f;
    }

    private void DrawSelectionRectangle()
    {
        if (!_boxSelecting || _selectionRectangle.Width <= 2 || _selectionRectangle.Height <= 2)
            return;

        var left = (_selectionRectangle.Left / Math.Max(1.0f, Width)) * 2.0f - 1.0f;
        var right = (_selectionRectangle.Right / Math.Max(1.0f, Width)) * 2.0f - 1.0f;
        var top = 1.0f - (_selectionRectangle.Top / Math.Max(1.0f, Height)) * 2.0f;
        var bottom = 1.0f - (_selectionRectangle.Bottom / Math.Max(1.0f, Height)) * 2.0f;
        var vertices = new[]
        {
            new Vector3(left, top, 0.0f),
            new Vector3(right, top, 0.0f),
            new Vector3(right, top, 0.0f),
            new Vector3(right, bottom, 0.0f),
            new Vector3(right, bottom, 0.0f),
            new Vector3(left, bottom, 0.0f),
            new Vector3(left, bottom, 0.0f),
            new Vector3(left, top, 0.0f)
        };
        var identity = Matrix4.Identity;
        DrawSingleColor(PrimitiveType.Lines, vertices, new Vector4(1.0f, 1.0f, 1.0f, 0.92f), identity, 1.5f, 1.0f, false);
    }

    private static float DistanceSquared(PointF a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static float DistanceToSegmentSquared(Point p, PointF a, PointF b)
    {
        var vx = b.X - a.X;
        var vy = b.Y - a.Y;
        var wx = p.X - a.X;
        var wy = p.Y - a.Y;
        var lengthSq = vx * vx + vy * vy;
        var t = lengthSq < 0.000001f ? 0.0f : Math.Clamp((wx * vx + wy * vy) / lengthSq, 0.0f, 1.0f);
        var x = a.X + vx * t;
        var y = a.Y + vy * t;
        var dx = p.X - x;
        var dy = p.Y - y;
        return dx * dx + dy * dy;
    }

    private static Rectangle MakeRectangle(Point a, Point b)
    {
        return new Rectangle(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Abs(a.X - b.X),
            Math.Abs(a.Y - b.Y));
    }

    private SysVec3? ToSysAxis(Vector3? axis)
    {
        return axis.HasValue ? ToSysVector(axis.Value) : null;
    }

    private SysVec3 ToSysVector(Vector3 vector) => new(ReflectSceneX ? -vector.X : vector.X, vector.Y, vector.Z);

    private Matrix4 GetMvp()
    {
        var aspect = Math.Max(1.0f, Width) / Math.Max(1.0f, Height);
        var eyeDistance = Math.Max(3.0f, _sceneRadius * 5.0f);
        var clipRange = Math.Max(100.0f, eyeDistance + _sceneRadius * 4.0f + 10.0f);
        var projection = Matrix4.CreateOrthographic(_orthoHeight * aspect, _orthoHeight, -clipRange, clipRange);
        var (forward, _, up) = GetCameraBasis();
        var eye = _target - forward * eyeDistance;
        var view = Matrix4.LookAt(eye, _target, up);
        return view * projection;
    }

    private (Vector3 Forward, Vector3 Right, Vector3 Up) GetCameraBasis()
    {
        var forward = new Vector3(
            MathF.Sin(_yaw) * MathF.Cos(_pitch),
            MathF.Sin(_pitch),
            MathF.Cos(_yaw) * MathF.Cos(_pitch));
        forward.Normalize();

        var right = Vector3.Cross(forward, Vector3.UnitY);
        if (right.LengthSquared < 0.000001f)
            right = Vector3.UnitX;
        else
            right.Normalize();

        var up = Vector3.Cross(right, forward);
        up.Normalize();
        return (forward, right, up);
    }

    private void DrawGridAndAxes(Matrix4 mvp)
    {
        // The grid and world axes share the scene depth buffer. A model may
        // cover lines behind it, while lines physically in front remain visible.
        GL.DepthMask(true);
        var gridVertices = new List<Vector3>();
        var gridY = _viewRoot.Y;
        // Limit line count for performance, but expand the spacing to preserve
        // the requested world extent. Previously the line-count cap silently
        // shrank the grid on large skeletons.
        var requestedHalf = Math.Max(1, (int)MathF.Ceiling(_gridExtent / _gridStep));
        var half = Math.Clamp(requestedHalf, 16, 32);
        var step = Math.Max(_gridStep, _gridExtent / half);
        var centerX = _viewRoot.X;
        var centerZ = _viewRoot.Z;

        for (var i = -half; i <= half; i++)
        {
            var offset = i * step;
            AddLine(gridVertices, new Vector3(centerX - half * step, gridY, centerZ + offset), new Vector3(centerX + half * step, gridY, centerZ + offset));
            AddLine(gridVertices, new Vector3(centerX + offset, gridY, centerZ - half * step), new Vector3(centerX + offset, gridY, centerZ + half * step));
        }
        DrawSingleColor(PrimitiveType.Lines, gridVertices, new Vector4(0.62f, 0.62f, 0.62f, 0.50f), mvp, 1.0f, 1.0f, true);

        var axisExtent = half * step;
        DrawSingleColor(
            PrimitiveType.Lines,
            new[] { new Vector3(centerX - axisExtent, gridY, centerZ), new Vector3(centerX + axisExtent, gridY, centerZ) },
            new Vector4(0.90f, 0.18f, 0.18f, 1.0f),
            mvp,
            2.2f,
            1.0f,
            true);
        DrawSingleColor(
            PrimitiveType.Lines,
            new[] { new Vector3(centerX, gridY, centerZ - axisExtent), new Vector3(centerX, gridY, centerZ + axisExtent) },
            new Vector4(0.20f, 0.34f, 0.95f, 1.0f),
            mvp,
            2.2f,
            1.0f,
            true);
        DrawSingleColor(
            PrimitiveType.Lines,
            new[] { new Vector3(centerX, gridY - axisExtent * 0.55f, centerZ), new Vector3(centerX, gridY + axisExtent * 0.55f, centerZ) },
            new Vector4(0.16f, 0.78f, 0.30f, 1.0f),
            mvp,
            2.2f,
            1.0f,
            true);
    }

    private void DrawSkeleton(Matrix4 mvp)
    {
        if (_data == null || _data.Bones.Count == 0)
            return;

        var referenceLines = new List<Vector3>();
        var lines = new List<Vector3>();
        var selectedLines = new List<Vector3>();
        var bonesByIndex = _data.Bones.ToDictionary(bone => bone.Index);
        var jointSize = GetBoneJointSize();
        foreach (var bone in _data.Bones)
        {
            var point = ToGl(bone.Position);
            var isSelected = bone.IsSelectable && GetBoneEditorIndex(bone) == SelectedBoneIndex;
            var jointLines = isSelected
                ? selectedLines
                : bone.IsReference ? referenceLines : lines;
            AddToolboxJoint(
                jointLines,
                point,
                ToGl(bone.AxisX),
                ToGl(bone.AxisY),
                ToGl(bone.AxisZ),
                jointSize);
        }

        foreach (var bone in _data.Bones)
        {
            if (bone.ParentIndex < 0 || !bonesByIndex.TryGetValue(bone.ParentIndex, out var parent))
                continue;

            var isSelected = bone.IsSelectable && GetBoneEditorIndex(bone) == SelectedBoneIndex ||
                             parent.IsSelectable && GetBoneEditorIndex(parent) == SelectedBoneIndex;
            var linkLines = isSelected
                ? selectedLines
                : bone.IsReference || parent.IsReference ? referenceLines : lines;
            AddToolboxBoneLink(
                linkLines,
                ToGl(parent.Position),
                ToGl(parent.AxisX),
                ToGl(parent.AxisZ),
                ToGl(bone.Position),
                jointSize);
        }

        if (referenceLines.Count > 0)
            DrawSingleColor(PrimitiveType.Lines, referenceLines, new Vector4(0.95f, 0.30f, 0.07f, 1.0f), mvp, 1.6f, 1.0f, false);
        if (lines.Count > 0)
            DrawSingleColor(PrimitiveType.Lines, lines, new Vector4(1.0f, 0.92f, 0.0f, 1.0f), mvp, 2.0f, 1.0f, false);
        if (selectedLines.Count > 0)
            DrawSingleColor(PrimitiveType.Lines, selectedLines, new Vector4(1.0f, 1.0f, 1.0f, 1.0f), mvp, 3.0f, 1.0f, false);
    }

    private float GetBoneJointSize() => Math.Clamp(_sceneRadius * 0.008f, 0.0025f, 0.025f);

    private static void AddToolboxBoneLink(
        List<Vector3> lines,
        Vector3 parent,
        Vector3 parentAxisX,
        Vector3 parentAxisZ,
        Vector3 child,
        float size)
    {
        var ring = BuildJointRing(parent, parentAxisX, parentAxisZ, size);
        foreach (var tip in ring)
            AddLine(lines, tip, child);
    }

    private static void AddToolboxJoint(
        List<Vector3> lines,
        Vector3 point,
        Vector3 axisX,
        Vector3 axisY,
        Vector3 axisZ,
        float size)
    {
        var ring = BuildJointRing(point, axisX, axisZ, size);
        for (var i = 0; i < ring.Length; i++)
            AddLine(lines, ring[i], ring[(i + 1) % ring.Length]);

        var top = point + NormalizeOrFallback(axisY, Vector3.UnitY) * size;
        var bottom = point - NormalizeOrFallback(axisY, Vector3.UnitY) * size;
        foreach (var tip in ring)
        {
            AddLine(lines, tip, top);
            AddLine(lines, tip, bottom);
        }
    }

    private static Vector3[] BuildJointRing(Vector3 center, Vector3 axisX, Vector3 axisZ, float size)
    {
        var x = NormalizeOrFallback(axisX, Vector3.UnitX) * size;
        var z = NormalizeOrFallback(axisZ, Vector3.UnitZ) * size;
        return new[] { center - z, center + x, center + z, center - x };
    }

    private static Vector3 NormalizeOrFallback(Vector3 vector, Vector3 fallback)
    {
        return vector.LengthSquared < 0.000001f ? fallback : Vector3.Normalize(vector);
    }

    private void DrawTriangles(Matrix4 mvp)
    {
        if (_data == null)
            return;

        var vertices = new List<Vector3>();
        foreach (var triangle in _data.Triangles)
        {
            if (!TryGetParticle(triangle.ParticleA, out var a)
                || !TryGetParticle(triangle.ParticleB, out var b)
                || !TryGetParticle(triangle.ParticleC, out var c))
                continue;

            vertices.Add(ToGl(a.Position));
            vertices.Add(ToGl(b.Position));
            vertices.Add(ToGl(c.Position));
        }

        if (vertices.Count > 0)
            DrawSingleColor(PrimitiveType.Triangles, vertices, new Vector4(0.40f, 0.62f, 0.95f, 0.22f), mvp, 1.0f, 1.0f, true);
    }

    private void DrawColliders(Matrix4 mvp)
    {
        if (_data == null || _data.Colliders.Count == 0)
            return;

        var lines = new List<Vector3>();
        var selectedLines = new List<Vector3>();
        foreach (var collider in _data.Colliders)
        {
            var target = collider.Index == SelectedColliderIndex || _selectedColliderIndices.Contains(collider.Index) ? selectedLines : lines;
            switch (collider.Kind)
            {
                case ColliderPreviewKind.Sphere:
                    AddSphereGuide(target, ToGl(collider.Start), collider.Radius);
                    break;
                case ColliderPreviewKind.TaperedCapsule:
                    AddTaperedCapsuleGuide(target, ToGl(collider.Start), ToGl(collider.End), collider.Radius, collider.EndRadius);
                    break;
                case ColliderPreviewKind.Plane:
                    AddPlaneGuide(target, ToGl(collider.Start), ToGl(collider.PlaneNormal), Math.Max(_sceneRadius * 0.18f, _gridStep * 2.0f));
                    break;
                case ColliderPreviewKind.Point:
                    AddSphereGuide(target, ToGl(collider.Start), Math.Max(_sceneRadius * 0.015f, _gridStep * 0.15f));
                    break;
                default:
                    AddCapsuleGuide(target, ToGl(collider.Start), ToGl(collider.End), collider.Radius);
                    break;
            }
        }

        if (lines.Count > 0)
            DrawSingleColor(PrimitiveType.Lines, lines, new Vector4(0.0f, 0.72f, 0.90f, 0.78f), mvp, 1.4f, 1.0f, false);
        if (selectedLines.Count > 0)
            DrawSingleColor(PrimitiveType.Lines, selectedLines, new Vector4(1.0f, 1.0f, 1.0f, 1.0f), mvp, 4.0f, 1.0f, false);
    }

    private static void AddCapsuleGuide(List<Vector3> lines, Vector3 start, Vector3 end, float radius)
    {
        AddLine(lines, start, end);
        var direction = end - start;
        if (direction.LengthSquared < 0.000001f)
            direction = Vector3.UnitY;
        else
            direction.Normalize();

        var side = Vector3.Cross(direction, Vector3.UnitY);
        if (side.LengthSquared < 0.000001f)
            side = Vector3.Cross(direction, Vector3.UnitX);
        side.Normalize();
        var up = Vector3.Cross(side, direction);
        up.Normalize();
        var r = Math.Max(radius, 0.005f);

        AddCircle(lines, start, side, up, r);
        AddCircle(lines, end, side, up, r);
        AddLine(lines, start + side * r, end + side * r);
        AddLine(lines, start - side * r, end - side * r);
        AddLine(lines, start + up * r, end + up * r);
        AddLine(lines, start - up * r, end - up * r);
    }

    private static void AddTaperedCapsuleGuide(List<Vector3> lines, Vector3 start, Vector3 end, float startRadius, float endRadius)
    {
        var direction = end - start;
        if (direction.LengthSquared < 0.000001f)
        {
            AddSphereGuide(lines, start, Math.Max(startRadius, endRadius));
            return;
        }

        direction.Normalize();
        var side = Vector3.Cross(direction, Vector3.UnitY);
        if (side.LengthSquared < 0.000001f)
            side = Vector3.Cross(direction, Vector3.UnitX);
        side.Normalize();
        var up = Vector3.Normalize(Vector3.Cross(side, direction));
        var a = Math.Max(startRadius, 0.005f);
        var b = Math.Max(endRadius, 0.005f);
        AddCircle(lines, start, side, up, a);
        AddCircle(lines, end, side, up, b);
        AddLine(lines, start + side * a, end + side * b);
        AddLine(lines, start - side * a, end - side * b);
        AddLine(lines, start + up * a, end + up * b);
        AddLine(lines, start - up * a, end - up * b);
    }

    private static void AddSphereGuide(List<Vector3> lines, Vector3 center, float radius)
    {
        var r = Math.Max(radius, 0.005f);
        AddCircle(lines, center, Vector3.UnitX, Vector3.UnitY, r);
        AddCircle(lines, center, Vector3.UnitX, Vector3.UnitZ, r);
        AddCircle(lines, center, Vector3.UnitY, Vector3.UnitZ, r);
    }

    private static void AddPlaneGuide(List<Vector3> lines, Vector3 center, Vector3 normal, float size)
    {
        if (normal.LengthSquared < 0.000001f)
            normal = Vector3.UnitY;
        else
            normal.Normalize();
        var side = Vector3.Cross(normal, Vector3.UnitY);
        if (side.LengthSquared < 0.000001f)
            side = Vector3.Cross(normal, Vector3.UnitX);
        side.Normalize();
        var up = Vector3.Normalize(Vector3.Cross(side, normal));
        var a = center - side * size - up * size;
        var b = center + side * size - up * size;
        var c = center + side * size + up * size;
        var d = center - side * size + up * size;
        AddLine(lines, a, b);
        AddLine(lines, b, c);
        AddLine(lines, c, d);
        AddLine(lines, d, a);
        AddLine(lines, center, center + normal * size * 0.6f);
    }

    private static void AddCircle(List<Vector3> lines, Vector3 center, Vector3 side, Vector3 up, float radius)
    {
        const int steps = 16;
        for (var i = 0; i < steps; i++)
        {
            var a = MathHelper.TwoPi * i / steps;
            var b = MathHelper.TwoPi * (i + 1) / steps;
            AddLine(
                lines,
                center + side * (MathF.Cos(a) * radius) + up * (MathF.Sin(a) * radius),
                center + side * (MathF.Cos(b) * radius) + up * (MathF.Sin(b) * radius));
        }
    }

    private void DrawRagdollConstraints(Matrix4 mvp)
    {
        if (_data == null || _data.RagdollConstraints.Count == 0)
            return;

        var colliderCenters = _data.Colliders.ToDictionary(collider => collider.Index, GetColliderCenter);
        var lines = new List<Vector3>();
        foreach (var constraint in _data.RagdollConstraints)
        {
            if (!colliderCenters.TryGetValue(constraint.BodyA, out var a) ||
                !colliderCenters.TryGetValue(constraint.BodyB, out var b))
            {
                continue;
            }

            AddLine(lines, ToGl(a), ToGl(b));
        }

        if (lines.Count > 0)
            DrawSingleColor(PrimitiveType.Lines, lines, new Vector4(1.0f, 0.42f, 0.08f, 1.0f), mvp, 2.6f, 1.0f, false);
    }

    private static SysVec3 GetColliderCenter(ColliderPreviewShape collider)
    {
        return collider.Kind == ColliderPreviewKind.Plane
            ? collider.Start
            : (collider.Start + collider.End) * 0.5f;
    }
    private void DrawLinks(Matrix4 mvp)
    {
        if (_data == null)
            return;

        var normal = new List<Vector3>();
        var selected = new List<Vector3>();
        foreach (var link in _data.Links)
        {
            if (!TryGetParticle(link.ParticleA, out var a) || !TryGetParticle(link.ParticleB, out var b))
                continue;

            var list = link.ParticleA == SelectedParticleIndex
                || link.ParticleB == SelectedParticleIndex
                || _selectedParticleIndices.Contains(link.ParticleA)
                || _selectedParticleIndices.Contains(link.ParticleB)
                ? selected
                : normal;
            AddLine(list, ToGl(a.Position), ToGl(b.Position));
        }

        if (normal.Count > 0)
            DrawSingleColor(PrimitiveType.Lines, normal, new Vector4(0.45f, 0.45f, 0.45f, 1.0f), mvp, 1.8f, 1.0f, false);
        if (selected.Count > 0)
            DrawSingleColor(PrimitiveType.Lines, selected, new Vector4(1.0f, 0.74f, 0.18f, 1.0f), mvp, 3.0f, 1.0f, false);
    }

    private void DrawTransformAxis(Matrix4 mvp)
    {
        if (_data == null || _activeTransformMode == ParticleTransformMode.None || !_axisConstraint.HasValue)
            return;

        var selected = GetTransformSelectionPositions();
        if (selected.Count == 0)
            return;

        var center = new Vector3(
            selected.Average(p => p.X),
            selected.Average(p => p.Y),
            selected.Average(p => p.Z));
        var axis = _axisConstraint.Value;
        if (axis.LengthSquared < 0.000001f)
            axis = Vector3.UnitX;
        else
            axis.Normalize();

        var length = Math.Max(_sceneRadius * 0.45f, _gridStep * 3.0f);
        var color = _axisConstraint switch
        {
            { } a when MathF.Abs(Vector3.Dot(a, Vector3.UnitX)) > 0.9f => new Vector4(1.0f, 0.18f, 0.18f, 1.0f),
            { } a when MathF.Abs(Vector3.Dot(a, Vector3.UnitY)) > 0.9f => new Vector4(0.25f, 0.90f, 0.35f, 1.0f),
            { } a when MathF.Abs(Vector3.Dot(a, Vector3.UnitZ)) > 0.9f => new Vector4(0.25f, 0.42f, 1.0f, 1.0f),
            _ => new Vector4(1.0f, 0.86f, 0.18f, 1.0f)
        };

        DrawSingleColor(
            PrimitiveType.Lines,
            new[] { center - axis * length, center + axis * length },
            color,
            mvp,
            3.0f,
            1.0f,
            false);
    }

    private void DrawParticles(Matrix4 mvp)
    {
        if (_data == null)
            return;

        var fixedParticles = new List<Vector3>();
        var dynamicParticles = new List<Vector3>();
        var selectedParticles = new List<Vector3>();

        foreach (var particle in _data.Particles)
        {
            if (_selectedParticleIndices.Contains(particle.Index) || particle.Index == SelectedParticleIndex)
                selectedParticles.Add(ToGl(particle.Position));
            else if (particle.Fixed)
                fixedParticles.Add(ToGl(particle.Position));
            else
                dynamicParticles.Add(ToGl(particle.Position));
        }

        if (dynamicParticles.Count > 0)
            DrawSingleColor(PrimitiveType.Points, dynamicParticles, new Vector4(0.20f, 0.48f, 0.92f, 1.0f), mvp, 1.0f, 9.0f, false);
        if (fixedParticles.Count > 0)
            DrawSingleColor(PrimitiveType.Points, fixedParticles, new Vector4(0.88f, 0.18f, 0.18f, 1.0f), mvp, 1.0f, 10.0f, false);
        if (selectedParticles.Count > 0)
            DrawSingleColor(PrimitiveType.Points, selectedParticles, new Vector4(1.0f, 0.80f, 0.18f, 1.0f), mvp, 1.0f, 13.0f, false);
    }

    private void DrawVisualModel(Matrix4 viewProjection)
    {
        if (_visualModel == null || !_visualModelSettings.Visible)
            return;

        EnsureVisualModelGpu();
        if (_visualModelGpuMeshes.Count == 0)
            return;

        var model = GetVisualModelMatrix();
        var mvp = model * viewProjection;
        var opacity = Math.Clamp(_visualModelSettings.Opacity, 0.05f, 1.0f);
        GL.Enable(EnableCap.DepthTest);
        GL.DepthMask(true);
        GL.DepthFunc(DepthFunction.Less);
        GL.Disable(EnableCap.Blend);
        GL.CullFace(CullFaceMode.Back);
        GL.PolygonMode(MaterialFace.FrontAndBack, _visualModelSettings.Wireframe ? PolygonMode.Line : PolygonMode.Fill);
        GL.UseProgram(_visualModelProgram);
        GL.UniformMatrix4(_visualModelMvpLocation, false, ref mvp);
        GL.UniformMatrix4(_visualModelModelLocation, false, ref model);
        GL.Uniform1(_visualModelOpacityLocation, opacity);
        GL.Uniform1(_visualModelTextureLocation, 0);
        GL.Uniform1(_visualModelSpecularTextureLocation, 1);
        GL.Uniform1(_visualModelNormalTextureLocation, 2);
        GL.Uniform1(_visualModelAmbientOcclusionTextureLocation, 3);
        GL.Uniform1(_visualModelAuxiliaryTextureLocation, 4);
        GL.Uniform1(_visualModelEyeGlintTextureLocation, 5);
        var (forward, right, up) = GetCameraBasis();
        var worldKey = Vector3.Normalize(new Vector3(0.35f, 0.80f, 0.45f));
        var studioKey = Vector3.Normalize(-forward * 0.62f + right * 0.34f + up * 0.70f);
        var keyLight = Vector3.Normalize(worldKey * 0.28f + studioKey * 0.72f);
        var fillLight = Vector3.Normalize(-forward * 0.45f - right * 0.72f + up * 0.22f);
        GL.Uniform3(_visualModelKeyLightLocation, keyLight);
        GL.Uniform3(_visualModelFillLightLocation, fillLight);
        // The viewport projection is orthographic, so every fragment shares
        // one surface-to-camera direction. Using a perspective-style vector
        // from a synthetic camera position bends facing and rim masks across
        // large meshes.
        GL.Uniform3(_visualModelViewDirectionLocation, -forward);

        // Populate depth for the complete model first. The color pass can then
        // blend the model as one visible shell instead of exposing later meshes
        // through hair, clothing, and other outer surfaces.
        GL.ColorMask(false, false, false, false);
        DrawVisualModelMeshes();

        GL.ColorMask(true, true, true, true);
        GL.DepthFunc(DepthFunction.Equal);
        GL.DepthMask(false);
        if (opacity < 0.999f || _visualModelGpuMeshes.Any(mesh => mesh.AlphaMode == VisualModelAlphaMode.Blend))
        {
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        }
        DrawVisualModelMeshes();

        GL.BindVertexArray(0);
        GL.ActiveTexture(TextureUnit.Texture5);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture4);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture3);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.UseProgram(0);
        GL.Disable(EnableCap.CullFace);
        GL.FrontFace(FrontFaceDirection.Ccw);
        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
        GL.DepthMask(true);
        GL.DepthFunc(DepthFunction.Less);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    private void DrawVisualModelMeshes()
    {
        foreach (var mesh in _visualModelGpuMeshes)
        {
            ApplyVisualModelCulling(mesh.CullingMode);
            GL.Uniform4(_visualModelColorLocation, mesh.BaseColor);
            var showTextures = _visualModelSettings.ShowTextures;
            var material = mesh.Material;
            var useTexture = showTextures && material.UseAlbedo && mesh.Texture != 0;
            var useSpecularTexture = showTextures && material.UseSpm && mesh.SpecularTexture != 0;
            var useNormalTexture = showTextures && material.UseNormal && mesh.NormalTexture != 0;
            var useAmbientOcclusion = showTextures && material.UseAmbientOcclusion && mesh.AmbientOcclusionTexture != 0;
            var useAuxiliary = showTextures && material.UseAuxiliary && mesh.AuxiliaryTexture != 0;
            var useEyeGlint = showTextures && material.UseEyeGlint && mesh.EyeGlintTexture != 0;
            GL.Uniform1(_visualModelUseTextureLocation, useTexture ? 1 : 0);
            GL.Uniform1(_visualModelUseSpecularTextureLocation, useSpecularTexture ? 1 : 0);
            GL.Uniform1(_visualModelUseNormalTextureLocation, useNormalTexture ? 1 : 0);
            GL.Uniform1(_visualModelUseAmbientOcclusionTextureLocation, useAmbientOcclusion ? 1 : 0);
            GL.Uniform1(_visualModelUseAuxiliaryTextureLocation, useAuxiliary ? 1 : 0);
            GL.Uniform1(_visualModelUseEyeGlintTextureLocation, useEyeGlint ? 1 : 0);
            GL.Uniform1(_visualModelAlphaModeLocation, useTexture ? (int)mesh.AlphaMode : (int)VisualModelAlphaMode.Opaque);
            GL.Uniform1(_visualModelShaderModelLocation, (int)material.ShaderModel);
            GL.Uniform1(_visualModelAuxiliaryStrengthLocation, Math.Clamp(material.AuxiliaryStrength, 0.0f, 2.0f));
            GL.Uniform1(_visualModelEyeGlintStrengthLocation, Math.Clamp(material.EyeGlintStrength, 0.0f, 2.0f));
            GL.Uniform1(_visualModelMetalChannelLocation, (int)material.MetalChannel);
            GL.Uniform1(_visualModelSmoothnessChannelLocation, (int)material.SmoothnessChannel);
            GL.Uniform1(_visualModelMetalScaleLocation, Math.Clamp(material.MetalScale, 0.0f, 2.0f));
            GL.Uniform1(_visualModelSmoothnessScaleLocation, Math.Clamp(material.SmoothnessScale, 0.0f, 2.0f));
            GL.Uniform1(_visualModelNormalStrengthLocation, Math.Clamp(material.NormalStrength, 0.0f, 2.0f));
            GL.Uniform1(_visualModelAmbientOcclusionStrengthLocation, Math.Clamp(material.AmbientOcclusionStrength, 0.0f, 1.0f));
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, useTexture ? mesh.Texture : 0);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, useSpecularTexture ? mesh.SpecularTexture : 0);
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, useNormalTexture ? mesh.NormalTexture : 0);
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.BindTexture(TextureTarget.Texture2D, useAmbientOcclusion ? mesh.AmbientOcclusionTexture : 0);
            GL.ActiveTexture(TextureUnit.Texture4);
            GL.BindTexture(TextureTarget.Texture2D, useAuxiliary ? mesh.AuxiliaryTexture : 0);
            GL.ActiveTexture(TextureUnit.Texture5);
            GL.BindTexture(TextureTarget.Texture2D, useEyeGlint ? mesh.EyeGlintTexture : 0);
            GL.BindVertexArray(mesh.VertexArray);
            GL.DrawElements(PrimitiveType.Triangles, mesh.IndexCount, DrawElementsType.UnsignedInt, 0);
        }
    }

    private void ApplyVisualModelCulling(VisualModelCullingMode cullingMode)
    {
        if (cullingMode == VisualModelCullingMode.Off)
        {
            GL.Disable(EnableCap.CullFace);
            return;
        }

        GL.Enable(EnableCap.CullFace);
        var clockwise = cullingMode == VisualModelCullingMode.Clockwise;
        // Reverse winding only when the effective model transform reflects X.
        if (ReflectSceneX != _visualModelSettings.MirrorX)
            clockwise = !clockwise;
        GL.FrontFace(clockwise ? FrontFaceDirection.Cw : FrontFaceDirection.Ccw);
    }
    private void EnsureVisualModelGpu()
    {
        if (!_visualModelGpuDirty || _visualModel == null)
            return;

        ReleaseVisualModelGpu();
        foreach (var source in _visualModel.Meshes)
        {
            var vao = GL.GenVertexArray();
            var vbo = GL.GenBuffer();
            var ebo = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            var gpuVertices = BuildVisualModelGpuVertices(source);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                gpuVertices.Length * sizeof(float),
                gpuVertices,
                BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(
                BufferTarget.ElementArrayBuffer,
                source.Indices.Length * sizeof(uint),
                source.Indices,
                BufferUsageHint.StaticDraw);

            const int stride = 20 * sizeof(float);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, 8 * sizeof(float));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, stride, 10 * sizeof(float));
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(5, 2, VertexAttribPointerType.Float, false, stride, 12 * sizeof(float));
            GL.EnableVertexAttribArray(5);
            GL.VertexAttribPointer(6, 2, VertexAttribPointerType.Float, false, stride, 14 * sizeof(float));
            GL.EnableVertexAttribArray(6);
            GL.VertexAttribPointer(7, 4, VertexAttribPointerType.Float, false, stride, 16 * sizeof(float));
            GL.EnableVertexAttribArray(7);

            var albedo = VisualModelImporter.ResolveAlbedoTexture(source);
            var texture = albedo == null ? 0 : UploadVisualModelTexture(albedo);
            var material = source.Material.Clone();
            if (source.Texture == null && albedo != null) material.UseAlbedo = true;
            var specularTexture = source.SpecularTexture == null ? 0 : UploadVisualModelTexture(source.SpecularTexture);
            var normalTexture = source.NormalTexture == null ? 0 : UploadVisualModelTexture(source.NormalTexture);
            var ambientOcclusionTexture = source.AmbientOcclusionTexture == null
                ? 0
                : UploadVisualModelTexture(source.AmbientOcclusionTexture);
            var auxiliaryTexture = source.AuxiliaryTexture == null
                ? 0
                : UploadVisualModelTexture(source.AuxiliaryTexture);
            var eyeGlintTexture = source.EyeGlintTexture == null
                ? 0
                : UploadVisualModelTexture(source.EyeGlintTexture);
            _visualModelGpuMeshes.Add(new VisualModelGpuMesh
            {
                VertexArray = vao,
                VertexBuffer = vbo,
                IndexBuffer = ebo,
                Texture = texture,
                SpecularTexture = specularTexture,
                NormalTexture = normalTexture,
                AmbientOcclusionTexture = ambientOcclusionTexture,
                AuxiliaryTexture = auxiliaryTexture,
                EyeGlintTexture = eyeGlintTexture,
                AlphaMode = source.Texture?.AlphaMode ?? VisualModelAlphaMode.Opaque,
                Material = material,
                IndexCount = source.Indices.Length,
                BaseColor = new Vector4(
                    source.Material.Tint.X,
                    source.Material.Tint.Y,
                    source.Material.Tint.Z,
                    source.Material.Tint.W),
                CullingMode = source.CullingMode
            });
        }

        GL.BindVertexArray(0);
        _visualModelGpuDirty = false;
    }

    private static float[] BuildVisualModelGpuVertices(VisualModelMesh source)
    {
        const int sourceStride = 8;
        const int targetStride = 20;
        var vertexCount = source.Vertices.Length / sourceStride;
        var result = new float[checked(vertexCount * targetStride)];
        var albedo = ResolveUvCoordinates(source, source.Material.AlbedoUvSet);
        var spm = ResolveUvCoordinates(source, source.Material.SpmUvSet);
        var normal = ResolveUvCoordinates(source, source.Material.NormalUvSet);
        var ao = ResolveUvCoordinates(source, source.Material.AmbientOcclusionUvSet);
        var auxiliary = ResolveUvCoordinates(source, source.Material.AuxiliaryUvSet);

        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            var sourceOffset = vertex * sourceStride;
            var targetOffset = vertex * targetStride;
            Array.Copy(source.Vertices, sourceOffset, result, targetOffset, 6);
            CopyUv(albedo, source.Vertices, vertex, result, targetOffset + 6);
            CopyUv(spm, source.Vertices, vertex, result, targetOffset + 8);
            CopyUv(normal, source.Vertices, vertex, result, targetOffset + 10);
            CopyUv(ao, source.Vertices, vertex, result, targetOffset + 12);
            CopyUv(auxiliary, source.Vertices, vertex, result, targetOffset + 14);
        }

        WriteVisualModelTangents(source, normal, result, targetStride);

        return result;
    }

    private static void WriteVisualModelTangents(
        VisualModelMesh source,
        IReadOnlyList<float>? normalUvs,
        float[] target,
        int targetStride)
    {
        const int sourceStride = 8;
        var vertexCount = source.Vertices.Length / sourceStride;
        var tangentGroups = new Dictionary<
            (Vector3 Position, Vector3 Normal, Vector2 Uv, int Orientation),
            (Vector3 Tangent, Vector3 Bitangent)>();
        var vertexOrientations = new int[vertexCount];
        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            var normal = ReadVisualModelVector(source.Vertices, vertex, 3);
            var reference = Math.Abs(normal.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;
            var tangent = NormalizeOrFallback(Vector3.Cross(reference, normal), Vector3.UnitX);
            WriteVisualModelTangent(target, vertex, targetStride, tangent, 1.0f);
        }

        for (var triangle = 0; triangle + 2 < source.Indices.Length; triangle += 3)
        {
            var first = checked((int)source.Indices[triangle]);
            var second = checked((int)source.Indices[triangle + 1]);
            var third = checked((int)source.Indices[triangle + 2]);
            if ((uint)first >= vertexCount || (uint)second >= vertexCount || (uint)third >= vertexCount)
                continue;

            var edge1 = ReadVisualModelVector(source.Vertices, second, 0)
                - ReadVisualModelVector(source.Vertices, first, 0);
            var edge2 = ReadVisualModelVector(source.Vertices, third, 0)
                - ReadVisualModelVector(source.Vertices, first, 0);
            var uv0 = ReadVisualModelUv(normalUvs, source.Vertices, first);
            var uv1 = ReadVisualModelUv(normalUvs, source.Vertices, second) - uv0;
            var uv2 = ReadVisualModelUv(normalUvs, source.Vertices, third) - uv0;
            var determinant = uv1.X * uv2.Y - uv1.Y * uv2.X;
            if (Math.Abs(determinant) < 0.00000001f)
                continue;

            var inverseDeterminant = 1.0f / determinant;
            var triangleTangent = (edge1 * uv2.Y - edge2 * uv1.Y) * inverseDeterminant;
            var triangleBitangent = (edge2 * uv1.X - edge1 * uv2.X) * inverseDeterminant;
            if (triangleTangent.LengthSquared < 0.00000001f ||
                triangleBitangent.LengthSquared < 0.00000001f)
                continue;

            triangleTangent.Normalize();
            triangleBitangent.Normalize();
            var orientation = determinant < 0.0f ? -1 : 1;
            var triangleVertices = new[] { first, second, third };
            for (var corner = 0; corner < triangleVertices.Length; corner++)
            {
                var vertex = triangleVertices[corner];
                var normal = NormalizeOrFallback(
                    ReadVisualModelVector(source.Vertices, vertex, 3),
                    Vector3.UnitY);
                var position = ReadVisualModelVector(source.Vertices, vertex, 0);
                var uv = ReadVisualModelUv(normalUvs, source.Vertices, vertex);
                var previous = triangleVertices[(corner + 2) % 3];
                var next = triangleVertices[(corner + 1) % 3];
                var previousEdge = ReadVisualModelVector(source.Vertices, previous, 0) - position;
                var nextEdge = ReadVisualModelVector(source.Vertices, next, 0) - position;
                var cornerWeight = VisualModelCornerAngle(previousEdge, nextEdge);
                var key = (position, normal, uv, orientation);
                tangentGroups.TryGetValue(key, out var accumulated);
                tangentGroups[key] = (
                    accumulated.Tangent + triangleTangent * cornerWeight,
                    accumulated.Bitangent + triangleBitangent * cornerWeight);
                vertexOrientations[vertex] = orientation;
            }
        }

        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            var orientation = vertexOrientations[vertex];
            if (orientation == 0)
                continue;

            var normal = NormalizeOrFallback(
                ReadVisualModelVector(source.Vertices, vertex, 3),
                Vector3.UnitY);
            var key = (
                ReadVisualModelVector(source.Vertices, vertex, 0),
                normal,
                ReadVisualModelUv(normalUvs, source.Vertices, vertex),
                orientation);
            if (!tangentGroups.TryGetValue(key, out var accumulated))
                continue;

            // The FBX bridge expands every polygon corner. Recombine corners
            // that MikkTSpace considers one smooth tangent vertex, while the
            // UV and orientation keys keep seams and mirrored islands split.
            var tangent = accumulated.Tangent - normal * Vector3.Dot(normal, accumulated.Tangent);
            var tangentReference = Math.Abs(normal.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;
            tangent = NormalizeOrFallback(
                tangent,
                Vector3.Cross(tangentReference, normal));
            var handedness = Vector3.Dot(
                Vector3.Cross(normal, tangent),
                accumulated.Bitangent) < 0.0f
                ? -1.0f
                : 1.0f;
            WriteVisualModelTangent(target, vertex, targetStride, tangent, handedness);
        }
    }

    private static float VisualModelCornerAngle(Vector3 first, Vector3 second)
    {
        if (first.LengthSquared < 0.00000001f || second.LengthSquared < 0.00000001f)
            return 1.0f;

        first.Normalize();
        second.Normalize();
        return MathF.Acos(Math.Clamp(Vector3.Dot(first, second), -1.0f, 1.0f));
    }

    private static Vector3 ReadVisualModelVector(
        IReadOnlyList<float> vertices,
        int vertex,
        int componentOffset)
    {
        var offset = vertex * 8 + componentOffset;
        return new Vector3(vertices[offset], vertices[offset + 1], vertices[offset + 2]);
    }

    private static Vector2 ReadVisualModelUv(
        IReadOnlyList<float>? coordinates,
        IReadOnlyList<float> fallbackVertices,
        int vertex)
    {
        return coordinates != null && vertex * 2 + 1 < coordinates.Count
            ? new Vector2(coordinates[vertex * 2], coordinates[vertex * 2 + 1])
            : new Vector2(fallbackVertices[vertex * 8 + 6], fallbackVertices[vertex * 8 + 7]);
    }

    private static void WriteVisualModelTangent(
        float[] target,
        int vertex,
        int targetStride,
        Vector3 tangent,
        float handedness)
    {
        var offset = vertex * targetStride + 16;
        target[offset] = tangent.X;
        target[offset + 1] = tangent.Y;
        target[offset + 2] = tangent.Z;
        target[offset + 3] = handedness;
    }

    private static float[]? ResolveUvCoordinates(VisualModelMesh source, string name)
    {
        return source.UvSets.FirstOrDefault(uv =>
            uv.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Coordinates;
    }

    private static void CopyUv(
        IReadOnlyList<float>? coordinates,
        IReadOnlyList<float> fallbackVertices,
        int vertex,
        float[] target,
        int targetOffset)
    {
        if (coordinates != null && vertex * 2 + 1 < coordinates.Count)
        {
            target[targetOffset] = coordinates[vertex * 2];
            target[targetOffset + 1] = coordinates[vertex * 2 + 1];
            return;
        }

        target[targetOffset] = fallbackVertices[vertex * 8 + 6];
        target[targetOffset + 1] = fallbackVertices[vertex * 8 + 7];
    }

    private static int UploadVisualModelTexture(VisualModelTexture source)
    {
        var texture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, texture);
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.Rgba,
            source.Width,
            source.Height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            source.Pixels);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        return texture;
    }

    private void ReleaseVisualModelGpu()
    {
        foreach (var mesh in _visualModelGpuMeshes)
        {
            if (mesh.Texture != 0)
                GL.DeleteTexture(mesh.Texture);
            if (mesh.SpecularTexture != 0)
                GL.DeleteTexture(mesh.SpecularTexture);
            if (mesh.NormalTexture != 0)
                GL.DeleteTexture(mesh.NormalTexture);
            if (mesh.AmbientOcclusionTexture != 0)
                GL.DeleteTexture(mesh.AmbientOcclusionTexture);
            if (mesh.AuxiliaryTexture != 0)
                GL.DeleteTexture(mesh.AuxiliaryTexture);
            if (mesh.EyeGlintTexture != 0)
                GL.DeleteTexture(mesh.EyeGlintTexture);
            if (mesh.IndexBuffer != 0)
                GL.DeleteBuffer(mesh.IndexBuffer);
            if (mesh.VertexBuffer != 0)
                GL.DeleteBuffer(mesh.VertexBuffer);
            if (mesh.VertexArray != 0)
                GL.DeleteVertexArray(mesh.VertexArray);
        }

        _visualModelGpuMeshes.Clear();
    }

    private bool TryGetVisualModelCenter(out Vector3 center)
    {
        if (_visualModel == null)
        {
            center = Vector3.Zero;
            return false;
        }

        center = Vector3.TransformPosition(
            new Vector3(
                (_visualModel.BoundsMinimum.X + _visualModel.BoundsMaximum.X) * 0.5f,
                (_visualModel.BoundsMinimum.Y + _visualModel.BoundsMaximum.Y) * 0.5f,
                (_visualModel.BoundsMinimum.Z + _visualModel.BoundsMaximum.Z) * 0.5f),
            GetVisualModelMatrix());
        return true;
    }

    private Matrix4 GetVisualModelMatrix()
    {
        var scale = Math.Max(0.0001f, _visualModelSettings.UniformScale);
        var rotation = _visualModelSettings.RotationDegrees;
        // Keep FBX overlays in the same presentation handedness as ToGl for
        // particles, bones, colliders, and editable physics geometry.
        return Matrix4.CreateScale(ReflectSceneX != _visualModelSettings.MirrorX ? -scale : scale, scale, scale)
            * Matrix4.CreateRotationX(MathHelper.DegreesToRadians(rotation.X))
            * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(rotation.Y))
            * Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(rotation.Z))
            * Matrix4.CreateTranslation(
                _visualModelSettings.Offset.X,
                _visualModelSettings.Offset.Y,
                _visualModelSettings.Offset.Z);
    }

    private static IEnumerable<Vector3> GetBoundsCorners(SysVec3 minimum, SysVec3 maximum)
    {
        yield return new Vector3(minimum.X, minimum.Y, minimum.Z);
        yield return new Vector3(maximum.X, minimum.Y, minimum.Z);
        yield return new Vector3(minimum.X, maximum.Y, minimum.Z);
        yield return new Vector3(maximum.X, maximum.Y, minimum.Z);
        yield return new Vector3(minimum.X, minimum.Y, maximum.Z);
        yield return new Vector3(maximum.X, minimum.Y, maximum.Z);
        yield return new Vector3(minimum.X, maximum.Y, maximum.Z);
        yield return new Vector3(maximum.X, maximum.Y, maximum.Z);
    }

    private void CreateVisualModelShaderProgram()
    {
        const string vertexSource = """
            #version 330 core
            layout(location = 0) in vec3 aPosition;
            layout(location = 1) in vec3 aNormal;
            layout(location = 2) in vec2 aAlbedoUv;
            layout(location = 3) in vec2 aSpmUv;
            layout(location = 4) in vec2 aNormalUv;
            layout(location = 5) in vec2 aAmbientOcclusionUv;
            layout(location = 6) in vec2 aAuxiliaryUv;
            layout(location = 7) in vec4 aTangent;
            uniform mat4 uMvp;
            uniform mat4 uModel;
            out vec3 vNormal;
            out vec3 vTangent;
            out float vTangentSign;
            out vec3 vWorldPosition;
            out vec2 vAlbedoUv;
            out vec2 vSpmUv;
            out vec2 vNormalUv;
            out vec2 vAmbientOcclusionUv;
            out vec2 vAuxiliaryUv;
            void main()
            {
                gl_Position = uMvp * vec4(aPosition, 1.0);
                mat3 normalMatrix = mat3(transpose(inverse(uModel)));
                vNormal = normalize(normalMatrix * aNormal);
                vTangent = normalize(normalMatrix * aTangent.xyz);
                vTangentSign = aTangent.w * sign(determinant(mat3(uModel)));
                vWorldPosition = (uModel * vec4(aPosition, 1.0)).xyz;
                vAlbedoUv = aAlbedoUv;
                vSpmUv = aSpmUv;
                vNormalUv = aNormalUv;
                vAmbientOcclusionUv = aAmbientOcclusionUv;
                vAuxiliaryUv = aAuxiliaryUv;
            }
            """;

        // This is a compact real-time approximation of the reference
        // BOTW/TOTK Cloth Shader: albedo plus SPM smoothness/metal channels,
        // optional tangent-space normals, and fixed sun/indirect lighting.
        const string fragmentSource = """
            #version 330 core
            in vec3 vNormal;
            in vec3 vTangent;
            in float vTangentSign;
            in vec3 vWorldPosition;
            in vec2 vAlbedoUv;
            in vec2 vSpmUv;
            in vec2 vNormalUv;
            in vec2 vAmbientOcclusionUv;
            in vec2 vAuxiliaryUv;
            uniform mat4 uModel;
            uniform vec4 uColor;
            uniform sampler2D uTexture;
            uniform sampler2D uSpecularTexture;
            uniform sampler2D uNormalTexture;
            uniform sampler2D uAmbientOcclusionTexture;
            uniform sampler2D uAuxiliaryTexture;
            uniform sampler2D uEyeGlintTexture;
            uniform int uUseTexture;
            uniform int uUseSpecularTexture;
            uniform int uUseNormalTexture;
            uniform int uUseAmbientOcclusionTexture;
            uniform int uUseAuxiliaryTexture;
            uniform int uUseEyeGlintTexture;
            uniform int uAlphaMode;
            uniform int uShaderModel;
            uniform int uMetalChannel;
            uniform int uSmoothnessChannel;
            uniform float uMetalScale;
            uniform float uSmoothnessScale;
            uniform float uNormalStrength;
            uniform float uAmbientOcclusionStrength;
            uniform float uAuxiliaryStrength;
            uniform float uEyeGlintStrength;
            uniform float uOpacity;
            uniform vec3 uKeyLight;
            uniform vec3 uFillLight;
            uniform vec3 uViewDirection;
            out vec4 FragColor;

            float sampleChannel(vec3 value, int channel)
            {
                if (channel == 0) return value.r;
                if (channel == 1) return value.g;
                if (channel == 2) return value.b;
                return 0.0;
            }

            float antialiasedStep(float edge, float value)
            {
                // The Blender graph uses clamped x10000 thresholds. Widen only
                // by the screen-space derivative so minified normal-map texels
                // cannot become isolated square patches at narrow mesh tips.
                float transition = max(fwidth(value) * 0.75, 0.001);
                return smoothstep(edge - transition, edge + transition, value);
            }

            vec3 srgbToLinear(vec3 value)
            {
                bvec3 low = lessThanEqual(value, vec3(0.04045));
                vec3 lower = value / 12.92;
                vec3 upper = pow((value + 0.055) / 1.055, vec3(2.4));
                return mix(upper, lower, low);
            }

            vec3 linearToSrgb(vec3 value)
            {
                value = max(value, vec3(0.0));
                bvec3 low = lessThanEqual(value, vec3(0.0031308));
                vec3 lower = value * 12.92;
                vec3 upper = 1.055 * pow(value, vec3(1.0 / 2.4)) - 0.055;
                return mix(upper, lower, low);
            }

            vec3 agxDefaultContrast(vec3 value)
            {
                vec3 value2 = value * value;
                vec3 value4 = value2 * value2;
                return 15.5 * value4 * value2
                    - 40.14 * value4 * value
                    + 31.96 * value4
                    - 6.868 * value2 * value
                    + 0.4298 * value2
                    + 0.1191 * value
                    - 0.00232;
            }

            vec3 agxToneMap(vec3 value)
            {
                const mat3 inset = mat3(
                    0.8424790623, 0.0423282423, 0.0423756549,
                    0.0784336000, 0.8784686365, 0.0784336000,
                    0.0792237451, 0.0791661275, 0.8791429738);
                const mat3 outset = mat3(
                    1.1968790051, -0.0528968518, -0.0529716355,
                    -0.0980208811, 1.1519031299, -0.0980434501,
                    -0.0990297441, -0.0989611768, 1.1510736726);
                const float minimumEv = -12.47393;
                const float maximumEv = 4.026069;

                value = inset * max(value, vec3(0.0000001));
                value = clamp(log2(value), minimumEv, maximumEv);
                value = (value - minimumEv) / (maximumEv - minimumEv);
                value = agxDefaultContrast(value);
                value = max(outset * value, vec3(0.0));
                // AgX's contrast approximation is display-encoded. Restore a
                // linear value before the shared linear-to-sRGB output stage.
                return clamp(pow(value, vec3(2.2)), 0.0, 1.0);
            }

            vec3 applyNormalMap(vec3 geometricNormal, vec3 encodedNormal)
            {
                vec3 tangent = normalize(
                    vTangent - geometricNormal * dot(geometricNormal, vTangent));
                vec3 bitangent = normalize(
                    cross(geometricNormal, tangent) * vTangentSign);
                mat3 tangentBasis = mat3(tangent, bitangent, geometricNormal);
                vec3 mapped = encodedNormal * 2.0 - 1.0;
                // Blender's node groups feed the authored normal into every
                // directional and facing band. A small gain keeps that detail
                // legible after the broad real-time interpolation.
                mapped.xy *= uNormalStrength * (uShaderModel == 3 ? 1.0 : 1.20);
                return normalize(tangentBasis * normalize(mapped));
            }

            vec3 inverseRotateEulerXyz(vec3 value, vec3 rotation)
            {
                float cz = cos(-rotation.z);
                float sz = sin(-rotation.z);
                value = vec3(
                    cz * value.x - sz * value.y,
                    sz * value.x + cz * value.y,
                    value.z);

                float cy = cos(-rotation.y);
                float sy = sin(-rotation.y);
                value = vec3(
                    cy * value.x + sy * value.z,
                    value.y,
                    -sy * value.x + cy * value.z);

                float cx = cos(-rotation.x);
                float sx = sin(-rotation.x);
                return vec3(
                    value.x,
                    cx * value.y - sx * value.z,
                    sx * value.y + cx * value.z);
            }

            void main()
            {
                vec4 surface = uUseTexture != 0 ? texture(uTexture, vAlbedoUv) : vec4(1.0);
                float sourceAlpha = surface.a * uColor.a;
                if ((uAlphaMode == 1 && sourceAlpha < 0.5) ||
                    (uAlphaMode == 2 && sourceAlpha < 0.01))
                    discard;

                vec3 geometricNormal = normalize(vNormal);
                vec3 normal = geometricNormal;
                if (uUseNormalTexture != 0)
                    normal = applyNormalMap(normal, texture(uNormalTexture, vNormalUv).xyz);

                // The supplied Blender materials mark SPM as sRGB, then split
                // its linearized R/G channels into smoothness and metal.
                vec3 spm = uUseSpecularTexture != 0
                    ? srgbToLinear(texture(uSpecularTexture, vSpmUv).rgb)
                    : vec3(0.0);
                float metal = uUseSpecularTexture != 0
                    ? clamp(sampleChannel(spm, uMetalChannel) * uMetalScale, 0.0, 1.0)
                    : 0.0;
                float smoothness = uUseSpecularTexture != 0
                    ? clamp(sampleChannel(spm, uSmoothnessChannel) * uSmoothnessScale, 0.0, 1.0)
                    : 0.25;
                if (uShaderModel == 2)
                {
                    // The skin group also uses SPM green as a separate shine
                    // control, while metal is not meaningful for skin.
                    smoothness = max(smoothness, spm.g * 0.65 * uSmoothnessScale);
                    metal = 0.0;
                }
                else if (uShaderModel == 3)
                {
                    metal = 0.0;
                }
                float occlusion = 1.0;
                if (uUseAmbientOcclusionTexture != 0)
                {
                    float sampledAo = srgbToLinear(texture(uAmbientOcclusionTexture, vAmbientOcclusionUv).rgb).r;
                    occlusion = mix(1.0, sampledAo, uAmbientOcclusionStrength);
                }

                vec3 baseColor = srgbToLinear(surface.rgb) * uColor.rgb;
                if (uShaderModel == 4 && uUseAuxiliaryTexture != 0)
                {
                    vec3 eyeShadow = srgbToLinear(texture(uAuxiliaryTexture, vAuxiliaryUv).rgb);
                    baseColor *= mix(vec3(1.0), eyeShadow, clamp(uAuxiliaryStrength, 0.0, 1.0));
                }
                if (uShaderModel == 4 && uUseEyeGlintTexture != 0)
                {
                    vec3 viewDirection = normalize(uViewDirection);
                    vec2 glintUv = vAlbedoUv + vec2(viewDirection.x, -viewDirection.y) * 0.025;
                    vec4 glint = texture(uEyeGlintTexture, glintUv);
                    float glintAmount = glint.a * clamp(uEyeGlintStrength, 0.0, 2.0);
                    baseColor = mix(baseColor, srgbToLinear(glint.rgb), glintAmount);
                }
                vec3 viewDirection = normalize(uViewDirection);
                float facing = clamp(dot(normal, viewDirection), 0.0, 1.0);
                float key = dot(normal, uKeyLight);
                float geometricKey = dot(geometricNormal, uKeyLight);
                float fill = max(dot(normal, uFillLight), 0.0);
                vec3 halfVector = normalize(uKeyLight + viewDirection);
                vec3 sunColor = vec3(0.61634, 0.64574, 0.48622);
                vec3 indirectColor = vec3(0.96272, 0.94923, 1.0);

                if (uShaderModel == 3)
                {
                    // FBX SDK converts Blender Z-up to OpenGL Y-up, and the
                    // viewport''s presentation matrix then reflects X. Restore
                    // Blender space before evaluating its authored hair graph.
                    vec3 blenderNormal = normalize(vec3(-normal.x, -normal.z, normal.y));
                    vec3 blenderView = normalize(vec3(
                        -viewDirection.x,
                        -viewDirection.z,
                        viewDirection.y));
                    // Match the inverse Euler rotation authored in the supplied
                    // BOTW/TOTK Hair Shader group. These values steer every
                    // normal-map lighting band, so approximating them moves the
                    // broad highlight regions to the wrong side of the hair.
                    const vec3 hairLightRotation = vec3(
                        1.823601,
                        -0.173579,
                        2.596994);
                    float lightZ = inverseRotateEulerXyz(
                        blenderNormal,
                        hairLightRotation).z;

                    float positiveLight = antialiasedStep(0.10, lightZ);
                    float negativeLight = 1.0 - positiveLight;
                    // Blender Layer Weight/Facing is one minus the absolute
                    // view-normal dot product. It selects grazing silhouettes,
                    // not the camera-facing center of the surface.
                    float facingBlender = clamp(
                        1.0 - abs(dot(blenderNormal, blenderView)),
                        0.0,
                        1.0);
                    float facingBand = antialiasedStep(0.70, facingBlender);

                    // Base World Light: Alb * (1 + positiveLight).
                    vec3 hairLayers = baseColor * (1.0 + positiveLight);

                    // Back Rim Light. Blender transforms world Z into camera
                    // space; the view elevation is its local equivalent here.
                    float cameraLightZ = inverseRotateEulerXyz(
                        blenderView,
                        hairLightRotation).z;
                    float negativeSlope = max(negativeLight - clamp(-lightZ, 0.0, 1.0), 0.0);
                    float backRim = antialiasedStep(
                        0.20,
                        cameraLightZ * negativeSlope * facingBlender);
                    hairLayers *= 1.0 + backRim;

                    // Vector Sunlight Rimlight and Fade Rimlight add neutral
                    // bands before the lighting-controller tint is applied.
                    hairLayers += vec3(0.30) * positiveLight * facingBand;
                    float fadeRim = negativeLight * facingBand * cameraLightZ;
                    hairLayers += vec3(0.10) * fadeRim;

                    // Hair Specular is another cel multiplier, not a glossy
                    // lobe. The graph combines reflected-Z thresholds at 0.1
                    // and 0.9, then gates them with positiveLight.
                    // Blender builds this as Incoming - 2*N*dot(N, Incoming),
                    // then multiplies the result by -1 before rotating it.
                    vec3 reflectedIncoming = -reflect(-blenderView, blenderNormal);
                    float reflectedZ = inverseRotateEulerXyz(
                        reflectedIncoming,
                        hairLightRotation).z;
                    float reflectedLow = antialiasedStep(0.10, reflectedZ);
                    float reflectedHigh = antialiasedStep(0.90, reflectedZ);
                    float hairSpecular = positiveLight
                        * clamp(reflectedLow * 0.5 + reflectedHigh, 0.0, 1.0);
                    hairLayers *= 1.0 + hairSpecular;

                    // Scene Lighting Data selects between the supplied indirect
                    // tint and the sun color at strength 2.
                    vec3 hairColor = hairLayers
                        * mix(indirectColor, sunColor * 2.0, positiveLight);
                    float hairAlpha = uAlphaMode == 1 ? 1.0 : sourceAlpha;
                    FragColor = vec4(
                        linearToSrgb(agxToneMap(hairColor)),
                        hairAlpha * uOpacity);
                    return;
                }

                vec3 shadowTint = vec3(0.82, 0.80, 0.76);
                if (uShaderModel == 2)
                    shadowTint = vec3(0.82, 0.70, 0.64);
                else if (uShaderModel == 3)
                    shadowTint = vec3(0.62, 0.50, 0.20);
                else if (uShaderModel == 5)
                    shadowTint = vec3(0.24);

                // The reference Blender groups use large multiply/add values to
                // turn diffuse light into hard BOTW/TOTK-style color bands.
                float mainBand = smoothstep(0.055, 0.085, key);
                float brightBand = smoothstep(0.58, 0.64, key);
                vec3 lighting = mix(shadowTint, indirectColor, mainBand);
                lighting = mix(lighting, vec3(1.10, 1.07, 0.98), brightBand * 0.58);
                lighting += indirectColor * smoothstep(0.30, 0.55, fill) * 0.035;

                // The Blender groups use Layer Weight/Facing repeatedly. This
                // broad view-facing band is what makes their shading move as
                // the camera orbits, without turning the material glossy.
                float grazing = 1.0 - facing;
                float facingShade = smoothstep(0.70, 0.94, grazing);
                lighting = mix(lighting, shadowTint * 0.92, facingShade * 0.18);

                if (uUseNormalTexture != 0)
                {
                    float normalDetail = clamp(key - geometricKey, -0.50, 0.50);
                    lighting *= 1.0 + normalDetail * 0.30;
                }

                if (uShaderModel == 4)
                    lighting = mix(vec3(0.84), vec3(1.10), smoothstep(-0.15, 0.24, key));

                float resolvedAo = uShaderModel == 2
                    ? clamp(occlusion * 5.0 - 1.25, 0.0, 1.0)
                    : occlusion;
                // In the reference skin group AO participates in the color-band
                // selection (AO * 5 - 1.25), rather than uniformly darkening the
                // final color. Approximate that by steering only shadowed and
                // grazing lighting toward the authored shadow tint.
                float aoAmount = 1.0 - resolvedAo;
                float aoShadowGate = 1.0 - smoothstep(-0.10, 0.56, key);
                float aoWeight = aoAmount
                    * mix(0.08, uShaderModel == 2 ? 0.42 : 0.28, aoShadowGate)
                    * mix(0.82, 1.14, smoothstep(0.42, 0.90, grazing));
                lighting = mix(lighting, shadowTint * 0.86, clamp(aoWeight, 0.0, 0.46));
                vec3 diffuse = baseColor * lighting;
                diffuse *= mix(1.0, 0.78, metal);

                float halfDot = max(dot(normal, halfVector), 0.0);
                float lightGate = smoothstep(-0.04, 0.28, key);
                float specularPower = mix(12.0, 88.0, smoothness);
                float specularLobe = pow(halfDot, specularPower);
                float specularBand = smoothstep(0.07, 0.24, specularLobe);
                float specularStrength = mix(0.006, 0.090, smoothness) * mix(1.0, 1.65, metal) * lightGate;
                vec3 specularColor = mix(indirectColor, baseColor, metal);

                if (uShaderModel == 1)
                {
                    // Cloth uses broad view-facing color bands. Smoothness opens
                    // the band while metal selects a stronger albedo-tinted path.
                    float clothLobe = pow(halfDot, mix(7.0, 34.0, smoothness));
                    float clothFacing = smoothstep(
                        mix(0.92, 0.66, smoothness),
                        mix(0.99, 0.86, smoothness),
                        grazing);
                    specularBand = max(smoothstep(0.08, 0.28, clothLobe), clothFacing * 0.72);
                    specularStrength = mix(0.004, 0.048, smoothness)
                        * mix(1.0, 2.35, metal) * lightGate;
                    specularColor = mix(indirectColor, baseColor, metal);
                }
                else if (uShaderModel == 2)
                {
                    float skinLobe = pow(halfDot, mix(18.0, 72.0, smoothness));
                    specularBand = smoothstep(0.10, 0.30, skinLobe);
                    specularStrength = mix(0.002, 0.028, smoothness) * lightGate;
                    specularColor = mix(indirectColor, vec3(1.0, 0.78, 0.70), 0.18);
                }
                else if (uShaderModel == 3)
                {
                    // Smoothness is exposed but unused by the supplied Blender
                    // hair group. Its highlights are already in the cel bands.
                    specularStrength = 0.0;
                }
                else if (uShaderModel == 4)
                {
                    // Eye highlights come from the authored glint texture. A
                    // second view-dependent lobe makes the eyeballs look wet.
                    specularStrength = 0.0;
                }
                else if (uShaderModel == 5)
                {
                    specularStrength = 0.0;
                }

                specularColor = mix(specularColor, sunColor * 1.12, metal * 0.32);
                float rim = 1.0 - facing;
                float rimBand = smoothstep(0.82, 0.94, rim);
                float rimStrength = uShaderModel == 3 ? 0.03 :
                    (uShaderModel == 4 || uShaderModel == 5 ? 0.0 : 0.018);
                vec3 color = diffuse
                    + specularColor * specularBand * specularStrength
                    + baseColor * rimBand * rimStrength;
                color *= 2.0;
                float resolvedAlpha = uAlphaMode == 1 ? 1.0 : sourceAlpha;
                FragColor = vec4(linearToSrgb(color), resolvedAlpha * uOpacity);
            }
            """;

        var vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        var fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
        _visualModelProgram = GL.CreateProgram();
        GL.AttachShader(_visualModelProgram, vertex);
        GL.AttachShader(_visualModelProgram, fragment);
        GL.LinkProgram(_visualModelProgram);
        GL.GetProgram(_visualModelProgram, GetProgramParameterName.LinkStatus, out var linked);
        if (linked == 0)
            throw new InvalidOperationException(GL.GetProgramInfoLog(_visualModelProgram));

        GL.DetachShader(_visualModelProgram, vertex);
        GL.DetachShader(_visualModelProgram, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
        _visualModelMvpLocation = GL.GetUniformLocation(_visualModelProgram, "uMvp");
        _visualModelModelLocation = GL.GetUniformLocation(_visualModelProgram, "uModel");
        _visualModelColorLocation = GL.GetUniformLocation(_visualModelProgram, "uColor");
        _visualModelTextureLocation = GL.GetUniformLocation(_visualModelProgram, "uTexture");
        _visualModelUseTextureLocation = GL.GetUniformLocation(_visualModelProgram, "uUseTexture");
        _visualModelSpecularTextureLocation = GL.GetUniformLocation(_visualModelProgram, "uSpecularTexture");
        _visualModelUseSpecularTextureLocation = GL.GetUniformLocation(_visualModelProgram, "uUseSpecularTexture");
        _visualModelNormalTextureLocation = GL.GetUniformLocation(_visualModelProgram, "uNormalTexture");
        _visualModelUseNormalTextureLocation = GL.GetUniformLocation(_visualModelProgram, "uUseNormalTexture");
        _visualModelAmbientOcclusionTextureLocation = GL.GetUniformLocation(_visualModelProgram, "uAmbientOcclusionTexture");
        _visualModelUseAmbientOcclusionTextureLocation = GL.GetUniformLocation(_visualModelProgram, "uUseAmbientOcclusionTexture");
        _visualModelAuxiliaryTextureLocation = GL.GetUniformLocation(_visualModelProgram, "uAuxiliaryTexture");
        _visualModelUseAuxiliaryTextureLocation = GL.GetUniformLocation(_visualModelProgram, "uUseAuxiliaryTexture");
        _visualModelEyeGlintTextureLocation = GL.GetUniformLocation(_visualModelProgram, "uEyeGlintTexture");
        _visualModelUseEyeGlintTextureLocation = GL.GetUniformLocation(_visualModelProgram, "uUseEyeGlintTexture");
        _visualModelAlphaModeLocation = GL.GetUniformLocation(_visualModelProgram, "uAlphaMode");
        _visualModelShaderModelLocation = GL.GetUniformLocation(_visualModelProgram, "uShaderModel");
        _visualModelAuxiliaryStrengthLocation = GL.GetUniformLocation(_visualModelProgram, "uAuxiliaryStrength");
        _visualModelEyeGlintStrengthLocation = GL.GetUniformLocation(_visualModelProgram, "uEyeGlintStrength");
        _visualModelMetalChannelLocation = GL.GetUniformLocation(_visualModelProgram, "uMetalChannel");
        _visualModelSmoothnessChannelLocation = GL.GetUniformLocation(_visualModelProgram, "uSmoothnessChannel");
        _visualModelMetalScaleLocation = GL.GetUniformLocation(_visualModelProgram, "uMetalScale");
        _visualModelSmoothnessScaleLocation = GL.GetUniformLocation(_visualModelProgram, "uSmoothnessScale");
        _visualModelNormalStrengthLocation = GL.GetUniformLocation(_visualModelProgram, "uNormalStrength");
        _visualModelAmbientOcclusionStrengthLocation = GL.GetUniformLocation(_visualModelProgram, "uAmbientOcclusionStrength");
        _visualModelOpacityLocation = GL.GetUniformLocation(_visualModelProgram, "uOpacity");
        _visualModelKeyLightLocation = GL.GetUniformLocation(_visualModelProgram, "uKeyLight");
        _visualModelFillLightLocation = GL.GetUniformLocation(_visualModelProgram, "uFillLight");
        _visualModelViewDirectionLocation = GL.GetUniformLocation(_visualModelProgram, "uViewDirection");
    }

    private void DrawSingleColor(
        PrimitiveType primitive,
        IReadOnlyList<Vector3> vertices,
        Vector4 color,
        Matrix4 mvp,
        float lineWidth,
        float pointSize,
        bool depthTest)
    {
        DrawSegments(primitive, vertices, new[] { (0, vertices.Count, color) }, mvp, lineWidth, pointSize, depthTest);
    }

    private void DrawSegments(
        PrimitiveType primitive,
        IReadOnlyList<Vector3> vertices,
        IEnumerable<(int Start, int Count, Vector4 Color)> segments,
        Matrix4 mvp,
        float lineWidth,
        float pointSize,
        bool depthTest)
    {
        if (vertices.Count == 0)
            return;

        if (depthTest)
            GL.Enable(EnableCap.DepthTest);
        else
            GL.Disable(EnableCap.DepthTest);

        GL.LineWidth(lineWidth);
        GL.UseProgram(_program);
        GL.UniformMatrix4(_uMvp, false, ref mvp);
        GL.Uniform1(_uPointSize, pointSize);
        GL.Uniform1(_uRoundPoints, primitive == PrimitiveType.Points ? 1 : 0);
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * 3 * sizeof(float), ToFloatArray(vertices), BufferUsageHint.DynamicDraw);

        foreach (var segment in segments)
        {
            GL.Uniform4(_uColor, segment.Color);
            GL.DrawArrays(primitive, segment.Start, segment.Count);
        }

        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    private void CreateShaderProgram()
    {
        const string vertexSource = """
            #version 330 core
            layout(location = 0) in vec3 aPosition;
            uniform mat4 uMvp;
            uniform float uPointSize;
            void main()
            {
                gl_Position = uMvp * vec4(aPosition, 1.0);
                gl_PointSize = uPointSize;
            }
            """;

        const string fragmentSource = """
            #version 330 core
            uniform vec4 uColor;
            uniform int uRoundPoints;
            out vec4 FragColor;
            void main()
            {
                if (uRoundPoints != 0)
                {
                    vec2 point = gl_PointCoord * 2.0 - 1.0;
                    if (dot(point, point) > 1.0)
                        discard;
                }
                FragColor = uColor;
            }
            """;

        var vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        var fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
        _program = GL.CreateProgram();
        GL.AttachShader(_program, vertex);
        GL.AttachShader(_program, fragment);
        GL.LinkProgram(_program);
        GL.GetProgram(_program, GetProgramParameterName.LinkStatus, out var linked);
        if (linked == 0)
            throw new InvalidOperationException(GL.GetProgramInfoLog(_program));

        GL.DetachShader(_program, vertex);
        GL.DetachShader(_program, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);

        _uMvp = GL.GetUniformLocation(_program, "uMvp");
        _uColor = GL.GetUniformLocation(_program, "uColor");
        _uPointSize = GL.GetUniformLocation(_program, "uPointSize");
        _uRoundPoints = GL.GetUniformLocation(_program, "uRoundPoints");
    }

    private static int CompileShader(ShaderType type, string source)
    {
        var shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out var compiled);
        if (compiled == 0)
            throw new InvalidOperationException(GL.GetShaderInfoLog(shader));

        return shader;
    }

    private bool TryGetParticle(int index, out ParticlePreviewPoint particle)
    {
        particle = _data?.Particles.FirstOrDefault(p => p.Index == index)!;
        return particle != null;
    }

    private static float FixedGridStep(float radius)
    {
        if (radius < 0.2f)
            return 0.01f;
        if (radius < 0.7f)
            return 0.025f;
        if (radius < 2.0f)
            return 0.05f;
        if (radius < 5.0f)
            return 0.1f;
        return 0.25f;
    }

    private static bool IsFinite(Vector3 point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);

    private bool HasTransformSelection() => GetTransformSelectionPositions().Count > 0;

    private List<Vector3> GetTransformSelectionPositions()
    {
        if (_data == null)
            return new List<Vector3>();

        return PickKind switch
        {
            PreviewPickKind.Bone => _data.Bones
                .Where(bone => bone.IsSelectable && GetBoneEditorIndex(bone) == SelectedBoneIndex)
                .Select(bone => ToGl(bone.Position))
                .ToList(),
            PreviewPickKind.Collider => _data.Colliders
                .Where(collider => collider.Index == SelectedColliderIndex || _selectedColliderIndices.Contains(collider.Index))
                .DistinctBy(collider => collider.Index)
                .Select(collider => (ToGl(collider.Start) + ToGl(collider.End)) * 0.5f)
                .ToList(),
            _ => _data.Particles
                .Where(particle => _selectedParticleIndices.Contains(particle.Index))
                .Select(particle => ToGl(particle.Position))
                .ToList()
        };
    }

    private void SetAxisConstraint(Keys key, Vector3 worldAxis)
    {
        if (_axisConstraintKey == key && _localAxisConstraint)
            return;

        var requestLocalAxis = _axisConstraintKey == key && PickKind != PreviewPickKind.Particle;
        _axisConstraintKey = key;
        _localAxisConstraint = requestLocalAxis;
        _axisConstraint = requestLocalAxis
            ? GetSelectedLocalWorldAxis(worldAxis)
            : ToGl(new SysVec3(worldAxis.X, worldAxis.Y, worldAxis.Z));
    }

    private Vector3 GetSelectedLocalWorldAxis(Vector3 fallback)
    {
        if (_data == null)
            return fallback;

        var boneIndex = PickKind switch
        {
            // Bone translations are stored in their parent's coordinate space.
            // A repeated axis key should therefore use the parent frame, not
            // the selected bone's rotated drawing axis.
            PreviewPickKind.Bone => _data.Bones.FirstOrDefault(bone =>
                bone.IsSelectable && GetBoneEditorIndex(bone) == SelectedBoneIndex)?.ParentIndex ?? -1,
            PreviewPickKind.Collider => _data.Colliders.FirstOrDefault(collider => collider.Index == SelectedColliderIndex)?.BoneIndex ?? -1,
            _ => -1
        };
        var bone = _data.Bones.FirstOrDefault(candidate => candidate.Index == boneIndex);
        if (bone == null)
            return fallback;

        var axis = fallback.X > 0.5f ? ToGl(bone.AxisX)
            : fallback.Y > 0.5f ? ToGl(bone.AxisY)
            : ToGl(bone.AxisZ);
        return axis.LengthSquared < 0.000001f ? fallback : Vector3.Normalize(axis);
    }

    private static void AddLine(List<Vector3> vertices, Vector3 a, Vector3 b)
    {
        vertices.Add(a);
        vertices.Add(b);
    }

    // Native physics/BFRES scenes keep their handedness even without a model.
    // Only the legacy FBX presentation opts into X reflection.
    private bool _reflectSceneX = true;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool ReflectSceneX
    {
        get => _reflectSceneX && _visualModel != null &&
            Path.GetExtension(_visualModel.SourcePath).Equals(".fbx", StringComparison.OrdinalIgnoreCase);
        init => _reflectSceneX = value;
    }

    private Vector3 ToGl(SysVec3 vector)
    {
        return new Vector3(ReflectSceneX ? -vector.X : vector.X, vector.Y, vector.Z);
    }

    private static int GetBoneEditorIndex(BonePreviewPoint bone) =>
        bone.EditorIndex >= 0 ? bone.EditorIndex : bone.Index;

    private static float[] ToFloatArray(IReadOnlyList<Vector3> vertices)
    {
        var result = new float[vertices.Count * 3];
        for (var i = 0; i < vertices.Count; i++)
        {
            result[i * 3] = vertices[i].X;
            result[i * 3 + 1] = vertices[i].Y;
            result[i * 3 + 2] = vertices[i].Z;
        }

        return result;
    }

    private void DrawFallbackMessage(Graphics graphics, string title, string? details)
    {
        graphics.Clear(Color.White);
        using var brush = new SolidBrush(Color.FromArgb(70, 70, 70));
        graphics.DrawString(title, Font, brush, 12, 12);
        if (!string.IsNullOrWhiteSpace(details))
            graphics.DrawString(details, Font, brush, 12, 34);
    }
}
