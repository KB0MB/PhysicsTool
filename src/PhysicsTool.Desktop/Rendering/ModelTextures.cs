using HKCLTool;
using SkiaSharp;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PhysicsTool.Desktop.Rendering;

public static class ModelTextures
{
    private static readonly Lazy<VisualModelTexture> Fallback = new(() => Resource("ToolboxDefaultTexture.png"));
    public static VisualModelTexture Default => Fallback.Value;
    public static VisualModelTexture Resource(string name)
    {
        var assembly = typeof(ModelTextures).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + name)))!;
        using var memory = new MemoryStream(); stream.CopyTo(memory);
        return Decode(memory.ToArray(), name);
    }

    public static VisualModelTexture Decode(byte[] source, string name)
    {
        using var data = SKData.CreateCopy(source);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Unsupported texture encoding: " + name);
        Validate(codec.Info.Width, codec.Info.Height);
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var status = codec.GetPixels(info, bitmap.GetPixels());
        if (status != SKCodecResult.Success) throw new InvalidDataException("Incomplete texture: " + name);
        var pixels = new byte[checked(info.Width * info.Height * 4)];
        for (int y = 0; y < info.Height; y++)
            Marshal.Copy(bitmap.GetPixels() + y * bitmap.RowBytes, pixels, (info.Height - 1 - y) * info.Width * 4, info.Width * 4);
        return FromPixels(name, info.Width, info.Height, pixels);
    }

    public static VisualModelTexture FromPixels(string name, int width, int height, byte[] pixels)
    {
        Validate(width, height);
        if (pixels.Length != checked(width * height * 4)) throw new InvalidDataException("Invalid texture buffer.");
        bool transparent = false, partial = false;
        for (int i = 3; i < pixels.Length; i += 4) { transparent |= pixels[i] < 255; partial |= pixels[i] is > 0 and < 255; }
        return new() { Name = name, Width = width, Height = height, Pixels = pixels,
            AlphaMode = partial ? VisualModelAlphaMode.Blend : transparent ? VisualModelAlphaMode.Cutout : VisualModelAlphaMode.Opaque };
    }

    public static void Validate(int width, int height)
    {
        if (width < 1 || height < 1 || width > 16384 || height > 16384 || (long)width * height > 32 * 1024 * 1024)
            throw new InvalidDataException("Texture exceeds the preview size limit.");
    }
}
