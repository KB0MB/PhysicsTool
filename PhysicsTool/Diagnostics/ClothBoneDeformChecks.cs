using System.Numerics;
using System.Reflection;

namespace HKCLTool;

internal static class ClothBoneDeformChecks
{
    public static object Measure(ParticlePreviewData source, HkclPreviewSimulator solver)
    {
        var positions = solver.GetPositions();
        var poses = solver.GetBonePoses();
        var errors = new List<(float Position, float Degrees, float Axis)>();
        foreach (var binding in source.BoneBindings)
        {
            if (!poses.TryGetValue(binding.BoneIndex, out var pose)) continue;
            var native = Evaluate(binding.LocalBoneTransform, positions[binding.ParticleA],
                positions[binding.ParticleB], positions[binding.ParticleC]);
            if (!float.IsFinite(native.M11)) continue;
            var actual = new Matrix4x4(pose.AxisX.X, pose.AxisX.Y, pose.AxisX.Z, 0,
                pose.AxisY.X, pose.AxisY.Y, pose.AxisY.Z, 0, pose.AxisZ.X, pose.AxisZ.Y, pose.AxisZ.Z, 0,
                pose.Position.X, pose.Position.Y, pose.Position.Z, 1);
            var delta = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(native))
                * Quaternion.Conjugate(Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(actual)));
            var angle = 2 * MathF.Atan2(new Vector3(delta.X, delta.Y, delta.Z).Length(), MathF.Abs(delta.W)) * 180 / MathF.PI;
            var axisError = Math.Max(Vector3.Distance(new(native.M11, native.M12, native.M13), pose.AxisX),
                Math.Max(Vector3.Distance(new(native.M21, native.M22, native.M23), pose.AxisY),
                    Vector3.Distance(new(native.M31, native.M32, native.M33), pose.AxisZ)));
            var positionError = Vector3.Distance(native.Translation, pose.Position);
            if (binding.UseNativeSimpleDeformer)
                Require(positionError < 0.00001f && axisError < 0.0001f,
                    $"Native deformer mismatch for bone {binding.BoneIndex}: position={positionError}, axis={axisError}, angle={angle}.");
            errors.Add((positionError, angle, axisError));
        }
        return new { Bindings = errors.Count, solver.LocalFirstBoneBindingCount, solver.NativeCentroidBoneBindingCount,
            MeanPositionError = errors.Count == 0 ? 0 : errors.Average(e => e.Position),
            MaximumPositionError = errors.Count == 0 ? 0 : errors.Max(e => e.Position),
            MeanRotationDegrees = errors.Count == 0 ? 0 : errors.Average(e => e.Degrees),
            MaximumRotationDegrees = errors.Count == 0 ? 0 : errors.Max(e => e.Degrees),
            MaximumAxisError = errors.Count == 0 ? 0 : errors.Max(e => e.Axis) };
    }

    public static void Run(string directory)
    {
        var source = new ParticlePreviewData { Gravity = Vector3.Zero };
        var rest = new[] { new Vector3(-0.7f, 0, 0), new Vector3(0.5f, -0.4f, 0), new Vector3(0.2f, 0.4f, 0) };
        for (var i = 0; i < 3; i++) source.Particles.Add(new() { Index = i, Position = rest[i], InverseMass = 1 });
        var center = (rest[0] + rest[1] + rest[2]) / 3;
        var u = rest[0] - center; var v = rest[1] - center; var n = Vector3.Cross(u, v);
        var frame = new Matrix4x4(u.X, u.Y, u.Z, 0, v.X, v.Y, v.Z, 0, n.X, n.Y, n.Z, 0,
            center.X, center.Y, center.Z, 1);
        Require(Matrix4x4.Invert(frame, out var inverse), "Synthetic triangle is singular.");
        var bone = Matrix4x4.CreateFromYawPitchRoll(0.4f, 0.8f, -0.3f) * Matrix4x4.CreateTranslation(0.1f, 0.2f, 0.3f);
        // Deliberately use a different skeleton rest pose: native authored
        // transforms remain authoritative, rather than being refitted to it.
        source.Bones.Add(new() { Index = 0, ParentIndex = -1, Position = new(0.4f, 0.2f, -0.1f),
            AxisX = Vector3.UnitX, AxisY = Vector3.UnitY, AxisZ = Vector3.UnitZ });
        source.BoneBindings.Add(new() { BoneIndex = 0, ParticleA = 0, ParticleB = 1, ParticleC = 2,
            BoneAxis = 3, UseNativeSimpleDeformer = true, LocalBoneTransform = bone * inverse });
        var solver = new HkclPreviewSimulator(source);
        var particles = (System.Collections.IDictionary)typeof(HkclPreviewSimulator)
            .GetField("_particles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(solver)!;
        void Set(Vector3[] points)
        {
            for (var i = 0; i < 3; i++)
                particles[i]!.GetType().GetProperty("Position")!.SetValue(particles[i], points[i]);
        }
        foreach (var scale in new[] { 0.04f, 0.2f, 0.8f, 1f, 2f, 5f })
        foreach (var angle in new[] { -2.9f, -1f, 0f, 1.7f, 3.1f })
        {
            var shear = Matrix4x4.Identity; shear.M12 = 0.7f; shear.M31 = -0.4f;
            var transform = Matrix4x4.CreateScale(scale, 1.3f, 0.7f) * shear
                * Matrix4x4.CreateFromYawPitchRoll(angle, 0.6f, -0.2f) * Matrix4x4.CreateTranslation(0.1f, 0.5f, -0.2f);
            Set(rest.Select(p => Vector3.Transform(p, transform)).ToArray());
            Measure(source, solver);
            var first = solver.GetBonePoses()[0];
            var repeat = solver.GetBonePoses()[0];
            Require(Vector3.Distance(first.AxisX, repeat.AxisX) < 1e-7f, "Reading a pose altered deformer rotation.");
        }
        var previous = solver.GetBonePoses()[0];
        Set(new[] { Vector3.One, Vector3.One, Vector3.One });
        var collapsed = solver.GetBonePoses()[0];
        Require(float.IsFinite(collapsed.Position.LengthSquared()) && Vector3.Distance(previous.AxisZ, collapsed.AxisZ) < 0.00001f,
            "Collapsed triangle did not retain a finite previous frame.");
        Set(rest); Measure(source, solver);
        solver.Reset(); Measure(source, solver);

        // Continuous triangle deformation must not switch the preserved axis
        // when transformed row lengths cross, even in reverse playback.
        var forward = new List<Vector3>();
        for (var i = 0; i <= 200; i++)
        {
            Set(rest.Select(p => Vector3.Transform(p, Matrix4x4.CreateScale(0.2f + i * 0.01f, 1, 1))).ToArray());
            Measure(source, solver);
            var z = solver.GetBonePoses()[0].AxisZ;
            if (forward.Count > 0) Require(Vector3.Distance(z, forward[^1]) < 0.06f, "Continuous triangle motion snapped bone rotation.");
            forward.Add(z);
        }
        for (var i = 200; i >= 0; i--)
        {
            Set(rest.Select(p => Vector3.Transform(p, Matrix4x4.CreateScale(0.2f + i * 0.01f, 1, 1))).ToArray());
            Require(Vector3.Distance(solver.GetBonePoses()[0].AxisZ, forward[i]) < 0.00001f, "Native output depends on playback direction.");
        }
        File.WriteAllText(Path.Combine(directory, "bone-deform.txt"),
            "PASS: authored centroid transforms, fixed-Z reconstruction, nonuniform stretch/shear/rotation, continuous forward/reverse motion, repeated reads, collapse recovery, and reset.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    // Independent transcription of Switch 0x7101e7dea0, not the preview's
    // calibration or orthogonalization helpers.
    private static Matrix4x4 Evaluate(Matrix4x4 local, Vector3 a, Vector3 b, Vector3 c)
    {
        var center = (a + b + c) / 3;
        var u = a - center;
        var v = b - center;
        var n = Vector3.Cross(u, v);
        Vector3 Row(float x, float y, float z, float w) => x * u + y * v + z * n + w * center;
        var rawZ = Row(local.M31, local.M32, local.M33, local.M34);
        var rawX = Row(local.M11, local.M12, local.M13, local.M14);
        var y = Vector3.Normalize(Vector3.Cross(rawZ, rawX));
        var x = Vector3.Normalize(Vector3.Cross(y, rawZ));
        var z = Vector3.Normalize(rawZ);
        var p = Row(local.M41, local.M42, local.M43, local.M44);
        return new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, p.X, p.Y, p.Z, 1);
    }
}
