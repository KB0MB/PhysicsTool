using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HKCLTool;

internal static class TotkMeshCodec
{
    public static byte[] Decode(byte[] source)
    {
        if (source.Length < 13 || source.Length > 256 * 1024 * 1024 || !source.AsSpan().StartsWith("MCPK"u8))
            throw new InvalidDataException("Invalid MeshCodec package header.");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(8));
        var size = (ulong)(flags >> 5) << (int)(flags & 15);
        if (size < 32 || size > 512 * 1024 * 1024) throw new InvalidDataException("Invalid MeshCodec decoded size.");
        return Run(source, (int)size, false);
    }

    internal static byte[] DecodeChunk(byte[] source, uint caveId, int expectedSize)
    {
        if (source.Length < 28 || source.Length > 64 * 1024 * 1024 || BinaryPrimitives.ReadUInt32LittleEndian(source) != caveId)
            throw new InvalidDataException("Invalid cave page header.");
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(12));
        uint work = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(16));
        ulong buffers = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(4)) + BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(8));
        if (size != expectedSize || size == 0 || size > 64 * 1024 * 1024 || buffers > size || work > 256 * 1024 * 1024)
            throw new InvalidDataException("Invalid cave page buffer sizes.");
        return Run(source, (int)size, true);
    }

    private static byte[] Run(byte[] source, int size, bool chunk)
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var rid = os + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var executable = Path.Combine(AppContext.BaseDirectory, "Tools", "MeshCodec", rid,
            "PhysicsTool.MeshCodec" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        if (!File.Exists(executable)) throw new NotSupportedException($"The MeshCodec helper for {rid} is not installed. Use a decoded model folder or a build containing that helper.");
        using var process = new Process { StartInfo = new(executable) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        if (chunk) process.StartInfo.ArgumentList.Add("--chunk");
        try
        {
            process.Start();
            var output = new byte[(int)size];
            var error = process.StandardError.ReadToEndAsync();
            async Task Transfer()
            {
                var read = process.StandardOutput.BaseStream.ReadExactlyAsync(output).AsTask();
                await process.StandardInput.BaseStream.WriteAsync(source);
                process.StandardInput.Close();
                await read;
                await process.WaitForExitAsync();
                await error;
                if (process.ExitCode != 0 || (!chunk && !output.AsSpan().StartsWith("FRES"u8)))
                    throw new InvalidDataException("MeshCodec did not produce valid decoded data.");
            }
            Transfer().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            return output;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or System.ComponentModel.Win32Exception)
        { throw new InvalidDataException("MeshCodec could not decode this " + (chunk ? "cave page." : "model."), ex); }
        finally
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); } }
            catch (InvalidOperationException) { }
        }
    }
}
