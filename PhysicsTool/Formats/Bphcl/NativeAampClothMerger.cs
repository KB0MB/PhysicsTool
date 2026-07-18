using System.Buffers.Binary;

namespace HKCLTool;

// A bounded AAMP writer for phcl's cloth_mesh_list. It rebuilds only that
// list's object records, preserving the rest of the archive byte-for-byte.
// Current retail cloth entries use bool, float, and StringRef parameters.
internal static class NativeAampClothMerger
{
    private const uint ClothMeshListHash = 1_571_872_146;
    private const uint CollidableListHash = 107_719_806;
    private const uint NameParameterHash = 4_262_580_536;
    private const byte StringReference = 20;

    public static byte[] GetOriginalArchive(NativeBphclDocument document) =>
        AampArchive.Read(document.Bytes, document.Header.ParameterOffset, document.Header.ParameterSize).Bytes;

    public static byte[] AppendClothEntry(NativeBphclDocument target, NativeBphclDocument source, string sourceClothName)
    {
        var targetArchive = AampArchive.Read(target.Bytes, target.Header.ParameterOffset, target.Header.ParameterSize);
        var sourceArchive = AampArchive.Read(source.Bytes, source.Header.ParameterOffset, source.Header.ParameterSize);
        return AppendEntries(
            targetArchive.Bytes,
            sourceArchive,
            ClothMeshListHash,
            "cloth_mesh_",
            new[] { sourceClothName },
            required: true);
    }

    public static byte[] AppendColliderEntries(
        byte[] targetAamp,
        NativeBphclDocument source,
        IEnumerable<string> colliderNames)
    {
        var sourceArchive = AampArchive.Read(source.Bytes, source.Header.ParameterOffset, source.Header.ParameterSize);
        return AppendEntries(
            targetAamp,
            sourceArchive,
            CollidableListHash,
            "collidable_",
            colliderNames.Distinct(StringComparer.Ordinal),
            required: false);
    }

    // Keep the AAMP collidable_list aligned with the live global collider
    // array. This relocates only the list records inside AAMP; unrelated AAMP
    // data and all TAG0 DATA/ITEM allocations remain untouched.
    public static byte[] KeepColliderEntries(
        NativeBphclDocument document,
        IEnumerable<string> retainedNames)
    {
        var archive = AampArchive.Read(document.Bytes, document.Header.ParameterOffset, document.Header.ParameterSize);
        var colliderList = archive.FindList(CollidableListHash);
        var retained = retainedNames.ToHashSet(StringComparer.Ordinal);
        var original = colliderList.Objects.ToArray();
        var kept = original
            .Where(item => string.IsNullOrWhiteSpace(item.Name) || retained.Contains(item.Name))
            .ToArray();
        if (kept.Length == original.Length)
            return archive.Bytes;

        var removed = original.Except(kept).ToArray();
        return RebuildList(
            archive,
            colliderList,
            kept,
            objectCountDelta: -removed.Length,
            parameterCountDelta: -removed.Sum(item => item.Parameters.Count),
            stringBytesDelta: -removed.Sum(item => item.Parameters
                .Where(parameter => parameter.Type == StringReference)
                .Sum(parameter => parameter.Value.Length)));
    }

    // A cloth exists in both TAG0 and the embedded AAMP cloth_mesh_list.
    // Keep the latter in sync when the native hclClothData name changes.
    public static byte[] RenameClothEntry(NativeBphclDocument document, string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return GetOriginalArchive(document);

        var archive = AampArchive.Read(document.Bytes, document.Header.ParameterOffset, document.Header.ParameterSize);
        var clothList = archive.FindList(ClothMeshListHash);
        var index = Array.FindIndex(
            clothList.Objects.ToArray(),
            item => string.Equals(item.Name, oldName, StringComparison.Ordinal));
        if (index < 0)
            throw new InvalidDataException($"BPHCL AAMP has no cloth_mesh_list entry named '{oldName}'.");
        if (clothList.Objects
            .Select((item, itemIndex) => (item, itemIndex))
            .Any(entry => entry.itemIndex != index && string.Equals(entry.item.Name, newName, StringComparison.Ordinal)))
            throw new InvalidDataException($"BPHCL AAMP already has a cloth_mesh_list entry named '{newName}'.");

        var current = clothList.Objects[index];
        var nameFound = false;
        var parameters = current.Parameters.Select(parameter =>
        {
            if (parameter.NameHash != NameParameterHash)
                return parameter;
            nameFound = true;
            return parameter with { Value = parameter.CreateStringValue(newName) };
        }).ToArray();
        if (!nameFound)
            throw new InvalidDataException($"BPHCL AAMP cloth_mesh_list entry '{oldName}' has no writable Name parameter.");

        var objects = clothList.Objects.ToArray();
        objects[index] = current with { Name = newName, Parameters = parameters };
        return RebuildList(archive, clothList, objects, 0, 0, 0);
    }

