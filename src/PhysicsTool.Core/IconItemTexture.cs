using HKCLTool;
using Toolbox.Library;
using ResS = Syroot.NintenTools.NSW.Bfres;
using ResU = Syroot.NintenTools.Bfres;

namespace PhysicsTool.Core;

internal static class IconItemTexture
{
    internal static byte[] EncodeBntx(byte[] template, string name, byte[] rgba, int width, int height, bool compressed)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Invalid icon name.", nameof(name));
        if (width is < 1 or > 2048 || height is < 1 or > 2048 || rgba.Length != checked(width * height * 4))
            throw new ArgumentException("Invalid icon pixel dimensions.");
        if (template.AsSpan().StartsWith(new byte[] { 0x28, 0xb5, 0x2f, 0xfd }))
        {
            using var decompressor = new ZstdSharp.Decompressor(); template = decompressor.Unwrap(template).ToArray();
        }
        if (!template.AsSpan().StartsWith("BNTX"u8))
        {
            var document = new ToolboxBfresDocument(template);
            if (document.Resource is not ResS.ResFile nx) throw new InvalidDataException("BNTX export requires a Switch texture template.");
            template = nx.ExternalFiles.Single(e => e.Data.AsSpan().StartsWith("BNTX"u8)).Data;
        }
        using var input = new MemoryStream(template);
        var bntx = new Syroot.NintenTools.NSW.Bntx.BntxFile(input);
        if (bntx.Textures.Count != 1) throw new InvalidDataException("Expected one icon texture.");
        var texture = bntx.Textures[0];
        texture.Name = name; bntx.Name = name;
        texture.Width = (uint)width; texture.Height = (uint)height;
        texture.Format = Syroot.NintenTools.NSW.Bntx.GFX.SurfaceFormat.R8_G8_B8_A8_SRGB;
        texture.TileMode = (Syroot.NintenTools.NSW.Bntx.GFX.TileMode)0;
        texture.MipCount = 1; texture.BlockHeightLog2 = 4;
        while (texture.BlockHeightLog2 > 0 && height <= (1 << (int)texture.BlockHeightLog2) * 4) texture.BlockHeightLog2--;
        texture.ChannelRed = (Syroot.NintenTools.NSW.Bntx.GFX.ChannelType)2;
        texture.ChannelGreen = (Syroot.NintenTools.NSW.Bntx.GFX.ChannelType)3;
        texture.ChannelBlue = (Syroot.NintenTools.NSW.Bntx.GFX.ChannelType)4;
        texture.ChannelAlpha = (Syroot.NintenTools.NSW.Bntx.GFX.ChannelType)5;
        texture.TextureData = [[Swizzle(rgba, width, height, (int)texture.BlockHeightLog2)]]; texture.MipOffsets = [0];
        using var output = new MemoryStream(); bntx.Save(output, true);
        if (!compressed) return output.ToArray();
        using var compressor = new ZstdSharp.Compressor(9); return compressor.Wrap(output.ToArray()).ToArray();
    }

    internal static byte[] Encode(byte[] template, string name, byte[] rgba, int width, int height)
    {
        if (width is < 1 or > 2048 || height is < 1 or > 2048 || rgba.Length != checked(width * height * 4))
            throw new ArgumentException("Invalid icon pixel dimensions.");
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Invalid icon name.", nameof(name));
        var document = new ToolboxBfresDocument(template);
        if (document.Models.Length > 0) throw new InvalidDataException("The icon template contains models.");
        using var output = new MemoryStream();
        if (document.Resource is ResS.ResFile nx)
        {
            var external = nx.ExternalFiles.Where(e => e.Data.AsSpan().StartsWith("BNTX"u8)).ToArray();
            if (external.Length != 1) throw new InvalidDataException("Expected one icon texture container.");
            external[0].Data = EncodeBntx(external[0].Data, name, rgba, width, height, false);
            nx.Name = name; nx.Save(output, true);
        }
        else if (document.Resource is ResU.ResFile wii)
        {
            if (wii.Textures.Count != 1) throw new InvalidDataException("Expected one icon texture.");
            var texture = wii.Textures.Values.Single();
            // Captured pixels are RGBA, independent of the old icon's compression or mip chain.
            texture.Format = ResU.GX2.GX2SurfaceFormat.TCS_R8_G8_B8_A8_SRGB;
            texture.MipCount = 1; texture.ViewMipFirst = 0; texture.ViewMipCount = 1;
            texture.Depth = 1; texture.ArrayLength = 1; texture.ViewSliceFirst = 0; texture.ViewSliceCount = 1;
            texture.Dim = ResU.GX2.GX2SurfaceDim.Dim2D;
            texture.TileMode = ResU.GX2.GX2TileMode.Mode2dTiledThin1;
            texture.Swizzle = 0;
            var surface = ToolboxActorTexture.EncodeIconSurface(rgba, texture, width, height);
            texture.Name = name; texture.Width = (uint)width; texture.Height = (uint)height;
            texture.Data = surface.data; texture.MipData = []; texture.MipOffsets = new uint[13];
            texture.Pitch = surface.pitch; texture.Alignment = surface.alignment; texture.Swizzle = surface.swizzle;
            texture.TileMode = (ResU.GX2.GX2TileMode)surface.tileMode;
            texture.Regs = GX2TexRegisters.CreateTexRegs((uint)width, (uint)height, 1, (uint)texture.Format, surface.tileMode, surface.pitch, [0, 1, 2, 3]);
            texture.CompSelR = (ResU.GX2.GX2CompSel)0; texture.CompSelG = (ResU.GX2.GX2CompSel)1;
            texture.CompSelB = (ResU.GX2.GX2CompSel)2; texture.CompSelA = (ResU.GX2.GX2CompSel)3;
            wii.Textures.Clear(); wii.Textures.Add(name, texture);
            wii.Name = name; wii.Save(output, true);
        }
        else throw new NotSupportedException("Unsupported icon template.");
        return BotwYaz0.Encode(output.ToArray());
    }

    private static byte[] Swizzle(byte[] rgba, int width, int height, int log2)
    {
        if (log2 is < 0 or > 5) throw new InvalidDataException("Invalid block height.");
        int blockHeight = 1 << log2;
        while (blockHeight > 1 && height <= blockHeight * 4) blockHeight >>= 1;
        int gobs = (width * 4 + 63) / 64;
        var output = new byte[((height + 8 * blockHeight - 1) / (8 * blockHeight)) * gobs * 512 * blockHeight];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int xb = x * 4;
            int offset = y / (8 * blockHeight) * 512 * blockHeight * gobs + xb / 64 * 512 * blockHeight +
                y % (8 * blockHeight) / 8 * 512 + xb % 64 / 32 * 256 + y % 8 / 2 * 64 + xb % 32 / 16 * 32 + y % 2 * 16 + xb % 16;
            rgba.AsSpan((y * width + x) * 4, 4).CopyTo(output.AsSpan(offset, 4));
        }
        return output;
    }
}
