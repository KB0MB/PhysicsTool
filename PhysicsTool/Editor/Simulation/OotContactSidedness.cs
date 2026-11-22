using System.Numerics;

namespace HKCLTool;

public sealed record OotContactSurface(IReadOnlyList<ParticlePreviewTriangle> Triangles,
    IReadOnlyList<byte> TriangleFlips, IReadOnlyList<int> SetupColliderSlots);

internal sealed class OotContactSidedness(OotContactSurface surface)
{
    private readonly Dictionary<int, Vector3> _normals = new();
    internal Vector3 Normal(int particle) => _normals.GetValueOrDefault(particle);

    internal void Update(IReadOnlyDictionary<int, Vector3> positions)
    {
        _normals.Clear();
        int vectorEnd = surface.Triangles.Count & ~3;
        for (int i = 0; i < surface.Triangles.Count; i++)
        {
            var triangle = surface.Triangles[i];
            var normal = Vector3.Cross(positions[triangle.ParticleB] - positions[triangle.ParticleA],
                positions[triangle.ParticleC] - positions[triangle.ParticleA]);
            if (i < vectorEnd)
            {
                if ((surface.TriangleFlips[i / 8] & (1 << (i & 7))) != 0) normal = -normal;
            }
            else
            {
                // 0x7101b1c510: the scalar remainder uses descending high bits
                // and 1 - 7 * bit, unlike the four-triangle sign-mask loop.
                int bits = surface.TriangleFlips[vectorEnd / 8] >> (vectorEnd & 4);
                normal *= 1 - 7 * ((bits >> (7 - (i - vectorEnd))) & 1);
            }
            Add(triangle.ParticleA, normal);
            Add(triangle.ParticleB, normal);
            Add(triangle.ParticleC, normal);
        }
        foreach (int p in _normals.Keys)
        {
            var normal = _normals[p];
            float squared = normal.LengthSquared();
            _normals[p] = squared <= 1.17549435e-38f ? Vector3.Zero : normal / MathF.Sqrt(squared);
        }
    }

    private void Add(int particle, Vector3 normal) => _normals[particle] = _normals.GetValueOrDefault(particle) + normal;

    internal static byte ChooseSide(int negative, int zero, int positive) =>
        zero > negative && zero > positive ? (byte)0 : positive > negative ? (byte)2 : (byte)1;

    internal static bool Accept(byte mode, float dot) => mode switch
    {
        0 => true,
        1 => dot < 0,
        2 => dot >= 0,
        _ => throw new InvalidDataException("Invalid OoT contact sidedness mode.")
    };
}
