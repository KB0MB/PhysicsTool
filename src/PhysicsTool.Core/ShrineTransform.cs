using System.Numerics;
using HKCLTool;

namespace PhysicsTool.Core;

internal readonly record struct ShrineTransform(Vector3 Position, Vector3 Rotation, Vector3 Scale)
{
    internal static ShrineTransform Local(ShrineActor actor) => new(actor.LocalPosition, actor.Rotation, actor.Scale);
    internal static bool SameLinear(Matrix4x4 a, Matrix4x4 b) { a.Translation = b.Translation = Vector3.Zero; return a == b; }
    internal void Validate()
    {
        Finite(Position); Finite(Rotation); Finite(Scale);
        if (Scale.X <= 1e-6f || Scale.Y <= 1e-6f || Scale.Z <= 1e-6f)
            throw new ArgumentException("Actor scale must be positive and nonzero. Mirrored collision is not supported.");
    }
    internal static ShrineTransform FromMatrix(Matrix4x4 matrix)
    {
        Decompose(matrix, out var scale, out var rotation, out var position);
        return new(position, BoneRotationAngles.ToRadians(rotation), scale);
    }
    internal static void Decompose(Matrix4x4 matrix, out Vector3 scale, out Quaternion rotation, out Vector3 position)
    {
        Finite(new(matrix.M14, matrix.M24, matrix.M34));
        if (!Matrix4x4.Decompose(matrix, out scale, out rotation, out position))
            throw new NotSupportedException("This transform introduces shear. Use uniform scaling or rotate the parent instead.");
        new ShrineTransform(position, Vector3.Zero, scale).Validate();
        if (!float.IsFinite(rotation.LengthSquared()) || !float.IsFinite(matrix.M44) ||
            Math.Abs(matrix.M14) > 1e-6f || Math.Abs(matrix.M24) > 1e-6f || Math.Abs(matrix.M34) > 1e-6f || Math.Abs(matrix.M44 - 1) > 1e-6f)
            throw new ArgumentException("Invalid actor transform.");
        rotation = Quaternion.Normalize(rotation);
        var rebuilt = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation);
        foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
        {
            var actual = Vector3.TransformNormal(axis, matrix);
            Finite(actual);
            if (Vector3.Distance(actual, Vector3.TransformNormal(axis, rebuilt)) > 1e-4f * Math.Max(1e-5f, actual.Length()))
                throw new NotSupportedException("This transform introduces shear. Use uniform scaling or rotate the parent instead.");
        }
    }
    internal static void Finite(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)) throw new ArgumentException("Transform values must be finite.");
    }
    internal static (Vector3 Min, Vector3 Max) Bounds(Vector3 min, Vector3 max, Matrix4x4 transform)
    {
        Vector3 low = new(float.MaxValue), high = new(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var p = Vector3.Transform(new((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z), transform);
            Finite(p); low = Vector3.Min(low, p); high = Vector3.Max(high, p);
        }
        return (low, high);
    }
}