    private static byte[] AppendEntries(
        byte[] targetAamp,
        AampArchive sourceArchive,
        uint listHash,
        string keyPrefix,
        IEnumerable<string> names,
        bool required)
    {
        var output = targetAamp;
        foreach (var name in names)
            output = AppendEntry(output, sourceArchive, listHash, keyPrefix, name, required);
        return output;
    }

    private static byte[] AppendEntry(
        byte[] targetAamp,
        AampArchive sourceArchive,
        uint listHash,
        string keyPrefix,
        string name,
        bool required)
    {
        var targetArchive = AampArchive.Read(targetAamp, 0, checked((uint)targetAamp.Length));
        var targetList = targetArchive.FindList(listHash);
        var sourceList = sourceArchive.FindList(listHash);
        var donor = sourceList.Objects.SingleOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (donor == null)
        {
            if (required)
                throw new InvalidDataException($"Donor BPHCL AAMP has no entry named '{name}'.");
            return targetAamp;
        }
        if (targetList.Objects.Any(item => string.Equals(item.Name, name, StringComparison.Ordinal)))
            return targetAamp;

        var donorKey = AllocateObjectHash(targetList.Objects, keyPrefix);
        var objects = targetList.Objects.Append(donor with { NameHash = donorKey }).ToArray();
        return RebuildList(
            targetArchive,
            targetList,
            objects,
            objectCountDelta: 1,
            parameterCountDelta: donor.Parameters.Count,
            stringBytesDelta: donor.Parameters.Where(parameter => parameter.Type == StringReference).Sum(parameter => parameter.Value.Length));
    }

    private static byte[] RebuildList(
        AampArchive archive,
        AampList targetList,
        IReadOnlyList<AampObject> objects,
        int objectCountDelta,
        int parameterCountDelta,
        int stringBytesDelta)
    {
        var output = archive.Bytes.ToList();
        Align(output, 4);
        var objectArrayOffset = output.Count;
        output.AddRange(new byte[checked(objects.Count * 8)]);

        for (var objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            var item = objects[objectIndex];
            Align(output, 4);
            var parameterOffset = output.Count;
            output.AddRange(new byte[checked(item.Parameters.Count * 8)]);

            for (var parameterIndex = 0; parameterIndex < item.Parameters.Count; parameterIndex++)
            {
                var parameter = item.Parameters[parameterIndex];
                Align(output, 4);
                var valueOffset = output.Count;
                output.AddRange(parameter.Value);
                var parameterEntryOffset = parameterOffset + parameterIndex * 8;
                WriteUInt32(output, parameterEntryOffset, parameter.NameHash);
                var relativeValueOffset = checked((uint)((valueOffset - parameterEntryOffset) / 4));
                if (relativeValueOffset > 0x00ff_ffff)
                    throw new InvalidDataException("Merged AAMP parameter value offset exceeds 24 bits.");
                WriteUInt32(output, parameterEntryOffset + 4, ((uint)parameter.Type << 24) | relativeValueOffset);
            }

            var objectOffset = objectArrayOffset + objectIndex * 8;
            var relativeParameterOffset = checked((uint)((parameterOffset - objectOffset) / 4));
            if (relativeParameterOffset > 0xffff)
                throw new InvalidDataException("Merged AAMP object parameter offset exceeds 16 bits.");
            if (item.Parameters.Count > ushort.MaxValue)
                throw new InvalidDataException("Merged AAMP object has too many parameters.");
            WriteUInt32(output, objectOffset, item.NameHash);
            WriteUInt32(output, objectOffset + 4, ((uint)item.Parameters.Count << 16) | relativeParameterOffset);
        }

        var relativeObjectOffset = checked((uint)((objectArrayOffset - targetList.Offset) / 4));
        if (relativeObjectOffset > 0xffff || objects.Count > ushort.MaxValue)
            throw new InvalidDataException("Merged AAMP cloth_mesh_list exceeds its 16-bit layout limits.");
        WriteUInt32(output, targetList.ObjectFlagsOffset,
            ((uint)objects.Count << 16) | relativeObjectOffset);

        // Archive counters describe logical objects/parameters, not the old
        // unreachable copies of target cloth metadata left by this relocation.
        WriteUInt32(output, 0x0c, checked((uint)output.Count));
        WriteUInt32(output, 0x1c, ApplyCountDelta(output, 0x1c, objectCountDelta));
        WriteUInt32(output, 0x20, ApplyCountDelta(output, 0x20, parameterCountDelta));
        WriteUInt32(output, 0x28, ApplyCountDelta(output, 0x28, stringBytesDelta));
        // Phive stores the AAMP region with alignment padding, while AAMP's
        // own file_size excludes that padding.
        while (output.Count % 8 != 0)
            output.Add(0);
        return output.ToArray();
    }

