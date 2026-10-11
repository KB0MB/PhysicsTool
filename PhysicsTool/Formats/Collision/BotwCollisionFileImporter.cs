using HKX2;
using System.Collections;
using System.Numerics;
using System.Reflection;

namespace HKCLTool;

internal static class BotwCollisionFileImporter
{
    private static readonly byte[] HavokMagic = [0x57, 0xe0, 0xe0, 0x57, 0x10, 0xc0, 0xc0, 0x10];

    public static IReadOnlyList<BotwCollisionMesh> Import(string path, string? actorEntry = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("The collision file could not be found.", path);

        var meshes = new List<BotwCollisionMesh>();
        if (actorEntry != null || Path.GetExtension(path).Equals(".sbactorpack", StringComparison.OrdinalIgnoreCase))
        {
            var archive = OpenActorArchive(path, actorEntry);
            var entries = archive.Entries
                .Where(entry => entry.Name.EndsWith(".hkrb", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (entries.Length == 0)
                throw new InvalidDataException("The actor pack contains no HKRB collision files.");
            foreach (var entry in entries)
                ImportHavok(archive.Read(entry).ToArray(), Path.GetFileNameWithoutExtension(entry.Name), meshes);
        }
        else
        {
            var data = File.ReadAllBytes(path);
            if (data.AsSpan().StartsWith("Yaz0"u8))
                data = BotwYaz0.Decode(data);
            ImportHavok(FindCollisionPackfile(data), Path.GetFileNameWithoutExtension(path), meshes);
        }

        if (meshes.Count == 0)
            throw new InvalidDataException("The collision file contains no supported shape geometry.");
        return meshes;
    }

    private static BotwSarcArchive OpenActorArchive(string path, string? actorEntry)
    {
        var archive = BotwSarcArchive.Open(path);
        if (actorEntry == null) return archive;
        var entry = archive.Entries.SingleOrDefault(e => e.Name == actorEntry)
            ?? throw new InvalidDataException("The selected actor is missing from its container.");
        return BotwSarcArchive.Parse(archive.Read(entry).Span);
    }

    internal static BotwCollisionPlatform DetectPlatform(string path, string? actorEntry = null)
    {
        byte[] data;
        if (actorEntry != null || Path.GetExtension(path).Equals(".sbactorpack", StringComparison.OrdinalIgnoreCase))
        {
            var archive = OpenActorArchive(path, actorEntry);
            var entry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".hkrb", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("The actor pack contains no HKRB collision files.");
            data = archive.Read(entry).ToArray();
        }
        else data = File.ReadAllBytes(path);
        if (data.AsSpan().StartsWith("Yaz0"u8)) data = BotwYaz0.Decode(data);
        var header = FindCollisionPackfile(data);
        if (header.Length >= 20 && header[16] == 4 && header[17] == 0) return BotwCollisionPlatform.WiiU;
        if (header.Length >= 20 && header[16] == 8 && header[17] != 0) return BotwCollisionPlatform.Switch;
        throw new InvalidDataException("Unsupported collision platform layout.");
    }

    private static byte[] FindCollisionPackfile(byte[] data)
    {
        var offsets = new List<int>();
        for (var offset = 0; offset <= data.Length - HavokMagic.Length; offset++)
        {
            if (data.AsSpan(offset, HavokMagic.Length).SequenceEqual(HavokMagic))
                offsets.Add(offset);
        }
        if (offsets.Count == 0)
            throw new InvalidDataException("The file contains no BotW Havok packfile.");
        // HKSC stores its StaticCompoundInfo packfile first and its renderable
        // rigid-body graph second. A standalone HKRB has only one header.
        return data[offsets[^1]..];
    }

    private static void ImportHavok(byte[] data, string prefix, List<BotwCollisionMesh> meshes)
    {
        object root;
        try
        {
            root = new PackFileDeserializer().Deserialize(new BinaryReaderEx(data));
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("The BotW Havok collision could not be read.", ex);
        }

        var rigidBodies = FindObjects<hkpRigidBody>(root).ToArray();
        if (rigidBodies.Length == 0)
            throw new InvalidDataException("The Havok file contains no rigid bodies.");

        var unsupported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var body in rigidBodies)
        {
            var bodyName = string.IsNullOrWhiteSpace(body.m_name) ? prefix : body.m_name;
            ExtractShape(body.m_collidable?.m_shape, Matrix4x4.Identity, bodyName, meshes, unsupported, 0);
        }
        if (unsupported.Count > 0)
            throw new NotSupportedException("Unsupported collision shape(s): " + string.Join(", ", unsupported.OrderBy(name => name)) + ".");
    }

    private static IEnumerable<T> FindObjects<T>(object root) where T : class
    {
        var found = new List<T>();
        var pending = new Stack<object>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        pending.Push(root);
        while (pending.Count > 0)
        {
            var value = pending.Pop();
            if (!visited.Add(value))
                continue;
            if (value is T match)
                found.Add(match);
            if (value is string || value.GetType().IsPrimitive || value.GetType().IsEnum || value.GetType().IsValueType)
                continue;
            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                    if (item != null) pending.Push(item);
                continue;
            }
            foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (field.GetValue(value) is { } child) pending.Push(child);
        }
        return found;
    }

