using System.Buffers.Binary;
using System.Numerics;
using BymlLibrary;
using PhysicsTool.Core;

internal static class ShrinePopulationChecks
{
    private static void Require(bool test, string message) { if (!test) throw new Exception(message); }
    private static int I(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
    private static long L(byte[] b, int at) => BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(at));
    private static int H(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static int Chunk(byte[] b, int at) => (int)(BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at)) & 0x3fffffff);
    private static (int Data, int Shapes, int Count, int Nodes, int NodeCount, int Tail, int End) Layout(byte[] b)
    {
        for (int at = 8; at < Chunk(b, 0); at += Chunk(b, at))
            if (b.AsSpan(at + 4, 4).SequenceEqual("DATA"u8))
            {
                int d = at + 8, shapes = checked(d + 72 + (int)L(b, d + 72)), count = I(b, d + 80), nodes = checked(d + 232 + (int)L(b, d + 232)), nn = I(b, d + 240);
                return (d, shapes, count, nodes, nn, nodes + nn * 128, at + Chunk(b, at));
            }
        throw new Exception("No compound DATA");
    }
    internal static void Native(string folder)
    {
        int tested = 0, guarded = 0;
        foreach (string path in Directory.EnumerateFiles(folder, "*.bphsc.zs"))
        {
            var original = BphscDocument.Load(path); byte[] before = original.Snapshot();
            if (original.Water.Count != 0 || original.ExternalMeshCount != 0) { guarded++; continue; }
            var actor = original.ActorLinks.FirstOrDefault(a => a.BodyIndex == 0 && a.GroupId == 0 && a.FirstInstance != uint.MaxValue);
            if (actor == null) continue;
            ulong id = ulong.MaxValue;
            while (original.ActorLinks.Any(l => l.Hash == id || l.Hash == id - 1)) id -= 2;
            var copy = original.EditActors(new HashSet<ulong> { actor.Hash }, [new(actor.Hash, id, 123), new(actor.Hash, id - 1, 456)]);
            Require(copy.ActorLinks.Count == original.ActorLinks.Count + 1 && copy.ActorLinks.All(l => l.Hash != actor.Hash), "Actor table did not replace source with copies");
            Verify(original, copy);
            var removed = copy.EditActors(new HashSet<ulong> { id, id - 1 }, []);
            Require(removed.ActorLinks.Count == original.ActorLinks.Count - 1, "Duplicate deletion failed");
            Verify(copy, removed);
            var moved = copy.MoveActors(new Dictionary<ulong, Vector3> { [id] = new(2, 3, 4) });
            Require(!moved.Snapshot().SequenceEqual(copy.Snapshot()), "Copied collision could not move independently");
            Require(original.Snapshot().SequenceEqual(before), "Population edits modified source");
            // Removing all static actors exercises genuinely empty shape arrays and trees.
            var cleared = original.EditActors(original.ActorLinks.Where(l => l.BodyIndex == 0 && l.GroupId == 0).Select(l => l.Hash).ToHashSet(), []);
            Verify(original, cleared);
            tested++;
        }
        Console.WriteLine($"PASS: {tested} native compounds: duplicate/delete, independent transforms, actor tags/hash lookup, collision trees, empty bodies and preserved geometry; {guarded} unsupported layouts guarded.");
    }
    private static void Verify(BphscDocument original, BphscDocument changed)
    {
        var b = changed.Snapshot(); int image = I(b, 12), refs = I(b, 28), count = I(b, image + 0x4014);
        var entries = Enumerable.Range(0, I(b, image + 0x4008)).Select(i => image + i * 4)
            .Concat(Enumerable.Range(0, I(b, image + 0x400c)).Select(i => image + 0x2000 + i * 4)).ToArray();
        var owners = new HashSet<(int, int)>(); int refCount = 0;
        foreach (var actor in changed.ActorLinks)
        {
            int capacity = changed.ActorLinks.Count * 2, slot = (int)(actor.Hash % (ulong)capacity), steps = 0;
            while (unchecked((ulong)L(b, I(b, 20) + slot * 8)) != actor.Hash)
            { Require(++steps < capacity, "Hash missing from lookup"); slot = (slot + 1) % capacity; }
            Require(H(b, I(b, 24) + slot * 2) == actor.Index, "Hash lookup points at wrong actor");
            if (actor.FirstInstance == uint.MaxValue) continue;
            for (uint j = actor.FirstInstance; j <= actor.EndInstance; j++)
            {
                int at = refs + (int)j * 20;
                Require(I(b, at) == j && H(b, at + 8) == actor.Index, "Reference actor tag/index incorrect");
                int body = Array.FindIndex(entries, e => b[e + 2] == actor.BodyIndex && b[e + 3] == b[at + 5]);
                Require(body >= 0 && owners.Add((body, H(b, at + 10))), "Shared/missing instance ownership"); refCount++;
                var section = changed.ExtractSection(body); var layout = Layout(section);
                Require(H(section, layout.Shapes + H(b, at + 10) * 64 + 44) == j, "Instance shape-reference tag is stale");
            }
        }
        Require(refCount == count, "Unreferenced actor collision record");
        for (int i = 0; i < changed.CompoundCount; i++)
        {
            var old = original.ExtractSection(i); var now = changed.ExtractSection(i);
            var a = Layout(old); var c = Layout(now);
            Require(old.AsSpan(a.Tail, a.End - a.Tail).SequenceEqual(now.AsSpan(c.Tail, c.End - c.Tail)), "Opaque geometry was changed");
            Require(I(now, c.Data + 92) == c.Count && c.Count == owners.Count(p => p.Item1 == i), "Instance allocation count wrong");
            var leaves = new HashSet<int>(); var nodes = new HashSet<int>();
            void Walk(int n)
            {
                Require(n > 0 && n < c.NodeCount && nodes.Add(n), "Invalid rebuilt tree");
                int at = c.Nodes + n * 128; bool leaf = now[at + 112] != 0;
                for (int lane = 0; lane < 4; lane++)
                {
                    int child = I(now, at + 96 + lane * 4);
                    if (child == (leaf ? -1 : 0)) continue;
                    if (leaf) Require(child >= 0 && child < c.Count && leaves.Add(child), "Invalid rebuilt leaf"); else Walk(child);
                }
            }
            Walk(1); Require(leaves.Count == c.Count, "Missing/ghost collision leaf");
            for (int n = 0; n < c.Count; n++)
            {
                int field = c.Shapes + n * 64 + 48; long target = field + L(now, field);
                Require(target >= c.Tail && target < c.End, "Relocated shape pointer is invalid");
            }
        }
        _ = BphscDocument.Parse(b);
    }

    internal static void Editing(ShrineSceneAssets assets, string root, string output)
    {
        var editor = new ShrinePlacementEditor(assets, root);
        var actor = editor.Scene.Actors.First(a => a.ParentIndex < 0 && a.MergedPath.Length == 0 && editor.PopulationRestriction(a.Index, false) == null &&
            assets.Compound!.ActorLinks.Any(l => l.Hash == (a.PlacementId != 0 ? a.PlacementId : a.Hash) && l.FirstInstance != uint.MaxValue));
        byte[] initial = editor.Compound!.Snapshot(); int originalCount = editor.Scene.Actors.Count;
        int duplicate = editor.Duplicate([actor.Index]).Single(); var copy = editor.Scene.Actors[duplicate];
        Require(copy.Hash != actor.Hash && editor.Scene.Actors.Count == originalCount + 1, "Duplicate ID or scene count invalid");
        Require(editor.Model.Meshes.Count > assets.Model.Meshes.Count, "Duplicate model missing");
        editor.Move(duplicate, copy.LocalPosition + new Vector3(4, 1, 2));
        Require(editor.Scene.Actors[actor.Index].Position == actor.Position, "Duplicate movement changed source");
        byte[] moved = editor.Compound!.Snapshot();
        editor.Delete([actor.Index]);
        Require(editor.Scene.Actors.Count == originalCount && editor.Scene.Actors.All(a => a.Hash != actor.Hash) &&
            editor.Scene.Actors.Any(a => a.Hash == copy.Hash), "Delete removed the wrong actor");
        editor.Undo(); Require(editor.Compound!.Snapshot().SequenceEqual(moved), "Delete undo did not restore exact collision");
        editor.Redo();
        string saved = editor.Save(output);
        var path = Path.Combine(saved, "romfs", Path.GetRelativePath(root, actor.SourceScene));
        var scene = ShrineSceneDocument.Load(path);
        Require(scene.Actors.All(a => a.Hash != actor.Hash) && scene.Actors.Single(a => a.Hash == copy.Hash).Position == copy.LocalPosition + new Vector3(4, 1, 2), "Saved duplicate/delete or pose is incorrect");
        var compound = BphscDocument.Load(Path.Combine(saved, "romfs", Path.GetRelativePath(root, assets.CompoundPath!)));
        Require(compound.Snapshot().SequenceEqual(editor.Compound!.Snapshot()), "Saved collision differs from edited collision");
        var sourceByml = Byml.FromBinary(BphscDocument.ReadResource(actor.SourceScene, [root]));
        var savedByml = Byml.FromBinary(BphscDocument.ReadResource(path));
        var oldActors = sourceByml.GetMap()["Actors"].GetArray(); var newActors = savedByml.GetMap()["Actors"].GetArray();
        var expectedActors = oldActors.Where((_, i) => i != actor.SourceIndex).ToArray();
        for (int i = 0; i < expectedActors.Length; i++)
            Require(expectedActors[i].ToBinary(Revrs.Endianness.Little, 7).SequenceEqual(newActors[i].ToBinary(Revrs.Endianness.Little, 7)), "Unrelated actor metadata changed");
        foreach (var entry in sourceByml.GetMap().Where(p => p.Key != "Actors"))
            Require(entry.Value.ToBinary(Revrs.Endianness.Little, 7).SequenceEqual(savedByml.GetMap()[entry.Key].ToBinary(Revrs.Endianness.Little, 7)), "Unrelated BANC metadata changed");
        editor.Undo(); editor.Undo(); editor.Undo();
        Require(editor.Compound!.Snapshot().SequenceEqual(initial) && editor.Scene.Actors.Count == originalCount, "Undo did not restore original population");
        var blocked = editor.Scene.Actors.First(a => a.Name.StartsWith("GroundControlActor"));
        bool rejected = false;
        try { editor.Delete([blocked.Index]); } catch (NotSupportedException) { rejected = true; }
        Require(rejected && editor.Compound.Snapshot().SequenceEqual(initial), "Unsafe group deletion was not atomic");
        Console.WriteLine($"PASS: {actor.Name} duplicate/move/delete, unique IDs, mesh population, exact undo/redo and paired BANC/BPHSC save. Output: {saved}");
    }
}
