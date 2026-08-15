using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace HKCLTool;

// HKRG uses Havok's rigid-body/ragdoll classes, not hcl cloth classes. The
// shared packfile reader can still load it, while this adapter exposes the
// useful ragdoll data without treating rigid bodies as hcl cloth colliders.
// The conservative editor exposes reference-pose transforms and capsule
// dimensions, while keeping the existing physical constraint graph editable through verified limit fields.
public sealed partial class HkclService
{
    // Avoid colliding with the compact ragdoll's 0-based bone indices in the
    // shared viewport. These reference IDs are never written to the file.
    private const int HkrgModelPreviewIndexOffset = 100_000;

    private IReadOnlyList<object> GetHkrgPhysicsSystems()
    {
        if (!IsHkrg || _root == null)
            return Array.Empty<object>();

        return (GetList(GetValue(_root, "namedVariants")) ?? Array.Empty<object>())
            .Select(variant => GetValue(variant, "variant"))
            .Where(variant => variant?.GetType().Name == "hkpPhysicsData")
            .SelectMany(physicsData => GetList(GetValue(physicsData, "systems")) ?? Array.Empty<object>())
            .ToArray();
    }

    private IReadOnlyList<object> GetHkrgRigidBodies()
    {
        // hkaRagdollInstance owns the body ordering used by
        // boneToRigidBodyMap. hkpPhysicsSystem contains the same objects but
        // is free to store them in a different order, so using the latter as
        // a row index can write an ankle capsule when the UI says wrist.
        var ragdollBodies = GetList(GetValue(GetHkrgRagdollInstance(), "rigidBodies"));
        if (ragdollBodies is { Count: > 0 })
            return ragdollBodies.ToArray();

        return GetHkrgPhysicsSystems()
            .SelectMany(system => GetList(GetValue(system, "rigidBodies")) ?? Array.Empty<object>())
            .ToArray();
    }

    private object? GetHkrgRagdollInstance()
    {
        if (!IsHkrg || _root == null)
            return null;

        return (GetList(GetValue(_root, "namedVariants")) ?? Array.Empty<object>())
            .Select(variant => GetValue(variant, "variant"))
            .FirstOrDefault(variant => variant?.GetType().Name == "hkaRagdollInstance");
    }

    private object? GetHkrgRagdollSkeleton() => GetValue(GetHkrgRagdollInstance(), "skeleton");

    private object? GetHkrgModelSkeleton()
    {
        if (!IsHkrg || _root == null)
            return null;

        var skeletons = (GetList(GetValue(_root, "namedVariants")) ?? Array.Empty<object>())
            .Select(variant => GetValue(variant, "variant"))
            .Where(variant => variant?.GetType().Name == "hkaAnimationContainer")
            .SelectMany(container => GetList(GetValue(container, "skeletons")) ?? Array.Empty<object>())
            .ToArray();

        return skeletons.FirstOrDefault(skeleton =>
                   string.Equals(GetString(skeleton, "name"), "model_skeleton", StringComparison.OrdinalIgnoreCase))
               ?? skeletons.FirstOrDefault(skeleton => !ReferenceEquals(skeleton, GetHkrgRagdollSkeleton()));
    }

    private IReadOnlyList<BoneEditRow> GetHkrgBoneRows() =>
        GetHkrgSkeletonRows(GetHkrgRagdollSkeleton(), "Ragdoll bone");

    public IReadOnlyList<BoneEditRow> GetHkrgModelBoneRows() =>
        GetHkrgSkeletonRows(GetHkrgModelSkeleton(), "Model bone");

    private IReadOnlyList<BoneEditRow> GetHkrgSkeletonRows(object? skeleton, string fallbackName)
    {
        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parents = GetList(GetValue(skeleton, "parentIndices")) ?? Array.Empty<object>();
        var poses = GetList(GetValue(skeleton, "referencePose")) ?? Array.Empty<object>();

        return bones.Select((bone, index) =>
        {
            var pose = poses.ElementAtOrDefault(index) is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
            return new BoneEditRow
            {
                Index = index,
                Name = GetString(bone, "name") ?? $"{fallbackName} {index}",
                ParentIndex = ToInt(parents.ElementAtOrDefault(index), -1),
                X = pose.M11,
                Y = pose.M12,
                Z = pose.M13,
                RotationX = pose.M21,
                RotationY = pose.M22,
                RotationZ = pose.M23,
                RotationW = pose.M24,
                ScaleX = pose.M31,
                ScaleY = pose.M32,
                ScaleZ = pose.M33
            };
        }).ToArray();
    }

    private void UpdateHkrgBoneRows(IEnumerable<BoneEditRow> rows) =>
        UpdateHkrgSkeletonRows(GetHkrgRagdollSkeleton(), rows, "ragdoll skeleton");

    public void UpdateHkrgModelBoneRows(IEnumerable<BoneEditRow> rows) =>
        UpdateHkrgSkeletonRows(GetHkrgModelSkeleton(), rows, "model skeleton");

    private void UpdateHkrgSkeletonRows(object? skeleton, IEnumerable<BoneEditRow> rows, string label)
    {
        if (skeleton == null)
            throw new InvalidOperationException($"The HKRG file has no {label}.");
        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var poses = GetValue(skeleton, "referencePose") as System.Collections.IList;
        if (poses == null)
            throw new InvalidOperationException($"The HKRG {label} reference pose is not editable.");

        foreach (var row in rows)
        {
            if (row.Index < 0 || row.Index >= bones.Count || row.Index >= poses.Count)
                continue;

            SetValue(bones[row.Index], "name", row.Name);
            var existing = poses[row.Index] is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
            SetListItem(poses, row.Index, new Matrix4x4(
                row.X, row.Y, row.Z, existing.M14,
                row.RotationX, row.RotationY, row.RotationZ, row.RotationW,
                existing.M31, existing.M32, existing.M33, existing.M34,
                existing.M41, existing.M42, existing.M43, existing.M44));
        }
    }

    public IReadOnlyList<HkrgConstraintEditRow> GetHkrgConstraintRows()
    {
        if (!IsHkrg)
            return Array.Empty<HkrgConstraintEditRow>();

        var bodies = GetHkrgRigidBodies();
        return GetHkrgConstraints().Select((constraint, index) =>
        {
            var data = GetValue(constraint, "data") ?? constraint;
            var atoms = GetValue(data, "atoms");
            var typeName = data.GetType().Name;
            var bodyA = GetHkrgConstraintBodyIndex(constraint, bodies, "entities_0");
            var bodyB = GetHkrgConstraintBodyIndex(constraint, bodies, "entities_1");
            var row = new HkrgConstraintEditRow
            {
                Index = index,
                Name = GetString(constraint, "name") ?? $"Constraint {index}",
                ConstraintType = typeName switch
                {
                    "hkpRagdollConstraintData" => "Ragdoll",
                    "hkpLimitedHingeConstraintData" => "Limited hinge",
                    _ => typeName
                },
                BodyA = bodyA,
                BodyB = bodyB,
                BodyAName = bodyA >= 0 && bodyA < bodies.Count ? GetString(bodies[bodyA], "name") ?? string.Empty : string.Empty,
                BodyBName = bodyB >= 0 && bodyB < bodies.Count ? GetString(bodies[bodyB], "name") ?? string.Empty : string.Empty
            };

            var twist = GetValue(atoms, "twistLimit");
            row.TwistMinAngle = GetOptionalSingle(twist, "minAngle");
            row.TwistMaxAngle = GetOptionalSingle(twist, "maxAngle");
            row.TwistTau = GetOptionalSingle(twist, "angularLimitsTauFactor");
            row.TwistDamping = GetOptionalSingle(twist, "angularLimitsDampFactor");

            var cone = GetValue(atoms, "coneLimit");
            row.ConeMinAngle = GetOptionalSingle(cone, "minAngle");
            row.ConeMaxAngle = GetOptionalSingle(cone, "maxAngle");
            row.ConeTau = GetOptionalSingle(cone, "angularLimitsTauFactor");
            row.ConeDamping = GetOptionalSingle(cone, "angularLimitsDampFactor");

            var planes = GetValue(atoms, "planesLimit");
            row.PlaneMinAngle = GetOptionalSingle(planes, "minAngle");
            row.PlaneMaxAngle = GetOptionalSingle(planes, "maxAngle");
            row.PlaneTau = GetOptionalSingle(planes, "angularLimitsTauFactor");
            row.PlaneDamping = GetOptionalSingle(planes, "angularLimitsDampFactor");

            var hinge = GetValue(atoms, "angLimit");
            row.HingeMinAngle = GetOptionalSingle(hinge, "minAngle");
            row.HingeMaxAngle = GetOptionalSingle(hinge, "maxAngle");
            row.HingeTau = GetOptionalSingle(hinge, "angularLimitsTauFactor");
            row.HingeDamping = GetOptionalSingle(hinge, "angularLimitsDampFactor");

            row.AngularFriction = GetOptionalSingle(GetValue(atoms, "angFriction"), "maxFrictionTorque");
            return row;
        }).ToArray();
    }

    public void UpdateHkrgConstraintRows(IEnumerable<HkrgConstraintEditRow> rows)
    {
        var constraints = GetHkrgConstraints();
        foreach (var row in rows)
        {
            if (row.Index < 0 || row.Index >= constraints.Count)
                continue;

            var constraint = constraints[row.Index];
            if (!string.IsNullOrWhiteSpace(row.Name))
                SetValue(constraint, "name", row.Name);
            var data = GetValue(constraint, "data") ?? constraint;
            var atoms = GetValue(data, "atoms");

            ApplyAngularLimit(GetValue(atoms, "twistLimit"), row.TwistMinAngle, row.TwistMaxAngle, row.TwistTau, row.TwistDamping);
            ApplyAngularLimit(GetValue(atoms, "coneLimit"), row.ConeMinAngle, row.ConeMaxAngle, row.ConeTau, row.ConeDamping);
            ApplyAngularLimit(GetValue(atoms, "planesLimit"), row.PlaneMinAngle, row.PlaneMaxAngle, row.PlaneTau, row.PlaneDamping);
            ApplyAngularLimit(GetValue(atoms, "angLimit"), row.HingeMinAngle, row.HingeMaxAngle, row.HingeTau, row.HingeDamping);
            SetOptionalSingle(GetValue(atoms, "angFriction"), "maxFrictionTorque", row.AngularFriction);
        }
    }

    public bool HkrgConstraintFramesAreFinite()
    {
        if (!IsHkrg)
            return true;

        foreach (var constraint in GetHkrgConstraints())
        {
            var data = GetValue(constraint, "data") ?? constraint;
            var atoms = GetValue(data, "atoms");
            var transforms = GetValue(atoms, "transforms");
            if (GetValue(transforms, "transformA") is Matrix4x4 transformA && !MatrixIsFinite(transformA))
                return false;
            if (GetValue(transforms, "transformB") is Matrix4x4 transformB && !MatrixIsFinite(transformB))
                return false;

            var motors = GetValue(atoms, "ragdollMotors");
            if (GetValue(motors, "target_bRca") is Matrix4x4 target && !MatrixIsFinite(target))
                return false;
        }

        return true;
    }

