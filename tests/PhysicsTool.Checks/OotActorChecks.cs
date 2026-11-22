using HKCLTool;
using PhysicsTool.Core;
using BymlLibrary;

internal static class OotActorChecks
{
    internal static void Run(string root)
    {
        var packPath = Path.Combine(root, "Pack/Actor/Player_Adult.pack.zs");
        var pack = BotwSarcArchive.Parse(new TotkResourceCompression().Decode(File.ReadAllBytes(packPath), [root]));
        var entry = new BotwActorCatalogEntry("Player_Adult", packPath, null, root, true, true, true) { IsOot = true };
        var resources = OotActorResources.Load(entry);
        foreach (var asset in resources.Assets) Console.WriteLine(asset);
        foreach (var warning in resources.Warnings) Console.WriteLine("WARNING: " + warning);
        if (!resources.Assets.Any(a => a.IsCloth)) throw new Exception("Player_Adult cloth not resolved.");
        if (!resources.Assets.Any(a => !a.IsCloth)) throw new Exception("Player_Adult helper parameters not resolved.");
        var catalog = OotActorResources.Scan(root, null, CancellationToken.None);
        Console.WriteLine($"OoT actors: {catalog.Actors.Count}; warnings: {catalog.Warnings.Count}");
        foreach (var warning in catalog.Warnings.Take(10)) Console.WriteLine(warning);
        if (!catalog.Actors.Any(a => a.Name == "Player_Adult" && a.IsOot && !a.IsTotk)) throw new Exception("OoT catalog identity is incorrect.");
        var seen = new HashSet<string>(); int groups = 0, curves = 0;
        foreach (var actor in catalog.Actors)
        foreach (var asset in OotActorResources.Load(actor).Assets.Where(a => !a.IsCloth))
        {
            if (!seen.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(asset.Bytes)))) continue;
            var helper = new OotHelperDocument(asset.Bytes);
            foreach (var group in helper.Groups)
            {
                var fields = helper.Fields(group);
                helper.Apply(group, fields.ToDictionary(f => f.Path, f => f.Value));
                if (!helper.Bytes.SequenceEqual(asset.Bytes)) throw new Exception("Unchanged helper apply altered source: " + asset.Path);
                groups++;
            }
            foreach (var connection in helper.Connections())
            {
                IReadOnlyList<OotCurveKey> keys;
                try { keys = helper.CurveKeys(connection); }
                catch (InvalidOperationException) { continue; }
                var displayed = keys.Select(k => new OotCurveKey((float)(decimal)k.X, (float)(decimal)k.Value, (float)(decimal)k.Incoming, (float)(decimal)k.Outgoing)).ToArray();
                helper.SetCurveKeys(connection, displayed);
                if (!helper.Bytes.SequenceEqual(asset.Bytes)) throw new Exception("Unchanged curve UI values altered source: " + asset.Path);
                curves++;
            }
        }
        Console.WriteLine($"PASS {seen.Count} unique helper files / {groups} driver and driven groups / {curves} curves; no-op field round trips preserved exact bytes.");
    }
}
