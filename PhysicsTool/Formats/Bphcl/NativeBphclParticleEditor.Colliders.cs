using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace HKCLTool;

internal sealed partial class NativeBphclParticleEditor
{
    public static NativeBphclDocument MakeCollidersUnique(NativeBphclDocument document, int clothIndex,
        IReadOnlySet<int> colliderIndices)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(colliderIndices);
        var cloth = document.Cloths[clothIndex];
        if (colliderIndices.Count == 0) return document;
        var edit = new NativeBphclParticleEditor(document);
        var requested = colliderIndices.Order().Select(index => document.Colliders.ElementAtOrDefault(index)
            ?? throw new ArgumentOutOfRangeException(nameof(colliderIndices))).ToArray();
        var usedItems = cloth.SimCloths.SelectMany(sim => sim.CollidableItemIndices).ToHashSet();
        if (requested.Any(collider => !usedItems.Contains(collider.ItemIndex)))
            throw new ArgumentException("A requested collider is not referenced by this cloth.", nameof(colliderIndices));
        var selectedItems = requested.Select(collider => collider.ItemIndex).ToHashSet();
        var simulations = cloth.SimCloths.Where(sim => sim.CollidableItemIndices.Any(selectedItems.Contains)).ToArray();
        ValidateColliderSimulationOwnership(document, clothIndex, simulations);

        var replacements = new Dictionary<int, int>();
        foreach (var collider in requested)
        {
            if (replacements.ContainsKey(collider.ItemIndex)) continue;
            var shapeField = edit.OptionalField(collider.ItemIndex, "shape") ?? checked(collider.DataOffset + 136);
            if (!document.TryGetReferencedItem(shapeField, out var shapeIndex) || shapeIndex == 0)
                throw new InvalidDataException("The collider has no native shape reference.");
            var shape = document.GetItem(shapeIndex);
            if (document.GetTypeName(shape.TypeIndex) is not
                ("hclSphereShape" or "hclCapsuleShape" or "hclTaperedCapsuleShape" or "hclPlaneShape"))
                throw new InvalidOperationException("Collider isolation supports only sphere, capsule, tapered-capsule and plane shapes.");

            // Clone per collider, not per shape: distinct global colliders may share geometry.
            var clonedShape = edit.CloneColliderObject(shapeIndex);
            var clonedCollider = edit.CloneColliderObject(collider.ItemIndex);
            var clonedShapeField = checked(edit._items[clonedCollider].DataOffset + shapeField - collider.DataOffset);
            edit.UInt(clonedShapeField, checked((uint)clonedShape));
            edit.UInt(clonedShapeField + 4, 0);
            replacements.Add(collider.ItemIndex, clonedCollider);
        }

