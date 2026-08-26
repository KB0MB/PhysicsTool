using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace HKCLTool;

public static class HkclToBphclGameReadyGraphBuilder
{
	/// <summary>
	/// Builds a donor-free TotK cloth graph from a normal HKCL document.
	/// Fresh conversion regenerates the BPHCL metadata instead of copying
	/// format-specific TYPE or AAMP sections from an unrelated source.
	/// </summary>
	public static StandaloneBphclDocumentGraph Build(HkclService source) =>
		BuildCore(source, preserveNativeBphclValues: false);

	/// <summary>
	/// Reconstructs an already loaded BPHCL while retaining its native metadata.
	/// This path exists for fidelity audits and is not used by normal conversion.
	/// </summary>
	public static StandaloneBphclDocumentGraph BuildNativeReconstruction(HkclService source) =>
		BuildCore(source, preserveNativeBphclValues: true);

	private static StandaloneBphclDocumentGraph BuildCore(
		HkclService source,
		bool preserveNativeBphclValues)
	{
		if (source.IsHelperBoneSidecar || source.IsHkrg || !source.HasDocument ||
			(source.IsBphcl && !preserveNativeBphclValues))
		{
			throw new InvalidOperationException("Standalone BPHCL conversion requires a loaded normal HKCL file.");
		}
		if (preserveNativeBphclValues && !source.IsBphcl)
		{
			throw new InvalidOperationException("Native BPHCL reconstruction requires a loaded BPHCL file.");
		}

		var cloths = new List<StandaloneBphclClothGraph>();
		for (var index = 0; index < source.GetClothSummaries().Count; index++)
		{
			cloths.Add(BuildCloth(source, index, preserveNativeBphclValues));
		}

		var referencedByIndex = cloths
			.SelectMany(cloth => cloth.Colliders)
			.GroupBy(collider => collider.Index)
			.ToDictionary(group => group.Key, group => group.First());
		var nativeDocument = preserveNativeBphclValues ? source.CaptureBphclNativeDocument() : null;
		var nativeOuterColliders = nativeDocument?.Colliders.ToDictionary(collider => collider.Index)
			?? new Dictionary<int, NativeBphclCollider>();
		var outerColliders = source.GetAllColliderRows()
			.OrderBy(collider => collider.Index)
			.Select(collider =>
			{
				if (referencedByIndex.TryGetValue(collider.Index, out var referenced))
					return referenced;

				nativeOuterColliders.TryGetValue(collider.Index, out var native);
				return CreateStandaloneCollider(collider, native);
			})
			.ToArray();

		var graph = new StandaloneBphclDocumentGraph(
			cloths,
			outerColliders,
			preserveNativeBphclValues,
			preserveNativeBphclValues ? CopyEmbeddedAamp(nativeDocument) : null,
			preserveNativeBphclValues ? CopyTypeSection(nativeDocument) : null,
			source.SourcePath);
		graph.Validate();
		return graph;
	}

	private static byte[]? CopyEmbeddedAamp(NativeBphclDocument? document)
	{
		if (document == null || document.Header.ParameterSize == 0)
		{
			return null;
		}
		int num;
		int num2;
		checked
		{
			num = (int)document.Header.ParameterOffset;
			num2 = (int)document.Header.ParameterSize;
		}
		if (num < 0 || num2 < 4 || num > document.Bytes.Length - num2 || !document.Bytes.AsSpan(num, 4).SequenceEqual("AAMP"u8))
		{
			throw new InvalidDataException("Native BPHCL has an invalid embedded AAMP range.");
		}
		return document.Bytes.AsSpan(num, num2).ToArray();
	}

	private static byte[]? CopyTypeSection(NativeBphclDocument? document)
	{
		NativeBphclSection nativeBphclSection = document?.TypeSection;
		if (nativeBphclSection == null)
		{
			return null;
		}
		if (nativeBphclSection.Offset < 0 || nativeBphclSection.Size < 8 || nativeBphclSection.Offset > document.Bytes.Length - nativeBphclSection.Size || !document.Bytes.AsSpan(nativeBphclSection.Offset + 4, 4).SequenceEqual("TYPE"u8))
		{
			throw new InvalidDataException("Native BPHCL has an invalid TYPE section.");
		}
		return document.Bytes.AsSpan(nativeBphclSection.Offset, nativeBphclSection.Size).ToArray();
	}

	private static StandaloneBphclCollider CreateStandaloneCollider(ColliderEditRow collider, NativeBphclCollider? native)
	{
		Matrix4x4 transform = collider.Transform;
		bool flag = (object)native != null;
		Vector3 vector = (flag ? new Vector3(native.Shape.Start.X, native.Shape.Start.Y, native.Shape.Start.Z) : ToColliderLocalPoint(transform, new Vector3(collider.StartX, collider.StartY, collider.StartZ)));
		Vector3 end = (flag ? new Vector3(native.Shape.End.X, native.Shape.End.Y, native.Shape.End.Z) : ToColliderLocalPoint(transform, new Vector3(collider.EndX, collider.EndY, collider.EndZ)));
		Vector3 vector2 = (flag ? new Vector3(native.Shape.PlaneEquation.X, native.Shape.PlaneEquation.Y, native.Shape.PlaneEquation.Z) : ToColliderLocalDirection(transform, new Vector3(collider.PlaneNormalX, collider.PlaneNormalY, collider.PlaneNormalZ)));
		Vector4? nativePlaneEquation = (flag ? new Vector4?(native.Shape.PlaneEquation) : (string.Equals(collider.ShapeType, "hclPlaneShape", StringComparison.Ordinal) ? new Vector4?(new Vector4(vector2, 0f - Vector3.Dot(vector2, vector))) : ((Vector4?)null)));
		return new StandaloneBphclCollider(collider.Index, flag ? native.Name : ToBphclRuntimeColliderName(collider.Name, collider.BoneName), flag ? native.Shape.TypeName : collider.ShapeType, collider.BoneIndex, StripLinkPrefix(collider.BoneName), vector, end, collider.Radius, vector2, transform, flag ? new Vector4?(native.AxisX) : ((Vector4?)null), flag ? new Vector4?(native.AxisY) : ((Vector4?)null), flag ? new Vector4?(native.AxisZ) : ((Vector4?)null), flag ? new Vector4?(native.Translation) : ((Vector4?)null), flag ? new Vector4?(native.Shape.Start) : ((Vector4?)null), flag ? new Vector4?(native.Shape.End) : ((Vector4?)null), nativePlaneEquation, flag ? native.Shape.Kind : 0, flag ? native.PinchDetectionRadius : 0f, (sbyte)(flag ? native.PinchDetectionPriority : 0), flag && native.PinchDetectionEnabled, flag && native.VirtualCollisionPointCollisionEnabled, !flag || native.Enabled, flag ? new Vector4?(native.Shape.Direction) : ((Vector4?)null), flag ? new float?(native.Shape.CapsuleLengthSquaredInverse) : ((float?)null));
	}

