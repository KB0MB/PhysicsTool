using System.Numerics;
using System.Reflection;

namespace HKCLTool;

internal static class ModelEditingChecks
{
    public static void Run(string output, string fixtures, string[] models)
    {
        Directory.CreateDirectory(output);
        ApplicationConfiguration.Initialize();
        var log = new List<string>();
        CheckPresentation();
        log.Add("PASS: native BFRES handedness, physics alignment, inverse picking, manual mirror, pose/format transitions, and legacy FBX/actor isolation.");
        var fallback = VisualModelImporter.GetDefaultTexture();
        Require(fallback.Width == 256 && fallback.Height == 256 && fallback.Pixels.Length == 256 * 256 * 4,
            "Toolbox fallback texture was not embedded/decoded correctly.");
        Require(Enumerable.Range(0, fallback.Width * fallback.Height).Any(i => fallback.Pixels[i * 4] != fallback.Pixels[i * 4 + 1]),
            "Toolbox colored UV marks are missing.");
        Require(VisualModelImporter.ResolveAlbedoTexture(new VisualModelMesh()) == null, "Collision/untextured mesh received a fallback.");
        var missing = new VisualModelMesh { UseMissingTextureFallback = true };
        Require(ReferenceEquals(VisualModelImporter.ResolveAlbedoTexture(missing), fallback) && missing.Texture == null,
            "Missing BFRES albedo did not use a render-only fallback.");
        var real = new VisualModelTexture { Name = "authored" };
        Require(ReferenceEquals(VisualModelImporter.ResolveAlbedoTexture(new VisualModelMesh { Texture = real, UseMissingTextureFallback = true }), real),
            "Fallback replaced an authored texture.");
        log.Add("PASS: embedded Toolbox UV fallback, colored marks, empty-slot preservation, authored textures, and untextured collision isolation.");
        var random = new Random(204);
        for (var i = 0; i < 3000; i++)
        {
            var a = new Vector3((float)(random.NextDouble() * 12 - 6), (float)(random.NextDouble() * 12 - 6), (float)(random.NextDouble() * 12 - 6));
            if (i < 12) a.Y = (i % 2 == 0 ? 1 : -1) * (MathF.PI / 2 + (i / 2) * 1e-7f);
            var q = BoneRotationAngles.FromRadians(a);
            var expected = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationX(a.X) * Matrix4x4.CreateRotationY(a.Y) * Matrix4x4.CreateRotationZ(a.Z));
            Require(MathF.Abs(Quaternion.Dot(q, expected)) > 0.99999f, "BFRES XYZ composition mismatch.");
            var round = BoneRotationAngles.FromRadians(BoneRotationAngles.ToRadians(q));
            Require(MathF.Abs(Quaternion.Dot(q, round)) > 0.99999f, "Quaternion/Euler round trip mismatch.");
            round = BoneRotationAngles.FromRadians(BoneRotationAngles.ToRadians(-q));
            Require(MathF.Abs(Quaternion.Dot(q, round)) > 0.99999f, "Quaternion sign changed Euler orientation.");
        }
        log.Add("PASS: 3000 Euler XYZ conversions, gimbal cases, quaternion signs, and BFRES matrix-order agreement.");
        var uiBone = new BoneEditRow { RotationX = 0.12345678f, RotationY = -0.23456789f, RotationZ = 0.34567891f, RotationW = 0.8f };
        var initial = new Quaternion(uiBone.RotationX, uiBone.RotationY, uiBone.RotationZ, uiBone.RotationW);
        var apply = typeof(MainForm).GetMethod("SetBoneRadians", BindingFlags.Static | BindingFlags.NonPublic)!;
        apply.Invoke(null, new object[] { uiBone, BoneRotationAngles.ToRadians(initial) });
        Require(initial == new Quaternion(uiBone.RotationX, uiBone.RotationY, uiBone.RotationZ, uiBone.RotationW), "Untouched UI rotation was quantized.");
        var radians = new Vector3(0.25f, -0.8f, 1.2f);
        apply.Invoke(null, new object[] { uiBone, radians });
        Require(MathF.Abs(Quaternion.Dot(BoneRotationAngles.FromRadians(radians), new(uiBone.RotationX, uiBone.RotationY, uiBone.RotationZ, uiBone.RotationW))) > 0.99999f,
            "UI radians did not become the expected quaternion.");
        var before = new[] { new BoneEditRow { Index = 0, ParentIndex = -1, RotationW = 1 },
            new BoneEditRow { Index = 1, ParentIndex = 0, X = 2, RotationW = 1 },
            new BoneEditRow { Index = 2, ParentIndex = -1, RotationW = 1 } };
        var after = new[] { new BoneEditRow { Index = 0, ParentIndex = -1, X = 4, RotationW = MathF.Sqrt(0.5f), RotationZ = MathF.Sqrt(0.5f) }, before[1], before[2] };
        var bindings = new[] { new ParticlePreviewAttachment { ParticleIndex = 0 }, new ParticlePreviewAttachment { ParticleIndex = 1 } };
        bindings[0].BoneWeights.Add(new() { BoneIndex = 1, Weight = 1 });
        bindings[1].BoneWeights.Add(new() { BoneIndex = 1, Weight = 0.25f });
        bindings[1].BoneWeights.Add(new() { BoneIndex = 2, Weight = 0.75f });
        var particles = new[] { new ParticleEditRow { Index = 0, X = 3, Fixed = true, W = 9 },
            new ParticleEditRow { Index = 1, X = 3, Mass = 2, InverseMass = 0.5f }, new ParticleEditRow { Index = 2, X = 9 } };
        var moved = BoneEditGeometry.FollowParticles(before, after, particles, bindings);
        Near(Position(moved[0]), new(4, 3, 0), "Child-weighted particle inherited parent rotation/translation incorrectly.");
        Near(Position(moved[1]), new(3.25f, 0.75f, 0), "Blended weights ignored the unmoved influence.");
        Require(moved.Count == 2 && moved[0].W == 9 && moved[1].Mass == 2 && particles[0].X == 3, "Particle metadata/source changed.");
        log.Add("PASS: fixed/free weighted particles, descendant transforms, mixed weights, unweighted isolation, immutable inputs.");
        var count = 0;
        foreach (var file in new[] { "Fixtures/Npc_Zelda_Miko.hkcl", "Matches/Armor_014_Lower.bphcl", "ZeldaAnicentHyrule/Npc_Zelda_AncientHyrule.bphcl" })
        {
            var path = Path.Combine(fixtures, file);
            var service = new HkclService(); service.Load(path);
            for (var cloth = 0; cloth < service.GetClothSummaries().Count; cloth++)
            {
                var state = service.CaptureState();
                var old = service.GetParticleRows(cloth);
                var bones = service.GetBoneRows(cloth);
                var weights = service.GetParticleBoneAttachments(cloth);
                var edited = service.GetBoneRows(cloth);
                foreach (var root in edited.Where(b => b.ParentIndex < 0)) root.X += 0.125f;
                var expected = BoneEditGeometry.FollowParticles(bones, edited, old, weights);
                Require(expected.Count > 0, "Fixture has no weighted particles.");
                service.UpdateBoneRows(cloth, edited);
                foreach (var row in expected)
                {
                    Near(Position(row), Position(old.Single(p => p.Index == row.Index)) + new Vector3(0.125f, 0, 0), "Root shift wasn't inherited.");
                    Near(Position(service.GetParticleRows(cloth).Single(p => p.Index == row.Index)), Position(row), "Committed particle differs.");
                }
                var platform = service.IsBphcl ? HkclPlatform.Switch : HkclService.DetectHkclPlatform(path);
                var target = Path.Combine(output, $"weighted-{count++}{Path.GetExtension(path)}");
                service.SaveHkcl(target, platform);
                var loaded = new HkclService(); loaded.Load(target);
                foreach (var row in expected) Near(Position(loaded.GetParticleRows(cloth).Single(p => p.Index == row.Index)), Position(row), "Saved particle differs.");
                CheckMetadata(old, loaded.GetParticleRows(cloth));
                service.RestoreState(state);
                foreach (var row in old) Near(Position(service.GetParticleRows(cloth).Single(p => p.Index == row.Index)), Position(row), "Undo particle differs.");
                var child = bones.FirstOrDefault(b => b.ParentIndex >= 0 && weights.Any(a => a.BoneWeights.Any(w => w.BoneIndex == b.Index)));
                if (child != null)
                {
                    child.X += 0.0125f;
                    service.UpdateBoneRows(cloth, new[] { child });
                    CheckMetadata(old, service.GetParticleRows(cloth));
                    service.RestoreState(state);
                }
            }
        }
        log.Add($"PASS: {count} native HKCL/BPHCL bone-edit particle save/reloads and exact snapshot restoration.");
        foreach (var path in models)
        {
            if (VisualModelImporter.IsBfres(path))
            {
                var bytes = File.ReadAllBytes(path);
                var plain = Path.Combine(output, $"plain-{models.ToList().IndexOf(path)}.bfres");
                File.WriteAllBytes(plain, bytes.AsSpan().StartsWith("Yaz0"u8) ? BotwYaz0.Decode(bytes) : bytes);
                Require(VisualModelImporter.LoadModel(plain).TriangleCount > 0, "Uncompressed BFRES import failed.");
            }
            using var session = VisualModelImporter.OpenPose(path);
            var rest = session.Sample();
            using (var preview = new ParticlePreviewControl())
            {
                preview.SetVisualModel(rest, false);
                CheckPresentationCoordinates(preview, !VisualModelImporter.IsBfres(path));
            }
            Require(rest.Meshes.Count > 0 && rest.Bones.Count > 0 && rest.TriangleCount > 0, "Empty imported model.");
            if (VisualModelImporter.IsBfres(path))
                Require(rest.Meshes.All(m => m.UseMissingTextureFallback), "BFRES fallback flag was lost during posing.");
            Require(rest.Meshes.All(m => m.Vertices.All(float.IsFinite)), "Non-finite model vertices.");
            Require(Path.GetFullPath(rest.SourcePath) == Path.GetFullPath(path), "Model source identity lost.");
            var root = rest.Bones.First(b => b.ParentIndex < 0);
            var posed = session.Sample(rest, new[] { new VisualBoneOverride(root.Name, root.Position + new Vector3(0.25f, 0, 0), root.Rotation, root.Scale) });
            Require(Vector3.Distance(posed.Bones.First(b => b.Name == root.Name).Position, root.Position + new Vector3(0.25f, 0, 0)) < 0.001f, "BFRES override did not reach skeleton.");
            Require(!posed.Meshes[0].Vertices.SequenceEqual(rest.Meshes[0].Vertices), "BFRES pose did not deform mesh.");
            log.Add($"PASS: {Path.GetFileName(path)} import/pose: {rest.Meshes.Count} meshes, {rest.Bones.Count} bones, {rest.EmbeddedTextureCount}+{rest.ExternalTextureCount} textures.");
        }
        File.WriteAllLines(Path.Combine(output, "model-editing.txt"), log);
    }
    private static void CheckPresentation()
    {
        using var preview = new ParticlePreviewControl();
        CheckPresentationCoordinates(preview, false);
        foreach (var source in new[] { "native.bfres", "native.SBFRES", "legacy.fbx", "other.bfres" })
        {
            preview.SetVisualModel(new VisualModelData { SourcePath = source }, false);
            var reflected = !VisualModelImporter.IsBfres(source);
            CheckPresentationCoordinates(preview, reflected);
            preview.SetVisualModelPose(new VisualModelData { SourcePath = source });
            CheckPresentationCoordinates(preview, reflected);
            preview.SetVisualModelDisplaySettings(new VisualModelDisplaySettings { MirrorX = true });
            CheckPresentationCoordinates(preview, reflected, manualMirror: true);
        }
        preview.ClearVisualModel();
        CheckPresentationCoordinates(preview, false);
        using var actor = new ParticlePreviewControl { ReflectSceneX = false };
        actor.SetVisualModel(new VisualModelData { SourcePath = "actor.sbactorpack" }, false);
        CheckPresentationCoordinates(actor, false);
        actor.ClearVisualModel();
        CheckPresentationCoordinates(actor, false);
    }

    private static void CheckPresentationCoordinates(ParticlePreviewControl preview, bool reflected, bool manualMirror = false)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(ParticlePreviewControl);
        var matrix = (OpenTK.Mathematics.Matrix4)type.GetMethod("GetVisualModelMatrix", flags)!.Invoke(preview, null)!;
        Require(preview.ReflectSceneX == reflected, "Incorrect scene reflection for imported model.");
        Require((matrix.Determinant < 0) == (reflected != manualMirror), "Incorrect model winding/handedness.");
        foreach (var p in new[] { new Vector3(1, 2, 3), new Vector3(-4, 5, -6) })
        {
            var overlay = (OpenTK.Mathematics.Vector3)type.GetMethod("ToGl", flags)!.Invoke(preview, new object[] { p })!;
            Near(new(overlay.X, overlay.Y, overlay.Z), new(reflected ? -p.X : p.X, p.Y, p.Z), "Physics overlay reflected incorrectly.");
            var pick = (Vector3)type.GetMethod("ToSysVector", flags)!.Invoke(preview, new object[] { overlay })!;
            Near(pick, p, "Picking/drag inverse does not match scene presentation.");
            var model = OpenTK.Mathematics.Vector3.TransformPosition(new(p.X, p.Y, p.Z), matrix);
            Near(new(model.X, model.Y, model.Z), new(reflected != manualMirror ? -p.X : p.X, p.Y, p.Z), "Model presentation disagrees with native coordinates.");
        }
    }

    private static Vector3 Position(ParticleEditRow p) => new(p.X, p.Y, p.Z);
    private static void CheckMetadata(IReadOnlyList<ParticleEditRow> before, IReadOnlyList<ParticleEditRow> after)
    {
        foreach (var p in before)
        {
            var n = after.Single(row => row.Index == p.Index);
            Require(p.Fixed == n.Fixed && p.Mass == n.Mass && p.InverseMass == n.InverseMass && p.Radius == n.Radius
                && p.Friction == n.Friction && p.CollisionMask == n.CollisionMask && p.W == n.W, "Bone edit changed particle metadata.");
        }
    }
    private static void Near(Vector3 a, Vector3 b, string message) => Require(Vector3.Distance(a, b) < 0.0001f, message);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