    private static void ExtractShape(
        hkpShape? shape,
        Matrix4x4 transform,
        string name,
        List<BotwCollisionMesh> meshes,
        HashSet<string> unsupported,
        int depth)
    {
        if (shape == null)
            return;
        if (depth > 32)
            throw new InvalidDataException("The collision shape graph is nested too deeply.");

        switch (shape)
        {
            case hkpListShape list:
                for (var index = 0; index < list.m_childInfo.Count; index++)
                    ExtractShape(list.m_childInfo[index].m_shape, transform, $"{name}_List_{index}", meshes, unsupported, depth + 1);
                break;
            case hkpStaticCompoundShape compound:
                for (var index = 0; index < compound.m_instances.Count; index++)
                {
                    var instance = compound.m_instances[index];
                    ExtractShape(instance.m_shape, DecodeStaticCompoundTransform(instance.m_transform) * transform, $"{name}_Instance_{index}", meshes, unsupported, depth + 1);
                }
                break;
            case hkpConvexTranslateShape translated:
                ExtractShape(
                    translated.m_childShape?.m_childShape,
                    Matrix4x4.CreateTranslation(ToVector3(translated.m_translation)) * transform,
                    name,
                    meshes,
                    unsupported,
                    depth + 1);
                break;
            case hkpConvexTransformShape transformed:
                ExtractShape(transformed.m_childShape?.m_childShape, transformed.m_transform * transform, name, meshes, unsupported, depth + 1);
                break;
            case hkpTransformShape transformed:
                ExtractShape(transformed.m_childShape?.m_childShape, transformed.m_transform * transform, name, meshes, unsupported, depth + 1);
                break;
            case hkpMoppBvTreeShape mopp:
                ExtractShape(mopp.m_child?.m_childShape, transform, name, meshes, unsupported, depth + 1);
                break;
            case hkpBvShape boundingVolume:
                ExtractShape(boundingVolume.m_childShape?.m_childShape, transform, name, meshes, unsupported, depth + 1);
                break;
            case hkpConvexListShape convexList:
                for (var index = 0; index < convexList.m_childShapes.Count; index++)
                    ExtractShape(convexList.m_childShapes[index], transform, $"{name}_ConvexList_{index}", meshes, unsupported, depth + 1);
                break;
            case hkpBvCompressedMeshShape compressed:
                ExtractCompressedMesh(compressed, transform, name, meshes);
                break;
            case hkpConvexVerticesShape convex:
                AddConvexMesh(convex, transform, name, meshes);
                break;
            case hkpBoxShape box:
                AddMesh(meshes, name + "_Box", CreateBox(ToVector3(box.m_halfExtents)), transform);
                break;
            case hkpSphereShape sphere:
                AddMesh(meshes, name + "_Sphere", CreateSphere(sphere.m_radius), transform);
                break;
            case hkpCapsuleShape capsule:
                AddMesh(meshes, name + "_Capsule", CreateCapsule(ToVector3(capsule.m_vertexA), ToVector3(capsule.m_vertexB), capsule.m_radius), transform);
                break;
            case hkpCylinderShape cylinder:
                AddMesh(meshes, name + "_Cylinder", CreateCylinder(ToVector3(cylinder.m_vertexA), ToVector3(cylinder.m_vertexB), cylinder.m_cylRadius), transform);
                break;
            case hkpMultiSphereShape multiSphere:
                var spheres = new[]
                {
                    multiSphere.m_spheres_0, multiSphere.m_spheres_1, multiSphere.m_spheres_2, multiSphere.m_spheres_3,
                    multiSphere.m_spheres_4, multiSphere.m_spheres_5, multiSphere.m_spheres_6, multiSphere.m_spheres_7
                };
                for (var index = 0; index < Math.Min(multiSphere.m_numSpheres, spheres.Length); index++)
                {
                    var sphere = spheres[index];
                    AddMesh(meshes, $"{name}_Sphere_{index}", CreateSphere(sphere.W), Matrix4x4.CreateTranslation(ToVector3(sphere)) * transform);
                }
                break;
            case hkpTriangleShape triangle:
                AddMesh(meshes, name + "_Triangle", (
                    new List<Vector3> { ToVector3(triangle.m_vertexA), ToVector3(triangle.m_vertexB), ToVector3(triangle.m_vertexC) },
                    new List<int> { 0, 1, 2 }), transform);
                break;
            case hkpSimpleMeshShape simpleMesh:
                var simpleVertices = simpleMesh.m_vertices.Select(ToVector3).ToList();
                var simpleIndices = new List<int>(simpleMesh.m_triangles.Count * 3);
                foreach (var triangleData in simpleMesh.m_triangles)
                    AddTriangle(simpleIndices, triangleData.m_a, triangleData.m_b, triangleData.m_c, simpleVertices.Count);
                AddMesh(meshes, name + "_SimpleMesh", (simpleVertices, simpleIndices), transform);
                break;
            default:
                unsupported.Add(shape.GetType().Name);
                break;
        }
    }