	private static StandaloneBphclClothGraph BuildCloth(
		HkclService source,
		int clothIndex,
		bool preserveNativeBphclValues)
	{
		ParticlePreviewData particlePreview = source.GetParticlePreview(clothIndex);
		ClothSimulationSettings simulationSettings = source.GetSimulationSettings(clothIndex);
		string sourceClothName = source.GetClothName(clothIndex);
		string text = (preserveNativeBphclValues ? sourceClothName : BphclConversionProfileCatalog.GetReverseClothName(source.SourcePath, sourceClothName));
		NativeBphclSimulationRuntime simulationRuntime = source.GetSimulationRuntimeData(clothIndex);
		float massScale = 1f;
		float constraintScale = 1f;
		if (!preserveNativeBphclValues)
		{
			BphclConversionProfileCatalog.TryGetReverseScales(source.SourcePath, sourceClothName, out massScale, out constraintScale);
		}
		ParticleEditRow[] array = (from particle in source.GetParticleRows(clothIndex)
			orderby particle.Index
			select particle).ToArray();
		HashSet<int> landscapeParticles = (preserveNativeBphclValues ? new HashSet<int>() : SelectLandscapeParticles(array, BphclConversionProfileCatalog.ShouldEnableReverseLandscapeCollision(source.SourcePath, sourceClothName)));
		StandaloneBphclParticle[] array2 = array.Select((ParticleEditRow particle) => new StandaloneBphclParticle(particle.Index, new Vector4(particle.X, particle.Y, particle.Z, particle.W), particle.Fixed, preserveNativeBphclValues ? particle.Mass : ToBphclMass(particle.Fixed, particle.Mass, massScale), preserveNativeBphclValues ? particle.InverseMass : ToBphclInverseMass(particle.Fixed, particle.Mass, massScale), particle.Radius, particle.Friction, preserveNativeBphclValues ? particle.CollisionMask : ApplyLandscapeCollisionMask(particle, landscapeParticles.Contains(particle.Index)))).ToArray();
		StandaloneBphclBone[] bones = (from bone in source.GetBoneRows(clothIndex)
			orderby bone.Index
			select new StandaloneBphclBone(bone.Index, preserveNativeBphclValues ? bone.Name : ToBphclRuntimeBoneName(bone.Name), bone.ParentIndex, new Vector4(bone.X, bone.Y, bone.Z, bone.TranslationW), new Vector4(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW), new Vector4(bone.ScaleX, bone.ScaleY, bone.ScaleZ, preserveNativeBphclValues ? bone.ScaleW : 1f))).ToArray();
		IReadOnlyList<ConstraintSetSummary> constraintSetSummaries = source.GetConstraintSetSummaries(clothIndex);
		Dictionary<int, BendStiffnessConstraintSetData> bendStiffnessSets = source.GetBendStiffnessConstraintSets(clothIndex).ToDictionary((BendStiffnessConstraintSetData set) => set.ConstraintSetIndex);
		List<StandaloneBphclConstraintSetMetadata> list = constraintSetSummaries.Select(delegate(ConstraintSetSummary set)
		{
			bendStiffnessSets.TryGetValue(set.Index, out var value);
			return new StandaloneBphclConstraintSetMetadata(set.Index, set.Name, set.ClassName, set.ConstraintId, set.ConstraintType, (!preserveNativeBphclValues) ? new bool?(string.Equals(set.ClassName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal)) : value?.ClampBendStiffness, value?.MaxRestPoseHeightSq, (!preserveNativeBphclValues) ? new bool?(string.Equals(set.ClassName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal)) : value?.UseRestPoseConfig);
		}).ToList();
		Dictionary<(int, int), StandaloneBphclConstraint> dictionary = new Dictionary<(int, int), StandaloneBphclConstraint>();
		StandaloneBphclLocalRange[] array3 = source.GetLocalRangeConstraintSets(clothIndex).SelectMany((LocalRangeConstraintSetData set) => set.Constraints.Select((LocalRangeConstraintData range) => new StandaloneBphclLocalRange(set.ConstraintSetIndex, range.Index, "hclLocalRangeConstraintSet", range.ParticleIndex, range.ReferenceVertex, range.ShapeRadius, range.MaxNormalDistance, range.MinNormalDistance, set.ReferenceMeshBufferIndex, set.Stiffness, set.ShapeType, set.ApplyNormalComponent))).ToArray();
		Dictionary<int, string> dictionary2 = constraintSetSummaries.ToDictionary((ConstraintSetSummary set) => set.Index, (ConstraintSetSummary set) => set.ClassName);
		StandaloneBphclParticle[] array4 = array2;
		foreach (StandaloneBphclParticle standaloneBphclParticle in array4)
		{
			foreach (ParticleRelationshipRow particleRelationship in source.GetParticleRelationships(clothIndex, standaloneBphclParticle.Index))
			{
				string valueOrDefault = dictionary2.GetValueOrDefault(particleRelationship.ConstraintSetIndex, particleRelationship.Name);
				if (particleRelationship.LinkIndex >= 0 && particleRelationship.ParticleA >= 0 && particleRelationship.ParticleB >= 0 && !string.Equals(valueOrDefault, "hclBendStiffnessConstraintSet", StringComparison.Ordinal))
				{
					dictionary.TryAdd((particleRelationship.ConstraintSetIndex, particleRelationship.LinkIndex), new StandaloneBphclConstraint(particleRelationship.ConstraintSetIndex, particleRelationship.LinkIndex, valueOrDefault, particleRelationship.ParticleA, particleRelationship.ParticleB, MapConstraint(valueOrDefault, "restLength", particleRelationship.RestLength), MapConstraint(valueOrDefault, "bendMinLength", particleRelationship.BendMinLength), MapConstraint(valueOrDefault, "stretchMaxLength", particleRelationship.StretchMaxLength), MapConstraint(valueOrDefault, "stiffness", particleRelationship.Stiffness), MapConstraint(valueOrDefault, "bendStiffness", particleRelationship.BendStiffness), MapConstraint(valueOrDefault, "stretchStiffness", particleRelationship.StretchStiffness)));
				}
			}
		}
		foreach (BendStiffnessConstraintSetData value2 in bendStiffnessSets.Values)
		{
			foreach (BendStiffnessConstraintData link in value2.Links)
			{
				dictionary[(value2.ConstraintSetIndex, link.LinkIndex)] = new StandaloneBphclConstraint(value2.ConstraintSetIndex, link.LinkIndex, "hclBendStiffnessConstraintSet", link.ParticleA, link.ParticleB, null, null, null, null, MapConstraint("hclBendStiffnessConstraintSet", "bendStiffness", link.BendStiffness), null, link.ParticleC, link.ParticleD, link.RestCurvature, link.WeightA, link.WeightB, link.WeightC, link.WeightD);
			}
		}
		StandaloneBphclTransitionConstraintSet[] array5 = (from set in source.GetTransitionConstraintSets(clothIndex)
			select new StandaloneBphclTransitionConstraintSet(set.ConstraintSetIndex, set.Name, set.ConstraintId, set.ConstraintType, set.ToAnimationPeriod, set.ToAnimationPlusDelayPeriod, set.ToSimulationPeriod, set.ToSimulationPlusDelayPeriod, set.ReferenceMeshBufferIndex, set.Particles.Select((TransitionConstraintParticleData particle) => new StandaloneBphclTransitionParticle(particle.Index, particle.ParticleIndex, particle.ReferenceVertex, particle.ToAnimationDelay, particle.ToSimulationDelay, particle.ToSimulationMaximumDistance)).ToArray())).ToArray();
		StandaloneBphclBonePlaneConstraintSet[] array6 = (from set in source.GetBonePlaneConstraintSets(clothIndex)
			select new StandaloneBphclBonePlaneConstraintSet(set.ConstraintSetIndex, set.Name, set.ConstraintId, set.ConstraintType, set.TransformSetIndex, set.Planes.Select((BonePlaneConstraintData plane) => new StandaloneBphclBonePlane(plane.Index, plane.PlaneEquationBone, plane.ParticleIndex, plane.TransformIndex, plane.Stiffness)).ToArray())).ToArray();
		IReadOnlyList<SkinOperatorData> skinOperatorData = source.GetSkinOperatorData(clothIndex);

		IReadOnlyList<StandaloneBphclColliderBinding> readOnlyList = source.GetStandaloneColliderBindings(clothIndex);
		ClothRuntimeData clothRuntimeData = source.GetClothRuntimeData(clothIndex);
		if (!preserveNativeBphclValues && TryCreateDerivedBendStiffnessSet(particlePreview, clothRuntimeData, list, dictionary.Values, BphclConversionProfileCatalog.ShouldDeriveMissingBendStiffness(source.SourcePath, sourceClothName), out int sourceBendSetIndex, out StandaloneBphclConstraintSetMetadata metadata, out IReadOnlyList<StandaloneBphclConstraint> links))
		{
			list.Add(metadata);
			foreach (StandaloneBphclConstraint item in links)
			{
				dictionary[(item.ConstraintSetIndex, item.LinkIndex)] = item;
			}
			clothRuntimeData = EnsureDerivedBendPass(clothRuntimeData, sourceBendSetIndex, metadata.Index);
		}
		if (!preserveNativeBphclValues)
		{
			(list, dictionary, array3, array5, array6, clothRuntimeData) = CanonicalizeFreshConstraintProgram(list, dictionary, array3, array5, array6, clothRuntimeData);
			if (BphclConversionProfileCatalog.TryGetReverseConstraintExecution(source.SourcePath, sourceClothName, out int[] execution))
			{
				clothRuntimeData = ApplyVerifiedConstraintExecution(clothRuntimeData, execution);
			}
		}
		if (!preserveNativeBphclValues)
		{
			RebuildFreshBendStiffnessRestPose(particlePreview, dictionary);
			RecoverMissingBendStiffnessRestPoseLimits(particlePreview, list, dictionary.Values);
		}
		Dictionary<int, NativeBphclCollider> nativeColliders = ((!preserveNativeBphclValues) ? new Dictionary<int, NativeBphclCollider>() : (source.CaptureBphclNativeDocument()?.Colliders.ToDictionary((NativeBphclCollider collider) => collider.Index) ?? new Dictionary<int, NativeBphclCollider>()));
		StandaloneBphclCollider[] source2 = (from collider in source.GetColliderRows(clothIndex)
			orderby collider.Index
			select collider).Select(delegate(ColliderEditRow collider)
		{
			nativeColliders.TryGetValue(collider.Index, out NativeBphclCollider value);
			return CreateStandaloneCollider(collider, value);
		}).ToArray();
		Dictionary<int, StandaloneBphclCollider> sourceCollidersByIndex = source2.ToDictionary((StandaloneBphclCollider collider) => collider.Index);
		// Per-instance collider slots are semantic: particle collision-mask bit N
		// addresses slot N, not the collider's outer-container index. Matched
		// HKCL/BPHCL pairs preserve this order across formats, so sorting slots by
		// collider index silently changes which shapes each particle can hit.
		int[] mappedColliderIndices = readOnlyList.Select((StandaloneBphclColliderBinding binding) => binding.ColliderIndex).Distinct().ToArray();
		StandaloneBphclCollider[] colliders = (from index in mappedColliderIndices.Where(sourceCollidersByIndex.ContainsKey)
			select sourceCollidersByIndex[index]).Concat(source2.Where((StandaloneBphclCollider collider) => !Enumerable.Contains(mappedColliderIndices, collider.Index))).ToArray();
		StandaloneBphclBoneBinding[] array7 = particlePreview.BoneBindings.Select((ParticlePreviewBoneBinding binding) => new StandaloneBphclBoneBinding(binding.BoneIndex, binding.ParticleA, binding.ParticleB, binding.ParticleC)).Distinct().ToArray();
		StandaloneBphclSimpleMeshBoneDeformer[] array8 = (from deformer in source.GetSimpleMeshBoneDeformData(clothIndex)
			select new StandaloneBphclSimpleMeshBoneDeformer(deformer.OperatorIndex, deformer.InputBufferIndex, deformer.OutputTransformSetIndex, preserveNativeBphclValues ? deformer.BoneAxis : 3, deformer.TriangleBonePairs.ToArray(), deformer.LocalBoneTransforms.ToArray())).ToArray();
		if (!preserveNativeBphclValues && array8.Length == 0 && array7.Length != 0)
		{
			Dictionary<(int ParticleA, int ParticleB, int ParticleC), int> triangles = particlePreview.Triangles.Select((ParticlePreviewTriangle triangle, int index) => (triangle: triangle, index: index)).ToDictionary(((ParticlePreviewTriangle triangle, int index) entry) => (ParticleA: entry.triangle.ParticleA, ParticleB: entry.triangle.ParticleB, ParticleC: entry.triangle.ParticleC), ((ParticlePreviewTriangle triangle, int index) entry) => entry.index);
			SimpleMeshTriangleBonePairData[] array9 = (from binding in array7
				where triangles.TryGetValue((binding.ParticleA, binding.ParticleB, binding.ParticleC), out var _)
				select checked(new SimpleMeshTriangleBonePairData((ushort)(binding.BoneIndex * 64), (ushort)(triangles[(binding.ParticleA, binding.ParticleB, binding.ParticleC)] * 6)))).ToArray();
			array8 = new StandaloneBphclSimpleMeshBoneDeformer[1]
			{
				new StandaloneBphclSimpleMeshBoneDeformer(-1, 1, 0, 3, array9, Enumerable.Repeat(Matrix4x4.Identity, array9.Length).ToArray())
			};
		}
		float collisionTolerance = ((preserveNativeBphclValues || simulationSettings.CollisionTolerance > 0f) ? simulationSettings.CollisionTolerance : 0.2f);
		return new StandaloneBphclClothGraph(clothIndex, text, "cloth_skeleton_" + text, new StandaloneBphclSimulationSettings(new Vector3(simulationSettings.GravityX, simulationSettings.GravityY, simulationSettings.GravityZ), simulationSettings.DampingPerSecond, collisionTolerance, simulationSettings.TransferTranslationMotion, simulationSettings.MinTranslationSpeed, simulationSettings.MaxTranslationSpeed, simulationSettings.MinTranslationBlend, simulationSettings.MaxTranslationBlend, simulationSettings.TransferRotationMotion, simulationSettings.MinRotationSpeed, simulationSettings.MaxRotationSpeed, simulationSettings.MinRotationBlend, simulationSettings.MaxRotationBlend), array2, bones, particlePreview.Triangles.Select((ParticlePreviewTriangle triangle) => new StandaloneBphclTriangle(triangle.ParticleA, triangle.ParticleB, triangle.ParticleC)).ToArray(), source.GetTriangleFlips(clothIndex), list, (from constraint in dictionary.Values
			orderby constraint.ConstraintSetIndex, constraint.LinkIndex
			select constraint).ToArray(), (from range in array3
			orderby range.ConstraintSetIndex, range.LocalConstraintIndex
			select range).ToArray(), array5, array6, clothRuntimeData, simulationRuntime, skinOperatorData, array8, colliders, readOnlyList, array7);
		float? MapConstraint(string constraintClass, string fieldName, float? value)
		{
			if (!preserveNativeBphclValues)
			{
				return ReverseConstraintValue(source.SourcePath, sourceClothName, constraintClass, fieldName, value, constraintScale);
			}
			return value;
		}
	}

