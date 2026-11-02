using System.Buffers.Binary;
using System.Runtime.InteropServices;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using Ryujinx.Graphics.Gal.Texture;
using ZstdSharp;

namespace HKCLTool;

// TXTG layout and ASTC format overrides follow Switch Toolbox's TXTG reader.
internal static class TexToGoTexture
{
    // tegra_swizzle block_height_mip0: TXTG does not store an explicit GOB height.
    // Unlike BNTX's explicit layout, this inferred base-mip layout caps at 16.
    internal static int BaseBlockHeight(int heightInBlocks)
    {
        int biasedHeight = checked(heightInBlocks + heightInBlocks / 2);
        return biasedHeight >= 128 ? 16 : biasedHeight >= 64 ? 8 : biasedHeight >= 32 ? 4 : biasedHeight >= 16 ? 2 : 1;
    }
    internal static string Key(string name, int surface) => surface == 0 ? name : $"{name}#surface{surface}";

    internal static VisualModelTexture DecodeBntx(byte[] data, string name, int surface = 0)
    {
        using var stream = new MemoryStream(data, false);
        var container = new Syroot.NintenTools.NSW.Bntx.BntxFile(stream);
        var texture = container.Textures.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? (container.Textures.Count == 1 ? container.Textures[0] : null);
        if (texture == null) throw new InvalidDataException("BNTX does not contain the requested texture.");
        return ToolboxActorTexture.Decode(texture, surface);
    }

