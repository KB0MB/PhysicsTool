using System.Numerics;
using System.Collections;
using System.Reflection;
using HKCLTool;
using Newtonsoft.Json;
using PhysicsTool.Core;

internal static class WholeClothResizeChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static Vector3 Position(ParticleEditRow p) => new(p.X, p.Y, p.Z);
    private static Vector3 Start(ColliderEditRow c) => new(c.StartX, c.StartY, c.StartZ);
    private static Vector3 End(ColliderEditRow c) => new(c.EndX, c.EndY, c.EndZ);
    private static void Near(Vector3 a, Vector3 b, string message) => Check(Vector3.Distance(a, b) < 3e-4f, $"{message}: {a} != {b}");
    private static string Cloth(HkclService doc, int cloth) => JsonConvert.SerializeObject(new {
        Bones = doc.GetBoneRows(cloth), Particles = doc.GetParticleRows(cloth), Colliders = doc.GetColliderRows(cloth),
        Bindings = doc.GetStandaloneColliderBindings(cloth), Skin = doc.GetSkinOperatorData(cloth), Runtime = doc.GetClothRuntimeData(cloth)
    });
    private static Dictionary<int, Matrix4x4> Worlds(HkclService doc, int cloth) =>
        doc.GetParticlePreview(cloth).Bones.ToDictionary(b => b.Index, b => new Matrix4x4(
            b.AxisX.X, b.AxisX.Y, b.AxisX.Z, 0, b.AxisY.X, b.AxisY.Y, b.AxisY.Z, 0,
            b.AxisZ.X, b.AxisZ.Y, b.AxisZ.Z, 0, b.Position.X, b.Position.Y, b.Position.Z, 1));
    private static Dictionary<int, Vector3> OutputPositions(HkclService doc, int cloth)
    {
        var preview = doc.GetParticlePreview(cloth);
        if (doc.IsBphcl) foreach (var binding in preview.BoneBindings) binding.UseNativeSimpleDeformer = true;
        return new HkclPreviewSimulator(preview).GetBonePoses().ToDictionary(p => p.Key, p => p.Value.Position);
    }

    internal static void Run(string path, string output)
    {
        Directory.CreateDirectory(output);
        var session = new EditorSession(); session.Load(path); var doc = session.Document;
        var count = doc.GetClothSummaries().Count;
        if (!doc.IsBphcl && count > 1) ShareHkclColliderForRegression(doc);
        var state = doc.CaptureState();
        var particles = Enumerable.Range(0, count).Select(i => doc.GetParticleRows(i).ToArray()).ToArray();
        var bones = Enumerable.Range(0, count).Select(i => doc.GetBoneRows(i).ToArray()).ToArray();
        var worlds = Enumerable.Range(0, count).Select(i => Worlds(doc, i)).ToArray();
        var colliders = Enumerable.Range(0, count).SelectMany(doc.GetColliderRows).DistinctBy(c => c.Index).ToDictionary(c => c.Index);
        var bindings = Enumerable.Range(0, count).Select(doc.GetStandaloneColliderBindings).ToArray();
        var outputPositions = Enumerable.Range(0, count).Select(i => OutputPositions(doc, i)).ToArray();
        var links = Enumerable.Range(0, count).Select(i => doc.GetParticlePreview(i).Links.ToArray()).ToArray();
        const float scale = .435f;
        session.Edit((d, _) => d.ResizeAllCloths(new Vector3(scale)));
        void Verify(HkclService result)
        {
            for (var cloth = 0; cloth < count; cloth++)
            {
                var afterBones = result.GetBoneRows(cloth); var afterWorlds = Worlds(result, cloth);
                foreach (var p in result.GetParticleRows(cloth))
                {
                    Near(Position(p), Position(particles[cloth][p.Index]) * scale, "Particle scaled around wrong pivot");
                    Check(p.Mass == particles[cloth][p.Index].Mass && p.InverseMass == particles[cloth][p.Index].InverseMass &&
                        p.CollisionMask == particles[cloth][p.Index].CollisionMask, "Scale changed mass or collision masks");
                    Check(MathF.Abs(p.Radius - particles[cloth][p.Index].Radius * scale) < 1e-6f, "Particle radius not resized");
                }
                foreach (var b in afterBones)
                {
                    var old = bones[cloth].Single(x => x.Index == b.Index);
                    Check(b.ScaleX == old.ScaleX && b.ScaleY == old.ScaleY && b.ScaleZ == old.ScaleZ, "Scale written to bone");
                    Near(afterWorlds[b.Index].Translation, worlds[cloth][b.Index].Translation * scale, "Bone did not resize about origin");
                }
                foreach (var (bone, position) in OutputPositions(result, cloth))
                    Near(position, outputPositions[cloth][bone] * scale, "Simulation output bone snapped to old position");
                foreach (var (old, current) in links[cloth].Zip(result.GetParticlePreview(cloth).Links))
                {
                    void Length(float? a, float? b) { if (a.HasValue) Check(b.HasValue && MathF.Abs(b.Value - a.Value * scale) < 3e-5f, "Constraint rest length not resized"); }
                    Length(old.RestLength, current.RestLength); Length(old.StretchMaxLength, current.StretchMaxLength);
                    Length(old.CompressionLength, current.CompressionLength); Length(old.BendMinLength, current.BendMinLength);
                }
                foreach (var c in result.GetColliderRows(cloth))
                {
                    var old = colliders[c.Index];
                    Near(Start(c), Start(old) * scale, "Shared collider start scaled incorrectly");
                    Near(End(c), End(old) * scale, "Shared collider end scaled incorrectly");
                    Check(MathF.Abs(c.Radius - old.Radius * scale) < 1e-6f, "Shared collider radius resized repeatedly");
                }
                var afterBindings = result.GetStandaloneColliderBindings(cloth);
                foreach (var (before, after) in bindings[cloth].Zip(afterBindings))
                    Near((after.Offset * afterWorlds[after.BoneIndex]).Translation,
                        (before.Offset * worlds[cloth][before.BoneIndex]).Translation * scale, "Runtime collider attachment offset incorrect");
            }
        }
        Verify(doc);
        var resized = doc.CaptureState(); session.Undo(); Check(doc.CaptureState() == state, "All undo failed");
        session.Redo(); Check(doc.CaptureState() == resized, "All redo failed");
        var saved = Path.Combine(output, "resized" + Path.GetExtension(path));
        doc.SaveHkcl(saved, doc.IsBphcl ? HkclPlatform.Switch : HkclPlatform.WiiU);
        var reopened = new HkclService(); reopened.Load(saved); Verify(reopened);
        doc.RestoreState(state);
        var colliderBaseline = Enumerable.Range(0, count).Select(i => JsonConvert.SerializeObject(doc.GetColliderRows(i))).ToArray();
        var xyz = new Vector3(.6f, .7f, .8f);
        doc.ResizeAllCloths(xyz, resizeColliders: false);
        for (var cloth = 0; cloth < count; cloth++)
        {
            foreach (var p in doc.GetParticleRows(cloth)) Near(Position(p), Position(particles[cloth][p.Index]) * xyz, "XYZ factors not applied");
            Check(JsonConvert.SerializeObject(doc.GetColliderRows(cloth)) == colliderBaseline[cloth], "Unchecked colliders changed");
            var currentWorlds = Worlds(doc, cloth);
            foreach (var (before, after) in bindings[cloth].Zip(doc.GetStandaloneColliderBindings(cloth)))
                Near((after.Offset * currentWorlds[after.BoneIndex]).Translation,
                    (before.Offset * worlds[cloth][before.BoneIndex]).Translation, "Unchecked collider moved through its attachment map");
        }
        doc.RestoreState(state);
        try { doc.ResizeAllCloths(new Vector3(float.NaN)); throw new Exception("Invalid scale accepted"); }
        catch (ArgumentOutOfRangeException) { Check(doc.CaptureState() == state, "Invalid scale changed document"); }
        // A single-cloth edit must preserve other cloths and keep mask slot order.
        var shared = Enumerable.Range(0, count).FirstOrDefault(i => doc.GetColliderRows(i).Any(c =>
            Enumerable.Range(0, count).Where(j => j != i).Any(j => doc.GetColliderRows(j).Any(o => o.Index == c.Index))), -1);
        if (shared >= 0)
        {
            var others = Enumerable.Range(0, count).Where(i => i != shared).ToDictionary(i => i, i => Cloth(doc, i));
            var oldRows = doc.GetColliderRows(shared).ToArray();
            session.ClothIndex = shared;
            var selected = doc.GetBoneRows(shared).Select(b => b.Index).ToHashSet();
            using (var preview = new ClothBranchResizePreview(session, selected, Vector3.Zero))
            {
                preview.Sample(.5f, true); preview.Sample(.7f, true);
            }
            Check(doc.CaptureState() == state, "Cancel left behind unique collider copies");
            using (var preview = new ClothBranchResizePreview(session, selected, Vector3.Zero))
            {
                preview.Sample(scale, true); preview.Commit();
            }
            var isolated = doc.CaptureState();
            session.Undo(); Check(doc.CaptureState() == state, "Undo left behind unique collider copies");
            session.Redo(); Check(doc.CaptureState() == isolated, "Redo lost unique collider copies");
            Check(others.All(p => Cloth(doc, p.Key) == p.Value), "Single-cloth resize changed another cloth");
            var newRows = doc.GetColliderRows(shared);
            Check(newRows.Count == oldRows.Length, "Isolation added collision mask slots");
            foreach (var old in oldRows)
            {
                var current = newRows.FirstOrDefault(c => c.Index == old.Index) ?? newRows.Single(c => c.Name == old.Name);
                var factor = old.BoneIndex >= 0 ? scale : 1f;
                Near(Start(current), Start(old) * factor, "Isolated collider start wrong");
                Check(MathF.Abs(current.Radius - old.Radius * factor) < 1e-6f, "Isolated collider radius wrong");
            }
            var unique = Path.Combine(output, "isolated" + Path.GetExtension(path));
            doc.SaveHkcl(unique, doc.IsBphcl ? HkclPlatform.Switch : HkclPlatform.WiiU);
            reopened.Load(unique);
            Check(others.All(p => Cloth(reopened, p.Key) == p.Value), "Collider isolation lost on round trip");
            Console.WriteLine("PASS shared colliders copied in place; other cloths unchanged before and after saving");
        }
        Console.WriteLine($"PASS {count} cloths: whole-file scale, bones, particle properties, shared colliders, attachment offsets, round trip and undo/redo");
    }

    private static void ShareHkclColliderForRegression(HkclService doc)
    {
        // The converted HKCL fixture has isolated collision objects. Share one in memory
        // to exercise vanilla-style ownership without changing the input file on disk.
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var root = typeof(HkclService).GetField("_root", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(doc)!;
        var cloths = ((IEnumerable)typeof(HkclService).GetMethod("GetClothDatas", flags)!.Invoke(null, new[] { root })!).Cast<object>().ToArray();
        object? Value(object owner, string name) => typeof(HkclService).GetMethod("GetValue", flags)!.Invoke(null, new object[] { owner, name });
        IList References(object cloth) => (IList)Value(((IEnumerable)Value(cloth, "simClothDatas")!).Cast<object>().First(), "perInstanceCollidables")!;
        var a = References(cloths[0]); var b = References(cloths[1]);
        if (a.Count > 0 && b.Count > 0) a[0] = b[0];
    }
}
