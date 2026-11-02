using System.Buffers.Binary;
using System.Numerics;
using BymlLibrary;
using HKCLTool;
using PhysicsTool.Core;
using Revrs;

internal static class ShrineGroundControlChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }

    internal static void Native(string root, string output)
    {
        int tested = 0, moved = 0, parametersOnly = 0; var guarded = new Dictionary<string, int>();
        var savedTypes = new HashSet<string>();
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "Banc", "SmallDungeon"), "*_Static.bcett.byml.zs"))
        {
            var actors = new List<ShrineActor>();
            foreach (string source in new[] { path, ShrineSceneCatalog.Companion(path) }.OfType<string>())
                foreach (var a in ShrineSceneDocument.Load(source, [root]).Actors)
                    actors.Add(a with { Index = actors.Count, SourceScene = source, SourceIndex = a.Index,
                        LocalPosition = a.Position, WorldTransform = ShrineSceneAssets.Placement(a) });
            var collisionPath = ShrineSceneCatalog.FindCompound(root, path);
            if (collisionPath == null) continue;
            var assets = new ShrineSceneAssets(new(actors), new() { SourcePath = path }, collisionPath, BphscDocument.Load(collisionPath, [root]), [], 0);
            foreach (var actor in actors.Where(a => a.Name.StartsWith("GroundControlActor", StringComparison.Ordinal)))
            {
                var editor = new ShrinePlacementEditor(assets, root);
                var group = editor.GroundControl(actor.Index)!;
                if (group.Restriction is { } reason)
                {
                    guarded[reason] = guarded.GetValueOrDefault(reason) + 1;
                    if (editor.GroundControlParameterRestriction(actor.Index) == null)
                    {
                        var parameters = editor.GroundControlValues(actor.Index).ToDictionary(p => p.Key, p => p.Value.Select(v => v + 1).ToArray());
                        editor.SetGroundControl(actor.Index, ShrineTransform.Local(actor), false, checked((ushort)actor.GroupId), actor.GroupGuid, parameters);
                        Require(editor.Compound!.Snapshot().SequenceEqual(assets.Compound!.Snapshot()), "Settings-only edit changed guarded collision");
                        foreach (var (key, value) in parameters) Require(value.SequenceEqual(editor.GroundControlValues(actor.Index)[key]), "Settings-only value lost");
                        if (parameters.Count > 0 && savedTypes.Add(actor.Name))
                        {
                            string folder = editor.Save(output);
                            var record = Byml.FromBinary(BphscDocument.ReadResource(Path.Combine(folder, "romfs", Path.GetRelativePath(root, actor.SourceScene))))
                                .GetMap()["Actors"].GetArray()[actor.SourceIndex];
                            CheckParameters(record, parameters);
                        }
                        parametersOnly++;
                    }
                    continue;
                }
                Check(editor, actor, group, assets, root, output, savedTypes.Add(actor.Name) || tested == 0);
                tested++; if (group.MoveRestriction == null) moved++;
            }
        }
        foreach (var (reason, count) in guarded) Console.WriteLine($"Guarded {count}: {reason}");
        Require(tested >= 17 && moved >= 16, $"Too few supported native groups: {tested}, movable {moved}.");
        Console.WriteLine($"PASS: {tested} native GroundControl groups: parameters, IDs, pivot rebasing, exact undo and collision alignment; {moved} whole-group relocations; {parametersOnly} settings-only groups; {savedTypes.Count} controller types saved/reopened.");
    }

    private static void Check(ShrinePlacementEditor editor, ShrineActor actor, ShrineGroundControl group, ShrineSceneAssets assets,
        string root, string output, bool save)
    {
        var original = assets.Compound!.Snapshot(); var poses = WorldShapes(assets.Compound);
        var definitions = ShrinePlacementEditor.GroundControlParameters(actor.Name);
        var values = editor.GroundControlValues(actor.Index);
        var changed = values.ToDictionary(p => p.Key, p => p.Value.Select(v => v + 1).ToArray());
        editor.SetGroundControl(actor.Index, ShrineTransform.Local(actor), false, checked((ushort)actor.GroupId), actor.GroupGuid, changed);
        foreach (var (key, value) in changed) Require(value.SequenceEqual(editor.GroundControlValues(actor.Index)[key]), "Parameter edit was lost");
        Require(editor.Compound!.Snapshot().SequenceEqual(original), "Parameter edit changed collision");
        if (definitions.Count > 0) { editor.Undo(); editor.Redo(); }

        var target = ShrineTransform.Local(actor) with { Position = actor.LocalPosition + new Vector3(2, -1, 3), Rotation = actor.Rotation + new Vector3(.1f, .3f, -.2f) };
        editor.SetGroundControl(actor.Index, target, false, checked((ushort)actor.GroupId), actor.GroupGuid, changed);
        Compare(poses, WorldShapes(editor.Compound!), new Dictionary<ulong, Matrix4x4>());
        Require(editor.Scene.Actors.Where(a => a.Index != actor.Index).All(a => a.WorldTransform == assets.Scene.Actors[a.Index].WorldTransform), "Pivot-only edit moved a member");
        Require(editor.Compound!.Bodies.Single(b => b.Index == group.Body).Position != assets.Compound.Bodies.Single(b => b.Index == group.Body).Position, "Body pivot stayed behind");
        var pivotBytes = editor.Compound.Snapshot(); editor.Undo(); editor.Redo();
        Require(pivotBytes.SequenceEqual(editor.Compound!.Snapshot()), "Pivot redo accumulated error");
        editor.Undo();

        if (group.MoveRestriction == null)
        {
            editor.SetGroundControl(actor.Index, target, true, checked((ushort)actor.GroupId), actor.GroupGuid, changed);
            Require(Matrix4x4.Invert(actor.WorldTransform, out var inverse), "Singular controller");
            var delta = inverse * editor.Scene.Actors[actor.Index].WorldTransform;
            var moves = group.Members.ToDictionary(i => assets.Scene.Actors[i].PlacementId != 0 ? assets.Scene.Actors[i].PlacementId : assets.Scene.Actors[i].Hash, _ => delta);
            Compare(poses, WorldShapes(editor.Compound!), moves);
            foreach (int member in group.Members)
                Require(Vector3.Distance(editor.Scene.Actors[member].Position, Vector3.Transform(assets.Scene.Actors[member].Position, delta)) < .002f, "Member stayed behind");
            editor.Undo();
        }
        ushort newId = Enumerable.Range(1, ushort.MaxValue).Select(i => (ushort)i).First(i => assets.Compound.Bodies.All(b => b.GroupId != i) && assets.Scene.Actors.All(a => a.GroupId != i));
        ulong newGuid = ulong.MaxValue - actor.Hash;
        editor.SetGroundControl(actor.Index, ShrineTransform.Local(actor), false, newId, newGuid, changed);
        Require(group.Members.All(i => editor.Scene.Actors[i].GroupId == newId && editor.Scene.Actors[i].GroupGuid == newGuid), "Member identity mismatch");
        Require(editor.Compound!.ActorLinks.Where(l => l.BodyIndex == group.Body).All(l => l.GroupId == newId), "Collision group identity mismatch");
        Compare(poses, WorldShapes(editor.Compound!), new Dictionary<ulong, Matrix4x4>());
        var beforeFailure = editor.Compound.Snapshot();
        try { editor.SetGroundControl(actor.Index, ShrineTransform.Local(actor), false, 0, newGuid, changed); throw new Exception("Zero group accepted"); }
        catch (ArgumentException) { }
        Require(beforeFailure.SequenceEqual(editor.Compound.Snapshot()), "Failed group edit changed collision");
        if (save)
        {
            editor.SetGroundControl(actor.Index, target, group.MoveRestriction == null, newId, newGuid, changed);
            string folder = editor.Save(output);
            foreach (string source in assets.Scene.Actors.Select(a => a.SourceScene).Distinct())
            {
                var old = Byml.FromBinary(BphscDocument.ReadResource(source, [root]));
                var reopened = Byml.FromBinary(BphscDocument.ReadResource(Path.Combine(folder, "romfs", Path.GetRelativePath(root, source))));
                var records = reopened.GetMap()["Actors"].GetArray();
                foreach (int member in group.Members.Where(i => assets.Scene.Actors[i].SourceScene == source))
                {
                    int index = assets.Scene.Actors[member].SourceIndex;
                    var oldRecord = old.GetMap()["Actors"].GetArray()[index].GetMap(); var record = records[index].GetMap();
                    Require(record["Phive"].GetMap()["Placement"].GetMap()["GroupGUID"].GetUInt64() == newGuid, "Saved member GUID mismatch");
                    var reopenedActor = ShrineSceneDocument.Load(Path.Combine(folder, "romfs", Path.GetRelativePath(root, source))).Actors[index];
                    Require(reopenedActor.Position == editor.Scene.Actors[member].LocalPosition && reopenedActor.Rotation == editor.Scene.Actors[member].Rotation,
                        "Saved member pose differs from edited group");
                    foreach (string key in new[] { "Translate", "Rotate", "Scale" })
                        if (oldRecord.TryGetValue(key, out var originalValue)) record[key] = originalValue; else record.Remove(key);
                    record["Phive"] = oldRecord["Phive"];
                    if (member == actor.Index)
                    {
                        CheckParameters(records[index], changed);
                        var savedActor = ShrineSceneDocument.Load(Path.Combine(folder, "romfs", Path.GetRelativePath(root, source))).Actors[index];
                        Require(savedActor.Parameters.Any(p => p.StartsWith(changed.Keys.First() + ":")), "Saved movement missing");
                        if (oldRecord.TryGetValue("Dynamic", out var dynamic)) record["Dynamic"] = dynamic; else record.Remove("Dynamic");
                    }
                }
                Require(old.ToBinary(Endianness.Little, 7).SequenceEqual(reopened.ToBinary(Endianness.Little, 7)), "Unrelated BANC/AI data changed");
            }
            var savedCompound = BphscDocument.Load(Path.Combine(folder, "romfs", Path.GetRelativePath(root, assets.CompoundPath!)));
            Require(savedCompound.Snapshot().SequenceEqual(editor.Compound.Snapshot()), "Saved GroundControl collision mismatch");
            Console.WriteLine("GroundControl export: " + folder);
        }
        while (editor.CanUndo) editor.Undo();
        Require(editor.Compound!.Snapshot().SequenceEqual(original), "GroundControl undo did not restore exact original bytes");
        Require(assets.Compound.Snapshot().SequenceEqual(original), "GroundControl edited source collision");
    }

    private static void CheckParameters(Byml record, IReadOnlyDictionary<string, float[]> expected)
    {
        foreach (var (key, values) in expected)
        {
            var value = record.GetMap()["Dynamic"].GetMap()[key];
            float Number(Byml node) => node.Type == BymlNodeType.Int ? node.GetInt() : node.GetFloat();
            var actual = value.Type == BymlNodeType.Array ? value.GetArray().Select(Number).ToArray() : new[] { Number(value) };
            Require(values.SequenceEqual(actual), "Saved parameter changed: " + key);
        }
    }

    private static Dictionary<(ulong Actor, int Reference), Matrix4x4> WorldShapes(BphscDocument doc)
    {
        var b = doc.Snapshot(); int I(int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
        Vector3 V(int at) => new(BitConverter.ToSingle(b, at), BitConverter.ToSingle(b, at + 4), BitConverter.ToSingle(b, at + 8));
        int image = I(12);
        var entries = Enumerable.Range(0, I(image + 0x4008)).Select(i => image + i * 4)
            .Concat(Enumerable.Range(0, I(image + 0x400c)).Select(i => image + 0x2000 + i * 4)).ToArray();
        var result = new Dictionary<(ulong, int), Matrix4x4>();
        foreach (var link in doc.ActorLinks.Where(l => l.FirstInstance != uint.MaxValue))
            for (uint r = link.FirstInstance; r <= link.EndInstance; r++)
            {
                int reference = checked(I(28) + (int)r * 20);
                int slot = Array.FindIndex(entries, e => b[e + 2] == link.BodyIndex && b[e + 3] == b[reference + 5]);
                int offset = I(56 + slot * 4); int data = -1;
                for (int at = offset + 8; at < offset + I(440 + slot * 4);)
                {
                    int length = (int)(BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at)) & 0x3fffffff);
                    if (b.AsSpan(at + 4, 4).SequenceEqual("DATA"u8)) { data = at + 8; break; }
                    Require(length >= 8, "Invalid tag chunk"); at += length;
                }
                Require(data >= 0, "No DATA");
                int instance = checked(data + 72 + (int)BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(data + 72)) + U16(reference + 10) * 64);
                var q = new Quaternion(V(instance), BitConverter.ToSingle(b, instance + 12));
                var pose = Matrix4x4.CreateScale(V(instance + 32)) * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q)) * Matrix4x4.CreateTranslation(V(instance + 16));
                result.Add((link.Hash, (int)(r - link.FirstInstance)), pose * ShrinePlacementEditor.BodyPose(doc.Bodies.Single(x => x.Index == link.BodyIndex)));
            }
        return result;
    }

    private static void Compare(Dictionary<(ulong Actor, int Reference), Matrix4x4> original,
        Dictionary<(ulong Actor, int Reference), Matrix4x4> actual, IReadOnlyDictionary<ulong, Matrix4x4> deltas)
    {
        Require(original.Count == actual.Count, "Collision instance count changed");
        foreach (var (key, pose) in original)
        {
            var expected = pose * deltas.GetValueOrDefault(key.Actor, Matrix4x4.Identity);
            foreach (var point in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                Require(Vector3.Distance(Vector3.Transform(point, expected), Vector3.Transform(point, actual[key])) < .01f, "GroundControl collision/world mismatch: " + key);
        }
    }
}