    internal static VisualModelTexture Decode(byte[] file, string name, int surface = 0)
    {
        if (file.Length < 80 || !file.AsSpan(4, 4).SequenceEqual("6PK0"u8))
            throw new InvalidDataException("Invalid TexToGo header.");
        int U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(offset, 2));
        uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(offset, 4));
        int header = U16(0), width = U16(8), height = U16(10), layers = U16(12), mips = file[14];
        if (U16(2) != 0x11 || header < 80 || width < 1 || height < 1 || (long)width * height > 16 * 1024 * 1024 ||
            layers is < 1 or > 2048 || mips is < 1 or > 16)
            throw new InvalidDataException("Unsupported TexToGo dimensions or version.");
        int count = checked(layers * mips), payload = checked(header + count * 12);
        if (payload > file.Length) throw new InvalidDataException("Truncated TexToGo surface table.");
        surface = Math.Clamp(surface, 0, layers - 1);
        int format = U16(60), bx = 4, by = 4, bytes = 16;
        bool srgb = format is 0x105 or 0x109 or 0x203 or 0x505;
        CompressionFormat? bc = format switch {
            0x202 or 0x203 or 0x302 => CompressionFormat.Bc1WithAlpha,
            0x505 => CompressionFormat.Bc3,
            0x602 or 0x605 or 0x606 or 0x607 => CompressionFormat.Bc4,
            0x702 or 0x703 or 0x705 or 0x707 => CompressionFormat.Bc5,
            0x901 => CompressionFormat.Bc7,
            0x101 or 0x102 or 0x105 or 0x109 => null,
            _ => throw new NotSupportedException($"TexToGo format 0x{format:X} is not supported.") };
        if (bc is CompressionFormat.Bc1WithAlpha or CompressionFormat.Bc4) bytes = 8;
        if (bc == null)
        {
            if (format is 0x102 or 0x105) bx = by = 8;
            switch (U32(68))
            {
                case 32628: bx = 8; by = 5; srgb = false; break;
                case 32629: bx = 8; by = 6; break;
                case 32631: bx = by = 8; srgb = false; break;
            }
        }
        for (int i = 0; i < count; i++)
        {
            int array = U16(header + i * 4), mip = file[header + i * 4 + 2];
            uint length = U32(header + count * 4 + i * 8);
            if (array >= layers || mip >= mips || length > file.Length - payload)
                throw new InvalidDataException("Invalid TexToGo surface range.");
            if (array == surface && mip == 0)
            {
                int w = (width + bx - 1) / bx, h = (height + by - 1) / by;
                int blockHeight = BaseBlockHeight(h);
                int expected = checked(((w * bytes + 63) / 64) * 512 * blockHeight * ((h + blockHeight * 8 - 1) / (blockHeight * 8)));
                using var decoder = new Decompressor();
                // Some base surfaces include extra array/alignment padding. Bound the
                // allocation without treating the minimum tiled footprint as an exact size.
                int limit = Math.Max(expected, checked(width * height * 16 + 65536));
                var tiled = decoder.Unwrap(file.AsSpan(payload, (int)length), limit).ToArray();
                // A few shrine textures use the shared 0x102/0x105 tag but store
                // 4x4 ASTC blocks. Accept that variant only on an exact footprint match.
                if (bc == null && bx == 8 && by == 8 && U32(68) == 32563 && tiled.Length > expected)
                {
                    int w4 = (width + 3) / 4, h4 = (height + 3) / 4, bh4 = BaseBlockHeight(h4);
                    int size4 = checked(((w4 * 16 + 63) / 64) * 512 * bh4 * ((h4 + bh4 * 8 - 1) / (bh4 * 8)));
                    if (tiled.Length == size4) { bx = by = 4; w = w4; h = h4; blockHeight = bh4; }
                }
                var raw = ToolboxActorTexture.Deswizzle(tiled, w, h, bytes, 0, System.Numerics.BitOperations.Log2((uint)blockHeight));
                var pixels = bc is { } compression
                    ? MemoryMarshal.AsBytes(new BcDecoder().DecodeRaw(raw, width, height, compression).AsSpan()).ToArray()
                    : ASTCDecoder.DecodeToRGBA8888(raw, bx, by, 1, width, height, 1);
                Span<byte> rgba = stackalloc byte[4];
                bool alpha = false;
                for (int p = 0; p < pixels.Length; p += 4)
                {
                    pixels.AsSpan(p, 4).CopyTo(rgba);
                    for (int c = 0; c < 4; c++) pixels[p + c] = file[24 + c] switch {
                        <= 3 => rgba[file[24 + c]], 4 => 0, 5 => 255,
                        _ => throw new InvalidDataException("Invalid TexToGo channel selector.") };
                    alpha |= pixels[p + 3] < 250;
                }
                return new() { Name = name, SourcePath = $"TexToGo/{name}.txtg", Width = width, Height = height,
                    Pixels = pixels, IsSrgb = srgb, IsSingleChannel = bc == CompressionFormat.Bc4,
                    UseRedAsLuminance = bc == CompressionFormat.Bc4 && file[24] == 0 && file[25] == 1 && file[26] == 2,
                    AlphaMode = alpha ? VisualModelAlphaMode.Cutout : VisualModelAlphaMode.Opaque };
            }
            payload = checked(payload + (int)length);
        }
        throw new InvalidDataException("TexToGo is missing the selected base surface.");
    }

    internal static void Load(ToolboxBfresDocument document, BotwActorResources resources,
        Dictionary<string, VisualModelTexture> textures, CancellationToken cancellation)
    {
        var attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in document.Models)
            foreach (var material in ToolboxBfresDocument.Items((object)model.Materials))
            {
                int surface = ToolboxTotkMaterial.SurfaceIndex((object)material);
                foreach (var reference in ToolboxBfresDocument.Items((object)material.TextureRefs))
                {
                    cancellation.ThrowIfCancellationRequested();
                    string name = reference is string text ? text : (string)reference.Name;
                    string key = Key(name, surface);
                    if (textures.ContainsKey(key) || !attempted.Add(key)) continue;
                    try
                    {
                        var data = resources.Read($"TexToGo/{name}.txtg");
                        if (data != null) textures[key] = Decode(data, name, surface);
                        else if (resources.Read($"TexToGo/{name}.bntx") is { } bntx)
                            textures[key] = DecodeBntx(bntx, name, surface);
                        else resources.Warnings.Add($"Missing TexToGo/{name} (.txtg or .bntx/.bntx.zs)");
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                    { resources.Warnings.Add($"Texture {name}: {UserFacingExceptionFormatter.Format(ex)}"); }
                }
            }
    }
}
