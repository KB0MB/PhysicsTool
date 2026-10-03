using System.Numerics;

namespace HKCLTool;

// BFRES EulerXYZ: qZ * qY * qX, equivalent to Rx * Ry * Rz
// with System.Numerics row vectors. Storage and simulation remain quaternion-based.
internal static class BoneRotationAngles
{
    internal static Quaternion FromRadians(Vector3 angles)
    {
        if (!float.IsFinite(angles.X) || !float.IsFinite(angles.Y) || !float.IsFinite(angles.Z))
            throw new ArgumentException("Rotation angles must be finite radians.");
        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angles.Z)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, angles.Y)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, angles.X));
    }

    internal static Vector3 ToRadians(Quaternion rotation)
    {
        if (!float.IsFinite(rotation.LengthSquared())) throw new ArgumentException("Rotation must be finite.");
        var m = Matrix4x4.CreateFromQuaternion(rotation.LengthSquared() < 1e-12f ? Quaternion.Identity : Quaternion.Normalize(rotation));
        var cy = MathF.Sqrt(m.M11 * m.M11 + m.M12 * m.M12);
        return new Vector3(cy > 1e-6f ? MathF.Atan2(m.M23, m.M33) : MathF.Atan2(-m.M32, m.M22),
            MathF.Atan2(-m.M13, cy), cy > 1e-6f ? MathF.Atan2(m.M12, m.M11) : 0);
    }
}
