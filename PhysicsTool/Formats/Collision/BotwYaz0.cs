using System.Buffers.Binary;

namespace HKCLTool;

internal static class BotwYaz0
{
    private const int WindowSize = 0x1000;
    private const int MaximumMatch = 0x111;

    public static byte[] Encode(ReadOnlySpan<byte> source)
    {
        using var output = new MemoryStream(source.Length + 32);
        output.Write("Yaz0"u8);
        Span<byte> header = stackalloc byte[12];
        header.Clear();
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)source.Length));
        output.Write(header);

        var position = 0;
        var nextInsert = 0;
        var heads = new Dictionary<int, int>();
        var previous = new int[source.Length];
        Array.Fill(previous, -1);
        while (position < source.Length)
        {
            var codePosition = output.Position;
            output.WriteByte(0);
            byte code = 0;
            for (var bit = 7; bit >= 0 && position < source.Length; bit--)
            {
                while (nextInsert < position)
                {
                    if (nextInsert + 2 < source.Length)
                    {
                        var key = MatchKey(source, nextInsert);
                        previous[nextInsert] = heads.GetValueOrDefault(key, -1);
                        heads[key] = nextInsert;
                    }
                    nextInsert++;
                }
                var (matchLength, matchDistance) = FindMatch(source, position, heads, previous);
                if (matchLength < 3)
                {
                    code |= (byte)(1 << bit);
                    output.WriteByte(source[position++]);
                    continue;
                }

                var distance = matchDistance - 1;
                if (matchLength >= 0x12)
                {
                    output.WriteByte((byte)(distance >> 8));
                    output.WriteByte((byte)distance);
                    output.WriteByte((byte)(matchLength - 0x12));
                }
                else
                {
                    output.WriteByte((byte)(((matchLength - 2) << 4) | (distance >> 8)));
                    output.WriteByte((byte)distance);
                }
                position += matchLength;
            }

            var endPosition = output.Position;
            output.Position = codePosition;
            output.WriteByte(code);
            output.Position = endPosition;
        }
        return output.ToArray();
    }

    public static byte[] Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < 16 || !source[..4].SequenceEqual("Yaz0"u8))
            throw new InvalidDataException("The data is not a Yaz0 stream.");
        var output = new byte[BinaryPrimitives.ReadUInt32BigEndian(source.Slice(4, 4))];
        var inputPosition = 16;
        var outputPosition = 0;
        while (outputPosition < output.Length)
        {
            if (inputPosition >= source.Length)
                throw new InvalidDataException("The Yaz0 stream ended unexpectedly.");
            var code = source[inputPosition++];
            for (var bit = 7; bit >= 0 && outputPosition < output.Length; bit--)
            {
                if ((code & (1 << bit)) != 0)
                {
                    if (inputPosition >= source.Length)
                        throw new InvalidDataException("The Yaz0 stream ended unexpectedly.");
                    output[outputPosition++] = source[inputPosition++];
                    continue;
                }
                if (inputPosition + 1 >= source.Length)
                    throw new InvalidDataException("The Yaz0 stream ended unexpectedly.");
                var first = source[inputPosition++];
                var second = source[inputPosition++];
                var distance = ((first & 0x0f) << 8) | second;
                var length = first >> 4;
                if (length == 0)
                {
                    if (inputPosition >= source.Length)
                        throw new InvalidDataException("The Yaz0 stream ended unexpectedly.");
                    length = source[inputPosition++] + 0x12;
                }
                else
                {
                    length += 2;
                }
                var copyPosition = outputPosition - distance - 1;
                if (copyPosition < 0)
                    throw new InvalidDataException("The Yaz0 stream contains an invalid back-reference.");
                for (var index = 0; index < length && outputPosition < output.Length; index++)
                    output[outputPosition++] = output[copyPosition + index];
            }
        }
        return output;
    }

    private static (int Length, int Distance) FindMatch(
        ReadOnlySpan<byte> source,
        int position,
        IReadOnlyDictionary<int, int> heads,
        IReadOnlyList<int> previous)
    {
        if (position + 2 >= source.Length || !heads.TryGetValue(MatchKey(source, position), out var candidate))
            return (0, 0);
        var bestLength = 0;
        var bestDistance = 0;
        var start = Math.Max(0, position - WindowSize);
        var maximum = Math.Min(MaximumMatch, source.Length - position);
        while (candidate >= start)
        {
            var length = 3;
            while (length < maximum && source[candidate + length] == source[position + length])
                length++;
            if (length > bestLength)
            {
                bestLength = length;
                bestDistance = position - candidate;
                if (length == maximum)
                    break;
            }
            candidate = previous[candidate];
        }
        return (bestLength, bestDistance);
    }

    private static int MatchKey(ReadOnlySpan<byte> source, int position) =>
        (source[position] << 16) | (source[position + 1] << 8) | source[position + 2];
}
