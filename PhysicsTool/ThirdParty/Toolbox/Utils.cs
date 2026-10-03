namespace Toolbox.Library;

// Only the byte-array helper needed by the vendored GX2 decoder.
internal static class Utils
{
    public static byte[] CombineByteArray(params byte[][] buffers) => buffers.SelectMany(b => b).ToArray();
}