	private static (StandaloneBphclParticle[] Particles, IReadOnlyList<StandaloneBphclColliderBinding> Bindings, ClothRuntimeData Runtime, NativeBphclSimulationRuntime SimulationRuntime) NormalizeFreshColliderSlots(IReadOnlyList<StandaloneBphclParticle> particles, IReadOnlyDictionary<int, StandaloneBphclCollider> collidersByIndex, IReadOnlyList<StandaloneBphclColliderBinding> bindings, ClothRuntimeData runtime, NativeBphclSimulationRuntime simulationRuntime)
	{
		var array = (from slot in bindings.Select((StandaloneBphclColliderBinding binding, int oldSlot) => new
			{
				Binding = binding,
				OldSlot = oldSlot
			})
			orderby slot.Binding.ColliderIndex, slot.OldSlot
			select slot).ToArray();
		int[] oldToNewSlot = new int[bindings.Count];
		for (int num = 0; num < array.Length; num++)
		{
			oldToNewSlot[array[num].OldSlot] = num;
		}
		StandaloneBphclParticle[] item = particles.Select((StandaloneBphclParticle particle) => particle with
		{
			CollisionMask = RemapPerInstanceColliderMask(particle.CollisionMask, oldToNewSlot)
		}).ToArray();
		IReadOnlyList<NativeBphclCollidablePinchingData> readOnlyList = simulationRuntime.CollidablePinchingData;
		if (readOnlyList.Count == bindings.Count)
		{
			NativeBphclCollidablePinchingData[] array2 = new NativeBphclCollidablePinchingData[bindings.Count];
			for (int num2 = 0; num2 < bindings.Count; num2++)
			{
				array2[oldToNewSlot[num2]] = readOnlyList[num2];
			}
			readOnlyList = array2;
		}
		ClothRuntimeData item2 = runtime with
		{
			Operators = runtime.Operators.Select((ClothOperatorData @operator) => @operator with
			{
				SimulateConfigs = @operator.SimulateConfigs.Select((ClothSimulateConfigData config) => config with
				{
					InstanceCollidablesUsed = config.InstanceCollidablesUsed.Select((int slot) => (slot < 0 || slot >= oldToNewSlot.Length) ? slot : oldToNewSlot[slot]).ToArray()
				}).ToArray()
			}).ToArray()
		};
		return (Particles: item, Bindings: array.Select(slot => slot.Binding).ToArray(), Runtime: item2, SimulationRuntime: simulationRuntime with
		{
			CollidablePinchingData = readOnlyList
		});
	}