    private static uint ApplyCountDelta(IReadOnlyList<byte> bytes, int offset, int delta)
    {
        var updated = checked((long)ReadUInt32(bytes, offset) + delta);
        if (updated is < 0 or > uint.MaxValue)
            throw new InvalidDataException("Merged AAMP counter would be outside UInt32 range.");
        return (uint)updated;
    }

    private sealed class AampArchive
    {
        private AampArchive(byte[] bytes, int rootOffset)
        {
            Bytes = bytes;
            RootOffset = rootOffset;
        }

        public byte[] Bytes { get; }
        public int RootOffset { get; }

        public static AampArchive Read(byte[] fileBytes, uint offset, uint size)
        {
            var archiveOffset = checked((int)offset);
            var archiveSize = checked((int)size);
            if (archiveSize < 0x30 || archiveOffset < 0 || archiveOffset > fileBytes.Length - archiveSize)
                throw new InvalidDataException("BPHCL AAMP range is invalid.");
            var wrapperBytes = fileBytes.AsSpan(archiveOffset, archiveSize);
            if (!wrapperBytes.Slice(0, 4).SequenceEqual("AAMP"u8))
                throw new InvalidDataException("BPHCL parameter archive is not AAMP.");
            var declaredSize = ReadUInt32(wrapperBytes.ToArray(), 0x0c);
            if (declaredSize < 0x30 || declaredSize > archiveSize)
                throw new InvalidDataException("BPHCL AAMP declared size exceeds its Phive wrapper.");
            var bytes = wrapperBytes.Slice(0, checked((int)declaredSize)).ToArray();
            var rootOffset = checked(0x30 + (int)ReadUInt32(bytes, 0x14));
            EnsureRange(bytes, rootOffset, 12);
            return new AampArchive(bytes, rootOffset);
        }

        public AampList FindList(uint listHash)
        {
            var visited = new HashSet<int>();
            return FindList(RootOffset, listHash, visited)
                ?? throw new InvalidDataException($"BPHCL AAMP has no required list {listHash}.");
        }

        private AampList? FindList(int offset, uint targetHash, ISet<int> visited)
        {
            if (!visited.Add(offset))
                return null;
            EnsureRange(Bytes, offset, 12);
            var hash = ReadUInt32(Bytes, offset);
            var childListFlags = ReadUInt32(Bytes, offset + 4);
            var childListOffset = checked(offset + (int)(childListFlags & 0xffff) * 4);
            var childListCount = checked((int)(childListFlags >> 16));
            EnsureRange(Bytes, childListOffset, checked(childListCount * 12));
            for (var index = 0; index < childListCount; index++)
            {
                var result = FindList(childListOffset + index * 12, targetHash, visited);
                if (result != null)
                    return result;
            }

            if (hash != targetHash)
                return null;

            var objectFlags = ReadUInt32(Bytes, offset + 8);
            var objectOffset = checked(offset + (int)(objectFlags & 0xffff) * 4);
            var objectCount = checked((int)(objectFlags >> 16));
            EnsureRange(Bytes, objectOffset, checked(objectCount * 8));
            var objects = Enumerable.Range(0, objectCount)
                .Select(index => ReadObject(objectOffset + index * 8))
                .ToArray();
            return new AampList(offset, offset + 8, objects);
        }

