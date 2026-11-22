using System.Numerics;
using BymlLibrary;
using PhysicsTool.Core;

internal static class ShrineActorConnectionChecks
{
    internal static void Run(string? root)
    {
        var scene = ShrineSceneDocument.Parse(Byml.FromText("""
            Actors:
            - {Gyaml: Flower, Hash: !ul 1, Links: [{Dst: !ul 2, Name: Reference}]}
            - {Gyaml: Flower, Hash: !ul 2}
            - {Gyaml: Korok, Hash: !ul 3}
            AiGroups:
            - Hash: !ul 99
              Logic: FlowerTrail.ain
              References:
              - {Reference: !ul 1}
              - {Reference: !ul 2}
              - {Reference: !ul 3}
              - {Reference: !ul 3}
              - {Reference: !ul 999}
              - {Id: AiGroup, Path: NestedGroup}
            """).ToBinary(Revrs.Endianness.Little, 7));
        Require(scene.Actors.All(a => a.Groups.Count == 1), "AI group memberships missing");
        var links = new ShrineActorConnections(scene);
        var lines = links.Lines(scene.Actors, Set(0, 1, 2), Set(2));
        Require(lines.Count == 3 && lines.Contains((0, 2)) && lines.Contains((1, 2)), "Direct/group edges or selected anchor missing");
        Require(links.Lines(scene.Actors, Set(0, 2), Set()).SequenceEqual(new[] { (0, 2) }), "Hidden/missing references must not create lines");
        var instances = scene.Actors.Select(a => a with { ParentIndex = 10, SourceScene = "Merged" })
            .Concat(scene.Actors.Select(a => a with { Index = a.Index + 3, ParentIndex = 20, SourceScene = "Merged" })).ToList();
        lines = new ShrineActorConnections(new(instances)).Lines(instances, Set(0, 1, 2, 3, 4, 5), Set());
        Require(lines.All(l => l.A / 3 == l.B / 3), "Connections crossed merged-scene instances");
        var moved = instances[2] with { Position = new Vector3(10, 20, 30) };
        instances[2] = moved;
        Require(new ShrineActorConnections(new(instances)).Resolve(instances[0], 3) == moved, "Moved endpoint is stale");
        var deleted = new ShrineSceneDocument(instances.Where(a => a.Index != 2).ToList());
        Require(new ShrineActorConnections(deleted).Resolve(instances[0], 3) == null, "Deleted endpoint resolved into another instance");
        var farScene = new ShrineSceneDocument([scene.Actors[0] with { Name = "Cliff" }, scene.Actors[1] with { Name = "Cliff_Far" }]);
        Require(ShrineSceneAssets.FarModelOwners(farScene).TryGetValue(1, out int near) && near == 0, "Matching far model was not recognized");
        farScene = new([farScene.Actors[0], farScene.Actors[1] with { WorldTransform = Matrix4x4.CreateTranslation(1, 0, 0) }]);
        Require(ShrineSceneAssets.FarModelOwners(farScene).Count == 0, "Distinct far placement was suppressed");
        if (root != null)
        {
            var native = ShrineSceneDocument.Load(Path.Combine(root, "Banc/MainField/F-4_Static.bcett.byml.zs"), [root]);
            var flower = native.Actors.Single(a => a.Hash == 0xaa9d59336af4011b);
            var group = flower.Groups.Single(g => g.Logic.Contains("HiddenKorok_TrackingFlower"));
            var resolver = new ShrineActorConnections(native);
            var members = group.Members.Select(h => resolver.Resolve(flower, h)).OfType<ShrineActor>().ToArray();
            Require(members.Count(a => a.Name == "Obj_Plant_Korok_A_01") == 5, "Native five-flower trail missing");
            var nativeLines = resolver.Lines(native.Actors, native.Actors.Select(a => a.Index).ToHashSet(), Set(flower.Index));
            Require(members.Where(a => a != flower).All(a => nativeLines.Contains((Math.Min(a.Index, flower.Index), Math.Max(a.Index, flower.Index)))), "Native Korok group lines missing");
            Console.WriteLine($"F-4 Korok: {members.Length} resolved group members; {nativeLines.Count} scene connections.");
        }
        Console.WriteLine("PASS actor connections: AI groups, direct links, filters, instance isolation, moved/deleted endpoints and distant model pairing.");
    }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static HashSet<int> Set(params int[] values) => values.ToHashSet();
}