	private static uint RemapPerInstanceColliderMask(uint mask, IReadOnlyList<int> oldToNewSlot)
	{
		uint num = ((oldToNewSlot.Count >= 31) ? 2147483647u : ((uint)((1 << oldToNewSlot.Count) - 1)));
		uint num2 = mask & ~num;
		for (int i = 0; i < oldToNewSlot.Count; i++)
		{
			if ((mask & (uint)(1 << i)) != 0)
			{
				num2 |= (uint)(1 << oldToNewSlot[i]);
			}
		}
		return num2;
	}

	private static bool TryCreateDerivedBendStiffnessSet(ParticlePreviewData preview, ClothRuntimeData runtime, IReadOnlyList<StandaloneBphclConstraintSetMetadata> constraintSets, IEnumerable<StandaloneBphclConstraint> constraints, bool allowSinglePass, out int sourceBendSetIndex, out StandaloneBphclConstraintSetMetadata metadata, out IReadOnlyList<StandaloneBphclConstraint> links)
	{
		sourceBendSetIndex = -1;
		metadata = null;
		links = Array.Empty<StandaloneBphclConstraint>();
		if (constraintSets.Any((StandaloneBphclConstraintSetMetadata set) => string.Equals(set.TypeName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal)))
		{
			return false;
		}
		int[] array = (from index in (from index in runtime.Operators.Where((ClothOperatorData @operator) => string.Equals(@operator.ClassName, "hclSimulateOperator", StringComparison.Ordinal)).SelectMany((ClothOperatorData @operator) => @operator.SimulateConfigs).SelectMany((ClothSimulateConfigData config) => config.ConstraintExecution)
				where index >= 0
				select index).ToArray()
			group index by index into @group
			where @group.Count() == 2
			select @group.Key into index
			where constraintSets.Any((StandaloneBphclConstraintSetMetadata set) => set.Index == index && string.Equals(set.TypeName, "hclBendLinkConstraintSet", StringComparison.Ordinal))
			select index).ToArray();
		if (array.Length != 1 && allowSinglePass)
		{
			array = constraintSets
				.Where(set => string.Equals(set.TypeName, "hclBendLinkConstraintSet", StringComparison.Ordinal))
				.Select(set => set.Index)
				.ToArray();
		}
		if (array.Length != 1)
		{
			return false;
		}
		int bendSetIndex = array[0];
		sourceBendSetIndex = bendSetIndex;
		StandaloneBphclConstraint[] array2 = (from link in constraints
			where link.ConstraintSetIndex == bendSetIndex && string.Equals(link.TypeName, "hclBendLinkConstraintSet", StringComparison.Ordinal)
			orderby link.LinkIndex
			select link).ToArray();
		if (array2.Length == 0)
		{
			return false;
		}
		if (array2.Any(delegate(StandaloneBphclConstraint link)
		{
			float? bendMinLength = link.BendMinLength;
			if (bendMinLength.HasValue)
			{
				float valueOrDefault = bendMinLength.GetValueOrDefault();
				bendMinLength = link.StretchMaxLength;
				if (bendMinLength.HasValue)
				{
					float valueOrDefault2 = bendMinLength.GetValueOrDefault();
					return MathF.Abs(valueOrDefault - valueOrDefault2) > 1E-05f;
				}
			}
			return true;
		}))
		{
			return false;
		}
		Dictionary<int, Vector3> dictionary = preview.Particles.ToDictionary((ParticlePreviewPoint particle) => particle.Index, (ParticlePreviewPoint particle) => particle.Position);
		List<StandaloneBphclConstraint> list = new List<StandaloneBphclConstraint>(array2.Length);
		float num = 0f;
		StandaloneBphclConstraint[] array3 = array2;
		foreach (StandaloneBphclConstraint standaloneBphclConstraint in array3)
		{
			if (!TryFindBendQuad(preview.Triangles, standaloneBphclConstraint.ParticleA, standaloneBphclConstraint.ParticleB, out var particleC, out var particleD) || !dictionary.TryGetValue(standaloneBphclConstraint.ParticleA, out var value) || !dictionary.TryGetValue(standaloneBphclConstraint.ParticleB, out var value2) || !dictionary.TryGetValue(particleC, out var value3) || !dictionary.TryGetValue(particleD, out var value4) || !TryGetBendRestConfiguration(value, value2, value3, value4, out var weightA, out var weightB, out var weightC, out var weightD, out var restCurvature, out var restDistance))
			{
				return false;
			}
			float num3 = MathF.Abs(standaloneBphclConstraint.BendStiffness ?? standaloneBphclConstraint.StretchStiffness.GetValueOrDefault());
			float num4 = standaloneBphclConstraint.StretchMaxLength ?? standaloneBphclConstraint.BendMinLength.GetValueOrDefault();
			float value5 = ((num3 <= 0f) ? 0f : ((0f - num3) / ((num4 >= 0.15f) ? 15f : 10f)));
			num = MathF.Max(num, MathF.Abs(restDistance));
			list.Add(new StandaloneBphclConstraint(-1, standaloneBphclConstraint.LinkIndex, "hclBendStiffnessConstraintSet", standaloneBphclConstraint.ParticleA, standaloneBphclConstraint.ParticleB, null, null, null, null, value5, null, particleC, particleD, restCurvature, weightA, weightB, weightC, weightD));
		}
		int derivedIndex = ((constraintSets.Count != 0) ? (constraintSets.Max((StandaloneBphclConstraintSetMetadata set) => set.Index) + 1) : 0);
		metadata = new StandaloneBphclConstraintSetMetadata(derivedIndex, "Bend Stiffness", "hclBendStiffnessConstraintSet", checked((uint)derivedIndex), 0u, true, (num > 0f) ? (64f * num * num) : 0f, true);
		links = list.Select((StandaloneBphclConstraint link) => link with
		{
			ConstraintSetIndex = derivedIndex
		}).ToArray();
		return true;
	}

