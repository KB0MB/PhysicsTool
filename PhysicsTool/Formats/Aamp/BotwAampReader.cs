using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace HKCLTool;

/// <summary>
/// Read-only AAMP tree decoder used by the helper-bone conversion pipeline.
/// It covers the parameter shapes observed in BPHHB/BPHYSSB and keeps unknown
/// hashes intact so the higher-level converter can make deliberate choices.
/// </summary>
internal static class BotwAampReader
{
    public static AampReadArchive Read(string path) => Read(File.ReadAllBytes(path));

    public static AampReadArchive Read(byte[] bytes)
    {
        if (bytes.Length < 0x40 || !bytes.AsSpan(0, 4).SequenceEqual("AAMP"u8))
            throw new InvalidDataException("The file is not a complete AAMP archive.");

        var declaredSize = ReadUInt32(bytes, 0x0c);
        if (declaredSize != bytes.Length)
            throw new InvalidDataException($"AAMP declares {declaredSize} bytes but the file contains {bytes.Length} bytes.");

        var type = ReadString(bytes, 0x30);
        var rootOffset = checked(0x30 + (int)ReadUInt32(bytes, 0x14));
        var lists = ReadList(bytes, rootOffset, new HashSet<int>());
        return new AampReadArchive(type, lists);
    }

    private static AampReadList ReadList(byte[] bytes, int offset, ISet<int> visited)
    {
        EnsureRange(bytes, offset, 12);
        if (!visited.Add(offset))
            throw new InvalidDataException("AAMP list graph contains a cycle.");

        var hash = ReadUInt32(bytes, offset);
        var childFlags = ReadUInt32(bytes, offset + 4);
        var objectFlags = ReadUInt32(bytes, offset + 8);
        var result = new AampReadList(hash);

        foreach (var childOffset in GetRelativeOffsets(bytes, offset, childFlags, 12))
            result.Children.Add(ReadList(bytes, childOffset, visited));
        foreach (var objectOffset in GetRelativeOffsets(bytes, offset, objectFlags, 8))
            result.Objects.Add(ReadObject(bytes, objectOffset));

        visited.Remove(offset);
        return result;
    }

    private static AampReadObject ReadObject(byte[] bytes, int offset)
    {
        EnsureRange(bytes, offset, 8);
        var hash = ReadUInt32(bytes, offset);
        var flags = ReadUInt32(bytes, offset + 4);
        var result = new AampReadObject(hash);
        foreach (var parameterOffset in GetRelativeOffsets(bytes, offset, flags, 8))
            result.Parameters.Add(ReadParameter(bytes, parameterOffset));
        return result;
    }

    private static AampReadParameter ReadParameter(byte[] bytes, int offset)
    {
        EnsureRange(bytes, offset, 8);
        var hash = ReadUInt32(bytes, offset);
        var flags = ReadUInt32(bytes, offset + 4);
        var type = checked((byte)(flags >> 24));
        var valueOffset = checked(offset + (int)(flags & 0x00ff_ffff) * 4);
        var valueSize = GetValueSize(bytes, valueOffset, type);
        EnsureRange(bytes, valueOffset, valueSize);
        return new AampReadParameter(hash, type, bytes.AsSpan(valueOffset, valueSize).ToArray());
    }

    private static IEnumerable<int> GetRelativeOffsets(byte[] bytes, int sourceOffset, uint flags, int stride)
    {
        var count = checked((int)(flags >> 16));
        if (count == 0)
            return Array.Empty<int>();
        var offset = checked(sourceOffset + (int)(flags & 0xffff) * 4);
        EnsureRange(bytes, offset, checked(count * stride));
        return Enumerable.Range(0, count).Select(index => offset + index * stride).ToArray();
    }

    private static int GetValueSize(byte[] bytes, int offset, byte type) => type switch
    {
        0 or 1 or 2 or 17 => 4,
        3 => 8,
        4 => 12,
        5 or 6 or 16 => 16,
        7 => GetNullTerminatedSize(bytes, offset),
        8 => GetNullTerminatedSize(bytes, offset),
        15 => GetNullTerminatedSize(bytes, offset),
        20 => GetNullTerminatedSize(bytes, offset),
        _ => throw new InvalidDataException($"AAMP parameter type {type} is not supported by the helper-bone reader.")
    };

    private static int GetNullTerminatedSize(byte[] bytes, int offset)
    {
        var end = offset;
        while (end < bytes.Length && bytes[end] != 0)
            end++;
        if (end == bytes.Length)
            throw new InvalidDataException("AAMP string parameter is not null terminated.");
        return end - offset + 1;
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        EnsureRange(bytes, offset, 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    }

    private static string ReadString(byte[] bytes, int offset)
    {
        var end = offset;
        while (end < bytes.Length && bytes[end] != 0)
            end++;
        return Encoding.UTF8.GetString(bytes, offset, end - offset);
    }

    private static void EnsureRange(byte[] bytes, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidDataException("An AAMP offset lies outside the file.");
    }
}

internal sealed class AampReadArchive(string type, AampReadList root)
{
    public string Type { get; } = type;
    public AampReadList Root { get; } = root;
}

internal sealed class AampReadList(uint hash)
{
    public uint Hash { get; } = hash;
    public List<AampReadList> Children { get; } = new();
    public List<AampReadObject> Objects { get; } = new();

    public AampReadList? FindChild(string name) => Children.FirstOrDefault(child => child.Hash == BotwAampWriter.Crc32(name));
    public AampReadList? FindChild(uint hash) => Children.FirstOrDefault(child => child.Hash == hash);
}

internal sealed class AampReadObject(uint hash)
{
    public uint Hash { get; } = hash;
    public List<AampReadParameter> Parameters { get; } = new();

    public AampReadParameter? Find(string name) => Parameters.FirstOrDefault(parameter => parameter.Hash == BotwAampWriter.Crc32(name));
    public AampReadParameter? Find(uint hash) => Parameters.FirstOrDefault(parameter => parameter.Hash == hash);
}

internal sealed class AampReadParameter(uint hash, byte type, byte[] bytes)
{
    public uint Hash { get; } = hash;
    public byte Type { get; } = type;
    public byte[] Bytes { get; } = bytes;

    public int AsInt32() => BitConverter.ToInt32(Bytes, 0);
    public float AsSingle() => BitConverter.ToSingle(Bytes, 0);
    public bool AsBoolean() => BitConverter.ToInt32(Bytes, 0) != 0;
    public Vector3 AsVector3() => new(BitConverter.ToSingle(Bytes, 0), BitConverter.ToSingle(Bytes, 4), BitConverter.ToSingle(Bytes, 8));
    public Vector4 AsVector4() => new(BitConverter.ToSingle(Bytes, 0), BitConverter.ToSingle(Bytes, 4), BitConverter.ToSingle(Bytes, 8), BitConverter.ToSingle(Bytes, 12));
    public string AsString() => Encoding.UTF8.GetString(Bytes).TrimEnd('\0');
}
