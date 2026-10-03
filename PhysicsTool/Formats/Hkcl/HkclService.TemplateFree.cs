using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HKX2;

namespace HKCLTool;

public sealed partial class HkclService
{
    /// <summary>
    /// Returns the current per-cloth conversion defaults. The suggested scale
    /// uses an exact source profile where available; otherwise authored mass
    /// and coefficients are preserved instead of inferred from particle count.
    /// </summary>
    public IReadOnlyList<BphclConversionScale> GetBphclConversionScaleSuggestions()
    {
        var sourceDocument = _bphcl?.NativeDocument
            ?? throw new InvalidOperationException("Open a BPHCL file before requesting conversion scales.");

        return sourceDocument.Cloths
            .Select((cloth, index) =>
            {
                var simulation = cloth.SimCloths.SingleOrDefault();
                var dynamicParticles = simulation?.Particles.Where(particle => !particle.Fixed).ToArray()
                    ?? Array.Empty<NativeBphclParticle>();
                var sourceDynamicMass = dynamicParticles.Sum(particle => particle.Mass);
                var (massScale, basis) = GetBphclSolverScaleSuggestion(index, cloth.Name, simulation);
                // Only measured source profiles establish independent mass
                // and constraint multipliers; unknown cloth keeps both at one.
                var constraintScale = BphclConversionProfileCatalog.TryGetConstraintScale(
                    _bphcl?.SourcePath ?? _path,
                    cloth.Name,
                    out var measuredConstraintScale)
                    ? measuredConstraintScale
                    : 1.0f;
                if (!NearlyEqual(massScale, constraintScale))
                    basis += "; constraint values retained from source";
                var suggestedBotwMass = sourceDynamicMass * massScale;
                var suggestedMaxInverseMass = dynamicParticles
                    .Where(particle => particle.Mass > float.Epsilon)
                    .Select(particle => 1.0f / (particle.Mass * massScale))
                    .DefaultIfEmpty(0.0f)
                    .Max();
                return new BphclConversionScale(
                    index,
                    StripBphclPrefix(cloth.Name),
                    dynamicParticles.Length,
                    sourceDynamicMass,
                    massScale,
                    constraintScale,
                    suggestedBotwMass,
                    suggestedMaxInverseMass,
                    basis);
            })
            .ToList();
    }

