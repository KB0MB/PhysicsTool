namespace Toolbox.Library;

// From Switch-Toolbox's TextureHelper; retained for GX2's surface routines.
internal static class TextureHelper
{
    public static Tuple<uint, uint> GetCurrentMipSize(uint width, uint height, uint blockWidth, uint blockHeight, uint bpp, int level)
    {
        uint Size(int mip) => ((Math.Max(1, width >> mip) + blockWidth - 1) / blockWidth) *
            ((Math.Max(1, height >> mip) + blockHeight - 1) / blockHeight) * bpp;
        uint offset = 0;
        for (var mip = 0; mip < level; mip++) offset += Size(mip);
        return Tuple.Create(offset, Size(level));
    }
}
