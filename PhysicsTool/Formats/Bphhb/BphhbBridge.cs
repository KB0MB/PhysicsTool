using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HKCLTool;

/// <summary>
/// Native, format-specific entry point for TotK helper-bone AAMP files.
/// BPHHB drives skeleton helper bones and is deliberately separate from BPHCL.
/// </summary>
public sealed class BphhbDocumentSummary
{
    internal byte[] Bytes { get; init; } = Array.Empty<byte>();
    public string SourcePath { get; init; } = string.Empty;
    public int FileSize { get; init; }
    public uint ArchiveVersion { get; init; }
    public uint ParameterIoVersion { get; init; }
    public uint ListCount { get; init; }
    public uint ObjectCount { get; init; }
    public uint ParameterCount { get; init; }
    public uint DataSize { get; init; }
    public uint StringPoolSize { get; init; }
    public IReadOnlyList<string> HelperBoneNames { get; init; } = Array.Empty<string>();
}

public static class BphhbBridge
{
    private const int HeaderSize = 0x30;

    public static BphhbDocumentSummary Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < HeaderSize)
            throw new InvalidDataException("BPHHB is shorter than an AAMP header.");
        if (Encoding.ASCII.GetString(bytes, 0, 4) != "AAMP")
            throw new InvalidDataException("BPHHB must begin with the AAMP signature.");

        var archiveVersion = ReadUInt32(bytes, 0x04);
        var declaredSize = ReadUInt32(bytes, 0x0C);
        var parameterIoVersion = ReadUInt32(bytes, 0x10);
        var parameterIoOffset = ReadUInt32(bytes, 0x14);
        var listCount = ReadUInt32(bytes, 0x18);
        var objectCount = ReadUInt32(bytes, 0x1C);
        var parameterCount = ReadUInt32(bytes, 0x20);
        var dataSize = ReadUInt32(bytes, 0x24);
        var stringPoolSize = ReadUInt32(bytes, 0x28);

        if (declaredSize != bytes.Length)
            throw new InvalidDataException($"AAMP header declares {declaredSize} bytes, but the file contains {bytes.Length} bytes.");
        if (HeaderSize + parameterIoOffset >= bytes.Length)
            throw new InvalidDataException("AAMP parameter root offset lies outside the file.");
        if (stringPoolSize > bytes.Length)
            throw new InvalidDataException("AAMP string pool lies outside the file.");

        var type = ReadZeroTerminatedUtf8(bytes, HeaderSize, Math.Min(bytes.Length, HeaderSize + (int)parameterIoOffset));
        if (!string.Equals(type, "phhb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected an AAMP helper-bone file (type phhb), found '{type}'.");

        return new BphhbDocumentSummary
        {
            Bytes = bytes,
            SourcePath = Path.GetFullPath(path),
            FileSize = bytes.Length,
            ArchiveVersion = archiveVersion,
            ParameterIoVersion = parameterIoVersion,
            ListCount = listCount,
            ObjectCount = objectCount,
            ParameterCount = parameterCount,
            DataSize = dataSize,
            StringPoolSize = stringPoolSize,
            HelperBoneNames = ReadStringPool(bytes, stringPoolSize)
        };
    }

    /// <summary>
    /// No AAMP fields have been modified yet, so an exact byte-preserving save
    /// is the only safe write path until the native AAMP serializer exists.
    /// </summary>
    public static BphhbDocumentSummary Save(BphhbDocumentSummary document, string outputPath)
    {
        if (!Path.GetExtension(outputPath).Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("BPHHB documents must be saved with the .bphhb extension.");

        File.WriteAllBytes(outputPath, document.Bytes);
        return Load(outputPath);
    }

    public static JObject ExportSummary(BphhbDocumentSummary summary) => new()
    {
        ["format"] = "BPHHBTool.Readable",
        ["version"] = 1,
        ["notes"] = "BPHHB helper-bone AAMP is currently opened and validated natively. Save is byte-preserving until the native AAMP writer is implemented.",
        ["bphhb"] = new JObject
        {
            ["sourcePath"] = summary.SourcePath,
            ["fileSize"] = summary.FileSize,
            ["archiveVersion"] = summary.ArchiveVersion,
            ["parameterIoVersion"] = summary.ParameterIoVersion,
            ["listCount"] = summary.ListCount,
            ["objectCount"] = summary.ObjectCount,
            ["parameterCount"] = summary.ParameterCount,
            ["dataSize"] = summary.DataSize,
            ["stringPoolSize"] = summary.StringPoolSize,
            ["helperBones"] = new JArray(summary.HelperBoneNames)
        }
    };

    private static uint ReadUInt32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);

    private static string ReadZeroTerminatedUtf8(byte[] bytes, int start, int endExclusive)
    {
        var end = start;
        while (end < endExclusive && bytes[end] != 0)
            end++;
        return Encoding.UTF8.GetString(bytes, start, end - start);
    }

    private static IReadOnlyList<string> ReadStringPool(byte[] bytes, uint stringPoolSize)
    {
        if (stringPoolSize == 0)
            return Array.Empty<string>();

        var start = checked(bytes.Length - (int)stringPoolSize);
        var values = new List<string>();
        var cursor = start;
        while (cursor < bytes.Length)
        {
            var next = cursor;
            while (next < bytes.Length && bytes[next] != 0)
                next++;

            if (next > cursor)
            {
                var value = Encoding.UTF8.GetString(bytes, cursor, next - cursor);
                if (value.All(character => !char.IsControl(character)))
                    values.Add(value);
            }

            cursor = next + 1;
        }

        return values.Distinct(StringComparer.Ordinal).ToArray();
    }
}