    private static void ExtractCompressedMesh(
        hkpBvCompressedMeshShape shape,
        Matrix4x4 transform,
        string name,
        List<BotwCollisionMesh> meshes)
    {
        var tree = shape.m_tree;
        var packed = tree.m_packedVertices.Select(DecodePackedVertex).ToArray();
        var domainMin = ToVector3(tree.m_domain.m_min);
        var domainScale = ToVector3(tree.m_domain.m_max) - domainMin;
        var shared = tree.m_sharedVertices.Select(value => DecodeSharedVertex(value, domainMin, domainScale)).ToArray();
        var material = shape.m_userStringPalette.FirstOrDefault(value =>
                BotwCollisionMaterials.Names.Contains(value, StringComparer.Ordinal))
            ?? BotwCollisionMaterials.Default;

        for (var sectionIndex = 0; sectionIndex < tree.m_sections.Count; sectionIndex++)
        {
            var section = tree.m_sections[sectionIndex];
            var vertices = new List<Vector3>(section.m_numPackedVertices + 32);
            var offset = new Vector3(section.m_codecParms_0, section.m_codecParms_1, section.m_codecParms_2);
            var scale = new Vector3(section.m_codecParms_3, section.m_codecParms_4, section.m_codecParms_5);
            for (var index = 0; index < section.m_numPackedVertices; index++)
            {
                var sourceIndex = checked((int)section.m_firstPackedVertex + index);
                if ((uint)sourceIndex >= packed.Length)
                    throw new InvalidDataException("A compressed collision section has an invalid packed-vertex range.");
                vertices.Add(packed[sourceIndex] * scale + offset);
            }

            var sharedRange = (Start: DecodeRangeStart(section.m_sharedVertices.m_data), Count: (int)section.m_numSharedIndices);
            for (var index = 0; index < sharedRange.Count; index++)
            {
                var lookupIndex = sharedRange.Start + index;
                if ((uint)lookupIndex >= tree.m_sharedVerticesIndex.Count)
                    throw new InvalidDataException($"A compressed collision section has an invalid shared-vertex range ({section.m_sharedVertices.m_data:X8}: {sharedRange.Start}+{sharedRange.Count}, available {tree.m_sharedVerticesIndex.Count}).");
                var sourceIndex = tree.m_sharedVerticesIndex[lookupIndex];
                if (sourceIndex >= shared.Length)
                    throw new InvalidDataException("A compressed collision section references an invalid shared vertex.");
                vertices.Add(shared[sourceIndex]);
            }

            var primitiveRange = DecodeRange(section.m_primitives.m_data);
            var indices = new List<int>(primitiveRange.Count * 6);
            for (var index = 0; index < primitiveRange.Count; index++)
            {
                var sourceIndex = primitiveRange.Start + index;
                if ((uint)sourceIndex >= tree.m_primitives.Count)
                    throw new InvalidDataException($"A compressed collision section has an invalid primitive range ({section.m_primitives.m_data:X8}: {primitiveRange.Start}+{primitiveRange.Count}, available {tree.m_primitives.Count}).");
                var primitive = tree.m_primitives[sourceIndex];
                AddTriangle(indices, primitive.m_indices_0, primitive.m_indices_1, primitive.m_indices_2, vertices.Count);
                if (primitive.m_indices_3 != primitive.m_indices_2)
                    AddTriangle(indices, primitive.m_indices_0, primitive.m_indices_2, primitive.m_indices_3, vertices.Count);
            }
            if (indices.Count == 0)
                continue;
            AddMesh(meshes, $"{name}_Mesh_{sectionIndex}", (vertices, indices), transform, material);
        }
    }

