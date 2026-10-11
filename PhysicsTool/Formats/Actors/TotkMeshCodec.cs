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
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var rid = os + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var executable = Path.Combine(AppContext.BaseDirectory, "Tools", "MeshCodec", rid,
            "PhysicsTool.MeshCodec" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        if (!File.Exists(executable)) throw new NotSupportedException($"The MeshCodec helper for {rid} is not installed. Use a decoded model folder or a build containing that helper.");
        using var process = new Process { StartInfo = new(executable) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } };
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
                if (process.ExitCode != 0 || !output.AsSpan().StartsWith("FRES"u8))
                    throw new InvalidDataException("MeshCodec did not produce a valid BFRES model.");
            }
            Transfer().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            return output;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or System.ComponentModel.Win32Exception)
        { throw new InvalidDataException("MeshCodec could not decode this model. The actor was not loaded.", ex); }
        finally
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); } }
            catch (InvalidOperationException) { }
        }
    }
}
