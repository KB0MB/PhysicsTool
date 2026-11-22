using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace HKCLTool;

// BFRES 10.4 permits quaternion tracks inside an otherwise Euler animation.
// The legacy reader masks out that curve type and consumes only half its keys.
internal static class OotAnimationTracks
{
    internal sealed record RotationTrack(float[] Frames, Quaternion[] Keys)
    {
        internal Quaternion Sample(double frame)
        {
            int i = Array.BinarySearch(Frames, (float)frame);
            if (i < 0) i = Math.Max(0, ~i - 1);
            int next = Math.Min(i + 1, Keys.Length - 1);
            float t = Frames[next] > Frames[i] ? (float)Math.Clamp((frame - Frames[i]) / (Frames[next] - Frames[i]), 0, 1) : 0;
            return Quaternion.Slerp(Keys[i], Keys[next], t);
        }
    }
    internal sealed record BoneRotation(RotationTrack? Curve);
    private static readonly ConditionalWeakTable<object, BoneRotation> Rotations = new();
    private static readonly ConditionalWeakTable<object, object> Animations = new();
    internal static bool IsOot(object animation) => Animations.TryGetValue(animation, out _);
    internal static bool TryGet(object bone, out BoneRotation value) => Rotations.TryGetValue(bone, out value!);

    internal static void Read(byte[] bytes, dynamic[] animations)
    {
        if (bytes.Length < 0x68 || bytes[10] != 10 || bytes[9] < 4 || animations.Length == 0) return;
        void Range(int offset, int size)
        {
            if (offset < 0 || size < 0 || offset > bytes.Length - size) throw new InvalidDataException("OoT animation points outside the BFRES.");
        }
        uint U32(int offset) { Range(offset, 4); return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)); }
        ushort U16(int offset) { Range(offset, 2); return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset)); }
        int Pointer(int offset) { Range(offset, 8); long value = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset)); if (value < 0 || value > bytes.Length) throw new InvalidDataException("Invalid OoT animation pointer."); return (int)value; }
        float F32(int offset) => BitConverter.Int32BitsToSingle((int)U32(offset));
        int start = Pointer(0x58);
        for (int a = 0; a < animations.Length; a++)
        {
            int header = checked(start + a * 0x50);
            Range(header, 0x50);
            if (!bytes.AsSpan(header, 4).SequenceEqual("FSKA"u8)) throw new InvalidDataException("Unrecognized OoT animation layout.");
            Animations.Add((object)animations[a], new object());
            var bones = ToolboxBfresDocument.Items((object)animations[a].BoneAnims);
            if (U16(header + 0x4c) != bones.Length) throw new InvalidDataException("OoT animation bone count mismatch.");
            int boneStart = Pointer(header + 0x28);
            for (int b = 0; b < bones.Length; b++)
            {
                int boneOffset = checked(boneStart + b * 0x38);
                Range(boneOffset, 0x38);
                if ((U32(boneOffset + 0x28) & 0x100000) == 0) continue;
                var curves = ToolboxBfresDocument.Items((object)bones[b].Curves);
                int curveStart = Pointer(boneOffset + 8);
                RotationTrack? rotation = null;
                for (int c = 0; c < curves.Length; c++)
                {
                    int curveOffset = checked(curveStart + c * 0x30);
                    var flags = U16(curveOffset + 0x10);
                    if (U32(curveOffset + 0x14) != 0x20) continue;
                    if ((flags & 0xf0) != 0x80 || (flags & 0xc) != 0)
                        throw new NotSupportedException($"Unsupported OoT quaternion encoding 0x{flags:x}.");
                    int count = U16(curveOffset + 0x12), keyStart = Pointer(curveOffset + 8);
                    Range(keyStart, checked(count * 16));
                    var frames = (float[])curves[c].Frames;
                    if (count == 0 || count != frames.Length || frames.Any(f => !float.IsFinite(f)) || frames.Zip(frames.Skip(1)).Any(p => p.First >= p.Second))
                        throw new InvalidDataException("Invalid OoT quaternion key times.");
                    var keys = new Quaternion[count];
                    for (int k = 0; k < count; k++)
                    {
                        int p = keyStart + k * 16;
                        var q = new Quaternion(F32(p), F32(p + 4), F32(p + 8), F32(p + 12));
                        if (!float.IsFinite(q.LengthSquared()) || MathF.Abs(q.LengthSquared() - 1) > .02f)
                            throw new InvalidDataException("Invalid OoT quaternion key.");
                        keys[k] = Quaternion.Normalize(q);
                    }
                    rotation = new(frames, keys);
                }
                Rotations.Add((object)bones[b], new(rotation));
            }
        }
    }
}