    /// <summary>
    /// Builds a new HKCL document from one native BPHCL cloth without cloning
    /// an HKCL cloth. This is intentionally an experimental structural export:
    /// the source geometry, skeleton, solver, colliders, operators, and
    /// constraints, state access, and BotW buffer metadata are rebuilt from
    /// the BPHCL source. It remains experimental because uncommon layouts
    /// have not yet been verified in-game.
    /// </summary>
    public HkclService CreateFreshHkclFromCurrentBphcl(
        int sourceClothIndex,
        IReadOnlyDictionary<int, float>? solverScaleOverrides = null,
        IReadOnlyDictionary<int, float>? constraintScaleOverrides = null)
    {
        var sourceDocument = _bphcl?.NativeDocument
            ?? throw new InvalidOperationException("Open a BPHCL file before creating a fresh HKCL export.");
        var sourceCloth = sourceDocument.Cloths.ElementAtOrDefault(sourceClothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(sourceClothIndex));
        var sourceSimulation = sourceCloth.SimCloths.SingleOrDefault()
            ?? throw new InvalidOperationException("The fresh HKCL exporter currently supports one simulation cloth per BPHCL unit.");
        var sourceSkeleton = sourceDocument.Skeletons.ElementAtOrDefault(sourceClothIndex)
            ?? throw new InvalidOperationException("The selected BPHCL cloth has no paired skeleton.");

        var activeSourceColliders = sourceDocument.Colliders
            .Where(collider => sourceSimulation.CollidableItemIndices.Contains(collider.ItemIndex))
            .ToList();
        var unsupportedShapes = sourceDocument.Colliders
            .Select(collider => collider.Shape.TypeName)
            .Where(type => type is not ("hclCapsuleShape" or "hclTaperedCapsuleShape" or "hclSphereShape" or "hclPlaneShape"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unsupportedShapes.Length > 0)
        {
            throw new InvalidOperationException(
                $"The fresh HKCL exporter cannot create these collider shape(s) yet: {string.Join(", ", unsupportedShapes)}.");
        }

        ValidateFreshOperatorLayout(sourceCloth);

        var clothContainer = new hclClothContainer
        {
            m_collidables = new List<hclCollidable>(),
            m_clothDatas = new List<hclClothData>()
        };
        var animationContainer = new hkaAnimationContainer
        {
            m_skeletons = new List<hkaSkeleton>(),
            m_animations = new List<hkaAnimation>(),
            m_bindings = new List<hkaAnimationBinding>(),
            m_attachments = new List<hkaBoneAttachment>(),
            m_skins = new List<hkaMeshBinding>()
        };
        var root = new hkRootLevelContainer
        {
            m_namedVariants = new List<hkRootLevelContainerNamedVariant>
            {
                new()
                {
                    m_name = "Cloth Container",
                    m_className = "hclClothContainer",
                    m_variant = clothContainer
                },
                new()
                {
                    m_name = "Animation Container",
                    m_className = "hkaAnimationContainer",
                    m_variant = animationContainer
                }
            }
        };

        var skeleton = CreateFreshSkeleton(sourceSkeleton);
        var cloth = CreateFreshCloth(sourceCloth, sourceSimulation, skeleton.m_bones.Count);
        var boneMap = sourceSkeleton.Bones.ToDictionary(bone => bone.Index, bone => bone.Index);

        ApplyBphclSkeleton(skeleton, sourceSkeleton, boneMap);
        RebuildBphclParticleTopology(cloth, sourceSimulation);
        ApplyBphclSimulationSettings(cloth, sourceSimulation);

        var solverScale = ResolveBphclSolverScale(sourceClothIndex, sourceSimulation, solverScaleOverrides);
        var constraintScale = ResolveBphclConstraintScale(
            sourceClothIndex,
            solverScale,
            constraintScaleOverrides);
        ApplyBphclParticles(
            cloth,
            sourceSimulation,
            solverScale,
            preserveSourceLandscapeCollisionBit: false);
        ApplyBphclBufferAndOperatorLayout(
            cloth,
            sourceCloth,
            boneMap,
            preserveTemplateTransformSetShape: false);

        // The outer container owns every collider in the source BPHCL, not
        // just the entries active for this cloth. The simulation reference
        // list below still contains only the active subset. Dropping the
        // inactive shells made a readable fresh file that Havok could not
        // safely initialize at runtime.
        var colliders = CreateBphclColliders(
            cloth,
            Array.Empty<object>(),
            sourceDocument.Colliders,
            sourceSkeleton,
            skeleton,
            boneMap,
            sourceSimulation.CollidableItemIndices);
        var activeConvertedColliders = OrderConvertedCollidersBySourceItems(
            sourceDocument.Colliders,
            colliders,
            sourceSimulation.CollidableItemIndices);
        ApplyBphclCollidableTransformMap(
            cloth,
            sourceSimulation.CollidableTransformMap,
            activeSourceColliders.Count,
            boneMap,
            skeleton,
            activeConvertedColliders);
        InitializeFreshColliderRuntimeMetadata(cloth, colliders);
        // Real matched pairs scale soft solver coefficients with the same
        // cloth-specific ratio as dynamic mass. Hard stretch stiffness stays
        // normalized and is copied verbatim.
        ApplyBphclConstraintLinks(
            cloth,
            sourceSimulation,
            _bphcl?.SourcePath,
            sourceCloth.Name,
            preserveTemplateLayout: false,
            solverScale: constraintScale);
        ApplyBphclConstraintExecution(
            cloth,
            sourceSimulation,
            sourceCloth.Name,
            sourceCloth.Operators);
        ApplyBphclSimpleMeshBoneDeform(cloth, sourceCloth.SimpleMeshBoneDeformers, boneMap);
        BuildFreshClothStates(cloth, sourceCloth);

        cloth.m_name = StripBphclPrefix(sourceCloth.Name);
        skeleton.m_name = StripBphclPrefix(sourceSkeleton.Name);
        clothContainer.m_clothDatas.Add(cloth);
        clothContainer.m_collidables.AddRange(colliders.Cast<hclCollidable>());
        animationContainer.m_skeletons.Add(skeleton);

        return new HkclService
        {
            _root = root,
            _path = null
        };
    }

    private float ResolveBphclSolverScale(
        int clothIndex,
        NativeBphclSimCloth? simulation,
        IReadOnlyDictionary<int, float>? overrides)
    {
        if (simulation == null)
            return 1.0f;

        if (overrides != null && overrides.TryGetValue(clothIndex, out var scale) &&
            float.IsFinite(scale) && scale > 0.0f)
        {
            return scale;
        }

        var sourceName = _bphcl?.NativeDocument?.Cloths.ElementAtOrDefault(clothIndex)?.Name ?? string.Empty;
        return GetBphclSolverScaleSuggestion(clothIndex, sourceName, simulation).Scale;
    }

    private float ResolveBphclConstraintScale(
        int clothIndex,
        float massScale,
        IReadOnlyDictionary<int, float>? overrides)
    {
        if (overrides != null && overrides.TryGetValue(clothIndex, out var scale) &&
            float.IsFinite(scale) && scale > 0.0f)
        {
            return scale;
        }

        var sourceName = _bphcl?.NativeDocument?.Cloths.ElementAtOrDefault(clothIndex)?.Name ?? string.Empty;
        return BphclConversionProfileCatalog.TryGetConstraintScale(
            _bphcl?.SourcePath ?? _path,
            sourceName,
            out var measuredConstraintScale)
            ? measuredConstraintScale
            : 1.0f;
    }

    private (float Scale, string Basis) GetBphclSolverScaleSuggestion(
        int clothIndex,
        string clothName,
        NativeBphclSimCloth? simulation)
    {
        if (simulation == null)
            return (1.0f, "No simulation data");

        if (BphclConversionProfileCatalog.HasExactSourceProfile(_bphcl?.SourcePath ?? _path, clothName) &&
            BphclConversionProfileCatalog.TryGet(_bphcl?.SourcePath ?? _path, clothName, out var verifiedScale, out var verifiedBasis))
            return (verifiedScale, verifiedBasis);
        return (1.0f, "Source mass preserved; no exact counterpart profile");
    }

    /// <summary>
    /// Builds one standalone HKCL document from every cloth unit in the
    /// current BPHCL. Collidables remain shared at the outer-container level,
    /// while each cloth receives its own ordered active-collider list and
    /// transform map.
    /// </summary>
    public HkclService CreateFreshHkclFromCurrentBphclDocument(
        IReadOnlyDictionary<int, float>? solverScaleOverrides = null,
        IReadOnlyDictionary<int, float>? constraintScaleOverrides = null)
    {
        var sourceDocument = _bphcl?.NativeDocument
            ?? throw new InvalidOperationException("Open a BPHCL file before creating a fresh HKCL export.");
        if (sourceDocument.Cloths.Count == 0)
            throw new InvalidOperationException("The BPHCL file contains no cloth units.");
        if (sourceDocument.Skeletons.Count < sourceDocument.Cloths.Count)
        {
            throw new InvalidOperationException(
                $"The BPHCL has {sourceDocument.Cloths.Count} cloth unit(s) but only " +
                $"{sourceDocument.Skeletons.Count} paired skeleton(s).");
        }

        var unsupportedShapes = sourceDocument.Colliders
            .Select(collider => collider.Shape.TypeName)
            .Where(type => type is not ("hclCapsuleShape" or "hclTaperedCapsuleShape" or "hclSphereShape" or "hclPlaneShape"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unsupportedShapes.Length > 0)
        {
            throw new InvalidOperationException(
                $"The fresh HKCL exporter cannot create these collider shape(s) yet: {string.Join(", ", unsupportedShapes)}.");
        }

        var clothContainer = new hclClothContainer
        {
            m_collidables = new List<hclCollidable>(),
            m_clothDatas = new List<hclClothData>()
        };
        var animationContainer = new hkaAnimationContainer
        {
            m_skeletons = new List<hkaSkeleton>(),
            m_animations = new List<hkaAnimation>(),
            m_bindings = new List<hkaAnimationBinding>(),
            m_attachments = new List<hkaBoneAttachment>(),
            m_skins = new List<hkaMeshBinding>()
        };
        var root = new hkRootLevelContainer
        {
            m_namedVariants = new List<hkRootLevelContainerNamedVariant>
            {
                new()
                {
                    m_name = "Cloth Container",
                    m_className = "hclClothContainer",
                    m_variant = clothContainer
                },
                new()
                {
                    m_name = "Animation Container",
                    m_className = "hkaAnimationContainer",
                    m_variant = animationContainer
                }
            }
        };

        var contexts = new List<(NativeBphclCloth SourceCloth, NativeBphclSimCloth Simulation, NativeBphclSkeleton SourceSkeleton, hkaSkeleton Skeleton, hclClothData Cloth, Dictionary<int, int> BoneMap)>();
        for (var index = 0; index < sourceDocument.Cloths.Count; index++)
        {
            var sourceCloth = sourceDocument.Cloths[index];
            var simulation = sourceCloth.SimCloths.SingleOrDefault()
                ?? throw new InvalidOperationException(
                    $"Fresh full-file export currently supports one simulation cloth per unit. {sourceCloth.Name} has {sourceCloth.SimCloths.Count}.");
            ValidateFreshOperatorLayout(sourceCloth);

            var sourceSkeleton = sourceDocument.Skeletons[index];
            var skeleton = CreateFreshSkeleton(sourceSkeleton);
            var cloth = CreateFreshCloth(sourceCloth, simulation, skeleton.m_bones.Count);
            var boneMap = sourceSkeleton.Bones.ToDictionary(bone => bone.Index, bone => bone.Index);

            ApplyBphclSkeleton(skeleton, sourceSkeleton, boneMap);
            RebuildBphclParticleTopology(cloth, simulation);
            ApplyBphclSimulationSettings(cloth, simulation);

            var solverScale = ResolveBphclSolverScale(index, simulation, solverScaleOverrides);
            ApplyBphclParticles(
                cloth,
                simulation,
                solverScale,
                preserveSourceLandscapeCollisionBit: false);
            ApplyBphclBufferAndOperatorLayout(
                cloth,
                sourceCloth,
                boneMap,
                preserveTemplateTransformSetShape: false);

            contexts.Add((sourceCloth, simulation, sourceSkeleton, skeleton, cloth, boneMap));
        }

        // A BPHCL collider table is global, but each cloth's transform map is
        // evaluated against its own skeleton. Reusing one HKCL collidable
        // object across those skeletons leaves its transformIndex and baked
        // rest transform bound to whichever skeleton happened to be created
        // first. Keep a private outer-container copy for every cloth instead.
        // This is larger, but it preserves the native per-cloth binding and
        // avoids cross-chain collider transforms fighting each other.
        var outerColliders = new List<object>();
        foreach (var context in contexts)
        {
            var contextColliders = CreateBphclColliders(
                context.Cloth,
                Array.Empty<object>(),
                sourceDocument.Colliders,
                context.SourceSkeleton,
                context.Skeleton,
                context.BoneMap,
                context.Simulation.CollidableItemIndices);
            outerColliders.AddRange(contextColliders);

            ApplyBphclCollidableTransformMap(
                context.Cloth,
                context.Simulation.CollidableTransformMap,
                context.Simulation.CollidableItemIndices.Count,
                context.BoneMap,
                context.Skeleton,
                OrderConvertedCollidersBySourceItems(
                    sourceDocument.Colliders,
                    contextColliders,
                    context.Simulation.CollidableItemIndices));
            InitializeFreshColliderRuntimeMetadata(context.Cloth, contextColliders);

            var solverScale = ResolveBphclSolverScale(
                contexts.IndexOf(context),
                context.Simulation,
                solverScaleOverrides);
            var constraintScale = ResolveBphclConstraintScale(
                contexts.IndexOf(context),
                solverScale,
                constraintScaleOverrides);
            ApplyBphclConstraintLinks(
                context.Cloth,
                context.Simulation,
                _bphcl?.SourcePath,
                context.SourceCloth.Name,
                preserveTemplateLayout: false,
                solverScale: constraintScale);
            ApplyBphclConstraintExecution(
                context.Cloth,
                context.Simulation,
                context.SourceCloth.Name,
                context.SourceCloth.Operators);
            ApplyBphclSimpleMeshBoneDeform(context.Cloth, context.SourceCloth.SimpleMeshBoneDeformers, context.BoneMap);
            BuildFreshClothStates(context.Cloth, context.SourceCloth);

            context.Cloth.m_name = StripBphclPrefix(context.SourceCloth.Name);
            context.Skeleton.m_name = StripBphclPrefix(context.SourceSkeleton.Name);
            clothContainer.m_clothDatas.Add(context.Cloth);
            animationContainer.m_skeletons.Add(context.Skeleton);
        }

        clothContainer.m_collidables.AddRange(outerColliders.Cast<hclCollidable>());
        return new HkclService
        {
            _root = root,
            _path = null
        };
    }

    private static hkaSkeleton CreateFreshSkeleton(NativeBphclSkeleton source)
    {
        var skeleton = new hkaSkeleton
        {
            m_name = StripBphclPrefix(source.Name),
            m_parentIndices = source.Bones.Select(bone => checked((short)bone.ParentIndex)).ToList(),
            m_bones = source.Bones.Select(bone => new hkaBone
            {
                m_name = StripBphclPrefix(bone.Name),
                m_lockTranslation = bone.LockTranslation
            }).ToList(),
            m_referencePose = source.Bones.Select(CreateFreshReferencePose).ToList(),
            m_referenceFloats = new List<float>(),
            m_floatSlots = new List<string>(),
            m_localFrames = new List<hkaSkeletonLocalFrameOnBone>(),
            m_partitions = new List<hkaSkeletonPartition>()
        };

        return skeleton;
    }

    private static Matrix4x4 CreateFreshReferencePose(NativeBphclBone bone)
    {
        // HKX2 stores hkQsTransform rows as translation, quaternion, scale,
        // then padding. BPHCL exposes the first two rows directly; its cloth
        // skeletons use unit scale.
        return new Matrix4x4(
            bone.Translation.X, bone.Translation.Y, bone.Translation.Z, bone.Translation.W,
            bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z, bone.Rotation.W,
            1.0f, 1.0f, 1.0f, bone.ParentIndex < 0 ? 1.0f : 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);
    }

    private static hclClothData CreateFreshCloth(
        NativeBphclCloth source,
        NativeBphclSimCloth simulationSource,
        int skeletonBoneCount)
    {
        var assembly = typeof(hclClothData).Assembly;
        var simulation = new hclSimClothData
        {
            m_name = "Simulation",
            m_simulationInfo = new hclSimClothDataOverridableSimulationInfo
            {
                m_collisionTolerance = 0.2f
            },
            m_particleDatas = simulationSource.Particles.Select(_ => new hclSimClothDataParticleData()).ToList(),
            m_fixedParticles = new List<ushort>(),
            m_triangleIndices = new List<ushort>(),
            m_triangleFlips = new List<byte>(),
            m_collidableTransformMap = new hclSimClothDataCollidableTransformMap
            {
                m_transformSetIndex = simulationSource.CollidableTransformMap.TransformSetIndex,
                m_transformIndices = new List<uint>(),
                m_offsets = new List<Matrix4x4>()
            },
            m_perInstanceCollidables = new List<hclCollidable>(),
            m_staticConstraintSets = new List<hclConstraintSet>(),
            m_antiPinchConstraintSets = new List<hclConstraintSet>(),
            m_simClothPoses = new List<hclSimClothPose>
            {
                new()
                {
                    m_name = "Default",
                    m_positions = simulationSource.Particles.Select(particle => particle.Position).ToList()
                }
            },
            m_actions = new List<hclAction>(),
            m_staticCollisionMasks = new List<uint>(),
            m_perParticlePinchDetectionEnabledFlags = new List<bool>(),
            m_collidablePinchingDatas = new List<hclSimClothDataCollidablePinchingData>(),
            m_maxCollisionPairs = checked((uint)simulationSource.TriangleCount),
            m_landscapeCollisionData = new hclSimClothDataLandscapeCollisionData
            {
                m_landscapeRadius = 0.05f,
                m_stuckParticlesStretchFactorSq = 9.0f
            },
            m_transferMotionData = new hclSimClothDataTransferMotionData
            {
                m_maxTranslationBlend = 1.0f,
                m_maxRotationBlend = 1.0f
            },
        };

        var cloth = new hclClothData
        {
            m_name = StripBphclPrefix(source.Name),
            m_simClothDatas = new List<hclSimClothData> { simulation },
            m_bufferDefinitions = source.BufferDefinitions
                .Select(buffer => CreateFreshBuffer(assembly, buffer, source.Name))
                .ToList(),
            m_transformSetDefinitions = source.TransformSetDefinitions.Select(set => new hclTransformSetDefinition
            {
                m_name = StripBphclPrefix(set.Name),
                m_type = set.Type,
                m_numTransforms = set.TransformCount
            }).ToList(),
            m_operators = CreateFreshOperators(assembly, source.Operators),
            m_clothStateDatas = new List<hclClothState>(),
            m_actions = new List<hclAction>(),
            m_targetPlatform = (Platform)8192
        };

        if (cloth.m_transformSetDefinitions.Count == 0)
        {
            cloth.m_transformSetDefinitions.Add(new hclTransformSetDefinition
            {
                m_name = "Transforms",
                m_type = 0,
                m_numTransforms = checked((uint)skeletonBoneCount)
            });
        }

        return cloth;
    }

    private static hclBufferDefinition CreateFreshBuffer(
        System.Reflection.Assembly assembly,
        NativeBphclBufferDefinition source,
        string clothName)
    {
        var type = FindHavokType(assembly, source.ClassName)
            ?? throw new InvalidOperationException($"PhysicsTool could not locate the HKX2 buffer class {source.ClassName}.");
        if (Activator.CreateInstance(type) is not hclBufferDefinition buffer)
            throw new InvalidOperationException($"{source.ClassName} is not an HKCL buffer definition.");

        buffer.m_name = source.Type == 6
            ? EnsureLinkPrefix(clothName)
            : string.IsNullOrWhiteSpace(source.BufferName)
                ? StripBphclPrefix(source.MeshName)
                : source.BufferName;
        buffer.m_type = source.Type;
        buffer.m_subType = source.SubType;
        buffer.m_numVertices = source.VertexCount;
        buffer.m_numTriangles = source.TriangleCount;
        buffer.m_bufferLayout = CreateFreshBufferLayout(source.Type == 6);
        if (buffer is hclScratchBufferDefinition scratch)
            scratch.m_triangleIndices = new List<ushort>();
        return buffer;
    }

    private static hclBufferLayout CreateFreshBufferLayout(bool isScratchBuffer)
    {
        // BotW uses a 16-byte position slot. Its disabled element slots use
        // the VectorConversion sentinel 250, not zero. Scratch buffers use
        // triangle format 2; the simulation current/previous buffers use 1.
        return new hclBufferLayout
        {
            m_elementsLayout_0 = new hclBufferLayoutBufferElement
            {
                m_vectorConversion = (VectorConversion)0,
                m_vectorSize = 16,
                m_slotId = 0,
                m_slotStart = 0
            },
            m_elementsLayout_1 = new hclBufferLayoutBufferElement { m_vectorConversion = (VectorConversion)250 },
            m_elementsLayout_2 = new hclBufferLayoutBufferElement { m_vectorConversion = (VectorConversion)250 },
            m_elementsLayout_3 = new hclBufferLayoutBufferElement { m_vectorConversion = (VectorConversion)250 },
            m_slots_0 = new hclBufferLayoutSlot
            {
                m_flags = (SlotFlags)1,
                m_stride = 16
            },
            m_slots_1 = new hclBufferLayoutSlot(),
            m_slots_2 = new hclBufferLayoutSlot(),
            m_slots_3 = new hclBufferLayoutSlot(),
            m_numSlots = 1,
            m_triangleFormat = (TriangleFormat)(isScratchBuffer ? 2 : 1)
        };
    }

    private static List<hclOperator> CreateFreshOperators(
        System.Reflection.Assembly assembly,
        IReadOnlyList<NativeBphclOperatorLayout> sourceOperators)
    {
        var ordered = sourceOperators.OrderBy(@operator => @operator.Index).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].Index != index)
                throw new InvalidOperationException("The BPHCL operator indexes are not contiguous, so a fresh HKCL operator list cannot be reconstructed safely.");
        }

