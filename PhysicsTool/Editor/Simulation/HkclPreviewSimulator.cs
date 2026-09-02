using System.Numerics;

namespace HKCLTool;

// A lightweight, non-authoritative preview of the HKCL particle solver.
// It intentionally never writes into the HKCL document or editor rows.
public sealed class HkclPreviewSimulator
{
    // The preview is intentionally conservative. Havok's production solver has
    // additional stabilization stages we do not emulate, so protect the editor
    // from malformed edits or large frame hitches without changing file data.
    private const float MaximumPreviewSpeed = 24.0f;
    private const float MaximumPreviewExcursion = 5.0f;
    private readonly Dictionary<int, SimParticle> _particles;
    private readonly IReadOnlyList<ParticlePreviewLink> _links;
    private readonly IReadOnlyList<ParticlePreviewLocalRange> _localRanges;
    private readonly IReadOnlyList<ParticlePreviewBendStiffness> _bendStiffnessConstraints;
    private readonly IReadOnlyDictionary<int, ParticlePreviewConstraintSet> _constraintSets;
    private readonly IReadOnlyList<int> _constraintExecution;
    private readonly IReadOnlyList<ColliderPreviewShape> _colliders;
    private readonly IReadOnlyList<ParticlePreviewBoneBinding> _boneBindings;
    private readonly Dictionary<int, SimulatedBonePreviewPose> _restBonePoses;
    private readonly Dictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> _particleAttachments;
    private readonly Dictionary<int, Matrix4x4> _inputBoneDeltas = new();
    private readonly Dictionary<int, int> _boneParents;
    private readonly HashSet<int> _triangleDrivenBones;
    private readonly IReadOnlyDictionary<ParticlePreviewBoneBinding, BoneBindingCalibration> _boneBindingCalibrations;
    private readonly Dictionary<ParticlePreviewBoneBinding, Matrix4x4> _previousBindingFrames = new();
    private readonly Dictionary<int, SimulatedBonePreviewPose> _previousDrivenBonePoses = new();
    private readonly HashSet<int> _drivenBoneIndices;
    private readonly Vector3 _gravity;
    private readonly float _dampingPerSecond;
    private readonly int _authoredSubSteps;
    private readonly int _authoredSolveIterations;
    private readonly bool _adaptConstraintStiffness;
    private readonly bool _transferMotionEnabled;
    private readonly int _transferMotionTransformIndex;
    private readonly bool _transferTranslationMotion;
    private readonly float _minTranslationSpeed;
    private readonly float _maxTranslationSpeed;
    private readonly float _minTranslationBlend;
    private readonly float _maxTranslationBlend;
    private readonly bool _transferRotationMotion;
    private readonly float _minRotationSpeed;
    private readonly float _maxRotationSpeed;
    private readonly float _minRotationBlend;
    private readonly float _maxRotationBlend;
    private Matrix4x4 _previousTransferMotionDelta = Matrix4x4.Identity;
    private bool _hasPreviousTransferMotionDelta;
    private readonly Random _windRandom = new();
    private float _windTime;
    private float _nextRandomWindChange;
    private Vector3 _currentWindDirection = Vector3.UnitX;
    private Vector3 _targetWindDirection = Vector3.UnitX;

