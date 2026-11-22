using System.Numerics;

namespace HKCLTool;

public sealed record NativeVirtualCollisionPoint(int ParticleA, int ParticleB, int ParticleC, Vector3 Weights, int Section = 0);
public sealed record NativeVirtualCollisionData(IReadOnlyList<NativeVirtualCollisionPoint> Points, float RadiusScale);

public sealed partial class NativeBphclDocument
{
    // Expand the authored fan/dictionary representation; never invent surface samples.
    public NativeVirtualCollisionData ReadVirtualCollisionData(int simulationItemIndex)
    {
        var sim = GetItem(simulationItemIndex);
        var member = TypeDefinitions.Single(t => t.TypeIndex == sim.TypeIndex).Members.Single(m => m.Name == "virtualCollisionPointsData");
        var layout = TypeDefinitions.Single(t => t.TypeIndex == member.TypeIndex);
        uint At(string name) => checked(sim.DataOffset + member.Offset + layout.Members.Single(m => m.Name == name).Offset);
        NativeBphclItem? Array(string name) => ReadArrayItem(At(name));
        var declared = ReadUInt16(At("numVCPoints"));
        float radiusScale = IsOot ? ReadSingle(MemberAddress(sim, "virtualCollisionPointsRadiusScale")) : 1;
        if (!float.IsFinite(radiusScale) || radiusScale < 0) throw new InvalidDataException("Invalid virtual collision radius scale.");
        if (ReadUInt16(At("numLandscapeVCPoints")) != 0 || Array("blocks") is { Count: > 0 } ||
            Array("edgesLandscape") is { Count: > 0 } || Array("trianglesLandscape") is { Count: > 0 } ||
            Array("edgeFansLandscape") is { Count: > 0 } || Array("triangleFansLandscape") is { Count: > 0 })
            throw new InvalidDataException("Packed or landscape virtual collision points are not supported yet.");
        var particles = ReadArrayItem(sim.DataOffset + 64)?.Count ?? 0;
        var points = new List<NativeVirtualCollisionPoint>();
        int sectionId = 0;

        void Expand(bool triangle)
        {
            string prefix = triangle ? "triangle" : "edge";
            var fans = Array(prefix + "Fans");
            var sections = Array(triangle ? "triangles" : "edges");
            var dictionary = Array(prefix + "DictionaryEntries");
            var barycentrics = Array(prefix + "BarycentricsDictionary");
            var indices = ReadUInt16Array(Array(prefix + "FanIndices"));
            if (fans is null || fans.Count == 0)
            {
                if (sections is { Count: > 0 }) throw new InvalidDataException("Virtual collision sections have no fan owners.");
                return;
            }
            if (sections == null || dictionary == null || barycentrics == null || indices.Count != particles)
                throw new InvalidDataException("Incomplete virtual collision fan data.");
            if (fans.Count > particles || sections.Count > declared || dictionary.Count > 65536 || barycentrics.Count > 65790)
                throw new InvalidDataException("Virtual collision arrays exceed their index ranges.");
            void Bounds(NativeBphclItem item, uint stride)
            {
                if ((ulong)item.DataOffset + (ulong)item.Count * stride > (ulong)DataSection!.PayloadSize)
                    throw new InvalidDataException("Virtual collision array exceeds DATA.");
            }
            Bounds(fans, 6); Bounds(sections, triangle ? 6u : 4u);
            Bounds(dictionary, 4); Bounds(barycentrics, triangle ? 8u : 4u);
            var seen = new HashSet<uint>();
            for (uint f = 0; f < fans.Count; f++)
            {
                uint fan = checked(fans.DataOffset + f * 6);
                int a = ReadUInt16(fan), start = ReadUInt16(fan + 2), count = ReadByte(fan + 4);
                if (a >= particles || indices[a] != f || (ulong)start + (uint)count > sections.Count)
                    throw new InvalidDataException("Virtual collision fan has an invalid particle or section range.");
                for (uint section = (uint)start; section < start + count; section++)
                {
                    sectionId++;
                    if (!seen.Add(section)) throw new InvalidDataException("Virtual collision fan sections overlap.");
                    uint at = checked(sections.DataOffset + section * (triangle ? 6u : 4u));
                    int b = ReadUInt16(at), c = triangle ? ReadUInt16(at + 2) : -1;
                    uint entry = ReadUInt16(at + (triangle ? 4u : 2u));
                    if (b >= particles || a == b || (triangle && (c >= particles || c == a || c == b)) || entry >= dictionary.Count)
                        throw new InvalidDataException("Virtual collision sample references an invalid particle or dictionary entry.");
                    uint dict = checked(dictionary.DataOffset + entry * 4);
                    uint first = ReadUInt16(dict), length = ReadByte(dict + 2);
                    if (length == 0 || (ulong)first + length > barycentrics.Count)
                        throw new InvalidDataException("Invalid virtual collision barycentric range.");
                    for (uint k = first; k < first + length; k++)
                    {
                        uint bary = checked(barycentrics.DataOffset + k * (triangle ? 8u : 4u));
                        float u = ReadSingle(bary), v = triangle ? ReadSingle(bary + 4) : 0;
                        var weights = triangle ? new Vector3(1 - u - v, u, v) : new Vector3(1 - u, u, 0);
                        if (!float.IsFinite(u) || !float.IsFinite(v) || weights.X < 0 || weights.Y < 0 || weights.Z < 0)
                            throw new InvalidDataException("Invalid virtual collision barycentric weights.");
                        points.Add(new(a, b, c, weights, sectionId));
                        if (points.Count > declared) throw new InvalidDataException("Virtual collision point count exceeds its declaration.");
                    }
                }
            }
            if (seen.Count != sections.Count) throw new InvalidDataException("Unowned virtual collision sections.");
            for (int p = 0; p < indices.Count; p++)
                if (indices[p] != ushort.MaxValue && (indices[p] >= fans.Count || ReadUInt16(fans.DataOffset + indices[p] * 6u) != p))
                    throw new InvalidDataException("Virtual collision fan index does not match its owner.");
        }

        Expand(false);
        Expand(true);
        if (points.Count != declared) throw new InvalidDataException("Virtual collision point count does not match its fan data.");
        return new(points, radiusScale);
    }
}
