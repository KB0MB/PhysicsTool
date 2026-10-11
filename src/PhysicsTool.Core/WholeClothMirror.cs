using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Core;

public static class WholeClothMirror
{
    public static string SwapSide(string name)
    {
        var chars = name.ToCharArray();
        static bool Separator(char c) => c is '_' or ':' or '-' or '.';
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] is 'L' or 'R' && (i == 0 || Separator(chars[i - 1])) && (i == chars.Length - 1 || Separator(chars[i + 1])))
                chars[i] = chars[i] == 'L' ? 'R' : 'L';
        return new(chars);
    }
    public static void Apply(HkclService doc, int cloth)
    {
        doc.ValidateWholeClothMirror(cloth);
        var snapshot = doc.CaptureState(); var native = doc.CaptureBphclNativeDocument();
        var reflection = Matrix4x4.CreateScale(-1, 1, 1);
        var source = doc.GetBoneRows(cloth); var worlds = BoneEditGeometry.WorldTransforms(source);
        var rows = source.ToDictionary(b => b.Index, SelectionClipboard.Clone);
        var after = new Dictionary<int, Matrix4x4>(); var branches = new Dictionary<int, bool>();
        Matrix4x4 Visit(int index)
        {
            if (after.TryGetValue(index, out var ready)) return ready;
            var b = rows[index]; var parent = b.ParentIndex < 0 ? Matrix4x4.Identity : Visit(b.ParentIndex);
            var name = SwapSide(b.Name);
            var mirror = !doc.IsBphcl || name != b.Name || branches.GetValueOrDefault(b.ParentIndex);
            branches[index] = mirror;
            if (!Matrix4x4.Invert(parent, out var inverse)) throw new InvalidOperationException("A mirrored parent transform is singular.");
            var positionWorld = reflection * worlds[index] * reflection;
            var rotationReflection = doc.IsBphcl ? Matrix4x4.CreateScale(1, 1, -1) : reflection;
            var rotationWorld = mirror ? rotationReflection * worlds[index] * rotationReflection : worlds[index];
            if (!Matrix4x4.Decompose(positionWorld * inverse, out var scale, out _, out var position) ||
                !Matrix4x4.Decompose(rotationWorld * inverse, out _, out var rotation, out _))
                throw new InvalidOperationException("A mirrored bone transform cannot be decomposed.");
            b.Name = name; b.X = position.X; b.Y = position.Y; b.Z = position.Z;
            b.RotationX = rotation.X; b.RotationY = rotation.Y; b.RotationZ = rotation.Z; b.RotationW = rotation.W;
            b.ScaleX = scale.X; b.ScaleY = scale.Y; b.ScaleZ = scale.Z;
            return after[index] = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position) * parent;
        }
        foreach (var b in source) Visit(b.Index);
        var particles = doc.GetParticleRows(cloth);
        foreach (var p in particles) p.X = -p.X;
        var colliders = doc.GetColliderRows(cloth).Select(c => SelectionMirror.ReflectCollider(c, 0, Vector3.Zero)).ToArray();
        foreach (var c in colliders) { c.Name = SwapSide(c.Name); if (rows.TryGetValue(c.BoneIndex, out var b)) c.BoneName = b.Name; }
        try
        {
            doc.UpdateBoneRows(cloth, rows.Values, false);
            doc.UpdateParticleRows(cloth, particles);
            doc.FlipTriangleWinding(cloth);
            doc.MirrorClothConstraintOrientation(cloth);
            if (doc.IsBphcl) doc.MirrorBphclClothGeometryAcrossX(cloth, native);
            else doc.MirrorHkclReferenceGeometryAcrossX(cloth);
            doc.UpdateColliderRows(colliders);
        }
        catch { doc.RestoreState(snapshot); throw; }
    }
}