        private AampObject ReadObject(int offset)
        {
            var objectHash = ReadUInt32(Bytes, offset);
            var flags = ReadUInt32(Bytes, offset + 4);
            var parameterOffset = checked(offset + (int)(flags & 0xffff) * 4);
            var parameterCount = checked((int)(flags >> 16));
            EnsureRange(Bytes, parameterOffset, checked(parameterCount * 8));
            var parameters = new List<AampParameter>(parameterCount);
            for (var index = 0; index < parameterCount; index++)
            {
                var entryOffset = parameterOffset + index * 8;
                var nameHash = ReadUInt32(Bytes, entryOffset);
                var parameterFlags = ReadUInt32(Bytes, entryOffset + 4);
                var type = checked((byte)(parameterFlags >> 24));
                var valueOffset = checked(entryOffset + (int)(parameterFlags & 0x00ff_ffff) * 4);
                var size = GetValueSize(type, valueOffset);
                parameters.Add(new AampParameter(nameHash, type, Bytes.AsSpan(valueOffset, size).ToArray()));
            }

            var name = parameters.FirstOrDefault(parameter => parameter.NameHash == NameParameterHash)?.ReadString() ?? string.Empty;
            return new AampObject(objectHash, name, parameters);
        }

        private int GetValueSize(byte type, int offset) => type switch
        {
            0 or 1 or 2 or 17 => 4,
            3 => 8,
            4 => 12,
            5 or 6 or 16 => 16,
            7 => 32,
            8 => 64,
            15 => 256,
            StringReference => GetNullTerminatedSize(offset),
            _ => throw new InvalidDataException($"BPHCL AAMP cloth metadata uses unsupported parameter type {type}.")
        };

        private int GetNullTerminatedSize(int offset)
        {
            var end = offset;
            while (end < Bytes.Length && Bytes[end] != 0)
                end++;
            if (end == Bytes.Length)
                throw new InvalidDataException("BPHCL AAMP StringRef is not null terminated.");
            return end - offset + 1;
        }
    }

    private sealed record AampList(int Offset, int ObjectFlagsOffset, IReadOnlyList<AampObject> Objects);
    private sealed record AampObject(uint NameHash, string Name, IReadOnlyList<AampParameter> Parameters);
    private sealed record AampParameter(uint NameHash, byte Type, byte[] Value)
    {
        public string ReadString() => System.Text.Encoding.UTF8.GetString(Value).TrimEnd('\0');

        public byte[] CreateStringValue(string value)
        {
            var encoded = System.Text.Encoding.UTF8.GetBytes(value + "\0");
            return Type switch
            {
                StringReference => encoded,
                7 or 8 or 15 when encoded.Length <= Value.Length => encoded.Concat(new byte[Value.Length - encoded.Length]).ToArray(),
                7 or 8 or 15 => throw new InvalidDataException($"BPHCL AAMP name '{value}' exceeds the fixed {Value.Length}-byte string field."),
                _ => throw new InvalidDataException($"BPHCL AAMP Name parameter uses unsupported type {Type}.")
            };
        }
    }

    private static uint AllocateObjectHash(IEnumerable<AampObject> existing, string keyPrefix)
    {
        var used = existing.Select(item => item.NameHash).ToHashSet();
        for (var index = 0; index < 65_536; index++)
        {
            var hash = Crc32($"{keyPrefix}{index}");
            if (!used.Contains(hash))
                return hash;
        }

        throw new InvalidDataException("BPHCL AAMP has no available cloth_mesh object key.");
    }

    private static uint Crc32(string value)
    {
        var crc = 0xffff_ffffu;
        foreach (var valueByte in System.Text.Encoding.UTF8.GetBytes(value))
        {
            crc ^= valueByte;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb8_8320u);
        }
        return ~crc;
    }

    private static void Align(List<byte> bytes, int alignment)
    {
        while (bytes.Count % alignment != 0)
            bytes.Add(0);
    }

    private static uint ReadUInt32(IReadOnlyList<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(new[] { bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3] });

    private static void WriteUInt32(List<byte> bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static void EnsureRange(IReadOnlyList<byte> bytes, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > bytes.Count - length)
            throw new InvalidDataException("BPHCL AAMP pointer lies outside the archive.");
    }
}
