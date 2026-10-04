using System.Runtime.InteropServices;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using Toolbox.Library;
using WiiTexture = Syroot.NintenTools.Bfres.Texture;
using NxTexture = Syroot.NintenTools.NSW.Bntx.Texture;

namespace HKCLTool;

internal static class ToolboxActorTexture
{
    private static readonly object Gx2Gate = new();
    private sealed record SurfaceFormat(CompressionFormat? Compression, int Bytes, int Block);
    private static SurfaceFormat Format(string name)
    {
        var n = name.Replace("_", "").ToUpperInvariant();
        if (n.Contains("SNORM")) throw new NotSupportedException("Signed texture format " + name);
        if (n.Contains("BC1")) return new(CompressionFormat.Bc1WithAlpha, 8, 4);
        if (n.Contains("BC2")) return new(CompressionFormat.Bc2, 16, 4);
        if (n.Contains("BC3")) return new(CompressionFormat.Bc3, 16, 4);
        if (n.Contains("BC4")) return new(CompressionFormat.Bc4, 8, 4);
        if (n.Contains("BC5")) return new(CompressionFormat.Bc5, 16, 4);
        if (n.Contains("BC7")) return new(CompressionFormat.Bc7, 16, 4);
        if (n.Contains("R8G8B8A8")) return new(null, 4, 1);
        if (n.Contains("R8G8")) return new(null, 2, 1);
        if (n.Contains("R8")) return new(null, 1, 1);
        throw new NotSupportedException("Texture format " + name);
    }
    private static void Validate(int width, int height)
    {
        if (width is < 1 or > 16384 || height is < 1 or > 16384 || (long)width * height > 64 * 1024 * 1024)
            throw new InvalidDataException("The texture dimensions exceed the viewer limit.");
    }
    public static VisualModelTexture Decode(WiiTexture tex)
    {
        int width = checked((int)tex.Width), height = checked((int)tex.Height); Validate(width, height);
        var format = Format(tex.Format.ToString());
        var surface = new GX2.GX2Surface { width = tex.Width, height = tex.Height, depth = tex.Depth, dim = (uint)tex.Dim,
            format = (uint)tex.Format, aa = (uint)tex.AAMode, use = (uint)tex.Use, pitch = tex.Pitch, data = tex.Data, mipData = tex.MipData,
            numMips = 1, mipOffset = tex.MipOffsets, tileMode = (uint)tex.TileMode, swizzle = tex.Swizzle, numArray = tex.ArrayLength };
        byte[] raw;
        // Toolbox's GX2 address library has static scratch state.
        lock (Gx2Gate) raw = GX2.Decode(surface, 0, 0);
        var pixels = DecodeSurface(raw, width, height, format);
        SwizzleChannels(pixels, [(int)tex.CompSelR, (int)tex.CompSelG, (int)tex.CompSelB, (int)tex.CompSelA], false);
        return Texture(tex.Name, width, height, pixels);
    }
    public static VisualModelTexture Decode(NxTexture tex)
    {
        int width = checked((int)tex.Width), height = checked((int)tex.Height); Validate(width, height);
        var format = Format(tex.Format.ToString());
        int w = (width + format.Block - 1) / format.Block, h = (height + format.Block - 1) / format.Block;
        var raw = Deswizzle(tex.TextureData[0][0], w, h, format.Bytes, (int)tex.TileMode, (int)tex.BlockHeightLog2);
        var pixels = DecodeSurface(raw, width, height, format);
        SwizzleChannels(pixels, [(int)tex.ChannelRed, (int)tex.ChannelGreen, (int)tex.ChannelBlue, (int)tex.ChannelAlpha], true);
        return Texture(tex.Name, width, height, pixels);
    }
    internal static byte[] Deswizzle(byte[] source, int width, int height, int bytes, int tileMode, int log2)
    {
        if (width <= 0 || height <= 0 || bytes <= 0 || log2 is < 0 or > 5 || tileMode is < 0 or > 1)
            throw new InvalidDataException("Invalid Tegra surface layout.");
        var result = new byte[checked(width * height * bytes)];
        var blockHeight = 1 << log2;
        while (blockHeight > 1 && height <= blockHeight * 4) blockHeight >>= 1;
        var gobs = (width * bytes + 63) / 64;
        // Tegra X1 TRM block-linear GOB addressing, also used by tegra_swizzle.
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var xb = x * bytes;
                var offset = tileMode == 1 ? y * ((width * bytes + 31) / 32 * 32) + xb
                    : y / (8 * blockHeight) * 512 * blockHeight * gobs + xb / 64 * 512 * blockHeight +
                      y % (8 * blockHeight) / 8 * 512 + xb % 64 / 32 * 256 + y % 8 / 2 * 64 +
                      xb % 32 / 16 * 32 + y % 2 * 16 + xb % 16;
                if (offset < 0 || offset > source.Length - bytes) throw new InvalidDataException("The tiled texture surface is truncated.");
                source.AsSpan(offset, bytes).CopyTo(result.AsSpan((y * width + x) * bytes, bytes));
            }
        return result;
    }
    private static byte[] DecodeSurface(byte[] raw, int width, int height, SurfaceFormat format)
    {
        var required = checked(((width + format.Block - 1) / format.Block) * ((height + format.Block - 1) / format.Block) * format.Bytes);
        if (raw.Length < required) throw new InvalidDataException("The texture surface is truncated.");
        if (format.Compression is { } compression)
        {
            var colors = new BcDecoder().DecodeRaw(raw.AsSpan(0, required).ToArray(), width, height, compression);
            return MemoryMarshal.AsBytes(colors.AsSpan()).ToArray();
        }
        var pixels = new byte[checked(width * height * 4)];
        for (var i = 0; i < width * height; i++)
        {
            raw.AsSpan(i * format.Bytes, format.Bytes).CopyTo(pixels.AsSpan(i * 4, format.Bytes));
            if (format.Bytes < 4) pixels[i * 4 + 3] = 255;
        }
        return pixels;
    }
    private static void SwizzleChannels(byte[] pixels, int[] channels, bool nx)
    {
        Span<byte> rgba = stackalloc byte[4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels.AsSpan(i, 4).CopyTo(rgba);
            for (int j = 0; j < 4; j++)
            {
                var c = channels[j];
                pixels[i + j] = (byte)(nx ? c switch { 0 => 0, 1 => 255, >= 2 and <= 5 => rgba[c - 2], _ => rgba[j] }
                    : c switch { >= 0 and <= 3 => rgba[c], 4 => 0, 5 => 255, _ => rgba[j] });
            }
        }
    }
    private static VisualModelTexture Texture(string name, int width, int height, byte[] pixels) => new()
    {
        Name = name, SourcePath = name, Width = width, Height = height, Pixels = pixels,
        AlphaMode = pixels.Where((_, index) => index % 4 == 3).Any(a => a < 250) ? VisualModelAlphaMode.Cutout : VisualModelAlphaMode.Opaque
    };
}