    private static (int Start, int Count) DecodeRange(uint value) => ((int)((value >> 8) & 0xffff), (int)(value & 0xff));
    private static int DecodeRangeStart(uint value) => (int)((value >> 8) & 0xffff);

    private static Vector3 DecodePackedVertex(uint value) => new(
        value & 0x7ff,
        (value >> 11) & 0x7ff,
        (value >> 22) & 0x3ff);

    private static Vector3 DecodeSharedVertex(ulong value, Vector3 offset, Vector3 scale)
    {
        const uint mask21 = 0x1fffff;
        const uint mask22 = 0x3fffff;
        var lo = (uint)(value >> 32);
        var hi = (uint)value;
        var mid = (lo << 11) | ((hi >> 21) & 0x7ff);
        return new Vector3(
            (hi & mask21) / (float)mask21 * scale.X + offset.X,
            (mid & mask21) / (float)mask21 * scale.Y + offset.Y,
            ((lo >> 10) & mask22) / (float)mask22 * scale.Z + offset.Z);
    }

    private static Matrix4x4 DecodeStaticCompoundTransform(Matrix4x4 packed)
    {
        var translation = new Vector3(packed.M11, packed.M12, packed.M13);
        var rotation = new Quaternion(packed.M21, packed.M22, packed.M23, packed.M24);
        rotation = rotation.LengthSquared() > 1e-12f ? Quaternion.Normalize(rotation) : Quaternion.Identity;
        var scale = new Vector3(packed.M31, packed.M32, packed.M33);
        if (!float.IsFinite(scale.X) || Math.Abs(scale.X) < 1e-8f) scale.X = 1.0f;
        if (!float.IsFinite(scale.Y) || Math.Abs(scale.Y) < 1e-8f) scale.Y = 1.0f;
        if (!float.IsFinite(scale.Z) || Math.Abs(scale.Z) < 1e-8f) scale.Z = 1.0f;
        return Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(translation);
    }

