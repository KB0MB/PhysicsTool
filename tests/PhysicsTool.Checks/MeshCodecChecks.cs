using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using HKCLTool;
using ZstdSharp;

internal static class MeshCodecChecks
{
    public static void Run()
    {
        var expected = new byte[64]; "FRES"u8.CopyTo(expected);
        using var compressor = new Compressor();
        var compressed = compressor.Wrap(expected).ToArray();
        var package = new byte[12 + compressed.Length - 4]; "MCPK"u8.CopyTo(package);
        package[4] = package[5] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(package.AsSpan(8), (uint)expected.Length << 5);
        compressed.AsSpan(4).CopyTo(package.AsSpan(12));
        if (!TotkMeshCodec.Decode(package).SequenceEqual(expected)) throw new Exception("MeshCodec helper changed decoded bytes");
        var roundedData = new byte[63]; "FRES"u8.CopyTo(roundedData);
        BinaryPrimitives.WriteUInt32LittleEndian(roundedData.AsSpan(28), 63);
        var roundedFrame = compressor.Wrap(roundedData).ToArray();
        var rounded = new byte[12 + roundedFrame.Length - 4]; "MCPK"u8.CopyTo(rounded);
        rounded[4] = rounded[5] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(rounded.AsSpan(8), (4u << 5) | 4);
        roundedFrame.AsSpan(4).CopyTo(rounded.AsSpan(12));
        var roundedOutput = TotkMeshCodec.Decode(rounded);
        if (roundedOutput.Length != 64 || !roundedOutput.AsSpan(0,63).SequenceEqual(roundedData) || roundedOutput[63] != 0)
            throw new Exception("Valid rounded mod package was not preserved");
        var wrongVersion = (byte[])package.Clone(); wrongVersion[5] = 2;
        var wrappedSize = (byte[])package.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(wrappedSize.AsSpan(8), uint.MaxValue);
        var tooLong = (byte[])package.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(tooLong.AsSpan(8), 128u << 5);
        var malformed = new List<byte[]> { new byte[8], wrongVersion, wrappedSize, tooLong };
        BinaryPrimitives.WriteUInt32LittleEndian(roundedData.AsSpan(28), 62);
        var incompleteFrame = compressor.Wrap(roundedData).ToArray();
        var incomplete = new byte[12 + incompleteFrame.Length - 4];
        rounded.AsSpan(0,12).CopyTo(incomplete);
        incompleteFrame.AsSpan(4).CopyTo(incomplete.AsSpan(12));
        malformed.Add(incomplete);
        for (var length = 12; length < package.Length; length++) malformed.Add(package[..length]);
        // The BFRES requests a mesh section, but its trailing FMSH header is absent or invalid.
        var header = new byte[256]; "FRES"u8.CopyTo(header); header[0xee] = 8;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x1c), 256);
        var frame = compressor.Wrap(header).ToArray();
        var missingMesh = new byte[12 + frame.Length - 4]; "MCPK"u8.CopyTo(missingMesh);
        BinaryPrimitives.WriteUInt32LittleEndian(missingMesh.AsSpan(8), 1024u << 5);
        frame.AsSpan(4).CopyTo(missingMesh.AsSpan(12));
        malformed.Add(missingMesh);
        var badAlignment = new byte[((missingMesh.Length + 3) & ~3) + 36];
        missingMesh.CopyTo(badAlignment, 0);
        "FMSH"u8.CopyTo(badAlignment.AsSpan((missingMesh.Length + 3) & ~3));
        malformed.Add(badAlignment);
        foreach (var invalid in malformed)
        {
            try { TotkMeshCodec.Decode(invalid); throw new Exception("Invalid MeshCodec data accepted"); }
            catch (InvalidDataException) { }
        }
        if (!TotkMeshCodec.Decode(package).SequenceEqual(expected)) throw new Exception("Decoder did not recover after malformed input");
        Console.WriteLine("PASS: bundled MeshCodec process round-trip, malformed input isolation and recovery.");
    }

    public static void Fixtures(string[] paths)
    {
        var baseline = Environment.GetEnvironmentVariable("PHYSICSTOOL_MESHCODEC_BASELINE");
        foreach (var path in paths)
        {
            var source = File.ReadAllBytes(path);
            var decoded = TotkMeshCodec.Decode(source);
            if (!string.IsNullOrEmpty(baseline))
            {
                using var process = new Process { StartInfo = new(baseline) {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true } };
                process.Start();
                try
                {
                    async Task<byte[]> ReadBaseline()
                    {
                        using var output = new MemoryStream();
                        var read = process.StandardOutput.BaseStream.CopyToAsync(output);
                        var error = process.StandardError.ReadToEndAsync();
                        await process.StandardInput.BaseStream.WriteAsync(source);
                        process.StandardInput.Close();
                        await read; await process.WaitForExitAsync(); await error;
                        if (process.ExitCode != 0) throw new Exception("Baseline decoder failed.");
                        return output.ToArray();
                    }
                    var previous = ReadBaseline().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
                    if (!previous.SequenceEqual(decoded)) throw new Exception($"Decoded bytes differ: {Path.GetFileName(path)}");
                }
                finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } }
            }
            Console.WriteLine($"PASS: {Path.GetFileName(path)} ({decoded.Length} bytes, SHA256 {Convert.ToHexString(SHA256.HashData(decoded))})");
        }
    }
}
