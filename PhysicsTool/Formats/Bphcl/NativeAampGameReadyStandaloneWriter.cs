using System;
using System.Collections.Generic;
using System.Linq;

namespace HKCLTool;

internal static class NativeAampGameReadyStandaloneWriter
{
	private const string DefaultAxisOrder = "xyz";

	private const string DefaultSwingAxis = "y";

	private const string DefaultPreset = "leather";

	public static byte[] Create(StandaloneBphclDocumentGraph graph)
	{
		BotwAampWriter.AampListNode aampListNode = new BotwAampWriter.AampListNode("param_root");
		BotwAampWriter.AampListNode aampListNode2 = new BotwAampWriter.AampListNode("cloth_mesh_list");
		BotwAampWriter.AampListNode aampListNode3 = new BotwAampWriter.AampListNode("collidable_list");
		aampListNode.Children.Add(aampListNode2);
		aampListNode.Children.Add(aampListNode3);
		foreach (StandaloneBphclClothGraph item in graph.Cloths.OrderBy((StandaloneBphclClothGraph cloth) => cloth.Index))
		{
			string value = ResolveBaseBone(item);
			string value2 = ResolvePreset(item);
			if (BphclConversionProfileCatalog.TryGetReverseAampProfile(graph.SourcePath, item.Name, out string baseBone, out string preset))
			{
				value = baseBone;
				value2 = preset;
			}
			BotwAampWriter.AampObjectNode aampObjectNode = new BotwAampWriter.AampObjectNode($"cloth_mesh_{item.Index}");
			aampObjectNode.Parameters.Add(BotwAampWriter.AampParameterNode.StringReferenceValue("Name", item.Name));
			aampObjectNode.Parameters.Add(BotwAampWriter.AampParameterNode.StringReferenceValue("BaseBone", value));
			aampObjectNode.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("BoneCorrection", value: false));
			aampObjectNode.Parameters.Add(BotwAampWriter.AampParameterNode.StringReferenceValue("BoneCorrectionAxisOrder", "xyz"));
			aampObjectNode.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("Twist", value: false));
			aampObjectNode.Parameters.Add(BotwAampWriter.AampParameterNode.StringReferenceValue("TwistSwingAxis", "y"));
			aampObjectNode.Parameters.Add(BotwAampWriter.AampParameterNode.Single("TwistAngleCoef", 0f));
			aampObjectNode.Parameters.Add(BotwAampWriter.AampParameterNode.Single("TwistMaxAngle", 180f));
			aampObjectNode.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("KeepBoneLength", value: false));
			aampObjectNode.Parameters.Add(BotwAampWriter.AampParameterNode.StringReferenceValue("Preset", value2));
			aampListNode2.Objects.Add(aampObjectNode);
		}
		int num = 0;
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (StandaloneBphclCollider item2 in EnumerateRegisteredColliders(graph))
		{
			if (hashSet.Add(item2.Name))
			{
				BotwAampWriter.AampObjectNode aampObjectNode2 = new BotwAampWriter.AampObjectNode($"collidable_{num++}");
				aampObjectNode2.Parameters.Add(BotwAampWriter.AampParameterNode.StringReferenceValue("Name", item2.Name));
				aampObjectNode2.Parameters.Add(BotwAampWriter.AampParameterNode.Boolean("ForReplace", value: false));
				aampListNode3.Objects.Add(aampObjectNode2);
			}
		}
		return BotwAampWriter.WriteArchive("phcl", aampListNode);
	}

	private static IEnumerable<StandaloneBphclCollider> EnumerateRegisteredColliders(StandaloneBphclDocumentGraph graph)
	{
		foreach (StandaloneBphclClothGraph item in graph.Cloths.OrderBy((StandaloneBphclClothGraph cloth) => cloth.Index))
		{
			Dictionary<int, StandaloneBphclCollider> byIndex = item.Colliders.ToDictionary((StandaloneBphclCollider collider) => collider.Index);
			foreach (StandaloneBphclColliderBinding colliderBinding in item.ColliderBindings)
			{
				if (byIndex.TryGetValue(colliderBinding.ColliderIndex, out var value))
				{
					yield return value;
				}
			}
		}
	}

	private static string ResolvePreset(StandaloneBphclClothGraph cloth)
	{
		string name = cloth.Name;
		if (name.Contains("earring", StringComparison.OrdinalIgnoreCase))
		{
			return "old_leather";
		}
		if (name.Contains("mant", StringComparison.OrdinalIgnoreCase) || name.Contains("cape", StringComparison.OrdinalIgnoreCase) || name.Contains("skirt", StringComparison.OrdinalIgnoreCase) || name.Contains("outer", StringComparison.OrdinalIgnoreCase))
		{
			return "old_leather";
		}
		if (name.Contains("hair", StringComparison.OrdinalIgnoreCase))
		{
			return "hair";
		}
		return "leather";
	}

	private static string ResolveBaseBone(StandaloneBphclClothGraph cloth)
	{
		if (cloth.Name.Contains("hair", StringComparison.OrdinalIgnoreCase))
		{
			StandaloneBphclBone standaloneBphclBone = cloth.Bones.FirstOrDefault((StandaloneBphclBone bone) => string.Equals(bone.Name, "Hair_Root", StringComparison.OrdinalIgnoreCase) || string.Equals(bone.Name, "Link:Hair_Root", StringComparison.OrdinalIgnoreCase));
			if ((object)standaloneBphclBone != null)
			{
				return StripRuntimeNamespace(standaloneBphclBone.Name);
			}
		}
		HashSet<int> simulatedBoneIndices = (from binding in cloth.BoneBindings
			select binding.BoneIndex into index
			where index >= 0 && index < cloth.Bones.Count
			select index).ToHashSet();
		if (simulatedBoneIndices.Count > 0)
		{
			int[] array = (from bone in cloth.Bones.Where((StandaloneBphclBone bone) => simulatedBoneIndices.Contains(bone.Index) && !simulatedBoneIndices.Contains(bone.ParentIndex)).ToArray()
				select bone.ParentIndex into index
				where index >= 0 && index < cloth.Bones.Count
				select index).Distinct().ToArray();
			if (array.Length == 1)
			{
				return StripRuntimeNamespace(cloth.Bones[array[0]].Name);
			}
			int num = FindCommonAncestor(cloth.Bones, array);
			if (num >= 0)
			{
				return StripRuntimeNamespace(cloth.Bones[num].Name);
			}
		}
		string[] array2 = ((!cloth.Name.Contains("hair", StringComparison.OrdinalIgnoreCase)) ? new string[4] { "Head", "Link:Head", "Hair_Root", "Link:Hair_Root" } : new string[4] { "Hair_Root", "Link:Hair_Root", "Head", "Link:Head" });
		foreach (string preferredName in array2)
		{
			StandaloneBphclBone standaloneBphclBone2 = cloth.Bones.FirstOrDefault((StandaloneBphclBone bone) => string.Equals(bone.Name, preferredName, StringComparison.OrdinalIgnoreCase));
			if ((object)standaloneBphclBone2 != null)
			{
				return StripRuntimeNamespace(standaloneBphclBone2.Name);
			}
		}
		return StripRuntimeNamespace(cloth.Bones.FirstOrDefault((StandaloneBphclBone bone) => bone.ParentIndex < 0)?.Name ?? cloth.Bones[0].Name);
	}

	private static int FindCommonAncestor(IReadOnlyList<StandaloneBphclBone> bones, IReadOnlyList<int> boneIndices)
	{
		if (boneIndices.Count == 0)
		{
			return -1;
		}
		HashSet<int> hashSet = GetAncestorChain(bones, boneIndices[0]).ToHashSet();
		for (int i = 1; i < boneIndices.Count; i++)
		{
			hashSet.IntersectWith(GetAncestorChain(bones, boneIndices[i]));
		}
		return hashSet.OrderByDescending((int index) => GetAncestorChain(bones, index).Count).FirstOrDefault(-1);
	}

	private static List<int> GetAncestorChain(IReadOnlyList<StandaloneBphclBone> bones, int boneIndex)
	{
		List<int> list = new List<int>();
		HashSet<int> hashSet = new HashSet<int>();
		while (boneIndex >= 0 && boneIndex < bones.Count && hashSet.Add(boneIndex))
		{
			list.Add(boneIndex);
			boneIndex = bones[boneIndex].ParentIndex;
		}
		return list;
	}

	private static string StripRuntimeNamespace(string name)
	{
		if (!name.StartsWith("Link:", StringComparison.OrdinalIgnoreCase))
		{
			return name;
		}
		return name.Substring(5);
	}
}
