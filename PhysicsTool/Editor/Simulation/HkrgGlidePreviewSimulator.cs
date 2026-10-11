using System.Numerics;

namespace HKCLTool;

// A lightweight ragdoll stress preview for HKRG files. It is not Havok and it
// never writes back to the document; it simply lets the viewport show how the
// ragdoll graph reacts to glide-like forces and the current constraint limits.
public sealed class HkrgGlidePreviewSimulator
{
    private const float MaximumPreviewSpeed = 6.0f;
    private const float MaximumPreviewExcursion = 0.45f;
    private const float PoseSpring = 42.0f;
    private readonly Dictionary<int, SimBone> _bones;
    private readonly List<SimLink> _links = new();
    private readonly List<IkChain> _ikChains = new();
    private readonly List<SimCollider> _colliders = new();
    private readonly Dictionary<int, SimulatedBonePreviewPose> _restPoses;
    private readonly Random _windRandom = new();
    private float _windTime;
    private float _nextRandomWindChange;
    private Vector3 _currentWindDirection = Vector3.UnitZ;
    private Vector3 _targetWindDirection = Vector3.UnitZ;

    public HkrgGlidePreviewSimulator(
        ParticlePreviewData source,
        IReadOnlyList<ColliderEditRow> rigidBodies,
        IReadOnlyList<HkrgConstraintEditRow> constraints)
    {
        _bones = source.Bones
            .Where(bone => !bone.IsReference)
            .ToDictionary(
                bone => bone.EditorIndex >= 0 ? bone.EditorIndex : bone.Index,
                bone => new SimBone(bone.EditorIndex >= 0 ? bone.EditorIndex : bone.Index, bone));
        _restPoses = _bones.ToDictionary(
            entry => entry.Key,
            entry => new SimulatedBonePreviewPose
            {
                Position = entry.Value.RestPosition,
                AxisX = entry.Value.RestAxisX,
                AxisY = entry.Value.RestAxisY,
                AxisZ = entry.Value.RestAxisZ,
                StretchScale = 1.0f
            });

        BuildParentLinks();
        BuildIkChains();
        BuildConstraintLinks(rigidBodies, constraints);
        BuildRigidBodyFollowers(source.Colliders);
    }

    public bool WindEnabled { get; set; }
    public bool RandomWindDirections { get; set; } = true;
    public Vector3 WindDirection { get; set; } = Vector3.UnitX;
    public float WindSpeed { get; set; } = 2.2f;
    public float WindGustiness { get; set; } = 0.35f;
    public float GravityScale { get; set; } = 1.0f;
    public int SolverIterations { get; set; } = 7;

    public void Reset()
    {
        foreach (var bone in _bones.Values)
            bone.Reset();
        _windTime = 0.0f;
        _nextRandomWindChange = 0.0f;
        _currentWindDirection = Vector3.UnitZ;
        _targetWindDirection = Vector3.UnitZ;
    }

