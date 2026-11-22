using System.Numerics;
using System.Security.Cryptography;
using PhysicsTool.Core;

internal static class MainFieldSceneChecks
{
    internal static void Run(string root, string cell = "B-7")
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(cell, "^[A-J]-[1-8]$")) throw new ArgumentException("Invalid MainField cell");
        string path = Path.Combine(root, "Banc", "MainField", cell + "_Static.bcett.byml.zs");
        byte[] original = SHA256.HashData(File.ReadAllBytes(path));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var assets = ShrineSceneAssets.Load(path, root);
        var terrain = assets.Model.Meshes.Where(m => m.Name.StartsWith("terrain:")).ToArray();
        Require(terrain.Length > 0, "MainField scene has no terrain: " + string.Join("\n", assets.Warnings.Where(w => w.StartsWith("Terrain"))));
        Require(assets.RenderedActors > 0, "MainField scene lost actor models: " + string.Join("\n", assets.Warnings.Take(10)));
        int x = (cell[0] - 'A' - 5) * 1000, z = (cell[2] - '1' - 4) * 1000;
        Require(assets.TerrainStartPosition is { } start && start.X >= x && start.X <= x + 1000 && start.Z >= z && start.Z <= z + 1000, "Camera starts outside " + cell);
        var editor = new ShrinePlacementEditor(assets, root);
        var actor = assets.Scene.Actors.First();
        var preview = editor.PreviewMove([actor.Index], new Vector3(2, 3, 4));
        Require(terrain.All(preview.Meshes.Contains), "Actor preview transformed or dropped terrain");
        Require(terrain.All(m => ShrineSceneAssets.MeshActorIndex(m) == null), "Terrain was assigned an actor owner");
        Require(assets.Model.TriangleCount == assets.Model.Meshes.Sum(m => m.Indices.Length / 3), "Scene triangle count mismatch");
        Require(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Loading changed source BANC");
        Console.WriteLine($"PASS: {cell} with {assets.RenderedActors} modeled actors, {terrain.Length} terrain chunks, {assets.Model.TriangleCount:N0} triangles; camera {assets.TerrainStartPosition}; {watch.Elapsed.TotalSeconds:0.0}s.");
        foreach (string warning in assets.Warnings.Where(w => w.StartsWith("Terrain"))) Console.WriteLine(warning);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