    private static void AddConvexMesh(hkpConvexVerticesShape shape, Matrix4x4 transform, string name, List<BotwCollisionMesh> meshes)
    {
        var vertices = DecodeConvexVertices(shape);
        var polygons = new List<IReadOnlyList<int>>();
        if (shape.m_connectivity?.m_numVerticesPerFace.Count > 0)
        {
            var offset = 0;
            foreach (var count in shape.m_connectivity.m_numVerticesPerFace)
            {
                if (count >= 3 && offset + count <= shape.m_connectivity.m_vertexIndices.Count)
                    polygons.Add(shape.m_connectivity.m_vertexIndices.Skip(offset).Take(count).Select(value => (int)value).ToArray());
                offset += count;
            }
        }
        else
        {
            var tolerance = Math.Max(0.0001f, vertices.Count == 0 ? 0.0001f : BoundsSize(vertices) * 0.001f);
            foreach (var plane in shape.m_planeEquations)
            {
                var normal = new Vector3(plane.X, plane.Y, plane.Z);
                var face = vertices.Select((vertex, index) => (vertex, index))
                    .Where(item => Math.Abs(Vector3.Dot(normal, item.vertex) + plane.W) <= tolerance)
                    .Select(item => item.index)
                    .Distinct()
                    .ToArray();
                if (face.Length >= 3)
                    polygons.Add(SortFace(vertices, face, normal));
            }
        }

        var indices = new List<int>();
        foreach (var polygon in polygons)
            for (var index = 1; index + 1 < polygon.Count; index++)
                AddTriangle(indices, polygon[0], polygon[index], polygon[index + 1], vertices.Count);
        if (vertices.Count <= 64)
        {
            var recovered = BuildConvexHullIndices(vertices);
            if (recovered.Count > indices.Count)
                indices = recovered;
        }
        if (indices.Count == 0)
            throw new InvalidDataException($"A convex collision shape could not be triangulated ({vertices.Count} vertices, {shape.m_planeEquations.Count} planes).");
        AddMesh(meshes, name + "_Convex", (vertices, indices), transform);
    }

    private static List<int> BuildConvexHullIndices(IReadOnlyList<Vector3> vertices)
    {
        var result = new List<int>();
        if (vertices.Count < 4)
            return result;
        var tolerance = Math.Max(0.00001f, BoundsSize(vertices) * 0.00001f);
        var planes = new List<(Vector3 Normal, float Distance)>();
        for (var a = 0; a < vertices.Count - 2; a++)
            for (var b = a + 1; b < vertices.Count - 1; b++)
                for (var c = b + 1; c < vertices.Count; c++)
                {
                    var normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    if (normal.LengthSquared() <= tolerance * tolerance)
                        continue;
                    normal = Vector3.Normalize(normal);
                    var distance = -Vector3.Dot(normal, vertices[a]);
                    var positive = false;
                    var negative = false;
                    foreach (var vertex in vertices)
                    {
                        var side = Vector3.Dot(normal, vertex) + distance;
                        positive |= side > tolerance;
                        negative |= side < -tolerance;
                        if (positive && negative) break;
                    }
                    if (positive && negative)
                        continue;
                    if (positive)
                    {
                        normal = -normal;
                        distance = -distance;
                    }
                    if (planes.Any(plane => Vector3.Dot(plane.Normal, normal) > 0.9999f && Math.Abs(plane.Distance - distance) <= tolerance * 4))
                        continue;
                    planes.Add((normal, distance));
                }

        foreach (var plane in planes)
        {
            var face = vertices.Select((vertex, index) => (vertex, index))
                .Where(item => Math.Abs(Vector3.Dot(plane.Normal, item.vertex) + plane.Distance) <= tolerance * 4)
                .Select(item => item.index)
                .Distinct()
                .ToArray();
            if (face.Length < 3)
                continue;
            var sorted = SortFace(vertices, face, plane.Normal);
            for (var index = 1; index + 1 < sorted.Length; index++)
                AddTriangle(result, sorted[0], sorted[index], sorted[index + 1], vertices.Count);
        }
        return result;
    }

    private static List<Vector3> DecodeConvexVertices(hkpConvexVerticesShape shape)
    {
        var columns = Decode(transposed: true);
        var rows = Decode(transposed: false);
        return PlaneMatchScore(columns, shape.m_planeEquations) >= PlaneMatchScore(rows, shape.m_planeEquations)
            ? columns
            : rows;

        List<Vector3> Decode(bool transposed)
        {
            var result = new List<Vector3>(Math.Max(0, shape.m_numVertices));
            for (var group = 0; result.Count < shape.m_numVertices; group++)
            {
                if (group >= shape.m_rotatedVertices.Count)
                    throw new InvalidDataException("A convex collision shape has incomplete vertex data.");
                var points = shape.m_rotatedVertices[group];
                for (var lane = 0; lane < 4 && result.Count < shape.m_numVertices; lane++)
                {
                    result.Add(transposed
                        ? new Vector3(MatrixComponent(points, 0, lane), MatrixComponent(points, 1, lane), MatrixComponent(points, 2, lane))
                        : new Vector3(MatrixComponent(points, lane, 0), MatrixComponent(points, lane, 1), MatrixComponent(points, lane, 2)));
                }
            }
            return result;
        }
    }

