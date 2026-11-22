using BymlLibrary;
using PhysicsTool.Core;
using System.Diagnostics;
using System.Security.Cryptography;

internal static class MapCatalogChecks
{
    internal static void Run(string output, string? nativeRoot)
    {
        string root = Path.Combine(output, "catalog-" + Guid.NewGuid().ToString("N"));
        string banc = Path.Combine(root, "Banc", "SmallDungeon");
        Directory.CreateDirectory(banc);
        string Write(string name, string yaml)
        {
            string path = Path.Combine(banc, name + ".bcett.byml");
            File.WriteAllBytes(path, Byml.FromText(yaml).ToBinary(Revrs.Endianness.Little, 7));
            return path;
        }
        Write("Metadata", "Rails: []");
        Write("Empty", "Actors: []");
        Write("InvalidActors", "Actors: 3");
        Write("Unnamed", "Actors: [{Hash: !ul 1}]");
        Write("MergePartsOnly", "Actors: [{Gyaml: FldObj_Tar_MergeParts_A_01, Hash: !ul 1}]");
        Write("BadPosition", "Actors: [{Gyaml: Test, Hash: !ul 1, Translate: [1.0]}]");
        File.WriteAllBytes(Path.Combine(banc, "Broken.bcett.byml"), [1, 2, 3]);
        const string actor = "Actors: [{Gyaml: Test, Hash: !ul 1}]";
        string emptyPair = Write("Pair_Static", "Rails: []");
        string populatedPair = Write("Pair_Dynamic", actor);
        Write("Both_Static", actor); Write("Both_Dynamic", actor);
        string compressed = Write("Compressed", actor);
        using (var encoder = new ZstdSharp.Compressor()) File.WriteAllBytes(compressed + ".zs", encoder.Wrap(File.ReadAllBytes(compressed)).ToArray());
        Write("Linked", "Actors: [{Gyaml: MergedActor, Hash: !ul 5, Dynamic: {BancPath: Banc/SmallDungeon/Compressed.bcett.byml}}]");
        var before = Directory.GetFiles(banc).ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
        string cache = Path.Combine(output, "catalog-cache.json");
        var entries = ShrineSceneCatalog.Scan(root, cachePath: cache);
        Require(entries.Count == 4, "Empty or invalid files survived filtering: " + string.Join(", ", entries));
        Require(entries.Single(e => e.ToString().EndsWith("Pair")).Path == populatedPair, "Empty static hid populated dynamic");
        Require(entries.Single(e => e.ToString().EndsWith("Both")).Path.EndsWith("Both_Static.bcett.byml"), "Static preference changed");
        Require(entries.Single(e => e.ToString().EndsWith("Compressed")).Path.EndsWith(".zs"), "Compressed/raw duplicates were not grouped");
        Require(ShrineSceneCatalog.Scan(root, cachePath: cache).SequenceEqual(entries), "Warm cache changed catalog");
        var assets = ShrineSceneAssets.Load(populatedPair, root);
        Require(assets.Scene.Actors.Count == 1, "Empty companion prevented scene loading");
        var linked = ShrineSceneAssets.Load(entries.Single(e => e.ToString().EndsWith("Linked")).Path, root);
        Require(linked.Scene.Actors.Count == 2, "Linked actor scene did not expand");
        Require(new ShrinePlacementEditor(assets, root).SourceCount == 2, "Empty companion was not preserved for saving");
        foreach (var (path, hash) in before) Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Catalog altered a source");
        Write("Metadata", actor);
        Require(ShrineSceneCatalog.Scan(root, cachePath: cache).Count == 5, "Changed file was not rescanned");
        File.Delete(Path.Combine(banc, "Metadata.bcett.byml"));
        Require(ShrineSceneCatalog.Scan(root, cachePath: cache).Count == 4, "Removed file remained cached");
        File.WriteAllText(cache, "invalid cache");
        Require(ShrineSceneCatalog.Scan(root, cachePath: cache).Count == 4, "Damaged cache prevented recovery");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { ShrineSceneCatalog.Scan(root, cancelled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { }
        if (nativeRoot != null)
        {
            var timer = Stopwatch.StartNew();
            string nativeCache = Path.Combine(output, "native-catalog-cache.json");
            entries = ShrineSceneCatalog.Scan(nativeRoot, progress: new Progress<string>(Console.WriteLine), cachePath: nativeCache);
            foreach (string name in new[] { "Dungeon000", "Dungeon004", "I-4", "F-4" })
                Require(entries.Any(e => Path.GetFileName(e.RelativePath) == name), "Native map disappeared: " + name);
            Require(entries.Count < 136797, "Native catalog was not filtered");
            File.WriteAllLines(Path.Combine(output, "filtered-map-scenes.txt"), entries.Select(e => e.RelativePath));
            Console.WriteLine($"PASS native catalog: {entries.Count:N0} scenes in {timer.Elapsed.TotalSeconds:0.0}s.");
            timer.Restart();
            Require(ShrineSceneCatalog.Scan(nativeRoot, cachePath: nativeCache).SequenceEqual(entries), "Native warm cache changed results");
            Console.WriteLine($"PASS native warm scan: {timer.Elapsed.TotalSeconds:0.0}s.");
        }
        Console.WriteLine("PASS map catalog: content filtering, pairs, compressed files, merged scenes, cancellation and source preservation.");
    }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
}