	private static ClothRuntimeData EnsureDerivedBendPass(ClothRuntimeData runtime, int sourceBendSetIndex, int bendStiffnessSetIndex)
	{
		bool changed = false;
		ClothOperatorData[] operators = runtime.Operators.Select(delegate(ClothOperatorData @operator)
		{
			if (!string.Equals(@operator.ClassName, "hclSimulateOperator", StringComparison.Ordinal) || @operator.SimulateConfigs.Count == 0)
			{
				return @operator;
			}
			ClothSimulateConfigData[] simulateConfigs = @operator.SimulateConfigs.Select(delegate(ClothSimulateConfigData config)
			{
				if (config.ConstraintExecution.Contains(bendStiffnessSetIndex))
				{
					return config;
				}
				List<int> execution = new List<int>(config.ConstraintExecution.Count + 1);
				int occurrences = 0;
				foreach (int index in config.ConstraintExecution)
				{
					if (index == sourceBendSetIndex)
					{
						occurrences++;
						if (occurrences == 2)
						{
							execution.Add(bendStiffnessSetIndex);
							changed = true;
							continue;
						}
					}
					execution.Add(index);
				}
				if (occurrences == 1)
				{
					int insertAt = execution.IndexOf(sourceBendSetIndex) + 1;
					execution.Insert(insertAt, bendStiffnessSetIndex);
					changed = true;
				}
				return occurrences == 0 ? config : config with
				{
					ConstraintExecution = execution.ToArray()
				};
			}).ToArray();
			return @operator with
			{
				SimulateConfigs = simulateConfigs
			};
		}).ToArray();
		if (!changed)
		{
			return runtime;
		}
		return runtime with
		{
			Operators = operators
		};
	}
	private static (List<StandaloneBphclConstraintSetMetadata> ConstraintSets, Dictionary<(int Set, int Link), StandaloneBphclConstraint> Constraints, StandaloneBphclLocalRange[] LocalRanges, StandaloneBphclTransitionConstraintSet[] TransitionSets, StandaloneBphclBonePlaneConstraintSet[] BonePlaneSets, ClothRuntimeData Runtime) CanonicalizeFreshConstraintProgram(IReadOnlyList<StandaloneBphclConstraintSetMetadata> sourceSets, IReadOnlyDictionary<(int Set, int Link), StandaloneBphclConstraint> sourceConstraints, IReadOnlyList<StandaloneBphclLocalRange> sourceLocalRanges, IReadOnlyList<StandaloneBphclTransitionConstraintSet> sourceTransitionSets, IReadOnlyList<StandaloneBphclBonePlaneConstraintSet> sourceBonePlaneSets, ClothRuntimeData sourceRuntime)
	{
		StandaloneBphclConstraintSetMetadata[] source = (from set in sourceSets
			where !string.Equals(set.TypeName, "hclBonePlanesConstraintSet", StringComparison.Ordinal)
			orderby GetFreshBphclConstraintOrder(set.TypeName), set.Index
			select set).ToArray();
		Dictionary<int, int> indexMap = source.Select((StandaloneBphclConstraintSetMetadata set, int index) => (OldIndex: set.Index, NewIndex: index)).ToDictionary(((int OldIndex, int NewIndex) entry) => entry.OldIndex, ((int OldIndex, int NewIndex) entry) => entry.NewIndex);
		checked
		{
			List<StandaloneBphclConstraintSetMetadata> item = source.Select((StandaloneBphclConstraintSetMetadata set, int index) => set with
			{
				Index = index,
				ConstraintId = (uint)index
			}).ToList();
			Dictionary<(int, int), StandaloneBphclConstraint> item2 = (from link in sourceConstraints.Values
				where indexMap.ContainsKey(link.ConstraintSetIndex)
				select link with
				{
					ConstraintSetIndex = indexMap[link.ConstraintSetIndex]
				}).ToDictionary((StandaloneBphclConstraint link) => (ConstraintSetIndex: link.ConstraintSetIndex, LinkIndex: link.LinkIndex));
			StandaloneBphclLocalRange[] item3 = (from range in sourceLocalRanges
				where indexMap.ContainsKey(range.ConstraintSetIndex)
				select range with
				{
					ConstraintSetIndex = indexMap[range.ConstraintSetIndex]
				}).ToArray();
			StandaloneBphclTransitionConstraintSet[] item4 = (from set in sourceTransitionSets
				where indexMap.ContainsKey(set.ConstraintSetIndex)
				select set with
				{
					ConstraintSetIndex = indexMap[set.ConstraintSetIndex],
					ConstraintId = (uint)indexMap[set.ConstraintSetIndex]
				}).ToArray();
			ClothOperatorData[] operators = sourceRuntime.Operators.Select(delegate(ClothOperatorData @operator)
			{
				if (!string.Equals(@operator.ClassName, "hclSimulateOperator", StringComparison.Ordinal) || @operator.SimulateConfigs.Count == 0)
				{
					return @operator;
				}
				ClothSimulateConfigData[] simulateConfigs = @operator.SimulateConfigs.Select((ClothSimulateConfigData config) => config with
				{
					ConstraintExecution = BuildFreshBphclConstraintExecution(config.ConstraintExecution, indexMap, item)
				}).ToArray();
				return @operator with
				{
					SimulateConfigs = simulateConfigs
				};
			}).ToArray();
			return (ConstraintSets: item, Constraints: item2, LocalRanges: item3, TransitionSets: item4, BonePlaneSets: Array.Empty<StandaloneBphclBonePlaneConstraintSet>(), Runtime: sourceRuntime with
			{
				Operators = operators
			});
		}
	}

