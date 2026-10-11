using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace HKCLTool;

// Native BPHCL writer for edits that stay within existing allocations. Array
// growth and relocation rebuilding will live in a later allocator layer.
public static class NativeBphclWriter
{
    // Some TotK actor files namespace their shared model bones as Link:Bone,
    // while a donor cloth uses plain Bone names. Retarget only names for which
    // the target already exposes an exact Link: counterpart; authored cloth
    // bones (for example Necklace_1) remain untouched.
    internal static NativeBphclDocument RetargetSourceSkeletonNamespaces(
        NativeBphclDocument target,
        NativeBphclDocument source,
        int sourceSkeletonIndex)
    {
        if ((uint)sourceSkeletonIndex >= (uint)source.Skeletons.Count)
            throw new ArgumentOutOfRangeException(nameof(sourceSkeletonIndex));

        var targetBoneNames = target.Skeletons
            .SelectMany(skeleton => skeleton.Bones)
            .Select(bone => bone.Name)
            .ToHashSet(StringComparer.Ordinal);
        var sourceSkeleton = source.Skeletons[sourceSkeletonIndex];
        var replacements = sourceSkeleton.Bones
            .Where(bone => !bone.Name.StartsWith("Link:", StringComparison.Ordinal) &&
                           targetBoneNames.Contains("Link:" + bone.Name))
            .ToDictionary(bone => bone.Index, bone => "Link:" + bone.Name);

        return replacements.Count == 0
            ? source
            : ApplyBoneNameEdits(source, sourceSkeletonIndex, replacements);
    }

    // Exercises the native section writer without changing semantic data. It
    // is the first round-trip gate before DATA/ITEM growth is allowed.
    public static void SaveRebuiltCopy(NativeBphclDocument document, string outputPath)
    {
        var data = document.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var repairedAamp = NativeAampClothMerger.RepairSuffixedClothBaseBones(document);
        var bytes = NativeBphclTagFileBuilder.Rebuild(
            document,
            document.Bytes.AsSpan(data.PayloadOffset, data.PayloadSize),
            document.Items,
            document.InternalPatches,
            replacementAamp: repairedAamp);
        _ = NativeBphclDocument.Parse(bytes);
        AtomicFile.WriteAllBytes(outputPath, bytes);
    }

    // Diagnostic gate for cross-file imports. This writes a union of the
    // target/source Havok TYPE metadata without changing any target object,
    // pointer, cloth-array, or AAMP data.
    public static void SaveWithMergedTypeMetadata(
        NativeBphclDocument target,
        NativeBphclDocument source,
        string outputPath)
    {
        var data = target.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var typeTable = NativeBphclTypeTable.Create(target, source);
        var items = target.Items
            .Select(item => RemapItemType(item, typeTable.TargetToMerged))
            .ToArray();
        var patches = target.InternalPatches
            .Select(patch => new NativeBphclPatch(
                RemapTypeIndex(patch.TypeIndex, typeTable.TargetToMerged),
                patch.Offsets))
            .ToArray();
        var bytes = NativeBphclTagFileBuilder.Rebuild(
            target,
            target.Bytes.AsSpan(data.PayloadOffset, data.PayloadSize),
            items,
            patches,
            typeTable.ReplacementSection);
        _ = NativeBphclDocument.Parse(bytes);
        File.WriteAllBytes(outputPath, bytes);
    }

    // Imports one complete cloth package through the native DATA/ITEM/PTCH
    // path. The source and target must expose compatible Havok type names.
    public static void SaveMergedCloth(
        NativeBphclDocument target,
        NativeBphclDocument source,
        int sourceClothIndex,
        string outputPath)
    {
        // Preserve the target's existing ITEM indices. The importer appends
        // the donor graph and remaps its pointers. Full live-graph relocation
        // remains a separate, not-yet game-validated pass.
        var merged = NativeBphclMerge.MergeCompleteCloth(target, source, sourceClothIndex);
        _ = NativeBphclDocument.Parse(merged);
        File.WriteAllBytes(outputPath, merged);
    }

    /// <summary>
    /// Adds a fully independent copy of a cloth package. Unlike ordinary
    /// imports, collider reuse is disabled so later edits cannot move the
    /// source cloth's collision behavior as a side effect.
    /// </summary>
    public static void SaveDuplicatedCloth(
        NativeBphclDocument target,
        NativeBphclDocument source,
        int sourceClothIndex,
        string outputPath)
    {
        var duplicated = NativeBphclMerge.DuplicateCompleteCloth(target, source, sourceClothIndex);
        _ = NativeBphclDocument.Parse(duplicated);
        File.WriteAllBytes(outputPath, duplicated);
    }

    // Replaces a cloth package in its existing container slot. This is useful
    // for controlled tests and later for deliberate cloth replacement: it
    // keeps cloth/skeleton/AAMP ordering stable instead of appending a second
    // entry with the same logical simulation-mesh name.
    public static void SaveReplacedCloth(
        NativeBphclDocument target,
        NativeBphclDocument source,
        int targetClothIndex,
        int sourceClothIndex,
        string outputPath)
    {
        var replaced = NativeBphclMerge.ReplaceCompleteCloth(
            target,
            source,
            targetClothIndex,
            sourceClothIndex);
        _ = NativeBphclDocument.Parse(replaced);
        File.WriteAllBytes(outputPath, replaced);
    }

    public static void SaveWithoutCloth(NativeBphclDocument target, int clothIndex, string outputPath)
    {
        File.WriteAllBytes(outputPath, DeleteClothAndCompact(target, clothIndex));
    }

