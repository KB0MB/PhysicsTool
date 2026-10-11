using System.Runtime.InteropServices;
using HKCLTool;

internal static class TegraLayoutChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong BaseHeight(ulong height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void Deswizzle(ulong width, ulong height, ulong depth,
        byte* source, ulong sourceLength, byte* destination, ulong destinationLength,
        ulong blockHeight, ulong bytesPerPixel);

    // Optional independent oracle; no native dependency is added to the application.
    public static unsafe void Native(string libraryPath)
    {
        var library = NativeLibrary.Load(Path.GetFullPath(libraryPath));
        try
        {
            var height = Marshal.GetDelegateForFunctionPointer<BaseHeight>(NativeLibrary.GetExport(library, "block_height_mip0"));
            var deswizzle = Marshal.GetDelegateForFunctionPointer<Deswizzle>(NativeLibrary.GetExport(library, "deswizzle_block_linear"));
            for (int h = 1; h <= 4096; h++)
                if (TexToGoTexture.BaseBlockHeight(h) != (int)height((ulong)h)) throw new Exception($"Native GOB height mismatch at {h}");
            int cases = 0;
            var random = new Random(7319);
            foreach (int h in new[] { 1, 9, 10, 11, 16, 21, 22, 42, 43, 85, 86, 128, 170, 256, 512 })
            foreach (int w in new[] { 1, 17, 128 })
            foreach (int bytes in new[] { 8, 16 })
            {
                int bh = (int)height((ulong)h);
                var tiled = new byte[((w * bytes + 63) / 64) * 512 * bh * ((h + 8 * bh - 1) / (8 * bh))];
                random.NextBytes(tiled);
                var expected = new byte[w * h * bytes];
                fixed (byte* src = tiled, dst = expected)
                    deswizzle((ulong)w, (ulong)h, 1, src, (ulong)tiled.Length, dst, (ulong)expected.Length, (ulong)bh, (ulong)bytes);
                var actual = ToolboxActorTexture.Deswizzle(tiled, w, h, bytes, 0, System.Numerics.BitOperations.Log2((uint)TexToGoTexture.BaseBlockHeight(h)));
                if (!actual.SequenceEqual(expected)) throw new Exception($"Native texture layout mismatch at {w}x{h}, {bytes} bytes/block");
                cases++;
            }
            Console.WriteLine($"PASS: 4096 inferred GOB heights and {cases} byte-exact layouts match Toolbox's native tegra_swizzle.");
        }
        finally { NativeLibrary.Free(library); }
    }
}