    public HkclPreviewSimulator(ParticlePreviewData source)
    {
        _particles = source.Particles.ToDictionary(
            particle => particle.Index,
            particle => new SimParticle(particle));
        _links = source.Links;
        _localRanges = source.LocalRanges;
        _bendStiffnessConstraints = source.BendStiffnessConstraints;
        _constraintSets = source.ConstraintSets.ToDictionary(set => set.Index);
        _constraintExecution = source.ConstraintExecution.Count > 0
            ? source.ConstraintExecution.ToArray()
            : source.ConstraintSets.Select(set => set.Index).Append(-1).ToArray();
        // The viewport animates its displayed collider objects in place. Keep
        // an immutable solver-side copy so those display updates cannot become
        // the next frame's collision bind pose.
        _colliders = source.Colliders.Select(CloneCollider).ToArray();
        _boneBindings = source.BoneBindings;
        _restBonePoses = source.Bones.ToDictionary(
            bone => bone.Index,
            bone => new SimulatedBonePreviewPose
            {
                Position = bone.Position,
                AxisX = bone.AxisX,
                AxisY = bone.AxisY,
                AxisZ = bone.AxisZ
            });
        _particleAttachments = source.ParticleAttachments
            .Where(attachment => attachment.BoneWeights.Count > 0)
            .GroupBy(attachment => attachment.ParticleIndex)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ParticlePreviewBoneWeight>)group.Last().BoneWeights.ToArray());
        _boneParents = source.Bones.ToDictionary(bone => bone.Index, bone => bone.ParentIndex);
        _triangleDrivenBones = _boneBindings.Select(binding => binding.BoneIndex).ToHashSet();
        _boneBindingCalibrations = _boneBindings.ToDictionary(binding => binding, CalibrateBoneBinding);
        ResetBindingContinuity();
        _drivenBoneIndices = BuildDrivenBoneIndices();
        _gravity = source.Gravity;
        _dampingPerSecond = Math.Max(0.0f, source.DampingPerSecond);
        _authoredSubSteps = Math.Clamp(source.SimulationSubSteps, 1, 32);
        _authoredSolveIterations = Math.Clamp(source.SimulationSolveIterations, 1, 32);
        _adaptConstraintStiffness = source.AdaptConstraintStiffness;
        _transferMotionEnabled = source.TransferMotionEnabled;
        _transferMotionTransformIndex = source.TransferMotionTransformIndex;
        _transferTranslationMotion = source.TransferTranslationMotion;
        _minTranslationSpeed = source.MinTranslationSpeed;
        _maxTranslationSpeed = source.MaxTranslationSpeed;
        _minTranslationBlend = source.MinTranslationBlend;
        _maxTranslationBlend = source.MaxTranslationBlend;
        _transferRotationMotion = source.TransferRotationMotion;
        _minRotationSpeed = source.MinRotationSpeed;
        _maxRotationSpeed = source.MaxRotationSpeed;
        _minRotationBlend = source.MinRotationBlend;
        _maxRotationBlend = source.MaxRotationBlend;
    }

    public void Reset()
    {
        foreach (var particle in _particles.Values)
            particle.Reset(GetParticleReferencePosition(particle));
        _windTime = 0.0f;
        _nextRandomWindChange = 0.0f;
        _currentWindDirection = Vector3.UnitX;
        _targetWindDirection = Vector3.UnitX;
        _previousTransferMotionDelta = Matrix4x4.Identity;
        _hasPreviousTransferMotionDelta = false;
        ResetBindingContinuity();
    }

    public IReadOnlyDictionary<int, Vector3> GetPositions()
    {
        return _particles.ToDictionary(entry => entry.Key, entry => entry.Value.Position);
    }

    public IReadOnlyDictionary<int, ColliderPreviewShape> GetColliderPoses()
    {
        return _colliders.ToDictionary(
            collider => collider.Index,
            collider => GetAnimatedCollider(collider));
    }

    public bool WindEnabled { get; set; }
    public bool RandomWindDirections { get; set; } = true;
    public Vector3 WindDirection { get; set; } = Vector3.UnitX;
    public float WindSpeed { get; set; } = 2.2f;
    public float WindGustiness { get; set; } = 0.35f;
    public float GravityScale { get; set; } = 1.0f;
    // Multiplies the file's authored solve count. A value of one is faithful.
    public int SolverIterations { get; set; } = 1;
    public IReadOnlySet<int> DrivenBoneIndices => _drivenBoneIndices;
    public int LocalFirstBoneBindingCount => _boneBindingCalibrations.Count(pair => pair.Value.LocalFirst);
    public int FrameFirstBoneBindingCount => _boneBindingCalibrations.Count - LocalFirstBoneBindingCount;
    public int AffineBoneBindingCount => _boneBindingCalibrations.Count(pair =>
        pair.Value.FrameKind != BoneBindingFrameKind.RigidVertex);
    public int NativeCentroidBoneBindingCount => _boneBindingCalibrations.Count(pair =>
        pair.Value.FrameKind == BoneBindingFrameKind.NativeCentroid);
    public int LastBindingFallbackCount { get; private set; }
    public int LastBindingFlipCorrectionCount { get; private set; }

    /// <summary>
    /// Supplies rest-world to current-world skeleton transforms for this frame.
    /// Havok consumes this transform set before MoveParticles and simulation.
    /// </summary>
    public void SetInputBoneDeltas(IReadOnlyDictionary<int, Matrix4x4> deltas)
    {
        _inputBoneDeltas.Clear();
        foreach (var (boneIndex, delta) in deltas)
        {
            if (IsFinite(delta))
                _inputBoneDeltas[boneIndex] = delta;
        }
    }

    public IReadOnlyDictionary<int, SimulatedBonePreviewPose> GetBonePoses()
    {
        LastBindingFallbackCount = 0;
        LastBindingFlipCorrectionCount = 0;
        var accumulated = new Dictionary<int, (Vector3 Position, Vector3 AxisX, Vector3 AxisY, Vector3 AxisZ, int Count)>();
        foreach (var binding in _boneBindings)
        {
            if (!TryEvaluateBoneBinding(binding, out var evaluated))
            {
                if (_previousDrivenBonePoses.TryGetValue(binding.BoneIndex, out var previousPose))
                {
                    evaluated = ClonePose(previousPose);
                    LastBindingFallbackCount++;
                }
                else
                {
                    continue;
                }
            }

            if (accumulated.TryGetValue(binding.BoneIndex, out var previous))
            {
                accumulated[binding.BoneIndex] = (
                    previous.Position + evaluated.Position,
                    previous.AxisX + evaluated.AxisX,
                    previous.AxisY + evaluated.AxisY,
                    previous.AxisZ + evaluated.AxisZ,
                    previous.Count + 1);
            }
            else
            {
                accumulated.Add(binding.BoneIndex, (
                    evaluated.Position,
                    evaluated.AxisX,
                    evaluated.AxisY,
                    evaluated.AxisZ,
                    1));
            }
        }

        var poses = _restBonePoses.ToDictionary(entry => entry.Key, entry =>
        {
            var inputBone = GetInputBonePose(entry.Key, entry.Value);
            if (!accumulated.TryGetValue(entry.Key, out var pose))
            {
                return new SimulatedBonePreviewPose
                {
                    Position = inputBone.Position,
                    AxisX = inputBone.AxisX,
                    AxisY = inputBone.AxisY,
                    AxisZ = inputBone.AxisZ,
                    StretchScale = 1.0f
                };
            }

            return new SimulatedBonePreviewPose
            {
                Position = pose.Position / pose.Count,
                AxisX = NormalizeOrFallback(pose.AxisX / pose.Count, inputBone.AxisX),
                AxisY = NormalizeOrFallback(pose.AxisY / pose.Count, inputBone.AxisY),
                AxisZ = NormalizeOrFallback(pose.AxisZ / pose.Count, inputBone.AxisZ),
                StretchScale = 1.0f
            };
        });

        ApplyRotationAndStretchHierarchy(poses);
        foreach (var boneIndex in _triangleDrivenBones)
        {
            if (poses.TryGetValue(boneIndex, out var pose))
                _previousDrivenBonePoses[boneIndex] = ClonePose(pose);
        }
        return poses;
    }

    private void ResetBindingContinuity()
    {
        _previousBindingFrames.Clear();
        _previousDrivenBonePoses.Clear();
        foreach (var binding in _boneBindings)
        {
            if (!_particles.TryGetValue(binding.ParticleA, out var a) ||
                !_particles.TryGetValue(binding.ParticleB, out var b) ||
                !_particles.TryGetValue(binding.ParticleC, out var c))
            {
                continue;
            }

            if (_boneBindingCalibrations.TryGetValue(binding, out var calibration) &&
                TryBuildBindingFrame(
                    calibration.FrameKind,
                    a.RestPosition,
                    b.RestPosition,
                    c.RestPosition,
                    out var frame))
            {
                _previousBindingFrames[binding] = frame;
            }
        }
    }

    private static SimulatedBonePreviewPose ClonePose(SimulatedBonePreviewPose pose) => new()
    {
        Position = pose.Position,
        AxisX = pose.AxisX,
        AxisY = pose.AxisY,
        AxisZ = pose.AxisZ,
        StretchScale = pose.StretchScale
    };

    private void ApplyRotationAndStretchHierarchy(Dictionary<int, SimulatedBonePreviewPose> poses)
    {
        var explicitlyDrivenBones = new HashSet<int>(_triangleDrivenBones);
        var explicitlyPositionedBones = new HashSet<int>(_triangleDrivenBones);
        foreach (var boneIndex in _inputBoneDeltas.Keys)
        {
            explicitlyDrivenBones.Add(boneIndex);
            explicitlyPositionedBones.Add(boneIndex);
        }

        BridgeTriangleDrivenBoneChains(
            poses,
            explicitlyDrivenBones,
            explicitlyPositionedBones);

        var childrenByParent = _boneParents
            .Where(entry => entry.Value >= 0 && poses.ContainsKey(entry.Value))
            .GroupBy(entry => entry.Value)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Key).ToArray());

        var resolvedBones = new HashSet<int>();
        void ResolveBone(int boneIndex)
        {
            if (!resolvedBones.Add(boneIndex) || !_restBonePoses.TryGetValue(boneIndex, out var restBone) ||
                !poses.TryGetValue(boneIndex, out var currentBone))
            {
                return;
            }

            if (_boneParents.TryGetValue(boneIndex, out var parentIndex) &&
                parentIndex >= 0 && _restBonePoses.TryGetValue(parentIndex, out var restParent) &&
                poses.TryGetValue(parentIndex, out var currentParent))
            {
                ResolveBone(parentIndex);
                currentParent = poses[parentIndex];

                if (!explicitlyDrivenBones.Contains(boneIndex))
                {
                    currentBone.AxisX = NormalizeOrFallback(TransformFromRestFrame(restBone.AxisX, restParent, currentParent), restBone.AxisX);
                    currentBone.AxisY = NormalizeOrFallback(TransformFromRestFrame(restBone.AxisY, restParent, currentParent), restBone.AxisY);
                    currentBone.AxisZ = NormalizeOrFallback(TransformFromRestFrame(restBone.AxisZ, restParent, currentParent), restBone.AxisZ);
                }

                if (!explicitlyPositionedBones.Contains(boneIndex))
                {
                    var restOffset = restBone.Position - restParent.Position;
                    var rotatedOffset = TransformFromRestFrame(restOffset, restParent, currentParent);
                    currentBone.Position = currentParent.Position + rotatedOffset * currentParent.StretchScale;
                }
            }

            if (childrenByParent.TryGetValue(boneIndex, out var children))
            {
                foreach (var childIndex in children)
                    ResolveBone(childIndex);
            }
        }

        foreach (var root in poses.Keys.Where(index => !_boneParents.TryGetValue(index, out var parent) || parent < 0).ToArray())
            ResolveBone(root);
        foreach (var boneIndex in poses.Keys.ToArray())
            ResolveBone(boneIndex);
    }

    private void BridgeTriangleDrivenBoneChains(
        Dictionary<int, SimulatedBonePreviewPose> poses,
        HashSet<int> explicitlyDrivenBones,
        HashSet<int> explicitlyPositionedBones)
    {
        foreach (var endBoneIndex in _triangleDrivenBones)
        {
            if (!poses.TryGetValue(endBoneIndex, out var endPose))
                continue;

            var intermediate = new List<int>();
            var ancestorIndex = _boneParents.GetValueOrDefault(endBoneIndex, -1);
            while (ancestorIndex >= 0 && !_triangleDrivenBones.Contains(ancestorIndex))
            {
                intermediate.Add(ancestorIndex);
                ancestorIndex = _boneParents.GetValueOrDefault(ancestorIndex, -1);
            }
            if (ancestorIndex < 0 || intermediate.Count == 0 ||
                !poses.TryGetValue(ancestorIndex, out var startPose) ||
                !_restBonePoses.TryGetValue(ancestorIndex, out var restStart) ||
                !_restBonePoses.TryGetValue(endBoneIndex, out var restEnd))
            {
                continue;
            }

            intermediate.Reverse();
            var chain = new List<int>(intermediate.Count + 2) { ancestorIndex };
            chain.AddRange(intermediate);
            chain.Add(endBoneIndex);
            var segmentLengths = new float[chain.Count - 1];
            var totalLength = 0.0f;
            for (var index = 0; index < segmentLengths.Length; index++)
            {
                if (!_restBonePoses.TryGetValue(chain[index], out var restA) ||
                    !_restBonePoses.TryGetValue(chain[index + 1], out var restB))
                {
                    totalLength = 0.0f;
                    break;
                }
                segmentLengths[index] = Vector3.Distance(restA.Position, restB.Position);
                totalLength += segmentLengths[index];
            }
            if (totalLength <= 0.000001f)
                continue;

            var startRotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(ToMatrix(startPose)));
            var endRotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(ToMatrix(endPose)));
            if (Quaternion.Dot(startRotation, endRotation) < 0.0f)
            {
                endRotation = new Quaternion(
                    -endRotation.X,
                    -endRotation.Y,
                    -endRotation.Z,
                    -endRotation.W);
            }

            var distance = 0.0f;
            for (var index = 0; index < intermediate.Count; index++)
            {
                distance += segmentLengths[index];
                var fraction = Math.Clamp(distance / totalLength, 0.0f, 1.0f);
                var rotation = Quaternion.Normalize(Quaternion.Slerp(startRotation, endRotation, fraction));
                var rotationMatrix = Matrix4x4.CreateFromQuaternion(rotation);
                var boneIndex = intermediate[index];
                poses[boneIndex] = new SimulatedBonePreviewPose
                {
                    Position = Vector3.Lerp(startPose.Position, endPose.Position, fraction),
                    AxisX = new Vector3(rotationMatrix.M11, rotationMatrix.M12, rotationMatrix.M13),
                    AxisY = new Vector3(rotationMatrix.M21, rotationMatrix.M22, rotationMatrix.M23),
                    AxisZ = new Vector3(rotationMatrix.M31, rotationMatrix.M32, rotationMatrix.M33),
                    StretchScale = 1.0f
                };
                explicitlyDrivenBones.Add(boneIndex);
                explicitlyPositionedBones.Add(boneIndex);
            }
        }
    }

    private HashSet<int> BuildDrivenBoneIndices()
    {
        var result = new HashSet<int>(_triangleDrivenBones);
        foreach (var endBoneIndex in _triangleDrivenBones)
        {
            var intermediate = new List<int>();
            var ancestorIndex = _boneParents.GetValueOrDefault(endBoneIndex, -1);
            while (ancestorIndex >= 0 && !_triangleDrivenBones.Contains(ancestorIndex))
            {
                intermediate.Add(ancestorIndex);
                ancestorIndex = _boneParents.GetValueOrDefault(ancestorIndex, -1);
            }

            // A gap is cloth-owned only when both ends are authored deformer
            // outputs. Bones above the first driven anchor remain animation-owned.
            if (ancestorIndex >= 0)
                result.UnionWith(intermediate);
        }
        return result;
    }

    private BoneBindingCalibration CalibrateBoneBinding(ParticlePreviewBoneBinding binding)
    {
        if (!_restBonePoses.TryGetValue(binding.BoneIndex, out var restBone) ||
            !_particles.TryGetValue(binding.ParticleA, out var a) ||
            !_particles.TryGetValue(binding.ParticleB, out var b) ||
            !_particles.TryGetValue(binding.ParticleC, out var c) ||
            !TryBuildTriangleMatrix(a.RestPosition, b.RestPosition, c.RestPosition, out var rigidRestFrame))
        {
            return new BoneBindingCalibration(
                Matrix4x4.Identity,
                true,
                BoneBindingFrameKind.RigidVertex);
        }

        var restBoneMatrix = ToMatrix(restBone);
        var candidates = new List<(float Error, bool LocalFirst, BoneBindingFrameKind FrameKind)>();
        if (TryBuildCentroidTriangleMatrix(a.RestPosition, b.RestPosition, c.RestPosition, out var centroidRestFrame))
            AddCandidates(centroidRestFrame, BoneBindingFrameKind.NativeCentroid);
        AddCandidates(rigidRestFrame, BoneBindingFrameKind.RigidVertex);
        if (TryBuildAffineTriangleMatrix(a.RestPosition, b.RestPosition, c.RestPosition, out var affineRestFrame))
            AddCandidates(affineRestFrame, BoneBindingFrameKind.AffineVertex);

        var best = candidates.OrderBy(candidate => candidate.Error).First();
        if (best.Error < 0.01f)
            return new BoneBindingCalibration(binding.LocalBoneTransform, best.LocalFirst, best.FrameKind);

        // Older conversions did not always retain localBoneTransforms. Deriving
        // the same triangle-local bind transform is deterministic and still uses
        // the authored TriangleBonePair rather than a nearest-particle guess.
        var hasNativeFrame = TryBuildCentroidTriangleMatrix(
            a.RestPosition,
            b.RestPosition,
            c.RestPosition,
            out var nativeFrame);
        var fallbackFrame = hasNativeFrame ? nativeFrame : rigidRestFrame;
        var fallbackKind = hasNativeFrame
            ? BoneBindingFrameKind.NativeCentroid
            : BoneBindingFrameKind.RigidVertex;
        return Matrix4x4.Invert(fallbackFrame, out var inverseRestFrame)
            ? new BoneBindingCalibration(restBoneMatrix * inverseRestFrame, true, fallbackKind)
            : new BoneBindingCalibration(Matrix4x4.Identity, true, fallbackKind);

        void AddCandidates(Matrix4x4 frame, BoneBindingFrameKind frameKind)
        {
            var localFirst = OrthogonalizeBoneMatrix(binding.LocalBoneTransform * frame, binding.BoneAxis, restBone);
            var frameFirst = OrthogonalizeBoneMatrix(frame * binding.LocalBoneTransform, binding.BoneAxis, restBone);
            candidates.Add((PoseError(localFirst, restBoneMatrix), true, frameKind));
            candidates.Add((PoseError(frameFirst, restBoneMatrix), false, frameKind));
        }
    }

    private bool TryEvaluateBoneBinding(ParticlePreviewBoneBinding binding, out SimulatedBonePreviewPose pose)
    {
        pose = new SimulatedBonePreviewPose();
        if (!_boneBindingCalibrations.TryGetValue(binding, out var calibration) ||
            !_particles.TryGetValue(binding.ParticleA, out var a) ||
            !_particles.TryGetValue(binding.ParticleB, out var b) ||
            !_particles.TryGetValue(binding.ParticleC, out var c) ||
            !TryBuildContinuousBindingFrame(binding, calibration, a.Position, b.Position, c.Position, out var frame))
        {
            return false;
        }

        var world = calibration.LocalFirst
            ? calibration.LocalTransform * frame
            : frame * calibration.LocalTransform;
        var fallbackPose = _previousDrivenBonePoses.TryGetValue(binding.BoneIndex, out var previousPose)
            ? previousPose
            : _restBonePoses.TryGetValue(binding.BoneIndex, out var restBone)
                ? restBone
                : new SimulatedBonePreviewPose();
        world = OrthogonalizeBoneMatrix(world, binding.BoneAxis, fallbackPose);
        pose = new SimulatedBonePreviewPose
        {
            Position = new Vector3(world.M41, world.M42, world.M43),
            AxisX = new Vector3(world.M11, world.M12, world.M13),
            AxisY = new Vector3(world.M21, world.M22, world.M23),
            AxisZ = new Vector3(world.M31, world.M32, world.M33),
            StretchScale = 1.0f
        };
        return IsFinite(pose.Position) && IsFinite(pose.AxisX) && IsFinite(pose.AxisY) && IsFinite(pose.AxisZ);
    }

    private bool TryBuildContinuousBindingFrame(
        ParticlePreviewBoneBinding binding,
        BoneBindingCalibration calibration,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out Matrix4x4 frame)
    {
        var built = TryBuildBindingFrame(calibration.FrameKind, a, b, c, out frame);
        if (!built)
        {
            if (!_previousBindingFrames.TryGetValue(binding, out frame))
                return false;
            var origin = calibration.FrameKind == BoneBindingFrameKind.NativeCentroid
                ? (a + b + c) / 3.0f
                : a;
            frame.M41 = origin.X;
            frame.M42 = origin.Y;
            frame.M43 = origin.Z;
            LastBindingFallbackCount++;
            return true;
        }

        _previousBindingFrames[binding] = frame;
        return true;
    }

    private static bool TryBuildBindingFrame(
        BoneBindingFrameKind kind,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out Matrix4x4 matrix) => kind switch
        {
            BoneBindingFrameKind.NativeCentroid => TryBuildCentroidTriangleMatrix(a, b, c, out matrix),
            BoneBindingFrameKind.AffineVertex => TryBuildAffineTriangleMatrix(a, b, c, out matrix),
            _ => TryBuildTriangleMatrix(a, b, c, out matrix)
        };

    // hclSimpleMeshBoneDeformOperator builds this unnormalized frame from the
    // simulated triangle, then applies its authored localBoneTransform. This is
    // also the frame used when rebuilding native BPHCL deformer matrices.
    private static bool TryBuildCentroidTriangleMatrix(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out Matrix4x4 matrix)
    {
        var centroid = (a + b + c) / 3.0f;
        var axisX = a - centroid;
        var axisY = b - centroid;
        var axisZ = Vector3.Cross(axisX, axisY);
        // The virtual triangles are often only millimeters wide. The former
        // generic 1e-6 squared-length cutoff treated valid skirt triangles as
        // degenerate and repeatedly froze their output bones.
        if (axisX.LengthSquared() <= 1.0e-12f ||
            axisY.LengthSquared() <= 1.0e-12f ||
            axisZ.LengthSquared() <= 1.0e-16f)
        {
            matrix = Matrix4x4.Identity;
            return false;
        }

        matrix = new Matrix4x4(
            axisX.X, axisX.Y, axisX.Z, 0.0f,
            axisY.X, axisY.Y, axisY.Z, 0.0f,
            axisZ.X, axisZ.Y, axisZ.Z, 0.0f,
            centroid.X, centroid.Y, centroid.Z, 1.0f);
        return true;
    }

    private static bool TryBuildTriangleMatrix(Vector3 a, Vector3 b, Vector3 c, out Matrix4x4 matrix)
    {
        if (!TryBuildTriangleFrame(a, b, c, out var axisX, out var axisY, out var axisZ))
        {
            matrix = Matrix4x4.Identity;
            return false;
        }

        matrix = new Matrix4x4(
            axisX.X, axisX.Y, axisX.Z, 0.0f,
            axisY.X, axisY.Y, axisY.Z, 0.0f,
            axisZ.X, axisZ.Y, axisZ.Z, 0.0f,
            a.X, a.Y, a.Z, 1.0f);
        return true;
    }

    private static bool TryBuildAffineTriangleMatrix(Vector3 a, Vector3 b, Vector3 c, out Matrix4x4 matrix)
    {
        var axisX = b - a;
        var axisY = c - a;
        var axisZ = Vector3.Cross(axisX, axisY);
        if (axisX.LengthSquared() <= 0.000001f ||
            axisY.LengthSquared() <= 0.000001f ||
            axisZ.LengthSquared() <= 0.000001f)
        {
            matrix = Matrix4x4.Identity;
            return false;
        }

        axisZ = Vector3.Normalize(axisZ);
        matrix = new Matrix4x4(
            axisX.X, axisX.Y, axisX.Z, 0.0f,
            axisY.X, axisY.Y, axisY.Z, 0.0f,
            axisZ.X, axisZ.Y, axisZ.Z, 0.0f,
            a.X, a.Y, a.Z, 1.0f);
        return true;
    }

    // Havok retains one authored bone axis while removing triangle shear from
    // the output transform. Normalizing each row independently is insufficient:
    // it leaves a skewed basis that a skinned FBX interprets as the wrong rotation.
    private static Matrix4x4 OrthogonalizeBoneMatrix(
        Matrix4x4 matrix,
        int boneAxis,
        SimulatedBonePreviewPose fallback,
        float maximumTwistStep = float.PositiveInfinity)
    {
        var rawX = new Vector3(matrix.M11, matrix.M12, matrix.M13);
        var rawY = new Vector3(matrix.M21, matrix.M22, matrix.M23);
        var rawZ = new Vector3(matrix.M31, matrix.M32, matrix.M33);
        var retainedAxis = boneAxis switch
        {
            0 or 1 or 2 => boneAxis,
            3 => SelectBestBoneAxis(rawX, rawY, rawZ),
            _ => 0
        };

        if (float.IsFinite(maximumTwistStep))
        {
            return BuildContinuousBoneMatrix(
                matrix,
                rawX,
                rawY,
                rawZ,
                retainedAxis,
                fallback,
                maximumTwistStep);
        }

        Vector3 axisX;
        Vector3 axisY;
        Vector3 axisZ;
        switch (retainedAxis)
        {
            case 1:
                axisY = NormalizeOrFallback(rawY, fallback.AxisY);
                axisX = NormalizeProjected(rawX, axisY, fallback.AxisX);
                axisZ = NormalizeOrFallback(Vector3.Cross(axisX, axisY), fallback.AxisZ);
                axisX = NormalizeOrFallback(Vector3.Cross(axisY, axisZ), axisX);
                break;
            case 2:
                axisZ = NormalizeOrFallback(rawZ, fallback.AxisZ);
                axisX = NormalizeProjected(rawX, axisZ, fallback.AxisX);
                axisY = NormalizeOrFallback(Vector3.Cross(axisZ, axisX), fallback.AxisY);
                axisX = NormalizeOrFallback(Vector3.Cross(axisY, axisZ), axisX);
                break;
            default:
                axisX = NormalizeOrFallback(rawX, fallback.AxisX);
                axisY = NormalizeProjected(rawY, axisX, fallback.AxisY);
                axisZ = NormalizeOrFallback(Vector3.Cross(axisX, axisY), fallback.AxisZ);
                axisY = NormalizeOrFallback(Vector3.Cross(axisZ, axisX), axisY);
                break;
        }

        return new Matrix4x4(
            axisX.X, axisX.Y, axisX.Z, 0.0f,
            axisY.X, axisY.Y, axisY.Z, 0.0f,
            axisZ.X, axisZ.Y, axisZ.Z, 0.0f,
            matrix.M41, matrix.M42, matrix.M43, 1.0f);
    }

    private static Matrix4x4 BuildContinuousBoneMatrix(
        Matrix4x4 source,
        Vector3 rawX,
        Vector3 rawY,
        Vector3 rawZ,
        int retainedAxis,
        SimulatedBonePreviewPose fallback,
        float maximumTwistStep)
    {
        var rawRetained = retainedAxis switch
        {
            1 => rawY,
            2 => rawZ,
            _ => rawX
        };
        var fallbackRetained = retainedAxis switch
        {
            1 => fallback.AxisY,
            2 => fallback.AxisZ,
            _ => fallback.AxisX
        };
        var targetRetained = NormalizeOrFallback(rawRetained, fallbackRetained);
        fallbackRetained = NormalizeOrFallback(fallbackRetained, targetRetained);
        var swingAngle = MathF.Acos(Math.Clamp(
            Vector3.Dot(fallbackRetained, targetRetained),
            -1.0f,
            1.0f));
        var swing = QuaternionFromTo(fallbackRetained, targetRetained);
        if (swingAngle > maximumTwistStep)
        {
            swing = Quaternion.Normalize(Quaternion.Slerp(
                Quaternion.Identity,
                swing,
                maximumTwistStep / swingAngle));
        }
        var retained = NormalizeOrFallback(
            Vector3.Transform(fallbackRetained, swing),
            targetRetained);

        Vector3 axisX;
        Vector3 axisY;
        Vector3 axisZ;
        switch (retainedAxis)
        {
            case 1:
                axisY = retained;
                axisX = LimitTwist(
                    rawX,
                    Vector3.Transform(fallback.AxisX, swing),
                    axisY,
                    maximumTwistStep);
                axisZ = NormalizeOrFallback(Vector3.Cross(axisX, axisY), fallback.AxisZ);
                axisX = NormalizeOrFallback(Vector3.Cross(axisY, axisZ), axisX);
                break;
            case 2:
                axisZ = retained;
                axisX = LimitTwist(
                    rawX,
                    Vector3.Transform(fallback.AxisX, swing),
                    axisZ,
                    maximumTwistStep);
                axisY = NormalizeOrFallback(Vector3.Cross(axisZ, axisX), fallback.AxisY);
                axisX = NormalizeOrFallback(Vector3.Cross(axisY, axisZ), axisX);
                break;
            default:
                axisX = retained;
                axisY = LimitTwist(
                    rawY,
                    Vector3.Transform(fallback.AxisY, swing),
                    axisX,
                    maximumTwistStep);
                axisZ = NormalizeOrFallback(Vector3.Cross(axisX, axisY), fallback.AxisZ);
                axisY = NormalizeOrFallback(Vector3.Cross(axisZ, axisX), axisY);
                break;
        }

        return new Matrix4x4(
            axisX.X, axisX.Y, axisX.Z, 0.0f,
            axisY.X, axisY.Y, axisY.Z, 0.0f,
            axisZ.X, axisZ.Y, axisZ.Z, 0.0f,
            source.M41, source.M42, source.M43, 1.0f);
    }

    private static Vector3 LimitTwist(
        Vector3 rawSecondary,
        Vector3 transportedSecondary,
        Vector3 retainedAxis,
        float maximumTwistStep)
    {
        var current = NormalizeProjected(rawSecondary, retainedAxis, transportedSecondary);
        var previous = NormalizeProjected(transportedSecondary, retainedAxis, current);
        if (Vector3.Dot(current, previous) < 0.0f)
            current = -current;

        var signedAngle = MathF.Atan2(
            Vector3.Dot(Vector3.Cross(previous, current), retainedAxis),
            Math.Clamp(Vector3.Dot(previous, current), -1.0f, 1.0f));
        var limitedAngle = Math.Clamp(signedAngle, -maximumTwistStep, maximumTwistStep);
        return NormalizeOrFallback(
            Vector3.Transform(
                previous,
                Quaternion.CreateFromAxisAngle(retainedAxis, limitedAngle)),
            previous);
    }

    private static int SelectBestBoneAxis(Vector3 axisX, Vector3 axisY, Vector3 axisZ)
    {
        var lengths = new[] { axisX.LengthSquared(), axisY.LengthSquared(), axisZ.LengthSquared() };
        var best = 0;
        for (var index = 1; index < lengths.Length; index++)
        {
            if (lengths[index] > lengths[best])
                best = index;
        }
        return best;
    }

    private static Vector3 NormalizeProjected(
        Vector3 value,
        Vector3 retainedAxis,
        Vector3 fallback)
    {
        var projected = value - retainedAxis * Vector3.Dot(value, retainedAxis);
        var fallbackProjected = fallback - retainedAxis * Vector3.Dot(fallback, retainedAxis);
        if (projected.LengthSquared() > 0.000001f)
        {
            projected = Vector3.Normalize(projected);
            if (fallbackProjected.LengthSquared() > 0.000001f &&
                Vector3.Dot(projected, fallbackProjected) < 0.0f)
            {
                projected = -projected;
            }

            return projected;
        }

        if (fallbackProjected.LengthSquared() > 0.000001f)
            return Vector3.Normalize(fallbackProjected);

        var perpendicular = MathF.Abs(retainedAxis.X) < 0.8f ? Vector3.UnitX : Vector3.UnitY;
        return Vector3.Normalize(perpendicular - retainedAxis * Vector3.Dot(perpendicular, retainedAxis));
    }

    private static Matrix4x4 ToMatrix(SimulatedBonePreviewPose pose) => new(
        pose.AxisX.X, pose.AxisX.Y, pose.AxisX.Z, 0.0f,
        pose.AxisY.X, pose.AxisY.Y, pose.AxisY.Z, 0.0f,
        pose.AxisZ.X, pose.AxisZ.Y, pose.AxisZ.Z, 0.0f,
        pose.Position.X, pose.Position.Y, pose.Position.Z, 1.0f);

    private static float PoseError(Matrix4x4 left, Matrix4x4 right) =>
        Vector3.Distance(new Vector3(left.M41, left.M42, left.M43), new Vector3(right.M41, right.M42, right.M43)) +
        0.25f * (1.0f - Math.Clamp(Vector3.Dot(
            NormalizeOrFallback(new Vector3(left.M11, left.M12, left.M13), Vector3.UnitX),
            NormalizeOrFallback(new Vector3(right.M11, right.M12, right.M13), Vector3.UnitX)), -1.0f, 1.0f)) +
        0.25f * (1.0f - Math.Clamp(Vector3.Dot(
            NormalizeOrFallback(new Vector3(left.M21, left.M22, left.M23), Vector3.UnitY),
            NormalizeOrFallback(new Vector3(right.M21, right.M22, right.M23), Vector3.UnitY)), -1.0f, 1.0f));

    private static Vector3 TransformFromRestFrame(Vector3 vector, SimulatedBonePreviewPose rest, SimulatedBonePreviewPose current)
    {
        return current.AxisX * Vector3.Dot(vector, rest.AxisX) +
               current.AxisY * Vector3.Dot(vector, rest.AxisY) +
               current.AxisZ * Vector3.Dot(vector, rest.AxisZ);
    }

    private static Quaternion QuaternionFromTo(Vector3 from, Vector3 to)
    {
        var dot = Math.Clamp(Vector3.Dot(from, to), -1.0f, 1.0f);
        if (dot > 0.999999f)
            return Quaternion.Identity;

        if (dot < -0.999999f)
        {
            var orthogonal = Math.Abs(from.X) < 0.8f ? Vector3.UnitX : Vector3.UnitY;
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from, orthogonal)), MathF.PI);
        }

        var cross = Vector3.Cross(from, to);
        return Quaternion.Normalize(new Quaternion(cross, 1.0f + dot));
    }

    private static bool TryBuildTriangleFrame(Vector3 a, Vector3 b, Vector3 c, out Vector3 axisX, out Vector3 axisY, out Vector3 axisZ)
    {
        axisX = b - a;
        if (axisX.LengthSquared() <= 0.000001f)
        {
            axisY = axisZ = Vector3.Zero;
            return false;
        }

        axisX = Vector3.Normalize(axisX);
        axisZ = Vector3.Cross(axisX, c - a);
        if (axisZ.LengthSquared() <= 0.000001f)
        {
            axisY = axisZ = Vector3.Zero;
            return false;
        }

        axisZ = Vector3.Normalize(axisZ);
        axisY = Vector3.Normalize(Vector3.Cross(axisZ, axisX));
        return true;
    }

    private static Vector3 RotateFromTriangleFrame(
        Vector3 axis,
        Vector3 restX,
        Vector3 restY,
        Vector3 restZ,
        Vector3 currentX,
        Vector3 currentY,
        Vector3 currentZ)
    {
        return currentX * Vector3.Dot(axis, restX) +
               currentY * Vector3.Dot(axis, restY) +
               currentZ * Vector3.Dot(axis, restZ);
    }

    private static Vector3 NormalizeOrFallback(Vector3 axis, Vector3 fallback) =>
        axis.LengthSquared() > 0.000001f ? Vector3.Normalize(axis) : fallback;

    public void Step(float deltaSeconds)
    {
        var step = Math.Clamp(deltaSeconds, 1.0f / 240.0f, 1.0f / 30.0f);
        var subSteps = _authoredSubSteps;
        var constraintIterations = _authoredSolveIterations * Math.Clamp(SolverIterations, 1, 24);
        var subStep = step / subSteps;
        _windTime += step;
        ApplyTransferMotion(step);

        for (var subStepIndex = 0; subStepIndex < subSteps; subStepIndex++)
        {
            var windDirection = GetWindDirection(subStep);
            var gust = 1.0f + MathF.Sin(_windTime * 1.8f) * Math.Clamp(WindGustiness, 0.0f, 1.0f);
            var wind = WindEnabled ? windDirection * Math.Max(0.0f, WindSpeed) * gust : Vector3.Zero;
            Integrate(subStep, wind);
            for (var iteration = 0; iteration < constraintIterations; iteration++)
            {
                foreach (var executionEntry in _constraintExecution)
                {
                    if (executionEntry < 0)
                    {
                        ResolveAllCollisions();
                        continue;
                    }

                    SolveConstraintSet(executionEntry);
                }
            }

            foreach (var particle in _particles.Values)
                StabilizeParticle(particle);
        }
    }

    private void SolveConstraintSet(int constraintSetIndex)
    {
        var kind = _constraintSets.TryGetValue(constraintSetIndex, out var set)
            ? set.Kind
            : string.Empty;
        if (kind.Contains("Transition", StringComparison.Ordinal))
            return;
        if (kind.Contains("LocalRange", StringComparison.Ordinal))
        {
            SolveLocalRanges(constraintSetIndex);
            return;
        }
        if (kind.Contains("BendStiffness", StringComparison.Ordinal))
        {
            SolveBendStiffness(constraintSetIndex);
            return;
        }

        foreach (var link in _links.Where(value => value.ConstraintSetIndex == constraintSetIndex))
            SolveLink(link, kind);
    }

    private void ResolveAllCollisions()
    {
        foreach (var particle in _particles.Values)
            ResolveCollisions(particle);
    }

    private Vector3 GetWindDirection(float deltaSeconds)
    {
        if (RandomWindDirections && _windTime >= _nextRandomWindChange)
        {
            // Favor side-to-side motion, with enough vertical variation to feel like a breeze.
            var candidate = new Vector3(
                (float)(_windRandom.NextDouble() * 2.0 - 1.0),
                (float)(_windRandom.NextDouble() * 0.7 - 0.35),
                (float)(_windRandom.NextDouble() * 2.0 - 1.0));
            _targetWindDirection = candidate.LengthSquared() > 0.000001f
                ? Vector3.Normalize(candidate)
                : Vector3.UnitX;
            _nextRandomWindChange = _windTime + 2.0f + (float)_windRandom.NextDouble() * 2.5f;
        }
        else if (!RandomWindDirections)
        {
            _targetWindDirection = WindDirection.LengthSquared() > 0.000001f
                ? Vector3.Normalize(WindDirection)
                : Vector3.UnitX;
        }

        var blend = 1.0f - MathF.Exp(-deltaSeconds * 2.2f);
        var blended = Vector3.Lerp(_currentWindDirection, _targetWindDirection, blend);
        _currentWindDirection = blended.LengthSquared() > 0.000001f
            ? Vector3.Normalize(blended)
            : _targetWindDirection;
        return _currentWindDirection;
    }

    private void Integrate(float deltaSeconds, Vector3 wind)
    {
        var damping = MathF.Exp(-_dampingPerSecond * deltaSeconds);
        foreach (var particle in _particles.Values)
        {
            if (particle.Fixed)
            {
                particle.PreviousPosition = particle.Position;
                particle.Position = GetParticleReferencePosition(particle);
                continue;
            }

            var velocity = ClampMagnitude(
                (particle.Position - particle.PreviousPosition) * damping,
                MaximumPreviewSpeed * deltaSeconds);
            particle.PreviousPosition = particle.Position;
            particle.Position += velocity + (_gravity * Math.Max(0.0f, GravityScale) + wind) * (deltaSeconds * deltaSeconds);
        }
    }

    private void SolveLink(ParticlePreviewLink link, string constraintKind)
    {
        if (!_particles.TryGetValue(link.ParticleA, out var a) || !_particles.TryGetValue(link.ParticleB, out var b))
            return;

        var offset = b.Position - a.Position;
        var length = offset.Length();
        if (length <= 0.000001f)
            return;

        var target = link.RestLength;
        var strength = link.Stiffness ?? 1.0f;
        if (constraintKind.Contains("StretchLink", StringComparison.Ordinal) && target.HasValue && length <= target.Value)
            return;
        if (constraintKind.Contains("BendLink", StringComparison.Ordinal))
        {
            if (link.BendMinLength.HasValue && length < link.BendMinLength.Value)
            {
                target = link.BendMinLength.Value;
                strength = link.BendStiffness ?? strength;
            }
            else if (link.StretchMaxLength.HasValue && length > link.StretchMaxLength.Value)
            {
                target = link.StretchMaxLength.Value;
                strength = link.StretchStiffness ?? link.BendStiffness ?? strength;
            }
            else
            {
                return;
            }
        }

        if (!target.HasValue)
            return;

        // A bad rest length must not reposition a chain several meters in one
        // projection. Havok solves these with more internal guards; resolve
        // large errors over successive preview iterations instead.
        var maximumProjection = Math.Clamp(
            Math.Max(target.Value, a.Radius + b.Radius) * 0.35f,
            0.0025f,
            0.15f);
        var lengthError = Math.Clamp(length - target.Value, -maximumProjection, maximumProjection);
        var correction = offset / length * lengthError;
        var weightA = a.EffectiveInverseMass;
        var weightB = b.EffectiveInverseMass;

        // Standard and bend links store a mass-scaled solver coefficient, not
        // a normalized spring strength. Havok's optimized StandardLink form
        // expands this into stiffnessA/stiffnessB, one value per endpoint.
        // Applying inverse mass a second time through a normalized pair weight
        // makes large cloths far too soft and also incorrectly clips valid
        // coefficients above one.
        var usesMassScaledCoefficient =
            constraintKind.Contains("StandardLink", StringComparison.Ordinal) ||
            constraintKind.Contains("BendLink", StringComparison.Ordinal);
        if (usesMassScaledCoefficient)
        {
            var coefficient = Math.Max(0.0f, strength);
            if (weightA > 0.0f)
            {
                var stiffnessA = GetPerPassStiffness(
                    Math.Clamp(coefficient * weightA, 0.0f, 1.0f),
                    link.ConstraintSetIndex);
                ApplyConstraintCorrection(a, correction * stiffnessA);
            }
            if (weightB > 0.0f)
            {
                var stiffnessB = GetPerPassStiffness(
                    Math.Clamp(coefficient * weightB, 0.0f, 1.0f),
                    link.ConstraintSetIndex);
                ApplyConstraintCorrection(b, -correction * stiffnessB);
            }
            return;
        }

        var weightTotal = weightA + weightB;
        if (weightTotal <= 0.0f)
            return;
        strength = GetPerPassStiffness(strength, link.ConstraintSetIndex);
        correction *= Math.Clamp(strength, 0.0f, 1.0f);
        if (weightA > 0.0f)
            ApplyConstraintCorrection(a, correction * (weightA / weightTotal));
        if (weightB > 0.0f)
            ApplyConstraintCorrection(b, -correction * (weightB / weightTotal));
    }

    private void SolveLocalRanges(int constraintSetIndex)
    {
        foreach (var range in _localRanges.Where(value => value.ConstraintSetIndex == constraintSetIndex))
        {
            if (!_particles.TryGetValue(range.ParticleIndex, out var particle) || particle.Fixed)
                continue;

            var maximumDistance = Math.Max(0.0f, range.MaximumDistance);
            var referencePosition = GetLocalRangeReferencePosition(range, particle);
            var offset = particle.Position - referencePosition;
            var distance = offset.Length();
            if (distance <= maximumDistance || distance <= 0.000001f)
                continue;

            ApplyConstraintCorrection(
                particle,
                (referencePosition + offset * (maximumDistance / distance) - particle.Position) *
                GetPerPassStiffness(range.Stiffness, range.ConstraintSetIndex));
        }
    }

    private void SolveBendStiffness(int constraintSetIndex)
    {
        foreach (var bend in _bendStiffnessConstraints.Where(value => value.ConstraintSetIndex == constraintSetIndex))
        {
            if (!_particles.TryGetValue(bend.ParticleA, out var a) ||
                !_particles.TryGetValue(bend.ParticleB, out var b) ||
                !_particles.TryGetValue(bend.ParticleC, out var c) ||
                !_particles.TryGetValue(bend.ParticleD, out var d))
            {
                continue;
            }

            // Havok stores the four linear bend weights and a negative solver
            // coefficient that already includes their inverse-mass denominator.
            // Preserve the authored rest curvature by projecting the weighted
            // displacement back to its rest-pose value.
            var current =
                a.Position * bend.WeightA + b.Position * bend.WeightB +
                c.Position * bend.WeightC + d.Position * bend.WeightD;
            var rest =
                a.RestPosition * bend.WeightA + b.RestPosition * bend.WeightB +
                c.RestPosition * bend.WeightC + d.RestPosition * bend.WeightD;
            var error = current - rest;
            if (!IsFinite(error) || error.LengthSquared() <= 0.000000000001f)
                continue;

            var coefficient = bend.BendStiffness;
            if (!float.IsFinite(coefficient) || coefficient == 0.0f)
                continue;
            if (coefficient > 0.0f)
                coefficient = -coefficient;

            ApplyBendCorrection(a, error * (a.EffectiveInverseMass * bend.WeightA * coefficient));
            ApplyBendCorrection(b, error * (b.EffectiveInverseMass * bend.WeightB * coefficient));
            ApplyBendCorrection(c, error * (c.EffectiveInverseMass * bend.WeightC * coefficient));
            ApplyBendCorrection(d, error * (d.EffectiveInverseMass * bend.WeightD * coefficient));
        }
    }

    private static void ApplyBendCorrection(SimParticle particle, Vector3 correction)
    {
        if (particle.Fixed)
            return;
        ApplyConstraintCorrection(particle, ClampMagnitude(correction, 0.05f));
    }

    private void ApplyTransferMotion(float deltaSeconds)
    {
        var current = _inputBoneDeltas.TryGetValue(_transferMotionTransformIndex, out var inputDelta)
            ? inputDelta
            : Matrix4x4.Identity;
        if (!_transferMotionEnabled || (!_transferTranslationMotion && !_transferRotationMotion))
        {
            _previousTransferMotionDelta = current;
            _hasPreviousTransferMotionDelta = true;
            return;
        }
        if (!_hasPreviousTransferMotionDelta)
        {
            _previousTransferMotionDelta = current;
            _hasPreviousTransferMotionDelta = true;
            return;
        }
        if (!Matrix4x4.Invert(_previousTransferMotionDelta, out var inversePrevious))
        {
            _previousTransferMotionDelta = current;
            return;
        }

        var relative = inversePrevious * current;
        var rotation = Quaternion.Identity;
        if (Matrix4x4.Decompose(relative, out _, out var relativeRotation, out _))
            rotation = Quaternion.Normalize(relativeRotation);

        var restPivot = _restBonePoses.TryGetValue(_transferMotionTransformIndex, out var restBone)
            ? restBone.Position
            : Vector3.Zero;
        var previousPivot = Vector3.Transform(restPivot, _previousTransferMotionDelta);
        var currentPivot = Vector3.Transform(restPivot, current);
        var translationDelta = currentPivot - previousPivot;
        var translationSpeed = translationDelta.Length() / Math.Max(deltaSeconds, 0.000001f);
        var translationBlend = _transferTranslationMotion
            ? MapTransferBlend(translationSpeed, _minTranslationSpeed, _maxTranslationSpeed, _minTranslationBlend, _maxTranslationBlend)
            : 0.0f;

        var rotationAngle = 2.0f * MathF.Acos(Math.Clamp(MathF.Abs(rotation.W), 0.0f, 1.0f));
        var rotationSpeed = rotationAngle * (180.0f / MathF.PI) / Math.Max(deltaSeconds, 0.000001f);
        var rotationBlend = _transferRotationMotion
            ? MapTransferBlend(rotationSpeed, _minRotationSpeed, _maxRotationSpeed, _minRotationBlend, _maxRotationBlend)
            : 0.0f;
        var blendedRotation = Quaternion.Slerp(Quaternion.Identity, rotation, rotationBlend);
        var blendedTranslation = translationDelta * translationBlend;

        foreach (var particle in _particles.Values.Where(value => !value.Fixed))
        {
            particle.Position = previousPivot +
                Vector3.Transform(particle.Position - previousPivot, blendedRotation) +
                blendedTranslation;
            particle.PreviousPosition = previousPivot +
                Vector3.Transform(particle.PreviousPosition - previousPivot, blendedRotation) +
                blendedTranslation;
        }
        _previousTransferMotionDelta = current;
    }

    private static float MapTransferBlend(float speed, float minSpeed, float maxSpeed, float minBlend, float maxBlend)
    {
        minBlend = Math.Clamp(minBlend, 0.0f, 1.0f);
        maxBlend = Math.Clamp(maxBlend, 0.0f, 1.0f);
        if (maxSpeed <= minSpeed + 0.000001f)
            return speed >= minSpeed ? maxBlend : minBlend;
        var fraction = Math.Clamp((speed - minSpeed) / (maxSpeed - minSpeed), 0.0f, 1.0f);
        return minBlend + (maxBlend - minBlend) * fraction;
    }

    private Vector3 GetLocalRangeReferencePosition(ParticlePreviewLocalRange range, SimParticle particle)
    {
        if (range.ReferenceBoneWeights.Count == 0)
            return GetParticleReferencePosition(particle);

        var position = Vector3.Zero;
        var totalWeight = 0.0f;
        foreach (var attachment in range.ReferenceBoneWeights)
        {
            var weight = Math.Max(0.0f, attachment.Weight);
            if (weight <= 0.0f)
                continue;
            position += (_inputBoneDeltas.TryGetValue(attachment.BoneIndex, out var delta)
                ? Vector3.Transform(particle.RestPosition, delta)
                : particle.RestPosition) * weight;
            totalWeight += weight;
        }
        return totalWeight > 0.000001f ? position / totalWeight : particle.RestPosition;
    }

    private float GetPerPassStiffness(float stiffness, int constraintSetIndex)
    {
        var clamped = Math.Clamp(stiffness, 0.0f, 1.0f);
        if (!_adaptConstraintStiffness || clamped is <= 0.0f or >= 1.0f)
            return clamped;

        var occurrences = Math.Max(1, _constraintExecution.Count(value => value == constraintSetIndex));
        var passes = Math.Max(1, occurrences * _authoredSubSteps * _authoredSolveIterations * Math.Clamp(SolverIterations, 1, 24));
        return 1.0f - MathF.Pow(1.0f - clamped, 1.0f / passes);
    }

    private Vector3 GetParticleReferencePosition(SimParticle particle)
    {
        if (!_particleAttachments.TryGetValue(particle.Index, out var attachments) || attachments.Count == 0)
            return particle.RestPosition;

        var position = Vector3.Zero;
        var totalWeight = 0f;
        foreach (var attachment in attachments)
        {
            var weight = Math.Max(0f, attachment.Weight);
            if (weight <= 0f)
                continue;
            position += (_inputBoneDeltas.TryGetValue(attachment.BoneIndex, out var delta)
                ? Vector3.Transform(particle.RestPosition, delta)
                : particle.RestPosition) * weight;
            totalWeight += weight;
        }
        return totalWeight > 0.000001f ? position / totalWeight : particle.RestPosition;
    }

    private SimulatedBonePreviewPose GetInputBonePose(int boneIndex, SimulatedBonePreviewPose rest)
    {
        if (!_inputBoneDeltas.TryGetValue(boneIndex, out var delta))
            return new SimulatedBonePreviewPose
            {
                Position = rest.Position,
                AxisX = rest.AxisX,
                AxisY = rest.AxisY,
                AxisZ = rest.AxisZ,
                StretchScale = 1f
            };

        return new SimulatedBonePreviewPose
        {
            Position = Vector3.Transform(rest.Position, delta),
            AxisX = NormalizeOrFallback(Vector3.TransformNormal(rest.AxisX, delta), rest.AxisX),
            AxisY = NormalizeOrFallback(Vector3.TransformNormal(rest.AxisY, delta), rest.AxisY),
            AxisZ = NormalizeOrFallback(Vector3.TransformNormal(rest.AxisZ, delta), rest.AxisZ),
            StretchScale = 1f
        };
    }

    private ColliderPreviewShape GetAnimatedCollider(ColliderPreviewShape source)
    {
        if (!_inputBoneDeltas.TryGetValue(source.BoneIndex, out var delta))
            return source;

        return new ColliderPreviewShape
        {
            Index = source.Index,
            CollisionBit = source.CollisionBit,
            Name = source.Name,
            BoneIndex = source.BoneIndex,
            Start = Vector3.Transform(source.Start, delta),
            End = Vector3.Transform(source.End, delta),
            Radius = source.Radius,
            EndRadius = source.EndRadius,
            PlaneNormal = NormalizeOrFallback(
                Vector3.TransformNormal(source.PlaneNormal, delta),
                source.PlaneNormal),
            Kind = source.Kind
        };
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

    private static void ApplyConstraintCorrection(SimParticle particle, Vector3 correction)
    {
        particle.Position += correction;
        // Retaining most of the pre-projection velocity makes a preview feel
        // lively, while moving PreviousPosition partway prevents a correction
        // from becoming a one-frame launch on the following Verlet step.
        particle.PreviousPosition += correction * 0.8f;
    }

    private void StabilizeParticle(SimParticle particle)
    {
        if (!IsFinite(particle.Position) || !IsFinite(particle.PreviousPosition) ||
            Vector3.DistanceSquared(particle.Position, GetParticleReferencePosition(particle)) > MaximumPreviewExcursion * MaximumPreviewExcursion)
        {
            particle.Reset(GetParticleReferencePosition(particle));
        }
    }

    private static Vector3 ClampMagnitude(Vector3 value, float maximumLength)
    {
        var lengthSquared = value.LengthSquared();
        var maximumSquared = maximumLength * maximumLength;
        return lengthSquared > maximumSquared && lengthSquared > 0.0000001f
            ? value * (maximumLength / MathF.Sqrt(lengthSquared))
            : value;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Matrix4x4 value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
        float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
        float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
        float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);

    private void ResolveCollisions(SimParticle particle)
    {
        if (particle.Fixed)
            return;

        // Keep a stable manifold while resolving overlapping body shapes. A
        // resting necklace otherwise alternates between nearly equal capsule
        // normals, which turns tiny penetrations into visible chatter.
        var hadContact = false;
        var contactNormal = Vector3.Zero;
        var contactWeight = 0.0f;
        var preferredCollider = particle.ContactColliderIndex;
        for (var contactPass = 0; contactPass < 4; contactPass++)
        {
            var hasContact = false;
            var deepestContact = default(CollisionContact);
            var hasPreferredContact = false;
            var preferredContact = default(CollisionContact);
            foreach (var collider in _colliders)
            {
                // Bit 31 is Havok's landscape-collision flag, rather than a slot
                // in perInstanceCollidables. Normal static colliders use 0..30.
                if (collider.CollisionBit is < 0 or >= 31)
                    continue;
                if ((particle.CollisionMask & (1u << collider.CollisionBit)) == 0)
                    continue;

                var animatedCollider = GetAnimatedCollider(collider);
                if (!TryGetCollisionContact(particle, animatedCollider, out var contact))
                    continue;
                if (contact.ColliderIndex == preferredCollider)
                {
                    preferredContact = contact;
                    hasPreferredContact = true;
                }
                if (!hasContact || contact.Penetration > deepestContact.Penetration)
                {
                    deepestContact = contact;
                    hasContact = true;
                }
            }
            if (!hasContact)
                break;

            if (hasPreferredContact &&
                preferredContact.Penetration + Math.Max(0.0005f, particle.Radius * 0.05f) >= deepestContact.Penetration)
            {
                deepestContact = preferredContact;
            }

            hadContact = true;
            preferredCollider = deepestContact.ColliderIndex;
            var slop = Math.Clamp(particle.Radius * 0.05f, 0.0001f, 0.0025f);
            var penetration = Math.Max(0.0f, deepestContact.Penetration - slop);
            contactNormal += deepestContact.Normal * Math.Max(penetration, slop);
            contactWeight += Math.Max(penetration, slop);
            if (penetration <= 0.0f)
                break;

            // Deep initial overlap should converge without one collision stage
            // folding an entire triangle. Structural constraints then get a
            // chance to distribute the remaining correction next frame.
            var maximumCorrection = Math.Clamp(particle.Radius * 0.35f, 0.0005f, 0.025f);
            ApplyCollisionCorrection(
                particle,
                deepestContact.Normal * Math.Min(penetration, maximumCorrection));
        }

        if (!hadContact || contactWeight <= 0.0f)
        {
            particle.ClearContact();
            return;
        }

        var resolvedNormal = NormalizeOrFallback(contactNormal, particle.ContactNormal);
        if (particle.HasContact && Vector3.Dot(resolvedNormal, particle.ContactNormal) > 0.0f)
            resolvedNormal = NormalizeOrFallback(Vector3.Lerp(particle.ContactNormal, resolvedNormal, 0.35f), resolvedNormal);
        particle.SetContact(preferredCollider, resolvedNormal);
        ApplyCollisionVelocity(particle, resolvedNormal);
    }

    private static void ApplyCollisionVelocity(SimParticle particle, Vector3 normal)
    {
        var velocity = particle.Position - particle.PreviousPosition;
        var normalSpeed = Vector3.Dot(velocity, normal);
        if (normalSpeed < 0.0f)
            velocity -= normal * normalSpeed;
        var tangent = velocity - normal * Vector3.Dot(velocity, normal);
        velocity -= tangent * particle.Friction;
        particle.PreviousPosition = particle.Position - velocity;
    }

    private static bool TryGetCollisionContact(
        SimParticle particle,
        ColliderPreviewShape collider,
        out CollisionContact contact)
    {
        switch (collider.Kind)
        {
            case ColliderPreviewKind.Sphere:
                return TryGetSphereContact(particle, collider.Start, collider.Radius, collider.Index, out contact);
            case ColliderPreviewKind.Capsule:
            case ColliderPreviewKind.TaperedCapsule:
                return TryGetCapsuleContact(particle, collider, out contact);
            case ColliderPreviewKind.Plane:
                return TryGetPlaneContact(particle, collider, out contact);
            default:
                contact = default;
                return false;
        }
    }

    private static bool TryGetSphereContact(
        SimParticle particle,
        Vector3 center,
        float radius,
        int colliderIndex,
        out CollisionContact contact)
    {
        var offset = particle.Position - center;
        var distance = offset.Length();
        var requiredDistance = Math.Max(0.0f, radius) + particle.Radius;
        if (distance >= requiredDistance)
        {
            contact = default;
            return false;
        }

        var fallback = particle.Position - particle.PreviousPosition;
        var normal = distance > 0.00001f
            ? offset / distance
            : NormalizeOrFallback(fallback, Vector3.UnitY);
        contact = new CollisionContact(normal, requiredDistance - distance, colliderIndex);
        return true;
    }

    private static bool TryGetCapsuleContact(
        SimParticle particle,
        ColliderPreviewShape collider,
        out CollisionContact contact)
    {
        var axis = collider.End - collider.Start;
        var axisLengthSquared = axis.LengthSquared();
        var t = axisLengthSquared > 0.000001f
            ? Math.Clamp(Vector3.Dot(particle.Position - collider.Start, axis) / axisLengthSquared, 0.0f, 1.0f)
            : 0.0f;
        var center = collider.Start + axis * t;
        var radius = collider.Radius + (collider.EndRadius - collider.Radius) * t;
        return TryGetSphereContact(particle, center, radius, collider.Index, out contact);
    }

    private static bool TryGetPlaneContact(
        SimParticle particle,
        ColliderPreviewShape collider,
        out CollisionContact contact)
    {
        var normal = collider.PlaneNormal;
        if (normal.LengthSquared() <= 0.000001f)
        {
            contact = default;
            return false;
        }
        normal = Vector3.Normalize(normal);
        var distance = Vector3.Dot(particle.Position - collider.Start, normal);
        if (distance >= particle.Radius)
        {
            contact = default;
            return false;
        }

        contact = new CollisionContact(normal, particle.Radius - distance, collider.Index);
        return true;
    }

    private static void ApplyCollisionCorrection(SimParticle particle, Vector3 correction)
    {
        // Verlet integration derives velocity from Position - PreviousPosition.
        // A positional collision correction is not a physical impulse; carrying
        // it into PreviousPosition avoids turning initial overlap resolution into
        // an artificial launch on the next simulation frame.
        particle.Position += correction;
        particle.PreviousPosition += correction;
    }

    private readonly record struct CollisionContact(Vector3 Normal, float Penetration, int ColliderIndex);

    private enum BoneBindingFrameKind
    {
        NativeCentroid,
        RigidVertex,
        AffineVertex
    }

    private readonly record struct BoneBindingCalibration(
        Matrix4x4 LocalTransform,
        bool LocalFirst,
        BoneBindingFrameKind FrameKind);

    private sealed class SimParticle
    {
        public SimParticle(ParticlePreviewPoint source)
        {
            Index = source.Index;
            RestPosition = source.Position;
            Position = source.Position;
            PreviousPosition = source.Position;
            Fixed = source.Fixed || source.InverseMass <= 0.0f;
            InverseMass = source.InverseMass;
            Radius = Math.Max(0.0f, source.Radius);
            Friction = Math.Clamp(source.Friction, 0.0f, 1.0f);
            CollisionMask = source.CollisionMask;
        }

        public int Index { get; }
        public Vector3 RestPosition { get; }
        public Vector3 Position { get; set; }
        public Vector3 PreviousPosition { get; set; }
        public bool Fixed { get; }
        public float InverseMass { get; }
        public float Radius { get; }
        public float Friction { get; }
        public uint CollisionMask { get; }
        public float EffectiveInverseMass => Fixed ? 0.0f : Math.Max(InverseMass, 0.0001f);
        public bool HasContact { get; private set; }
        public int ContactColliderIndex { get; private set; } = -1;
        public Vector3 ContactNormal { get; private set; } = Vector3.UnitY;

        public void SetContact(int colliderIndex, Vector3 normal)
        {
            HasContact = true;
            ContactColliderIndex = colliderIndex;
            ContactNormal = normal;
        }

        public void ClearContact()
        {
            HasContact = false;
            ContactColliderIndex = -1;
        }

        public void Reset(Vector3 position)
        {
            Position = position;
            PreviousPosition = position;
            ClearContact();
        }
    }
}
