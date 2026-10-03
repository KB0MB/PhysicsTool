namespace HKCLTool;

// Compares the source HKCL skeleton as the standalone exporter sees it with a
// native BPHCL skeleton. Link: is a runtime namespace prefix, not part of the
// underlying actor-bone identity, so comparisons intentionally ignore it.
public static class BphclSourceBoneAudit
{
    public static void Write(string hkclPath, string bphclPath)
    {
        var source = new HkclService();
        source.Load(hkclPath);
        if (source.IsBphcl || source.IsHelperBoneSidecar)
            throw new InvalidOperationException("The source must be an HKCL file.");

        var graph = HkclToBphclStandaloneGraphBuilder.Build(source);
        var native = NativeBphclDocument.Open(bphclPath);
        var nativeByName = native.Cloths
            .GroupBy(cloth => Key(cloth.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

        foreach (var cloth in graph.Cloths.OrderBy(cloth => cloth.Index))
        {
            if (!nativeByName.TryGetValue(Key(cloth.Name), out var nativeCloth) ||
                native.Skeletons.ElementAtOrDefault(nativeCloth.Index) is not { } nativeSkeleton)
            {
                Console.WriteLine($"missing|cloth={cloth.Name}|sourceBones={cloth.Bones.Count}");
                continue;
            }

            var sourceBones = cloth.Bones.OrderBy(bone => bone.Index).ToArray();
            var nativeBones = nativeSkeleton.Bones.OrderBy(bone => bone.Index).ToArray();
            var namesMatch = sourceBones.Select(bone => Key(bone.Name))
                .SequenceEqual(nativeBones.Select(bone => Key(bone.Name)), StringComparer.Ordinal);
            var parentsMatch = sourceBones.Select(bone => bone.ParentIndex)
                .SequenceEqual(nativeBones.Select(bone => bone.ParentIndex));

            Console.WriteLine(
                $"cloth|{cloth.Name}|sourceBones={sourceBones.Length}|nativeBones={nativeBones.Length}|" +
                $"namesMatch={namesMatch}|parentsMatch={parentsMatch}");

            var sourceSkins = source.GetSkinOperatorData(cloth.Index);
            foreach (var sourceSkin in sourceSkins.OrderBy(skin => skin.OperatorIndex))
            {
                var nativeRange = sourceSkin.ClassName switch
                {
                    "hclObjectSpaceSkinPOperator" => nativeCloth.ObjectSpaceSkins
                        .Where(skin => skin.OperatorIndex == sourceSkin.OperatorIndex)
                        .Select(skin => $"{skin.Deformer.StartVertexIndex}-{skin.Deformer.EndVertexIndex}")
                        .FirstOrDefault(),
                    "hclBoneSpaceSkinPOperator" => nativeCloth.BoneSpaceSkins
                        .Where(skin => skin.OperatorIndex == sourceSkin.OperatorIndex)
                        .Select(skin => $"{skin.Deformer.StartVertexIndex}-{skin.Deformer.EndVertexIndex}")
                        .FirstOrDefault(),
                    _ => null
                };
                var maxVertex = sourceSkin.BlendArrays
                    .SelectMany(array => array.Blocks)
                    .SelectMany(block => block.VertexIndices)
                    .Select(index => (int)index)
                    .DefaultIfEmpty(-1)
                    .Max();
                Console.WriteLine(
                    $"skin|cloth={cloth.Name}|op={sourceSkin.OperatorIndex}|sourceRange={sourceSkin.StartVertexIndex}-{sourceSkin.EndVertexIndex}|" +
                    $"maxPackedVertex={maxVertex}|blocks={sourceSkin.LocalPositionBlocks.Count}|nativeRange={nativeRange ?? "<none>"}");
            }

            var maximum = Math.Max(sourceBones.Length, nativeBones.Length);
            for (var index = 0; index < maximum; index++)
            {
                var sourceBone = index < sourceBones.Length ? sourceBones[index] : null;
                var nativeBone = index < nativeBones.Length ? nativeBones[index] : null;
                if (sourceBone is not null && nativeBone is not null &&
                    string.Equals(Key(sourceBone.Name), Key(nativeBone.Name), StringComparison.Ordinal) &&
                    sourceBone.ParentIndex == nativeBone.ParentIndex)
                {
                    continue;
                }

                Console.WriteLine(
                    $"boneDiff|cloth={cloth.Name}|index={index}|" +
                    $"source={(sourceBone is null ? "<none>" : $"{sourceBone.Name};parent={sourceBone.ParentIndex}")}|" +
                    $"native={(nativeBone is null ? "<none>" : $"{nativeBone.Name};parent={nativeBone.ParentIndex}")}");
            }
        }
    }

    private static string Key(string value) =>
        value.StartsWith("Link:", StringComparison.Ordinal) ? value[5..] : value;
}