    public void Step(float deltaSeconds)
    {
        var step = Math.Clamp(deltaSeconds, 1.0f / 240.0f, 1.0f / 30.0f);
        const int subSteps = 2;
        var subStep = step / subSteps;
        var iterations = Math.Clamp(SolverIterations, 1, 32);
        _windTime += step;

        for (var subStepIndex = 0; subStepIndex < subSteps; subStepIndex++)
        {
            Integrate(subStep);
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                foreach (var link in _links)
                    SolveLink(link);

                SolveIkChains();
            }

            foreach (var bone in _bones.Values)
                StabilizeBone(bone);
        }
    }

    public IReadOnlyDictionary<int, SimulatedBonePreviewPose> GetBonePoses()
    {
        var poses = new Dictionary<int, SimulatedBonePreviewPose>();
        foreach (var bone in _bones.Values)
        {
            var pose = new SimulatedBonePreviewPose
            {
                Position = bone.Position,
                AxisX = bone.RestAxisX,
                AxisY = bone.RestAxisY,
                AxisZ = bone.RestAxisZ,
                StretchScale = 1.0f
            };

            if (bone.ParentIndex >= 0 && _bones.TryGetValue(bone.ParentIndex, out var parent))
            {
                var restDirection = bone.RestPosition - parent.RestPosition;
                var currentDirection = bone.Position - parent.Position;
                var restLength = restDirection.Length();
                var currentLength = currentDirection.Length();
                if (restLength > 0.000001f && currentLength > 0.000001f)
                {
                    var rotation = QuaternionFromTo(restDirection / restLength, currentDirection / currentLength);
                    pose.AxisX = NormalizeOrFallback(Vector3.Transform(bone.RestAxisX, rotation), bone.RestAxisX);
                    pose.AxisY = NormalizeOrFallback(Vector3.Transform(bone.RestAxisY, rotation), bone.RestAxisY);
                    pose.AxisZ = NormalizeOrFallback(Vector3.Transform(bone.RestAxisZ, rotation), bone.RestAxisZ);
                    pose.StretchScale = currentLength / restLength;
                }
            }

            poses[bone.Index] = pose;
        }

        return poses;
    }

    public IReadOnlyDictionary<int, ColliderPreviewShape> GetColliderPoses()
    {
        var result = new Dictionary<int, ColliderPreviewShape>();
        var poses = GetBonePoses();
        foreach (var collider in _colliders)
        {
            if (!_bones.TryGetValue(collider.BoneIndex, out var bone) ||
                !_restPoses.TryGetValue(collider.BoneIndex, out var restPose))
            {
                continue;
            }

            var currentPose = poses.TryGetValue(collider.BoneIndex, out var pose) ? pose : restPose;
            result[collider.Index] = new ColliderPreviewShape
            {
                Index = collider.Index,
                Name = collider.Name,
                BoneIndex = collider.BoneIndex,
                CollisionBit = collider.CollisionBit,
                Kind = collider.Kind,
                Radius = collider.Radius,
                EndRadius = collider.EndRadius,
                PlaneNormal = TransformFromRestFrame(collider.RestPlaneNormal, restPose, currentPose),
                Start = bone.Position + TransformFromRestFrame(collider.RestStartOffset, restPose, currentPose),
                End = bone.Position + TransformFromRestFrame(collider.RestEndOffset, restPose, currentPose)
            };
        }

        return result;
    }

    private void BuildIkChains()
    {
        AddNamedIkChain("Arm", "Arm_1", "Arm_2", "Wrist", "L");
        AddNamedIkChain("Arm", "Arm_1", "Arm_2", "Wrist", "R");
        AddNamedIkChain("Leg", "Leg_1", "Leg_2", "Ankle", "L");
        AddNamedIkChain("Leg", "Leg_1", "Leg_2", "Ankle", "R");
    }

    private void AddNamedIkChain(string family, string rootName, string middleName, string endName, string side)
    {
        var root = FindBoneByName(rootName, side);
        var middle = FindBoneByName(middleName, side);
        var end = FindBoneByName(endName, side);
        if (root == null || middle == null || end == null)
            return;

        var firstLength = Vector3.Distance(root.RestPosition, middle.RestPosition);
        var secondLength = Vector3.Distance(middle.RestPosition, end.RestPosition);
        if (firstLength <= 0.000001f || secondLength <= 0.000001f)
            return;

        var restNormal = Vector3.Cross(middle.RestPosition - root.RestPosition, end.RestPosition - root.RestPosition);
        if (restNormal.LengthSquared() <= 0.000001f)
            restNormal = GuessChainNormal(family, side);
        restNormal = NormalizeOrFallback(restNormal, Vector3.UnitZ);

        _ikChains.Add(new IkChain(root.Index, middle.Index, end.Index, firstLength, secondLength, restNormal));
    }

    private SimBone? FindBoneByName(string token, string side)
    {
        return _bones.Values.FirstOrDefault(bone =>
            bone.Name.Contains(token, StringComparison.OrdinalIgnoreCase) &&
            (bone.Name.EndsWith("_" + side, StringComparison.OrdinalIgnoreCase) ||
             bone.Name.Contains("_" + side + "_", StringComparison.OrdinalIgnoreCase)));
    }

    private static Vector3 GuessChainNormal(string family, string side)
    {
        if (family.Equals("Arm", StringComparison.OrdinalIgnoreCase))
            return side.Equals("L", StringComparison.OrdinalIgnoreCase) ? Vector3.UnitZ : -Vector3.UnitZ;
        return side.Equals("L", StringComparison.OrdinalIgnoreCase) ? -Vector3.UnitZ : Vector3.UnitZ;
    }

    private void SolveIkChains()
    {
        foreach (var chain in _ikChains)
            SolveIkChain(chain);
    }

    private void SolveIkChain(IkChain chain)
    {
        if (!_bones.TryGetValue(chain.Root, out var root) ||
            !_bones.TryGetValue(chain.Middle, out var middle) ||
            !_bones.TryGetValue(chain.End, out var end))
        {
            return;
        }

        var rootPosition = root.Position;
        var target = end.Position;
        var offset = target - rootPosition;
        var distance = offset.Length();
        var maximumLength = MathF.Max(0.0001f, chain.FirstLength + chain.SecondLength);
        var minimumLength = MathF.Max(0.0001f, MathF.Abs(chain.FirstLength - chain.SecondLength) + 0.0001f);
        if (distance <= 0.000001f)
        {
            offset = NormalizeOrFallback(end.RestPosition - root.RestPosition, Vector3.UnitY) * minimumLength;
            distance = minimumLength;
        }

        var direction = offset / distance;
        var clampedDistance = Math.Clamp(distance, minimumLength, maximumLength);
        target = rootPosition + direction * clampedDistance;

        var bendNormal = NormalizeOrFallback(chain.RestNormal, Vector3.UnitZ);
        var bendDirection = Vector3.Cross(bendNormal, direction);
        if (bendDirection.LengthSquared() <= 0.000001f)
            bendDirection = Vector3.Cross(Vector3.UnitY, direction);
        bendDirection = NormalizeOrFallback(bendDirection, Vector3.UnitX);

        var along = (chain.FirstLength * chain.FirstLength + clampedDistance * clampedDistance - chain.SecondLength * chain.SecondLength) /
                    (2.0f * clampedDistance);
        var heightSquared = MathF.Max(0.0f, chain.FirstLength * chain.FirstLength - along * along);
        var middleTarget = rootPosition + direction * along + bendDirection * MathF.Sqrt(heightSquared);

        ApplyIkCorrection(middle, middleTarget - middle.Position, 0.88f);
        ApplyIkCorrection(end, target - end.Position, 0.72f);
    }

    private static void ApplyIkCorrection(SimBone bone, Vector3 correction, float strength)
    {
        if (bone.Fixed)
            return;

        var applied = correction * Math.Clamp(strength, 0.0f, 1.0f);
        bone.Position += applied;
        bone.PreviousPosition += applied * 0.85f;
    }
    private void BuildParentLinks()
    {
        foreach (var bone in _bones.Values)
        {
            if (bone.ParentIndex < 0 || !_bones.TryGetValue(bone.ParentIndex, out var parent))
                continue;

            var restLength = Vector3.Distance(parent.RestPosition, bone.RestPosition);
            if (restLength <= 0.000001f)
                continue;

            _links.Add(new SimLink(parent.Index, bone.Index, restLength, restLength, 0.92f));
        }
    }

    private void BuildConstraintLinks(
        IReadOnlyList<ColliderEditRow> rigidBodies,
        IReadOnlyList<HkrgConstraintEditRow> constraints)
    {
        var bodyToBone = rigidBodies
            .Where(body => body.BoneIndex >= 0)
            .ToDictionary(body => body.Index, body => body.BoneIndex);
        foreach (var constraint in constraints)
        {
            if (!bodyToBone.TryGetValue(constraint.BodyA, out var boneA) ||
                !bodyToBone.TryGetValue(constraint.BodyB, out var boneB) ||
                !_bones.TryGetValue(boneA, out var a) ||
                !_bones.TryGetValue(boneB, out var b) ||
                boneA == boneB)
            {
                continue;
            }

            var restLength = Vector3.Distance(a.RestPosition, b.RestPosition);
            if (restLength <= 0.000001f)
                continue;

            var angle = MaxAngleMagnitude(constraint);
            var slack = Math.Clamp(MathF.Sin(Math.Clamp(angle, 0.0f, MathF.PI * 0.75f)) * 0.35f, 0.02f, 0.35f);
            var tau = constraint.TwistTau ?? constraint.ConeTau ?? constraint.PlaneTau ?? constraint.HingeTau ?? 0.6f;
            var friction = constraint.AngularFriction ?? 0.0f;
            var stiffness = Math.Clamp(0.30f + tau * 0.45f + MathF.Min(friction, 100.0f) * 0.0025f, 0.25f, 0.96f);
            _links.Add(new SimLink(boneA, boneB, restLength, restLength * (1.0f + slack), stiffness));
        }
    }

    private void BuildRigidBodyFollowers(IReadOnlyList<ColliderPreviewShape> sourceColliders)
    {
        foreach (var collider in sourceColliders)
        {
            if (!_bones.TryGetValue(collider.BoneIndex, out var bone) ||
                !_restPoses.TryGetValue(collider.BoneIndex, out var restPose))
            {
                continue;
            }

            _colliders.Add(new SimCollider(
                collider.Index,
                collider.Name,
                collider.BoneIndex,
                collider.CollisionBit,
                collider.Kind,
                collider.Start - bone.RestPosition,
                collider.End - bone.RestPosition,
                collider.Radius,
                collider.EndRadius,
                collider.PlaneNormal));
        }
    }

    private void Integrate(float deltaSeconds)
    {
        var windDirection = GetWindDirection(deltaSeconds);
        var gust = 1.0f + MathF.Sin(_windTime * 2.1f) * Math.Clamp(WindGustiness, 0.0f, 1.0f);
        var glide = (WindEnabled ? windDirection : Vector3.UnitZ) * Math.Max(0.0f, WindSpeed) * gust;
        glide += Vector3.UnitY * Math.Max(0.0f, WindSpeed) * 0.08f;
        var damping = MathF.Exp(-8.0f * deltaSeconds);

        foreach (var bone in _bones.Values)
        {
            if (bone.Fixed)
            {
                bone.Position = bone.RestPosition;
                bone.PreviousPosition = bone.RestPosition;
                continue;
            }

            var displacement = bone.Position - bone.RestPosition;
            var poseAcceleration = -displacement * PoseSpring;
            var limbWeight = IsLimb(bone.Name) ? 0.35f : 0.12f;
            var velocity = ClampMagnitude((bone.Position - bone.PreviousPosition) * damping, MaximumPreviewSpeed * deltaSeconds);
            bone.PreviousPosition = bone.Position;
            bone.Position += velocity + (poseAcceleration + glide * limbWeight) * (deltaSeconds * deltaSeconds);

            var excursion = bone.Position - bone.RestPosition;
            if (excursion.LengthSquared() > MaximumPreviewExcursion * MaximumPreviewExcursion)
                bone.Position = bone.RestPosition + Vector3.Normalize(excursion) * MaximumPreviewExcursion;
        }
    }

    private Vector3 GetWindDirection(float deltaSeconds)
    {
        if (RandomWindDirections && _windTime >= _nextRandomWindChange)
        {
            var candidate = new Vector3(
                (float)(_windRandom.NextDouble() * 2.0 - 1.0),
                (float)(_windRandom.NextDouble() * 0.35 - 0.05),
                (float)(_windRandom.NextDouble() * 2.0 - 1.0));
            _targetWindDirection = candidate.LengthSquared() > 0.000001f ? Vector3.Normalize(candidate) : Vector3.UnitZ;
            _nextRandomWindChange = _windTime + 1.2f + (float)_windRandom.NextDouble() * 1.7f;
        }
        else if (!RandomWindDirections)
        {
            _targetWindDirection = WindDirection.LengthSquared() > 0.000001f ? Vector3.Normalize(WindDirection) : Vector3.UnitZ;
        }

        var blend = 1.0f - MathF.Exp(-deltaSeconds * 2.8f);
        var blended = Vector3.Lerp(_currentWindDirection, _targetWindDirection, blend);
        _currentWindDirection = blended.LengthSquared() > 0.000001f ? Vector3.Normalize(blended) : _targetWindDirection;
        return _currentWindDirection;
    }

    private void SolveLink(SimLink link)
    {
        if (!_bones.TryGetValue(link.BoneA, out var a) || !_bones.TryGetValue(link.BoneB, out var b))
            return;

        var offset = b.Position - a.Position;
        var distance = offset.Length();
        if (distance <= 0.000001f)
            return;

        var target = Math.Clamp(distance, link.RestLength, link.MaxLength);
        var error = distance - target;
        if (Math.Abs(error) <= 0.000001f)
            return;

        var weightA = a.Fixed ? 0.0f : 1.0f;
        var weightB = b.Fixed ? 0.0f : 1.0f;
        var total = weightA + weightB;
        if (total <= 0.0f)
            return;

        var correction = offset / distance * (error * Math.Clamp(link.Stiffness, 0.0f, 1.0f));
        if (weightA > 0.0f)
            ApplyCorrection(a, correction * (weightA / total));
        if (weightB > 0.0f)
            ApplyCorrection(b, -correction * (weightB / total));
    }

    private static void ApplyCorrection(SimBone bone, Vector3 correction)
    {
        bone.Position += correction;
        bone.PreviousPosition += correction * 0.82f;
    }

    private static float MaxAngleMagnitude(HkrgConstraintEditRow constraint)
    {
        var values = new[]
        {
            constraint.TwistMinAngle, constraint.TwistMaxAngle,
            constraint.ConeMinAngle, constraint.ConeMaxAngle,
            constraint.PlaneMinAngle, constraint.PlaneMaxAngle,
            constraint.HingeMinAngle, constraint.HingeMaxAngle
        };
        return values.Where(value => value.HasValue).Select(value => Math.Abs(value!.Value)).DefaultIfEmpty(MathF.PI / 4.0f).Max();
    }

    private static bool IsLimb(string name) =>
        name.Contains("Leg", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Arm", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Wrist", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Ankle", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Knee", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Foot", StringComparison.OrdinalIgnoreCase);

    private static bool IsFixedAnchor(string name, int parentIndex)
    {
        if (parentIndex < 0)
            return true;
        return name.Contains("Root", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Spine", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Head", StringComparison.OrdinalIgnoreCase);
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

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback) =>
        value.LengthSquared() > 0.000001f ? Vector3.Normalize(value) : fallback;

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

    private void StabilizeBone(SimBone bone)
    {
        if (!IsFinite(bone.Position) || !IsFinite(bone.PreviousPosition) ||
            Vector3.DistanceSquared(bone.Position, bone.RestPosition) > MaximumPreviewExcursion * MaximumPreviewExcursion)
        {
            bone.Reset();
        }
    }

    private sealed class SimBone
    {
        public SimBone(int index, BonePreviewPoint source)
        {
            Index = index;
            ParentIndex = source.ParentIndex;
            if (source.ParentIndex >= 0)
                ParentIndex = source.ParentIndex >= 100_000 ? source.ParentIndex - 100_000 : source.ParentIndex;
            Name = source.Name;
            RestPosition = source.Position;
            Position = source.Position;
            PreviousPosition = source.Position;
            RestAxisX = source.AxisX;
            RestAxisY = source.AxisY;
            RestAxisZ = source.AxisZ;
            Fixed = IsFixedAnchor(Name, ParentIndex);
        }

        public int Index { get; }
        public int ParentIndex { get; }
        public string Name { get; }
        public Vector3 RestPosition { get; }
        public Vector3 Position { get; set; }
        public Vector3 PreviousPosition { get; set; }
        public Vector3 RestAxisX { get; }
        public Vector3 RestAxisY { get; }
        public Vector3 RestAxisZ { get; }
        public bool Fixed { get; }

        public void Reset()
        {
            Position = RestPosition;
            PreviousPosition = RestPosition;
        }
    }

    private sealed record SimLink(int BoneA, int BoneB, float RestLength, float MaxLength, float Stiffness);

    private sealed record IkChain(int Root, int Middle, int End, float FirstLength, float SecondLength, Vector3 RestNormal);

    private sealed record SimCollider(
        int Index,
        string Name,
        int BoneIndex,
        int CollisionBit,
        ColliderPreviewKind Kind,
        Vector3 RestStartOffset,
        Vector3 RestEndOffset,
        float Radius,
        float EndRadius,
        Vector3 RestPlaneNormal);
}




