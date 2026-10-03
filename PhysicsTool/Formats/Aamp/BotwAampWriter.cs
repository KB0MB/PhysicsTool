using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace HKCLTool;

/// <summary>
/// Small native writer for the BotW-flavoured AAMP layouts PhysicsTool creates.
/// It intentionally supports the scalar, vector, and string parameter types we
/// can verify from the supplied BPHYSICS samples instead of pretending to be a
/// complete AAMP implementation.
/// </summary>
internal static class BotwAampWriter
{
    private const int HeaderSize = 0x30;
    private const byte Bool = 0;
    private const byte Float = 1;
    private const byte Int = 2;
    private const byte Vector3 = 4;
    private const byte Vector4 = 5;
    private const byte String256 = 15;
    private const byte StringReference = 20;

    public static byte[] WriteXml(AampListNode root) => WriteArchive("xml", root);

    // Used by BPHHB to rebuild an existing AAMP graph while retaining the
    // source archive type, parameter hashes, and raw parameter payloads.
    public static byte[] WriteArchive(
        string archiveType,
        AampListNode root,
        uint archiveVersion = 2,
        uint formatVersion = 3,
        uint parameterIoVersion = 0)
    {
        var lists = new List<AampListNode>();
        CollectLists(root, lists);
        var objects = lists.SelectMany(list => list.Objects).ToList();
        var parameters = objects.SelectMany(item => item.Parameters).ToList();

        var bytes = new List<byte>(1024);
        AddZeroes(bytes, HeaderSize);
        bytes.AddRange(Encoding.UTF8.GetBytes(archiveType));
        bytes.Add(0);

        foreach (var list in lists)
        {
            Align(bytes, 4);
            list.Offset = bytes.Count;
            AddZeroes(bytes, 12);
        }

        foreach (var item in objects)
        {
            Align(bytes, 4);
            item.Offset = bytes.Count;
            AddZeroes(bytes, 8);
        }

        foreach (var parameter in parameters)
        {
            Align(bytes, 4);
            parameter.Offset = bytes.Count;
            AddZeroes(bytes, 8);
        }

        var dataStart = bytes.Count;
        foreach (var parameter in parameters.Where(parameter => !parameter.IsString))
        {
            Align(bytes, 4);
            parameter.ValueOffset = bytes.Count;
            bytes.AddRange(parameter.ToBinary());
        }

        var dataSize = bytes.Count - dataStart;
        foreach (var parameter in parameters.Where(parameter => parameter.IsString))
        {
            Align(bytes, 4);
            parameter.ValueOffset = bytes.Count;
            bytes.AddRange(parameter.RawBytes ?? Encoding.UTF8.GetBytes((parameter.StringValue ?? string.Empty) + "\0"));
        }

        var stringPoolSize = bytes.Count - dataStart - dataSize;
        PatchHeader(bytes, archiveType, root.Offset, lists.Count, objects.Count, parameters.Count,
            dataSize, stringPoolSize, archiveVersion, formatVersion, parameterIoVersion);

        foreach (var list in lists)
        {
            WriteUInt32(bytes, list.Offset, list.NameHash ?? Crc32(list.Name));
            WriteRelativeCollection(bytes, list.Offset + 4, list.Children.Select(child => child.Offset), list.Offset);
            WriteRelativeCollection(bytes, list.Offset + 8, list.Objects.Select(item => item.Offset), list.Offset);
        }

        foreach (var item in objects)
        {
            WriteUInt32(bytes, item.Offset, item.NameHash ?? Crc32(item.Name));
            WriteRelativeCollection(bytes, item.Offset + 4, item.Parameters.Select(parameter => parameter.Offset), item.Offset);
        }

        foreach (var parameter in parameters)
        {
            WriteUInt32(bytes, parameter.Offset, parameter.NameHash ?? Crc32(parameter.Name));
            var relativeWords = checked((parameter.ValueOffset - parameter.Offset) / 4);
            if (relativeWords is < 0 or > 0x00ff_ffff)
                throw new InvalidDataException($"AAMP parameter '{parameter.Name}' points outside the supported range.");
            WriteUInt32(bytes, parameter.Offset + 4, ((uint)parameter.Type << 24) | (uint)relativeWords);
        }

        return bytes.ToArray();
    }

    private static void PatchHeader(
        List<byte> bytes,
        string archiveType,
        int rootOffset,
        int listCount,
        int objectCount,
        int parameterCount,
        int dataSize,
        int stringPoolSize,
        uint archiveVersion,
        uint formatVersion,
        uint parameterIoVersion)
    {
        "AAMP"u8.CopyTo(CollectionsMarshal.AsSpan(bytes));
        WriteUInt32(bytes, 0x04, archiveVersion);
        WriteUInt32(bytes, 0x08, formatVersion);
        WriteUInt32(bytes, 0x0c, checked((uint)bytes.Count));
        WriteUInt32(bytes, 0x10, parameterIoVersion);
        WriteUInt32(bytes, 0x14, checked((uint)(rootOffset - HeaderSize)));
        WriteUInt32(bytes, 0x18, checked((uint)listCount));
        WriteUInt32(bytes, 0x1c, checked((uint)objectCount));
        WriteUInt32(bytes, 0x20, checked((uint)parameterCount));
        WriteUInt32(bytes, 0x24, checked((uint)dataSize));
        WriteUInt32(bytes, 0x28, checked((uint)stringPoolSize));
        WriteUInt32(bytes, 0x2c, 0);
    }

