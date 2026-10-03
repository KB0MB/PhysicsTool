using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace HKCLTool;

// Compares a native BPHCL with a fresh HKCL-to-BPHCL export without relying on
// the editor projection. Output is intentionally concise so real structural
// gaps are visible beside harmless floating-point serialization noise.
internal static class BphclFidelityAudit
{
    public static void Write(string nativePath, string freshPath)
    {
        var native = NativeBphclDocument.Open(nativePath);
        var fresh = NativeBphclDocument.Open(freshPath);

        Console.WriteLine($"audit|native={Path.GetFileName(nativePath)}|fresh={Path.GetFileName(freshPath)}");
        WriteFileSummary("native", native);
        WriteFileSummary("fresh", fresh);
        WriteTypeSummary(native, fresh);
        WriteOuterColliderSummary(native, fresh);
        WriteAampSummary(native, fresh);
        WriteClothSummaries(native, fresh);
    }

    private static void WriteFileSummary(string label, NativeBphclDocument document)
    {
        Console.WriteLine(
            $"file|{label}|bytes={document.Bytes.Length}|cloths={document.Cloths.Count}|skeletons={document.Skeletons.Count}|" +
            $"outerColliders={document.Colliders.Count}|items={document.Items.Count}|patchGroups={document.InternalPatches.Count}|" +
            $"data={document.DataSection?.Size ?? 0}|type={document.TypeSection?.Size ?? 0}|index={document.IndexSection?.Size ?? 0}|" +
            $"aamp={document.Header.ParameterSize}");

        var definitions = document.TypeDefinitions.GroupBy(definition => definition.TypeIndex).ToDictionary(group => group.Key, group => group.First());
        var misaligned = document.Items
            .Select((item, index) => (item, index, alignment: definitions.GetValueOrDefault(item.TypeIndex)?.Alignment ?? 1u))
            .Where(entry => entry.alignment > 1 && entry.item.DataOffset % entry.alignment != 0)
            .Select(entry => $"{entry.index}:{document.GetTypeName(entry.item.TypeIndex) ?? $"#{entry.item.TypeIndex}"}@0x{entry.item.DataOffset:X}/a{entry.alignment}")
            .Take(12)
            .ToArray();
        Console.WriteLine($"alignment|{label}|invalid={misaligned.Length}|items=[{string.Join(',', misaligned)}]");
    }

    private static void WriteTypeSummary(NativeBphclDocument native, NativeBphclDocument fresh)
    {
        static string Hash(NativeBphclDocument document)
        {
            if (document.TypeSection is not { } section)
                return "<missing>";

            return Convert.ToHexString(SHA256.HashData(document.Bytes.AsSpan(section.Offset, section.Size)))[..16];
        }

        var nativeTypes = native.TypeNames.Skip(1).Where(name => !string.IsNullOrWhiteSpace(name)).ToHashSet(StringComparer.Ordinal);
        var freshTypes = fresh.TypeNames.Skip(1).Where(name => !string.IsNullOrWhiteSpace(name)).ToHashSet(StringComparer.Ordinal);
        var missing = nativeTypes.Except(freshTypes, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var extra = freshTypes.Except(nativeTypes, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Console.WriteLine(
            $"type|nativeBytes={native.TypeSection?.Size ?? 0}|freshBytes={fresh.TypeSection?.Size ?? 0}|" +
            $"nativeHash={Hash(native)}|freshHash={Hash(fresh)}|nativeNames={nativeTypes.Count}|freshNames={freshTypes.Count}|" +
            $"missing=[{string.Join(',', missing)}]|extra=[{string.Join(',', extra)}]");

        var nativeLayouts = native.TypeDefinitions
            .GroupBy(definition => native.GetTypeName(definition.TypeIndex) ?? $"#{definition.TypeIndex}", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(definition => DescribeTypeDefinition(native, definition)).OrderBy(value => value, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var freshLayouts = fresh.TypeDefinitions
            .GroupBy(definition => fresh.GetTypeName(definition.TypeIndex) ?? $"#{definition.TypeIndex}", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(definition => DescribeTypeDefinition(fresh, definition)).OrderBy(value => value, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var layoutDifferences = nativeLayouts.Keys.Union(freshLayouts.Keys, StringComparer.Ordinal)
            .Where(name => !nativeLayouts.GetValueOrDefault(name, Array.Empty<string>()).SequenceEqual(freshLayouts.GetValueOrDefault(name, Array.Empty<string>()), StringComparer.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine($"typeLayouts|same={layoutDifferences.Length == 0}|different=[{string.Join(',', layoutDifferences)}]");
    }

    private static string DescribeTypeDefinition(NativeBphclDocument document, NativeBphclTypeDefinition definition) =>
        $"parent={document.GetTypeName(definition.ParentTypeIndex) ?? $"#{definition.ParentTypeIndex}"};flags={definition.Flags:X};size={definition.Size};align={definition.Alignment};" +
        string.Join(",", definition.Members.Select(member =>
            $"{member.Name}@{member.Offset:X}:{document.GetTypeName(member.TypeIndex) ?? $"#{member.TypeIndex}"}:{member.Flags:X}"));

    private static void WriteOuterColliderSummary(NativeBphclDocument native, NativeBphclDocument fresh)
    {
        var freshByName = fresh.Colliders
            .GroupBy(collider => Key(collider.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var nativeCollider in native.Colliders)
        {
            var key = Key(nativeCollider.Name);
            if (!freshByName.TryGetValue(key, out var candidates))
            {
                Console.WriteLine($"outerCollider|missing|name={nativeCollider.Name}");
                continue;
            }

            if (candidates.Length != 1)
            {
                Console.WriteLine($"outerCollider|ambiguous|name={nativeCollider.Name}|fresh=[{string.Join(',', candidates.Select(collider => collider.Name))}]");
                continue;
            }

            var freshCollider = candidates[0];
            var positionDelta = SpatialDelta(nativeCollider.Translation, freshCollider.Translation);
            var axisDelta = Math.Max(
                SpatialDelta(nativeCollider.AxisX, freshCollider.AxisX),
                Math.Max(SpatialDelta(nativeCollider.AxisY, freshCollider.AxisY), SpatialDelta(nativeCollider.AxisZ, freshCollider.AxisZ)));
            var shapeDelta = Math.Max(
                Math.Max(SpatialDelta(nativeCollider.Shape.Start, freshCollider.Shape.Start), SpatialDelta(nativeCollider.Shape.End, freshCollider.Shape.End)),
                Math.Max(MathF.Abs(nativeCollider.Shape.Radius - freshCollider.Shape.Radius), MathF.Abs(nativeCollider.Shape.EndRadius - freshCollider.Shape.EndRadius)));
            var planeDelta = MaxDelta(nativeCollider.Shape.PlaneEquation, freshCollider.Shape.PlaneEquation);

            Console.WriteLine(
                $"outerCollider|name={nativeCollider.Name}|kind={nativeCollider.Shape.Kind}/{freshCollider.Shape.Kind}|" +
                $"translationDelta={positionDelta:G6}|axisDelta={axisDelta:G6}|shapeDelta={shapeDelta:G6}|planeDelta={planeDelta:G6}|" +
                $"enabled={nativeCollider.Enabled}/{freshCollider.Enabled}|pinch={nativeCollider.PinchDetectionEnabled}/{freshCollider.PinchDetectionEnabled}");

            if (positionDelta > 1.0e-4f || axisDelta > 1.0e-4f || shapeDelta > 1.0e-4f || planeDelta > 1.0e-4f)
            {
                Console.WriteLine(
                    $"outerColliderDetail|name={nativeCollider.Name}|" +
                    $"nativeT={VectorText(nativeCollider.Translation)}|freshT={VectorText(freshCollider.Translation)}|" +
                    $"nativeX={VectorText(nativeCollider.AxisX)}|freshX={VectorText(freshCollider.AxisX)}|" +
                    $"nativeY={VectorText(nativeCollider.AxisY)}|freshY={VectorText(freshCollider.AxisY)}|" +
                    $"nativeZ={VectorText(nativeCollider.AxisZ)}|freshZ={VectorText(freshCollider.AxisZ)}|" +
                    $"nativeStart={VectorText(nativeCollider.Shape.Start)}|freshStart={VectorText(freshCollider.Shape.Start)}|" +
                    $"nativeEnd={VectorText(nativeCollider.Shape.End)}|freshEnd={VectorText(freshCollider.Shape.End)}");
            }
        }

        foreach (var freshCollider in fresh.Colliders.Where(collider => !native.Colliders.Any(nativeCollider => string.Equals(Key(nativeCollider.Name), Key(collider.Name), StringComparison.Ordinal))))
            Console.WriteLine($"outerCollider|extra|name={freshCollider.Name}");
    }

    private static void WriteAampSummary(NativeBphclDocument native, NativeBphclDocument fresh)
    {
        var nativeProfiles = ReadAampProfiles(native);
        var freshProfiles = ReadAampProfiles(fresh);

        var nativeProfileOrder = ReadAampList(native, "cloth_mesh_list").Select(item => Key(ReadParameter(item, "Name"))).ToArray();
        var freshProfileOrder = ReadAampList(fresh, "cloth_mesh_list").Select(item => Key(ReadParameter(item, "Name"))).ToArray();
        Console.WriteLine($"aamp|nativeProfiles={nativeProfiles.Count}|freshProfiles={freshProfiles.Count}");
        Console.WriteLine($"aampOrder|same={nativeProfileOrder.SequenceEqual(freshProfileOrder, StringComparer.Ordinal)}|native=[{string.Join(',', nativeProfileOrder)}]|fresh=[{string.Join(',', freshProfileOrder)}]");
        foreach (var (name, nativeProfile) in nativeProfiles.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!freshProfiles.TryGetValue(name, out var freshProfile))
            {
                Console.WriteLine($"aampProfile|missing|name={name}|native={nativeProfile}");
                continue;
            }

            Console.WriteLine(string.Equals(nativeProfile, freshProfile, StringComparison.Ordinal)
                ? $"aampProfile|same|name={name}"
                : $"aampProfile|diff|name={name}|native={nativeProfile}|fresh={freshProfile}");
        }

        foreach (var (name, freshProfile) in freshProfiles.Where(pair => !nativeProfiles.ContainsKey(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine($"aampProfile|extra|name={name}|fresh={freshProfile}");

        var nativeColliderNames = ReadAampColliderNames(native);
        var freshColliderNames = ReadAampColliderNames(fresh);
        Console.WriteLine(
            $"aampColliders|same={nativeColliderNames.SequenceEqual(freshColliderNames, StringComparer.Ordinal)}|" +
            $"native=[{string.Join(',', nativeColliderNames)}]|fresh=[{string.Join(',', freshColliderNames)}]");
    }

    private static void WriteClothSummaries(NativeBphclDocument native, NativeBphclDocument fresh)
    {
        var freshByName = fresh.Cloths
            .GroupBy(cloth => Key(cloth.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var nativeCloth in native.Cloths)
        {
            var key = Key(nativeCloth.Name);
            if (!freshByName.TryGetValue(key, out var candidates))
            {
                Console.WriteLine($"cloth|missing|name={nativeCloth.Name}");
                continue;
            }

            if (candidates.Length != 1)
            {
                Console.WriteLine($"cloth|ambiguous|name={nativeCloth.Name}|fresh=[{string.Join(',', candidates.Select(cloth => cloth.Name))}]");
                continue;
            }

            var freshCloth = candidates[0];
            Console.WriteLine(
                $"cloth|name={nativeCloth.Name}|index={nativeCloth.Index}/{freshCloth.Index}|sims={nativeCloth.SimCloths.Count}/{freshCloth.SimCloths.Count}|" +
                $"buffers={nativeCloth.BufferDefinitions.Count}/{freshCloth.BufferDefinitions.Count}|" +
                $"sets={nativeCloth.TransformSetDefinitions.Count}/{freshCloth.TransformSetDefinitions.Count}|" +
                $"ops={nativeCloth.Operators.Count}/{freshCloth.Operators.Count}|" +
                $"states={nativeCloth.States.Count}/{freshCloth.States.Count}|" +
                $"objectSkins={nativeCloth.ObjectSpaceSkins.Count}/{freshCloth.ObjectSpaceSkins.Count}|" +
                $"boneSkins={nativeCloth.BoneSpaceSkins.Count}/{freshCloth.BoneSpaceSkins.Count}|" +
                $"meshDeformers={nativeCloth.SimpleMeshBoneDeformers.Count}/{freshCloth.SimpleMeshBoneDeformers.Count}");

            WriteRuntimeRoutingSummary(nativeCloth.Name, nativeCloth, freshCloth, native.Skeletons.ElementAtOrDefault(nativeCloth.Index), fresh.Skeletons.ElementAtOrDefault(freshCloth.Index));
            WriteSkinRoutingSummary(nativeCloth.Name, nativeCloth.ObjectSpaceSkins, freshCloth.ObjectSpaceSkins);
            WriteSkinPayloadSummary(nativeCloth.Name, nativeCloth.ObjectSpaceSkins, freshCloth.ObjectSpaceSkins);
            WriteBoneSpaceSkinSummary(nativeCloth.Name, nativeCloth.BoneSpaceSkins, freshCloth.BoneSpaceSkins);
            WriteSkeletonPayloadSummary(nativeCloth.Name, native.Skeletons.ElementAtOrDefault(nativeCloth.Index), fresh.Skeletons.ElementAtOrDefault(freshCloth.Index));
            WriteMeshDeformerPayloadSummary(native, nativeCloth.Name, nativeCloth.SimpleMeshBoneDeformers, fresh, freshCloth.SimpleMeshBoneDeformers);
            WriteRawObjectPayloadSummary(native, nativeCloth, fresh, freshCloth);
            WriteRawGraphDifferenceSummary(native, nativeCloth, fresh, freshCloth);
            WriteStateDependencySummary(nativeCloth.Name, nativeCloth.States, freshCloth.States);
            var simCount = Math.Max(nativeCloth.SimCloths.Count, freshCloth.SimCloths.Count);
            for (var simIndex = 0; simIndex < simCount; simIndex++)
            {
                if (simIndex >= nativeCloth.SimCloths.Count || simIndex >= freshCloth.SimCloths.Count)
                {
                    Console.WriteLine($"sim|cloth={nativeCloth.Name}|index={simIndex}|missing={(simIndex >= nativeCloth.SimCloths.Count ? "native" : "fresh")}");
                    continue;
                }

                WriteSimulationSummary(native, nativeCloth.Name, nativeCloth.SimCloths[simIndex], fresh, freshCloth.SimCloths[simIndex]);
            }
        }

        foreach (var freshCloth in fresh.Cloths.Where(cloth => !native.Cloths.Any(nativeCloth => string.Equals(Key(nativeCloth.Name), Key(cloth.Name), StringComparison.Ordinal))))
            Console.WriteLine($"cloth|extra|name={freshCloth.Name}");
    }

    private static void WriteRuntimeRoutingSummary(
        string clothName,
        NativeBphclCloth native,
        NativeBphclCloth fresh,
        NativeBphclSkeleton? nativeSkeleton,
        NativeBphclSkeleton? freshSkeleton)
    {
        var operatorCount = Math.Max(native.Operators.Count, fresh.Operators.Count);
        for (var index = 0; index < operatorCount; index++)
        {
            if (index >= native.Operators.Count || index >= fresh.Operators.Count)
            {
                Console.WriteLine($"operatorRoute|cloth={clothName}|index={index}|missing={(index >= native.Operators.Count ? "native" : "fresh")}");
                continue;
            }

            var nativeRoute = DescribeOperatorRoute(native.Operators[index], nativeSkeleton);
            var freshRoute = DescribeOperatorRoute(fresh.Operators[index], freshSkeleton);
            Console.WriteLine(
                $"operatorRoute|cloth={clothName}|index={index}|same={string.Equals(nativeRoute, freshRoute, StringComparison.Ordinal)}|" +
                $"native={nativeRoute}|fresh={freshRoute}");
        }

        var stateCount = Math.Max(native.States.Count, fresh.States.Count);
        for (var index = 0; index < stateCount; index++)
        {
            if (index >= native.States.Count || index >= fresh.States.Count)
            {
                Console.WriteLine($"stateRoute|cloth={clothName}|index={index}|missing={(index >= native.States.Count ? "native" : "fresh")}");
                continue;
            }

            var nativeRoute = DescribeStateRoute(native.States[index], nativeSkeleton);
            var freshRoute = DescribeStateRoute(fresh.States[index], freshSkeleton);
            Console.WriteLine(
                $"stateRoute|cloth={clothName}|index={index}|same={string.Equals(nativeRoute, freshRoute, StringComparison.Ordinal)}|" +
                $"native={nativeRoute}|fresh={freshRoute}");
        }
    }

    private static void WriteSkinRoutingSummary(
        string clothName,
        IReadOnlyList<NativeBphclObjectSpaceSkin> nativeSkins,
        IReadOnlyList<NativeBphclObjectSpaceSkin> freshSkins)
    {
        var count = Math.Max(nativeSkins.Count, freshSkins.Count);
        for (var index = 0; index < count; index++)
        {
            if (index >= nativeSkins.Count || index >= freshSkins.Count)
            {
                Console.WriteLine($"skinRoute|cloth={clothName}|index={index}|missing={(index >= nativeSkins.Count ? "native" : "fresh")}");
                continue;
            }

            var nativeRoute = DescribeSkinRoute(nativeSkins[index]);
            var freshRoute = DescribeSkinRoute(freshSkins[index]);
            Console.WriteLine(
                $"skinRoute|cloth={clothName}|index={index}|same={string.Equals(nativeRoute, freshRoute, StringComparison.Ordinal)}|" +
                $"native={nativeRoute}|fresh={freshRoute}");
        }
    }

    private static string DescribeSkinRoute(NativeBphclObjectSpaceSkin skin)
    {
        return $"op={skin.OperatorIndex};out={skin.OutputBufferIndex};range={skin.Deformer.StartVertexIndex}-{skin.Deformer.EndVertexIndex};" +
            $"positionBlocks={skin.LocalPs.Count};positionSlots={skin.LocalPs.Count * 16};" +
            $"controls={skin.Deformer.ControlBytes.Count}";
    }
    // The route alone is not enough: these payloads turn simulated particles
    // back into visible mesh vertices. Hash them so the audit exposes a raw
    // skin/deformer mismatch without flooding the console with every float.
    private static void WriteSkinPayloadSummary(
        string clothName,
        IReadOnlyList<NativeBphclObjectSpaceSkin> nativeSkins,
        IReadOnlyList<NativeBphclObjectSpaceSkin> freshSkins)
    {
        var count = Math.Min(nativeSkins.Count, freshSkins.Count);
        for (var index = 0; index < count; index++)
        {
            var nativeSkin = nativeSkins[index];
            var freshSkin = freshSkins[index];
            Console.WriteLine(
                $"skinPayload|cloth={clothName}|index={index}|" +
                $"matrices={HashMatrices(nativeSkin.BoneFromSkinMeshTransforms)}/{HashMatrices(freshSkin.BoneFromSkinMeshTransforms)}|" +
                $"subset={HashUShorts(nativeSkin.TransformSubset)}/{HashUShorts(freshSkin.TransformSubset)}|" +
                $"positions={HashPackedPositions(nativeSkin.LocalPs)}/{HashPackedPositions(freshSkin.LocalPs)}|" +
                $"blends={HashDeformer(nativeSkin.Deformer)}/{HashDeformer(freshSkin.Deformer)}|" +
                $"controls={HashBytes(nativeSkin.Deformer.ControlBytes)}/{HashBytes(freshSkin.Deformer.ControlBytes)}");
            Console.WriteLine(
                $"skinExponent|cloth={clothName}|index={index}|native={DescribePackedExponents(nativeSkin.LocalPs)}|fresh={DescribePackedExponents(freshSkin.LocalPs)}");
        }
    }

    // Bone-space skins power accessories such as earrings. They have a
    // different packed deformer layout, so audit them separately from the
    // object-space path rather than silently assuming both serialize alike.
    private static void WriteBoneSpaceSkinSummary(
        string clothName,
        IReadOnlyList<NativeBphclBoneSpaceSkin> nativeSkins,
        IReadOnlyList<NativeBphclBoneSpaceSkin> freshSkins)
    {
        var count = Math.Max(nativeSkins.Count, freshSkins.Count);
        for (var index = 0; index < count; index++)
        {
            if (index >= nativeSkins.Count || index >= freshSkins.Count)
            {
                Console.WriteLine($"boneSkin|cloth={clothName}|index={index}|missing={(index >= nativeSkins.Count ? "native" : "fresh")}");
                continue;
            }

            var nativeSkin = nativeSkins[index];
            var freshSkin = freshSkins[index];
            var nativeRoute = DescribeBoneSkinRoute(nativeSkin);
            var freshRoute = DescribeBoneSkinRoute(freshSkin);
            Console.WriteLine(
                $"boneSkinRoute|cloth={clothName}|index={index}|same={string.Equals(nativeRoute, freshRoute, StringComparison.Ordinal)}|" +
                $"native={nativeRoute}|fresh={freshRoute}");
            Console.WriteLine(
                $"boneSkinPayload|cloth={clothName}|index={index}|" +
                $"subset={HashUShorts(nativeSkin.TransformSubset)}/{HashUShorts(freshSkin.TransformSubset)}|" +
                $"positions={HashUnpackedPositions(nativeSkin.LocalPs)}/{HashUnpackedPositions(freshSkin.LocalPs)}|" +
                $"blends={HashBoneDeformer(nativeSkin.Deformer)}/{HashBoneDeformer(freshSkin.Deformer)}|" +
                $"controls={HashBytes(nativeSkin.Deformer.ControlBytes)}/{HashBytes(freshSkin.Deformer.ControlBytes)}");
        }
    }

    private static string DescribeBoneSkinRoute(NativeBphclBoneSpaceSkin skin) =>
        $"op={skin.OperatorIndex};out={skin.OutputBufferIndex};set={skin.TransformSetIndex};" +
        $"range={skin.Deformer.StartVertexIndex}-{skin.Deformer.EndVertexIndex};" +
        $"positionBlocks={skin.LocalPs.Count};positionSlots={skin.LocalPs.Count * 16};" +
        $"controls={skin.Deformer.ControlBytes.Count};batch={skin.Deformer.BatchSizeSpu};partial={skin.Deformer.PartialWrite}";
    private static void WriteSkeletonPayloadSummary(
        string clothName,
        NativeBphclSkeleton? nativeSkeleton,
        NativeBphclSkeleton? freshSkeleton)
    {
        if (nativeSkeleton is null || freshSkeleton is null)
        {
            Console.WriteLine($"skeletonPayload|cloth={clothName}|missing={(nativeSkeleton is null ? "native" : "fresh")}");
            return;
        }

        var pairCount = Math.Min(nativeSkeleton.Bones.Count, freshSkeleton.Bones.Count);
        var maximumPoseDelta = 0.0f;
        var maximumOrientationDelta = 0.0f;
        var metadataMatches = nativeSkeleton.Bones.Count == freshSkeleton.Bones.Count;
        NativeBphclBone? firstMismatch = null;
        NativeBphclBone? firstFreshMismatch = null;
        for (var index = 0; index < pairCount; index++)
        {
            var nativeBone = nativeSkeleton.Bones[index];
            var freshBone = freshSkeleton.Bones[index];
            var delta = Math.Max(MaxDelta(nativeBone.Translation, freshBone.Translation),
                Math.Max(MaxDelta(nativeBone.Rotation, freshBone.Rotation), MaxDelta(nativeBone.Scale, freshBone.Scale)));
            maximumPoseDelta = Math.Max(maximumPoseDelta, delta);
            var nativeRotation = new Quaternion(nativeBone.Rotation.X, nativeBone.Rotation.Y, nativeBone.Rotation.Z, nativeBone.Rotation.W);
            var freshRotation = new Quaternion(freshBone.Rotation.X, freshBone.Rotation.Y, freshBone.Rotation.Z, freshBone.Rotation.W);
            if (nativeRotation.LengthSquared() > 1.0e-8f && freshRotation.LengthSquared() > 1.0e-8f)
                maximumOrientationDelta = Math.Max(maximumOrientationDelta, 1.0f - MathF.Abs(Quaternion.Dot(Quaternion.Normalize(nativeRotation), Quaternion.Normalize(freshRotation))));
            var metadataEqual = nativeBone.Name == freshBone.Name &&
                nativeBone.ParentIndex == freshBone.ParentIndex &&
                nativeBone.LockTranslation == freshBone.LockTranslation;
            metadataMatches &= metadataEqual;
            if (firstMismatch is null && (!metadataEqual || delta > 1.0e-5f))
            {
                firstMismatch = nativeBone;
                firstFreshMismatch = freshBone;
            }
        }

        var nativeSignature = HashText(string.Join('|', nativeSkeleton.Bones.Select(DescribeBone)));
        var freshSignature = HashText(string.Join('|', freshSkeleton.Bones.Select(DescribeBone)));
        Console.WriteLine($"skeletonPayload|cloth={clothName}|bones={nativeSkeleton.Bones.Count}/{freshSkeleton.Bones.Count}|metadataSame={metadataMatches}|maxPoseDelta={maximumPoseDelta:G7}|orientationDelta={maximumOrientationDelta:G7}|poses={nativeSignature}/{freshSignature}");
        if (firstMismatch is not null && firstFreshMismatch is not null)
            Console.WriteLine($"skeletonPoseDetail|cloth={clothName}|bone={firstMismatch.Index}:{firstMismatch.Name}/{firstFreshMismatch.Name}|native={DescribeBone(firstMismatch)}|fresh={DescribeBone(firstFreshMismatch)}");
    }

    private static void WriteMeshDeformerPayloadSummary(
        NativeBphclDocument nativeDocument,
        string clothName,
        IReadOnlyList<NativeBphclSimpleMeshBoneDeform> nativeDeformers,
        NativeBphclDocument freshDocument,
        IReadOnlyList<NativeBphclSimpleMeshBoneDeform> freshDeformers)
    {
        var pairCount = Math.Min(nativeDeformers.Count, freshDeformers.Count);
        var maximumTransformDelta = 0.0f;
        var routingMatches = nativeDeformers.Count == freshDeformers.Count;
        for (var index = 0; index < pairCount; index++)
        {
            var nativeDeformer = nativeDeformers[index];
            var freshDeformer = freshDeformers[index];
            routingMatches &= nativeDeformer.InputBufferIndex == freshDeformer.InputBufferIndex &&
                nativeDeformer.OutputTransformSetIndex == freshDeformer.OutputTransformSetIndex &&
                nativeDeformer.BoneAxis == freshDeformer.BoneAxis &&
                nativeDeformer.TriangleBonePairs.SequenceEqual(freshDeformer.TriangleBonePairs);
            var transforms = Math.Min(nativeDeformer.LocalBoneTransforms.Count, freshDeformer.LocalBoneTransforms.Count);
            for (var transform = 0; transform < transforms; transform++)
                maximumTransformDelta = Math.Max(maximumTransformDelta, MaxDelta(nativeDeformer.LocalBoneTransforms[transform], freshDeformer.LocalBoneTransforms[transform]));
            routingMatches &= nativeDeformer.LocalBoneTransforms.Count == freshDeformer.LocalBoneTransforms.Count;
        }

        var nativeSignature = HashText(string.Join('|', nativeDeformers.Select(DescribeMeshDeformer)));
        var freshSignature = HashText(string.Join('|', freshDeformers.Select(DescribeMeshDeformer)));
        Console.WriteLine($"meshDeformerPayload|cloth={clothName}|count={nativeDeformers.Count}/{freshDeformers.Count}|routingSame={routingMatches}|maxTransformDelta={maximumTransformDelta:G7}|data={nativeSignature}/{freshSignature}");
        WriteMeshDeformerStorageSummary(nativeDocument, clothName, nativeDeformers, freshDocument, freshDeformers);
    }

    private static void WriteMeshDeformerStorageSummary(
        NativeBphclDocument nativeDocument,
        string clothName,
        IReadOnlyList<NativeBphclSimpleMeshBoneDeform> nativeDeformers,
        NativeBphclDocument freshDocument,
        IReadOnlyList<NativeBphclSimpleMeshBoneDeform> freshDeformers)
    {
        var count = Math.Min(nativeDeformers.Count, freshDeformers.Count);
        for (var index = 0; index < count; index++)
        {
            var nativeDeformer = nativeDeformers[index];
            var freshDeformer = freshDeformers[index];
            var nativePointer = checked(nativeDeformer.DataOffset + 96u);
            var freshPointer = checked(freshDeformer.DataOffset + 96u);
            if (!nativeDocument.TryGetReferencedItem(nativePointer, out var nativeStorageIndex) ||
                !freshDocument.TryGetReferencedItem(freshPointer, out var freshStorageIndex))
            {
                Console.WriteLine($"meshDeformerStorage|cloth={clothName}|index={index}|reference=missing");
                continue;
            }

            var nativeStorage = nativeDocument.GetItem(nativeStorageIndex);
            var freshStorage = freshDocument.GetItem(freshStorageIndex);
            var nativeSize = nativeDocument.TypeDefinitions.FirstOrDefault(definition => definition.TypeIndex == nativeStorage.TypeIndex)?.Size;
            var freshSize = freshDocument.TypeDefinitions.FirstOrDefault(definition => definition.TypeIndex == freshStorage.TypeIndex)?.Size;
            var nativeHash = GetMaskedItemPayloadHash(nativeDocument, nativeStorage, nativeSize);
            var freshHash = GetMaskedItemPayloadHash(freshDocument, freshStorage, freshSize);
            var firstDifference = DescribeFirstMatrixDifference(nativeDeformer.LocalBoneTransforms, freshDeformer.LocalBoneTransforms);
            Console.WriteLine(
                $"meshDeformerStorage|cloth={clothName}|index={index}|item={nativeStorage.TypeIndex}[{nativeStorage.Count}]/{freshStorage.TypeIndex}[{freshStorage.Count}]|" +
                $"size={nativeSize?.ToString() ?? "none"}/{freshSize?.ToString() ?? "none"}|raw={nativeHash}/{freshHash}|firstMatrixDiff={firstDifference}");
        }
    }

    private static string DescribeFirstMatrixDifference(IReadOnlyList<Matrix4x4> nativeMatrices, IReadOnlyList<Matrix4x4> freshMatrices)
    {
        var count = Math.Min(nativeMatrices.Count, freshMatrices.Count);
        for (var matrixIndex = 0; matrixIndex < count; matrixIndex++)
        {
            var nativeValues = MatrixValues(nativeMatrices[matrixIndex]);
            var freshValues = MatrixValues(freshMatrices[matrixIndex]);
            for (var valueIndex = 0; valueIndex < nativeValues.Length; valueIndex++)
            {
                if (nativeValues[valueIndex] == freshValues[valueIndex])
                    continue;
                return $"{matrixIndex}:m{valueIndex / 4 + 1}{valueIndex % 4 + 1}={nativeValues[valueIndex]:G9}/{freshValues[valueIndex]:G9}";
            }
        }
        return nativeMatrices.Count == freshMatrices.Count ? "none" : $"count={nativeMatrices.Count}/{freshMatrices.Count}";
    }

    private static float[] MatrixValues(Matrix4x4 matrix) =>
    [
        matrix.M11, matrix.M12, matrix.M13, matrix.M14,
        matrix.M21, matrix.M22, matrix.M23, matrix.M24,
        matrix.M31, matrix.M32, matrix.M33, matrix.M34,
        matrix.M41, matrix.M42, matrix.M43, matrix.M44
    ];
    // Walk the ITEM/PTCH graph rather than only the top-level operators. This
    // catches opaque arrays that the friendly document projection does not model.
    private static void WriteRawGraphDifferenceSummary(
        NativeBphclDocument nativeDocument,
        NativeBphclCloth nativeCloth,
        NativeBphclDocument freshDocument,
        NativeBphclCloth freshCloth)
    {
        var roots = new List<(int NativeItem, int FreshItem, string Path)>
        {
            (nativeCloth.ItemIndex, freshCloth.ItemIndex, "cloth")
        };
        var nativeSkeleton = nativeDocument.Skeletons.ElementAtOrDefault(nativeCloth.Index);
        var freshSkeleton = freshDocument.Skeletons.ElementAtOrDefault(freshCloth.Index);
        if (nativeSkeleton is not null && freshSkeleton is not null)
            roots.Add((nativeSkeleton.ItemIndex, freshSkeleton.ItemIndex, "skeleton"));

        var queue = new Queue<(int NativeItem, int FreshItem, string Path)>();
        foreach (var root in roots)
            queue.Enqueue(root);
        var visited = new HashSet<(int NativeItem, int FreshItem)>();
        var differences = new List<string>();
        var visitedNodes = 0;

        while (queue.Count > 0 && differences.Count < 32)
        {
            var (nativeItemIndex, freshItemIndex, path) = queue.Dequeue();
            if (!visited.Add((nativeItemIndex, freshItemIndex)))
                continue;
            visitedNodes++;
            var nativeItem = nativeDocument.GetItem(nativeItemIndex);
            var freshItem = freshDocument.GetItem(freshItemIndex);
            var nativeType = nativeDocument.GetTypeName(nativeItem.TypeIndex) ?? $"#{nativeItem.TypeIndex}";
            var freshType = freshDocument.GetTypeName(freshItem.TypeIndex) ?? $"#{freshItem.TypeIndex}";
            if (!string.Equals(nativeType, freshType, StringComparison.Ordinal) || nativeItem.Count != freshItem.Count)
            {
                differences.Add($"{path}:type={nativeType}[{nativeItem.Count}]/{freshType}[{freshItem.Count}]");
                continue;
            }

            if (nativeItem.Flags != freshItem.Flags)
                differences.Add($"{path}:itemFlags=0x{nativeItem.Flags:X8}/0x{freshItem.Flags:X8}");

            var nativeBytes = GetMaskedItemPayloadBytes(nativeDocument, nativeItem);
            var freshBytes = GetMaskedItemPayloadBytes(freshDocument, freshItem);
            if (nativeBytes is not null && freshBytes is not null)
            {
                var compared = Math.Min(nativeBytes.Length, freshBytes.Length);
                var byteDifferences = 0;
                var firstDifference = -1;
                for (var index = 0; index < compared; index++)
                {
                    if (nativeBytes[index] == freshBytes[index])
                        continue;
                    byteDifferences++;
                    if (firstDifference < 0)
                        firstDifference = index;
                }
                byteDifferences += Math.Abs(nativeBytes.Length - freshBytes.Length);
                if (byteDifferences > 0)
                    differences.Add($"{path}:{nativeType}[{nativeItem.Count}]:bytes={byteDifferences}@{firstDifference}:hash={HashBytes(nativeBytes)[..8]}/{HashBytes(freshBytes)[..8]}");
            }

            var nativeReferences = GetRelativeReferences(nativeDocument, nativeItem);
            var freshReferences = GetRelativeReferences(freshDocument, freshItem);
            foreach (var offset in nativeReferences.Keys.Union(freshReferences.Keys).OrderBy(value => value))
            {
                if (!nativeReferences.TryGetValue(offset, out var nativeReference) || !freshReferences.TryGetValue(offset, out var freshReference))
                {
                    differences.Add($"{path}:pointer@0x{offset:X}={(nativeReferences.ContainsKey(offset) ? "native" : "fresh")}-only");
                    continue;
                }

                var nativePatchType = nativeDocument.GetTypeName(nativeReference.PatchType) ?? $"#{nativeReference.PatchType}";
                var freshPatchType = freshDocument.GetTypeName(freshReference.PatchType) ?? $"#{freshReference.PatchType}";
                if (!string.Equals(nativePatchType, freshPatchType, StringComparison.Ordinal))
                    differences.Add($"{path}:patch@0x{offset:X}={nativePatchType}/{freshPatchType}");

                queue.Enqueue((nativeReference.TargetItem, freshReference.TargetItem, $"{path}/0x{offset:X}"));
            }
        }

        Console.WriteLine(differences.Count == 0
            ? $"rawGraph|cloth={nativeCloth.Name}|same=True|nodes={visitedNodes}"
            : $"rawGraph|cloth={nativeCloth.Name}|same=False|nodes={visitedNodes}|diffs={string.Join(';', differences)}");
    }

    private static IReadOnlyDictionary<int, RawReference> GetRelativeReferences(NativeBphclDocument document, NativeBphclItem item)
    {
        var elementSize = GetRawElementSize(document, item);
        var length = GetItemByteLength(item, elementSize);
        if (length == 0)
            return new Dictionary<int, RawReference>();

        var patchTypes = document.InternalPatches
            .SelectMany(patch => patch.Offsets.Select(offset => (Offset: offset, patch.TypeIndex)))
            .ToDictionary(entry => entry.Offset, entry => entry.TypeIndex);
        var references = new Dictionary<int, RawReference>();
        foreach (var absoluteOffset in patchTypes.Keys.Where(offset => offset >= item.DataOffset && offset < item.DataOffset + length))
        {
            if (document.TryGetReferencedItem(absoluteOffset, out var target))
                references[checked((int)(absoluteOffset - item.DataOffset))] = new RawReference(target, patchTypes[absoluteOffset]);
        }
        return references;
    }

    private readonly record struct RawReference(int TargetItem, uint PatchType);

    private static byte[]? GetMaskedItemPayloadBytes(NativeBphclDocument document, NativeBphclItem item)
    {
        if (document.DataSection is null)
            return null;
        var length = GetItemByteLength(item, GetRawElementSize(document, item));
        if (length == 0 || item.DataOffset > document.DataSection.PayloadSize - length)
            return null;
        var payload = document.Bytes.AsSpan(document.DataSection.PayloadOffset + checked((int)item.DataOffset), checked((int)length)).ToArray();
        foreach (var relocation in document.RelocationOffsets.Where(offset => offset >= item.DataOffset && offset < item.DataOffset + length))
        {
            var relative = checked((int)(relocation - item.DataOffset));
            Array.Clear(payload, relative, Math.Min(sizeof(uint), payload.Length - relative));
        }
        return payload;
    }

    private static uint? GetRawElementSize(NativeBphclDocument document, NativeBphclItem item)
    {
        var reflected = document.TypeDefinitions.FirstOrDefault(definition => definition.TypeIndex == item.TypeIndex)?.Size;
        if (reflected is > 0)
            return reflected;
        return (document.GetTypeName(item.TypeIndex) ?? string.Empty) switch
        {
            "hkMatrix4" => 64,
            "hkVector4" => 16,
            "hkUint64" or "hkInt64" => 8,
            "hkUint32" or "hkInt32" or "hkReal" => 4,
            "hkUint16" or "hkInt16" => 2,
            "hkUint8" or "hkInt8" or "hkBool" or "char" => 1,
            _ => null
        };
    }
    // Compare reflected object payloads after masking PTCH-backed pointer slots.
    // This deliberately exposes fields the specialised parser has not modelled yet.
    private static void WriteRawObjectPayloadSummary(
        NativeBphclDocument nativeDocument,
        NativeBphclCloth nativeCloth,
        NativeBphclDocument freshDocument,
        NativeBphclCloth freshCloth)
    {
        var pairs = new List<(string Label, int NativeItem, int FreshItem)>
        {
            ("cloth", nativeCloth.ItemIndex, freshCloth.ItemIndex)
        };

        if (nativeCloth.Index == 0)
        {
            foreach (var nativeRoot in nativeDocument.RootVariants)
            {
                var freshRoot = freshDocument.RootVariants.FirstOrDefault(root => string.Equals(root.ClassName, nativeRoot.ClassName, StringComparison.Ordinal));
                if (freshRoot is not null)
                    pairs.Add(($"root={nativeRoot.ClassName}", nativeRoot.ObjectItemIndex, freshRoot.ObjectItemIndex));
            }
        }

        var simulationCount = Math.Min(nativeCloth.SimCloths.Count, freshCloth.SimCloths.Count);
        for (var index = 0; index < simulationCount; index++)
            pairs.Add(($"simulation[{index}]", nativeCloth.SimCloths[index].ItemIndex, freshCloth.SimCloths[index].ItemIndex));

        var operatorCount = Math.Min(nativeCloth.Operators.Count, freshCloth.Operators.Count);
        for (var index = 0; index < operatorCount; index++)
            pairs.Add(($"operator[{index}]={nativeCloth.Operators[index].ClassName}", nativeCloth.Operators[index].ItemIndex, freshCloth.Operators[index].ItemIndex));

        var stateCount = Math.Min(nativeCloth.States.Count, freshCloth.States.Count);
        for (var index = 0; index < stateCount; index++)
            pairs.Add(($"state[{index}]", nativeCloth.States[index].ItemIndex, freshCloth.States[index].ItemIndex));

        foreach (var (label, nativeItemIndex, freshItemIndex) in pairs)
            WriteRawObjectComparison(nativeDocument, nativeCloth.Name, label, nativeItemIndex, freshDocument, freshItemIndex);
    }

    private static void WriteRawObjectComparison(
        NativeBphclDocument nativeDocument,
        string clothName,
        string label,
        int nativeItemIndex,
        NativeBphclDocument freshDocument,
        int freshItemIndex)
    {
        var nativeItem = nativeDocument.GetItem(nativeItemIndex);
        var freshItem = freshDocument.GetItem(freshItemIndex);
        var nativeType = nativeDocument.GetTypeName(nativeItem.TypeIndex) ?? $"#{nativeItem.TypeIndex}";
        var freshType = freshDocument.GetTypeName(freshItem.TypeIndex) ?? $"#{freshItem.TypeIndex}";
        // Type definitions are sparse and TYPE ids are not guaranteed to be
        // list indexes. Look them up by the actual Havok type id.
        var nativeDefinition = nativeDocument.TypeDefinitions.FirstOrDefault(definition => definition.TypeIndex == nativeItem.TypeIndex);
        var freshDefinition = freshDocument.TypeDefinitions.FirstOrDefault(definition => definition.TypeIndex == freshItem.TypeIndex);
        var nativeSize = nativeDefinition?.Size;
        var freshSize = freshDefinition?.Size;
        if (nativeDocument.DataSection is null || freshDocument.DataSection is null || nativeSize is null || freshSize is null)
        {
            Console.WriteLine($"rawObjectSkipped|cloth={clothName}|label={label}|type={nativeType}/{freshType}|sizes={nativeSize?.ToString() ?? "none"}/{freshSize?.ToString() ?? "none"}");
            return;
        }

        var size = checked((int)Math.Min(nativeSize.Value, freshSize.Value));
        var nativeStart = checked((int)nativeItem.DataOffset);
        var freshStart = checked((int)freshItem.DataOffset);
        if (nativeStart < 0 || freshStart < 0 || nativeStart > nativeDocument.DataSection.PayloadSize - size || freshStart > freshDocument.DataSection.PayloadSize - size)
            return;

        var nativePayload = nativeDocument.Bytes.AsSpan(nativeDocument.DataSection.PayloadOffset + nativeStart, size);
        var freshPayload = freshDocument.Bytes.AsSpan(freshDocument.DataSection.PayloadOffset + freshStart, size);
        var nativePointers = nativeDocument.RelocationOffsets.Where(offset => offset >= nativeItem.DataOffset && offset < nativeItem.DataOffset + size)
            .Select(offset => checked((int)(offset - nativeItem.DataOffset))).ToHashSet();
        var freshPointers = freshDocument.RelocationOffsets.Where(offset => offset >= freshItem.DataOffset && offset < freshItem.DataOffset + size)
            .Select(offset => checked((int)(offset - freshItem.DataOffset))).ToHashSet();
        var differences = new List<int>();
        for (var offset = 0; offset < size; offset++)
        {
            var pointerByte = nativePointers.Any(pointer => offset >= pointer && offset < pointer + 4) ||
                freshPointers.Any(pointer => offset >= pointer && offset < pointer + 4);
            if (!pointerByte && nativePayload[offset] != freshPayload[offset])
                differences.Add(offset);
        }

        Console.WriteLine($"rawObject|cloth={clothName}|label={label}|type={nativeType}/{freshType}|size={size}|nonPointerDiffs={differences.Count}");
        WriteRawPointerRouteSummary(nativeDocument, nativeItem, freshDocument, freshItem, clothName, label, size);
        if (differences.Count == 0)
            return;

        var groups = differences
            .GroupBy(offset => DescribeMember(nativeDefinition, offset))
            .Select(group => $"{group.Key}@[{string.Join(',', group.Take(20))}{(group.Count() > 20 ? ",..." : string.Empty)}]");
        Console.WriteLine($"rawObjectDetail|cloth={clothName}|label={label}|{string.Join(';', groups)}");
    }


    // DATA pointers are patched through ITEM indices. Equal inline bytes do
    // not prove that the two objects point at equivalent backing arrays, so
    // compare the relocation route independently of the object payload.
    private static void WriteRawPointerRouteSummary(
        NativeBphclDocument nativeDocument,
        NativeBphclItem nativeItem,
        NativeBphclDocument freshDocument,
        NativeBphclItem freshItem,
        string clothName,
        string label,
        int objectSize)
    {
        var nativeRoutes = GetPointerRoutes(nativeDocument, nativeItem, objectSize);
        var freshRoutes = GetPointerRoutes(freshDocument, freshItem, objectSize);
        var offsets = nativeRoutes.Keys.Union(freshRoutes.Keys).OrderBy(offset => offset).ToArray();
        var differences = offsets
            .Where(offset => !string.Equals(nativeRoutes.GetValueOrDefault(offset, "<missing>"), freshRoutes.GetValueOrDefault(offset, "<missing>"), StringComparison.Ordinal))
            .Select(offset => $"0x{offset:X}:{nativeRoutes.GetValueOrDefault(offset, "<missing>")}/{freshRoutes.GetValueOrDefault(offset, "<missing>")}")
            .ToArray();
        if (differences.Length > 0)
            Console.WriteLine($"rawPointerRoute|cloth={clothName}|label={label}|{string.Join(';', differences)}");
    }

    private static IReadOnlyDictionary<int, string> GetPointerRoutes(NativeBphclDocument document, NativeBphclItem owner, int objectSize)
    {
        var routes = new Dictionary<int, string>();
        foreach (var absoluteOffset in document.RelocationOffsets.Where(offset => offset >= owner.DataOffset && offset < owner.DataOffset + objectSize))
            routes[checked((int)(absoluteOffset - owner.DataOffset))] = DescribePointerTarget(document, absoluteOffset);
        return routes;
    }

    private static string DescribePointerTarget(NativeBphclDocument document, uint pointerOffset)
    {
        if (!document.TryGetReferencedItem(pointerOffset, out var targetItemIndex))
            return "invalid";

        var item = document.GetItem(targetItemIndex);
        var type = document.GetTypeName(item.TypeIndex) ?? $"#{item.TypeIndex}";
        var role = DescribeItemRole(document, targetItemIndex);
        var definition = document.TypeDefinitions.FirstOrDefault(candidate => candidate.TypeIndex == item.TypeIndex);
        var payloadHash = GetMaskedItemPayloadHash(document, item, definition?.Size);
        var nestedTypes = document.RelocationOffsets
            .Where(offset => offset >= item.DataOffset && offset < item.DataOffset + GetItemByteLength(item, definition?.Size))
            .Select(offset => document.TryGetReferencedItem(offset, out var nestedIndex)
                ? DescribeItemRole(document, nestedIndex) ?? document.GetTypeName(document.GetItem(nestedIndex).TypeIndex) ?? $"#{document.GetItem(nestedIndex).TypeIndex}"
                : "invalid")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return $"{type}[{item.Count}]{role}#{payloadHash}:refs({string.Join(',', nestedTypes)})";
    }


    private static string? DescribeItemRole(NativeBphclDocument document, int itemIndex)
    {
        var cloth = document.Cloths.FirstOrDefault(value => value.ItemIndex == itemIndex);
        if (cloth is not null) return $"<cloth:{cloth.Name}>";
        var skeleton = document.Skeletons.FirstOrDefault(value => value.ItemIndex == itemIndex);
        if (skeleton is not null) return $"<skeleton:{skeleton.Name}>";
        var collider = document.Colliders.FirstOrDefault(value => value.ItemIndex == itemIndex);
        return collider is null ? null : $"<collider:{collider.Name}>";
    }    private static string GetMaskedItemPayloadHash(NativeBphclDocument document, NativeBphclItem item, uint? elementSize)
    {
        if (document.DataSection is null)
            return "nodata";
        var byteLength = GetItemByteLength(item, elementSize);
        if (byteLength == 0 || item.DataOffset > document.DataSection.PayloadSize - byteLength)
            return "empty";
        var payload = document.Bytes.AsSpan(document.DataSection.PayloadOffset + checked((int)item.DataOffset), checked((int)byteLength)).ToArray();
        foreach (var relocation in document.RelocationOffsets.Where(offset => offset >= item.DataOffset && offset < item.DataOffset + byteLength))
        {
            var relative = checked((int)(relocation - item.DataOffset));
            Array.Clear(payload, relative, Math.Min(sizeof(uint), payload.Length - relative));
        }
        return HashBytes(payload)[..8];
    }

    private static uint GetItemByteLength(NativeBphclItem item, uint? elementSize)
    {
        if (elementSize is not > 0 || item.Count == 0)
            return 0;
        try { return checked(elementSize.Value * item.Count); }
        catch (OverflowException) { return 0; }
    }
    private static string DescribeMember(NativeBphclTypeDefinition? definition, int offset)
    {
        var member = definition?.Members
            .Where(candidate => candidate.Offset <= offset)
            .OrderByDescending(candidate => candidate.Offset)
            .FirstOrDefault();
        return member is null ? $"padding/unknown+0x{offset:X}" : $"{member.Name}+0x{offset - member.Offset:X}";
    }
    private static string DescribeBone(NativeBphclBone bone) =>
        $"{bone.Index}:{bone.Name}:{bone.ParentIndex}:{bone.LockTranslation}:{VectorText(bone.Translation)}:{VectorText(bone.Rotation)}:{VectorText(bone.Scale)}";

    private static string DescribeMeshDeformer(NativeBphclSimpleMeshBoneDeform deformer) =>
        $"{deformer.OperatorIndex}:{deformer.InputBufferIndex}:{deformer.OutputTransformSetIndex}:{deformer.BoneAxis}:" +
        $"{string.Join(',', deformer.TriangleBonePairs.Select(pair => $"{pair.BoneOffset}/{pair.TriangleOffset}"))}:" +
        $"{HashMatrices(deformer.LocalBoneTransforms)}";

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
    private static string HashMatrices(IReadOnlyList<Matrix4x4> matrices) =>
        HashBytes(matrices.SelectMany(matrix => new[]
        {
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44
        }).SelectMany(BitConverter.GetBytes));

    private static string DescribePackedExponents(IReadOnlyList<NativeBphclPackedPositionBlock> blocks) =>
        string.Join('/', blocks.Select(block =>
            string.Join(',', block.Positions.Where((_, index) => index % 4 == 3).Distinct())));
    private static string HashPackedPositions(IReadOnlyList<NativeBphclPackedPositionBlock> blocks) =>
        HashBytes(blocks.SelectMany(block => block.Positions).SelectMany(BitConverter.GetBytes));

    private static string HashDeformer(NativeBphclObjectSpaceDeformer deformer) =>
        HashBytes(deformer.EightBlendEntries
            .Concat(deformer.SevenBlendEntries)
            .Concat(deformer.SixBlendEntries)
            .Concat(deformer.FiveBlendEntries)
            .Concat(deformer.FourBlendEntries)
            .Concat(deformer.ThreeBlendEntries)
            .Concat(deformer.TwoBlendEntries)
            .Concat(deformer.OneBlendEntries)
            .SelectMany(entry => entry.Bytes));

    private static string HashUnpackedPositions(IReadOnlyList<NativeBphclPositionBlock> blocks) =>
        HashBytes(blocks.SelectMany(block => block.Positions).SelectMany(position => new[]
        {
            position.X, position.Y, position.Z, position.W
        }).SelectMany(BitConverter.GetBytes));

    private static string HashBoneDeformer(NativeBphclBoneSpaceDeformer deformer) =>
        HashBytes(deformer.FourBlendEntries
            .Concat(deformer.ThreeBlendEntries)
            .Concat(deformer.TwoBlendEntries)
            .Concat(deformer.OneBlendEntries)
            .SelectMany(entry => entry.Bytes));

    private static string HashUShorts(IReadOnlyList<ushort> values) =>
        HashBytes(values.SelectMany(BitConverter.GetBytes));

    private static string HashBytes(IEnumerable<byte> values) =>
        Convert.ToHexString(SHA256.HashData(values.ToArray()))[..16];
    private static string DescribeOperatorRoute(NativeBphclOperatorLayout value, NativeBphclSkeleton? skeleton) =>
        $"{value.ClassName};name={value.Name};in={value.InputBufferIndex};out={value.OutputBufferIndex};" +
        $"set={value.TransformSetIndex};sim={value.SimulationClothIndex};ref={value.ReferenceBufferIndex};" +
        $"verts={value.VertexCount},{value.StartVertexIn},{value.StartVertexOut};" +
        $"pairs={string.Join(',', value.VertexParticlePairs.Select(pair => $"{pair.VertexIndex}:{pair.ParticleIndex}"))};" +
        $"configs={string.Join('/', value.SimulateConfigs.Select(config =>
            $"[{string.Join(',', config.ConstraintExecution)}]|[{string.Join(',', config.InstanceCollidablesUsed)}]|" +
            $"{config.SubSteps},{config.NumberOfSolveIterations},{config.UseAllInstanceCollidables},{config.AdaptConstraintStiffness}"))};" +
        $"access={DescribeTransformAccesses(value.TransformSetAccesses, skeleton)}";

    private static string DescribeStateRoute(NativeBphclClothState value, NativeBphclSkeleton? skeleton) =>
        $"name={value.Name};ops={string.Join(',', value.OperatorItemIndices)};" +
        $"buffers={string.Join('/', value.BufferAccesses.Select(access =>
            $"{access.BufferIndex}:{access.ComponentFlags0},{access.ComponentFlags1},{access.ComponentFlags2},{access.ComponentFlags3}," +
            $"{access.TrianglesRead},{access.ShadowBufferIndex}"))};" +
        $"sets={DescribeTransformAccesses(value.TransformSetAccesses, skeleton)};" +
        $"sims={string.Join(',', value.SimClothItemIndices)}";

    private static string DescribeTransformAccesses(
        IReadOnlyList<NativeBphclClothStateTransformSetAccess> accesses,
        NativeBphclSkeleton? skeleton) =>
        string.Join('/', accesses.Select(access =>
            $"{access.TransformSetIndex}:{access.ComponentFlags0},{access.ComponentFlags1}:" +
            string.Join('+', access.TransformTrackers.Select(tracker => DescribeTracker(tracker, skeleton)))));

    private static string DescribeTracker(NativeBphclTransformTracker value, NativeBphclSkeleton? skeleton) =>
        $"r={DescribeBitField(value.Read, skeleton)},b={DescribeBitField(value.ReadBeforeWrite, skeleton)},w={DescribeBitField(value.Written, skeleton)}";

    private static string DescribeBitField(NativeBphclBitField value, NativeBphclSkeleton? skeleton)
    {
        var setBits = new List<string>();
        for (var wordIndex = 0; wordIndex < value.Words.Count; wordIndex++)
        {
            var word = value.Words[wordIndex];
            for (var bit = 0; bit < 32; bit++)
            {
                if ((word & (1u << bit)) == 0)
                    continue;
                var index = wordIndex * 32 + bit;
                var name = skeleton?.Bones.ElementAtOrDefault(index)?.Name;
                setBits.Add(name is null ? index.ToString() : $"{index}={name}");
            }
        }
        return $"{value.NumBits}[{string.Join(',', value.Words)}]{{{string.Join(',', setBits)}}}";
    }

    private static void WriteStateDependencySummary(
        string clothName,
        IReadOnlyList<NativeBphclClothState> nativeStates,
        IReadOnlyList<NativeBphclClothState> freshStates)
    {
        var count = Math.Max(nativeStates.Count, freshStates.Count);
        for (var index = 0; index < count; index++)
        {
            if (index >= nativeStates.Count || index >= freshStates.Count)
            {
                Console.WriteLine($"stateGraph|cloth={clothName}|state={index}|missing={(index >= nativeStates.Count ? "native" : "fresh")}");
                continue;
            }

            var nativeGraph = DescribeStateGraph(nativeStates[index].DependencyGraph);
            var freshGraph = DescribeStateGraph(freshStates[index].DependencyGraph);
            Console.WriteLine(
                $"stateGraph|cloth={clothName}|state={index}|same={string.Equals(nativeGraph, freshGraph, StringComparison.Ordinal)}|" +
                $"native={nativeGraph}|fresh={freshGraph}");
        }
    }

    private static string DescribeStateGraph(NativeBphclStateDependencyGraph? graph)
    {
        if (graph is null)
            return "<none>";

        static string List(IReadOnlyList<int> values) => string.Join(',', values);
        static string Nested(IReadOnlyList<IReadOnlyList<int>> values) =>
            string.Join('/', values.Select(List));

        return $"branches={string.Join('/', graph.Branches.Select(branch => $"{branch.BranchId}:{List(branch.OperatorIndices)}"))};" +
            $"roots={List(graph.RootBranchIds)};children={Nested(graph.Children)};parents={Nested(graph.Parents)};cycles={graph.HasCycles}";
    }
    private static void WriteSimulationSummary(
        NativeBphclDocument nativeDocument,
        string clothName,
        NativeBphclSimCloth native,
        NativeBphclDocument freshDocument,
        NativeBphclSimCloth fresh)
    {
        var nativeConstraintSignature = string.Join(',', native.ConstraintSets.Select(set => $"{set.ClassName}:{set.Links.Count}/{set.LocalConstraints.Count}/{set.LocalStiffnessConstraints.Count}/{set.TransitionParticles.Count}"));
        var freshConstraintSignature = string.Join(',', fresh.ConstraintSets.Select(set => $"{set.ClassName}:{set.Links.Count}/{set.LocalConstraints.Count}/{set.LocalStiffnessConstraints.Count}/{set.TransitionParticles.Count}"));

        Console.WriteLine(
            $"sim|cloth={clothName}|index={native.Index}|particles={native.Particles.Count}/{fresh.Particles.Count}|" +
            $"triangles={native.TriangleCount}/{fresh.TriangleCount}|constraints={nativeConstraintSignature}/{freshConstraintSignature}|" +
            $"masks={string.Join(',', native.StaticCollisionMasks)}/{string.Join(',', fresh.StaticCollisionMasks)}|" +
            $"gravityDelta={MaxDelta(native.SimulationInfo.Gravity, fresh.SimulationInfo.Gravity):G6}|" +
            $"dampingDelta={MathF.Abs(native.SimulationInfo.GlobalDampingPerSecond - fresh.SimulationInfo.GlobalDampingPerSecond):G6}|" +
            $"massDelta={MathF.Abs(native.Runtime.TotalMass - fresh.Runtime.TotalMass):G6}|" +
            $"radiusDelta={MathF.Abs(native.Runtime.MaxParticleRadius - fresh.Runtime.MaxParticleRadius):G6}");

        var nativeColliders = nativeDocument.Colliders.ToDictionary(collider => collider.ItemIndex);
        var freshColliders = freshDocument.Colliders.ToDictionary(collider => collider.ItemIndex);
        var slotCount = Math.Max(native.CollidableItemIndices.Count, fresh.CollidableItemIndices.Count);
        for (var slot = 0; slot < slotCount; slot++)
        {
            var nativeName = DescribeSlot(native, nativeColliders, slot, out var nativeTransform, out var nativeOffset);
            var freshName = DescribeSlot(fresh, freshColliders, slot, out var freshTransform, out var freshOffset);
            var offsetDelta = nativeOffset is { } left && freshOffset is { } right ? MaxDelta(left, right) : float.PositiveInfinity;

            Console.WriteLine(
                $"colliderSlot|cloth={clothName}|sim={native.Index}|slot={slot}|" +
                $"native={nativeName}|fresh={freshName}|transform={nativeTransform}/{freshTransform}|offsetDelta={offsetDelta:G6}");
        }
    }

    private static string DescribeSlot(
        NativeBphclSimCloth sim,
        IReadOnlyDictionary<int, NativeBphclCollider> colliders,
        int slot,
        out uint? transformIndex,
        out Matrix4x4? offset)
    {
        transformIndex = slot < sim.CollidableTransformMap.TransformIndices.Count
            ? sim.CollidableTransformMap.TransformIndices[slot]
            : null;
        offset = slot < sim.CollidableTransformMap.Offsets.Count
            ? sim.CollidableTransformMap.Offsets[slot]
            : null;

        if (slot >= sim.CollidableItemIndices.Count)
            return "<missing>";

        var itemIndex = sim.CollidableItemIndices[slot];
        return colliders.TryGetValue(itemIndex, out var collider)
            ? $"{collider.Name}#{itemIndex}"
            : $"item#{itemIndex}";
    }

    private static Dictionary<string, string> ReadAampProfiles(NativeBphclDocument document)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in ReadAampList(document, "cloth_mesh_list"))
        {
            var name = ReadParameter(item, "Name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            result[Key(name)] = string.Join(';', item.Parameters
                .OrderBy(parameter => parameter.Hash)
                .Select(parameter => $"{KnownParameterName(parameter.Hash)}={DescribeAampValue(parameter)}"));
        }

        return result;
    }

    private static IReadOnlyList<string> ReadAampColliderNames(NativeBphclDocument document) =>
        ReadAampList(document, "collidable_list")
            .Select(item => ReadParameter(item, "Name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

    private static IReadOnlyList<AampReadObject> ReadAampList(NativeBphclDocument document, string listName)
    {
        if (document.Header.ParameterSize == 0)
            return Array.Empty<AampReadObject>();

        var offset = checked((int)document.Header.ParameterOffset);
        var size = checked((int)document.Header.ParameterSize);
        if (offset < 0 || size < 16 || offset > document.Bytes.Length - size)
            return Array.Empty<AampReadObject>();

        var archiveSize = BitConverter.ToInt32(document.Bytes, offset + 0x0c);
        if (archiveSize <= 0 || archiveSize > size)
            return Array.Empty<AampReadObject>();

        var archive = BotwAampReader.Read(document.Bytes.AsSpan(offset, archiveSize).ToArray());
        return archive.Root.FindChild(listName) is { } list ? list.Objects : Array.Empty<AampReadObject>();
    }

    private static string ReadParameter(AampReadObject item, string name)
    {
        var parameter = item.Find(name);
        return parameter is null ? string.Empty : DescribeAampValue(parameter);
    }

    private static string DescribeAampValue(AampReadParameter parameter) => parameter.Type switch
    {
        0 => parameter.AsBoolean() ? "true" : "false",
        1 => parameter.AsSingle().ToString("G9", System.Globalization.CultureInfo.InvariantCulture),
        2 => parameter.AsInt32().ToString(System.Globalization.CultureInfo.InvariantCulture),
        7 or 8 or 15 or 20 => parameter.AsString(),
        _ => $"0x{Convert.ToHexString(parameter.Bytes)}"
    };

    private static string KnownParameterName(uint hash)
    {
        var known = new[]
        {
            "Name", "BaseBone", "BoneCorrection", "BoneCorrectionAxisOrder",
            "Twist", "TwistSwingAxis", "TwistAngleCoef", "TwistMaxAngle",
            "KeepBoneLength", "Preset", "ForReplace"
        };
        return known.FirstOrDefault(name => BotwAampWriter.Crc32(name) == hash) ?? $"0x{hash:X8}";
    }

    private static string VectorText(Vector4 value) =>
        $"{value.X:G6},{value.Y:G6},{value.Z:G6},{value.W:G6}";
    private static string Key(string name) => name.StartsWith("Link:", StringComparison.Ordinal) ? name[5..] : name;

    private static float SpatialDelta(Vector4 left, Vector4 right) =>
        Math.Max(MathF.Abs(left.X - right.X), Math.Max(MathF.Abs(left.Y - right.Y), MathF.Abs(left.Z - right.Z)));
    private static float MaxDelta(Vector4 left, Vector4 right) =>
        Math.Max(Math.Max(MathF.Abs(left.X - right.X), MathF.Abs(left.Y - right.Y)),
            Math.Max(MathF.Abs(left.Z - right.Z), MathF.Abs(left.W - right.W)));

    private static float MaxDelta(Matrix4x4 left, Matrix4x4 right)
    {
        var maximum = 0.0f;
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 4; column++)
            maximum = Math.Max(maximum, MathF.Abs(left[row, column] - right[row, column]));
        return maximum;
    }
}