    private static int PlaneMatchScore(IReadOnlyList<Vector3> vertices, IReadOnlyList<Vector4> planes)
    {
        var score = 0;
        foreach (var vertex in vertices)
            foreach (var plane in planes)
            {
                var length = new Vector3(plane.X, plane.Y, plane.Z).Length();
                if (length > 1e-8f && Math.Abs((Vector3.Dot(new Vector3(plane.X, plane.Y, plane.Z), vertex) + plane.W) / length) < 0.002f)
                    score++;
            }
        return score;
    }

    private static int[] SortFace(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> face, Vector3 normal)
    {
        var center = Vector3.Zero;
        foreach (var index in face) center += vertices[index];
        center /= face.Count;
        normal = NormalizeOr(normal, Vector3.UnitY);
        var axisX = NormalizeOr(Vector3.Cross(Math.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX, normal), Vector3.UnitX);
        var axisY = Vector3.Cross(normal, axisX);
        return face.OrderBy(index => MathF.Atan2(Vector3.Dot(vertices[index] - center, axisY), Vector3.Dot(vertices[index] - center, axisX))).ToArray();
    }

    private static (List<Vector3> Vertices, List<int> Indices) CreateBox(Vector3 half)
    {
        var vertices = new List<Vector3>(8);
        for (var z = -1; z <= 1; z += 2)
            for (var y = -1; y <= 1; y += 2)
                for (var x = -1; x <= 1; x += 2)
                    vertices.Add(new Vector3(x * half.X, y * half.Y, z * half.Z));
        var indices = new List<int>
        {
            0,2,3, 0,3,1, 4,5,7, 4,7,6, 0,1,5, 0,5,4,
            2,6,7, 2,7,3, 0,4,6, 0,6,2, 1,3,7, 1,7,5
        };
        return (vertices, indices);
    }

    private static (List<Vector3> Vertices, List<int> Indices) CreateSphere(float radius, int slices = 20, int stacks = 12)
    {
        var vertices = new List<Vector3>((stacks + 1) * (slices + 1));
        for (var stack = 0; stack <= stacks; stack++)
        {
            var latitude = -MathF.PI / 2 + MathF.PI * stack / stacks;
            var ring = MathF.Cos(latitude) * radius;
            var y = MathF.Sin(latitude) * radius;
            for (var slice = 0; slice <= slices; slice++)
            {
                var angle = MathF.Tau * slice / slices;
                vertices.Add(new Vector3(MathF.Cos(angle) * ring, y, MathF.Sin(angle) * ring));
            }
        }
        var indices = new List<int>(stacks * slices * 6);
        for (var stack = 0; stack < stacks; stack++)
            for (var slice = 0; slice < slices; slice++)
            {
                var a = stack * (slices + 1) + slice;
                var b = a + slices + 1;
                indices.AddRange([a, b, a + 1, a + 1, b, b + 1]);
            }
        return (vertices, indices);
    }

    private static (List<Vector3> Vertices, List<int> Indices) CreateCapsule(Vector3 a, Vector3 b, float radius)
    {
        var result = CreateCylinder(a, b, radius, capEnds: false);
        AppendTransformed(result.Vertices, result.Indices, CreateSphere(radius), Matrix4x4.CreateTranslation(a));
        AppendTransformed(result.Vertices, result.Indices, CreateSphere(radius), Matrix4x4.CreateTranslation(b));
        return result;
    }

