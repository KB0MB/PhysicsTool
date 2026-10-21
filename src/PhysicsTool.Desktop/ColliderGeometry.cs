using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Desktop;

public readonly record struct ColliderLine(Vector3 Start, Vector3 End);
public readonly record struct ColliderTriangle(Vector3 A, Vector3 B, Vector3 C);

public static class ColliderGeometry
{
    public static IReadOnlyList<ColliderTriangle> Surface(ColliderPreviewShape shape)
    {
        var triangles = new List<ColliderTriangle>();
        if (!Finite(shape.Start) || !Finite(shape.End) || shape.Kind == ColliderPreviewKind.Point) return triangles;
        var start = shape.Start; var end = shape.End;
        var r0 = Radius(shape.Radius); var r1 = shape.Kind == ColliderPreviewKind.TaperedCapsule ? Radius(shape.EndRadius) : r0;
        if (shape.Kind == ColliderPreviewKind.Plane)
        {
            var normal = Finite(shape.PlaneNormal) && shape.PlaneNormal.LengthSquared() > 1e-12f ? Vector3.Normalize(shape.PlaneNormal) : Vector3.UnitY;
            Basis(normal, out var side, out var up); side *= .5f; up *= .5f;
            triangles.Add(new(start - side - up, start + side - up, start + side + up));
            triangles.Add(new(start - side - up, start + side + up, start - side + up));
            return triangles;
        }
        var profile = CapsuleProfile.Create(shape);
        start = profile.Start; end = profile.End; r0 = profile.Radius; r1 = profile.EndRadius;
        var sphere = profile.IsSphere;
        var axis = profile.Axis;
        var joinAngle = -MathF.Asin(profile.Sine);
        Basis(axis, out var u, out var v);
        // Tapered end caps meet the side at tangent rings, not sphere equators.
        var rings = new List<(Vector3 Center, float Radius)>();
        const int latitude = 8, longitude = 32;
        for (int i = 0; i <= latitude; i++)
        {
            var angle = -MathF.PI / 2 + (joinAngle + MathF.PI / 2) * i / latitude;
            rings.Add((start + axis * (r0 * MathF.Sin(angle)), r0 * MathF.Cos(angle)));
        }
        if (!sphere) rings.Add((end - axis * (r1 * profile.Sine), r1 * profile.Cosine));
        for (int i = 1; i <= latitude; i++)
        {
            var angle = joinAngle + (MathF.PI / 2 - joinAngle) * i / latitude;
            rings.Add((end + axis * (r1 * MathF.Sin(angle)), r1 * MathF.Cos(angle)));
        }
        var vertices = new Vector3[rings.Count * (longitude + 1)];
        for (int j = 0; j <= longitude; j++)
        {
            var radial = u * MathF.Cos(MathF.Tau * j / longitude) + v * MathF.Sin(MathF.Tau * j / longitude);
            for (int i = 0; i < rings.Count; i++) vertices[i * (longitude + 1) + j] = rings[i].Center + rings[i].Radius * radial;
        }
        Vector3 Point(int ring, int j) => vertices[ring * (longitude + 1) + j];
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            if (Vector3.Cross(b - a, c - a).LengthSquared() > 1e-16f) triangles.Add(new(a, b, c));
        }
        for (int i = 0; i + 1 < rings.Count; i++)
            for (int j = 0; j < longitude; j++)
            {
                var a = Point(i, j); var b = Point(i, j + 1); var c = Point(i + 1, j + 1); var d = Point(i + 1, j);
                Triangle(a, b, c); Triangle(a, c, d);
            }
        return triangles;
    }

    public static IReadOnlyList<ColliderLine> Create(ColliderPreviewShape shape)
    {
        var lines = new List<ColliderLine>();
        if (!Finite(shape.Start) || !Finite(shape.End)) return lines;
        var a = shape.Start;
        var b = shape.End;
        var radius = Radius(shape.Radius);
        var endRadius = shape.Kind == ColliderPreviewKind.TaperedCapsule ? Radius(shape.EndRadius) : radius;
        void Line(Vector3 start, Vector3 end) => lines.Add(new(start, end));
        void Arc(Vector3 center, Vector3 u, Vector3 v, float r, float angle = MathF.Tau, float startAngle = 0, int segments = 32)
        {
            Vector3 Point(float t) => center + r * (u * MathF.Cos(t) + v * MathF.Sin(t));
            var previous = Point(startAngle);
            for (int i = 0; i < segments; i++)
            {
                var next = Point(startAngle + angle * (i + 1) / segments);
                Line(previous, next); previous = next;
            }
        }
        void Sphere(Vector3 center, float r)
        {
            Arc(center, Vector3.UnitX, Vector3.UnitY, r);
            Arc(center, Vector3.UnitX, Vector3.UnitZ, r);
            Arc(center, Vector3.UnitY, Vector3.UnitZ, r);
        }

        if (shape.Kind == ColliderPreviewKind.Plane)
        {
            var normal = Finite(shape.PlaneNormal) && shape.PlaneNormal.LengthSquared() > 1e-12f
                ? Vector3.Normalize(shape.PlaneNormal) : Vector3.UnitY;
            Basis(normal, out var u, out var v);
            u *= .5f; v *= .5f;
            Vector3[] corners = [a + u + v, a - u + v, a - u - v, a + u - v];
            for (int i = 0; i < 4; i++) Line(corners[i], corners[(i + 1) % 4]);
            Line(a, a + normal * .3f);
        }
        else if (shape.Kind == ColliderPreviewKind.Point)
        {
            const float size = .015f;
            Line(a - Vector3.UnitX * size, a + Vector3.UnitX * size);
            Line(a - Vector3.UnitY * size, a + Vector3.UnitY * size);
            Line(a - Vector3.UnitZ * size, a + Vector3.UnitZ * size);
        }
        else
        {
            var profile = CapsuleProfile.Create(shape);
            if (profile.IsSphere) { Sphere(profile.Start, profile.Radius); return lines; }
            var axis = profile.Axis;
            a = profile.Start; b = profile.End; radius = profile.Radius; endRadius = profile.EndRadius;
            Basis(axis, out var u, out var v);
            var aRing = a - axis * (radius * profile.Sine);
            var bRing = b - axis * (endRadius * profile.Sine);
            var joinAngle = -MathF.Asin(profile.Sine);
            Arc(aRing, u, v, radius * profile.Cosine);
            Arc(bRing, u, v, endRadius * profile.Cosine);
            foreach (var side in new[] { u, -u, v, -v })
                Line(aRing + side * (radius * profile.Cosine), bRing + side * (endRadius * profile.Cosine));
            foreach (var side in new[] { u, -u, v, -v })
            {
                Arc(a, side, axis, radius, joinAngle + MathF.PI / 2, -MathF.PI / 2, 16);
                Arc(b, side, axis, endRadius, MathF.PI / 2 - joinAngle, joinAngle, 16);
            }
        }
        return lines;
    }

    private static float Radius(float value) => float.IsFinite(value) ? Math.Max(0, value) : 0;
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private static void Basis(Vector3 axis, out Vector3 u, out Vector3 v)
    {
        u = Vector3.Normalize(Vector3.Cross(axis, Math.Abs(axis.Y) > .9f ? Vector3.UnitX : Vector3.UnitY));
        v = Vector3.Cross(axis, u);
    }
}
