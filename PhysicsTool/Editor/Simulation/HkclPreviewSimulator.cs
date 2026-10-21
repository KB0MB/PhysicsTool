using System.Numerics;

namespace HKCLTool;

// A lightweight, non-authoritative preview of the HKCL particle solver.
// It intentionally never writes into the HKCL document or editor rows.
public sealed class HkclPreviewSimulator
{
    public const float DefaultWindSpeed = 12.0f;
    // The preview is intentionally conservative. Havok's production solver has
    // additional stabilization stages we do not emulate, so protect the editor
    // from malformed edits or large frame hitches without changing file data.
    private const float MaximumPreviewSpeed = 24.0f;
    private const float MaximumPreviewExcursion = 5.0f;
    private readonly Dictionary<int, SimParticle> _particles;
    private readonly IReadOnlyList<ParticlePreviewLink> _links;
    private readonly IReadOnlyList<ParticlePreviewLocalRange> _localRanges;
    private readonly IReadOnlyList<ParticlePreviewBendStiffness> _bendStiffnessConstraints;
    private readonly ILookup<int, ParticlePreviewLink> _linksBySet;
    private readonly ILookup<int, ParticlePreviewLocalRange> _rangesBySet;
    private readonly ILookup<int, ParticlePreviewBendStiffness> _bendsBySet;
    private readonly IReadOnlyDictionary<int, ParticlePreviewConstraintSet> _constraintSets;
    private readonly IReadOnlyList<int> _constraintExecution;
    private readonly IReadOnlyList<ColliderPreviewShape> _colliders;
    private readonly bool _usePinchContacts;
    private readonly HashSet<int> _pinchParticles;
    private readonly Dictionary<int, ParticlePreviewColliderPinch> _colliderPinching;
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
    private readonly bool _useNativeHkclInputSampling;
    private readonly ClothSolverProfile _profile;
    private readonly Dictionary<int, Vector3> _particleForces = new();
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
    private float _previousIntegrationStep;
    private int _subStepIndex;
    private readonly Dictionary<int, Matrix4x4> _previousInputBoneDeltas = new();
    private bool _hasInputHistory;
    private float _nextRandomWindChange;
    private Vector3 _currentWindDirection = Vector3.UnitX;
    private Vector3 _targetWindDirection = Vector3.UnitX;

    public HkclPreviewSimulator(ParticlePreviewData source)
    {
        _profile = source.SolverProfile;
        _particles = source.Particles.ToDictionary(
            particle => particle.Index,
            particle => new SimParticle(particle));
        _links = source.Links;
        _localRanges = source.LocalRanges;
        _bendStiffnessConstraints = source.BendStiffnessConstraints;
        _linksBySet = _links.ToLookup(link => link.ConstraintSetIndex);
        _rangesBySet = _localRanges.ToLookup(range => range.ConstraintSetIndex);
        _bendsBySet = _bendStiffnessConstraints.ToLookup(bend => bend.ConstraintSetIndex);
        _constraintSets = source.ConstraintSets.ToDictionary(set => set.Index);
        _constraintExecution = source.ConstraintExecution.Count > 0
            ? source.ConstraintExecution.ToArray()
            : source.ConstraintSets.Select(set => set.Index).Append(-1).ToArray();
        // The viewport animates its displayed collider objects in place. Keep
        // an immutable solver-side copy so those display updates cannot become
        // the next frame's collision bind pose.
        _colliders = source.Colliders.Select(CloneCollider).ToArray();
        _pinchParticles = new(source.PinchEnabledParticles);
        _colliderPinching = source.ColliderPinching.ToDictionary(value => value.CollisionBit);
        // Retain ordinary contacts when a native shape/anti-pinch stage is not
        // implemented; do not discard tied contacts without its recovery stage.
        _usePinchContacts = source.PinchDetectionEnabled && !source.HasAntiPinchConstraints &&
            _colliders.Count(IsPinchCollider) > 1 &&
            _colliders.Where(IsPinchCollider).All(c => c.Kind is ColliderPreviewKind.Sphere or ColliderPreviewKind.Capsule);
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
        if (_profile == ClothSolverProfile.TotkExperimental)
        {
            // MoveParticles supplies fixed inputs only. Free particles also need
            // their authored skin weights when resetting/seeking the preview.
            // Keep LocalRange's original reference vertices intact for solving.
            foreach (var range in source.LocalRanges)
            {
                if (_particleAttachments.ContainsKey(range.ParticleIndex) ||
                    !_particles.TryGetValue(range.ParticleIndex, out var particle) || particle.Fixed) continue;
                var weights = range.ReferenceBoneWeights.Where(w => float.IsFinite(w.Weight) && w.Weight > 0 && w.ReferencePosition.HasValue).ToArray();
                float total = weights.Sum(w => w.Weight);
                if (total <= 0.000001f) continue;
                var reference = weights.Aggregate(Vector3.Zero, (p, w) => p + w.ReferencePosition!.Value * (w.Weight / total));
                var offset = particle.RestPosition - reference;
                _particleAttachments.Add(particle.Index, weights.Select(w => new ParticlePreviewBoneWeight
                {
                    BoneIndex = w.BoneIndex, Weight = w.Weight,
                    ReferencePosition = w.ReferencePosition!.Value + offset
                }).ToArray());
            }
        }
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
        _useNativeHkclInputSampling = source.UseNativeHkclInputSampling || _profile == ClothSolverProfile.TotkExperimental;
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
        NumericalResetCount = 0;
        _particleForces.Clear();
        _previousIntegrationStep = 0.0f;
        RememberInputPose();
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
        // Collision masks address slots, which may reference the same native
        // collider repeatedly. Only the viewport pose map is keyed by unique
        // collider ID; keep all slots in _colliders for solving and pinching.
        return _colliders.DistinctBy(collider => collider.Index).ToDictionary(
            collider => collider.Index,
            collider => GetAnimatedCollider(collider));
    }