    private static (List<Vector3> Vertices, List<int> Indices) CreateCylinder(Vector3 a, Vector3 b, float radius, bool capEnds = true)
    {
        const int slices = 20;
        var axis = NormalizeOr(b - a, Vector3.UnitY);
        var side = NormalizeOr(Vector3.Cross(axis, Math.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX), Vector3.UnitX);
        var up = Vector3.Cross(side, axis);
        var vertices = new List<Vector3>(slices * 2 + (capEnds ? 2 : 0));
        for (var end = 0; end < 2; end++)
            for (var slice = 0; slice < slices; slice++)
            {
                var angle = MathF.Tau * slice / slices;
                vertices.Add((end == 0 ? a : b) + radius * (MathF.Cos(angle) * side + MathF.Sin(angle) * up));
            }
        var indices = new List<int>(slices * (capEnds ? 12 : 6));
        for (var slice = 0; slice < slices; slice++)
        {
            var next = (slice + 1) % slices;
            indices.AddRange([slice, slices + slice, next, next, slices + slice, slices + next]);
        }
        if (capEnds)
        {
            var centerA = vertices.Count;
            vertices.Add(a);
            var centerB = vertices.Count;
            vertices.Add(b);
            for (var slice = 0; slice < slices; slice++)
            {
                var next = (slice + 1) % slices;
                indices.AddRange([centerA, next, slice, centerB, slices + slice, slices + next]);
            }
        }
        return (vertices, indices);
    }

    private static void AppendTransformed(List<Vector3> vertices, List<int> indices, (List<Vector3> Vertices, List<int> Indices) source, Matrix4x4 transform)
    {
        var start = vertices.Count;
        vertices.AddRange(source.Vertices.Select(vertex => Vector3.Transform(vertex, transform)));
        indices.AddRange(source.Indices.Select(index => start + index));
    }

    private static void AddMesh(
        List<BotwCollisionMesh> meshes,
        string name,
        (List<Vector3> Vertices, List<int> Indices) geometry,
        Matrix4x4 transform,
        string? material = null)
    {
        if (geometry.Indices.Count == 0)
            return;
        var vertices = geometry.Vertices.Select(vertex => Vector3.Transform(vertex, transform)).ToArray();
        var indices = geometry.Indices.ToArray();
        if (transform.GetDeterminant() < 0)
            for (var index = 0; index < indices.Length; index += 3)
                (indices[index + 1], indices[index + 2]) = (indices[index + 2], indices[index + 1]);
        meshes.Add(new BotwCollisionMesh
        {
            Name = UniqueName(meshes, name),
            Vertices = vertices,
            Indices = indices,
            Material = string.IsNullOrWhiteSpace(material) ? BotwCollisionMaterials.Infer(name) : material
        });
    }

    private static string UniqueName(IReadOnlyList<BotwCollisionMesh> meshes, string name)
    {
        var clean = string.IsNullOrWhiteSpace(name) ? "Collision" : name;
        if (!meshes.Any(mesh => mesh.Name.Equals(clean, StringComparison.OrdinalIgnoreCase)))
            return clean;
        var suffix = 2;
        while (meshes.Any(mesh => mesh.Name.Equals($"{clean}_{suffix}", StringComparison.OrdinalIgnoreCase))) suffix++;
        return $"{clean}_{suffix}";
    }

    private static void AddTriangle(List<int> indices, int a, int b, int c, int vertexCount)
    {
        if ((uint)a >= vertexCount || (uint)b >= vertexCount || (uint)c >= vertexCount || a == b || b == c || a == c)
            return;
        indices.AddRange([a, b, c]);
    }

    private static float BoundsSize(IReadOnlyList<Vector3> vertices)
    {
        var min = vertices.Aggregate(Vector3.Min);
        var max = vertices.Aggregate(Vector3.Max);
        return (max - min).Length();
    }

    private static Vector3 NormalizeOr(Vector3 value, Vector3 fallback) =>
        value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : fallback;

    private static Vector3 ToVector3(Vector4 value) => new(value.X, value.Y, value.Z);
    private static float MatrixComponent(Matrix4x4 value, int row, int column) => (row, column) switch
    {
        (0, 0) => value.M11, (0, 1) => value.M12, (0, 2) => value.M13, (0, 3) => value.M14,
        (1, 0) => value.M21, (1, 1) => value.M22, (1, 2) => value.M23, (1, 3) => value.M24,
        (2, 0) => value.M31, (2, 1) => value.M32, (2, 2) => value.M33, (2, 3) => value.M34,
        (3, 0) => value.M41, (3, 1) => value.M42, (3, 2) => value.M43, _ => value.M44
    };
}