        var result = new List<hclOperator>(ordered.Length);
        foreach (var source in ordered)
        {
            var type = FindHavokType(assembly, source.ClassName)
                ?? throw new InvalidOperationException($"PhysicsTool could not locate the HKX2 operator class {source.ClassName}.");
            if (Activator.CreateInstance(type) is not hclOperator @operator)
                throw new InvalidOperationException($"{source.ClassName} is not an HKCL operator.");

            if (string.Equals(source.ClassName, "hclObjectSpaceSkinPOperator", StringComparison.Ordinal))
            {
                var deformerType = FindHavokType(assembly, "hclObjectSpaceDeformer")
                    ?? throw new InvalidOperationException("PhysicsTool could not locate hclObjectSpaceDeformer.");
                var deformer = Activator.CreateInstance(deformerType)
                    ?? throw new InvalidOperationException("PhysicsTool could not create hclObjectSpaceDeformer.");
                SetValue(@operator, "objectSpaceDeformer", deformer);
                SetValue(deformer, "batchSizeSpu", 512);
            }

            if (string.Equals(source.ClassName, "hclBoneSpaceSkinPOperator", StringComparison.Ordinal))
            {
                var deformerType = FindHavokType(assembly, "hclBoneSpaceDeformer")
                    ?? throw new InvalidOperationException("PhysicsTool could not locate hclBoneSpaceDeformer.");
                var deformer = Activator.CreateInstance(deformerType)
                    ?? throw new InvalidOperationException("PhysicsTool could not create hclBoneSpaceDeformer.");
                SetValue(@operator, "boneSpaceDeformer", deformer);
                SetValue(deformer, "batchSizeSpu", 512);
            }

            SetValue(@operator, "name", GetFreshOperatorName(source.ClassName, source.Index));

            result.Add(@operator);
        }

