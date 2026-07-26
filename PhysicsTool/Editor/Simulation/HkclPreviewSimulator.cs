using System.Numerics;

namespace HKCLTool;

// A lightweight, non-authoritative preview of the HKCL particle solver.
// It intentionally never writes into the HKCL document or editor rows.
public sealed class HkclPreviewSimulator
{
    private readonly Dictionary<int, SimParticle> _particles;
    private readonly IReadOnlyList<ParticlePreviewLink> _links;
    private readonly IReadOnlyList<ParticlePreviewLocalRange> _localRanges;
    private readonly IReadOnlyList<ColliderPreviewShape> _colliders;
    private readonly IReadOnlyList<ParticlePreviewBoneBinding> _boneBindings;
    private readonly Dictionary<int, SimulatedBonePreviewPose> _restBonePoses;
    private readonly Dictionary<int, int> _boneParents;
    private readonly HashSet<int> _triangleDrivenBones;
    private readonly Dictionary<int, (int ParticleA, int ParticleB, float PositionFactor)> _boneParticleAxes;
    private readonly Vector3 _gravity;
    private readonly float _dampingPerSecond;
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
        _colliders = source.Colliders;
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
        _boneParents = source.Bones.ToDictionary(bone => bone.Index, bone => bone.ParentIndex);
        _triangleDrivenBones = _boneBindings.Select(binding => binding.BoneIndex).ToHashSet();
        _boneParticleAxes = BuildBoneParticleAxes();
        _gravity = source.Gravity;
        _dampingPerSecond = Math.Max(0.0f, source.DampingPerSecond);
    }

    public void Reset()
    {
        foreach (var particle in _particles.Values)
            particle.Reset();
        _windTime = 0.0f;
        _nextRandomWindChange = 0.0f;
        _currentWindDirection = Vector3.UnitX;
        _targetWindDirection = Vector3.UnitX;
    }

    public IReadOnlyDictionary<int, Vector3> GetPositions()
    {
        return _particles.ToDictionary(entry => entry.Key, entry => entry.Value.Position);
    }

    public bool WindEnabled { get; set; }
    public bool RandomWindDirections { get; set; } = true;
    public Vector3 WindDirection { get; set; } = Vector3.UnitX;
    public float WindSpeed { get; set; } = 2.2f;
    public float WindGustiness { get; set; } = 0.35f;
    public float GravityScale { get; set; } = 1.0f;
    public int SolverIterations { get; set; } = 7;

    public IReadOnlyDictionary<int, SimulatedBonePreviewPose> GetBonePoses()
    {
        var accumulatedAxes = new Dictionary<int, (Vector3 AxisX, Vector3 AxisY, Vector3 AxisZ, int Count)>();
        foreach (var binding in _boneBindings)
        {
            if (!_restBonePoses.TryGetValue(binding.BoneIndex, out var restBone) ||
                !_particles.TryGetValue(binding.ParticleA, out var a) ||
                !_particles.TryGetValue(binding.ParticleB, out var b) ||
                !_particles.TryGetValue(binding.ParticleC, out var c))
            {
                continue;
            }

            var axisX = restBone.AxisX;
            var axisY = restBone.AxisY;
            var axisZ = restBone.AxisZ;
            if (TryBuildTriangleFrame(a.RestPosition, b.RestPosition, c.RestPosition, out var restX, out var restY, out var restZ) &&
                TryBuildTriangleFrame(a.Position, b.Position, c.Position, out var currentX, out var currentY, out var currentZ))
            {
                axisX = RotateFromTriangleFrame(restBone.AxisX, restX, restY, restZ, currentX, currentY, currentZ);
                axisY = RotateFromTriangleFrame(restBone.AxisY, restX, restY, restZ, currentX, currentY, currentZ);
                axisZ = RotateFromTriangleFrame(restBone.AxisZ, restX, restY, restZ, currentX, currentY, currentZ);
            }

            if (accumulatedAxes.TryGetValue(binding.BoneIndex, out var previous))
            {
                accumulatedAxes[binding.BoneIndex] = (
                    previous.AxisX + axisX,
                    previous.AxisY + axisY,
                    previous.AxisZ + axisZ,
                    previous.Count + 1);
            }
            else
            {
                accumulatedAxes.Add(binding.BoneIndex, (axisX, axisY, axisZ, 1));
            }
        }

        var poses = _restBonePoses.ToDictionary(entry => entry.Key, entry =>
        {
            if (!accumulatedAxes.TryGetValue(entry.Key, out var pose))
            {
                return new SimulatedBonePreviewPose
                {
                    Position = entry.Value.Position,
                    AxisX = entry.Value.AxisX,
                    AxisY = entry.Value.AxisY,
                    AxisZ = entry.Value.AxisZ,
                    StretchScale = 1.0f
                };
            }

            return new SimulatedBonePreviewPose
            {
                // Particle output drives rotation and stretch. Bone origins are rebuilt
                // below through the skeleton hierarchy rather than translated directly.
                Position = entry.Value.Position,
                AxisX = NormalizeOrFallback(pose.AxisX / pose.Count, entry.Value.AxisX),
                AxisY = NormalizeOrFallback(pose.AxisY / pose.Count, entry.Value.AxisY),
                AxisZ = NormalizeOrFallback(pose.AxisZ / pose.Count, entry.Value.AxisZ),
                StretchScale = 1.0f
            };
        });

        ApplyRotationAndStretchHierarchy(poses);
        return poses;
    }

    private void ApplyRotationAndStretchHierarchy(Dictionary<int, SimulatedBonePreviewPose> poses)
    {
        var explicitlyDrivenBones = new HashSet<int>(_triangleDrivenBones);
        var explicitlyPositionedBones = new HashSet<int>();
        foreach (var (boneIndex, pair) in _boneParticleAxes)
        {
            if (!_restBonePoses.TryGetValue(boneIndex, out var restBone) ||
                !poses.TryGetValue(boneIndex, out var currentBone) ||
                !_particles.TryGetValue(pair.ParticleA, out var particleA) ||
                !_particles.TryGetValue(pair.ParticleB, out var particleB))
            {
                continue;
            }

            ApplyParticleAxis(
                restBone,
                currentBone,
                particleA.RestPosition,
                particleA.Position,
                particleB.RestPosition,
                particleB.Position);
            var restAnchor = Vector3.Lerp(particleA.RestPosition, particleB.RestPosition, pair.PositionFactor);
            var currentAnchor = Vector3.Lerp(particleA.Position, particleB.Position, pair.PositionFactor);
            // A bone is usually offset from the closest point of its driving
            // particle edge. Preserve that authored rest-pose offset while the
            // edge rotates, instead of snapping the bone origin onto the edge.
            currentBone.Position = currentAnchor + TransformFromRestFrame(
                restBone.Position - restAnchor,
                restBone,
                currentBone);
            explicitlyDrivenBones.Add(boneIndex);
            explicitlyPositionedBones.Add(boneIndex);
        }

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

    private static void ApplyParticleAxis(
        SimulatedBonePreviewPose restBone,
        SimulatedBonePreviewPose currentBone,
        Vector3 restStart,
        Vector3 currentStart,
        Vector3 restEnd,
        Vector3 currentEnd)
    {
        var restDirection = restEnd - restStart;
        var currentDirection = currentEnd - currentStart;
        var restLength = restDirection.Length();
        var currentLength = currentDirection.Length();
        if (restLength <= 0.000001f || currentLength <= 0.000001f)
            return;

        // The triangle binding has already supplied the full frame, including
        // roll around the bone. Correct only its aim toward the two-particle
        // axis so that secondary rotation is preserved.
        var predictedDirection = TransformFromRestFrame(restDirection, restBone, currentBone);
        if (predictedDirection.LengthSquared() <= 0.000001f)
            predictedDirection = restDirection;

        var correction = QuaternionFromTo(
            Vector3.Normalize(predictedDirection),
            currentDirection / currentLength);
        currentBone.AxisX = NormalizeOrFallback(Vector3.Transform(currentBone.AxisX, correction), currentBone.AxisX);
        currentBone.AxisY = NormalizeOrFallback(Vector3.Transform(currentBone.AxisY, correction), currentBone.AxisY);
        currentBone.AxisZ = NormalizeOrFallback(Vector3.Transform(currentBone.AxisZ, correction), currentBone.AxisZ);
        currentBone.StretchScale = currentLength / restLength;
    }

    private Dictionary<int, (int ParticleA, int ParticleB, float PositionFactor)> BuildBoneParticleAxes()
    {
        var axes = new Dictionary<int, (int ParticleA, int ParticleB, float PositionFactor)>();
        foreach (var group in _boneBindings.GroupBy(binding => binding.BoneIndex))
        {
            if (!_restBonePoses.TryGetValue(group.Key, out var bone))
                continue;

            (int ParticleA, int ParticleB, float PositionFactor, float DistanceSquared)? best = null;
            foreach (var binding in group)
            {
                foreach (var (particleA, particleB) in new[]
                {
                    (binding.ParticleA, binding.ParticleB),
                    (binding.ParticleA, binding.ParticleC),
                    (binding.ParticleB, binding.ParticleC)
                })
                {
                    if (!_particles.TryGetValue(particleA, out var a) ||
                        !_particles.TryGetValue(particleB, out var b))
                    {
                        continue;
                    }

                    var positionFactor = GetSegmentFactor(bone.Position, a.RestPosition, b.RestPosition);
                    var closestPoint = Vector3.Lerp(a.RestPosition, b.RestPosition, positionFactor);
                    var distanceSquared = Vector3.DistanceSquared(bone.Position, closestPoint);
                    if (best == null || distanceSquared < best.Value.DistanceSquared)
                        best = (particleA, particleB, positionFactor, distanceSquared);
                }
            }

            if (best.HasValue)
                axes[group.Key] = (best.Value.ParticleA, best.Value.ParticleB, best.Value.PositionFactor);
        }

        return axes;
    }

    private static float GetSegmentFactor(Vector3 point, Vector3 start, Vector3 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0.000001f)
            return 0.0f;

        return Math.Clamp(Vector3.Dot(point - start, segment) / lengthSquared, 0.0f, 1.0f);
    }

    private Dictionary<int, (int AnchorParticle, IReadOnlyList<int> ChildParticles)> BuildBoneParticleTargets(
        IReadOnlyList<ParticlePreviewTriangle> triangles)
    {
        var targets = new Dictionary<int, (int AnchorParticle, IReadOnlyList<int> ChildParticles)>();
        var linkedParticles = new Dictionary<int, HashSet<int>>();
        void AddLinkedPair(int a, int b)
        {
            if (!linkedParticles.TryGetValue(a, out var fromA))
                linkedParticles.Add(a, fromA = new HashSet<int>());
            if (!linkedParticles.TryGetValue(b, out var fromB))
                linkedParticles.Add(b, fromB = new HashSet<int>());
            fromA.Add(b);
            fromB.Add(a);
        }

        foreach (var link in _links)
            AddLinkedPair(link.ParticleA, link.ParticleB);
        foreach (var triangle in triangles)
        {
            AddLinkedPair(triangle.ParticleA, triangle.ParticleB);
            AddLinkedPair(triangle.ParticleA, triangle.ParticleC);
            AddLinkedPair(triangle.ParticleB, triangle.ParticleC);
        }

        foreach (var (boneIndex, restBone) in _restBonePoses)
        {
            // A bone normally begins at its nearest simulation particle. Fixed roots
            // are only one case; the rest of a chain is anchored at moving particles.
            var anchor = _particles.Values
                .OrderBy(particle => Vector3.DistanceSquared(particle.RestPosition, restBone.Position))
                .FirstOrDefault();
            if (anchor == null || Vector3.DistanceSquared(anchor.RestPosition, restBone.Position) > 0.04f ||
                !linkedParticles.TryGetValue(anchor.Index, out var neighbours))
            {
                continue;
            }

            var closestBoneToAnchor = _restBonePoses
                .OrderBy(entry => Vector3.DistanceSquared(entry.Value.Position, anchor.RestPosition))
                .FirstOrDefault().Key;
            if (closestBoneToAnchor != boneIndex)
                continue;

            var expectedDirection = _boneParents
                .Where(entry => entry.Value == boneIndex)
                .Select(entry => _restBonePoses.TryGetValue(entry.Key, out var child)
                    ? child.Position - restBone.Position
                    : Vector3.Zero)
                .Aggregate(Vector3.Zero, (sum, direction) => sum + direction);
            if (expectedDirection.LengthSquared() <= 0.000001f)
                expectedDirection = restBone.AxisY;
            expectedDirection = Vector3.Normalize(expectedDirection);

            // A middle particle usually touches both its parent and child. Averaging
            // those directions cancels them out, leaving its bone unable to rotate.
            // Use the neighbour aligned with this bone's child direction instead.
            var children = neighbours
                .Select(index => _particles.TryGetValue(index, out var particle) ? particle : null)
                .Where(particle => particle != null && particle.Index != anchor.Index)
                .Cast<SimParticle>()
                .Where(particle => Vector3.DistanceSquared(particle.RestPosition, anchor.RestPosition) > 0.000001f)
                .OrderByDescending(particle => Vector3.Dot(
                    Vector3.Normalize(particle.RestPosition - anchor.RestPosition),
                    expectedDirection))
                .ThenBy(particle => Vector3.DistanceSquared(particle.RestPosition, anchor.RestPosition))
                .Take(1)
                .Select(particle => particle.Index)
                .ToArray();
            if (children.Length > 0)
                targets[boneIndex] = (anchor.Index, children);
        }

        return targets;
    }

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
        const int subSteps = 2;
        var constraintIterations = Math.Clamp(SolverIterations, 1, 24);
        var subStep = step / subSteps;
        _windTime += step;

        for (var subStepIndex = 0; subStepIndex < subSteps; subStepIndex++)
        {
            var windDirection = GetWindDirection(subStep);
            var gust = 1.0f + MathF.Sin(_windTime * 1.8f) * Math.Clamp(WindGustiness, 0.0f, 1.0f);
            var wind = WindEnabled ? windDirection * Math.Max(0.0f, WindSpeed) * gust : Vector3.Zero;
            Integrate(subStep, wind);
            for (var iteration = 0; iteration < constraintIterations; iteration++)
            {
                foreach (var link in _links)
                    SolveLink(link);
                SolveLocalRanges();
                foreach (var particle in _particles.Values)
                    ResolveCollisions(particle);
            }
        }
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
                particle.Position = particle.RestPosition;
                particle.PreviousPosition = particle.RestPosition;
                continue;
            }

            var velocity = (particle.Position - particle.PreviousPosition) * damping;
            particle.PreviousPosition = particle.Position;
            particle.Position += velocity + (_gravity * Math.Max(0.0f, GravityScale) + wind) * (deltaSeconds * deltaSeconds);
        }
    }

    private void SolveLink(ParticlePreviewLink link)
    {
        if (!_particles.TryGetValue(link.ParticleA, out var a) || !_particles.TryGetValue(link.ParticleB, out var b))
            return;

        var offset = b.Position - a.Position;
        var length = offset.Length();
        if (length <= 0.000001f)
            return;

        var target = link.RestLength;
        var strength = link.Stiffness ?? 1.0f;
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

        if (!target.HasValue)
            return;

        var weightA = a.EffectiveInverseMass;
        var weightB = b.EffectiveInverseMass;
        var weightTotal = weightA + weightB;
        if (weightTotal <= 0.0f)
            return;

        var correction = offset / length * ((length - target.Value) * Math.Clamp(strength, 0.0f, 1.0f));
        if (weightA > 0.0f)
            a.Position += correction * (weightA / weightTotal);
        if (weightB > 0.0f)
            b.Position -= correction * (weightB / weightTotal);
    }

    private void SolveLocalRanges()
    {
        foreach (var range in _localRanges)
        {
            if (!_particles.TryGetValue(range.ParticleIndex, out var particle) || particle.Fixed)
                continue;

            var maximumDistance = Math.Max(0.0f, range.MaximumDistance);
            var offset = particle.Position - particle.RestPosition;
            var distance = offset.Length();
            if (distance <= maximumDistance || distance <= 0.000001f)
                continue;

            particle.Position = particle.RestPosition + offset * (maximumDistance / distance);
        }
    }

    private void ResolveCollisions(SimParticle particle)
    {
        if (particle.Fixed)
            return;

        // A particle can be assigned to several overlapping capsules. Applying
        // every full-depth correction in one pass makes those contacts fight
        // each other and can catapult the preview particle. Solve the deepest
        // contact first; later solver iterations converge against the rest.
        var hasContact = false;
        var deepestContact = default(CollisionContact);
        foreach (var collider in _colliders)
        {
            // Bit 31 is Havok's landscape-collision flag, rather than a slot
            // in perInstanceCollidables. Normal static colliders use 0..30.
            if (collider.CollisionBit is < 0 or >= 31)
                continue;
            if ((particle.CollisionMask & (1u << collider.CollisionBit)) == 0)
                continue;

            if (!TryGetCollisionContact(particle, collider, out var contact))
                continue;
            if (!hasContact || contact.Penetration > deepestContact.Penetration)
            {
                deepestContact = contact;
                hasContact = true;
            }
        }

        if (!hasContact)
            return;

        // Keep deeply overlapping authoring mistakes from teleporting a whole
        // chain across the viewport in a single solver pass. The remaining
        // penetration is resolved by the following iterations and substeps.
        var maximumCorrection = Math.Max(0.01f, particle.Radius * 0.75f);
        var correction = Math.Min(deepestContact.Penetration, maximumCorrection);
        ApplyCollisionCorrection(particle, deepestContact.Normal * correction);
    }

    private static bool TryGetCollisionContact(
        SimParticle particle,
        ColliderPreviewShape collider,
        out CollisionContact contact)
    {
        switch (collider.Kind)
        {
            case ColliderPreviewKind.Sphere:
                return TryGetSphereContact(particle, collider.Start, collider.Radius, out contact);
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
        contact = new CollisionContact(normal, requiredDistance - distance);
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
        return TryGetSphereContact(particle, center, radius, out contact);
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

        contact = new CollisionContact(normal, particle.Radius - distance);
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

    private readonly record struct CollisionContact(Vector3 Normal, float Penetration);

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
            CollisionMask = source.CollisionMask;
        }

        public int Index { get; }
        public Vector3 RestPosition { get; }
        public Vector3 Position { get; set; }
        public Vector3 PreviousPosition { get; set; }
        public bool Fixed { get; }
        public float InverseMass { get; }
        public float Radius { get; }
        public uint CollisionMask { get; }
        public float EffectiveInverseMass => Fixed ? 0.0f : Math.Max(InverseMass, 0.0001f);

        public void Reset()
        {
            Position = RestPosition;
            PreviousPosition = RestPosition;
        }
    }
}