    private static void WriteRelativeCollection(List<byte> bytes, int flagsOffset, IEnumerable<int> targets, int sourceOffset)
    {
        var values = targets.ToArray();
        if (values.Length == 0)
        {
            WriteUInt32(bytes, flagsOffset, 0);
            return;
        }

        var relativeWords = checked((values[0] - sourceOffset) / 4);
        if (relativeWords is < 0 or > ushort.MaxValue || values.Length > ushort.MaxValue)
            throw new InvalidDataException("AAMP collection exceeds the supported 16-bit layout range.");
        WriteUInt32(bytes, flagsOffset, ((uint)values.Length << 16) | (uint)relativeWords);
    }

    private static void CollectLists(AampListNode list, ICollection<AampListNode> result)
    {
        result.Add(list);
        foreach (var child in list.Children)
            CollectLists(child, result);
    }

    private static void AddZeroes(List<byte> bytes, int count)
    {
        for (var index = 0; index < count; index++)
            bytes.Add(0);
    }

    private static void Align(List<byte> bytes, int alignment)
    {
        while (bytes.Count % alignment != 0)
            bytes.Add(0);
    }

    private static void WriteUInt32(List<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(offset, 4), value);

    internal static uint Crc32(string value)
    {
        var crc = 0xffff_ffffu;
        foreach (var valueByte in Encoding.UTF8.GetBytes(value))
        {
            crc ^= valueByte;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb8_8320u);
        }
        return ~crc;
    }

    internal sealed class AampListNode(string name, uint? nameHash = null)
    {
        public string Name { get; } = name;
        public uint? NameHash { get; } = nameHash;
        public List<AampListNode> Children { get; } = new();
        public List<AampObjectNode> Objects { get; } = new();
        internal int Offset { get; set; }
    }

    internal sealed class AampObjectNode(string name, uint? nameHash = null)
    {
        public string Name { get; } = name;
        public uint? NameHash { get; } = nameHash;
        public List<AampParameterNode> Parameters { get; } = new();
        internal int Offset { get; set; }
    }

    internal sealed class AampParameterNode
    {
        private AampParameterNode(string name, byte type, object? value, uint? nameHash = null, byte[]? rawBytes = null)
        {
            Name = name;
            Type = type;
            Value = value;
            NameHash = nameHash;
            RawBytes = rawBytes;
        }

        public string Name { get; }
        public byte Type { get; }
        public object? Value { get; }
        public uint? NameHash { get; }
        public byte[]? RawBytes { get; }
        public bool IsString => Type is 7 or 8 or String256 or StringReference;
        public string? StringValue => Value as string;
        internal int Offset { get; set; }
        internal int ValueOffset { get; set; }

        public static AampParameterNode Integer(string name, int value) => new(name, Int, value);
        public static AampParameterNode Single(string name, float value) => new(name, Float, value);
        public static AampParameterNode Boolean(string name, bool value) => new(name, Bool, value);
        public static AampParameterNode Vector3(string name, Vector3 value) => new(name, BotwAampWriter.Vector3, value);
        public static AampParameterNode Vector4(string name, Vector4 value) => new(name, BotwAampWriter.Vector4, value);
        public static AampParameterNode String256Value(string name, string value) => new(name, String256, value);
        public static AampParameterNode StringReferenceValue(string name, string value) => new(name, StringReference, value);
        public static AampParameterNode Raw(uint nameHash, byte type, byte[] bytes) =>
            new(string.Empty, type, null, nameHash, bytes.ToArray());

        internal byte[] ToBinary() => Type switch
        {
            _ when RawBytes != null => RawBytes,
            Int => BitConverter.GetBytes((int)Value!),
            Float => BitConverter.GetBytes((float)Value!),
            Bool => BitConverter.GetBytes((bool)Value! ? 1 : 0),
            BotwAampWriter.Vector3 => SerializeVector3((Vector3)Value!),
            BotwAampWriter.Vector4 => SerializeVector4((Vector4)Value!),
            _ => throw new InvalidOperationException($"AAMP parameter '{Name}' is not a binary scalar value.")
        };

        private static byte[] SerializeVector3(Vector3 value)
        {
            var result = new byte[12];
            BitConverter.GetBytes(value.X).CopyTo(result, 0);
            BitConverter.GetBytes(value.Y).CopyTo(result, 4);
            BitConverter.GetBytes(value.Z).CopyTo(result, 8);
            return result;
        }

        private static byte[] SerializeVector4(Vector4 value)
        {
            var result = new byte[16];
            BitConverter.GetBytes(value.X).CopyTo(result, 0);
            BitConverter.GetBytes(value.Y).CopyTo(result, 4);
            BitConverter.GetBytes(value.Z).CopyTo(result, 8);
            BitConverter.GetBytes(value.W).CopyTo(result, 12);
            return result;
        }
    }
}
