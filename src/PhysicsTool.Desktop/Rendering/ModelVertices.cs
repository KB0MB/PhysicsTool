using System.Numerics;
using HKCLTool;
namespace PhysicsTool.Desktop.Rendering;
// Ported from the original viewport, preserving UV-seam tangent grouping.
public static class ModelVertices
{
    public static float[] Build(VisualModelMesh source)
    {
        const int sourceStride = 8;
        var totk = source.Material.Totk;
        int targetStride = totk == null ? 20 : 24;
        var vertexCount = source.Vertices.Length / sourceStride;
        var result = new float[checked(vertexCount * targetStride)];
        var albedo = ResolveUvCoordinates(source, source.Material.AlbedoUvSet);
        var spm = ResolveUvCoordinates(source, source.Material.SpmUvSet);
        var normal = ResolveUvCoordinates(source, source.Material.NormalUvSet);
        var ao = ResolveUvCoordinates(source, source.Material.AmbientOcclusionUvSet);
        var auxiliary = ResolveUvCoordinates(source, source.Material.AuxiliaryUvSet);
        var rawUvs = totk?.UvSets.Select(name => ResolveUvCoordinates(source, name)).ToArray();
        if (totk != null && totk.Integers.GetValueOrDefault("totkNormalSource", -1) is >= 0 and <= 7 and var normalSlot)
        {
            // Tangents must use the same transformed UVs as the normal texture.
            var srt = totk.Vectors[$"totkTextureSrt{normalSlot}"];
            var rotation = totk.Scalars[$"totkTextureRotation{normalSlot}"];
            var transformed = new float[vertexCount * 2];
            float c = MathF.Cos(rotation), s = MathF.Sin(rotation);
            for (int v = 0; v < vertexCount; v++)
            {
                var uv = ReadVisualModelUv(normal, source.Vertices, v) * new Vector2(srt.X, srt.Y);
                transformed[v * 2] = c * uv.X + s * uv.Y + srt.Z;
                transformed[v * 2 + 1] = -s * uv.X + c * uv.Y + srt.W;
            }
            normal = transformed;
        }

        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            var sourceOffset = vertex * sourceStride;
            var targetOffset = vertex * targetStride;
            Array.Copy(source.Vertices, sourceOffset, result, targetOffset, 6);
            CopyUv(albedo, source.Vertices, vertex, result, targetOffset + 6);
            CopyUv(spm, source.Vertices, vertex, result, targetOffset + 8);
            CopyUv(normal, source.Vertices, vertex, result, targetOffset + 10);
            CopyUv(ao, source.Vertices, vertex, result, targetOffset + 12);
            CopyUv(auxiliary, source.Vertices, vertex, result, targetOffset + 14);
            if (totk != null)
            {
                for (int uv = 0; uv < 4; uv++) CopyUv(rawUvs![uv], source.Vertices, vertex, result, targetOffset + 6 + uv * 2);
                var color = vertex < totk.VertexColors.Length ? totk.VertexColors[vertex] : Vector4.One;
                result[targetOffset + 20] = color.X; result[targetOffset + 21] = color.Y;
                result[targetOffset + 22] = color.Z; result[targetOffset + 23] = color.W;
            }
        }

        bool authored = source.Tangents.Length == vertexCount && source.TangentUvSet == source.Material.NormalUvSet;
        if (totk != null && totk.Integers.GetValueOrDefault("totkNormalSource", -1) is >= 0 and <= 7 and var slot)
            authored &= totk.Vectors[$"totkTextureSrt{slot}"] == new Vector4(1, 1, 0, 0) && totk.Scalars[$"totkTextureRotation{slot}"] == 0;
        if (authored)
        {
            for (int v = 0; v < vertexCount; v++)
            {
                var t = source.Tangents[v]; WriteVisualModelTangent(result, v, targetStride, new(t.X, t.Y, t.Z), t.W);
            }
        }
        else WriteVisualModelTangents(source, normal, result, targetStride);

