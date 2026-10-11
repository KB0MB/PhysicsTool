using System.Numerics;

namespace HKCLTool;

// Shared by contact queries and viewport geometry, including edited/degenerate shapes.
internal readonly record struct CapsuleProfile(Vector3 Start, Vector3 End, float Radius, float EndRadius,
    Vector3 Axis, float Length, float Sine, float Cosine, bool IsSphere)
{
    public static CapsuleProfile Create(ColliderPreviewShape shape)
    {
        static float RadiusOf(float value) => float.IsFinite(value) ? Math.Max(0, value) : 0;
        var a = shape.Start;
        var b = shape.End;
        var r0 = RadiusOf(shape.Radius);
        var r1 = shape.Kind == ColliderPreviewKind.TaperedCapsule ? RadiusOf(shape.EndRadius) : r0;
        var length = Vector3.Distance(a, b);
        if (shape.Kind == ColliderPreviewKind.Sphere || length <= 1e-6f || length <= MathF.Abs(r1 - r0))
        {
            if (shape.Kind != ColliderPreviewKind.Sphere && r1 > r0) { a = b; r0 = r1; }
            return new(a, a, r0, r0, Vector3.UnitY, 0, 0, 1, true);
        }
        var sine = (r1 - r0) / length;
        return new(a, b, r0, r1, (b - a) / length, length, sine,
            MathF.Sqrt(Math.Max(0, 1 - sine * sine)), false);
    }

    public float SignedDistance(Vector3 point, Vector3 fallback, out Vector3 normal)
    {
        if (IsSphere) return SphereDistance(point, Start, Radius, fallback, out normal);
        var offset = point - Start;
        var axial = Vector3.Dot(offset, Axis);
        var radial = offset - axial * Axis;
        var radialLength = radial.Length();
        // BotW main 7101db2538: spherical ends joined at their common tangent cone.
        var capCoordinate = axial + radialLength * Sine / Cosine;
        if (capCoordinate <= 0) return SphereDistance(point, Start, Radius, fallback, out normal);
        if (capCoordinate >= Length) return SphereDistance(point, End, EndRadius, fallback, out normal);
        var radialFallback = fallback - Vector3.Dot(fallback, Axis) * Axis;
        var perpendicular = Vector3.Cross(Axis, MathF.Abs(Axis.Y) > .9f ? Vector3.UnitX : Vector3.UnitY);
        var radialDirection = radialLength > 1e-6f ? radial / radialLength : Normalize(radialFallback, Vector3.Normalize(perpendicular));
        normal = radialDirection * Cosine - Axis * Sine;
        return radialLength * Cosine - axial * Sine - Radius;
    }

    private static float SphereDistance(Vector3 point, Vector3 center, float radius, Vector3 fallback, out Vector3 normal)
    {
        var offset = point - center;
        var distance = offset.Length();
        normal = distance > 1e-6f ? offset / distance : Normalize(fallback, Vector3.UnitY);
        return distance - radius;
    }

    private static Vector3 Normalize(Vector3 value, Vector3 fallback) =>
        float.IsFinite(value.LengthSquared()) && value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : fallback;
}