    public bool WindEnabled { get; set; }
    public bool RandomWindDirections { get; set; } = true;
    public Vector3 WindDirection { get; set; } = Vector3.UnitX;
    public float WindSpeed { get; set; } = DefaultWindSpeed;
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
    public int NumericalResetCount { get; private set; }
    public ClothSolverProfile Profile => _profile;

    // One-frame force accumulator matching TotK's action buffer, before inverse mass.
    public void SetParticleForces(IReadOnlyDictionary<int, Vector3> forces)
    {
        if (_profile != ClothSolverProfile.TotkExperimental)
            throw new NotSupportedException("Native action forces are available in the TotK runtime only.");
        if (forces.Any(pair => !_particles.ContainsKey(pair.Key) || !IsFinite(pair.Value)))
            throw new ArgumentException("Forces must reference existing particles and be finite.", nameof(forces));
        _particleForces.Clear();
        foreach (var pair in forces) _particleForces.Add(pair.Key, pair.Value);
    }

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

    private void RememberInputPose()
    {
        _previousInputBoneDeltas.Clear();
        foreach (var entry in _inputBoneDeltas) _previousInputBoneDeltas.Add(entry.Key, entry.Value);
        _hasInputHistory = true;
    }

    // A preview loop is a timeline discontinuity, not a physical impulse.
    // Carry deformation and velocity into the new attachment pose together.
    public void RebaseInputPose()
    {
        if (!_hasInputHistory) { RememberInputPose(); return; }
        Matrix4x4 Change(int index)
        {
            var previous = _previousInputBoneDeltas.GetValueOrDefault(index, Matrix4x4.Identity);
            return Matrix4x4.Invert(previous, out var inverse)
                ? inverse * _inputBoneDeltas.GetValueOrDefault(index, Matrix4x4.Identity) : Matrix4x4.Identity;
        }
        foreach (var particle in _particles.Values)
        {
            if (particle.Fixed) { particle.Reset(GetParticleReferencePosition(particle)); continue; }
            if (!_particleAttachments.TryGetValue(particle.Index, out var weights))
                weights = _particleAttachments.Where(a => _particles.ContainsKey(a.Key))
                    .OrderBy(a => Vector3.DistanceSquared(_particles[a.Key].RestPosition, particle.RestPosition))
                    .Select(a => a.Value).FirstOrDefault();
            if (weights == null) continue;
            var position = Vector3.Zero;
            var previousPosition = Vector3.Zero;
            var total = 0f;
            foreach (var weight in weights.Where(w => float.IsFinite(w.Weight) && w.Weight > 0))
            {
                var change = Change(weight.BoneIndex);
                position += Vector3.Transform(particle.Position, change) * weight.Weight;
                previousPosition += Vector3.Transform(particle.PreviousPosition, change) * weight.Weight;
                total += weight.Weight;
            }
            if (total > 0 && IsFinite(position) && IsFinite(previousPosition))
            {
                particle.Position = position / total;
                particle.PreviousPosition = previousPosition / total;
            }
        }
        foreach (var binding in _previousBindingFrames.Keys.ToArray())
            _previousBindingFrames[binding] *= Change(binding.BoneIndex);
        foreach (var index in _previousDrivenBonePoses.Keys.ToArray())
        {
            var pose = _previousDrivenBonePoses[index];
            var change = Change(index);
            pose.Position = Vector3.Transform(pose.Position, change);
            pose.AxisX = NormalizeOrFallback(Vector3.TransformNormal(pose.AxisX, change), pose.AxisX);
            pose.AxisY = NormalizeOrFallback(Vector3.TransformNormal(pose.AxisY, change), pose.AxisY);
            pose.AxisZ = NormalizeOrFallback(Vector3.TransformNormal(pose.AxisZ, change), pose.AxisZ);
        }
        _previousTransferMotionDelta = _inputBoneDeltas.GetValueOrDefault(_transferMotionTransformIndex, Matrix4x4.Identity);
        _hasPreviousTransferMotionDelta = true;
        RememberInputPose();
    }