        return result;
    }

    private static void WriteVisualModelTangents(
        VisualModelMesh source,
        IReadOnlyList<float>? normalUvs,
        float[] target,
        int targetStride)
    {
        const int sourceStride = 8;
        var vertexCount = source.Vertices.Length / sourceStride;
        var tangentGroups = new Dictionary<
            (Vector3 Position, Vector3 Normal, Vector2 Uv, int Orientation),
            (Vector3 Tangent, Vector3 Bitangent)>();
        var vertexOrientations = new int[vertexCount];
        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            var normal = ReadVisualModelVector(source.Vertices, vertex, 3);
            var reference = Math.Abs(normal.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;
            var tangent = NormalizeOrFallback(Vector3.Cross(reference, normal), Vector3.UnitX);
            WriteVisualModelTangent(target, vertex, targetStride, tangent, 1.0f);
        }

        for (var triangle = 0; triangle + 2 < source.Indices.Length; triangle += 3)
        {
            var first = checked((int)source.Indices[triangle]);
            var second = checked((int)source.Indices[triangle + 1]);
            var third = checked((int)source.Indices[triangle + 2]);
            if ((uint)first >= vertexCount || (uint)second >= vertexCount || (uint)third >= vertexCount)
                continue;

            var edge1 = ReadVisualModelVector(source.Vertices, second, 0)
                - ReadVisualModelVector(source.Vertices, first, 0);
            var edge2 = ReadVisualModelVector(source.Vertices, third, 0)
                - ReadVisualModelVector(source.Vertices, first, 0);
            var uv0 = ReadVisualModelUv(normalUvs, source.Vertices, first);
            var uv1 = ReadVisualModelUv(normalUvs, source.Vertices, second) - uv0;
            var uv2 = ReadVisualModelUv(normalUvs, source.Vertices, third) - uv0;
            var determinant = uv1.X * uv2.Y - uv1.Y * uv2.X;
            if (Math.Abs(determinant) < 0.00000001f)
                continue;

            var inverseDeterminant = 1.0f / determinant;
            var triangleTangent = (edge1 * uv2.Y - edge2 * uv1.Y) * inverseDeterminant;
            var triangleBitangent = (edge2 * uv1.X - edge1 * uv2.X) * inverseDeterminant;
            if (triangleTangent.LengthSquared() < 0.00000001f ||
                triangleBitangent.LengthSquared() < 0.00000001f)
                continue;

            triangleTangent = Vector3.Normalize(triangleTangent);
            triangleBitangent = Vector3.Normalize(triangleBitangent);
            var orientation = determinant < 0.0f ? -1 : 1;
            var triangleVertices = new[] { first, second, third };
            for (var corner = 0; corner < triangleVertices.Length; corner++)
            {
                var vertex = triangleVertices[corner];
                var normal = NormalizeOrFallback(
                    ReadVisualModelVector(source.Vertices, vertex, 3),
                    Vector3.UnitY);
                var position = ReadVisualModelVector(source.Vertices, vertex, 0);
                var uv = ReadVisualModelUv(normalUvs, source.Vertices, vertex);
                var previous = triangleVertices[(corner + 2) % 3];
                var next = triangleVertices[(corner + 1) % 3];
                var previousEdge = ReadVisualModelVector(source.Vertices, previous, 0) - position;
                var nextEdge = ReadVisualModelVector(source.Vertices, next, 0) - position;
                var cornerWeight = VisualModelCornerAngle(previousEdge, nextEdge);
                var key = (position, normal, uv, orientation);
                tangentGroups.TryGetValue(key, out var accumulated);
                tangentGroups[key] = (
                    accumulated.Tangent + triangleTangent * cornerWeight,
                    accumulated.Bitangent + triangleBitangent * cornerWeight);
                vertexOrientations[vertex] = orientation;
            }
        }

        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            var orientation = vertexOrientations[vertex];
            if (orientation == 0)
                continue;

            var normal = NormalizeOrFallback(
                ReadVisualModelVector(source.Vertices, vertex, 3),
                Vector3.UnitY);
            var key = (
                ReadVisualModelVector(source.Vertices, vertex, 0),
                normal,
                ReadVisualModelUv(normalUvs, source.Vertices, vertex),
                orientation);
            if (!tangentGroups.TryGetValue(key, out var accumulated))
                continue;

            // The FBX bridge expands every polygon corner. Recombine corners
            // that MikkTSpace considers one smooth tangent vertex, while the
            // UV and orientation keys keep seams and mirrored islands split.
            var tangent = accumulated.Tangent - normal * Vector3.Dot(normal, accumulated.Tangent);
            var tangentReference = Math.Abs(normal.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;
            tangent = NormalizeOrFallback(
                tangent,
                Vector3.Cross(tangentReference, normal));
            var handedness = Vector3.Dot(
                Vector3.Cross(normal, tangent),
                accumulated.Bitangent) < 0.0f
                ? -1.0f
                : 1.0f;
            WriteVisualModelTangent(target, vertex, targetStride, tangent, handedness);
        }
    }

    private static float VisualModelCornerAngle(Vector3 first, Vector3 second)
    {
        if (first.LengthSquared() < 0.00000001f || second.LengthSquared() < 0.00000001f)
            return 1.0f;

        first = Vector3.Normalize(first);
        second = Vector3.Normalize(second);
        return MathF.Acos(Math.Clamp(Vector3.Dot(first, second), -1.0f, 1.0f));
    }

    private static Vector3 ReadVisualModelVector(
        IReadOnlyList<float> vertices,
        int vertex,
        int componentOffset)
    {
        var offset = vertex * 8 + componentOffset;
        return new Vector3(vertices[offset], vertices[offset + 1], vertices[offset + 2]);
    }

    private static Vector2 ReadVisualModelUv(
        IReadOnlyList<float>? coordinates,
        IReadOnlyList<float> fallbackVertices,
        int vertex)
    {
        return coordinates != null && vertex * 2 + 1 < coordinates.Count
            ? new Vector2(coordinates[vertex * 2], coordinates[vertex * 2 + 1])
            : new Vector2(fallbackVertices[vertex * 8 + 6], fallbackVertices[vertex * 8 + 7]);
    }

    private static void WriteVisualModelTangent(
        float[] target,
        int vertex,
        int targetStride,
        Vector3 tangent,
        float handedness)
    {
        var offset = vertex * targetStride + 16;
        target[offset] = tangent.X;
        target[offset + 1] = tangent.Y;
        target[offset + 2] = tangent.Z;
        target[offset + 3] = handedness;
    }

    private static float[]? ResolveUvCoordinates(VisualModelMesh source, string name)
    {
        return source.UvSets.FirstOrDefault(uv =>
            uv.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Coordinates;
    }

    private static void CopyUv(
        IReadOnlyList<float>? coordinates,
        IReadOnlyList<float> fallbackVertices,
        int vertex,
        float[] target,
        int targetOffset)
    {
        if (coordinates != null && vertex * 2 + 1 < coordinates.Count)
        {
            target[targetOffset] = coordinates[vertex * 2];
            target[targetOffset + 1] = coordinates[vertex * 2 + 1];
            return;
        }

        target[targetOffset] = fallbackVertices[vertex * 8 + 6];
        target[targetOffset + 1] = fallbackVertices[vertex * 8 + 7];
    }

    private static Vector3 NormalizeOrFallback(Vector3 v, Vector3 fallback) => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : fallback;
}
