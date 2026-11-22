using System.Numerics;
using BymlLibrary;
using HKCLTool;
using PhysicsTool.Core;

internal static class MapAssetChecks
{
    internal static void NearHash(string root, ulong hash, string output)
    {
        Directory.CreateDirectory(output);
        var scenes = new Dictionary<string, ShrineSceneDocument>(StringComparer.OrdinalIgnoreCase);
        var actors = new List<(string Cell, ShrineActor Actor)>();
        void Read(string path, string cell, Matrix4x4 parent, int depth)
        {
            if (depth > 16) throw new InvalidDataException("Cyclic BANC");
            if (!scenes.TryGetValue(path, out var scene)) scenes[path] = scene = ShrineSceneDocument.Load(path, [root], allowMissingActors: true);
            foreach (var actor in scene.Actors)
            {
                var pose = ShrineSceneAssets.Placement(actor) * parent;
                actors.Add((cell, actor with { Position = pose.Translation, WorldTransform = pose, SourceScene = path }));
                if (actor.MergedPath.Length == 0) continue;
                string child = Path.GetFullPath(Path.Combine(root, actor.MergedPath));
                if (!File.Exists(child)) child += ".zs";
                if (File.Exists(child)) Read(child, cell, pose, depth + 1);
            }
        }
        foreach (string cell in new[] { "A-2", "A-1", "A-3", "B-1", "B-2", "B-3" })
            foreach (string kind in new[] { "Static", "Dynamic" })
            {
                string path = Path.Combine(root, "Banc/MainField", $"{cell}_{kind}.bcett.byml.zs");
                if (File.Exists(path)) Read(path, cell, Matrix4x4.Identity, 0);
            }
        var found = actors.Where(a => a.Actor.Hash == hash).ToArray();
        if (found.Length == 0) throw new Exception($"Hash {hash:x16} not found in A-2 or its neighboring sections");
        var target = found[0].Actor;
        var lines = new List<string>();
        foreach (var match in found) lines.Add($"TARGET {match.Cell} {match.Actor.Name} {match.Actor.Hash:x16} {match.Actor.Position} source={match.Actor.SourceScene}");
        var terrain = TotkTerrainFormat.Parse(File.ReadAllBytes(Path.Combine(root, "TerrainArc/MainField.tscb")));
        var area = terrain.Areas.Where(a => a.Size >= 62.5f && Math.Abs(a.X-target.Position.X) < a.Size*.5f && Math.Abs(a.Z-target.Position.Z) < a.Size*.5f).MinBy(a => a.Size)!;
        string archive = Path.Combine(root, "TerrainArc/MainField", TotkTerrainFormat.ArchiveName(area.Name));
        var heights = TotkTerrain.OpenArchive(archive + ".hght.ta.zs");
        var materials = TotkTerrain.OpenArchive(archive + ".mate.ta.zs");
        var h = TotkTerrainFormat.DecodeHeight(heights.Read(heights.Entries.Single(e => e.Name == area.Name + ".hght")).Span);
        var m = TotkTerrainFormat.DecodeMaterial(materials.Read(materials.Entries.Single(e => e.Name == area.Name + ".mate")).Span);
        int tx = (int)(2 + (target.Position.X-area.X+area.Size*.5f)*256/area.Size);
        int tz = (int)(2 + (target.Position.Z-area.Z+area.Size*.5f)*256/area.Size);
        int sample = tz * 260 + tx;
        lines.Add($"TERRAIN tile={area.Name}, center={area.X},{area.Z}, size={area.Size}, nearest height={h[sample]*terrain.HeightScale}, hole={TotkTerrainFormat.Hole(m[sample*4],m[sample*4+1])}");
        foreach (var item in actors.Where(a => Vector3.Distance(a.Actor.Position, target.Position) < 180).OrderBy(a => Vector3.Distance(a.Actor.Position, target.Position)))
        {
            var a = item.Actor;
            lines.Add($"{item.Cell} {a.Name} {a.Hash:x16} position={a.Position} distance={Vector3.Distance(a.Position,target.Position):0.0} merge={a.MergedPath} source={Path.GetFileName(a.SourceScene)}");
        }
        File.WriteAllLines(Path.Combine(output, "near-hash.txt"), lines);
        File.WriteAllText(Path.Combine(output, "target-source.yml"), Byml.FromBinary(BphscDocument.ReadResource(target.SourceScene, [root])).ToYaml());
        Console.WriteLine(string.Join("\n", lines.Take(20)));
    }
    internal static void Cave(string root, string actorName, string output)
    {
        Directory.CreateDirectory(output);
        var pack = BotwSarcArchive.Parse(BphscDocument.ReadResource(Path.Combine(root, "Pack/Actor", actorName + ".pack.zs"), [root]));
        string parameters = string.Join("\n", pack.Entries.Where(e => e.Name.EndsWith(".bgyml")).Select(e => e.Name + "\n" + Byml.FromBinary(pack.Read(e).ToArray()).ToYaml()));
        File.WriteAllText(Path.Combine(output, actorName + ".txt"), parameters);
        Console.WriteLine(string.Join("\n", parameters.Split('\n').Where(l => l.Contains("Cave", StringComparison.OrdinalIgnoreCase))));
        var scene = ShrineSceneDocument.Load(Path.Combine(root, "Banc/MainField/I-4_Static.bcett.byml.zs"), [root]);
        foreach (var a in scene.Actors.Where(a => a.Name.Contains("Cave"))) Console.WriteLine($"{a.Name} {a.Hash:x16} {a.Position} {string.Join(";", a.Parameters)}");
    }
    internal static void Inspect(string root, string cell, string output)
    {
        Directory.CreateDirectory(output);
        var lines = new List<string>();
        var actors = new List<ShrineActor>();
        void Read(string path, Matrix4x4 parent, int depth)
        {
            if (depth > 16) throw new InvalidDataException("Cyclic BANC");
            var scene = ShrineSceneDocument.Load(path, [root]);
            foreach (var a in scene.Actors)
            {
                var pose = ShrineSceneAssets.Placement(a) * parent;
                actors.Add(a with { Position = pose.Translation, WorldTransform = pose, SourceScene = path });
                if (a.MergedPath.Length > 0)
                {
                    string child = Path.Combine(root, a.MergedPath);
                    if (!File.Exists(child)) child += ".zs";
                    if (File.Exists(child)) Read(child, pose, depth + 1);
                    else lines.Add("Missing merged: " + child);
                }
            }
        }
        foreach (string kind in new[] { "Static", "Dynamic" }) Read(Path.Combine(root, "Banc", "MainField", cell + "_" + kind + ".bcett.byml.zs"), Matrix4x4.Identity, 0);
        foreach (var group in actors.Where(a => a.IsMergeParts).GroupBy(a => a.Name))
            Console.WriteLine($"MERGEPARTS {group.Key}: {group.Count()} placements");
        var target = actors.FirstOrDefault(a => a.Hash == 0xb9100fcd582e3bcc);
        if (target != null)
        {
            lines.Add($"TARGET {target.Name} {target.Position} {target.SourceScene}");
            foreach (var actor in actors.Where(a => Vector3.Distance(a.Position, target.Position) < 100).OrderBy(a => Vector3.Distance(a.Position, target.Position)))
            {
                lines.Add($"{actor.Name} {actor.Hash:x16} {actor.Position} {actor.MergedPath}");
                string pack = Path.Combine(root, "Pack", "Actor", actor.Name + ".pack.zs");
                try
                {
                    var resource = new BotwActorResources(pack, [root]);
                    lines.Add(" Models: " + string.Join(", ", resource.Models.Select(m => m.Resource + "/" + m.Model)));
                }
                catch (Exception ex) { lines.Add(" ERROR " + ex.Message); }
            }
            var raw = Byml.FromBinary(BphscDocument.ReadResource(target.SourceScene, [root]));
            File.WriteAllText(Path.Combine(output, "chest-source.yml"), raw.ToYaml());
            var chestPack = BotwSarcArchive.Parse(BphscDocument.ReadResource(Path.Combine(root, "Pack", "Actor", target.Name + ".pack.zs"), [root]));
            File.WriteAllText(Path.Combine(output, "chest-parameters.txt"), string.Join("\n", chestPack.Entries.Where(e => e.Name.EndsWith(".bgyml")).Select(e => e.Name + "\n" + Byml.FromBinary(chestPack.Read(e).ToArray()).ToYaml())));
        }
        File.WriteAllLines(Path.Combine(output, "near-chest.txt"), lines);
        Console.WriteLine(string.Join("\n", lines));
        string terrain = Path.Combine(root, "TerrainArc", "MainField");
        var waterFile = Directory.EnumerateFiles(terrain, "*.water.extm.ta.zs").First();
        var archive = BotwSarcArchive.Parse(BphscDocument.ReadResource(waterFile, [root]));
        foreach (var entry in archive.Entries.Take(4))
        {
            var b = archive.Read(entry).ToArray();
            File.WriteAllBytes(Path.Combine(output, entry.Name), b);
            Console.WriteLine($"WATER {waterFile} {entry.Name} bytes={b.Length} {Convert.ToHexString(b.AsSpan(0, Math.Min(64, b.Length)))}");
        }
        var index = File.ReadAllBytes(Path.Combine(root, "TerrainArc", "MainField.tscb"));
        int U(int p) => BitConverter.ToInt32(index, p);
        int start = 0x70 + U(0x70);
        for (int i = 0; i < U(0x30); i++)
        {
            int p = start + i * 4; p += U(p);
            Console.WriteLine($"IMAGE {U(p):x} width={BitConverter.ToUInt16(index, p + 16)} margin={index[p + 20]} mips={U(p + 24)} format={index[p + 28]} archive={U(p + 32)} flags={U(p + 80):x} data={Convert.ToHexString(index.AsSpan(p,128))}");
        }
    }
}
