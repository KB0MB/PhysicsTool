using HKCLTool;
using PhysicsTool.Core;
using SkiaSharp;
using System.Runtime.InteropServices;

internal static class ShrineTextureChecks
{
    internal static void Run(string root, string output)
    {
        Directory.CreateDirectory(output);
        foreach (string name in new[] { "CmnTex_BakeDefault", "Mt_WarpHole_00_Gn4", "TBox_Zonau_Stone_Emm" })
        {
            var bytes = BphscDocument.ReadResource(Path.Combine(root, "TexToGo", name + ".bntx.zs"), [root]);
            Save(TexToGoTexture.DecodeBntx(bytes, name), output, name);
        }
        foreach (string name in new[] { "Etc_DgnObjRopeWireA_A_Alb", "DgnObj_Small_BoxPartsFloor_X_Alb" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(root, "TexToGo", name + ".txtg"));
            var decoded = TexToGoTexture.Decode(bytes, name);
            Save(decoded, output, name);
            // The native surface footprint and visual reference identify 4x4 blocks.
            bytes[60] = 1;
            if (!decoded.Pixels.SequenceEqual(TexToGoTexture.Decode(bytes, name).Pixels)) throw new Exception("ASTC variant was decoded with the wrong block size");
        }
        foreach (string name in new[] { "DgnObj_SkyAntiZonau_B_Gn1", "DgnObj_Small_Black02_Alb" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(root, "TexToGo", name + ".txtg"));
            var decoded = TexToGoTexture.Decode(bytes, name);
            bytes[60] = bytes[61] == 7 ? (byte)7 : (byte)6;
            if (!decoded.Pixels.SequenceEqual(TexToGoTexture.Decode(bytes, name).Pixels)) throw new Exception("BC format variant mismatch");
            Save(decoded, output, name);
        }
        Console.WriteLine("PASS: compressed standalone BNTX and padded TXTG texture decoding.");
    }

    private static void Save(VisualModelTexture texture, string output, string name)
    {
        if (texture.Pixels.Length != texture.Width * texture.Height * 4) throw new Exception("Decoded pixel size mismatch");
        using var bitmap = new SKBitmap(texture.Width, texture.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        Marshal.Copy(texture.Pixels, 0, bitmap.GetPixels(), texture.Pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(output, name + ".png"), png.ToArray());
    }
}