        foreach (var sim in simulations.DistinctBy(sim => sim.ItemIndex))
        {
            var field = edit.OptionalField(sim.ItemIndex, "perInstanceCollidables") ?? checked(sim.DataOffset + 208);
            edit.ReplaceColliderReferences(field, sim.CollidableItemIndices
                .Select(item => replacements.GetValueOrDefault(item, item)).ToArray());
        }
        var container = document.FindRootVariants("hclClothContainer").SingleOrDefault()
            ?? throw new InvalidDataException("Missing native cloth container.");
        var globalField = edit.OptionalField(container.ObjectItemIndex, "collidables") ?? checked(container.ObjectDataOffset + 24);
        edit.ReplaceColliderReferences(globalField, document.GetReferenceItemIndices(globalField)
            .Concat(replacements.Values).ToArray());
        return edit.Finish();
    }

    public static NativeBphclDocument UpdateColliderOffsets(NativeBphclDocument document, int clothIndex,
        IReadOnlyDictionary<int, Matrix4x4> offsetsByColliderIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(offsetsByColliderIndex);
        var cloth = document.Cloths[clothIndex];
        if (offsetsByColliderIndex.Count == 0) return document;
        var offsetsByItem = new Dictionary<int, Matrix4x4>();
        foreach (var (index, matrix) in offsetsByColliderIndex)
        {
            var collider = document.Colliders.ElementAtOrDefault(index)
                ?? throw new ArgumentOutOfRangeException(nameof(offsetsByColliderIndex));
            if (offsetsByItem.TryGetValue(collider.ItemIndex, out var existing) && existing != matrix)
                throw new ArgumentException("Aliased collider entries have conflicting offsets.", nameof(offsetsByColliderIndex));
            offsetsByItem[collider.ItemIndex] = matrix;
        }

        var edit = new NativeBphclParticleEditor(document);
        var changed = false;
        foreach (var sim in cloth.SimCloths.DistinctBy(sim => sim.ItemIndex))
        {
            var setField = edit.ColliderMapField(sim, "transformSetIndex", 0);
            // Native maps with -1 are deliberately disabled, even when arrays are present.
            if (unchecked((int)edit.Read(setField)) < 0 || !sim.CollidableItemIndices.Any(offsetsByItem.ContainsKey)) continue;
            var offsetField = edit.ColliderMapField(sim, "offsets", 24);
            var array = edit.Array(offsetField);
            if (array is not { Count: > 0 }) continue;
            var indexField = edit.ColliderMapField(sim, "transformIndices", 8);
            var indices = edit.Array(indexField);
            if (array.Count != sim.CollidableItemIndices.Count || indices?.Count != array.Count)
                throw new InvalidDataException("The collider transform map does not match its simulation slots.");
            ValidateColliderSimulationOwnership(document, clothIndex, new[] { sim });
            var bytes = edit.ArrayBytes(offsetField, 64);
            for (var slot = 0; slot < sim.CollidableItemIndices.Count; slot++)
            {
                if (!offsetsByItem.TryGetValue(sim.CollidableItemIndices[slot], out var matrix)) continue;
                var values = new[] { matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                    matrix.M21, matrix.M22, matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                    matrix.M41, matrix.M42, matrix.M43, matrix.M44 };
                for (var component = 0; component < values.Length; component++)
                    WriteFloat(bytes, checked(slot * 64 + component * 4), values[component]);
            }
            // Only offset storage is replaced; bone indices and map enablement remain byte-identical.
            edit.ReplaceExistingArray(offsetField, bytes, 64);
            changed = true;
        }
        return changed ? edit.Finish() : document;
    }

    private static void ValidateColliderSimulationOwnership(NativeBphclDocument document, int clothIndex,
        IEnumerable<NativeBphclSimCloth> simulations)
    {
        var items = simulations.Select(sim => sim.ItemIndex).ToHashSet();
        var offsets = simulations.Select(sim => sim.DataOffset).ToHashSet();
        if (document.Cloths.Where((_, index) => index != clothIndex).SelectMany(cloth => cloth.SimCloths)
            .Any(sim => items.Contains(sim.ItemIndex) || offsets.Contains(sim.DataOffset)))
            throw new InvalidOperationException("This cloth shares a simulation object with another cloth. Duplicate it before editing colliders.");
    }

    private uint ColliderMapField(NativeBphclSimCloth sim, string name, uint fallbackOffset)
    {
        if (OptionalField(sim.ItemIndex, "collidableTransformMap") is { } mapField)
        {
            var map = Member(_items[sim.ItemIndex].TypeIndex, "collidableTransformMap");
            return checked(mapField + Member(map.TypeIndex, name).Offset);
        }
        return checked(sim.DataOffset + 168 + fallbackOffset);
    }

    private int CloneColliderObject(int itemIndex)
    {
        var original = _items[itemIndex];
        var definition = _document.TypeDefinitions.Single(type => type.TypeIndex == original.TypeIndex);
        if (original.Count != 1 || definition.Size is not { } size || size == 0 ||
            (definition.Alignment is { } alignment && alignment > 16))
            throw new InvalidDataException("Unsupported native collider object layout.");
        var bytes = CollectionsMarshal.AsSpan(_data).Slice(checked((int)original.DataOffset), checked((int)size)).ToArray();
        var offset = Allocate(bytes);
        var index = _items.Count;
        _items.Add(original with { DataOffset = offset });
        foreach (var patches in _patches.Values)
            foreach (var patch in patches.Where(p => p >= original.DataOffset && (ulong)p < (ulong)original.DataOffset + size).ToArray())
            {
                if ((ulong)patch + 8 > (ulong)original.DataOffset + size)
                    throw new InvalidDataException("A collider relocation extends beyond its object.");
                patches.Add(checked(offset + patch - original.DataOffset));
            }
        return index;
    }

    private void ReplaceColliderReferences(uint field, IReadOnlyList<int> references)
    {
        var original = _document.GetReferenceArray(field);
        var bytes = new byte[checked(references.Count * 8)];
        for (var index = 0; index < references.Count; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 8, 4), checked((uint)references[index]));
        ReplaceArray(field, bytes, references.Count, original.StorageItem.TypeIndex,
            _patches.Single(group => group.Value.Contains(field)).Key);
        if (Array(field) is not { } array) return;
        if (!_patches.TryGetValue(original.EntryPatchTypeIndex, out var patches))
            _patches[original.EntryPatchTypeIndex] = patches = new HashSet<uint>();
        for (uint index = 0; index < array.Count; index++)
            patches.Add(checked(array.DataOffset + index * 8));
    }
}