    public static byte[] DeleteClothAndCompact(NativeBphclDocument target, int clothIndex)
    {
        if ((uint)clothIndex >= (uint)target.Cloths.Count)
            throw new ArgumentOutOfRangeException(nameof(clothIndex));

        var expectedClothCount = target.Cloths.Count - 1;
        var expectedSkeletonCount = target.Skeletons.Count -
            (clothIndex < target.Skeletons.Count ? 1 : 0);
        var deletedName = target.Cloths[clothIndex].Name;

        var logicallyDeleted = NativeBphclMerge.DeleteCloth(target, clothIndex);
        var compacted = NativeBphclCompactor.Compact(NativeBphclDocument.Parse(logicallyDeleted));
        var result = NativeBphclDocument.Parse(compacted);

        if (result.Cloths.Count != expectedClothCount)
            throw new InvalidDataException(
                $"BPHCL deletion produced {result.Cloths.Count} cloths; expected {expectedClothCount}.");
        if (result.Skeletons.Count != expectedSkeletonCount)
            throw new InvalidDataException(
                $"BPHCL deletion produced {result.Skeletons.Count} skeletons; expected {expectedSkeletonCount}.");
        if (result.Aamp.IsReadable && result.Aamp.ClothEntries.Any(entry =>
                string.Equals(entry.Name, deletedName, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"BPHCL deletion left the removed cloth '{deletedName}' registered in AAMP metadata.");
        }

        var referencedColliders = result.Cloths
            .SelectMany(cloth => cloth.SimCloths)
            .SelectMany(simulation => simulation.CollidableItemIndices)
            .ToHashSet();
        if (result.Colliders.Any(collider => !referencedColliders.Contains(collider.ItemIndex)))
            throw new InvalidDataException("BPHCL deletion left unreferenced colliders in the active collider array.");

        return compacted;
    }
    // Optional cleanup pass used only after a delete/merge has been
    // validated. It trims live collider references and AAMP metadata without
    // relocating arbitrary DATA allocations.
    public static void SaveWithUnreferencedCollidersPruned(
        NativeBphclDocument target,
        string outputPath)
    {
        var pruned = NativeBphclMerge.PruneUnreferencedColliders(target);
        _ = NativeBphclDocument.Parse(pruned);
        File.WriteAllBytes(outputPath, pruned);
    }

    // Retargeting experiment: keep the skeleton's structure and transforms,
    // but replace the Link: namespace used by some BPHCL skeletons with the
    // underlying model-bone name. Existing pointer patches remain valid.
    public static void SaveWithoutLinkBonePrefixes(NativeBphclDocument document, string outputPath)
    {
        var dataSection = document.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var data = document.Bytes
            .AsSpan(dataSection.PayloadOffset, dataSection.PayloadSize)
            .ToArray()
            .ToList();
        var items = document.Items.ToList();

        foreach (var skeleton in document.Skeletons)
        {
            if (!document.TryGetReferencedItem(skeleton.DataOffset + 48, out var boneArrayItemIndex))
                throw new InvalidDataException($"BPHCL skeleton '{skeleton.Name}' has no readable bone array.");
            var boneArrayItem = document.GetItem(boneArrayItemIndex);

            for (var index = 0; index < skeleton.Bones.Count && index < boneArrayItem.Count; index++)
            {
                var bone = skeleton.Bones[index];
                if (!bone.Name.StartsWith("Link:", StringComparison.Ordinal))
                    continue;

                var boneOffset = checked(boneArrayItem.DataOffset + (uint)(index * 16));
                if (!document.TryGetReferencedItem(boneOffset, out var oldNameItemIndex))
                    throw new InvalidDataException($"BPHCL bone '{bone.Name}' has no readable name pointer.");

                var oldNameItem = document.GetItem(oldNameItemIndex);
                var newNameBytes = Encoding.UTF8.GetBytes(bone.Name[5..]);
                Align(data, 8);
                var newNameOffset = checked((uint)data.Count);
                data.AddRange(newNameBytes);
                data.Add(0);

                var newNameItemIndex = items.Count;
                items.Add(oldNameItem with
                {
                    DataOffset = newNameOffset,
                    Count = checked((uint)(newNameBytes.Length + 1))
                });
                BinaryPrimitives.WriteUInt32LittleEndian(
                    CollectionsMarshal.AsSpan(data).Slice(checked((int)boneOffset), 4),
                    checked((uint)newNameItemIndex));
            }
        }

        var rebuilt = NativeBphclTagFileBuilder.Rebuild(
            document,
            CollectionsMarshal.AsSpan(data),
            items,
            document.InternalPatches);
        File.WriteAllBytes(outputPath, rebuilt);
    }

    /// <summary>
    /// Repoints existing BPHCL bone-name ITEMs to fresh UTF-8 strings. The
    /// bone array itself is untouched, so changing a name is safe even when
    /// the replacement is longer than the original.
    /// </summary>
    public static NativeBphclDocument ApplyBoneNameEdits(
        NativeBphclDocument document,
        int skeletonIndex,
        IReadOnlyDictionary<int, string> replacements)
    {
        if (replacements.Count == 0)
            return document;

        var skeleton = document.Skeletons.ElementAtOrDefault(skeletonIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(skeletonIndex));
        var dataSection = document.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var data = document.Bytes
            .AsSpan(dataSection.PayloadOffset, dataSection.PayloadSize)
            .ToArray()
            .ToList();
        var items = document.Items.ToList();
        var replacementAamp = NativeAampClothMerger.SynchronizeClothBaseBoneEdits(
            document,
            skeletonIndex,
            replacements);

        if (!document.TryGetReferencedItem(skeleton.DataOffset + 48, out var boneArrayItemIndex))
            throw new InvalidDataException($"BPHCL skeleton '{skeleton.Name}' has no readable bone array.");
        var boneArrayItem = document.GetItem(boneArrayItemIndex);

        foreach (var bone in skeleton.Bones)
        {
            if (!replacements.TryGetValue(bone.Index, out var replacementName))
                continue;
            if (bone.Index >= boneArrayItem.Count)
                throw new InvalidDataException($"BPHCL bone '{bone.Name}' lies outside its bone array.");

            var boneOffset = checked(boneArrayItem.DataOffset + (uint)(bone.Index * 16));
            if (!document.TryGetReferencedItem(boneOffset, out var oldNameItemIndex))
                throw new InvalidDataException($"BPHCL bone '{bone.Name}' has no readable name pointer.");

            var oldNameItem = document.GetItem(oldNameItemIndex);
            var newNameBytes = Encoding.UTF8.GetBytes(replacementName);
            Align(data, 8);
            var newNameOffset = checked((uint)data.Count);
            data.AddRange(newNameBytes);
            data.Add(0);

            var newNameItemIndex = items.Count;
            items.Add(oldNameItem with
            {
                DataOffset = newNameOffset,
                Count = checked((uint)(newNameBytes.Length + 1))
            });
            BinaryPrimitives.WriteUInt32LittleEndian(
                CollectionsMarshal.AsSpan(data).Slice(checked((int)boneOffset), 4),
                checked((uint)newNameItemIndex));
        }

        var rebuilt = NativeBphclTagFileBuilder.Rebuild(
            document,
            CollectionsMarshal.AsSpan(data),
            items,
            document.InternalPatches,
            replacementAamp: replacementAamp);
        return NativeBphclDocument.Parse(rebuilt);
    }

    /// <summary>
    /// Repoints collider-name ITEMs without altering collider array layout.
    /// Collider names are used by the editor's bone-binding convention, so
    /// whole-cloth mirroring must update them alongside skeleton bone names.
    /// </summary>
    public static NativeBphclDocument ApplyColliderNameEdits(
        NativeBphclDocument document,
        IReadOnlyDictionary<int, string> replacements)
    {
        if (replacements.Count == 0)
            return document;

        var dataSection = document.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var data = document.Bytes
            .AsSpan(dataSection.PayloadOffset, dataSection.PayloadSize)
            .ToArray()
            .ToList();
        var items = document.Items.ToList();
        var replacementAamp = NativeAampClothMerger.SynchronizeColliderNameEdits(document, replacements);

        foreach (var collider in document.Colliders)
        {
            if (!replacements.TryGetValue(collider.Index, out var replacementName))
                continue;
            if (!document.TryGetReferencedItem(collider.DataOffset + 144, out var oldNameItemIndex))
                throw new InvalidDataException($"BPHCL collider '{collider.Name}' has no readable name pointer.");

            var oldNameItem = document.GetItem(oldNameItemIndex);
            var newNameBytes = Encoding.UTF8.GetBytes(replacementName);
            Align(data, 8);
            var newNameOffset = checked((uint)data.Count);
            data.AddRange(newNameBytes);
            data.Add(0);

            var newNameItemIndex = items.Count;
            items.Add(oldNameItem with
            {
                DataOffset = newNameOffset,
                Count = checked((uint)(newNameBytes.Length + 1))
            });
            BinaryPrimitives.WriteUInt32LittleEndian(
                CollectionsMarshal.AsSpan(data).Slice(checked((int)collider.DataOffset + 144), 4),
                checked((uint)newNameItemIndex));
        }

        var rebuilt = NativeBphclTagFileBuilder.Rebuild(
            document,
            CollectionsMarshal.AsSpan(data),
            items,
            document.InternalPatches,
            replacementAamp: replacementAamp);
        return NativeBphclDocument.Parse(rebuilt);
    }

    // Cloth names are pointer-backed UTF-8 strings. Add a fresh string ITEM
    // and repoint the cloth so renames are not limited by the old name length.
    public static void SaveRenamedCloth(
        NativeBphclDocument document,
        int clothIndex,
        string name,
        string outputPath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A cloth name cannot be empty.", nameof(name));
        if (name.IndexOf('\0') >= 0)
            throw new ArgumentException("A cloth name cannot contain a null character.", nameof(name));

        var cloth = document.Cloths.ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var dataSection = document.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var replacementAamp = NativeAampClothMerger.RenameClothEntry(document, cloth.Name, name);
        var oldSkeletonName = "cloth_skeleton_" +
            (cloth.Name.StartsWith("Link:", StringComparison.Ordinal) ? cloth.Name[5..] : cloth.Name);
        var newSkeletonName = "cloth_skeleton_" +
            (name.StartsWith("Link:", StringComparison.Ordinal) ? name[5..] : name);

        // hclClothData begins with hkReferencedObject (24 bytes), followed by
        // its name pointer. The existing pointer has an INDX relocation entry.
        var namePointerOffset = checked(cloth.DataOffset + 24u);
        if (!document.TryGetReferencedItem(namePointerOffset, out var oldStringItemIndex))
            throw new InvalidDataException("BPHCL cloth name pointer could not be resolved.");

        var oldStringItem = document.GetItem(oldStringItemIndex);
        var data = document.Bytes
            .AsSpan(dataSection.PayloadOffset, dataSection.PayloadSize)
            .ToArray()
            .ToList();
        Align(data, 8);
        var newStringOffset = checked((uint)data.Count);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        data.AddRange(nameBytes);
        data.Add(0);

        var items = document.Items.ToList();
        var newStringItemIndex = items.Count;
        items.Add(oldStringItem with
        {
            DataOffset = newStringOffset,
            Count = checked((uint)(nameBytes.Length + 1))
        });

        BinaryPrimitives.WriteUInt32LittleEndian(
            CollectionsMarshal.AsSpan(data).Slice(checked((int)namePointerOffset), 4),
            checked((uint)newStringItemIndex));

        // The three cloth buffers share the same logical simulation-mesh key
        // as the hclClothData name. A duplicate that keeps these on the source
        // name collides with the source's scratch/current/previous buffers at
        // runtime, even though its skeleton and colliders are independent.
        // Repoint all matching buffer fields to one fresh string ITEM so the
        // buffer trio remains internally consistent with the renamed cloth.
        var namedBuffers = cloth.BufferDefinitions
            .Where(buffer => !string.IsNullOrWhiteSpace(buffer.MeshName))
            .ToArray();
        var meshBuffers = namedBuffers
            .Select(buffer => buffer.MeshName)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .Count() == 1
            // Normal HCL cloths deliberately share one mesh key across the
            // scratch/current/previous buffers. This also repairs duplicates
            // written by older PhysicsTool builds, whose three buffers were
            // still all keyed to the source cloth after the cloth was renamed.
            ? namedBuffers
            : namedBuffers
                .Where(buffer => string.Equals(buffer.MeshName, cloth.Name, StringComparison.Ordinal))
                .ToArray();
        if (meshBuffers.Length > 0)
        {
            var firstBufferNamePointer = checked(meshBuffers[0].DataOffset + 24u);
            if (!document.TryGetReferencedItem(firstBufferNamePointer, out var oldMeshNameItemIndex))
                throw new InvalidDataException("BPHCL buffer mesh-name pointer could not be resolved.");

            var meshNameBytes = Encoding.UTF8.GetBytes(name);
            Align(data, 8);
            var meshNameOffset = checked((uint)data.Count);
            data.AddRange(meshNameBytes);
            data.Add(0);

            var oldMeshNameItem = document.GetItem(oldMeshNameItemIndex);
            var newMeshNameItemIndex = items.Count;
            items.Add(oldMeshNameItem with
            {
                DataOffset = meshNameOffset,
                Count = checked((uint)(meshNameBytes.Length + 1))
            });

            foreach (var buffer in meshBuffers)
            {
                var meshNamePointer = checked(buffer.DataOffset + 24u);
                if (!document.TryGetReferencedItem(meshNamePointer, out _))
                    throw new InvalidDataException("BPHCL buffer mesh-name pointer could not be resolved.");
                BinaryPrimitives.WriteUInt32LittleEndian(
                    CollectionsMarshal.AsSpan(data).Slice(checked((int)meshNamePointer), 4),
                    checked((uint)newMeshNameItemIndex));
            }
        }

        // Cloth and skeleton arrays are paired by index. Keep the duplicated
        // skeleton easy to identify as well, matching HKCL's conventional
        // cloth_skeleton_<cloth name> naming scheme.
        var skeleton = document.Skeletons.ElementAtOrDefault(clothIndex);
        if (skeleton != null &&
            document.TryGetReferencedItem(skeleton.DataOffset + 24u, out var oldSkeletonNameItemIndex))
        {
            var skeletonNameBytes = Encoding.UTF8.GetBytes(newSkeletonName);
            Align(data, 8);
            var skeletonNameOffset = checked((uint)data.Count);
            data.AddRange(skeletonNameBytes);
            data.Add(0);

            var oldSkeletonNameItem = document.GetItem(oldSkeletonNameItemIndex);
            var newSkeletonNameItemIndex = items.Count;
            items.Add(oldSkeletonNameItem with
            {
                DataOffset = skeletonNameOffset,
                Count = checked((uint)(skeletonNameBytes.Length + 1))
            });
            BinaryPrimitives.WriteUInt32LittleEndian(
                CollectionsMarshal.AsSpan(data).Slice(checked((int)skeleton.DataOffset + 24), 4),
                checked((uint)newSkeletonNameItemIndex));
        }

        // The transform set is the runtime bridge between hcl operators and
        // the paired hkaSkeleton. Leaving this name at the source cloth's
        // value makes a duplicate resolve the same left-side transform set.
        foreach (var transformSet in cloth.TransformSetDefinitions)
        {
            if (!string.Equals(transformSet.Name, oldSkeletonName, StringComparison.Ordinal) ||
                !document.TryGetReferencedItem(transformSet.DataOffset + 24u, out var oldTransformSetNameItemIndex))
            {
                continue;
            }

            var oldTransformSetNameItem = document.GetItem(oldTransformSetNameItemIndex);
            var transformSetNameBytes = Encoding.UTF8.GetBytes(newSkeletonName);
            Align(data, 8);
            var transformSetNameOffset = checked((uint)data.Count);
            data.AddRange(transformSetNameBytes);
            data.Add(0);

            var newTransformSetNameItemIndex = items.Count;
            items.Add(oldTransformSetNameItem with
            {
                DataOffset = transformSetNameOffset,
                Count = checked((uint)(transformSetNameBytes.Length + 1))
            });
            BinaryPrimitives.WriteUInt32LittleEndian(
                CollectionsMarshal.AsSpan(data).Slice(checked((int)transformSet.DataOffset + 24), 4),
                checked((uint)newTransformSetNameItemIndex));
        }

        var rebuilt = NativeBphclTagFileBuilder.Rebuild(
            document,
            CollectionsMarshal.AsSpan(data),
            items,
            document.InternalPatches,
            replacementAamp: replacementAamp);
        // Keep the original object layout. Compaction is a separate,
        // game-validation task and must never be a side effect of renaming.
        File.WriteAllBytes(outputPath, rebuilt);
    }

    public static void SaveCompactedCopy(NativeBphclDocument document, string outputPath)
    {
        File.WriteAllBytes(outputPath, NativeBphclCompactor.Compact(document));
    }

    public static void SaveReindexedDiagnosticCopy(NativeBphclDocument document, string outputPath)
    {
        File.WriteAllBytes(outputPath, NativeBphclCompactor.ReindexAllItemsForDiagnostic(document));
    }

    public static void SaveInactiveItemsReindexedDiagnosticCopy(NativeBphclDocument document, string outputPath)
    {
        File.WriteAllBytes(outputPath, NativeBphclCompactor.ReindexInactiveItemsForDiagnostic(document));
    }

    public static void SaveIndexStableCompactedCopy(NativeBphclDocument document, string outputPath)
    {
        File.WriteAllBytes(outputPath, NativeBphclCompactor.CompactPreservingItemIndices(document));
    }

    public static void SaveParticleEdit(
        NativeBphclDocument document,
        string outputPath,
        int clothIndex,
        int simulationIndex,
        int particleIndex,
        NativeBphclParticleEdit edit)
    {
        var updated = ApplyParticleEdits(
            document,
            clothIndex,
            simulationIndex,
            new Dictionary<int, NativeBphclParticleEdit> { [particleIndex] = edit });
        File.WriteAllBytes(outputPath, updated.Bytes);
    }

    /// <summary>
    /// Applies edits that fit inside existing particle records, then reparses
    /// the actual BPHCL bytes. This is native BPHCL editing: no HKCL objects,
    /// allocation growth, ITEM rewrite, or guessed relocation data is involved.
    /// </summary>
    public static NativeBphclDocument ApplyParticleEdits(
        NativeBphclDocument document,
        int clothIndex,
        int simulationIndex,
        IReadOnlyDictionary<int, NativeBphclParticleEdit> edits)
    {
        if (edits.Count == 0)
            return document;

        var simulation = document.Cloths
            .ElementAtOrDefault(clothIndex)?.SimCloths
            .ElementAtOrDefault(simulationIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(simulationIndex));
        var bytes = (byte[])document.Bytes.Clone();
        var dataStart = document.DataSection?.PayloadOffset
            ?? throw new InvalidDataException("BPHCL has no DATA section.");

        foreach (var (particleIndex, edit) in edits)
        {
            var particle = simulation.Particles.ElementAtOrDefault(particleIndex)
                ?? throw new ArgumentOutOfRangeException(nameof(particleIndex));
            if (particle.PhysicsDataOffset == 0)
                throw new InvalidDataException($"BPHCL particle {particleIndex} has no writable physics-data offset.");

            if (edit.Position is { } position)
            {
                if (particle.PositionDataOffset == 0)
                    throw new InvalidDataException($"BPHCL particle {particleIndex} has no writable pose offset.");
                WriteVector4(bytes, checked(dataStart + (int)particle.PositionDataOffset), position);
            }

            if (edit.Mass is { } mass)
            {
                if (!float.IsFinite(mass) || mass < 0)
                    throw new ArgumentOutOfRangeException(nameof(edit), "Particle mass must be a finite non-negative number.");
                WriteSingle(bytes, checked(dataStart + (int)particle.PhysicsDataOffset), mass);
                WriteSingle(bytes, checked(dataStart + (int)particle.PhysicsDataOffset + 4), mass == 0 ? 0 : 1f / mass);
            }
            if (edit.Radius is { } radius)
            {
                if (!float.IsFinite(radius) || radius < 0)
                    throw new ArgumentOutOfRangeException(nameof(edit), "Particle radius must be a finite non-negative number.");
                WriteSingle(bytes, checked(dataStart + (int)particle.PhysicsDataOffset + 8), radius);
            }
            if (edit.Friction is { } friction)
            {
                if (!float.IsFinite(friction))
                    throw new ArgumentOutOfRangeException(nameof(edit), "Particle friction must be finite.");
                WriteSingle(bytes, checked(dataStart + (int)particle.PhysicsDataOffset + 12), friction);
            }
        }

        return NativeBphclDocument.Parse(bytes);
    }

    /// <summary>Updates existing skeleton reference poses without changing
    /// names, array sizes, ITEM entries, or relocation tables.</summary>
    public static NativeBphclDocument ApplyBoneEdits(
        NativeBphclDocument document,
        int skeletonIndex,
        IReadOnlyDictionary<int, NativeBphclBoneEdit> edits)
    {
        if (edits.Count == 0)
            return document;

        var skeleton = document.Skeletons.ElementAtOrDefault(skeletonIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(skeletonIndex));
        var parents = skeleton.Bones.Select(b => b.ParentIndex).ToArray();
        foreach (var (index, edit) in edits)
        {
            if (index < 0 || index >= parents.Length) throw new ArgumentOutOfRangeException(nameof(edits));
            if (edit.ParentIndex is { } parent) parents[index] = parent;
            foreach (var vector in new[] { edit.Translation, edit.Rotation, edit.Scale })
                if (vector is { } v && (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || !float.IsFinite(v.W)))
                    throw new ArgumentException("Bone transforms must be finite.", nameof(edits));
        }
        for (var index = 0; index < parents.Length; index++)
        {
            var visited = new HashSet<int>();
            for (var current = index; current != -1; current = parents[current])
                if (current < 0 || current >= parents.Length || !visited.Add(current))
                    throw new ArgumentException("Bone parenting is invalid or cyclic.", nameof(edits));
        }
        var bytes = (byte[])document.Bytes.Clone();
        var dataStart = document.DataSection?.PayloadOffset
            ?? throw new InvalidDataException("BPHCL has no DATA section.");

        foreach (var (boneIndex, edit) in edits)
        {
            var bone = skeleton.Bones.ElementAtOrDefault(boneIndex)
                ?? throw new ArgumentOutOfRangeException(nameof(boneIndex));
            if (bone.PoseDataOffset == 0)
                throw new InvalidDataException($"BPHCL bone {boneIndex} has no writable reference-pose offset.");
            if (edit.Translation is { } translation)
                WriteVector4(bytes, checked(dataStart + (int)bone.PoseDataOffset), translation);
            if (edit.Rotation is { } rotation)
                WriteVector4(bytes, checked(dataStart + (int)bone.PoseDataOffset + 16), rotation);
            if (edit.Scale is { } scale)
                WriteVector4(bytes, checked(dataStart + (int)bone.PoseDataOffset + 32), scale);
            if (edit.ParentIndex is { } parent)
            {
                var layout = document.DescribeArrayLayout(skeleton.DataOffset + 32);
                if (boneIndex >= layout.Count) throw new InvalidDataException("BPHCL parent array is incomplete.");
                var item = document.GetItem(layout.StorageItemIndex);
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(checked(dataStart + (int)item.DataOffset + boneIndex * 2), 2), checked((short)parent));
            }
        }

        return NativeBphclDocument.Parse(bytes);
    }