    private void SampleInputPose(IReadOnlyDictionary<int, Matrix4x4> target, float fraction)
    {
        _inputBoneDeltas.Clear();
        foreach (var index in target.Keys.Union(_previousInputBoneDeltas.Keys))
        {
            var start = _previousInputBoneDeltas.GetValueOrDefault(index, Matrix4x4.Identity);
            var end = target.GetValueOrDefault(index, Matrix4x4.Identity);
            if (fraction >= 1f) _inputBoneDeltas[index] = end;
            else if (Matrix4x4.Decompose(start, out var s0, out var r0, out var t0) &&
                     Matrix4x4.Decompose(end, out var s1, out var r1, out var t1))
                _inputBoneDeltas[index] = Matrix4x4.CreateScale(Vector3.Lerp(s0, s1, fraction)) *
                    Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(r0, r1, fraction)) *
                    Matrix4x4.CreateTranslation(Vector3.Lerp(t0, t1, fraction));
            else _inputBoneDeltas[index] = end;
        }
    }

    private Dictionary<ColliderPreviewShape, ColliderMotion> BuildColliderMotions(IReadOnlyDictionary<int, Matrix4x4> target, float dt)
    {
        var result = new Dictionary<ColliderPreviewShape, ColliderMotion>();
        foreach (var collider in _colliders)
        {
            var start = _previousInputBoneDeltas.GetValueOrDefault(collider.BoneIndex, Matrix4x4.Identity);
            var end = target.GetValueOrDefault(collider.BoneIndex, Matrix4x4.Identity);
            var pivot = collider.TransformOrigin ?? (_restBonePoses.TryGetValue(collider.BoneIndex, out var bone) ? bone.Position : collider.Start);
            var startPivot = Vector3.Transform(pivot, start);
            var endPivot = Vector3.Transform(pivot, end);
            var velocity = (endPivot - startPivot) / dt;
            var angular = Vector3.Zero;
            if (Matrix4x4.Decompose(start, out _, out var r0, out _) && Matrix4x4.Decompose(end, out _, out var r1, out _))
            {
                var rotation = Quaternion.Normalize(r1 * Quaternion.Inverse(r0));
                if (rotation.W < 0f) rotation = -rotation;
                var xyz = new Vector3(rotation.X, rotation.Y, rotation.Z);
                var sine = xyz.Length();
                if (sine > 1e-8f) angular = xyz * (2f * MathF.Atan2(sine, rotation.W) / (sine * dt));
            }
            result[collider] = new ColliderMotion(pivot, startPivot, endPivot, velocity, angular);
        }
        return result;
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
        // BotW's simple deformer consumes the authored matrix in a centroid
        // triangle frame. Rest-pose fitting must not replace this mapping.
        if (binding.UseNativeSimpleDeformer)
            return new BoneBindingCalibration(binding.LocalBoneTransform, true, BoneBindingFrameKind.NativeCentroid);

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
        // Switch 0x7101e7dea0 retains Z, then reconstructs Y from Z cross X.
        // BotW has no boneAxis field; the old default dynamically switched axes.
        world = binding.UseNativeSimpleDeformer
            ? ReconstructNativeSimpleBoneMatrix(world, fallbackPose)
            : OrthogonalizeBoneMatrix(world, binding.BoneAxis, fallbackPose);
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

    private static Matrix4x4 ReconstructNativeSimpleBoneMatrix(Matrix4x4 world, SimulatedBonePreviewPose fallback)
    {
        var rawZ = new Vector3(world.M31, world.M32, world.M33);
        var rawX = new Vector3(world.M11, world.M12, world.M13);
        var rawY = Vector3.Cross(rawZ, rawX);
        var rebuiltX = Vector3.Cross(rawY, rawZ);
        // Cross-product signs are authored orientation, not quaternion signs.
        // Flipping them toward the previous pose introduces a 180-degree error.
        if (!TryNormalize(rawZ, out var z) || !TryNormalize(rawY, out var y) || !TryNormalize(rebuiltX, out var x))
        {
            var previous = ToMatrix(fallback);
            previous.Translation = world.Translation;
            return previous;
        }
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0,
            world.M41, world.M42, world.M43, 1);

        static bool TryNormalize(Vector3 value, out Vector3 normalized)
        {
            var squared = value.LengthSquared();
            normalized = default;
            if (!float.IsFinite(squared) || squared <= 1e-30f) return false;
            normalized = value / MathF.Sqrt(squared);
            return true;
        }
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
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
            return;
        var step = Math.Min(deltaSeconds, 1.0f / 30.0f);
        var subSteps = _authoredSubSteps;
        var constraintIterations = _authoredSolveIterations * Math.Clamp(SolverIterations, 1, 24);
        var subStep = step / subSteps;
        _windTime += step;
        ApplyTransferMotion(step);
        var frameInput = new Dictionary<int, Matrix4x4>(_inputBoneDeltas);
        if (!_hasInputHistory) RememberInputPose();
        var motions = BuildColliderMotions(frameInput, step);
        // BotW 0x7101e10dd0: fixed particles interpolate between evaluated
        // world positions. Reference buffers remain at the frame input pose.
        var fixedInputs = _useNativeHkclInputSampling
            ? _particles.Values.Where(p => p.Fixed).ToDictionary(p => p.Index,
                p => (Start: p.Position, End: GetParticleReferencePosition(p))) : null;
        var rangeInputs = _useNativeHkclInputSampling
            ? _localRanges.Where(r => _particles.ContainsKey(r.ParticleIndex)).Distinct()
                .ToDictionary(r => r, r => GetLocalRangeReferencePosition(r, _particles[r.ParticleIndex])) : null;

        for (var subStepIndex = 0; subStepIndex < subSteps; subStepIndex++)
        {
            _subStepIndex = subStepIndex;
            SampleInputPose(frameInput, (subStepIndex + 1f) / subSteps);
            var colliders = _colliders.Select(source => new MovingCollider(
                GetSubstepCollider(source, motions[source], (subStepIndex + 1f) / subSteps), motions[source])).ToArray();
            var windDirection = GetWindDirection(subStep);
            var gust = 1.0f + MathF.Sin(_windTime * 1.8f) * Math.Clamp(WindGustiness, 0.0f, 1.0f);
            var wind = WindEnabled ? windDirection * Math.Max(0.0f, WindSpeed) * gust : Vector3.Zero;
            Integrate(subStep, wind, fixedInputs, (subStepIndex + 1f) / subSteps);
            for (var iteration = 0; iteration < constraintIterations; iteration++)
            {
                foreach (var executionEntry in _constraintExecution)
                {
                    if (executionEntry < 0)
                    {
                        ResolveAllCollisions(colliders, subStep);
                        continue;
                    }

                    SolveConstraintSet(executionEntry, rangeInputs);
                }
            }

            foreach (var particle in _particles.Values)
                StabilizeParticle(particle);
        }
        _inputBoneDeltas.Clear();
        foreach (var entry in frameInput) _inputBoneDeltas.Add(entry.Key, entry.Value);
        RememberInputPose();
        _particleForces.Clear();
    }

    private void SolveConstraintSet(int constraintSetIndex,
        IReadOnlyDictionary<ParticlePreviewLocalRange, Vector3>? rangeInputs)
    {
        var kind = _constraintSets.TryGetValue(constraintSetIndex, out var set)
            ? set.Kind
            : string.Empty;
        if (kind.Contains("Transition", StringComparison.Ordinal))
            return;
        if (kind.Contains("LocalRange", StringComparison.Ordinal))
        {
            SolveLocalRanges(constraintSetIndex, rangeInputs);
            return;
        }
        if (kind.Contains("BendStiffness", StringComparison.Ordinal))
        {
            SolveBendStiffness(constraintSetIndex);
            return;
        }

        foreach (var link in _linksBySet[constraintSetIndex])
            SolveLink(link, kind);
    }

    private void ResolveAllCollisions(IReadOnlyList<MovingCollider> colliders, float dt)
    {
        foreach (var particle in _particles.Values)
            ResolveCollisions(particle, colliders, dt);
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
        var dot = Math.Clamp(Vector3.Dot(_currentWindDirection, _targetWindDirection), -1f, 1f);
        if (dot > .9999f) _currentWindDirection = _targetWindDirection;
        else
        {
            var axis = Vector3.Cross(_currentWindDirection, _targetWindDirection);
            // Normalized linear blending cannot leave an exactly opposite direction.
            if (axis.LengthSquared() < .000001f)
                axis = Vector3.Cross(_currentWindDirection, MathF.Abs(_currentWindDirection.Y) < .9f ? Vector3.UnitY : Vector3.UnitX);
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(dot) * blend);
            _currentWindDirection = Vector3.Normalize(Vector3.Transform(_currentWindDirection, rotation));
        }
        return _currentWindDirection;
    }

    private void Integrate(float deltaSeconds, Vector3 wind,
        IReadOnlyDictionary<int, (Vector3 Start, Vector3 End)>? fixedInputs, float fraction)
    {
        // BotW hclSimulateOperator stores a fraction lost per second, not a rate.
        var damping = _dampingPerSecond >= 1f ? 0f : MathF.Pow(1f - _dampingPerSecond, deltaSeconds);
        var timeRatio = _previousIntegrationStep > 0.0f ? deltaSeconds / _previousIntegrationStep : 1.0f;
        _previousIntegrationStep = deltaSeconds;
        foreach (var particle in _particles.Values)
        {
            if (particle.Fixed)
            {
                if (fixedInputs != null && fixedInputs.TryGetValue(particle.Index, out var input))
                    particle.Reset(Vector3.Lerp(input.Start, input.End, fraction));
                else
                {
                    particle.PreviousPosition = particle.Position;
                    particle.Position = GetParticleReferencePosition(particle);
                }
                continue;
            }

            var velocity = ClampMagnitude(
                (particle.Position - particle.PreviousPosition) * (damping * timeRatio),
                MaximumPreviewSpeed * deltaSeconds);
            particle.PreviousPosition = particle.Position;
            // TotK 710059aee0 integrates (actionForce + gravity * mass) * inverseMass.
            // Preview wind remains an acceleration, not a reconstructed game action.
            var gravityWeight = _profile == ClothSolverProfile.TotkExperimental
                ? particle.Mass * particle.EffectiveInverseMass : 1f;
            var forceAcceleration = _particleForces.GetValueOrDefault(particle.Index) * particle.EffectiveInverseMass;
            particle.Position += velocity + (_gravity * (Math.Max(0.0f, GravityScale) * gravityWeight) + forceAcceleration + wind) * (deltaSeconds * deltaSeconds);
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
        if (constraintKind.Contains("CompressibleLink", StringComparison.Ordinal))
        {
            // TotK 71003dd6c0: a free interval, then the same mass-scaled
            // endpoint correction as StandardLink. Compression wins if limits overlap.
            if (link.CompressionLength is { } minimum && length < minimum)
                target = minimum;
            else if (!target.HasValue || length <= target.Value)
                return;
        }
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

        var correction = offset / length * (length - target.Value);
        var weightA = a.EffectiveInverseMass;
        var weightB = b.EffectiveInverseMass;
        var adaptation = GetConstraintScale(constraintKind);

        // Native StretchLink is directed: A is the anchor and only B moves.
        if (constraintKind.Contains("StretchLink", StringComparison.Ordinal))
        {
            if (!b.Fixed) ApplyConstraintCorrection(b, -correction * (strength * adaptation));
            return;
        }

        // Standard and bend links store a mass-scaled solver coefficient, not
        // a normalized spring strength. Havok's optimized StandardLink form
        // expands this into stiffnessA/stiffnessB, one value per endpoint.
        // Applying inverse mass a second time through a normalized pair weight
        // makes large cloths far too soft and also incorrectly clips valid
        // coefficients above one.
        var usesMassScaledCoefficient =
            constraintKind.Contains("StandardLink", StringComparison.Ordinal) ||
            constraintKind.Contains("CompressibleLink", StringComparison.Ordinal) ||
            constraintKind.Contains("BendLink", StringComparison.Ordinal);
        if (usesMassScaledCoefficient)
        {
            var coefficient = Math.Max(0.0f, strength);
            if (weightA > 0.0f)
            {
                var stiffnessA = coefficient * weightA * adaptation;
                ApplyConstraintCorrection(a, correction * stiffnessA);
            }
            if (weightB > 0.0f)
            {
                var stiffnessB = coefficient * weightB * adaptation;
                ApplyConstraintCorrection(b, -correction * stiffnessB);
            }
            return;
        }

        var weightTotal = weightA + weightB;
        if (weightTotal <= 0.0f)
            return;
        strength *= adaptation;
        correction *= Math.Clamp(strength, 0.0f, 1.0f);
        if (weightA > 0.0f)
            ApplyConstraintCorrection(a, correction * (weightA / weightTotal));
        if (weightB > 0.0f)
            ApplyConstraintCorrection(b, -correction * (weightB / weightTotal));
    }

    private void SolveLocalRanges(int constraintSetIndex,
        IReadOnlyDictionary<ParticlePreviewLocalRange, Vector3>? rangeInputs)
    {
        foreach (var range in _rangesBySet[constraintSetIndex])
        {
            if (!_particles.TryGetValue(range.ParticleIndex, out var particle) || particle.Fixed)
                continue;

            var maximumDistance = Math.Max(0.0f, range.MaximumDistance);
            var referencePosition = rangeInputs != null && rangeInputs.TryGetValue(range, out var input)
                ? input : GetLocalRangeReferencePosition(range, particle);
            var offset = particle.Position - referencePosition;
            var distance = offset.Length();
            if (distance <= maximumDistance || distance <= 0.000001f)
                continue;

            var correction = (referencePosition + offset * (maximumDistance / distance) - particle.Position) *
                (range.Stiffness * GetConstraintScale("LocalRange"));
            ApplyConstraintCorrection(particle, correction);
            // Native adaptive local-range projection preserves velocity.
            if (_adaptConstraintStiffness && _authoredSubSteps > 1)
                particle.PreviousPosition += correction;
        }
    }

    private void SolveBendStiffness(int constraintSetIndex)
    {
        foreach (var bend in _bendsBySet[constraintSetIndex])
        {
            if (!_particles.TryGetValue(bend.ParticleA, out var a) ||
                !_particles.TryGetValue(bend.ParticleB, out var b) ||
                !_particles.TryGetValue(bend.ParticleC, out var c) ||
                !_particles.TryGetValue(bend.ParticleD, out var d))
            {
                continue;
            }

            // Native curvature is evaluated in the current triangle frame.
            // A world-space rest vector would incorrectly resist rigid rotation.
            var error =
                a.Position * bend.WeightA + b.Position * bend.WeightB +
                c.Position * bend.WeightC + d.Position * bend.WeightD;
            if (bend.UseRestPoseConfig && bend.RestCurvature != 0f)
            {
                var edge = d.Position - c.Position;
                var n1 = Vector3.Cross(edge, a.Position - c.Position);
                var n2 = Vector3.Cross(b.Position - c.Position, edge);
                var area1 = n1.Length();
                var area2 = n2.Length();
                if (area1 > 1e-10f && area2 > 1e-10f && edge.LengthSquared() > 1e-12f)
                {
                    var normal = NormalizeOrFallback(n1 / area1 + n2 / area2, Vector3.Zero);
                    var height = area1 * area2 / edge.LengthSquared() * bend.RestCurvature;
                    // TotK's rest-pose kernel disables this correction above the
                    // authored squared-height limit. BotW has no equivalent flag.
                    if (bend.ClampBendStiffness && height * height > bend.MaxRestPoseHeightSq)
                        continue;
                    error += normal * height;
                }
            }
            if (!IsFinite(error) || error.LengthSquared() <= 0.000000000001f)
                continue;

            var coefficient = bend.BendStiffness * GetConstraintScale("BendStiffness");
            if (!float.IsFinite(coefficient) || coefficient == 0.0f)
                continue;

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
        ApplyConstraintCorrection(particle, correction);
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
            var reference = attachment.ReferencePosition ?? particle.RestPosition;
            position += (_inputBoneDeltas.TryGetValue(attachment.BoneIndex, out var delta)
                ? Vector3.Transform(reference, delta)
                : reference) * weight;
            totalWeight += weight;
        }
        return totalWeight > 0.000001f ? position / totalWeight : particle.RestPosition;
    }

    private float GetConstraintScale(string kind)
    {
        if (!_adaptConstraintStiffness || _authoredSubSteps == 1) return 1f;
        // BotW 7101db3d68, with runtime time/stiffness scales at their defaults.
        if (kind.Contains("LocalRange", StringComparison.Ordinal))
            return _subStepIndex == _authoredSubSteps - 1 ? 1f : 0f;
        return MathF.Pow(_authoredSubSteps, -1.725f);
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
            var reference = attachment.ReferencePosition ?? particle.RestPosition;
            position += (_inputBoneDeltas.TryGetValue(attachment.BoneIndex, out var delta)
                ? Vector3.Transform(reference, delta)
                : reference) * weight;
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

    private ColliderPreviewShape GetSubstepCollider(ColliderPreviewShape source, ColliderMotion motion, float fraction)
    {
        var animated = GetAnimatedCollider(source);
        if (animated.TransformOrigin is not { } origin) return animated;
        // Native 0x7101da3570 measures velocity at the collider origin; the
        // simulate loop translates that origin linearly, not around the bone.
        var pivot = Vector3.Lerp(motion.StartPivot, motion.EndPivot, fraction);
        var sampled = CloneCollider(animated);
        sampled.Start += pivot - origin;
        sampled.End += pivot - origin;
        sampled.TransformOrigin = pivot;
        return sampled;
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
            TransformOrigin = source.TransformOrigin is { } origin ? Vector3.Transform(origin, delta) : null,
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
        TransformOrigin = source.TransformOrigin,
        End = source.End,
        Radius = source.Radius,
        EndRadius = source.EndRadius,
        PlaneNormal = source.PlaneNormal,
        Kind = source.Kind
    };

    private static void ApplyConstraintCorrection(SimParticle particle, Vector3 correction)
    {
        if (IsFinite(correction)) particle.Position += correction;
    }

    private void StabilizeParticle(SimParticle particle)
    {
        if (!IsFinite(particle.Position) || !IsFinite(particle.PreviousPosition) ||
            Vector3.DistanceSquared(particle.Position, GetParticleReferencePosition(particle)) > MaximumPreviewExcursion * MaximumPreviewExcursion)
        {
            NumericalResetCount++;
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

    private void ResolveCollisions(SimParticle particle, IReadOnlyList<MovingCollider> colliders, float dt)
    {
        if (particle.Fixed) return;
        if (!_usePinchContacts)
        {
            foreach (var moving in colliders) ResolveOrdinaryContact(particle, moving, dt);
            return;
        }

        // BotW 7101e163bc/7101e57898: collect pinch-enabled colliders first.
        // Equal priorities invalidate the cached choice, not average normals.
        var priority = -1;
        var valid = false;
        var selected = default(MovingCollider);
        var selectedContact = default(CollisionContact);
        foreach (var moving in colliders)
        {
            if (!IsPinchCollider(moving.Shape)) continue;
            if (!_pinchParticles.Contains(particle.Index))
            {
                ResolveOrdinaryContact(particle, moving, dt);
                continue;
            }
            var settings = _colliderPinching[moving.Shape.CollisionBit];
            if (!CanCollide(particle, moving.Shape) ||
                !TryGetCollisionContact(particle, moving.Shape, out var contact, Math.Max(0f, settings.Radius))) continue;
            if (settings.Priority > priority)
            {
                priority = settings.Priority;
                valid = contact.Penetration > 0f;
                selected = moving;
                selectedContact = contact;
            }
            else if (settings.Priority == priority) valid = false;
        }
        if (valid) ApplyContact(particle, selected, selectedContact, dt);
        foreach (var moving in colliders)
            if (!IsPinchCollider(moving.Shape)) ResolveOrdinaryContact(particle, moving, dt);
    }

    private bool IsPinchCollider(ColliderPreviewShape collider) =>
        _colliderPinching.TryGetValue(collider.CollisionBit, out var settings) && settings.Enabled;

    private static bool CanCollide(SimParticle particle, ColliderPreviewShape collider) =>
        collider.CollisionBit is >= 0 and < 31 && (particle.CollisionMask & (1u << collider.CollisionBit)) != 0;

    private void ResolveOrdinaryContact(SimParticle particle, MovingCollider moving, float dt)
    {
        if (CanCollide(particle, moving.Shape) && TryGetCollisionContact(particle, moving.Shape, out var contact))
            ApplyContact(particle, moving, contact, dt);
    }

    private void ApplyContact(SimParticle particle, MovingCollider moving, CollisionContact contact, float dt)
    {
        particle.Position += contact.Normal * contact.Penetration;
        var surface = particle.Position - contact.Normal * particle.Radius;
        var pivot = moving.Shape.TransformOrigin ?? (_inputBoneDeltas.TryGetValue(moving.Shape.BoneIndex, out var delta)
            ? Vector3.Transform(moving.Motion.RestPivot, delta) : moving.Motion.RestPivot);
        var surfaceVelocity = moving.Motion.LinearVelocity + Vector3.Cross(moving.Motion.AngularVelocity, surface - pivot);
        var relativeMotion = particle.Position - particle.PreviousPosition - surfaceVelocity * dt;
        var tangent = relativeMotion - contact.Normal * Vector3.Dot(relativeMotion, contact.Normal);
        particle.PreviousPosition += tangent * particle.Friction;
    }

    private static bool TryGetCollisionContact(
        SimParticle particle,
        ColliderPreviewShape collider,
        out CollisionContact contact,
        float detectionMargin = 0f)
    {
        switch (collider.Kind)
        {
            case ColliderPreviewKind.Sphere:
                return TryGetSphereContact(particle, collider.Start, collider.Radius, collider.Index, out contact, detectionMargin);
            case ColliderPreviewKind.Capsule:
            case ColliderPreviewKind.TaperedCapsule:
                return TryGetCapsuleContact(particle, collider, out contact, detectionMargin);
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
        out CollisionContact contact,
        float detectionMargin = 0f)
    {
        var offset = particle.Position - center;
        var distance = offset.Length();
        var requiredDistance = Math.Max(0.0f, radius) + particle.Radius;
        if (distance >= requiredDistance + detectionMargin)
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
        out CollisionContact contact,
        float detectionMargin = 0f)
    {
        if (collider.Kind == ColliderPreviewKind.TaperedCapsule)
        {
            var distance = CapsuleProfile.Create(collider).SignedDistance(particle.Position,
                particle.Position - particle.PreviousPosition, out var normal);
            if (distance >= particle.Radius + detectionMargin)
            {
                contact = default;
                return false;
            }
            contact = new CollisionContact(normal, particle.Radius - distance, collider.Index);
            return true;
        }
        var axis = collider.End - collider.Start;
        var axisLengthSquared = axis.LengthSquared();
        var t = axisLengthSquared > 0.000001f
            ? Math.Clamp(Vector3.Dot(particle.Position - collider.Start, axis) / axisLengthSquared, 0.0f, 1.0f)
            : 0.0f;
        var center = collider.Start + axis * t;
        // BotW hclCapsuleShape (main: 7101d9efa0) has one radius.
        return TryGetSphereContact(particle, center, collider.Radius, collider.Index, out contact, detectionMargin);
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

    private readonly record struct ColliderMotion(Vector3 RestPivot, Vector3 StartPivot, Vector3 EndPivot,
        Vector3 LinearVelocity, Vector3 AngularVelocity);
    private readonly record struct MovingCollider(ColliderPreviewShape Shape, ColliderMotion Motion);
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
            Mass = source.Mass;
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
        public float Mass { get; }
        public float Radius { get; }
        public float Friction { get; }
        public uint CollisionMask { get; }
        public float EffectiveInverseMass => Fixed ? 0.0f : Math.Max(InverseMass, 0f);

        public void Reset(Vector3 position)
        {
            Position = position;
            PreviousPosition = position;
        }
    }
}
