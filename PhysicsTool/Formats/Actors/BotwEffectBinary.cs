using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace HKCLTool;

// Bounded reads shared by the version-20 particle and version-30 ELink readers.
internal sealed class BotwEffectBinary(byte[] bytes, bool bigEndian)
{
    internal byte[] Bytes => bytes;
    internal ReadOnlySpan<byte> Span(int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > bytes.Length - count)
            throw new InvalidDataException("Truncated BotW effect data.");
        return bytes.AsSpan(offset, count);
    }
    internal byte B(int p) => Span(p, 1)[0];
    internal ushort U16(int p) => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(Span(p, 2)) : BinaryPrimitives.ReadUInt16LittleEndian(Span(p, 2));
    internal uint U32(int p) => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(Span(p, 4)) : BinaryPrimitives.ReadUInt32LittleEndian(Span(p, 4));
    internal int I32(int p) => unchecked((int)U32(p));
    internal ulong U64(int p) => bigEndian ? BinaryPrimitives.ReadUInt64BigEndian(Span(p, 8)) : BinaryPrimitives.ReadUInt64LittleEndian(Span(p, 8));
    internal float F(int p)
    {
        var f = BitConverter.UInt32BitsToSingle(U32(p));
        if (!float.IsFinite(f)) throw new InvalidDataException("Non-finite BotW effect value.");
        return f;
    }
    internal Vector3 V3(int p) => new(F(p), F(p + 4), F(p + 8));
    internal Vector4 V4(int p) => new(F(p), F(p + 4), F(p + 8), F(p + 12));
    internal string Text(int p, int limit = 256)
    {
        var data = Span(p, Math.Min(limit, bytes.Length - p));
        int end = data.IndexOf((byte)0);
        if (end < 0) throw new InvalidDataException("Unterminated BotW effect string.");
        return Encoding.UTF8.GetString(data[..end]);
    }
    internal int Relative(int p, int field)
    {
        uint value = U32(p + field);
        if (value is 0 or uint.MaxValue) return -1;
        int target = checked(p + (int)value);
        Span(target, 32);
        return target;
    }
    internal IEnumerable<int> Sections(int start)
    {
        var visited = new HashSet<int>();
        for (int p = start; p >= 0; p = Relative(p, 12))
        {
            if (!visited.Add(p) || visited.Count > 100000) throw new InvalidDataException("Cyclic BotW effect sections.");
            Span(p, 32);
            yield return p;
        }
    }
    internal string Magic(int p) => Encoding.ASCII.GetString(Span(p, 4));
}

internal sealed record BotwEffectBinding(string Effect, string Bone, Vector3 Position, float StartFrame, float EndFrame)
{
    internal static BotwEffectBinding? Read(byte[] bytes, bool bigEndian, string user)
    {
        var r = new BotwEffectBinary(bytes, bigEndian);
        if (r.Magic(0) != "XLNK" || r.U32(8) != 30) throw new NotSupportedException("Unsupported BotW ELink database version.");
        int users = r.I32(0x3c), names = r.I32(0x44);
        if (users is < 0 or > 100000) throw new InvalidDataException("Invalid ELink user count.");
        int pdt = checked(0x48 + users * 8), userParams = r.I32(pdt + 4), assetParams = r.I32(pdt + 8), triggerParams = r.I32(pdt + 16);
        if (userParams is < 0 or > 256 || assetParams is < 0 or > 64 || triggerParams is < 0 or > 256)
            throw new InvalidDataException("Invalid ELink parameter table.");
        int definitions = pdt + 20, definitionNames = checked(definitions + 12 * (userParams + assetParams + triggerParams));
        int assets = checked((pdt + r.I32(pdt) + 3) & ~3);
        int direct = checked(r.I32(0x1c) + 4 * (r.I32(0x20) + r.I32(0x24)));
        uint hash = BotwAampWriter.Crc32(user);
        int header = -1;
        for (int i = 0; i < users; i++)
            if (r.U32(0x48 + i * 4) == hash) { header = r.I32(0x48 + users * 4 + i * 4); break; }
        if (header < 0) return null;
        int count = r.I32(header + 8);
        if (count is < 0 or > 100000) throw new InvalidDataException("Invalid ELink call count.");
        int calls = checked((header + 48 + r.I32(header + 4) * 4 + userParams * 4 + count * 2 + 3) & ~3);
        string Name(int offset) => r.Text(checked(names + offset));
        for (int i = 0; i < count; i++)
        {
            int call = checked(calls + i * 32);
            if (Name(r.I32(call)) != "Blade_00") continue;
            int values = checked(assets + r.I32(call + 24));
            ulong mask = r.U64(values);
            var text = new Dictionary<string, string>();
            var number = new Dictionary<string, float>();
            int cursor = values + 8;
            for (int j = 0; j < assetParams; j++)
            {
                if ((mask & (1UL << j)) == 0) continue;
                int def = definitions + 12 * (userParams + j);
                string key = r.Text(checked(definitionNames + r.I32(def)));
                uint packed = r.U32(cursor); cursor += 4;
                int value = (int)(packed & 0xffffff);
                if ((packed >> 24) == 1) text[key] = Name(value);
                else if ((packed >> 24) == 0) number[key] = r.F(checked(direct + value * 4));
            }
            if (!text.TryGetValue("RuntimeAssetName", out var effect) || !text.TryGetValue("Bone", out var bone)) return null;
            int slots = r.I32(header + 20), actions = r.I32(header + 24);
            if (slots is < 0 or > 256 || actions is < 0 or > 100000) throw new InvalidDataException("Invalid ELink action count.");
            int actionTable = checked(header + r.I32(header + 44) + slots * 8);
            int triggers = checked(actionTable + actions * 12);
            for (int a = 0; a < actions; a++)
            {
                int action = actionTable + a * 12;
                if (Name(r.I32(action)) != "BladeOpen") continue;
                int first = r.I32(action + 4), last = r.I32(action + 8);
                if (first < 0 || last < first || last >= r.I32(header + 28)) throw new InvalidDataException("Invalid blade trigger range.");
                int trigger = checked(triggers + first * 24);
                return new(effect, bone, new(number.GetValueOrDefault("PositionX"), number.GetValueOrDefault("PositionY"), number.GetValueOrDefault("PositionZ")),
                    r.I32(trigger + 8), r.I32(trigger + 12));
            }
        }
        return null;
    }
}