	private static int[] BuildFreshBphclConstraintExecution(IReadOnlyList<int> sourceExecution, IReadOnlyDictionary<int, int> indexMap, IReadOnlyList<StandaloneBphclConstraintSetMetadata> constraintSets)
	{
		Dictionary<int, string> typeByIndex = constraintSets.ToDictionary((StandaloneBphclConstraintSetMetadata set) => set.Index, (StandaloneBphclConstraintSetMetadata set) => set.TypeName);
		int[] mapped = (from index in sourceExecution
			where index >= 0 && indexMap.ContainsKey(index)
			select indexMap[index]).ToArray();
		int[] simulationPass = (from entry in mapped.Select((int index, int order) => (Index: index, Order: order))
			where !string.Equals(typeByIndex.GetValueOrDefault(entry.Index), "hclTransitionConstraintSet", StringComparison.Ordinal)
			orderby GetFreshBphclExecutionOrder(typeByIndex.GetValueOrDefault(entry.Index)), entry.Order
			select entry.Index).ToArray();
		int[] transitionPass = (from index in mapped
			where string.Equals(typeByIndex.GetValueOrDefault(index), "hclTransitionConstraintSet", StringComparison.Ordinal)
			select index).ToArray();
		if (transitionPass.Length == 0)
		{
			return simulationPass;
		}
		return simulationPass.Concat(new int[1] { -1 }).Concat(transitionPass).ToArray();
	}

	private static ClothRuntimeData ApplyVerifiedConstraintExecution(ClothRuntimeData runtime, IReadOnlyList<int> execution)
	{
		ClothOperatorData[] operators = runtime.Operators.Select(delegate(ClothOperatorData item)
		{
			if (!string.Equals(item.ClassName, "hclSimulateOperator", StringComparison.Ordinal) || item.SimulateConfigs.Count == 0)
			{
				return item;
			}

			return item with
			{
				SimulateConfigs = item.SimulateConfigs.Select(config => config with
				{
					ConstraintExecution = execution.ToArray()
				}).ToArray()
			};
		}).ToArray();
		return runtime with { Operators = operators };
	}

	private static int GetFreshBphclExecutionOrder(string? typeName)
	{
		return typeName switch
		{
			"hclLocalRangeConstraintSet" => 0,
			"hclStandardLinkConstraintSet" => 1,
			"hclBendLinkConstraintSet" => 2,
			"hclBendStiffnessConstraintSet" => 3,
			"hclStretchLinkConstraintSet" => 4,
			_ => 100,
		};
	}
	private static int GetFreshBphclConstraintOrder(string typeName)
	{
		return typeName switch
		{
			"hclStandardLinkConstraintSet" => 0, 
			"hclStretchLinkConstraintSet" => 1, 
			"hclLocalRangeConstraintSet" => 2, 
			"hclTransitionConstraintSet" => 3, 
			"hclBendLinkConstraintSet" => 4, 
			"hclBendStiffnessConstraintSet" => 5, 
			_ => 100, 
		};
	}

	private static bool TryFindBendQuad(IReadOnlyList<ParticlePreviewTriangle> triangles, int particleA, int particleB, out int particleC, out int particleD)
	{
		particleC = -1;
		particleD = -1;
		foreach (ParticlePreviewTriangle triangle in triangles)
		{
			if (TriangleContains(triangle, particleA) && !TriangleContains(triangle, particleB) && TryGetNeighborsAroundVertex(triangle, particleA, out var previous, out var next) && triangles.Any((ParticlePreviewTriangle triangleB) => TriangleContains(triangleB, particleB) && TriangleContains(triangleB, previous) && TriangleContains(triangleB, next)))
			{
				particleC = previous;
				particleD = next;
				return true;
			}
		}
		return false;
	}

	private static bool TriangleContains(ParticlePreviewTriangle triangle, int particle)
	{
		if (triangle.ParticleA != particle && triangle.ParticleB != particle)
		{
			return triangle.ParticleC == particle;
		}
		return true;
	}

	private static bool TryGetNeighborsAroundVertex(ParticlePreviewTriangle triangle, int particle, out int previous, out int next)
	{
		if (triangle.ParticleA == particle)
		{
			previous = triangle.ParticleC;
			next = triangle.ParticleB;
			return true;
		}
		if (triangle.ParticleB == particle)
		{
			previous = triangle.ParticleA;
			next = triangle.ParticleC;
			return true;
		}
		if (triangle.ParticleC == particle)
		{
			previous = triangle.ParticleB;
			next = triangle.ParticleA;
			return true;
		}
		previous = -1;
		next = -1;
		return false;
	}

