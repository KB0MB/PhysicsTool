using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using HKX2;

namespace HKCLTool;

internal sealed record BotwCollisionBuildResult(
    string OutputPath,
    int SourceMeshCount,
    int ShapeCount,
    int TriangleCount,
    long FileSize);

internal static class BotwCollisionBuilder
{
    private const int ElementLimit = 126;

    public static BotwCollisionBuildResult Build(
        IReadOnlyList<BotwCollisionMesh> meshes,
        BotwCollisionBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("Choose an output file.", nameof(options));
        if (!float.IsFinite(options.Scale) || options.Scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Scale must be greater than zero.");
        if (!IsFinite(options.Offset))
            throw new ArgumentOutOfRangeException(nameof(options), "Offset values must be finite.");

        var selected = meshes.Where(mesh => mesh.Included).ToArray();
        if (selected.Length == 0)
            throw new InvalidOperationException("Select at least one collision mesh.");
        var chunks = selected.SelectMany(mesh => CreateChunks(mesh, options.Scale, options.Offset)).ToArray();
        if (chunks.Length == 0)
            throw new InvalidOperationException("The selected meshes contain no usable triangles.");

        if (options.Target == BotwCollisionTarget.StaticCompound && options.Platform != BotwCollisionPlatform.WiiU)
            throw new InvalidOperationException("Switch static-compound output is not supported yet. Choose Actor rigid body for Switch collision.");
        if (options.Target == BotwCollisionTarget.ActorRigidBody && options.Yaz0Compress)
            throw new InvalidOperationException("Actor HKRB data is stored uncompressed inside an actor pack.");

        var raw = options.Target == BotwCollisionTarget.StaticCompound
            ? BuildStaticCompound(chunks)
            : BuildActorRigidBody(chunks, options.RigidBodyName, options.OutputPath);
        if (options.Platform == BotwCollisionPlatform.Switch)
            raw = SerializeForSwitch(raw);
        if (raw.Length < 32)
            throw new InvalidDataException("The collision writer returned an incomplete Havok file.");
        ValidateHavok(raw);
        var output = options.Yaz0Compress ? BotwYaz0.Encode(raw) : raw;
        if (options.Yaz0Compress && !BotwYaz0.Decode(output).AsSpan().SequenceEqual(raw))
            throw new InvalidDataException("Yaz0 verification failed; no output was written.");

        var fullPath = Path.GetFullPath(options.OutputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        AtomicFile.WriteAllBytes(fullPath, output);

        return new BotwCollisionBuildResult(
            fullPath,
            selected.Length,
            chunks.Length,
            chunks.Sum(chunk => chunk.Indices.Count / 3),
            output.LongLength);
    }

    private static byte[] BuildStaticCompound(IReadOnlyList<CollisionChunk> chunks)
    {
        var root = JsonNode.Parse(IceHailRuntime.ReadCompoundTemplate())?.AsObject()
            ?? throw new InvalidDataException("The embedded collision template is invalid.");
        var actors = root["compound"]?["actors"]?.AsArray()
            ?? throw new InvalidDataException("The embedded collision template has no actor list.");
        var compoundChildren = root["data"]?["children"]?[0]?["children"]?[0]?["children"]?[0]?["children"]?[0]?["children"]?.AsArray()
            ?? throw new InvalidDataException("The embedded collision template has no compound shape.");

        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            actors.Add(new JsonObject
            {
                ["id"] = index,
                ["hashId"] = index.ToString("x8", CultureInfo.InvariantCulture),
                ["strHash"] = "00000000"
            });
            compoundChildren.Add(new JsonObject
            {
                ["type"] = "hkpBvCompressedMeshShape",
                ["actorId"] = index,
                ["materials"] = new JsonArray(JsonValue.Create(chunk.Material)),
                ["parts"] = new JsonArray(new JsonObject
                {
                    ["vertices"] = ToVertexJson(chunk.Vertices),
                    ["indices"] = ToTriangleJson(chunk.Indices)
                })
            });
        }

        return IceHailRuntime.Generate(root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private static byte[] BuildActorRigidBody(
        IReadOnlyList<CollisionChunk> chunks,
        string? requestedName,
        string outputPath)
    {
        var shapeGroups = chunks
            .GroupBy(chunk => chunk.Material, StringComparer.Ordinal)
            .Select(group => new JsonObject
            {
                ["type"] = "hkpBvCompressedMeshShape",
                ["materials"] = new JsonArray(JsonValue.Create(group.Key)),
                ["parts"] = new JsonArray(group.Select(chunk => (JsonNode)new JsonObject
                {
                    ["vertices"] = ToVertexJson(chunk.Vertices),
                    ["indices"] = ToTriangleJson(chunk.Indices)
                }).ToArray())
            })
            .ToArray();
        JsonNode shape = shapeGroups.Length == 1
            ? shapeGroups[0]
            : new JsonObject
            {
                ["type"] = "hkpListShape",
                ["children"] = new JsonArray(shapeGroups.Cast<JsonNode>().ToArray())
            };
        var rigidBodyName = string.IsNullOrWhiteSpace(requestedName)
            ? Path.GetFileNameWithoutExtension(outputPath)
            : requestedName.Trim();
        if (rigidBodyName.Length == 0)
            rigidBodyName = "RigidBody_0";

        var root = new JsonObject
        {
            ["version"] = 4,
            ["data"] = new JsonObject
            {
                ["type"] = "hkRootLevelContainer",
                ["name"] = "Physics Data",
                ["physicsClass"] = "hkpPhysicsData",
                ["children"] = new JsonArray(new JsonObject
                {
                    ["type"] = "hkpPhysicsData",
                    ["children"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "hkpPhysicsSystem",
                        ["name"] = "Default Physics System",
                        ["children"] = new JsonArray(new JsonObject
                        {
                            ["type"] = "hkpRigidBody",
                            ["name"] = rigidBodyName,
                            ["children"] = new JsonArray(shape)
                        })
                    })
                })
            }
        };
        return IceHailRuntime.Generate(root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private static byte[] SerializeForSwitch(byte[] wiiUData)
    {
        using var input = new MemoryStream(wiiUData, writable: false);
        var root = new PackFileDeserializer().Deserialize(new BinaryReaderEx(input));
        if (root == null)
            throw new InvalidDataException("The generated actor collision has no Havok root object.");
        using var output = new MemoryStream();
        new PackFileSerializer().Serialize(root, new BinaryWriterEx(output), HKXHeader.BotwNx());
        return output.ToArray();
    }

    private static IEnumerable<CollisionChunk> CreateChunks(BotwCollisionMesh mesh, float scale, Vector3 offset)
    {
        var triangles = Enumerable.Range(0, mesh.Indices.Count / 3)
            .Select(index => new[]
            {
                Transform(mesh.Vertices[mesh.Indices[index * 3]], scale, offset),
                Transform(mesh.Vertices[mesh.Indices[index * 3 + 1]], scale, offset),
                Transform(mesh.Vertices[mesh.Indices[index * 3 + 2]], scale, offset)
            })
            .OrderBy(triangle => triangle.Min(vertex => vertex.X))
            .ThenBy(triangle => triangle.Min(vertex => vertex.Z))
            .ThenBy(triangle => triangle.Min(vertex => vertex.Y));

        var vertices = new List<Vector3>(ElementLimit);
        var indices = new List<int>(ElementLimit * 3);
        var vertexMap = new Dictionary<Vector3, int>();
        foreach (var triangle in triangles)
        {
            var newVertices = triangle.Count(vertex => !vertexMap.ContainsKey(vertex));
            if (indices.Count > 0 &&
                (vertices.Count + newVertices > ElementLimit - 3 || indices.Count / 3 >= ElementLimit))
            {
                yield return new CollisionChunk(mesh.Material, vertices, indices);
                vertices = new List<Vector3>(ElementLimit);
                indices = new List<int>(ElementLimit * 3);
                vertexMap = new Dictionary<Vector3, int>();
            }

            foreach (var vertex in triangle)
            {
                if (!vertexMap.TryGetValue(vertex, out var index))
                {
                    index = vertices.Count;
                    vertexMap.Add(vertex, index);
                    vertices.Add(vertex);
                }
                indices.Add(index);
            }
        }
        if (indices.Count > 0)
            yield return new CollisionChunk(mesh.Material, vertices, indices);
    }

    private static Vector3 Transform(Vector3 vertex, float scale, Vector3 offset) => vertex * scale + offset;

    private static JsonArray ToVertexJson(IReadOnlyList<Vector3> vertices)
    {
        var result = new JsonArray();
        foreach (var vertex in vertices)
            result.Add(new JsonArray(vertex.X, vertex.Y, vertex.Z));
        return result;
    }

    private static JsonArray ToTriangleJson(IReadOnlyList<int> indices)
    {
        var result = new JsonArray();
        for (var index = 0; index < indices.Count; index += 3)
            result.Add(new JsonArray(indices[index], indices[index + 1], indices[index + 2]));
        return result;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static void ValidateHavok(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        var root = new PackFileDeserializer().Deserialize(new BinaryReaderEx(stream));
        if (root == null)
            throw new InvalidDataException("The generated Havok file has no root object.");
    }

    private sealed record CollisionChunk(
        string Material,
        IReadOnlyList<Vector3> Vertices,
        IReadOnlyList<int> Indices);
}
