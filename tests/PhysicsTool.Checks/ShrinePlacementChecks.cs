using System.Numerics;
using PhysicsTool.Core;
using BymlLibrary;
using Revrs;
using System.Buffers.Binary;

internal static class ShrinePlacementChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Compounds(string folder)
    {
        int tested = 0, unsupported = 0;
        foreach (string path in Directory.EnumerateFiles(folder, "*.bphsc.zs"))
        {
            var doc = BphscDocument.Load(path); var original = doc.Snapshot();
            if (doc.Water.Count > 0 || doc.ExternalMeshCount > 0) { unsupported++; continue; }
            var moves = doc.ActorLinks.Where(l => l.FirstInstance != uint.MaxValue).ToDictionary(l => l.Hash, _ => new Vector3(1.25f, -2, 3));
            var changed = doc.MoveActors(moves); _ = BphscDocument.Parse(changed.Snapshot());
            Require(doc.Snapshot().SequenceEqual(original), "Collision preview mutated source");
            Require(moves.Count == 0 || !changed.Snapshot().SequenceEqual(original), "Baked collision was not moved");
            Require(changed.ActorLinks.SequenceEqual(doc.ActorLinks), "Collision actor links changed");
            Require(changed.Bodies.SequenceEqual(doc.Bodies), "Collision body origins changed");
            var delta = Matrix4x4.CreateScale(1.25f) * Matrix4x4.CreateRotationX(.2f) * Matrix4x4.CreateRotationY(-.4f) * Matrix4x4.CreateTranslation(2, -3, 4);
            var transforms = moves.ToDictionary(p => p.Key, _ => delta);
            var transformed = doc.TransformActors(transforms);
            CheckCollisionTransforms(doc, transformed, transforms);
            _ = BphscDocument.Parse(transformed.Snapshot());
            tested++;
        }
        Console.WriteLine($"PASS: translated, rotated, scaled and refitted {tested} shrine compounds; {unsupported} water/external layouts guarded.");
    }

    internal static void Editing(ShrineSceneAssets assets, string root, string output)
    {
        GroupHierarchy(assets, root);
        var editor = new ShrinePlacementEditor(assets, root);
        var picker = new ShrineModelPicking(assets.Model);
        var actor = assets.Scene.Actors.First(a => editor.EditRestriction(a.Index) == null && a.ParentIndex < 0 && a.MergedPath.Length == 0 &&
            picker.Meshes(a.Index).Any() && assets.Compound!.ActorLinks.Any(l => l.Hash == (a.PlacementId != 0 ? a.PlacementId : a.Hash) && l.FirstInstance != uint.MaxValue));
        var oldCollision = assets.Compound!.Snapshot(); var offset = new Vector3(2, 3, -4);
        var oldBounds = picker.Bounds(actor.Index)!.Value;
        editor.Move(actor.Index, actor.LocalPosition + offset);
        Require(Vector3.Distance(editor.Scene.Actors[actor.Index].Position, actor.Position + offset) < 1e-4f, "Actor world position wrong");
        var bounds = new ShrineModelPicking(editor.Model).Bounds(actor.Index)!.Value;
        Require(Vector3.Distance(bounds.Minimum, oldBounds.Minimum + offset) < 1e-4f, "Model did not follow actor edit");
        Require(!editor.Compound!.Snapshot().SequenceEqual(oldCollision), "Actor collision stayed behind");
        editor.Undo();
        Require(editor.Scene.Actors[actor.Index].Position == actor.Position && editor.Compound!.Snapshot().SequenceEqual(oldCollision), "Undo did not restore exact collision");
        editor.Redo();
        var target = ShrineTransform.Local(editor.Scene.Actors[actor.Index]) with
        { Rotation = actor.Rotation + new Vector3(.2f, -.35f, .1f), Scale = actor.Scale * 1.4f };
        var movedCollision = editor.Compound!.Snapshot();
        editor.SetTransform(actor.Index, target);
        Require(Matrix4x4.Invert(actor.WorldTransform, out var inverse), "Original actor is singular");
        var worldDelta = inverse * editor.Scene.Actors[actor.Index].WorldTransform;
        var oldMesh = assets.Model.Meshes.First(m => m.Name.StartsWith(actor.Index + ":"));
        var newMesh = editor.Model.Meshes.Single(m => m.Name == oldMesh.Name);
        for (int i = 0; i < oldMesh.Vertices.Length; i += 8)
        {
            var expected = Vector3.Transform(new(oldMesh.Vertices[i], oldMesh.Vertices[i + 1], oldMesh.Vertices[i + 2]), worldDelta);
            Require(Vector3.Distance(expected, new(newMesh.Vertices[i], newMesh.Vertices[i + 1], newMesh.Vertices[i + 2])) < .001f, "Model transform mismatch");
        }
        var transformedCollision = editor.Compound!.Snapshot();
        CheckCollisionTransforms(assets.Compound, editor.Compound, new Dictionary<ulong, Matrix4x4> { [actor.PlacementId != 0 ? actor.PlacementId : actor.Hash] = worldDelta });
        Require(!transformedCollision.SequenceEqual(movedCollision), "Rotation/scale left collision unchanged");
        editor.Undo(); Require(editor.Compound!.Snapshot().SequenceEqual(movedCollision), "Transform undo lost previous collision");
        editor.Redo(); Require(editor.Compound!.Snapshot().SequenceEqual(transformedCollision), "Transform redo drifted");
        string saved = editor.Save(output);
        var savedPath = Path.Combine(saved, "romfs", Path.GetRelativePath(root, actor.SourceScene));
        var reopened = ShrineSceneDocument.Load(savedPath);
        Require(reopened.Actors[actor.SourceIndex].Position == actor.LocalPosition + offset, "Saved BANC did not retain move");
        Require(reopened.Actors[actor.SourceIndex].Rotation == target.Rotation && reopened.Actors[actor.SourceIndex].Scale == target.Scale, "Saved BANC lost rotation/scale");
        var originalByml = Byml.FromBinary(BphscDocument.ReadResource(actor.SourceScene, [root]));
        var changedByml = Byml.FromBinary(BphscDocument.ReadResource(savedPath));
        var originalMap = originalByml.GetMap()["Actors"].GetArray()[actor.SourceIndex].GetMap();
        var changedMap = changedByml.GetMap()["Actors"].GetArray()[actor.SourceIndex].GetMap();
        foreach (string field in new[] { "Translate", "Rotate", "Scale" })
            if (originalMap.TryGetValue(field, out var value)) changedMap[field] = value; else changedMap.Remove(field);
        Require(originalByml.ToBinary(Endianness.Little, 7).SequenceEqual(changedByml.ToBinary(Endianness.Little, 7)), "Unrelated BANC data was changed");
        Require(File.Exists(Path.Combine(saved, "romfs", "Banc", "SmallDungeon", "Dungeon004_Dynamic.bcett.byml.zs")), "Dynamic companion not saved");
        var compound = BphscDocument.Load(Path.Combine(saved, "romfs", Path.GetRelativePath(root, assets.CompoundPath!)));
        Require(compound.Snapshot().SequenceEqual(editor.Compound!.Snapshot()), "Saved collision differs from preview");
        Require(assets.Compound.Snapshot().SequenceEqual(oldCollision), "Original collision changed");
        Require(!editor.Dirty, "Save did not clear dirty state");
        try { editor.Save(root); throw new Exception("Dump overwrite allowed"); } catch (InvalidOperationException) { }
        Console.WriteLine($"PASS: {actor.Name} move/rotate/scale, mesh/collision alignment, exact undo, redo, paired BANC export and unknown-data preservation. Output: {saved}");
    }

    private static void CheckCollisionTransforms(BphscDocument source, BphscDocument result, IReadOnlyDictionary<ulong, Matrix4x4> changes)
    {
        byte[] old = source.Snapshot(), saved = result.Snapshot();
        int I32(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
        int Relative(byte[] b, int at) => checked(at + (int)BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(at)));
        float F(byte[] b, int at) => BitConverter.ToSingle(b, at);
        Vector3 V(byte[] b, int at) => new(F(b, at), F(b, at + 4), F(b, at + 8));
        Matrix4x4 Pose(byte[] b, int at) => Matrix4x4.CreateScale(V(b, at + 32)) *
            Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(new(F(b, at), F(b, at + 4), F(b, at + 8), F(b, at + 12)))) *
            Matrix4x4.CreateTranslation(V(b, at + 16));
        int image = I32(old, 12);
        var entries = Enumerable.Range(0, I32(old, image + 0x4008)).Select(i => image + i * 4)
            .Concat(Enumerable.Range(0, I32(old, image + 0x400c)).Select(i => image + 0x2000 + i * 4)).ToArray();
        var expected = new Dictionary<(int Slot, int Instance), Matrix4x4>();
        foreach (var link in source.ActorLinks)
        {
            if (link.FirstInstance == uint.MaxValue || !changes.TryGetValue(link.Hash, out var worldDelta)) continue;
            var body = source.Bodies.Single(b => b.Index == link.BodyIndex);
            var world = Matrix4x4.CreateRotationX(body.Rotation.X) * Matrix4x4.CreateRotationY(body.Rotation.Y) * Matrix4x4.CreateRotationZ(body.Rotation.Z) * Matrix4x4.CreateTranslation(body.Position);
            Require(Matrix4x4.Invert(world, out var inverse), "Body is singular");
            for (uint i = link.FirstInstance; i <= link.EndInstance; i++)
            {
                int reference = checked(I32(old, 28) + (int)i * 20);
                int slot = Array.FindIndex(entries, at => old[at + 2] == link.BodyIndex && old[at + 3] == old[reference + 5]);
                int instance = BinaryPrimitives.ReadUInt16LittleEndian(old.AsSpan(reference + 10));
                expected.Add((slot, instance), world * worldDelta * inverse);
            }
        }
        var allowed = new bool[old.Length]; int checkedInstances = 0;
        foreach (var section in source.Sections)
        {
            if (!expected.Keys.Any(k => k.Slot == section.Index)) continue;
            int data = -1;
            for (int at = section.Offset + 8; at < section.Offset + section.Size;)
            {
                int length = (int)(BinaryPrimitives.ReadUInt32BigEndian(old.AsSpan(at)) & 0x3fffffff);
                if (old.AsSpan(at + 4, 4).SequenceEqual("DATA"u8)) { data = at + 8; break; }
                Require(length >= 8, "Invalid chunk"); at += length;
            }
            Require(data >= 0, "Missing DATA");
            int shapes = Relative(old, data + 72), count = I32(old, data + 80);
            for (int i = 0; i < count; i++)
            {
                if (!expected.TryGetValue((section.Index, i), out var delta)) continue;
                checkedInstances++;
                int at = shapes + i * 64;
                var wanted = Pose(old, at) * delta; var actual = Pose(saved, at);
                foreach (var point in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                    Require(Vector3.Distance(Vector3.Transform(point, wanted), Vector3.Transform(point, actual)) < .002f, "Collision instance does not match actor transform");
                byte flags = saved[at + 28];
                Require((flags & ~14) == (old[at + 28] & ~14), "Unrelated shape flags changed");
                Require(((flags & 2) != 0) == (V(saved, at + 16) != Vector3.Zero) &&
                    ((flags & 4) != 0) == (V(saved, at) != Vector3.Zero) &&
                    ((flags & 8) != 0) == (V(saved, at + 32) != Vector3.One), "Cached transform flags incorrect");
                Array.Fill(allowed, true, at, 29); Array.Fill(allowed, true, at + 32, 12);
            }
            int nodes = Relative(old, data + 232), nodeCount = I32(old, data + 240);
            for (int n = 1; n < nodeCount; n++)
            {
                int at = nodes + n * 128;
                Array.Fill(allowed, true, at, 96);
                if (old[at + 112] == 0) continue;
                for (int lane = 0; lane < 4; lane++)
                {
                    int shape = I32(old, at + 96 + lane * 4);
                    if (!expected.TryGetValue((section.Index, shape), out var delta)) continue;
                    Vector3 Lo(byte[] b) => new(F(b, at + lane * 4), F(b, at + 32 + lane * 4), F(b, at + 64 + lane * 4));
                    Vector3 Hi(byte[] b) => new(F(b, at + 16 + lane * 4), F(b, at + 48 + lane * 4), F(b, at + 80 + lane * 4));
                    var lo = Lo(old); var hi = Hi(old); var newLo = Lo(saved); var newHi = Hi(saved);
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var p = Vector3.Transform(new((corner & 1) == 0 ? lo.X : hi.X, (corner & 2) == 0 ? lo.Y : hi.Y, (corner & 4) == 0 ? lo.Z : hi.Z), delta);
                        Require(p.X >= newLo.X - .002f && p.Y >= newLo.Y - .002f && p.Z >= newLo.Z - .002f &&
                            p.X <= newHi.X + .002f && p.Y <= newHi.Y + .002f && p.Z <= newHi.Z + .002f, "Collision tree clips transformed geometry");
                    }
                }
            }
            Array.Fill(allowed, true, data + 112, 12); Array.Fill(allowed, true, data + 128, 12); Array.Fill(allowed, true, data + 144, 4);
        }
        Require(checkedInstances == expected.Count, "Collision transform test missed an instance");
        Require(old.Length == saved.Length && old.Where((b, i) => !allowed[i] && b != saved[i]).Count() == 0, "Transform altered geometry, topology or unrelated data");
    }

    private static void GroupHierarchy(ShrineSceneAssets assets, string root)
    {
        var parent = new ShrineActor(0, "TestParent", "", ulong.MaxValue, new(2, 3, 4), new(0, MathF.PI / 2, 0), new(2), 0, 0, 0, "", [], [])
        { SourceScene = assets.Scene.Actors[0].SourceScene, SourceIndex = 0, LocalPosition = new(2, 3, 4) };
        parent = parent with { WorldTransform = ShrineSceneAssets.Placement(parent) };
        var child = parent with { Index = 1, Name = "TestChild", Hash = ulong.MaxValue - 1, SourceIndex = 1, ParentIndex = 0,
            Position = new(1, 2, 3), LocalPosition = new(1, 2, 3), Rotation = Vector3.Zero, Scale = Vector3.One };
        child = child with { WorldTransform = ShrineSceneAssets.Placement(child) * parent.WorldTransform };
        child = child with { Position = child.WorldTransform.Translation };
        var model = new HKCLTool.VisualModelData { SourcePath = assets.Model.SourcePath };
        var editor = new ShrinePlacementEditor(assets with { Scene = new([parent, child]), Model = model }, root);
        var delta = new Vector3(3, 4, 5);
        editor.MoveTogether([1, 0, 1], delta);
        Require(Vector3.Distance(editor.Scene.Actors[0].Position, parent.Position + delta) < 1e-5f &&
            Vector3.Distance(editor.Scene.Actors[1].Position, child.Position + delta) < 1e-5f &&
            editor.Scene.Actors[1].LocalPosition == child.LocalPosition, "Parent/child group moved a descendant twice");
        editor.Undo();
        Require(editor.Scene.Actors[0].Position == parent.Position && editor.Scene.Actors[1].Position == child.Position && !editor.CanUndo, "Group undo was not atomic");
        editor.MoveTogether([1], delta);
        Require(Vector3.Distance(editor.Scene.Actors[1].Position, child.Position + delta) < 1e-5f, "Child move did not account for rotated/scaled parent");
        var before = editor.Scene;
        try { editor.MoveTogether([0, 1], new(float.NaN)); throw new Exception("Nonfinite group movement accepted"); }
        catch (ArgumentException) { }
        Require(ReferenceEquals(before, editor.Scene), "Rejected group move partially changed the scene");
        editor.Undo();
        var pivot = parent.Position;
        var transform = Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateScale(1.3f) *
            Matrix4x4.CreateRotationX(.3f) * Matrix4x4.CreateRotationY(-.2f) * Matrix4x4.CreateTranslation(pivot);
        editor.TransformTogether([1, 0, 1], transform);
        Require(Vector3.Distance(editor.Scene.Actors[1].Position, Vector3.Transform(child.Position, transform)) < 1e-4f &&
            editor.Scene.Actors[1].LocalPosition == child.LocalPosition && editor.Scene.Actors[1].Scale == child.Scale,
            "Group rotation/scale transformed a selected descendant twice");
        editor.Undo();
        var resized = ShrineTransform.Local(parent) with { Scale = new Vector3(1.2f, .7f, 2) };
        editor.SetTransform(0, resized);
        var resizedWorld = ShrineSceneAssets.Placement(parent with { Scale = resized.Scale });
        Require(Vector3.Distance(editor.Scene.Actors[1].Position, Vector3.Transform(child.LocalPosition, resizedWorld)) < 1e-4f,
            "Nonuniform local scaling did not update child position");
        editor.Undo();
        editor.TransformTogether([1], transform);
        Require(Vector3.Distance(editor.Scene.Actors[1].Position, Vector3.Transform(child.Position, transform)) < 1e-4f,
            "Child rotation/scale ignored parent transform");
        before = editor.Scene;
        foreach (var invalid in new[] { Matrix4x4.CreateScale(0), Matrix4x4.CreateScale(-1, 1, 1),
            Matrix4x4.CreateRotationY(.4f) * Matrix4x4.CreateScale(2, 1, 1) })
        {
            bool rejected = false;
            try { editor.TransformTogether([0], invalid); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException) { rejected = true; }
            Require(rejected && ReferenceEquals(before, editor.Scene), "Unsupported transform was not rejected atomically");
        }
    }
}