	private static bool TryGetBendRestConfiguration(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out float weightA, out float weightB, out float weightC, out float weightD, out float restCurvature, out float restDistance)
	{
		Vector3 vector = b - a;
		Vector3 vector2 = d - c;
		float num = vector.LengthSquared();
		float num2 = vector2.LengthSquared();
		float num3 = Vector3.Dot(vector, vector2);
		float num4 = num * num2 - num3 * num3;
		if (num < 1E-07f || num2 < 1E-07f || MathF.Abs(num4) < 1E-07f)
		{
			weightA = (weightB = (weightC = (weightD = (restCurvature = (restDistance = 0f)))));
			return false;
		}
		Vector3 vector3 = a - c;
		float num5 = (num3 * Vector3.Dot(vector2, vector3) - num2 * Vector3.Dot(vector, vector3)) / num4;
		float num6 = (num * Vector3.Dot(vector2, vector3) - num3 * Vector3.Dot(vector, vector3)) / num4;
		bool flag = ((num5 < -0.25f || num5 > 1.25f) ? true : false);
		bool flag2 = flag;
		if (!flag2)
		{
			bool flag3 = ((num6 < -0.25f || num6 > 1.25f) ? true : false);
			flag2 = flag3;
		}
		if (flag2)
		{
			weightA = (weightB = (weightC = (weightD = (restCurvature = (restDistance = 0f)))));
			return false;
		}
		float num7 = MathF.Sqrt(num);
		float num8 = Vector3.Cross(c - a, vector).Length() / num7;
		float num9 = Vector3.Cross(d - a, vector).Length() / num7;
		float num10 = num8 + num9;
		if (num10 < 1E-07f)
		{
			weightA = (weightB = (weightC = (weightD = (restCurvature = (restDistance = 0f)))));
			return false;
		}
		float num11 = num9 / num10;
		float num12 = num8 / num10;
		float num13 = Vector3.Dot(num11 * c + num12 * d - a, vector) / num;
		if ((num13 < -0.25f || num13 > 1.25f) ? true : false)
		{
			weightA = (weightB = (weightC = (weightD = (restCurvature = (restDistance = 0f)))));
			return false;
		}
		weightA = 1f - num13;
		weightB = num13;
		weightC = 0f - num11;
		weightD = 0f - num12;
		Vector3 vector4 = a + num5 * vector;
		Vector3 vector5 = c + num6 * vector2;
		Vector3 vector6 = Vector3.Cross(vector, vector2);
		float num14 = vector6.Length();
		restDistance = ((num14 > 1E-07f) ? Vector3.Dot(vector5 - vector4, vector6 / num14) : 0f);
		float num15 = MathF.Sqrt(num2);
		Vector3 vector7 = Vector3.Cross(vector2, a - c);
		Vector3 vector8 = Vector3.Cross(b - c, vector2);
		float num16 = vector7.Length() / num15;
		float num17 = vector8.Length() / num15;
		float num18 = vector7.Length();
		float num19 = vector8.Length();
		if (num16 < 1E-07f || num17 < 1E-07f || num18 < 1E-07f || num19 < 1E-07f)
		{
			restCurvature = 0f;
			return true;
		}
		float num20 = Vector3.Dot(Vector3.Cross(vector7 / num18, vector8 / num19), vector2 / num15);
		restCurvature = num20 / (num16 + num17);
		if (float.IsFinite(restCurvature))
		{
			return float.IsFinite(restDistance);
		}
		return false;
	}

	private static void RebuildFreshBendStiffnessRestPose(
		ParticlePreviewData preview,
		Dictionary<(int Set, int Link), StandaloneBphclConstraint> constraints)
	{
		Dictionary<int, Vector3> positions = preview.Particles.ToDictionary(
			(ParticlePreviewPoint particle) => particle.Index,
			(ParticlePreviewPoint particle) => particle.Position);

		foreach ((int Set, int Link) key in constraints.Keys.ToArray())
		{
			StandaloneBphclConstraint link = constraints[key];
			if (!string.Equals(link.TypeName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal) ||
				link.ParticleC is not int particleC ||
				link.ParticleD is not int particleD ||
				!positions.TryGetValue(link.ParticleA, out Vector3 particleA) ||
				!positions.TryGetValue(link.ParticleB, out Vector3 particleB) ||
				!positions.TryGetValue(particleC, out Vector3 positionC) ||
				!positions.TryGetValue(particleD, out Vector3 positionD) ||
				!TryGetBendRestConfiguration(
					particleA,
					particleB,
					positionC,
					positionD,
					out float weightA,
					out float weightB,
					out float weightC,
					out float weightD,
					out float restCurvature,
					out _))
			{
				continue;
			}

			constraints[key] = link with
			{
				RestCurvature = restCurvature,
				WeightA = weightA,
				WeightB = weightB,
				WeightC = weightC,
				WeightD = weightD
			};
		}
	}

	private static void RecoverMissingBendStiffnessRestPoseLimits(ParticlePreviewData preview, List<StandaloneBphclConstraintSetMetadata> constraintSets, IEnumerable<StandaloneBphclConstraint> constraints)
	{
		Dictionary<int, Vector3> dictionary = preview.Particles.ToDictionary((ParticlePreviewPoint particle) => particle.Index, (ParticlePreviewPoint particle) => particle.Position);
		StandaloneBphclConstraint[] source = constraints.ToArray();
		for (int num = 0; num < constraintSets.Count; num++)
		{
			StandaloneBphclConstraintSetMetadata metadata = constraintSets[num];
			if (!string.Equals(metadata.TypeName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal))
			{
				continue;
			}
			float? maxRestPoseHeightSq = metadata.MaxRestPoseHeightSq;
			if (maxRestPoseHeightSq.HasValue && maxRestPoseHeightSq.GetValueOrDefault() > 1E-07f)
			{
				continue;
			}
			float num2 = 0f;
			bool flag = false;
			foreach (StandaloneBphclConstraint item in source.Where((StandaloneBphclConstraint link) => link.ConstraintSetIndex == metadata.Index && string.Equals(link.TypeName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal)))
			{
				int? particleC = item.ParticleC;
				if (!particleC.HasValue)
				{
					continue;
				}
				int valueOrDefault = particleC.GetValueOrDefault();
				particleC = item.ParticleD;
				if (particleC.HasValue)
				{
					int valueOrDefault2 = particleC.GetValueOrDefault();
					if (dictionary.TryGetValue(item.ParticleA, out var value) && dictionary.TryGetValue(item.ParticleB, out var value2) && dictionary.TryGetValue(valueOrDefault, out var value3) && dictionary.TryGetValue(valueOrDefault2, out var value4) && TryGetBendRestConfiguration(value, value2, value3, value4, out var _, out var _, out var _, out var _, out var _, out var restDistance))
					{
						num2 = MathF.Max(num2, MathF.Abs(restDistance));
						flag = true;
					}
				}
			}
			if (flag && num2 > 0f)
			{
				constraintSets[num] = metadata with
				{
					ClampBendStiffness = true,
					MaxRestPoseHeightSq = 64f * num2 * num2,
					UseRestPoseConfig = true
				};
			}
		}
	}

