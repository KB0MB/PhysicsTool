using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PhysicsTool.Core;

// Independent reader based on inspected TSCB records and native resource bytes.
// No editor source is incorporated. Offsets are relative to the pointer field.
internal static class TotkTerrainFormat
{
    internal const int Width = 260, Interior = 256, Margin = 2, Samples = Width * Width;
    internal sealed record Material(int Layer, float U, float V);
    internal sealed record Area(string Name, float X, float Z, float Size, bool HasWater = false);
    internal sealed record Scene(float HeightScale, Material[] Materials, Area[] Areas);

    internal static Scene Parse(byte[] data)
    {
        var r = new Reader(data);
        r.Range(0, 0x7c);
        if (!data.AsSpan(0, 4).SequenceEqual("TSCB"u8) || data[4] != 12 || data[5] != 7 || r.U32(8) != 1)
            throw new InvalidDataException("Unsupported terrain TSCB header (expected little-endian 12.7).");
        float world = r.Float(0x10), height = r.Float(0x14);
        if (world <= 0 || world > 10000 || height <= 0 || height > 10000)
            throw new InvalidDataException("Invalid terrain world/height scale.");
        var images = r.Table(0x70, r.Count(0x30, 32));
        foreach (uint type in new uint[] { 0, 0x2000 })
        {
            int p = images.FirstOrDefault(p => r.U32(p) == type, -1);
            if (p < 0) throw new InvalidDataException("Terrain height/material image description is missing.");
            r.Range(p, 0x80);
            if (r.U16(p + 0x10) != Interior || data[p + 0x14] != Margin || r.U32(p + 0x20) != 1 ||
                data[p + 0x1c] != (type == 0 ? 5 : 29))
                throw new InvalidDataException("Unsupported terrain image dimensions, format or archive level.");
        }
        foreach (int p in images.Where(p => r.U32(p) == 0x4100))
        {
            r.Range(p, 0x80);
            if (r.U16(p + 0x10) != 64 || data[p + 0x14] != 2 || data[p + 0x1c] != 39 || r.U32(p + 0x20) != 1)
                throw new InvalidDataException("Unsupported terrain water image layout.");
        }
        var materials = r.Table(0x74, r.Count(0x34, 128)).Select(p =>
        {
            r.Range(p, 0x40);
            uint layer = r.U32(p); float u = r.Float(p + 0x10), v = r.Float(p + 0x14);
            if (layer > 2047 || Math.Abs(u) > 100 || Math.Abs(v) > 100)
                throw new InvalidDataException("Invalid terrain material descriptor.");
            return new Material((int)layer, u, v);
        }).ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var areas = new List<Area>();
        foreach (int p in r.Table(0x78, r.Count(0x38, 100000)))
        {
            r.Range(p, 0x68);
            float x = r.Float(p) * world, z = r.Float(p + 4) * world, size = r.Float(p + 8) * world;
            if (Math.Abs(x) > 100000 || Math.Abs(z) > 100000 || size < 1 || size > 100000)
                throw new InvalidDataException("Invalid terrain tile bounds.");
            string name = r.String(r.Pointer(p + 0x60));
            if (name.Length != 10 || !ulong.TryParse(name, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                throw new InvalidDataException("Invalid terrain tile identifier.");
            if (!names.Add(name)) throw new InvalidDataException("Duplicate terrain tile identifier.");
            var resources = r.Table(p + 0x64, r.Count(p + 0x20, 32));
            foreach (int q in resources) r.Range(q, 0x40);
            if (resources.Any(q => r.U32(q) == 0) && resources.Any(q => r.U32(q) == 0x2000))
                areas.Add(new(name, x, z, size, resources.Any(q => r.U32(q) == 0x4100)));
        }
        return new(height / ushort.MaxValue, materials, areas.ToArray());
    }

    internal static string ArchiveName(string tile) =>
        (ulong.Parse(tile, NumberStyles.HexNumber, CultureInfo.InvariantCulture) & 0xffffffffcUL).ToString("X9");

    internal static ushort[] DecodeHeight(ReadOnlySpan<byte> data)
    {
        // Base image occupies 135200 bytes; preserve/ignore the remaining normal payload.
        if (data.Length < Samples * 2) throw new InvalidDataException("Truncated terrain height image.");
        var result = new ushort[Samples];
        for (int row = 0; row < Width; row++)
        {
            int accumulator = 0;
            for (int col = 0; col < Width; col++)
            {
                int i = row * Width + col, shift = (col & 1) == 0 ? 4 : 0;
                int low = ((data[i / 2] >> shift) & 15) - 8;
                int middle = ((data[Samples / 2 + i / 2] >> shift) & 15) - 7;
                int high = unchecked((sbyte)data[Samples + i]);
                accumulator = (accumulator + low + middle * 16 + high * 256) & 65535;
                result[i] = (ushort)accumulator;
            }
        }
        return result;
    }

    internal static byte[] DecodeMaterial(ReadOnlySpan<byte> data)
    {
        if (data.Length != Samples * 4) throw new InvalidDataException("Invalid terrain material image length.");
        var result = new byte[data.Length];
        for (int channel = 0; channel < 4; channel++)
            for (int row = 0; row < Width; row++)
            {
                int value = 0;
                for (int col = 0; col < Width; col++)
                {
                    int i = row * Width + col;
                    value = (value + data[channel * Samples + i]) & 255;
                    result[i * 4 + channel] = (byte)value;
                }
            }
        return result;
    }

    internal static bool Hole(byte a, byte b) => (a & 128) != 0 || (b & 128) != 0 || a == 125 || b == 125;

    private sealed class Reader(byte[] data)
    {
        internal void Range(int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > data.Length - length)
                throw new InvalidDataException("Terrain reference is outside the TSCB file.");
        }
        internal uint U32(int p) { Range(p, 4); return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p, 4)); }
        internal ushort U16(int p) { Range(p, 2); return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p, 2)); }
        internal float Float(int p)
        {
            float f = BitConverter.Int32BitsToSingle(unchecked((int)U32(p)));
            if (!float.IsFinite(f)) throw new InvalidDataException("Nonfinite terrain descriptor.");
            return f;
        }
        internal int Count(int p, int max)
        {
            uint count = U32(p);
            if (count == 0 || count > max) throw new InvalidDataException("Invalid terrain table count.");
            return (int)count;
        }
        internal int Pointer(int p)
        {
            long target = p + (long)U32(p);
            if (target < 0 || target >= data.Length) throw new InvalidDataException("Invalid terrain relative pointer.");
            return (int)target;
        }
        internal int[] Table(int p, int count)
        {
            int start = Pointer(p); Range(start, count * 4);
            var result = new int[count];
            for (int i = 0; i < count; i++) result[i] = Pointer(start + i * 4);
            return result;
        }
        internal string String(int p)
        {
            Range(p, 1);
            int length = Array.IndexOf(data, (byte)0, p, Math.Min(64, data.Length - p)) - p;
            if (length < 0) throw new InvalidDataException("Unterminated terrain tile identifier.");
            return Encoding.ASCII.GetString(data, p, length);
        }
    }
}