    /// <summary>Updates an existing BPHCL collider's transform and supported
    /// shape values in place. This never changes array allocation or ITEM/PTCH
    /// metadata, so it is safe for viewport transform gestures.</summary>
    public static NativeBphclDocument ApplyColliderEdits(
        NativeBphclDocument document,
        IReadOnlyDictionary<int, NativeBphclColliderEdit> edits)
    {
        if (edits.Count == 0)
            return document;

        var bytes = (byte[])document.Bytes.Clone();
        var dataStart = document.DataSection?.PayloadOffset
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        foreach (var (colliderIndex, edit) in edits)
        {
            var collider = document.Colliders.ElementAtOrDefault(colliderIndex)
                ?? throw new ArgumentOutOfRangeException(nameof(colliderIndex));
            foreach (var value in new System.Numerics.Vector4?[] { edit.AxisX, edit.AxisY, edit.AxisZ,
                edit.Translation, edit.Start, edit.End, edit.PlaneEquation })
                if (value is { } v && (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || !float.IsFinite(v.W)))
                    throw new ArgumentException("Collider values must be finite.", nameof(edits));
            WriteVector4(bytes, checked(dataStart + (int)collider.DataOffset + 32), edit.AxisX);
            WriteVector4(bytes, checked(dataStart + (int)collider.DataOffset + 48), edit.AxisY);
            WriteVector4(bytes, checked(dataStart + (int)collider.DataOffset + 64), edit.AxisZ);
            WriteVector4(bytes, checked(dataStart + (int)collider.DataOffset + 80), edit.Translation);

            var shape = collider.Shape;
            if (shape.DataOffset == 0)
                continue;
            switch (shape.TypeName)
            {
                case "hclCapsuleShape":
                    if (edit.Start != null || edit.End != null)
                    {
                        var a = edit.Start ?? shape.Start;
                        var b = edit.End ?? shape.End;
                        var delta = new System.Numerics.Vector3(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
                        var lengthSquared = delta.LengthSquared();
                        if (!float.IsFinite(lengthSquared) || lengthSquared <= 0.000001f)
                            throw new ArgumentException("Capsule endpoints are too close together.", nameof(edits));
                        WriteVector4(bytes, checked(dataStart + (int)shape.DataOffset + 64), new System.Numerics.Vector4(System.Numerics.Vector3.Normalize(delta), 0));
                        WriteSingle(bytes, checked(dataStart + (int)shape.DataOffset + 84), 1f / lengthSquared);
                    }
                    if (edit.Start is { } capsuleStart)
                        WriteVector4(bytes, checked(dataStart + (int)shape.DataOffset + 32), capsuleStart);
                    if (edit.End is { } capsuleEnd)
                        WriteVector4(bytes, checked(dataStart + (int)shape.DataOffset + 48), capsuleEnd);
                    if (edit.Radius is { } capsuleRadius)
                        WriteSingle(bytes, checked(dataStart + (int)shape.DataOffset + 80), ValidateRadius(capsuleRadius));
                    break;
                case "hclSphereShape":
                    if (edit.Start is { } sphereCenter)
                    {
                        var radius = edit.Radius ?? sphereCenter.W;
                        WriteVector4(bytes, checked(dataStart + (int)shape.DataOffset + 32), new System.Numerics.Vector4(sphereCenter.X, sphereCenter.Y, sphereCenter.Z, ValidateRadius(radius)));
                    }
                    else if (edit.Radius is { } sphereRadius)
                    {
                        WriteVector4(bytes, checked(dataStart + (int)shape.DataOffset + 32), new System.Numerics.Vector4(shape.Start.X, shape.Start.Y, shape.Start.Z, ValidateRadius(sphereRadius)));
                    }
                    break;
                case "hclTaperedCapsuleShape":
                    if ((edit.Start is { } small && small != shape.Start) ||
                        (edit.End is { } big && big != shape.End) ||
                        (edit.Radius is { } taperedRadius && taperedRadius != shape.Radius))
                        throw new NotSupportedException("BPHCL tapered-capsule geometry edits require derived cone data. Move or rotate the collider as a whole instead; its two radii will be preserved.");
                    break;
                case "hclPlaneShape":
                    if (edit.PlaneEquation is { } equation)
                    {
                        if (new System.Numerics.Vector3(equation.X, equation.Y, equation.Z).LengthSquared() < 0.000001f)
                            throw new ArgumentException("Plane normal must not be zero.", nameof(edits));
                        WriteVector4(bytes, checked(dataStart + (int)shape.DataOffset + 32), equation);
                    }
                    break;
            }
        }

        return NativeBphclDocument.Parse(bytes);
    }

    /// <summary>
    /// Mirrors the uncompressed matrix payloads that bridge a BPHCL cloth's
    /// skeleton and its virtual mesh. Particle positions alone are not enough:
    /// object-space skin matrices initialize the cloth from animated bones,
    /// and simple-mesh matrices write the solved mesh back to output bones.
    /// </summary>
    public static NativeBphclDocument MirrorClothGeometryAcrossX(
        NativeBphclDocument document,
        int clothIndex,
        NativeBphclDocument? sourceDocument = null)
    {
        var cloth = document.Cloths.ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var bytes = (byte[])document.Bytes.Clone();
        var dataStart = document.DataSection?.PayloadOffset
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var reflection = System.Numerics.Matrix4x4.CreateScale(-1.0f, 1.0f, 1.0f);
        var sourceCloth = sourceDocument?.Cloths.ElementAtOrDefault(clothIndex);
        var sourceSkeleton = sourceDocument?.Skeletons.ElementAtOrDefault(clothIndex);
        var targetSkeleton = document.Skeletons.ElementAtOrDefault(clothIndex);

        foreach (var skin in cloth.ObjectSpaceSkins)
        {
            var item = document.GetItem(skin.ItemIndex);
            var memberOffset = GetMemberOffset(document, item.TypeIndex, "boneFromSkinMeshTransforms")
                ?? throw new InvalidDataException("BPHCL object-space skin has no boneFromSkinMeshTransforms member.");
            var sourceSkin = sourceCloth?.ObjectSpaceSkins
                .FirstOrDefault(candidate => candidate.OperatorIndex == skin.OperatorIndex);
            if (sourceSkin != null && sourceSkeleton != null && targetSkeleton != null)
            {
                MirrorMatrixArrayInBoneFrames(
                    document,
                    bytes,
                    dataStart,
                    checked(item.DataOffset + memberOffset),
                    sourceSkin.BoneFromSkinMeshTransforms,
                    skin.TransformSubset,
                    sourceSkeleton,
                    targetSkeleton,
                    reflection);
            }
            else
            {
                MirrorMatrixArray(document, bytes, dataStart, checked(item.DataOffset + memberOffset), reflection);
            }
            MirrorPackedPositionArray(
                document,
                bytes,
                dataStart,
                GetRequiredMemberArrayOffset(document, item, "localPs"));
            MirrorPositionArray(
                document,
                bytes,
                dataStart,
                GetRequiredMemberArrayOffset(document, item, "localUnpackedPs"),
                positionsPerBlock: 16);
        }

        foreach (var skin in cloth.BoneSpaceSkins)
        {
            var item = document.GetItem(skin.ItemIndex);
            MirrorPositionArray(
                document,
                bytes,
                dataStart,
                GetRequiredMemberArrayOffset(document, item, "localPs"),
                positionsPerBlock: 16);
            MirrorPositionArray(
                document,
                bytes,
                dataStart,
                GetRequiredMemberArrayOffset(document, item, "localUnpackedPs"),
                positionsPerBlock: 16);
        }

        foreach (var deformer in cloth.SimpleMeshBoneDeformers)
        {
            // hclSimpleMeshBoneDeformOperator does not store a generic offset.
            // At rest its matrix is: outputBoneWorld * inverse(triangleFrame),
            // where triangleFrame has its origin at the triangle centroid,
            // its first two rows point from that centroid to vertices 0 and 1,
            // and its third row is their cross product. This reproduces native
            // left/right BPHCL transforms within floating-point tolerance.
            if (targetSkeleton != null &&
                TryRebuildSimpleMeshBoneDeformMatrices(
                    document,
                    bytes,
                    dataStart,
                    cloth,
                    deformer,
                    targetSkeleton, sourceCloth,
                    sourceCloth?.SimpleMeshBoneDeformers.FirstOrDefault(d => d.OperatorIndex == deformer.OperatorIndex),
                    sourceSkeleton))
            {
                continue;
            }

            // Retain the legacy fallback for unusual layouts where the
            // deformer is not backed by the primary simulation triangle mesh.
            if (sourceDocument != null)
                throw new InvalidOperationException("The native cloth deformer cannot be rebased safely for mirroring. No data was changed.");
            var item = document.GetItem(deformer.ItemIndex);
            MirrorMatrixArray(document, bytes, dataStart, checked(item.DataOffset + 96), reflection);
        }

        return NativeBphclDocument.Parse(bytes);
    }

    /// <summary>
    /// Reflection reverses a triangle's handedness. Swap its final two
    /// particle indexes so the mirrored virtual surface keeps its original
    /// normal direction without changing any link-rest distances.
    /// </summary>
    public static NativeBphclDocument FlipTriangleWinding(
        NativeBphclDocument document,
        int clothIndex)
    {
        var cloth = document.Cloths.ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var bytes = (byte[])document.Bytes.Clone();
        var dataStart = document.DataSection?.PayloadOffset
            ?? throw new InvalidDataException("BPHCL has no DATA section.");

        // The shared editor currently exposes one simulation instance per
        // logical cloth, matching the primary instance used for particles.
        // Do not flip hidden secondary instances whose particles were not
        // mirrored by the editor.
        var simulation = cloth.SimCloths.FirstOrDefault();
        var mirroredTriangleItems = new HashSet<int>();
        if (simulation != null)
        {
            if (simulation.TriangleIndices.Count >= 3 &&
                document.TryGetReferencedItem(simulation.DataOffset + 352, out var triangleItemIndex))
            {
                var triangleItem = document.GetItem(triangleItemIndex);
                mirroredTriangleItems.Add(triangleItemIndex);
                for (var offset = 0; offset + 2 < simulation.TriangleIndices.Count; offset += 3)
                {
                    var bOffset = checked(dataStart + (int)triangleItem.DataOffset + (offset + 1) * sizeof(ushort));
                    var cOffset = checked(dataStart + (int)triangleItem.DataOffset + (offset + 2) * sizeof(ushort));
                    var b = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(bOffset, sizeof(ushort)));
                    var c = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(cOffset, sizeof(ushort)));
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bOffset, sizeof(ushort)), c);
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(cOffset, sizeof(ushort)), b);
                }
            }
        }

        foreach (var buffer in cloth.BufferDefinitions)
        {
            var item = document.GetItem(buffer.ItemIndex);
            var member = GetMemberOffset(document, item.TypeIndex, "triangleIndices");
            if (!member.HasValue || !document.TryGetReferencedItem(checked(item.DataOffset + member.Value), out var index) || !mirroredTriangleItems.Add(index)) continue;
            var triangles = document.GetItem(index);
            for (var i = 0; i + 2 < triangles.Count; i += 3)
            {
                var b = checked(dataStart + (int)triangles.DataOffset + (i + 1) * sizeof(ushort));
                var c = checked(b + sizeof(ushort));
                var value = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(b, 2));
                bytes.AsSpan(c, 2).CopyTo(bytes.AsSpan(b, 2));
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(c, 2), value);
            }
        }

        return NativeBphclDocument.Parse(bytes);
    }

    /// <summary>
    /// Updates reflected hkReal fields inside existing BPHCL constraint
    /// records. The type table supplies the byte offsets, so this never
    /// assumes a hand-written record layout or changes array allocation.
    /// </summary>
    public static NativeBphclDocument ApplyConstraintEdits(
        NativeBphclDocument document,
        int clothIndex,
        int simulationIndex,
        IReadOnlyList<NativeBphclConstraintEdit> edits)
    {
        if (edits.Count == 0)
            return document;

        var simulation = document.Cloths
            .ElementAtOrDefault(clothIndex)?.SimCloths
            .ElementAtOrDefault(simulationIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(simulationIndex));
        var bytes = (byte[])document.Bytes.Clone();
        var dataStart = document.DataSection?.PayloadOffset
            ?? throw new InvalidDataException("BPHCL has no DATA section.");

        foreach (var edit in edits)
        {
            var set = simulation.ConstraintSets.ElementAtOrDefault(edit.ConstraintSetIndex)
                ?? throw new ArgumentOutOfRangeException(nameof(edit), "BPHCL constraint set no longer exists.");
            var records = edit.IsLocalRecord ? set.LocalConstraints.Concat(set.LocalStiffnessConstraints).ToArray() : set.Links;
            var record = records.ElementAtOrDefault(edit.RecordIndex)
                ?? throw new ArgumentOutOfRangeException(nameof(edit), "BPHCL constraint record no longer exists.");

            foreach (var (name, value) in edit.Values)
            {
                if (!float.IsFinite(value))
                    throw new ArgumentOutOfRangeException(nameof(edit), $"Constraint field {name} must be finite.");
                // The shared editor exposes a normalized vocabulary (for
                // example maximumDistance/maxDistance). A native record may
                // implement only one spelling; fields absent from this exact
                // reflected record are intentionally left untouched.
                if (!record.EditableValueOffsets.TryGetValue(name, out var memberOffset))
                    continue;
                WriteSingle(bytes, checked(dataStart + (int)record.DataOffset + (int)memberOffset), value);
            }
        }

        return NativeBphclDocument.Parse(bytes);
    }

    private static void WriteVector4(byte[] bytes, int offset, System.Numerics.Vector4 value)
    {
        WriteSingle(bytes, offset, value.X);
        WriteSingle(bytes, offset + 4, value.Y);
        WriteSingle(bytes, offset + 8, value.Z);
        WriteSingle(bytes, offset + 12, value.W);
    }

    private static void WriteSingle(byte[] bytes, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));

    private static void MirrorMatrixArray(
        NativeBphclDocument document,
        byte[] bytes,
        int dataStart,
        uint arrayFieldOffset,
        System.Numerics.Matrix4x4 reflection)
    {
        if (!document.TryGetReferencedItem(arrayFieldOffset, out var itemIndex))
            throw new InvalidDataException("BPHCL matrix array has no valid ITEM reference.");
        var item = document.GetItem(itemIndex);
        if (item.Count == 0)
            return;

        for (uint index = 0; index < item.Count; index++)
        {
            var offset = checked(dataStart + (int)item.DataOffset + (int)(index * 64));
            if (offset < 0 || offset > bytes.Length - 64)
                throw new InvalidDataException("BPHCL matrix array extends beyond DATA.");
            var matrix = ReadMatrix4(bytes, offset);
            WriteMatrix4(bytes, offset, reflection * matrix * reflection);
        }
    }

    private static bool TryRebuildSimpleMeshBoneDeformMatrices(
        NativeBphclDocument document,
        byte[] bytes,
        int dataStart,
        NativeBphclCloth cloth,
        NativeBphclSimpleMeshBoneDeform deformer,
        NativeBphclSkeleton skeleton,
        NativeBphclCloth? sourceCloth = null,
        NativeBphclSimpleMeshBoneDeform? sourceDeformer = null,
        NativeBphclSkeleton? sourceSkeleton = null)
    {
        if (deformer.TriangleBonePairs.Count == 0 ||
            deformer.TriangleBonePairs.Count != deformer.LocalBoneTransforms.Count ||
            !TryFindTriangleSimulation(cloth, deformer, out var simulation))
        {
            return false;
        }

        var item = document.GetItem(deformer.ItemIndex);
        if (!document.TryGetReferencedItem(checked(item.DataOffset + 96), out var transformItemIndex))
            return false;

        var transformItem = document.GetItem(transformItemIndex);
        if (transformItem.Count != deformer.TriangleBonePairs.Count)
            return false;

        var worlds = BuildSkeletonWorldMatrices(skeleton);
        var originalWorlds = sourceSkeleton == null ? null : BuildSkeletonWorldMatrices(sourceSkeleton);
        NativeBphclSimCloth? originalSimulation = null;
        if (sourceCloth != null && sourceDeformer != null && TryFindTriangleSimulation(sourceCloth, sourceDeformer, out var original)) originalSimulation = original;
        for (var index = 0; index < deformer.TriangleBonePairs.Count; index++)
        {
            var pair = deformer.TriangleBonePairs[index];
            var triangleIndex = pair.TriangleOffset / 6;
            var triangleOffset = checked((int)triangleIndex * 3);
            if (triangleOffset < 0 || triangleOffset > simulation.TriangleIndices.Count - 3)
                return false;

            var particle0 = simulation.TriangleIndices[triangleOffset];
            var particle1 = simulation.TriangleIndices[triangleOffset + 1];
            var particle2 = simulation.TriangleIndices[triangleOffset + 2];
            if (particle0 >= simulation.Particles.Count ||
                particle1 >= simulation.Particles.Count ||
                particle2 >= simulation.Particles.Count)
            {
                return false;
            }

            var triangleFrame = BuildCentroidTriangleFrame(
                simulation.Particles[particle0].Position,
                simulation.Particles[particle1].Position,
                simulation.Particles[particle2].Position);
            if (!System.Numerics.Matrix4x4.Invert(triangleFrame, out var inverseTriangleFrame))
                return false;

            var boneIndex = pair.BoneOffset / 64;
            if (!worlds.TryGetValue(boneIndex, out var boneWorld))
                return false;

            var rebuilt = boneWorld * inverseTriangleFrame;
            if (originalSimulation != null && sourceDeformer != null && originalWorlds != null && index < sourceDeformer.LocalBoneTransforms.Count)
            {
                var originalOffset = sourceDeformer.TriangleBonePairs[index].TriangleOffset / 6 * 3;
                if (originalOffset > originalSimulation.TriangleIndices.Count - 3 || !originalWorlds.TryGetValue(boneIndex, out var originalBone)) return false;
                var originalFrame = BuildCentroidTriangleFrame(
                    originalSimulation.Particles[originalSimulation.TriangleIndices[originalOffset]].Position,
                    originalSimulation.Particles[originalSimulation.TriangleIndices[originalOffset + 1]].Position,
                    originalSimulation.Particles[originalSimulation.TriangleIndices[originalOffset + 2]].Position);
                var reflection = System.Numerics.Matrix4x4.CreateScale(-1, 1, 1);
                if (!System.Numerics.Matrix4x4.Invert(reflection * originalBone * reflection, out var inverseMirroredBone)) return false;
                // Preserve the authored deformer-to-bone offset, including scale,
                // while rebasing into TotK's mirrored target bone convention.
                rebuilt = reflection * sourceDeformer.LocalBoneTransforms[index] * originalFrame * reflection * inverseMirroredBone * boneWorld * inverseTriangleFrame;
            }
            var matrixOffset = checked(dataStart + (int)transformItem.DataOffset + index * 64);
            if (matrixOffset < 0 || matrixOffset > bytes.Length - 64)
                return false;
            WriteMatrix4(bytes, matrixOffset, rebuilt);
        }

        return true;
    }

    private static bool TryFindTriangleSimulation(
        NativeBphclCloth cloth,
        NativeBphclSimpleMeshBoneDeform deformer,
        out NativeBphclSimCloth simulation)
    {
        var requiredTriangleCount = deformer.TriangleBonePairs
            .Select(pair => checked((int)(pair.TriangleOffset / 6) + 1))
            .DefaultIfEmpty(0)
            .Max();

        simulation = cloth.SimCloths.FirstOrDefault(candidate =>
            candidate.TriangleIndices.Count >= requiredTriangleCount * 3 &&
            candidate.TriangleIndices.All(index => index < candidate.Particles.Count))!;
        return simulation != null;
    }

    private static System.Numerics.Matrix4x4 BuildCentroidTriangleFrame(
        System.Numerics.Vector4 point0,
        System.Numerics.Vector4 point1,
        System.Numerics.Vector4 point2)
    {
        var centroid = new System.Numerics.Vector3(
            (point0.X + point1.X + point2.X) / 3.0f,
            (point0.Y + point1.Y + point2.Y) / 3.0f,
            (point0.Z + point1.Z + point2.Z) / 3.0f);
        var offset0 = new System.Numerics.Vector3(point0.X, point0.Y, point0.Z) - centroid;
        var offset1 = new System.Numerics.Vector3(point1.X, point1.Y, point1.Z) - centroid;
        var normal = System.Numerics.Vector3.Cross(offset0, offset1);
        return new System.Numerics.Matrix4x4(
            offset0.X, offset0.Y, offset0.Z, 0.0f,
            offset1.X, offset1.Y, offset1.Z, 0.0f,
            normal.X, normal.Y, normal.Z, 0.0f,
            centroid.X, centroid.Y, centroid.Z, 1.0f);
    }

    /// <summary>
    /// Mirrors matrices that bridge virtual-mesh space and a cloth skeleton.
    /// A raw global reflection is only correct when the source and target bone
    /// frames themselves are exact reflections. BPHCL skeletons are authored
    /// in local parent space, so each matrix must be rebased through the real
    /// source and target world transforms as well.
    /// </summary>
    private static void MirrorMatrixArrayInBoneFrames(
        NativeBphclDocument document,
        byte[] bytes,
        int dataStart,
        uint arrayFieldOffset,
        IReadOnlyList<System.Numerics.Matrix4x4> sourceMatrices,
        IReadOnlyList<ushort> boneIndices,
        NativeBphclSkeleton sourceSkeleton,
        NativeBphclSkeleton targetSkeleton,
        System.Numerics.Matrix4x4 reflection)
    {
        if (!document.TryGetReferencedItem(arrayFieldOffset, out var itemIndex))
            throw new InvalidDataException("BPHCL matrix array has no valid ITEM reference.");

        var item = document.GetItem(itemIndex);
        if (item.Count == 0)
            return;

        // The mapping is per matrix, so do not apply a partial frame rewrite
        // when the duplicate no longer has the same operator layout.
        if (item.Count != sourceMatrices.Count || sourceMatrices.Count != boneIndices.Count)
        {
            MirrorMatrixArray(document, bytes, dataStart, arrayFieldOffset, reflection);
            return;
        }

        var sourceWorlds = BuildSkeletonWorldMatrices(sourceSkeleton);
        var targetWorlds = BuildSkeletonWorldMatrices(targetSkeleton);
        for (var index = 0; index < sourceMatrices.Count; index++)
        {
            var boneIndex = boneIndices[index];
            if (!sourceWorlds.TryGetValue(boneIndex, out var sourceWorld) ||
                !targetWorlds.TryGetValue(boneIndex, out var targetWorld) ||
                !System.Numerics.Matrix4x4.Invert(targetWorld, out var inverseTargetWorld))
            {
                // An incomplete skeleton is safer with the old, known-global
                // reflection than a partially rebased bridge matrix.
                MirrorMatrixArray(document, bytes, dataStart, arrayFieldOffset, reflection);
                return;
            }

            // Row-vector convention: virtual -> source bone -> world, then
            // reflect world X, then transform into the target bone frame.
            var mirrored = reflection * sourceMatrices[index] * sourceWorld * reflection * inverseTargetWorld;
            var offset = checked(dataStart + (int)item.DataOffset + index * 64);
            if (offset < 0 || offset > bytes.Length - 64)
                throw new InvalidDataException("BPHCL matrix array extends beyond DATA.");
            WriteMatrix4(bytes, offset, mirrored);
        }
    }

    private static IReadOnlyDictionary<int, System.Numerics.Matrix4x4> BuildSkeletonWorldMatrices(
        NativeBphclSkeleton skeleton)
    {
        var bones = skeleton.Bones.ToDictionary(bone => bone.Index);
        var worlds = new Dictionary<int, System.Numerics.Matrix4x4>();
        var visiting = new HashSet<int>();

        System.Numerics.Matrix4x4 GetWorld(int boneIndex)
        {
            if (worlds.TryGetValue(boneIndex, out var cached))
                return cached;
            if (!bones.TryGetValue(boneIndex, out var bone) || !visiting.Add(boneIndex))
                return System.Numerics.Matrix4x4.Identity;

            var quaternion = new System.Numerics.Quaternion(
                bone.Rotation.X,
                bone.Rotation.Y,
                bone.Rotation.Z,
                bone.Rotation.W);
            var rotation = quaternion.LengthSquared() > 0.00000001f
                ? System.Numerics.Matrix4x4.CreateFromQuaternion(System.Numerics.Quaternion.Normalize(quaternion))
                : System.Numerics.Matrix4x4.Identity;
            var local = rotation * System.Numerics.Matrix4x4.CreateTranslation(
                bone.Translation.X,
                bone.Translation.Y,
                bone.Translation.Z);
            var world = bone.ParentIndex >= 0 ? local * GetWorld(bone.ParentIndex) : local;
            visiting.Remove(boneIndex);
            worlds[boneIndex] = world;
            return world;
        }

        foreach (var bone in skeleton.Bones)
            _ = GetWorld(bone.Index);
        return worlds;
    }

    // hclObjectSpaceDeformer::LocalBlockP contains sixteen hkPackedVector3
    // values per block: X/Y/Z signed mantissas followed by a shared exponent.
    // A global X reflection changes only the first mantissa of each vector.
    private static void MirrorPackedPositionArray(
        NativeBphclDocument document,
        byte[] bytes,
        int dataStart,
        uint arrayFieldOffset)
    {
        // Both local position arrays are optional. TotK commonly stores only
        // the packed block and leaves localUnpackedPs null.
        if (!document.TryGetReferencedItem(arrayFieldOffset, out var itemIndex))
            return;
        var item = document.GetItem(itemIndex);
        const int positionsPerBlock = 16;
        const int packedComponentsPerPosition = 4;
        const int bytesPerPosition = packedComponentsPerPosition * sizeof(short);
        const int bytesPerBlock = positionsPerBlock * bytesPerPosition;

        for (uint blockIndex = 0; blockIndex < item.Count; blockIndex++)
        {
            var blockOffset = checked(dataStart + (int)item.DataOffset + (int)(blockIndex * bytesPerBlock));
            if (blockOffset < 0 || blockOffset > bytes.Length - bytesPerBlock)
                throw new InvalidDataException("BPHCL packed position array extends beyond DATA.");

            for (var positionIndex = 0; positionIndex < positionsPerBlock; positionIndex++)
            {
                var xOffset = blockOffset + positionIndex * bytesPerPosition;
                var x = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(xOffset, sizeof(short)));
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(xOffset, sizeof(short)), unchecked((short)-x));
            }
        }
    }

    private static void MirrorPositionArray(
        NativeBphclDocument document,
        byte[] bytes,
        int dataStart,
        uint arrayFieldOffset,
        int positionsPerBlock)
    {
        if (!document.TryGetReferencedItem(arrayFieldOffset, out var itemIndex))
            return;
        var item = document.GetItem(itemIndex);
        var bytesPerBlock = checked(positionsPerBlock * sizeof(float) * 4);

        for (uint blockIndex = 0; blockIndex < item.Count; blockIndex++)
        {
            var blockOffset = checked(dataStart + (int)item.DataOffset + (int)(blockIndex * bytesPerBlock));
            if (blockOffset < 0 || blockOffset > bytes.Length - bytesPerBlock)
                throw new InvalidDataException("BPHCL position array extends beyond DATA.");

            for (var positionIndex = 0; positionIndex < positionsPerBlock; positionIndex++)
            {
                var xOffset = blockOffset + positionIndex * sizeof(float) * 4;
                var x = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(xOffset, sizeof(float))));
                WriteSingle(bytes, xOffset, -x);
            }
        }
    }

    private static uint GetRequiredMemberArrayOffset(
        NativeBphclDocument document,
        NativeBphclItem item,
        string memberName)
    {
        var memberOffset = GetMemberOffset(document, item.TypeIndex, memberName)
            ?? throw new InvalidDataException($"BPHCL operator has no reflected {memberName} member.");
        return checked(item.DataOffset + memberOffset);
    }

    private static uint? GetMemberOffset(NativeBphclDocument document, uint typeIndex, string memberName)
    {
        var visited = new HashSet<uint>();
        while (typeIndex != 0 && visited.Add(typeIndex))
        {
            var type = document.TypeDefinitions.FirstOrDefault(candidate => candidate.TypeIndex == typeIndex);
            if (type == null)
                return null;
            var member = type.Members.FirstOrDefault(candidate => string.Equals(candidate.Name, memberName, StringComparison.Ordinal));
            if (member != null)
                return member.Offset;
            typeIndex = type.ParentTypeIndex;
        }
        return null;
    }

    private static System.Numerics.Matrix4x4 ReadMatrix4(byte[] bytes, int offset) => new(
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 8, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 12, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 16, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 20, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 24, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 28, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 32, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 36, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 40, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 44, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 48, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 52, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 56, 4))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 60, 4))));

    private static void WriteMatrix4(byte[] bytes, int offset, System.Numerics.Matrix4x4 matrix)
    {
        WriteVector4(bytes, offset, new System.Numerics.Vector4(matrix.M11, matrix.M12, matrix.M13, matrix.M14));
        WriteVector4(bytes, offset + 16, new System.Numerics.Vector4(matrix.M21, matrix.M22, matrix.M23, matrix.M24));
        WriteVector4(bytes, offset + 32, new System.Numerics.Vector4(matrix.M31, matrix.M32, matrix.M33, matrix.M34));
        WriteVector4(bytes, offset + 48, new System.Numerics.Vector4(matrix.M41, matrix.M42, matrix.M43, matrix.M44));
    }

    private static float ValidateRadius(float radius)
    {
        if (!float.IsFinite(radius) || radius < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(radius), "Collider radius must be finite and non-negative.");
        return radius;
    }

    private static void Align(List<byte> data, int alignment)
    {
        while (data.Count % alignment != 0)
            data.Add(0);
    }

    private static NativeBphclItem RemapItemType(
        NativeBphclItem item,
        IReadOnlyDictionary<uint, uint> typeMap)
    {
        var typeIndex = RemapTypeIndex(item.TypeIndex, typeMap);
        return item with { Flags = (item.Flags & 0xff00_0000u) | typeIndex, TypeIndex = typeIndex };
    }

    private static uint RemapTypeIndex(uint typeIndex, IReadOnlyDictionary<uint, uint> typeMap)
    {
        if (!typeMap.TryGetValue(typeIndex, out var remapped))
            throw new InvalidDataException($"BPHCL TYPE map has no entry for type {typeIndex}.");
        return remapped;
    }
}