	private static string ToBphclRuntimeBoneName(string value)
	{
		string text = StripLinkPrefix(value);
		if (!UsesLinkRuntimeNamespace(text))
		{
			return text;
		}
		return "Link:" + text;
	}

	private static bool UsesLinkRuntimeNamespace(string name)
	{
		bool flag;
		switch (name)
		{
		case "Root":
		case "Skl_Root":
		case "Spine_1":
		case "Spine_2":
		case "Neck":
		case "Head":
		case "Waist":
		case "Clavicle_L":
		case "Clavicle_R":
		case "Arm_1_L":
		case "Arm_1_R":
		case "Arm_2_L":
		case "Arm_2_R":
		case "Arm_3_L":
		case "Arm_3_R":
		case "Wrist_L":
		case "Wrist_R":
		case "Hand_L":
		case "Hand_R":
		case "Leg_1_L":
		case "Leg_1_R":
		case "Leg_2_L":
		case "Leg_2_R":
		case "Ankle_L":
		case "Ankle_R":
		case "Foot_L":
		case "Foot_R":
		case "Toe_L":
		case "Toe_R":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return true;
		}
		if (!name.StartsWith("Finger", StringComparison.OrdinalIgnoreCase) && !IsSharedHairBone(name) && !name.StartsWith("Momi_", StringComparison.OrdinalIgnoreCase))
		{
			if (name.StartsWith("Ponytail_", StringComparison.OrdinalIgnoreCase))
			{
				return !name.EndsWith("_Armor", StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}
		return true;
	}

	private static bool IsSharedHairBone(string name)
	{
		if (string.Equals(name, "Hair_Root", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string[] array = name.Split('_');
		int result;
		if (array.Length == 3 && string.Equals(array[0], "Hair", StringComparison.OrdinalIgnoreCase) && array[1].Length == 1 && char.IsLetter(array[1][0]))
		{
			return int.TryParse(array[2], out result);
		}
		return false;
	}

	private static string ToBphclRuntimeColliderName(string colliderName, string attachedBoneName)
	{
		string text = StripLinkPrefix(colliderName);
		string text2 = ToBphclRuntimeBoneName(attachedBoneName);
		if (!string.IsNullOrEmpty(attachedBoneName) && !text2.StartsWith("Link:", StringComparison.Ordinal))
		{
			return text;
		}
		return "Link:" + text;
	}

	private static string StripLinkPrefix(string value)
	{
		while (value.StartsWith("Link:", StringComparison.OrdinalIgnoreCase))
		{
			value = value.Substring(5);
		}
		return value;
	}

	private static HashSet<int> SelectLandscapeParticles(IReadOnlyList<ParticleEditRow> particles, bool enableLandscapeCollision)
	{
		if (!enableLandscapeCollision)
		{
			return new HashSet<int>();
		}
		return (from particle in particles
			where !particle.Fixed
			select particle.Index).ToHashSet();
	}

	private static uint ApplyLandscapeCollisionMask(ParticleEditRow particle, bool useLandscapeCollision)
	{
		uint num = particle.CollisionMask & 0x7FFFFFFF;
		if (!(!particle.Fixed & useLandscapeCollision))
		{
			return num;
		}
		return num | 0x80000000u;
	}

	private static float ToBphclMass(bool fixedParticle, float sourceMass, float scale)
	{
		if (!fixedParticle && float.IsFinite(scale) && !(scale <= 0f))
		{
			return sourceMass / scale;
		}
		return 0f;
	}

	private static float ToBphclInverseMass(bool fixedParticle, float sourceMass, float scale)
	{
		float num = ToBphclMass(fixedParticle, sourceMass, scale);
		if (!(num > float.Epsilon))
		{
			return 0f;
		}
		return 1f / num;
	}

	private static float? ReverseConstraintValue(string? sourcePath, string clothName, string constraintClass, string fieldName, float? value, float constraintScale)
	{
		if (!value.HasValue)
		{
			return null;
		}
		float value2 = (float)BphclConversionProfileCatalog.ReverseConstraintFieldProfile(sourcePath, clothName, constraintClass, fieldName, value.Value);
		return ScaleSoftConstraint(constraintClass, fieldName, value2, constraintScale);
	}

	private static float? ScaleSoftConstraint(string constraintClass, string fieldName, float? value, float scale)
	{
		if (!value.HasValue || !float.IsFinite(scale) || scale <= 0f)
		{
			return value;
		}
		bool flag;
		switch (constraintClass)
		{
		case "hclStandardLinkConstraintSet":
			flag = fieldName == "stiffness";
			break;
		case "hclBendLinkConstraintSet":
		{
			bool flag2 = ((fieldName == "bendStiffness" || fieldName == "stretchStiffness") ? true : false);
			flag = flag2;
			break;
		}
		case "hclBendStiffnessConstraintSet":
			flag = fieldName == "bendStiffness";
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			return value;
		}
		return value.Value / scale;
	}

	private static Vector3 ToColliderLocalPoint(Matrix4x4 transform, Vector3 world)
	{
		if (!Matrix4x4.Invert(new Matrix4x4(transform.M11, transform.M12, transform.M13, 0f, transform.M21, transform.M22, transform.M23, 0f, transform.M31, transform.M32, transform.M33, 0f, 0f, 0f, 0f, 1f), out var result))
		{
			throw new InvalidDataException("A collider transform cannot be inverted.");
		}
		Vector3 vector = world - new Vector3(transform.M41, transform.M42, transform.M43);
		return new Vector3(result.M11 * vector.X + result.M21 * vector.Y + result.M31 * vector.Z, result.M12 * vector.X + result.M22 * vector.Y + result.M32 * vector.Z, result.M13 * vector.X + result.M23 * vector.Y + result.M33 * vector.Z);
	}

	private static Vector3 ToColliderLocalDirection(Matrix4x4 transform, Vector3 worldDirection)
	{
		Vector3 world = worldDirection + new Vector3(transform.M41, transform.M42, transform.M43);
		Vector3 value = ToColliderLocalPoint(transform, world);
		if (!(value.LengthSquared() < 1E-06f))
		{
			return Vector3.Normalize(value);
		}
		return Vector3.UnitY;
	}
}
