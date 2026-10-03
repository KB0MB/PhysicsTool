using System.Runtime.InteropServices;
using DirectXTexNet;
using Toolbox.Library;
using WiiTexture = Syroot.NintenTools.Bfres.Texture;
using NxTexture = Syroot.NintenTools.NSW.Bntx.Texture;

namespace HKCLTool;

internal static class ToolboxActorTexture
{
    // Same tegra_swizzle entry point used by Toolbox; only the base surface is needed here.
    [DllImport("tegra_swizzle_x64", EntryPoint = "deswizzle_block_linear")]
    private static extern void Deswizzle(ulong width, ulong height, ulong depth, byte[] source, ulong sourceLength,
        byte[] destination, ulong destinationLength, ulong blockHeight, ulong bytesPerPixel);

    public static VisualModelTexture Decode(WiiTexture tex)
    {
        Validate((int)tex.Width, (int)tex.Height);
        var format = Format(tex.Format.ToString());
        var surface = new GX2.GX2Surface { width = tex.Width, height = tex.Height, depth = tex.Depth, dim = (uint)tex.Dim,
            format = (uint)tex.Format, aa = (uint)tex.AAMode, use = (uint)tex.Use, pitch = tex.Pitch, data = tex.Data, mipData = tex.MipData,
            numMips = 1, mipOffset = tex.MipOffsets, tileMode = (uint)tex.TileMode, swizzle = tex.Swizzle, numArray = tex.ArrayLength };
        var raw = GX2.Decode(surface, 0, 0);
        var pixels = DecodeSurface(raw, (int)tex.Width, (int)tex.Height, format);
        SwizzleChannels(pixels, new[] { (int)tex.CompSelR, (int)tex.CompSelG, (int)tex.CompSelB, (int)tex.CompSelA }, false);
        return Texture(tex.Name, (int)tex.Width, (int)tex.Height, pixels);
    }
    public static VisualModelTexture Decode(NxTexture tex)
    {
        var width = (int)tex.Width; var height = (int)tex.Height;
        Validate(width, height);
        var format = Format(tex.Format.ToString());
        var compressed = TexHelper.Instance.IsCompressed(format);
        var block = compressed ? 4 : 1;
        TexHelper.Instance.ComputePitch(format, width, height, out var pitch, out var size, CP_FLAGS.NONE);
        var source = tex.TextureData[0][0];
        var raw = new byte[checked((int)size)];
        if ((uint)tex.TileMode == 1)
        {
            var aligned = ((pitch + 31) / 32) * 32;
            for (var row = 0; row < (height + block - 1) / block; row++)
                Array.Copy(source, checked(row * aligned), raw, row * pitch, pitch);
        }
        else
        {
            var blocksWide = (width + block - 1) / block;
            var blocksHigh = (height + block - 1) / block;
            var blockHeight = 1 << (int)tex.BlockHeightLog2;
            var power = 1;
            while (power < blocksHigh) power <<= 1;
            if (power < blockHeight * 8) blockHeight = Math.Max(1, blockHeight / 2);
            if (source.Length < size) throw new InvalidDataException("The tiled texture surface is truncated.");
            Deswizzle((ulong)blocksWide, (ulong)blocksHigh, 1, source, (ulong)source.Length, raw, (ulong)raw.Length,
                (ulong)blockHeight, (ulong)(pitch / blocksWide));
        }
        var pixels = DecodeSurface(raw, width, height, format);
        SwizzleChannels(pixels, new[] { (int)tex.ChannelRed, (int)tex.ChannelGreen, (int)tex.ChannelBlue, (int)tex.ChannelAlpha }, true);
        return Texture(tex.Name, width, height, pixels);
    }
    private static void Validate(int width, int height)
    {
        if (width is < 1 or > 16384 || height is < 1 or > 16384 || (long)width * height > 64 * 1024 * 1024)
            throw new InvalidDataException("The texture dimensions exceed the viewer limit.");
    }
    private static DXGI_FORMAT Format(string name)
    {
        var n = name.Replace("_", "").ToUpperInvariant();
        var srgb = n.Contains("SRGB"); var signed = n.Contains("SNORM");
        if (n.Contains("BC1")) return srgb ? DXGI_FORMAT.BC1_UNORM_SRGB : DXGI_FORMAT.BC1_UNORM;
        if (n.Contains("BC2")) return srgb ? DXGI_FORMAT.BC2_UNORM_SRGB : DXGI_FORMAT.BC2_UNORM;
        if (n.Contains("BC3")) return srgb ? DXGI_FORMAT.BC3_UNORM_SRGB : DXGI_FORMAT.BC3_UNORM;
        if (n.Contains("BC4")) return signed ? DXGI_FORMAT.BC4_SNORM : DXGI_FORMAT.BC4_UNORM;
        if (n.Contains("BC5")) return signed ? DXGI_FORMAT.BC5_SNORM : DXGI_FORMAT.BC5_UNORM;
        if (n.Contains("BC7")) return srgb ? DXGI_FORMAT.BC7_UNORM_SRGB : DXGI_FORMAT.BC7_UNORM;
        if (n.Contains("R8G8B8A8")) return srgb ? DXGI_FORMAT.R8G8B8A8_UNORM_SRGB : DXGI_FORMAT.R8G8B8A8_UNORM;
        if (n.Contains("R8G8")) return signed ? DXGI_FORMAT.R8G8_SNORM : DXGI_FORMAT.R8G8_UNORM;
        if (n.Contains("R8")) return signed ? DXGI_FORMAT.R8_SNORM : DXGI_FORMAT.R8_UNORM;
        throw new NotSupportedException($"Texture format {name} is not supported by the actor viewer.");
    }
    private static byte[] DecodeSurface(byte[] raw, int width, int height, DXGI_FORMAT format)
    {
        TexHelper.Instance.ComputePitch(format, width, height, out var pitch, out var size, CP_FLAGS.NONE);
        if (raw.Length < size) throw new InvalidDataException("The texture surface is truncated.");
        var pinned = GCHandle.Alloc(raw, GCHandleType.Pinned);
        try
        {
            var img = new DirectXTexNet.Image(width, height, format, pitch, size, pinned.AddrOfPinnedObject(), null);
            var meta = new TexMetadata(width, height, 1, 1, 1, 0, 0, format, TEX_DIMENSION.TEXTURE2D);
            using var input = TexHelper.Instance.InitializeTemporary(new[] { img }, meta, null);
            var outputFormat = TexHelper.Instance.IsSRGB(format) ? DXGI_FORMAT.R8G8B8A8_UNORM_SRGB : DXGI_FORMAT.R8G8B8A8_UNORM;
            using var output = TexHelper.Instance.IsCompressed(format) ? input.Decompress(0, outputFormat) : input.Convert(0, outputFormat, TEX_FILTER_FLAGS.DEFAULT, 0.5f);
            var pixels = new byte[checked(width * height * 4)];
            Marshal.Copy(output.GetImage(0).Pixels, pixels, 0, pixels.Length);
            return pixels;
        }
        finally { pinned.Free(); }
    }
    private static void SwizzleChannels(byte[] pixels, int[] channels, bool nx)
    {
        Span<byte> rgba = stackalloc byte[4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels.AsSpan(i, 4).CopyTo(rgba);
            for (var j = 0; j < 4; j++)
            {
                var c = channels[j];
                pixels[i + j] = (byte)(nx ? c switch { 0 => 0, 1 => 255, >= 2 and <= 5 => rgba[c - 2], _ => rgba[j] }
                    : c switch { >= 0 and <= 3 => rgba[c], 4 => 0, 5 => 255, _ => rgba[j] });
            }
        }
    }
    private static VisualModelTexture Texture(string name, int width, int height, byte[] pixels)
    {
        var transparent = false;
        for (var i = 3; i < pixels.Length; i += 4) if (pixels[i] < 250) { transparent = true; break; }
        return new() { Name = name, SourcePath = name, Width = width, Height = height, Pixels = pixels,
            AlphaMode = transparent ? VisualModelAlphaMode.Cutout : VisualModelAlphaMode.Opaque };
    }
}