        return result;
    }

    private static string GetFreshOperatorName(string className, int index) => className switch
    {
        "hclObjectSpaceSkinPOperator" => "Skin",
        "hclBoneSpaceSkinPOperator" => "BoneSpaceSkin",
        "hclMoveParticlesOperator" => "MoveFixedParticles",
        "hclSimulateOperator" => "Simulate",
        "hclSimpleMeshBoneDeformOperator" => "MeshBone",
        "hclGatherAllVerticesOperator" when index == 4 => "VertexGatherCurrent",
        "hclGatherAllVerticesOperator" when index == 5 => "VertexGatherPrev",
        "hclGatherAllVerticesOperator" => "VertexGather",
        "hclCopyVerticesOperator" => "CopyVertices",
        _ => className
    };

    private static void InitializeFreshColliderRuntimeMetadata(
        object cloth,
        IReadOnlyList<object> colliders)
    {
        // BotW's collision runtime expects these HKCL-native defaults. TotK
        // stores a richer pinching/virtual-collision model, so copying its
        // bookkeeping fields verbatim can leave otherwise valid capsules
        // inert after conversion.
        foreach (var collider in colliders)
        {
            SetValue(collider, "pinchDetectionEnabled", false);
            SetValue(collider, "pinchDetectionPriority", 0);
            SetValue(collider, "pinchDetectionRadius", 0.01f);
        }

        var simulation = GetFirst(GetValue(cloth, "simClothDatas"));
        if (simulation == null)
            return;

        var activeColliders = GetPrimaryMutableCollidableReferences(cloth);
        var pinchingData = EnsureMutableObjectList(simulation, "collidablePinchingDatas", "collider pinching data");
        pinchingData.Clear();
        foreach (var _ in activeColliders.Cast<object>())
        {
            pinchingData.Add(new hclSimClothDataCollidablePinchingData
            {
                m_pinchDetectionEnabled = false,
                m_pinchDetectionPriority = 0,
                m_pinchDetectionRadius = 0.01f
            });
        }

        SetValue(simulation, "maxPinchedParticleIndex", 3072u);
        var pose = GetFirst(GetValue(simulation, "simClothPoses"));
        if (pose != null)
            SetValue(pose, "name", "DefaultClothPose");
    }

    private static void BuildFreshClothStates(hclClothData cloth, NativeBphclCloth source)
    {
        var states = source.States.Count == 0
            ? new[]
            {
                new NativeBphclClothState(
                    0,
                    -1,
                    "Default",
                    Array.Empty<int>(),
                    Array.Empty<NativeBphclClothStateBufferAccess>(),
                    Array.Empty<NativeBphclClothStateTransformSetAccess>(),
                    Array.Empty<int>())
            }
            : source.States;
        var transformCount = checked((int)(source.TransformSetDefinitions.FirstOrDefault()?.TransformCount ?? 0));
        var skinTransforms = source.ObjectSpaceSkins
            .SelectMany(skin => skin.TransformSubset)
            .Concat(source.BoneSpaceSkins.SelectMany(skin => skin.TransformSubset))
            .Select(index => checked((int)index))
            .Distinct()
            .ToArray();
        var colliderTransforms = source.SimCloths
            .SelectMany(simulation => simulation.CollidableTransformMap.TransformIndices)
            .Select(index => checked((int)index))
            .Distinct()
            .ToArray();
        var writtenTransforms = source.SimpleMeshBoneDeformers
            .SelectMany(deformer => deformer.TriangleBonePairs)
            .Select(pair => pair.BoneOffset / 64)
            .Select(index => checked((int)index))
            .Distinct()
            .ToArray();
        var skinBufferIndex = source.ObjectSpaceSkins.FirstOrDefault()?.OutputBufferIndex
            ?? source.BoneSpaceSkins.FirstOrDefault()?.OutputBufferIndex
            ?? 0;

        cloth.m_clothStateDatas.Clear();
        foreach (var sourceState in states)
        {
            // Despite the legacy record name, these native arrays contain
            // list indexes, not ITEM-table indexes. Treating them as ITEMs
            // made every fresh state fall back to every operator.
            var operators = sourceState.OperatorItemIndices
                .Where(index => index >= 0 && index < cloth.m_operators.Count)
                .Select(index => checked((uint)index))
                .ToList();
            if (operators.Count == 0)
                operators = Enumerable.Range(0, cloth.m_operators.Count).Select(index => checked((uint)index)).ToList();

            var simCloths = sourceState.SimClothItemIndices
                .Where(index => index >= 0 && index < source.SimCloths.Count)
                .Select(index => checked((uint)index))
                .ToList();
            if (simCloths.Count == 0)
                simCloths.Add(0);

            var isDefaultState = sourceState.Index == 0 ||
                string.Equals(sourceState.Name, "Default", StringComparison.OrdinalIgnoreCase);
            var bufferAccesses = BuildFreshBufferAccesses(
                source,
                sourceState,
                skinBufferIndex,
                isDefaultState);
            // TotK's state tracker records are valid for TotK's transform
            // scheduler, but their component routing is not interchangeable
            // with BotW's HKCL runtime. In particular, copying them verbatim
            // can omit active collider-bone transforms from BotW's per-frame
            // read set: the capsules remain visible at rest but never update
            // as collision participants. Build the proven BotW access layout
            // instead, retaining every skinned input plus every active
            // collider transform for the default state.
            var transformAccesses = transformCount == 0
                ? new List<hclClothStateTransformSetAccess>()
                : new List<hclClothStateTransformSetAccess>
                {
                    CreateFreshTransformSetAccess(
                        0,
                        (byte)(isDefaultState ? 11 : 9),
                        0,
                        isDefaultState
                            ? skinTransforms.Concat(colliderTransforms)
                            : skinTransforms,
                        isDefaultState ? writtenTransforms : Array.Empty<int>(),
                        transformCount)
                };

            cloth.m_clothStateDatas.Add(new hclClothState
            {
                m_name = string.IsNullOrWhiteSpace(sourceState.Name) ? "Default" : sourceState.Name,
                m_operators = operators,
                m_usedSimCloths = simCloths,
                m_usedBuffers = bufferAccesses,
                m_usedTransformSets = transformAccesses
            });
        }
    }

    private static List<hclClothStateBufferAccess> BuildFreshBufferAccesses(
        NativeBphclCloth source,
        NativeBphclClothState state,
        int skinBufferIndex,
        bool isDefaultState)
    {
        // Buffer component flags are runtime-generation specific. TotK's
        // state records use combinations such as 15 and extra gathered-buffer
        // routes that BotW's HKCL scheduler does not interpret like its own
        // 7/9 layout. The result can simulate particles but skip collision
        // response. Rebuild the known BotW state access pattern instead of
        // copying the source BPHCL access records verbatim.
        var result = new List<hclClothStateBufferAccess>
        {
            CreateFreshBufferAccess(skinBufferIndex, 7, 0, 0, 0, false, skinBufferIndex)
        };

        if (isDefaultState)
        {
            var simulationBuffer = source.SimpleMeshBoneDeformers
                .Select(deformer => checked((int)deformer.InputBufferIndex))
                .FirstOrDefault(skinBufferIndex);
            var simulationComponentFlags = ShouldPreserveSourceDefaultSimulationBufferFlags(source) ? (byte)15 : (byte)9;
            result[0].m_bufferUsage.m_perComponentFlags_1 = simulationComponentFlags;
            result.Add(CreateFreshBufferAccess(simulationBuffer, simulationComponentFlags, 0, 0, 0, true, simulationBuffer));
            return result;
        }

        if (string.Equals(source.Name, "HairLong_1086_SimulationMesh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source.Name, "Momi_1086_SimulationMesh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source.Name, "Tactile_1086_SimulationMesh", StringComparison.OrdinalIgnoreCase))
        {
            var carriedBuffer = state.OperatorItemIndices
                .Where(index => index >= 0 && index < source.Operators.Count)
                .Select(index => source.Operators[index])
                .Where(@operator =>
                    string.Equals(@operator.ClassName, "hclGatherAllVerticesOperator", StringComparison.Ordinal) ||
                    string.Equals(@operator.ClassName, "hclCopyVerticesOperator", StringComparison.Ordinal))
                .Select(@operator => @operator.OutputBufferIndex)
                .FirstOrDefault(index => index.HasValue);
            if (carriedBuffer.HasValue && carriedBuffer.Value != skinBufferIndex)
                result.Add(CreateFreshBufferAccess(carriedBuffer.Value, 6, 0, 0, 0, false, carriedBuffer.Value));
        }

        return result;
    }

    private static bool ShouldPreserveSourceDefaultSimulationBufferFlags(NativeBphclCloth source)
        => source.Name.Contains("_1061", StringComparison.OrdinalIgnoreCase) &&
           (source.Name.Contains("Maedare", StringComparison.OrdinalIgnoreCase) ||
            source.Name.Contains("Necklace", StringComparison.OrdinalIgnoreCase) ||
            source.Name.Contains("Outer", StringComparison.OrdinalIgnoreCase) ||
            source.Name.Contains("Sleeve", StringComparison.OrdinalIgnoreCase));

    private static hclClothStateBufferAccess CreateFreshBufferAccess(
        int bufferIndex,
        byte componentFlags0,
        byte componentFlags1,
        byte componentFlags2,
        byte componentFlags3,
        bool trianglesRead,
        int shadowBufferIndex) => new()
    {
        m_bufferIndex = checked((uint)bufferIndex),
        m_bufferUsage = new hclBufferUsage
        {
            m_perComponentFlags_0 = componentFlags0,
            m_perComponentFlags_1 = componentFlags1,
            m_perComponentFlags_2 = componentFlags2,
            m_perComponentFlags_3 = componentFlags3,
            m_trianglesRead = trianglesRead
        },
        m_shadowBufferIndex = checked((uint)shadowBufferIndex)
    };

    private static hclClothStateTransformSetAccess CreateFreshTransformSetAccess(
        NativeBphclClothStateTransformSetAccess source,
        IEnumerable<int> fallbackReadTransforms,
        IEnumerable<int> fallbackWrittenTransforms,
        int fallbackTransformCount)
    {
        if (source.TransformTrackers.Count > 0)
        {
            return new hclClothStateTransformSetAccess
            {
                m_transformSetIndex = source.TransformSetIndex,
                m_transformSetUsage = new hclTransformSetUsage
                {
                    m_perComponentFlags_0 = source.ComponentFlags0,
                    m_perComponentFlags_1 = source.ComponentFlags1,
                    m_perComponentTransformTrackers = source.TransformTrackers
                        .Select(tracker => new hclTransformSetUsageTransformTracker
                        {
                            m_read = CreateFreshBitField(tracker.Read),
                            m_readBeforeWrite = CreateFreshBitField(tracker.ReadBeforeWrite),
                            m_written = CreateFreshBitField(tracker.Written)
                        })
                        .ToList()
                }
            };
        }

        return CreateFreshTransformSetAccess(
            source.TransformSetIndex,
            source.ComponentFlags0,
            source.ComponentFlags1,
            fallbackReadTransforms,
            fallbackWrittenTransforms,
            fallbackTransformCount);
    }

    private static hclClothStateTransformSetAccess CreateFreshTransformSetAccess(
        uint transformSetIndex,
        byte componentFlags0,
        byte componentFlags1,
        IEnumerable<int> readTransforms,
        IEnumerable<int> writtenTransforms,
        int transformCount)
    {
        var read = CreateFreshBitField(readTransforms, transformCount);
        var written = CreateFreshBitField(writtenTransforms, transformCount);
        var empty = CreateFreshBitField(Array.Empty<int>(), transformCount);

        return new hclClothStateTransformSetAccess
        {
            m_transformSetIndex = transformSetIndex,
            m_transformSetUsage = new hclTransformSetUsage
            {
                m_perComponentFlags_0 = componentFlags0,
                m_perComponentFlags_1 = componentFlags1,
                m_perComponentTransformTrackers = new List<hclTransformSetUsageTransformTracker>
                {
                    new()
                    {
                        m_read = read,
                        m_readBeforeWrite = CreateFreshBitField(readTransforms, transformCount),
                        m_written = written
                    },
                    new()
                    {
                        m_read = empty,
                        m_readBeforeWrite = CreateFreshBitField(Array.Empty<int>(), transformCount),
                        m_written = CreateFreshBitField(Array.Empty<int>(), transformCount)
                    }
                }
            }
        };
    }

    private static hkBitField CreateFreshBitField(NativeBphclBitField source) => new()
    {
        m_storage = new hkBitFieldStoragehkArrayunsignedinthkContainerHeapAllocator
        {
            m_words = source.Words.ToList(),
            m_numBits = checked((int)source.NumBits)
        }
    };

    private static hkBitField CreateFreshBitField(IEnumerable<int> indices, int numBits)
    {
        var wordCount = Math.Max(1, (numBits + 31) / 32);
        var words = Enumerable.Repeat(0u, wordCount).ToList();
        foreach (var index in indices.Distinct())
        {
            if (index < 0 || index >= numBits)
                continue;
            words[index / 32] |= 1u << (index % 32);
        }

        return new hkBitField
        {
            m_storage = new hkBitFieldStoragehkArrayunsignedinthkContainerHeapAllocator
            {
                m_words = words,
                m_numBits = numBits
            }
        };
    }

    private static void ValidateFreshOperatorLayout(NativeBphclCloth source)
    {
        if (source.BufferDefinitions.Count == 0)
            throw new InvalidOperationException("The BPHCL cloth has no buffer definitions to build into HKCL.");
        if (source.Operators.Count == 0)
            throw new InvalidOperationException("The BPHCL cloth has no operators to build into HKCL.");

        var allowedOperators = new HashSet<string>(StringComparer.Ordinal)
        {
            "hclObjectSpaceSkinPOperator",
            "hclBoneSpaceSkinPOperator",
            "hclMoveParticlesOperator",
            "hclSimulateOperator",
            "hclSimpleMeshBoneDeformOperator",
            "hclCopyVerticesOperator",
            "hclGatherAllVerticesOperator"
        };
        var unsupported = source.Operators
            .Select(@operator => @operator.ClassName)
            .Where(name => !allowedOperators.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unsupported.Length > 0)
        {
            throw new InvalidOperationException(
                $"The selected BPHCL cloth uses operator class(es) not yet supported by the fresh HKCL exporter: {string.Join(", ", unsupported)}.");
        }
    }
}