    private static bool MatrixIsFinite(Matrix4x4 matrix) =>
        float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14) &&
        float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24) &&
        float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34) &&
        float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);
    public void RepairHkrgConstraintFramesForCurrentPose(bool updateMotorTargets = true)
    {
        if (!IsHkrg)
            return;

        var bodies = GetHkrgRigidBodies();
        var constraints = GetHkrgConstraints();
        if (bodies.Count == 0 || constraints.Count == 0)
            return;

        var bodyToBone = GetHkrgBodyToBoneMap();
        var ragdollWorld = GetHkrgSkeletonWorldTransforms(GetHkrgRagdollSkeleton());
        var parents = GetList(GetValue(GetHkrgRagdollSkeleton(), "parentIndices")) ?? Array.Empty<object>();

        foreach (var constraint in constraints)
        {
            var bodyAIndex = GetHkrgConstraintBodyIndex(constraint, bodies, "entities_0");
            var bodyBIndex = GetHkrgConstraintBodyIndex(constraint, bodies, "entities_1");
            if (bodyAIndex < 0 || bodyAIndex >= bodies.Count ||
                bodyBIndex < 0 || bodyBIndex >= bodies.Count)
                continue;

            var data = GetValue(constraint, "data") ?? constraint;
            var atoms = GetValue(data, "atoms");
            var transforms = GetValue(atoms, "transforms");
            if (transforms == null)
                continue;

            var bodyA = GetHkrgAffineTransform(GetHkrgBodyTransform(bodies[bodyAIndex]));
            var bodyB = GetHkrgAffineTransform(GetHkrgBodyTransform(bodies[bodyBIndex]));
            if (!Matrix4x4.Invert(bodyA, out var inverseA) ||
                !Matrix4x4.Invert(bodyB, out var inverseB))
                continue;

            bodyToBone.TryGetValue(bodyAIndex, out var boneA);
            bodyToBone.TryGetValue(bodyBIndex, out var boneB);
            var jointWorld = BuildHkrgJointWorldFrame(
                ragdollWorld,
                parents,
                boneA,
                boneB,
                GetHkrgBodyCenter(bodyA),
                GetHkrgBodyCenter(bodyB));

            var localA = jointWorld * inverseA;
            var localB = jointWorld * inverseB;
            SetConstraintTransform(transforms, "transformA", localA);
            SetConstraintTransform(transforms, "transformB", localB);

            var motors = GetValue(atoms, "ragdollMotors");
            if (updateMotorTargets && motors != null && GetValue(motors, "target_bRca") is Matrix4x4 target)
            {
                // Vanilla target_bRca is transformB's rotation: constraint A
                // expressed in body B space. It is not the A/B frame delta.
                var bRotation = ClearTranslation(localB);
                SetValue(motors, "target_bRca", PreserveConstraintTransformPadding(target, bRotation));
            }
        }
    }

    // Retargets every stored copy of an authored ragdoll joint into the target
    // skeleton. HKRG keeps one collection on hkaRagdollInstance and another
    // inside hkpPhysicsSystem; those collections are not ordered identically.
    public void RetargetHkrgConstraintFramesForTargetPose(HkclService source)
    {
        if (!IsHkrg || !source.IsHkrg)
            throw new InvalidOperationException("Both documents must be HKRG files.");

        var targetBodies = GetHkrgRigidBodies();
        var sourceBodies = source.GetHkrgRigidBodies();
        if (targetBodies.Count != sourceBodies.Count)
            throw new InvalidOperationException("HKRG source and target rigid-body topology does not match.");

        var targetBodyToBone = GetHkrgBodyToBoneMap();
        var sourceBodyToBone = source.GetHkrgBodyToBoneMap();
        var targetBoneWorld = GetHkrgSkeletonWorldTransforms(GetHkrgRagdollSkeleton());
        var sourceBoneWorld = GetHkrgSkeletonWorldTransforms(source.GetHkrgRagdollSkeleton());
        var sourceParents = GetList(GetValue(source.GetHkrgRagdollSkeleton(), "parentIndices")) ?? Array.Empty<object>();

        RetargetHkrgConstraintCollectionForTargetPose(
            source,
            GetHkrgConstraints(),
            source.GetHkrgConstraints(),
            targetBodies,
            sourceBodies,
            targetBodyToBone,
            sourceBodyToBone,
            targetBoneWorld,
            sourceBoneWorld,
            sourceParents,
            "ragdoll instance");

        var targetSystems = GetHkrgPhysicsSystems();
        var sourceSystems = source.GetHkrgPhysicsSystems();
        if (targetSystems.Count != sourceSystems.Count)
            throw new InvalidOperationException("HKRG source and target physics-system topology does not match.");

        for (var systemIndex = 0; systemIndex < targetSystems.Count; systemIndex++)
        {
            var targetConstraints = GetList(GetValue(targetSystems[systemIndex], "constraints")) ?? Array.Empty<object>();
            var sourceConstraints = GetList(GetValue(sourceSystems[systemIndex], "constraints")) ?? Array.Empty<object>();
            if (targetConstraints.Count == 0 && sourceConstraints.Count == 0)
                continue;

            RetargetHkrgConstraintCollectionForTargetPose(
                source,
                targetConstraints,
                sourceConstraints,
                targetBodies,
                sourceBodies,
                targetBodyToBone,
                sourceBodyToBone,
                targetBoneWorld,
                sourceBoneWorld,
                sourceParents,
                $"physics system {systemIndex}");
        }
    }

    private void RetargetHkrgConstraintCollectionForTargetPose(
        HkclService source,
        IReadOnlyList<object> targetConstraints,
        IReadOnlyList<object> sourceConstraints,
        IReadOnlyList<object> targetBodies,
        IReadOnlyList<object> sourceBodies,
        IReadOnlyDictionary<int, int> targetBodyToBone,
        IReadOnlyDictionary<int, int> sourceBodyToBone,
        IReadOnlyList<Matrix4x4> targetBoneWorld,
        IReadOnlyList<Matrix4x4> sourceBoneWorld,
        IReadOnlyList<object> sourceParents,
        string collectionName)
    {
        if (targetConstraints.Count != sourceConstraints.Count)
            throw new InvalidOperationException($"HKRG source and target {collectionName} constraint counts do not match.");

        var consumedSourceConstraints = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var targetConstraint in targetConstraints)
        {
            var targetName = GetString(targetConstraint, "name");
            var sourceConstraint = sourceConstraints.FirstOrDefault(candidate =>
                !consumedSourceConstraints.Contains(candidate) &&
                string.Equals(GetString(candidate, "name"), targetName, StringComparison.Ordinal));
            if (sourceConstraint == null)
                throw new InvalidOperationException($"HKRG {collectionName} is missing a source constraint named '{targetName ?? "<unnamed>"}'.");

            consumedSourceConstraints.Add(sourceConstraint);
            var targetBodyAIndex = GetHkrgConstraintBodyIndex(targetConstraint, targetBodies, "entities_0");
            var targetBodyBIndex = GetHkrgConstraintBodyIndex(targetConstraint, targetBodies, "entities_1");
            var sourceBodyAIndex = GetHkrgConstraintBodyIndex(sourceConstraint, sourceBodies, "entities_0");
            var sourceBodyBIndex = GetHkrgConstraintBodyIndex(sourceConstraint, sourceBodies, "entities_1");
            if (targetBodyAIndex < 0 || targetBodyBIndex < 0 || sourceBodyAIndex < 0 || sourceBodyBIndex < 0)
                continue;

            var targetAtoms = GetValue(GetValue(targetConstraint, "data") ?? targetConstraint, "atoms");
            var targetTransforms = GetValue(targetAtoms, "transforms");
            var sourceAtoms = GetValue(GetValue(sourceConstraint, "data") ?? sourceConstraint, "atoms");
            var sourceTransforms = GetValue(sourceAtoms, "transforms");
            if (targetTransforms == null || sourceTransforms == null ||
                GetValue(sourceTransforms, "transformA") is not Matrix4x4 sourceFrameA ||
                GetValue(sourceTransforms, "transformB") is not Matrix4x4 sourceFrameB)
                continue;

            var sourceBodyA = GetHkrgAffineTransform(GetHkrgBodyTransform(sourceBodies[sourceBodyAIndex]));
            var sourceBodyB = GetHkrgAffineTransform(GetHkrgBodyTransform(sourceBodies[sourceBodyBIndex]));
            var targetBodyA = GetHkrgAffineTransform(GetHkrgBodyTransform(targetBodies[targetBodyAIndex]));
            var targetBodyB = GetHkrgAffineTransform(GetHkrgBodyTransform(targetBodies[targetBodyBIndex]));
            if (!Matrix4x4.Invert(targetBodyA, out var inverseTargetA) ||
                !Matrix4x4.Invert(targetBodyB, out var inverseTargetB))
                continue;

            sourceBodyToBone.TryGetValue(sourceBodyAIndex, out var sourceBoneA);
            sourceBodyToBone.TryGetValue(sourceBodyBIndex, out var sourceBoneB);
            targetBodyToBone.TryGetValue(targetBodyAIndex, out var targetBoneA);
            targetBodyToBone.TryGetValue(targetBodyBIndex, out var targetBoneB);

            var sourceWorldA = GetHkrgAffineTransform(sourceFrameA) * sourceBodyA;
            var sourceWorldB = GetHkrgAffineTransform(sourceFrameB) * sourceBodyB;
            var targetWorldA = RetargetHkrgConstraintWorldFrame(sourceWorldA, sourceBoneWorld, targetBoneWorld, sourceBoneA, targetBoneA);
            var targetWorldB = RetargetHkrgConstraintWorldFrame(sourceWorldB, sourceBoneWorld, targetBoneWorld, sourceBoneB, targetBoneB);

            var sourceChildBone = ChooseHkrgConstraintFrameBone(sourceParents, sourceBoneA, sourceBoneB);
            var targetChildBone = sourceChildBone == sourceBoneA ? targetBoneA : targetBoneB;
            var targetAnchor = RetargetHkrgConstraintPoint(
                GetHkrgBodyCenter(sourceWorldA),
                sourceBoneWorld,
                targetBoneWorld,
                sourceChildBone,
                targetChildBone);

            targetWorldA = SetHkrgFramePosition(NormalizeAffineFrame(targetWorldA), targetAnchor);
            targetWorldB = SetHkrgFramePosition(NormalizeAffineFrame(targetWorldB), targetAnchor);
            var targetLocalA = targetWorldA * inverseTargetA;
            var targetLocalB = targetWorldB * inverseTargetB;
            SetConstraintTransform(targetTransforms, "transformA", targetLocalA);
            SetConstraintTransform(targetTransforms, "transformB", targetLocalB);

            var motors = GetValue(targetAtoms, "ragdollMotors");
            if (motors != null && GetValue(motors, "target_bRca") is Matrix4x4 motorTarget)
            {
                // Vanilla target_bRca is transformB's rotation in body B space.
                var bRotation = ClearTranslation(targetLocalB);
                SetValue(motors, "target_bRca", PreserveConstraintTransformPadding(motorTarget, bRotation));
            }
        }
    }
    private static Matrix4x4 RetargetHkrgConstraintWorldFrame(
        Matrix4x4 sourceWorldFrame,
        IReadOnlyList<Matrix4x4> sourceBoneWorld,
        IReadOnlyList<Matrix4x4> targetBoneWorld,
        int sourceBoneIndex,
        int targetBoneIndex)
    {
        if (sourceBoneIndex >= 0 && sourceBoneIndex < sourceBoneWorld.Count &&
            targetBoneIndex >= 0 && targetBoneIndex < targetBoneWorld.Count &&
            Matrix4x4.Invert(sourceBoneWorld[sourceBoneIndex], out var inverseSourceBone))
        {
            return sourceWorldFrame * inverseSourceBone * targetBoneWorld[targetBoneIndex];
        }

        return sourceWorldFrame;
    }

    private static Matrix4x4 SetHkrgFramePosition(Matrix4x4 frame, Vector3 position) => new(
        frame.M11, frame.M12, frame.M13, frame.M14,
        frame.M21, frame.M22, frame.M23, frame.M24,
        frame.M31, frame.M32, frame.M33, frame.M34,
        position.X, position.Y, position.Z, frame.M44);

    // Retargets each original A/B frame through its own ragdoll-bone delta. Unlike
    // rebuilding a shared joint frame, this preserves the authored angular relation.
    public void RetargetHkrgConstraintFramesFrom(HkclService source)
    {
        if (!IsHkrg || !source.IsHkrg)
            throw new InvalidOperationException("Both documents must be HKRG files.");

        var targetBodies = GetHkrgRigidBodies();
        var sourceBodies = source.GetHkrgRigidBodies();
        var targetConstraints = GetHkrgConstraints();
        var sourceConstraints = source.GetHkrgConstraints();
        if (targetBodies.Count != sourceBodies.Count || targetConstraints.Count != sourceConstraints.Count)
            throw new InvalidOperationException("HKRG source and target topology do not match.");

        var targetBodyToBone = GetHkrgBodyToBoneMap();
        var sourceBodyToBone = source.GetHkrgBodyToBoneMap();
        var targetBoneWorld = GetHkrgSkeletonWorldTransforms(GetHkrgRagdollSkeleton());
        var sourceBoneWorld = GetHkrgSkeletonWorldTransforms(source.GetHkrgRagdollSkeleton());

        for (var index = 0; index < targetConstraints.Count; index++)
        {
            var targetConstraint = targetConstraints[index];
            var sourceConstraint = sourceConstraints[index];
            var targetBodyAIndex = GetHkrgConstraintBodyIndex(targetConstraint, targetBodies, "entities_0");
            var targetBodyBIndex = GetHkrgConstraintBodyIndex(targetConstraint, targetBodies, "entities_1");
            var sourceBodyAIndex = GetHkrgConstraintBodyIndex(sourceConstraint, sourceBodies, "entities_0");
            var sourceBodyBIndex = GetHkrgConstraintBodyIndex(sourceConstraint, sourceBodies, "entities_1");
            if (targetBodyAIndex < 0 || targetBodyBIndex < 0 || sourceBodyAIndex < 0 || sourceBodyBIndex < 0)
                continue;

            var targetAtoms = GetValue(GetValue(targetConstraint, "data") ?? targetConstraint, "atoms");
            var targetTransforms = GetValue(targetAtoms, "transforms");
            var sourceAtoms = GetValue(GetValue(sourceConstraint, "data") ?? sourceConstraint, "atoms");
            var sourceTransforms = GetValue(sourceAtoms, "transforms");
            if (targetTransforms == null || sourceTransforms == null ||
                GetValue(sourceTransforms, "transformA") is not Matrix4x4 sourceFrameA ||
                GetValue(sourceTransforms, "transformB") is not Matrix4x4 sourceFrameB)
                continue;

            var targetBodyA = GetHkrgAffineTransform(GetHkrgBodyTransform(targetBodies[targetBodyAIndex]));
            var targetBodyB = GetHkrgAffineTransform(GetHkrgBodyTransform(targetBodies[targetBodyBIndex]));
            var sourceBodyA = GetHkrgAffineTransform(GetHkrgBodyTransform(sourceBodies[sourceBodyAIndex]));
            var sourceBodyB = GetHkrgAffineTransform(GetHkrgBodyTransform(sourceBodies[sourceBodyBIndex]));
            if (!Matrix4x4.Invert(targetBodyA, out var inverseTargetA) ||
                !Matrix4x4.Invert(targetBodyB, out var inverseTargetB))
                continue;

            sourceBodyToBone.TryGetValue(sourceBodyAIndex, out var sourceBoneA);
            sourceBodyToBone.TryGetValue(sourceBodyBIndex, out var sourceBoneB);
            targetBodyToBone.TryGetValue(targetBodyAIndex, out var targetBoneA);
            targetBodyToBone.TryGetValue(targetBodyBIndex, out var targetBoneB);

            var targetFrameA = RetargetHkrgConstraintFrame(
                sourceFrameA * sourceBodyA, sourceBoneWorld, targetBoneWorld, sourceBoneA, targetBoneA, inverseTargetA);
            var targetFrameB = RetargetHkrgConstraintFrame(
                sourceFrameB * sourceBodyB, sourceBoneWorld, targetBoneWorld, sourceBoneB, targetBoneB, inverseTargetB);
            SetConstraintTransform(targetTransforms, "transformA", targetFrameA);
            SetConstraintTransform(targetTransforms, "transformB", targetFrameB);
        }
    }

    private static Matrix4x4 RetargetHkrgConstraintFrame(
        Matrix4x4 sourceWorldFrame,
        IReadOnlyList<Matrix4x4> sourceBoneWorld,
        IReadOnlyList<Matrix4x4> targetBoneWorld,
        int sourceBoneIndex,
        int targetBoneIndex,
        Matrix4x4 inverseTargetBody)
    {
        if (sourceBoneIndex >= 0 && sourceBoneIndex < sourceBoneWorld.Count &&
            targetBoneIndex >= 0 && targetBoneIndex < targetBoneWorld.Count &&
            Matrix4x4.Invert(sourceBoneWorld[sourceBoneIndex], out var inverseSourceBone))
        {
            sourceWorldFrame = sourceWorldFrame * inverseSourceBone * targetBoneWorld[targetBoneIndex];
        }

        return sourceWorldFrame * inverseTargetBody;
    }
    // Keeps the authored joint axes and angular limits intact, while mapping only
    // the two anchor positions from a reference ragdoll into this skeleton.
    public void RetargetHkrgConstraintAnchorsFrom(HkclService source)
    {
        if (!IsHkrg || !source.IsHkrg)
            throw new InvalidOperationException("Both documents must be HKRG files.");

        var targetBodies = GetHkrgRigidBodies();
        var sourceBodies = source.GetHkrgRigidBodies();
        var targetConstraints = GetHkrgConstraints();
        var sourceConstraints = source.GetHkrgConstraints();
        if (targetBodies.Count != sourceBodies.Count || targetConstraints.Count != sourceConstraints.Count)
            throw new InvalidOperationException("HKRG source and target topology do not match.");

        var targetBodyToBone = GetHkrgBodyToBoneMap();
        var sourceBodyToBone = source.GetHkrgBodyToBoneMap();
        var targetBoneWorld = GetHkrgSkeletonWorldTransforms(GetHkrgRagdollSkeleton());
        var sourceBoneWorld = GetHkrgSkeletonWorldTransforms(source.GetHkrgRagdollSkeleton());

        for (var index = 0; index < targetConstraints.Count; index++)
        {
            var targetConstraint = targetConstraints[index];
            var sourceConstraint = sourceConstraints[index];
            var targetBodyAIndex = GetHkrgConstraintBodyIndex(targetConstraint, targetBodies, "entities_0");
            var targetBodyBIndex = GetHkrgConstraintBodyIndex(targetConstraint, targetBodies, "entities_1");
            var sourceBodyAIndex = GetHkrgConstraintBodyIndex(sourceConstraint, sourceBodies, "entities_0");
            var sourceBodyBIndex = GetHkrgConstraintBodyIndex(sourceConstraint, sourceBodies, "entities_1");
            if (targetBodyAIndex < 0 || targetBodyBIndex < 0 || sourceBodyAIndex < 0 || sourceBodyBIndex < 0)
                continue;

            var targetAtoms = GetValue(GetValue(targetConstraint, "data") ?? targetConstraint, "atoms");
            var targetTransforms = GetValue(targetAtoms, "transforms");
            var sourceAtoms = GetValue(GetValue(sourceConstraint, "data") ?? sourceConstraint, "atoms");
            var sourceTransforms = GetValue(sourceAtoms, "transforms");
            if (targetTransforms == null || sourceTransforms == null ||
                GetValue(targetTransforms, "transformA") is not Matrix4x4 targetFrameA ||
                GetValue(targetTransforms, "transformB") is not Matrix4x4 targetFrameB ||
                GetValue(sourceTransforms, "transformA") is not Matrix4x4 sourceFrameA ||
                GetValue(sourceTransforms, "transformB") is not Matrix4x4 sourceFrameB)
                continue;

            var targetBodyA = GetHkrgAffineTransform(GetHkrgBodyTransform(targetBodies[targetBodyAIndex]));
            var targetBodyB = GetHkrgAffineTransform(GetHkrgBodyTransform(targetBodies[targetBodyBIndex]));
            var sourceBodyA = GetHkrgAffineTransform(GetHkrgBodyTransform(sourceBodies[sourceBodyAIndex]));
            var sourceBodyB = GetHkrgAffineTransform(GetHkrgBodyTransform(sourceBodies[sourceBodyBIndex]));
            sourceBodyToBone.TryGetValue(sourceBodyAIndex, out var sourceBoneA);
            sourceBodyToBone.TryGetValue(sourceBodyBIndex, out var sourceBoneB);
            targetBodyToBone.TryGetValue(targetBodyAIndex, out var targetBoneA);
            targetBodyToBone.TryGetValue(targetBodyBIndex, out var targetBoneB);

            var sourceAnchorA = GetHkrgBodyCenter(sourceFrameA * sourceBodyA);
            var sourceAnchorB = GetHkrgBodyCenter(sourceFrameB * sourceBodyB);
            var targetAnchorA = RetargetHkrgConstraintPoint(sourceAnchorA, sourceBoneWorld, targetBoneWorld, sourceBoneA, targetBoneA);
            var targetAnchorB = RetargetHkrgConstraintPoint(sourceAnchorB, sourceBoneWorld, targetBoneWorld, sourceBoneB, targetBoneB);
            SetConstraintTransform(targetTransforms, "transformA", SetHkrgConstraintLocalPosition(targetFrameA, targetBodyA, targetAnchorA));
            SetConstraintTransform(targetTransforms, "transformB", SetHkrgConstraintLocalPosition(targetFrameB, targetBodyB, targetAnchorB));
        }
    }

    private static Vector3 RetargetHkrgConstraintPoint(
        Vector3 sourcePoint,
        IReadOnlyList<Matrix4x4> sourceBoneWorld,
        IReadOnlyList<Matrix4x4> targetBoneWorld,
        int sourceBoneIndex,
        int targetBoneIndex)
    {
        if (sourceBoneIndex >= 0 && sourceBoneIndex < sourceBoneWorld.Count &&
            targetBoneIndex >= 0 && targetBoneIndex < targetBoneWorld.Count &&
            Matrix4x4.Invert(sourceBoneWorld[sourceBoneIndex], out var inverseSourceBone))
        {
            return Vector3.Transform(sourcePoint, inverseSourceBone * targetBoneWorld[targetBoneIndex]);
        }

        return sourcePoint;
    }

    private static Matrix4x4 SetHkrgConstraintLocalPosition(Matrix4x4 localFrame, Matrix4x4 bodyWorld, Vector3 worldPosition)
    {
        var bodyRotation = ClearTranslation(NormalizeAffineFrame(bodyWorld));
        if (!Matrix4x4.Invert(bodyRotation, out var inverseBodyRotation))
            return localFrame;
        var bodyPosition = GetHkrgBodyCenter(bodyWorld);
        var localPosition = Vector3.Transform(worldPosition - bodyPosition, inverseBodyRotation);
        return new Matrix4x4(
            localFrame.M11, localFrame.M12, localFrame.M13, localFrame.M14,
            localFrame.M21, localFrame.M22, localFrame.M23, localFrame.M24,
            localFrame.M31, localFrame.M32, localFrame.M33, localFrame.M34,
            localPosition.X, localPosition.Y, localPosition.Z, localFrame.M44);
    }
    public IReadOnlyList<HkrgConstraintPoseDiagnostic> GetHkrgConstraintPoseDiagnostics()
    {
        if (!IsHkrg)
            return Array.Empty<HkrgConstraintPoseDiagnostic>();

        var bodies = GetHkrgRigidBodies();
        var constraints = GetHkrgConstraints();
        var bodyToBone = GetHkrgBodyToBoneMap();
        var boneRows = GetHkrgBoneRows();
        var boneWorld = GetHkrgSkeletonWorldTransforms(GetHkrgRagdollSkeleton());
        var result = new List<HkrgConstraintPoseDiagnostic>(constraints.Count);

        for (var index = 0; index < constraints.Count; index++)
        {
            var constraint = constraints[index];
            var bodyAIndex = GetHkrgConstraintBodyIndex(constraint, bodies, "entities_0");
            var bodyBIndex = GetHkrgConstraintBodyIndex(constraint, bodies, "entities_1");
            var data = GetValue(constraint, "data") ?? constraint;
            var atoms = GetValue(data, "atoms");
            var transforms = GetValue(atoms, "transforms");
            var frameA = GetValue(transforms, "transformA") is Matrix4x4 a ? GetHkrgAffineTransform(a) : Matrix4x4.Identity;
            var frameB = GetValue(transforms, "transformB") is Matrix4x4 b ? GetHkrgAffineTransform(b) : Matrix4x4.Identity;
            var bodyA = bodyAIndex >= 0 && bodyAIndex < bodies.Count
                ? GetHkrgAffineTransform(GetHkrgBodyTransform(bodies[bodyAIndex]))
                : Matrix4x4.Identity;
            var bodyB = bodyBIndex >= 0 && bodyBIndex < bodies.Count
                ? GetHkrgAffineTransform(GetHkrgBodyTransform(bodies[bodyBIndex]))
                : Matrix4x4.Identity;
            var worldA = frameA * bodyA;
            var worldB = frameB * bodyB;
            bodyToBone.TryGetValue(bodyAIndex, out var boneAIndex);
            bodyToBone.TryGetValue(bodyBIndex, out var boneBIndex);
            var motor = GetValue(atoms, "ragdollMotors");
            var motorTarget = GetValue(motor, "target_bRca") is Matrix4x4 target ? target : Matrix4x4.Identity;
            var twist = GetValue(atoms, "twistLimit");
            var cone = GetValue(atoms, "coneLimit");
            var planes = GetValue(atoms, "planesLimit");
            var hinge = GetValue(atoms, "angLimit");

            result.Add(new HkrgConstraintPoseDiagnostic(
                index,
                GetString(constraint, "name") ?? $"Constraint {index}",
                bodyAIndex,
                bodyBIndex,
                boneAIndex,
                boneBIndex,
                boneRows.ElementAtOrDefault(boneAIndex)?.Name ?? string.Empty,
                boneRows.ElementAtOrDefault(boneBIndex)?.Name ?? string.Empty,
                Vector3.Distance(GetHkrgBodyCenter(worldA), GetHkrgBodyCenter(worldB)),
                RotationAngleDegrees(worldA, worldB),
                boneAIndex >= 0 && boneAIndex < boneWorld.Length && boneBIndex >= 0 && boneBIndex < boneWorld.Length
                    ? RotationAngleDegrees(boneWorld[boneAIndex], boneWorld[boneBIndex])
                    : 0.0f,
                GetRelativeRotation(worldA, worldB),
                boneAIndex >= 0 && boneAIndex < boneWorld.Length && boneBIndex >= 0 && boneBIndex < boneWorld.Length
                    ? GetRelativeRotation(boneWorld[boneAIndex], boneWorld[boneBIndex])
                    : Quaternion.Identity,
                GetValue(motor, "isEnabled") is bool isEnabled && isEnabled,
                RotationAngleDegrees(Matrix4x4.Identity, motorTarget),
                Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(NormalizeAffineFrame(motorTarget))),
                GetOptionalSingle(twist, "minAngle"),
                GetOptionalSingle(twist, "maxAngle"),
                GetOptionalSingle(cone, "minAngle"),
                GetOptionalSingle(cone, "maxAngle"),
                GetOptionalSingle(planes, "minAngle"),
                GetOptionalSingle(planes, "maxAngle"),
                GetOptionalSingle(hinge, "minAngle"),
                GetOptionalSingle(hinge, "maxAngle")));
        }

        return result;
    }

    private static float RotationAngleDegrees(Matrix4x4 from, Matrix4x4 to) =>
        RotationAngleDegrees(
            Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(NormalizeAffineFrame(from))),
            Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(NormalizeAffineFrame(to))));

    private static Quaternion GetRelativeRotation(Matrix4x4 from, Matrix4x4 to)
    {
        var fromRotation = ClearTranslation(NormalizeAffineFrame(from));
        var toRotation = ClearTranslation(NormalizeAffineFrame(to));
        if (!Matrix4x4.Invert(fromRotation, out var inverseFrom))
            return Quaternion.Identity;
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(toRotation * inverseFrom));
    }

    private static float RotationAngleDegrees(Quaternion from, Quaternion to)
    {
        var dot = Math.Clamp(MathF.Abs(Quaternion.Dot(from, to)), 0.0f, 1.0f);
        return 2.0f * MathF.Acos(dot) * (180.0f / MathF.PI);
    }
    private Dictionary<int, int> GetHkrgBodyToBoneMap()
    {
        var ragdoll = GetHkrgRagdollInstance();
        var boneToBody = GetList(GetValue(ragdoll, "boneToRigidBodyMap")) ?? Array.Empty<object>();
        return boneToBody
            .Select((bodyIndex, boneIndex) => new { BodyIndex = ToInt(bodyIndex, -1), BoneIndex = boneIndex })
            .Where(entry => entry.BodyIndex >= 0)
            .GroupBy(entry => entry.BodyIndex)
            .ToDictionary(group => group.Key, group => group.First().BoneIndex);
    }

    private static Matrix4x4[] GetHkrgSkeletonWorldTransforms(object? skeleton)
    {
        var poses = GetList(GetValue(skeleton, "referencePose")) ?? Array.Empty<object>();
        var parents = GetList(GetValue(skeleton, "parentIndices")) ?? Array.Empty<object>();
        var worldTransforms = new Matrix4x4[poses.Count];
        for (var index = 0; index < poses.Count; index++)
        {
            var local = PoseToLocalMatrix(poses[index]);
            var parentIndex = ToInt(parents.ElementAtOrDefault(index), -1);
            worldTransforms[index] = parentIndex >= 0 && parentIndex < index
                ? local * worldTransforms[parentIndex]
                : local;
        }

        return worldTransforms;
    }

    private static Matrix4x4 BuildHkrgJointWorldFrame(
        IReadOnlyList<Matrix4x4> boneWorld,
        IReadOnlyList<object> parents,
        int boneA,
        int boneB,
        Vector3 fallbackA,
        Vector3 fallbackB)
    {
        var frameBone = ChooseHkrgConstraintFrameBone(parents, boneA, boneB);
        if (frameBone >= 0 && frameBone < boneWorld.Count)
            return NormalizeAffineFrame(boneWorld[frameBone]);

        var position = Vector3.Lerp(fallbackA, fallbackB, 0.5f);
        return Matrix4x4.CreateTranslation(position);
    }

    private static int ChooseHkrgConstraintFrameBone(IReadOnlyList<object> parents, int boneA, int boneB)
    {
        var parentA = boneA >= 0 && boneA < parents.Count ? ToInt(parents[boneA], -1) : -1;
        var parentB = boneB >= 0 && boneB < parents.Count ? ToInt(parents[boneB], -1) : -1;

        if (parentA == boneB)
            return boneA;
        if (parentB == boneA)
            return boneB;
        if (boneB >= 0)
            return boneB;
        return boneA;
    }

    private static Vector3 GetHkrgBodyCenter(Matrix4x4 bodyTransform) =>
        new(bodyTransform.M41, bodyTransform.M42, bodyTransform.M43);

    private static Matrix4x4 NormalizeAffineFrame(Matrix4x4 frame)
    {
        var x = NormalizeOr(new Vector3(frame.M11, frame.M12, frame.M13), Vector3.UnitX);
        var rawY = NormalizeOr(new Vector3(frame.M21, frame.M22, frame.M23), Vector3.UnitY);
        var y = rawY - x * Vector3.Dot(rawY, x);
        if (y.LengthSquared() < 0.000001f)
            y = BuildPerpendicularUnit(x);
        else
            y = Vector3.Normalize(y);

        var z = Vector3.Cross(x, y);
        if (z.LengthSquared() < 0.000001f)
            z = Vector3.UnitZ;
        else
            z = Vector3.Normalize(z);
        y = Vector3.Normalize(Vector3.Cross(z, x));

        return new Matrix4x4(
            x.X, x.Y, x.Z, 0.0f,
            y.X, y.Y, y.Z, 0.0f,
            z.X, z.Y, z.Z, 0.0f,
            frame.M41, frame.M42, frame.M43, 1.0f);
    }

    private static Vector3 BuildPerpendicularUnit(Vector3 axis)
    {
        var helper = MathF.Abs(Vector3.Dot(axis, Vector3.UnitX)) < 0.75f
            ? Vector3.UnitX
            : Vector3.UnitY;
        var perpendicular = Vector3.Cross(axis, helper);
        return perpendicular.LengthSquared() < 0.000001f
            ? Vector3.UnitZ
            : Vector3.Normalize(perpendicular);
    }

    private static Matrix4x4 ClearTranslation(Matrix4x4 matrix) => new(
        matrix.M11, matrix.M12, matrix.M13, 0.0f,
        matrix.M21, matrix.M22, matrix.M23, 0.0f,
        matrix.M31, matrix.M32, matrix.M33, 0.0f,
        0.0f, 0.0f, 0.0f, 1.0f);

    private static void SetConstraintTransform(object transforms, string field, Matrix4x4 value)
    {
        var existing = GetValue(transforms, field) is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
        SetValue(transforms, field, PreserveConstraintTransformPadding(existing, value));
    }

    private static Matrix4x4 PreserveConstraintTransformPadding(Matrix4x4 existing, Matrix4x4 value) => new(
        value.M11, value.M12, value.M13, existing.M14,
        value.M21, value.M22, value.M23, existing.M24,
        value.M31, value.M32, value.M33, existing.M34,
        value.M41, value.M42, value.M43, existing.M44);
    // The editor exposes the common limits, but the game consumes every atom
    // stored below hkpConstraintData. Keep this inspection path read-only so
    // retargeting work can be based on the actual binary values, not guesses.
    public IReadOnlyList<HkrgConstraintAtomValue> GetHkrgConstraintAtomValues()
    {
        if (!IsHkrg)
            return Array.Empty<HkrgConstraintAtomValue>();

        var result = new List<HkrgConstraintAtomValue>();
        foreach (var (constraint, index) in GetHkrgConstraints().Select((constraint, index) => (constraint, index)))
        {
            var data = GetValue(constraint, "data") ?? constraint;
            var atoms = GetValue(data, "atoms");
            if (atoms == null)
                continue;

            var name = GetString(constraint, "name") ?? $"Constraint {index}";
            FlattenHkrgConstraintAtom(atoms, "atoms", 0, result, index, name, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        return result;
    }

    private static void FlattenHkrgConstraintAtom(
        object? value,
        string path,
        int depth,
        ICollection<HkrgConstraintAtomValue> destination,
        int constraintIndex,
        string constraintName,
        ISet<object> visited)
    {
        if (value == null)
        {
            destination.Add(new HkrgConstraintAtomValue(constraintIndex, constraintName, path, "<null>"));
            return;
        }

        if (depth > 8)
        {
            destination.Add(new HkrgConstraintAtomValue(constraintIndex, constraintName, path, "<depth-limit>"));
            return;
        }

        switch (value)
        {
            case Matrix4x4 matrix:
                AddMatrixAtomValues(matrix, path, destination, constraintIndex, constraintName);
                return;
            case Vector4 vector:
                AddVector4AtomValues(vector, path, destination, constraintIndex, constraintName);
                return;
            case Vector3 vector:
                AddVector3AtomValues(vector, path, destination, constraintIndex, constraintName);
                return;
            case Quaternion quaternion:
                AddVector4AtomValues(new Vector4(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W), path, destination, constraintIndex, constraintName);
                return;
            case string text:
                destination.Add(new HkrgConstraintAtomValue(constraintIndex, constraintName, path, text));
                return;
            case Enum enumValue:
                destination.Add(new HkrgConstraintAtomValue(constraintIndex, constraintName, path, enumValue.ToString()));
                return;
            case bool boolean:
                destination.Add(new HkrgConstraintAtomValue(constraintIndex, constraintName, path, boolean ? "true" : "false"));
                return;
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                destination.Add(new HkrgConstraintAtomValue(constraintIndex, constraintName, path, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty));
                return;
            case IEnumerable enumerable when value is not string:
            {
                var itemIndex = 0;
                foreach (var item in enumerable)
                {
                    FlattenHkrgConstraintAtom(item, $"{path}[{itemIndex}]", depth + 1, destination, constraintIndex, constraintName, visited);
                    itemIndex++;
                    if (itemIndex >= 128)
                    {
                        destination.Add(new HkrgConstraintAtomValue(constraintIndex, constraintName, $"{path}[...]", "<truncated>"));
                        break;
                    }
                }
                return;
            }
        }

        if (!value.GetType().IsValueType && !visited.Add(value))
            return;

        var fields = value.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Where(field => !field.IsStatic && !string.Equals(field.Name, "Signature", StringComparison.OrdinalIgnoreCase))
            .OrderBy(field => field.Name, StringComparer.Ordinal);
        foreach (var field in fields)
            FlattenHkrgConstraintAtom(field.GetValue(value), $"{path}.{field.Name}", depth + 1, destination, constraintIndex, constraintName, visited);
    }

    private static void AddMatrixAtomValues(Matrix4x4 value, string path, ICollection<HkrgConstraintAtomValue> destination, int index, string name)
    {
        foreach (var (component, componentValue) in new[]
        {
            ("M11", value.M11), ("M12", value.M12), ("M13", value.M13), ("M14", value.M14),
            ("M21", value.M21), ("M22", value.M22), ("M23", value.M23), ("M24", value.M24),
            ("M31", value.M31), ("M32", value.M32), ("M33", value.M33), ("M34", value.M34),
            ("M41", value.M41), ("M42", value.M42), ("M43", value.M43), ("M44", value.M44)
        })
            destination.Add(new HkrgConstraintAtomValue(index, name, $"{path}.{component}", componentValue.ToString("R", CultureInfo.InvariantCulture)));
    }

    private static void AddVector4AtomValues(Vector4 value, string path, ICollection<HkrgConstraintAtomValue> destination, int index, string name)
    {
        destination.Add(new HkrgConstraintAtomValue(index, name, $"{path}.X", value.X.ToString("R", CultureInfo.InvariantCulture)));
        destination.Add(new HkrgConstraintAtomValue(index, name, $"{path}.Y", value.Y.ToString("R", CultureInfo.InvariantCulture)));
        destination.Add(new HkrgConstraintAtomValue(index, name, $"{path}.Z", value.Z.ToString("R", CultureInfo.InvariantCulture)));
        destination.Add(new HkrgConstraintAtomValue(index, name, $"{path}.W", value.W.ToString("R", CultureInfo.InvariantCulture)));
    }

    private static void AddVector3AtomValues(Vector3 value, string path, ICollection<HkrgConstraintAtomValue> destination, int index, string name)
    {
        destination.Add(new HkrgConstraintAtomValue(index, name, $"{path}.X", value.X.ToString("R", CultureInfo.InvariantCulture)));
        destination.Add(new HkrgConstraintAtomValue(index, name, $"{path}.Y", value.Y.ToString("R", CultureInfo.InvariantCulture)));
        destination.Add(new HkrgConstraintAtomValue(index, name, $"{path}.Z", value.Z.ToString("R", CultureInfo.InvariantCulture)));
    }
    private IReadOnlyList<object> GetHkrgConstraints()
    {
        var ragdollConstraints = GetList(GetValue(GetHkrgRagdollInstance(), "constraints"));
        if (ragdollConstraints is { Count: > 0 })
            return ragdollConstraints.ToArray();

        return GetHkrgPhysicsSystems()
            .SelectMany(system => GetList(GetValue(system, "constraints")) ?? Array.Empty<object>())
            .ToArray();
    }

    private static int GetHkrgConstraintBodyIndex(object constraint, IReadOnlyList<object> bodies, string entityField)
    {
        var entity = GetValue(constraint, entityField);
        return entity == null ? -1 : IndexOfReference(bodies, entity);
    }

    private static void ApplyAngularLimit(object? atom, float? minAngle, float? maxAngle, float? tau, float? damping)
    {
        SetOptionalSingle(atom, "minAngle", minAngle);
        SetOptionalSingle(atom, "maxAngle", maxAngle);
        SetOptionalSingle(atom, "angularLimitsTauFactor", tau);
        SetOptionalSingle(atom, "angularLimitsDampFactor", damping);
    }

    private static float? GetOptionalSingle(object? source, string field)
    {
        var value = GetValue(source, field);
        if (value == null)
            return null;

        try
        {
            return Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static void SetOptionalSingle(object? target, string field, float? value)
    {
        if (!value.HasValue || GetValue(target, field) == null)
            return;

        SetValue(target, field, value.Value);
    }
    private IReadOnlyList<ColliderEditRow> GetHkrgRigidBodyRows()
    {
        if (!IsHkrg)
            return Array.Empty<ColliderEditRow>();

        var ragdoll = GetHkrgRagdollInstance();
        var bodyToBone = GetHkrgBodyToBoneMap();
        var bones = GetList(GetValue(GetHkrgRagdollSkeleton(), "bones")) ?? Array.Empty<object>();

        return GetHkrgRigidBodies().Select((body, index) =>
        {
            var collidable = GetValue(body, "collidable");
            var shape = GetValue(collidable, "shape");
            var shapeType = shape?.GetType().Name ?? "hkpShape";
            var localStart = GetValue(shape, "vertexA") is Vector4 vertexA
                ? new Vector3(vertexA.X, vertexA.Y, vertexA.Z)
                : Vector3.Zero;
            var localEnd = GetValue(shape, "vertexB") is Vector4 vertexB
                ? new Vector3(vertexB.X, vertexB.Y, vertexB.Z)
                : localStart;
            var rawTransform = GetHkrgBodyTransform(body);
            var transform = GetHkrgAffineTransform(rawTransform);
            var boneIndex = bodyToBone.TryGetValue(index, out var mappedBone) ? mappedBone : -1;
            return new ColliderEditRow
            {
                Index = index,
                Name = GetString(body, "name") ?? $"Rigid body {index}",
                ShapeType = shapeType switch
                {
                    "hkpCapsuleShape" => "Capsule",
                    "hkpSphereShape" => "Sphere",
                    "hkpBoxShape" => "Box",
                    _ => shapeType
                },
                BoneIndex = boneIndex,
                BoneName = boneIndex >= 0 && boneIndex < bones.Count
                    ? GetString(bones[boneIndex], "name") ?? string.Empty
                    : string.Empty,
                StartX = Vector3.Transform(localStart, transform).X,
                StartY = Vector3.Transform(localStart, transform).Y,
                StartZ = Vector3.Transform(localStart, transform).Z,
                EndX = Vector3.Transform(localEnd, transform).X,
                EndY = Vector3.Transform(localEnd, transform).Y,
                EndZ = Vector3.Transform(localEnd, transform).Z,
                Radius = Convert.ToSingle(GetValue(shape, "radius") ?? 0.0f),
                // Keep the raw Havok matrix for serialization. Its fourth
                // column is not an affine Matrix4x4 column and is preserved.
                Transform = rawTransform
            };
        }).ToArray();
    }

    private static Matrix4x4 GetHkrgBodyTransform(object rigidBody)
    {
        var motion = GetValue(rigidBody, "motion");
        var motionState = GetValue(motion, "motionState");
        return GetValue(motionState, "transform") is Matrix4x4 transform
            ? transform
            : Matrix4x4.Identity;
    }

    // hkpMotionState stores an hkTransform in a Matrix4x4 container. The
    // fourth components of the rotation basis are non-affine Havok data, not
    // projection terms. Preserve them for serialization, but never pass them
    // through Matrix4x4.Invert when converting world vertices to local shape
    // coordinates.
    private static Matrix4x4 GetHkrgAffineTransform(Matrix4x4 raw) => new(
        raw.M11, raw.M12, raw.M13, 0.0f,
        raw.M21, raw.M22, raw.M23, 0.0f,
        raw.M31, raw.M32, raw.M33, 0.0f,
        raw.M41, raw.M42, raw.M43, 1.0f);

    private static Matrix4x4 ApplyHkrgAffineDelta(Matrix4x4 raw, Matrix4x4 delta)
    {
        var transformed = GetHkrgAffineTransform(raw) * delta;
        return new Matrix4x4(
            transformed.M11, transformed.M12, transformed.M13, raw.M14,
            transformed.M21, transformed.M22, transformed.M23, raw.M24,
            transformed.M31, transformed.M32, transformed.M33, raw.M34,
            transformed.M41, transformed.M42, transformed.M43, raw.M44);
    }

    // Applies a ragdoll-bone world delta to the viewport row while retaining
    // native hkTransform padding/components needed for a safe reserialize.
    public void ApplyHkrgRigidBodyWorldDelta(ColliderEditRow row, Matrix4x4 delta)
    {
        if (!IsHkrg)
            return;

        var start = Vector3.Transform(new Vector3(row.StartX, row.StartY, row.StartZ), delta);
        var end = Vector3.Transform(new Vector3(row.EndX, row.EndY, row.EndZ), delta);
        row.StartX = start.X;
        row.StartY = start.Y;
        row.StartZ = start.Z;
        row.EndX = end.X;
        row.EndY = end.Y;
        row.EndZ = end.Z;
        row.Transform = ApplyHkrgAffineDelta(row.Transform, delta);
    }

    // Re-centers capsule bodies after their endpoints change. Havok stores the
    // local center of mass separately from the visible capsule geometry.
    // Existing Link/Mipha files show it at the exact midpoint of vertexA/B.
    public int RebuildHkrgCapsuleCentersOfMass()
    {
        if (!IsHkrg)
            return 0;

        var updated = 0;
        foreach (var body in GetHkrgRigidBodies())
        {
            var shape = GetValue(GetValue(body, "collidable"), "shape");
            var motionState = GetValue(GetValue(body, "motion"), "motionState");
            if (shape == null || motionState == null ||
                GetValue(shape, "vertexA") is not Vector4 vertexA ||
                GetValue(shape, "vertexB") is not Vector4 vertexB ||
                GetValue(motionState, "sweptTransform_4") is not Vector4 existing)
            {
                continue;
            }

            var center = (new Vector3(vertexA.X, vertexA.Y, vertexA.Z) +
                          new Vector3(vertexB.X, vertexB.Y, vertexB.Z)) * 0.5f;
            SetValue(motionState, "sweptTransform_4", new Vector4(center, existing.W));
            updated++;
        }

        return updated;
    }

    // hkpMotionState stores the body center of mass separately from the
    // visible body transform. Retargeting transform alone leaves Havok with
    // stale Link positions/orientations to integrate toward at runtime.
    public void RebuildHkrgMotionSweepsFromTransforms()
    {
        if (!IsHkrg)
            return;

        foreach (var body in GetHkrgRigidBodies())
        {
            var motionState = GetValue(GetValue(body, "motion"), "motionState");
            if (motionState == null)
                continue;

            var transform = GetHkrgAffineTransform(GetHkrgBodyTransform(body));
            var centerOfMassLocal = GetValue(motionState, "sweptTransform_4") is Vector4 local
                ? new Vector3(local.X, local.Y, local.Z)
                : Vector3.Zero;
            var centerOfMassWorld = Vector3.Transform(centerOfMassLocal, transform);
            SetHkrgMotionPosition(motionState, "sweptTransform_0", centerOfMassWorld);
            SetHkrgMotionPosition(motionState, "sweptTransform_1", centerOfMassWorld);

            var orientation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(NormalizeAffineFrame(transform)));
            if (GetValue(motionState, "sweptTransform_2") is Vector4 previous &&
                Quaternion.Dot(orientation, new Quaternion(previous.X, previous.Y, previous.Z, previous.W)) < 0.0f)
            {
                orientation = Quaternion.Negate(orientation);
            }

            SetValue(motionState, "sweptTransform_2", new Vector4(orientation.X, orientation.Y, orientation.Z, orientation.W));
            SetValue(motionState, "sweptTransform_3", new Vector4(orientation.X, orientation.Y, orientation.Z, orientation.W));
        }
    }

    public float GetHkrgMotionSweepPositionError()
    {
        if (!IsHkrg)
            return 0.0f;

        var maximumError = 0.0f;
        foreach (var body in GetHkrgRigidBodies())
        {
            var motionState = GetValue(GetValue(body, "motion"), "motionState");
            if (motionState == null || GetValue(motionState, "sweptTransform_0") is not Vector4 stored)
                continue;

            var transform = GetHkrgAffineTransform(GetHkrgBodyTransform(body));
            var centerOfMassLocal = GetValue(motionState, "sweptTransform_4") is Vector4 local
                ? new Vector3(local.X, local.Y, local.Z)
                : Vector3.Zero;
            var expected = Vector3.Transform(centerOfMassLocal, transform);
            maximumError = MathF.Max(maximumError, Vector3.Distance(expected, new Vector3(stored.X, stored.Y, stored.Z)));
        }

        return maximumError;
    }

    private static void SetHkrgMotionPosition(object motionState, string field, Vector3 position)
    {
        if (GetValue(motionState, field) is Vector4 existing)
            SetValue(motionState, field, new Vector4(position, existing.W));
    }
    // The motion object contains more than the visible transform. In particular,
    // sweptTransform_* keeps Havok's center-of-mass pose used by constraints.
    // Copy it as a unit when retargeting a compatible ragdoll to another actor.
    public void AdoptHkrgRigidBodyMotionsFrom(HkclService source)
    {
        if (!IsHkrg || !source.IsHkrg)
            throw new InvalidOperationException("Both documents must be HKRG files.");

        var targetBodies = GetHkrgRigidBodies();
        var sourceBodies = source.GetHkrgRigidBodies();
        if (targetBodies.Count != sourceBodies.Count)
            throw new InvalidOperationException("HKRG source and target rigid-body counts do not match.");

        for (var index = 0; index < targetBodies.Count; index++)
        {
            var sourceMotion = GetValue(sourceBodies[index], "motion");
            if (sourceMotion == null || GetValue(targetBodies[index], "motion") == null)
                continue;

            SetValue(targetBodies[index], "motion", CloneForCurrentGraph(sourceMotion));
        }
    }

    private void UpdateHkrgRigidBodyRows(IEnumerable<ColliderEditRow> rows)
    {
        var bodies = GetHkrgRigidBodies();
        foreach (var row in rows)
        {
            if (row.Index < 0 || row.Index >= bodies.Count)
                continue;

            var body = bodies[row.Index];
            SetValue(body, "name", row.Name);
            var collidable = GetValue(body, "collidable");
            var shape = GetValue(collidable, "shape");
            if (shape == null)
                continue;

            // Rigid body motion owns the world transform; capsule vertices are
            // stored underneath it in local space. Persist the row transform
            // first so a body following its ragdoll bone does not bake that
            // movement into the shape vertices.
            var motionState = GetValue(GetValue(body, "motion"), "motionState");
            if (motionState != null)
                SetValue(motionState, "transform", row.Transform);
            var rawTransform = GetHkrgBodyTransform(body);
            var transform = GetHkrgAffineTransform(rawTransform);
            if (!Matrix4x4.Invert(transform, out var inverse))
                inverse = Matrix4x4.Identity;
            var start = Vector3.Transform(new Vector3(row.StartX, row.StartY, row.StartZ), inverse);
            var end = Vector3.Transform(new Vector3(row.EndX, row.EndY, row.EndZ), inverse);

            switch (shape.GetType().Name)
            {
                case "hkpCapsuleShape":
                {
                    var previousA = GetValue(shape, "vertexA") is Vector4 vertexA ? vertexA : Vector4.Zero;
                    var previousB = GetValue(shape, "vertexB") is Vector4 vertexB ? vertexB : Vector4.Zero;
                    SetValue(shape, "vertexA", new Vector4(start, row.Radius > 0.0f ? row.Radius : previousA.W));
                    SetValue(shape, "vertexB", new Vector4(end, row.Radius > 0.0f ? row.Radius : previousB.W));
                    SetValue(shape, "radius", row.Radius);
                    break;
                }
                case "hkpSphereShape":
                    SetValue(shape, "radius", row.Radius);
                    break;
            }
        }
    }

    private IReadOnlyList<object> GetHkrgSkeletonMappers()
    {
        if (!IsHkrg || _root == null)
            return Array.Empty<object>();

        return (GetList(GetValue(_root, "namedVariants")) ?? Array.Empty<object>())
            .Select(variant => GetValue(variant, "variant"))
            .Where(variant => variant?.GetType().Name == "hkaSkeletonMapper")
            .Cast<object>()
            .ToArray();
    }

    // Carries the authored model/ragdoll mapper offsets from source pose space
    // to target pose space. This avoids recomputing arbitrary mapping data
    // from scratch while ensuring a renamed/rescaled actor is not pulled
    // toward the source skeleton as animation begins to drive the mapper.
    public void RetargetHkrgSkeletonMappersForCurrentPose(HkclService source)
    {
        if (!IsHkrg || !source.IsHkrg)
            throw new InvalidOperationException("Both documents must be HKRG files.");

        var targets = GetHkrgSkeletonMappers();
        var sources = source.GetHkrgSkeletonMappers();
        if (targets.Count != sources.Count)
            throw new InvalidOperationException("HKRG mapper count does not match the source file.");

        for (var mapperIndex = 0; mapperIndex < targets.Count; mapperIndex++)
        {
            var targetMapping = GetValue(targets[mapperIndex], "mapping");
            var sourceMapping = GetValue(sources[mapperIndex], "mapping");
            var targetA = GetHkrgSkeletonWorldTransforms(GetValue(targetMapping, "skeletonA"));
            var targetB = GetHkrgSkeletonWorldTransforms(GetValue(targetMapping, "skeletonB"));
            var sourceA = GetHkrgSkeletonWorldTransforms(GetValue(sourceMapping, "skeletonA"));
            var sourceB = GetHkrgSkeletonWorldTransforms(GetValue(sourceMapping, "skeletonB"));

            var targetSimple = GetList(GetValue(targetMapping, "simpleMappings")) ?? Array.Empty<object>();
            var sourceSimple = GetList(GetValue(sourceMapping, "simpleMappings")) ?? Array.Empty<object>();
            if (targetSimple.Count != sourceSimple.Count)
                throw new InvalidOperationException($"HKRG mapper {mapperIndex} simple-mapping topology does not match the source file.");

            for (var mappingIndex = 0; mappingIndex < targetSimple.Count; mappingIndex++)
            {
                var targetEntry = targetSimple[mappingIndex];
                var sourceEntry = sourceSimple[mappingIndex];
                RetargetHkrgMapperTransform(
                    targetEntry,
                    sourceEntry,
                    "aFromBTransform",
                    ToInt(GetValue(sourceEntry, "boneA"), -1),
                    ToInt(GetValue(sourceEntry, "boneB"), -1),
                    sourceA,
                    sourceB,
                    targetA,
                    targetB);
            }

            var targetChains = GetList(GetValue(targetMapping, "chainMappings")) ?? Array.Empty<object>();
            var sourceChains = GetList(GetValue(sourceMapping, "chainMappings")) ?? Array.Empty<object>();
            if (targetChains.Count != sourceChains.Count)
                throw new InvalidOperationException($"HKRG mapper {mapperIndex} chain-mapping topology does not match the source file.");

            for (var chainIndex = 0; chainIndex < targetChains.Count; chainIndex++)
            {
                var targetEntry = targetChains[chainIndex];
                var sourceEntry = sourceChains[chainIndex];
                RetargetHkrgMapperTransform(
                    targetEntry,
                    sourceEntry,
                    "startAFromBTransform",
                    ToInt(GetValue(sourceEntry, "startBoneA"), -1),
                    ToInt(GetValue(sourceEntry, "startBoneB"), -1),
                    sourceA,
                    sourceB,
                    targetA,
                    targetB);
                RetargetHkrgMapperTransform(
                    targetEntry,
                    sourceEntry,
                    "endAFromBTransform",
                    ToInt(GetValue(sourceEntry, "endBoneA"), -1),
                    ToInt(GetValue(sourceEntry, "endBoneB"), -1),
                    sourceA,
                    sourceB,
                    targetA,
                    targetB);
            }
        }
    }

    // Preserves the original no-stretch mapper path: only the model-to-ragdoll
    // mapping is retargeted. This is the conservative path used before the
    // paired write-back mapping experiment.
    public int RetargetHkrgAnimationToRagdollLegMapperOffsetsForCurrentPose(HkclService source) =>
        RetargetHkrgLegMapperOffsetsForCurrentPose(source, includeForwardMapping: true, includeReverseMapping: false);

    // Retargets only the ragdoll-to-model mapper. This is a diagnostic path:
    // it isolates the write-back which can alter visible limb positions.
    public int RetargetHkrgReverseLegMapperOffsetsForCurrentPose(HkclService source) =>
        RetargetHkrgLegMapperOffsetsForCurrentPose(source, includeForwardMapping: false, includeReverseMapping: true);

    // Retargets both directions of leg/ankle mappings. The reverse mapper
    // writes ragdoll motion back to the model skeleton, so its translation
    // offsets can affect the visible limb length during gameplay.
    public int RetargetHkrgTwoWayLegMapperOffsetsForCurrentPose(HkclService source) =>
        RetargetHkrgLegMapperOffsetsForCurrentPose(source, includeForwardMapping: true, includeReverseMapping: true);

    private int RetargetHkrgLegMapperOffsetsForCurrentPose(
        HkclService source,
        bool includeForwardMapping,
        bool includeReverseMapping)
    {
        if (!IsHkrg || !source.IsHkrg)
            throw new InvalidOperationException("Both documents must be HKRG files.");

        var targetModel = GetHkrgModelSkeleton();
        var targetRagdoll = GetHkrgRagdollSkeleton();
        var sourceModel = source.GetHkrgModelSkeleton();
        var sourceRagdoll = source.GetHkrgRagdollSkeleton();
        var targets = GetHkrgSkeletonMappers();
        var sources = source.GetHkrgSkeletonMappers();
        if (targetModel == null || targetRagdoll == null || sourceModel == null || sourceRagdoll == null ||
            targets.Count != sources.Count)
        {
            throw new InvalidOperationException("HKRG model/ragdoll mapper topology does not match the source file.");
        }

        var updated = 0;
        for (var mapperIndex = 0; mapperIndex < targets.Count; mapperIndex++)
        {
            var targetMapping = GetValue(targets[mapperIndex], "mapping");
            var sourceMapping = GetValue(sources[mapperIndex], "mapping");
            var targetSkeletonA = GetValue(targetMapping, "skeletonA");
            var targetSkeletonB = GetValue(targetMapping, "skeletonB");
            var sourceSkeletonA = GetValue(sourceMapping, "skeletonA");
            var sourceSkeletonB = GetValue(sourceMapping, "skeletonB");
            var isModelToRagdoll =
                ReferenceEquals(targetSkeletonA, targetModel) &&
                ReferenceEquals(targetSkeletonB, targetRagdoll) &&
                ReferenceEquals(sourceSkeletonA, sourceModel) &&
                ReferenceEquals(sourceSkeletonB, sourceRagdoll);
            var isRagdollToModel =
                ReferenceEquals(targetSkeletonA, targetRagdoll) &&
                ReferenceEquals(targetSkeletonB, targetModel) &&
                ReferenceEquals(sourceSkeletonA, sourceRagdoll) &&
                ReferenceEquals(sourceSkeletonB, sourceModel);
            if (!(includeForwardMapping && isModelToRagdoll) && !(includeReverseMapping && isRagdollToModel))
                continue;

            var targetA = GetHkrgSkeletonWorldTransforms(targetSkeletonA);
            var targetB = GetHkrgSkeletonWorldTransforms(targetSkeletonB);
            var sourceA = GetHkrgSkeletonWorldTransforms(sourceSkeletonA);
            var sourceB = GetHkrgSkeletonWorldTransforms(sourceSkeletonB);
            var targetSimple = GetList(GetValue(targetMapping, "simpleMappings")) ?? Array.Empty<object>();
            var sourceSimple = GetList(GetValue(sourceMapping, "simpleMappings")) ?? Array.Empty<object>();
            if (targetSimple.Count != sourceSimple.Count)
                throw new InvalidOperationException("HKRG leg simple-mapping topology does not match the source file.");

            for (var mappingIndex = 0; mappingIndex < targetSimple.Count; mappingIndex++)
            {
                var sourceEntry = sourceSimple[mappingIndex];
                var boneA = ToInt(GetValue(sourceEntry, "boneA"), -1);
                var boneB = ToInt(GetValue(sourceEntry, "boneB"), -1);
                if (!IsHkrgLegBone(sourceSkeletonA, boneA) && !IsHkrgLegBone(sourceSkeletonB, boneB))
                    continue;

                RetargetHkrgMapperTransform(
                    targetSimple[mappingIndex], sourceEntry, "aFromBTransform", boneA, boneB,
                    sourceA, sourceB, targetA, targetB);
                updated++;
            }
        }

        return updated;
    }

    // Retargets the ragdoll-to-model leg mappings while preserving each
    // authored aFromB pivot correction in model/output-bone space. This is
    // deliberately separate from the basis-change path: right-side leg maps
    // contain large custom pivots that are not direct bone relations.
    public int RetargetHkrgReverseLegMapperOffsetsWithOutputLocalPivotsForCurrentPose(HkclService source)
    {
        if (!IsHkrg || !source.IsHkrg)
            throw new InvalidOperationException("Both documents must be HKRG files.");

        var targetModel = GetHkrgModelSkeleton();
        var targetRagdoll = GetHkrgRagdollSkeleton();
        var sourceModel = source.GetHkrgModelSkeleton();
        var sourceRagdoll = source.GetHkrgRagdollSkeleton();
        var targets = GetHkrgSkeletonMappers();
        var sources = source.GetHkrgSkeletonMappers();
        if (targetModel == null || targetRagdoll == null || sourceModel == null || sourceRagdoll == null ||
            targets.Count != sources.Count)
        {
            throw new InvalidOperationException("HKRG model/ragdoll mapper topology does not match the source file.");
        }

        var updated = 0;
        for (var mapperIndex = 0; mapperIndex < targets.Count; mapperIndex++)
        {
            var targetMapping = GetValue(targets[mapperIndex], "mapping");
            var sourceMapping = GetValue(sources[mapperIndex], "mapping");
            var targetSkeletonA = GetValue(targetMapping, "skeletonA");
            var targetSkeletonB = GetValue(targetMapping, "skeletonB");
            var sourceSkeletonA = GetValue(sourceMapping, "skeletonA");
            var sourceSkeletonB = GetValue(sourceMapping, "skeletonB");
            var isRagdollToModel =
                ReferenceEquals(targetSkeletonA, targetRagdoll) &&
                ReferenceEquals(targetSkeletonB, targetModel) &&
                ReferenceEquals(sourceSkeletonA, sourceRagdoll) &&
                ReferenceEquals(sourceSkeletonB, sourceModel);
            if (!isRagdollToModel)
                continue;

            var targetA = GetHkrgSkeletonWorldTransforms(targetSkeletonA);
            var targetB = GetHkrgSkeletonWorldTransforms(targetSkeletonB);
            var sourceA = GetHkrgSkeletonWorldTransforms(sourceSkeletonA);
            var sourceB = GetHkrgSkeletonWorldTransforms(sourceSkeletonB);
            var targetSimple = GetList(GetValue(targetMapping, "simpleMappings")) ?? Array.Empty<object>();
            var sourceSimple = GetList(GetValue(sourceMapping, "simpleMappings")) ?? Array.Empty<object>();
            if (targetSimple.Count != sourceSimple.Count)
                throw new InvalidOperationException("HKRG reverse leg simple-mapping topology does not match the source file.");

            for (var mappingIndex = 0; mappingIndex < targetSimple.Count; mappingIndex++)
            {
                var sourceEntry = sourceSimple[mappingIndex];
                var boneA = ToInt(GetValue(sourceEntry, "boneA"), -1);
                var boneB = ToInt(GetValue(sourceEntry, "boneB"), -1);
                if (!IsHkrgLegBone(sourceSkeletonA, boneA) && !IsHkrgLegBone(sourceSkeletonB, boneB))
                    continue;

                if (RetargetHkrgMapperTransformWithOutputLocalPivot(
                        targetSimple[mappingIndex], sourceEntry, "aFromBTransform", boneA, boneB,
                        sourceA, sourceB, targetA, targetB))
                {
                    updated++;
                }
            }
        }

        return updated;
    }

    // Keeps the mapper pair coherent after output-local pivot retargeting.
    // The forward mapper is rebuilt from the edited write-back mapper because
    // these entries are authored as inverse pairs in the source HKRG.
    public int RetargetHkrgTwoWayLegMapperOffsetsWithOutputLocalPivotsForCurrentPose(HkclService source)
    {
        var updated = RetargetHkrgReverseLegMapperOffsetsWithOutputLocalPivotsForCurrentPose(source);
        if (!IsHkrg)
            return updated;

        var model = GetHkrgModelSkeleton();
        var ragdoll = GetHkrgRagdollSkeleton();
        if (model == null || ragdoll == null)
            throw new InvalidOperationException("HKRG model/ragdoll skeletons are unavailable.");

        foreach (var mapper in GetHkrgSkeletonMappers())
        {
            var mapping = GetValue(mapper, "mapping");
            var skeletonA = GetValue(mapping, "skeletonA");
            var skeletonB = GetValue(mapping, "skeletonB");
            var isModelToRagdoll = ReferenceEquals(skeletonA, model) && ReferenceEquals(skeletonB, ragdoll);
            var isRagdollToModel = ReferenceEquals(skeletonA, ragdoll) && ReferenceEquals(skeletonB, model);
            if (!isModelToRagdoll && !isRagdollToModel)
                continue;

            var simpleMappings = GetList(GetValue(mapping, "simpleMappings")) ?? Array.Empty<object>();
            if (isModelToRagdoll)
            {
                var reverseMapper = GetHkrgSkeletonMappers().FirstOrDefault(candidate =>
                {
                    var candidateMapping = GetValue(candidate, "mapping");
                    return ReferenceEquals(GetValue(candidateMapping, "skeletonA"), ragdoll) &&
                           ReferenceEquals(GetValue(candidateMapping, "skeletonB"), model);
                });
                var reverseMappings = reverseMapper == null
                    ? Array.Empty<object>()
                    : GetList(GetValue(GetValue(reverseMapper, "mapping"), "simpleMappings")) ?? Array.Empty<object>();

                foreach (var forwardEntry in simpleMappings)
                {
                    var modelBone = ToInt(GetValue(forwardEntry, "boneA"), -1);
                    var ragdollBone = ToInt(GetValue(forwardEntry, "boneB"), -1);
                    if (!IsHkrgLegBone(ragdoll, ragdollBone))
                        continue;

                    var reverseEntry = reverseMappings.FirstOrDefault(candidate =>
                        ToInt(GetValue(candidate, "boneA"), -1) == ragdollBone &&
                        ToInt(GetValue(candidate, "boneB"), -1) == modelBone);
                    if (reverseEntry == null ||
                        GetValue(reverseEntry, "aFromBTransform") is not Matrix4x4 reverseRaw ||
                        GetValue(forwardEntry, "aFromBTransform") is not Matrix4x4 forwardRaw ||
                        !Matrix4x4.Invert(PoseToLocalMatrix(reverseRaw), out var inverseReverse))
                    {
                        continue;
                    }

                    SetValue(forwardEntry, "aFromBTransform", ToHkrgQsTransform(forwardRaw, inverseReverse));
                    updated++;
                }
            }
        }

        return updated;
    }
    // Rebuilds only leg/ankle simple mappings from the current two skeleton
    // reference poses, matching the setup path described for
    // hkaSkeletonMapperUtils. Unlike retargeting an authored correction, this
    // intentionally removes source-character pivot translations.
    public int RebuildHkrgLegMapperOffsetsFromCurrentPose()
    {
        if (!IsHkrg)
            throw new InvalidOperationException("The current document is not an HKRG file.");

        var updated = 0;
        foreach (var mapper in GetHkrgSkeletonMappers())
        {
            var mapping = GetValue(mapper, "mapping");
            var skeletonA = GetValue(mapping, "skeletonA");
            var skeletonB = GetValue(mapping, "skeletonB");
            var worldA = GetHkrgSkeletonWorldTransforms(skeletonA);
            var worldB = GetHkrgSkeletonWorldTransforms(skeletonB);
            var simpleMappings = GetList(GetValue(mapping, "simpleMappings")) ?? Array.Empty<object>();

            foreach (var simple in simpleMappings)
            {
                var boneA = ToInt(GetValue(simple, "boneA"), -1);
                var boneB = ToInt(GetValue(simple, "boneB"), -1);
                if (boneA < 0 || boneA >= worldA.Length || boneB < 0 || boneB >= worldB.Length ||
                    (!IsHkrgLegBone(skeletonA, boneA) && !IsHkrgLegBone(skeletonB, boneB)) ||
                    GetValue(simple, "aFromBTransform") is not Matrix4x4 existing ||
                    !Matrix4x4.Invert(worldB[boneB], out var inverseB))
                {
                    continue;
                }

                // This is the same direct B-to-A relation used by the mapper
                // diagnostics for Link's non-custom simple mappings.
                var aFromB = inverseB * worldA[boneA];
                SetValue(simple, "aFromBTransform", ToHkrgQsTransform(existing, aFromB));
                updated++;
            }
        }

        return updated;
    }
    private static bool RetargetHkrgMapperTransformWithOutputLocalPivot(
        object targetEntry,
        object sourceEntry,
        string field,
        int boneA,
        int boneB,
        IReadOnlyList<Matrix4x4> sourceA,
        IReadOnlyList<Matrix4x4> sourceB,
        IReadOnlyList<Matrix4x4> targetA,
        IReadOnlyList<Matrix4x4> targetB)
    {
        if (boneA < 0 || boneB < 0 ||
            boneA >= sourceA.Count || boneA >= targetA.Count ||
            boneB >= sourceB.Count || boneB >= targetB.Count ||
            GetValue(sourceEntry, field) is not Matrix4x4 sourceRaw ||
            GetValue(targetEntry, field) is not Matrix4x4 targetRaw ||
            !Matrix4x4.Invert(sourceB[boneB], out var inverseSourceB) ||
            !Matrix4x4.Invert(targetB[boneB], out var inverseTargetB))
        {
            return false;
        }

        var sourceExpected = inverseSourceB * sourceA[boneA];
        var targetExpected = inverseTargetB * targetA[boneA];
        if (!Matrix4x4.Invert(sourceExpected, out var inverseSourceExpected))
            return false;

        // aFromB maps output B model space into input A model space.
        // Keeping M * inverse(expected) retains the authored correction in B,
        // which is the visible model skeleton for this reverse mapper.
        var outputLocalCorrection = PoseToLocalMatrix(sourceRaw) * inverseSourceExpected;
        var retargeted = outputLocalCorrection * targetExpected;
        SetValue(targetEntry, field, ToHkrgQsTransform(targetRaw, retargeted));
        return true;
    }


    private static bool IsHkrgLegBone(object? skeleton, int boneIndex)
    {
        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var name = GetString(bones.ElementAtOrDefault(boneIndex), "name") ?? string.Empty;
        return name.Contains("Leg", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Ankle", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Foot", StringComparison.OrdinalIgnoreCase);
    }
    // hkaSkeletonMapper stores aFromB as the reference-pose transform that
    // converts a point from skeleton B model space into skeleton A model
    // space. Rebuild only mappings that already equal that direct relation.
    // Other entries are intentional authoring corrections (twist/assist
    // bones, chain end effectors, and translation locks) and must be kept.
    public int RetargetHkrgDirectMapperOffsetsForCurrentPose(HkclService source)
    {
        if (!IsHkrg || !source.IsHkrg)
            throw new InvalidOperationException("Both documents must be HKRG files.");

        var targets = GetHkrgSkeletonMappers();
        var sources = source.GetHkrgSkeletonMappers();
        if (targets.Count != sources.Count)
            throw new InvalidOperationException("HKRG mapper count does not match the source file.");

        var updated = 0;
        for (var mapperIndex = 0; mapperIndex < targets.Count; mapperIndex++)
        {
            var targetMapping = GetValue(targets[mapperIndex], "mapping");
            var sourceMapping = GetValue(sources[mapperIndex], "mapping");
            var targetA = GetHkrgSkeletonWorldTransforms(GetValue(targetMapping, "skeletonA"));
            var targetB = GetHkrgSkeletonWorldTransforms(GetValue(targetMapping, "skeletonB"));
            var sourceA = GetHkrgSkeletonWorldTransforms(GetValue(sourceMapping, "skeletonA"));
            var sourceB = GetHkrgSkeletonWorldTransforms(GetValue(sourceMapping, "skeletonB"));

            var targetSimple = GetList(GetValue(targetMapping, "simpleMappings")) ?? Array.Empty<object>();
            var sourceSimple = GetList(GetValue(sourceMapping, "simpleMappings")) ?? Array.Empty<object>();
            if (targetSimple.Count != sourceSimple.Count)
                throw new InvalidOperationException($"HKRG mapper {mapperIndex} simple-mapping topology does not match the source file.");

            for (var mappingIndex = 0; mappingIndex < targetSimple.Count; mappingIndex++)
            {
                var sourceEntry = sourceSimple[mappingIndex];
                updated += RetargetHkrgDirectMapperOffset(
                    targetSimple[mappingIndex], sourceEntry, "aFromBTransform",
                    ToInt(GetValue(sourceEntry, "boneA"), -1),
                    ToInt(GetValue(sourceEntry, "boneB"), -1),
                    sourceA, sourceB, targetA, targetB);
            }

            var targetChains = GetList(GetValue(targetMapping, "chainMappings")) ?? Array.Empty<object>();
            var sourceChains = GetList(GetValue(sourceMapping, "chainMappings")) ?? Array.Empty<object>();
            if (targetChains.Count != sourceChains.Count)
                throw new InvalidOperationException($"HKRG mapper {mapperIndex} chain-mapping topology does not match the source file.");

            for (var chainIndex = 0; chainIndex < targetChains.Count; chainIndex++)
            {
                var sourceEntry = sourceChains[chainIndex];
                updated += RetargetHkrgDirectMapperOffset(
                    targetChains[chainIndex], sourceEntry, "startAFromBTransform",
                    ToInt(GetValue(sourceEntry, "startBoneA"), -1),
                    ToInt(GetValue(sourceEntry, "startBoneB"), -1),
                    sourceA, sourceB, targetA, targetB);
                updated += RetargetHkrgDirectMapperOffset(
                    targetChains[chainIndex], sourceEntry, "endAFromBTransform",
                    ToInt(GetValue(sourceEntry, "endBoneA"), -1),
                    ToInt(GetValue(sourceEntry, "endBoneB"), -1),
                    sourceA, sourceB, targetA, targetB);
            }
        }

        return updated;
    }

    private static int RetargetHkrgDirectMapperOffset(
        object targetEntry,
        object sourceEntry,
        string field,
        int boneA,
        int boneB,
        IReadOnlyList<Matrix4x4> sourceA,
        IReadOnlyList<Matrix4x4> sourceB,
        IReadOnlyList<Matrix4x4> targetA,
        IReadOnlyList<Matrix4x4> targetB)
    {
        if (boneA < 0 || boneB < 0 ||
            boneA >= sourceA.Count || boneA >= targetA.Count ||
            boneB >= sourceB.Count || boneB >= targetB.Count ||
            GetValue(sourceEntry, field) is not Matrix4x4 sourceRaw ||
            GetValue(targetEntry, field) is not Matrix4x4 targetRaw ||
            !Matrix4x4.Invert(sourceB[boneB], out var inverseSourceB) ||
            !Matrix4x4.Invert(targetB[boneB], out var inverseTargetB))
        {
            return 0;
        }

        var sourceExpected = inverseSourceB * sourceA[boneA];
        var stored = PoseToLocalMatrix(sourceRaw);
        if (Vector3.Distance(GetHkrgBodyCenter(stored), GetHkrgBodyCenter(sourceExpected)) > 0.0005f ||
            RotationAngleDegrees(stored, sourceExpected) > 0.1f)
        {
            return 0;
        }

        var targetExpected = inverseTargetB * targetA[boneA];
        SetValue(targetEntry, field, ToHkrgQsTransform(targetRaw, targetExpected));
        return 1;
    }
    private static void RetargetHkrgMapperTransform(
        object targetEntry,
        object sourceEntry,
        string field,
        int boneA,
        int boneB,
        IReadOnlyList<Matrix4x4> sourceA,
        IReadOnlyList<Matrix4x4> sourceB,
        IReadOnlyList<Matrix4x4> targetA,
        IReadOnlyList<Matrix4x4> targetB)
    {
        if (boneA < 0 || boneB < 0 ||
            boneA >= sourceA.Count || boneA >= targetA.Count ||
            boneB >= sourceB.Count || boneB >= targetB.Count ||
            GetValue(sourceEntry, field) is not Matrix4x4 sourceRaw ||
            GetValue(targetEntry, field) is not Matrix4x4 targetRaw ||
            !Matrix4x4.Invert(sourceA[boneA], out var inverseSourceA) ||
            !Matrix4x4.Invert(sourceB[boneB], out var inverseSourceB))
        {
            return;
        }

        var deltaA = inverseSourceA * targetA[boneA];
        var deltaB = inverseSourceB * targetB[boneB];
        if (!Matrix4x4.Invert(deltaB, out var inverseDeltaB))
            return;

        var retargeted = inverseDeltaB * PoseToLocalMatrix(sourceRaw) * deltaA;
        SetValue(targetEntry, field, ToHkrgQsTransform(targetRaw, retargeted));
    }

    private static Matrix4x4 ToHkrgQsTransform(Matrix4x4 existing, Matrix4x4 affine)
    {
        if (!Matrix4x4.Decompose(affine, out var scale, out var rotation, out var translation) ||
            rotation.LengthSquared() < 0.000001f)
        {
            return existing;
        }

        rotation = Quaternion.Normalize(rotation);
        return new Matrix4x4(
            translation.X, translation.Y, translation.Z, existing.M14,
            rotation.X, rotation.Y, rotation.Z, rotation.W,
            scale.X, scale.Y, scale.Z, existing.M34,
            existing.M41, existing.M42, existing.M43, existing.M44);
    }
    // hkaSkeletonMapper holds the static reference-pose relationship between
    // the model and ragdoll skeletons. It is a likely pose driver, so expose
    // a diagnostic before changing any mapper data during retargeting.
    public IReadOnlyList<HkrgMapperPoseDiagnostic> GetHkrgMapperPoseDiagnostics()
    {
        var rows = new List<HkrgMapperPoseDiagnostic>();
        foreach (var (mapper, mapperIndex) in GetHkrgSkeletonMappers().Select((mapper, index) => (mapper, index)))
        {
            var mapping = GetValue(mapper, "mapping");
            var skeletonA = GetValue(mapping, "skeletonA");
            var skeletonB = GetValue(mapping, "skeletonB");
            var worldA = GetHkrgSkeletonWorldTransforms(skeletonA);
            var worldB = GetHkrgSkeletonWorldTransforms(skeletonB);
            var simpleMappings = GetList(GetValue(mapping, "simpleMappings")) ?? Array.Empty<object>();

            foreach (var (simple, simpleIndex) in simpleMappings.Select((simple, index) => (simple, index)))
            {
                var boneA = ToInt(GetValue(simple, "boneA"), -1);
                var boneB = ToInt(GetValue(simple, "boneB"), -1);
                if (boneA < 0 || boneA >= worldA.Length || boneB < 0 || boneB >= worldB.Length ||
                    GetValue(simple, "aFromBTransform") is not Matrix4x4 raw)
                {
                    continue;
                }

                var stored = PoseToLocalMatrix(raw);
                var bToA = Matrix4x4.Invert(worldB[boneB], out var inverseB)
                    ? inverseB * worldA[boneA]
                    : Matrix4x4.Identity;
                var aToB = Matrix4x4.Invert(worldA[boneA], out var inverseA)
                    ? inverseA * worldB[boneB]
                    : Matrix4x4.Identity;

                rows.Add(new HkrgMapperPoseDiagnostic(
                    mapperIndex,
                    simpleIndex,
                    boneA,
                    boneB,
                    Vector3.Distance(GetHkrgBodyCenter(stored), GetHkrgBodyCenter(bToA)),
                    RotationAngleDegrees(stored, bToA),
                    Vector3.Distance(GetHkrgBodyCenter(stored), GetHkrgBodyCenter(aToB)),
                    RotationAngleDegrees(stored, aToB)));
            }
        }

        return rows;
    }


    // A mapper pair should round-trip between the model and ragdoll skeletons.
    // This verifies that the two aFromB transforms are inverse counterparts
    // after a retarget, without making any file changes.
    public IReadOnlyList<HkrgMapperPairDiagnostic> GetHkrgMapperPairDiagnostics()
    {
        var model = GetHkrgModelSkeleton();
        var ragdoll = GetHkrgRagdollSkeleton();
        if (model == null || ragdoll == null)
            return Array.Empty<HkrgMapperPairDiagnostic>();

        var forwardMappings = new Dictionary<(int ModelBone, int RagdollBone), (int MapperIndex, int MappingIndex, Matrix4x4 Transform)>();
        var reverseMappings = new Dictionary<(int ModelBone, int RagdollBone), (int MapperIndex, int MappingIndex, Matrix4x4 Transform)>();

        foreach (var (mapper, mapperIndex) in GetHkrgSkeletonMappers().Select((mapper, index) => (mapper, index)))
        {
            var mapping = GetValue(mapper, "mapping");
            var skeletonA = GetValue(mapping, "skeletonA");
            var skeletonB = GetValue(mapping, "skeletonB");
            var isModelFromRagdoll = ReferenceEquals(skeletonA, model) && ReferenceEquals(skeletonB, ragdoll);
            var isRagdollFromModel = ReferenceEquals(skeletonA, ragdoll) && ReferenceEquals(skeletonB, model);
            if (!isModelFromRagdoll && !isRagdollFromModel)
                continue;

            var simpleMappings = GetList(GetValue(mapping, "simpleMappings")) ?? Array.Empty<object>();
            foreach (var (simple, mappingIndex) in simpleMappings.Select((simple, index) => (simple, index)))
            {
                var boneA = ToInt(GetValue(simple, "boneA"), -1);
                var boneB = ToInt(GetValue(simple, "boneB"), -1);
                if (boneA < 0 || boneB < 0 || GetValue(simple, "aFromBTransform") is not Matrix4x4 raw)
                    continue;

                var entry = (mapperIndex, mappingIndex, PoseToLocalMatrix(raw));
                if (isModelFromRagdoll)
                    forwardMappings[(boneA, boneB)] = entry;
                else
                    reverseMappings[(boneB, boneA)] = entry;
            }
        }

        var rows = new List<HkrgMapperPairDiagnostic>();
        foreach (var (key, forward) in forwardMappings)
        {
            if (!reverseMappings.TryGetValue(key, out var reverse))
                continue;

            var ragdollRoundTrip = forward.Transform * reverse.Transform;
            var modelRoundTrip = reverse.Transform * forward.Transform;
            rows.Add(new HkrgMapperPairDiagnostic(
                key.ModelBone,
                key.RagdollBone,
                forward.MapperIndex,
                forward.MappingIndex,
                reverse.MapperIndex,
                reverse.MappingIndex,
                Vector3.Distance(GetHkrgBodyCenter(ragdollRoundTrip), Vector3.Zero),
                RotationAngleDegrees(ragdollRoundTrip, Matrix4x4.Identity),
                Vector3.Distance(GetHkrgBodyCenter(modelRoundTrip), Vector3.Zero),
                RotationAngleDegrees(modelRoundTrip, Matrix4x4.Identity)));
        }

        return rows;
    }


    // Measures the initial knee/hinge angle in the reference pose. A retarget
    // can preserve coincident anchors while still placing that angle outside an
    // authored limit, which makes Havok drive the limb back toward the limit.
    public IReadOnlyList<HkrgHingeReferenceDiagnostic> GetHkrgHingeReferenceDiagnostics()
    {
        var bodies = GetHkrgRigidBodies();
        var rows = new List<HkrgHingeReferenceDiagnostic>();
        foreach (var (constraint, index) in GetHkrgConstraints().Select((constraint, index) => (constraint, index)))
        {
            var data = GetValue(constraint, "data") ?? constraint;
            if (!string.Equals(data.GetType().Name, "hkpLimitedHingeConstraintData", StringComparison.Ordinal))
                continue;

            var atoms = GetValue(data, "atoms");
            var transforms = GetValue(atoms, "transforms");
            var limit = GetValue(atoms, "angLimit");
            var bodyAIndex = GetHkrgConstraintBodyIndex(constraint, bodies, "entities_0");
            var bodyBIndex = GetHkrgConstraintBodyIndex(constraint, bodies, "entities_1");
            if (bodyAIndex < 0 || bodyBIndex < 0 || bodyAIndex >= bodies.Count || bodyBIndex >= bodies.Count ||
                GetValue(transforms, "transformA") is not Matrix4x4 frameA ||
                GetValue(transforms, "transformB") is not Matrix4x4 frameB)
            {
                continue;
            }

            var worldA = GetHkrgAffineTransform(frameA) * GetHkrgAffineTransform(GetHkrgBodyTransform(bodies[bodyAIndex]));
            var worldB = GetHkrgAffineTransform(frameB) * GetHkrgAffineTransform(GetHkrgBodyTransform(bodies[bodyBIndex]));
            if (!Matrix4x4.Invert(ClearTranslation(worldA), out var inverseWorldA))
                continue;

            // The limited-hinge free axis is axis 0 of the local constraint frame.
                        if (!Matrix4x4.Invert(ClearTranslation(worldB), out var inverseWorldB))
                continue;
            var relative = ClearTranslation(worldA) * inverseWorldB;
            var rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(relative));
            var angleRadians = NormalizeHkrgAngle(2.0f * MathF.Atan2(rotation.X, rotation.W));
            var offAxisRadians = 2.0f * MathF.Atan2(MathF.Sqrt(rotation.Y * rotation.Y + rotation.Z * rotation.Z), MathF.Sqrt(rotation.X * rotation.X + rotation.W * rotation.W));
            var minAngle = GetOptionalSingle(limit, "minAngle") ?? float.NegativeInfinity;
            var maxAngle = GetOptionalSingle(limit, "maxAngle") ?? float.PositiveInfinity;
            rows.Add(new HkrgHingeReferenceDiagnostic(
                index,
                GetString(constraint, "name") ?? $"Constraint {index}",
                angleRadians * (180.0f / MathF.PI),
                minAngle * (180.0f / MathF.PI),
                maxAngle * (180.0f / MathF.PI),
                offAxisRadians * (180.0f / MathF.PI),
                angleRadians >= minAngle - 0.001f && angleRadians <= maxAngle + 0.001f));
        }

        return rows;
    }

    private static float NormalizeHkrgAngle(float angle)
    {
        while (angle > MathF.PI)
            angle -= 2.0f * MathF.PI;
        while (angle < -MathF.PI)
            angle += 2.0f * MathF.PI;
        return angle;
    }
    // target_bRca is the live rotation target used when a ragdoll motor is
    // enabled by the game. Expose the useful frame-order candidates so
    // retargeting code can be checked against a vanilla file before writing.
    public IReadOnlyList<HkrgMotorTargetDiagnostic> GetHkrgMotorTargetDiagnostics()
    {
        var rows = new List<HkrgMotorTargetDiagnostic>();
        foreach (var (constraint, index) in GetHkrgConstraints().Select((constraint, index) => (constraint, index)))
        {
            var atoms = GetValue(GetValue(constraint, "data") ?? constraint, "atoms");
            var transforms = GetValue(atoms, "transforms");
            var motors = GetValue(atoms, "ragdollMotors");
            if (GetValue(transforms, "transformA") is not Matrix4x4 frameA ||
                GetValue(transforms, "transformB") is not Matrix4x4 frameB ||
                GetValue(motors, "target_bRca") is not Matrix4x4 target)
            {
                continue;
            }

            var a = ClearTranslation(GetHkrgAffineTransform(frameA));
            var b = ClearTranslation(GetHkrgAffineTransform(frameB));
            if (!Matrix4x4.Invert(a, out var inverseA) || !Matrix4x4.Invert(b, out var inverseB))
                continue;

            rows.Add(new HkrgMotorTargetDiagnostic(
                index,
                GetString(constraint, "name") ?? $"Constraint {index}",
                RotationAngleDegrees(target, b),
                RotationAngleDegrees(target, a),
                RotationAngleDegrees(target, b * inverseA),
                RotationAngleDegrees(target, inverseB * a),
                RotationAngleDegrees(target, a * inverseB),
                RotationAngleDegrees(target, inverseA * b)));
        }

        return rows;
    }

    private string GetHkrgDetails()
    {
        var systems = GetHkrgPhysicsSystems();
        var bodies = GetHkrgRigidBodies();
        var ragdoll = GetHkrgRagdollInstance();
        var constraintCount = GetList(GetValue(ragdoll, "constraints"))?.Count ?? 0;
        var ragdollBodies = GetList(GetValue(ragdoll, "rigidBodies"))?.Count ?? 0;
        var skeletonName = GetString(GetValue(ragdoll, "skeleton"), "name") ?? "(not linked)";
        var modelBoneCount = GetList(GetValue(GetHkrgModelSkeleton(), "bones"))?.Count ?? 0;
        var ragdollBoneCount = GetList(GetValue(GetHkrgRagdollSkeleton(), "bones"))?.Count ?? 0;

        return string.Join(Environment.NewLine, new[]
        {
            "Havok ragdoll",
            new string('-', 64),
            "Format: HKRG / Havok 2014.2 packfile",
            $"Physics systems: {systems.Count}",
            $"Rigid bodies: {bodies.Count}",
            $"Ragdoll bodies: {ragdollBodies}",
            $"Constraints: {constraintCount}",
            $"Model skeleton bones: {modelBoneCount}",
            $"Ragdoll skeleton bones: {ragdollBoneCount}",
            $"Ragdoll skeleton: {skeletonName}",
            string.Empty,
            "The HKRG editor can change ragdoll reference-pose bones and rigid-body capsule endpoints/radii. Parent topology, body-to-bone mapping, and motion properties stay locked. Existing ragdoll/hinge constraint limits are editable."
        });
    }

    private void AddHkrgPreview(ParticlePreviewData preview)
    {
        if (_root == null)
            return;

        // The model skeleton explains the actor's complete pose; the ragdoll
        // skeleton is the subset backed by rigid bodies and constraints.
        AddHkrgSkeletonPreview(preview, GetHkrgModelSkeleton(), HkrgModelPreviewIndexOffset, isReference: true);
        AddHkrgSkeletonPreview(preview, GetHkrgRagdollSkeleton(), 0, isReference: false);
        var rigidBodies = GetHkrgRigidBodyRows().ToList();
        foreach (var body in rigidBodies)
        {
            preview.Colliders.Add(new ColliderPreviewShape
            {
                Index = body.Index,
                Name = body.Name,
                BoneIndex = body.BoneIndex,
                Kind = body.ShapeType == "Sphere" ? ColliderPreviewKind.Sphere : ColliderPreviewKind.Capsule,
                Start = new Vector3(body.StartX, body.StartY, body.StartZ),
                End = new Vector3(body.EndX, body.EndY, body.EndZ),
                Radius = body.Radius
            });
        }

        var bodyBones = rigidBodies.ToDictionary(body => body.Index, body => body.BoneIndex);
        foreach (var constraint in GetHkrgConstraintRows())
        {
            preview.RagdollConstraints.Add(new RagdollConstraintPreviewLink
            {
                Index = constraint.Index,
                Name = constraint.Name,
                Kind = constraint.ConstraintType,
                BodyA = constraint.BodyA,
                BodyB = constraint.BodyB,
                BoneA = bodyBones.TryGetValue(constraint.BodyA, out var boneA) ? boneA : -1,
                BoneB = bodyBones.TryGetValue(constraint.BodyB, out var boneB) ? boneB : -1
            });
        }
    }

    private void AddHkrgSkeletonPreview(
        ParticlePreviewData preview,
        object? skeleton,
        int indexOffset,
        bool isReference)
    {
        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parents = GetList(GetValue(skeleton, "parentIndices")) ?? Array.Empty<object>();
        var poses = GetList(GetValue(skeleton, "referencePose")) ?? Array.Empty<object>();
        var worldTransforms = new Matrix4x4[bones.Count];

        for (var index = 0; index < bones.Count; index++)
        {
            var local = PoseToLocalMatrix(poses.ElementAtOrDefault(index));
            var parentIndex = ToInt(parents.ElementAtOrDefault(index), -1);
            var world = parentIndex >= 0 && parentIndex < index
                ? local * worldTransforms[parentIndex]
                : local;
            worldTransforms[index] = world;
            preview.Bones.Add(new BonePreviewPoint
            {
                Index = indexOffset + index,
                ParentIndex = parentIndex >= 0 ? indexOffset + parentIndex : -1,
                Name = GetString(bones[index], "name") ?? $"Bone {index}",
                IsReference = isReference,
                EditorIndex = index,
                IsSelectable = !isReference,
                Position = new Vector3(world.M41, world.M42, world.M43),
                AxisX = NormalizeOr(new Vector3(world.M11, world.M12, world.M13), Vector3.UnitX),
                AxisY = NormalizeOr(new Vector3(world.M21, world.M22, world.M23), Vector3.UnitY),
                AxisZ = NormalizeOr(new Vector3(world.M31, world.M32, world.M33), Vector3.UnitZ)
            });
        }
    }
}






public sealed record HkrgConstraintPoseDiagnostic(
    int Index,
    string Name,
    int BodyA,
    int BodyB,
    int BoneA,
    int BoneB,
    string BoneAName,
    string BoneBName,
    float FramePositionGap,
    float FrameRotationGapDegrees,
    float SkeletonRelativeRotationDegrees,
    Quaternion FrameRelativeRotation,
    Quaternion SkeletonRelativeRotation,
    bool MotorEnabled,
    float MotorTargetRotationDegrees,
    Quaternion MotorTargetRotation,
    float? TwistMin,
    float? TwistMax,
    float? ConeMin,
    float? ConeMax,
    float? PlanesMin,
    float? PlanesMax,
    float? HingeMin,
    float? HingeMax);


public sealed record HkrgMapperPoseDiagnostic(
    int MapperIndex,
    int MappingIndex,
    int BoneA,
    int BoneB,
    float BToAPositionError,
    float BToARotationErrorDegrees,
    float AToBPositionError,
    float AToBRotationErrorDegrees);

public sealed record HkrgMapperPairDiagnostic(
    int ModelBone,
    int RagdollBone,
    int ForwardMapperIndex,
    int ForwardMappingIndex,
    int ReverseMapperIndex,
    int ReverseMappingIndex,
    float RagdollRoundTripPositionError,
    float RagdollRoundTripRotationErrorDegrees,
    float ModelRoundTripPositionError,
    float ModelRoundTripRotationErrorDegrees);

public sealed record HkrgHingeReferenceDiagnostic(
    int ConstraintIndex,
    string ConstraintName,
    float ReferenceAngleDegrees,
    float MinimumAngleDegrees,
    float MaximumAngleDegrees,
    float OffAxisDegrees,
    bool IsInsideLimit);
public sealed record HkrgMotorTargetDiagnostic(
    int ConstraintIndex,
    string ConstraintName,
    float FrameBErrorDegrees,
    float FrameAErrorDegrees,
    float BTimesInverseAErrorDegrees,
    float InverseBTimesAErrorDegrees,
    float ATimesInverseBErrorDegrees,
    float InverseATimesBErrorDegrees);

public sealed record HkrgConstraintAtomValue(
    int ConstraintIndex,
    string ConstraintName,
    string Path,
    string Value);