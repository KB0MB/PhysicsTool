using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Desktop.Rendering;

internal sealed class ClothModelBinding
{
    private readonly Dictionary<string, Matrix4x4> _rest = new(StringComparer.OrdinalIgnoreCase);
    private static string Name(string name) => name[(name.LastIndexOf(':') + 1)..];
    internal void Capture(IEnumerable<BonePreviewPoint> bones)
    {
        foreach (var bone in bones) _rest.TryAdd(Name(bone.Name), Matrix(bone));
    }
    internal IReadOnlyList<VisualBoneOverride> Overrides(VisualModelData model, ParticlePreviewData physics,
        IReadOnlyDictionary<int, SimulatedBonePreviewPose>? simulated = null)
    {
        var visual = model.Bones.GroupBy(b => Name(b.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var result = new List<VisualBoneOverride>();
        foreach (var bone in physics.Bones)
        {
            var name = Name(bone.Name);
            if (!visual.TryGetValue(name, out var target) || !_rest.TryGetValue(name, out var rest) || !Matrix4x4.Invert(rest, out var inverse)) continue;
            var current = simulated != null && simulated.TryGetValue(bone.Index, out var pose) ? Matrix(pose) : Matrix(bone);
            // Retain the imported bind offset instead of snapping visual bones to physics origins.
            var delta = inverse * current;
            if (simulated == null && NearIdentity(delta)) continue;
            var world = Matrix4x4.CreateScale(target.Scale) * Matrix4x4.CreateFromQuaternion(target.Rotation) *
                Matrix4x4.CreateTranslation(target.Position) * delta;
            if (Matrix4x4.Decompose(world, out var scale, out var rotation, out var position))
                result.Add(new(target.Name, position, Quaternion.Normalize(rotation), scale));
        }
        return result;
    }
    private static bool NearIdentity(Matrix4x4 m) =>
        Vector3.DistanceSquared(new(m.M11, m.M12, m.M13), Vector3.UnitX) +
        Vector3.DistanceSquared(new(m.M21, m.M22, m.M23), Vector3.UnitY) +
        Vector3.DistanceSquared(new(m.M31, m.M32, m.M33), Vector3.UnitZ) +
        new Vector3(m.M41, m.M42, m.M43).LengthSquared() < 1e-12f;
    internal static Matrix4x4 Matrix(BonePreviewPoint b) => Basis(b.Position, b.AxisX, b.AxisY, b.AxisZ);
    private static Matrix4x4 Matrix(SimulatedBonePreviewPose b) => Basis(b.Position, b.AxisX, b.AxisY, b.AxisZ);
    private static Matrix4x4 Basis(Vector3 p, Vector3 x, Vector3 y, Vector3 z) => new(
        x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, p.X, p.Y, p.Z, 1);
}
