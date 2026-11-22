using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop.Rendering;

internal static class OotModelChecks
{
    internal static void Run(string root, string name)
    {
        var entry = new BotwActorCatalogEntry(name, Path.Combine(root, "Pack/Actor", name + ".pack.zs"), null, root, true, true, true) { IsOot = true };
        var resources = OotActorResources.Load(entry);
        Console.WriteLine("Model: " + resources.ModelPath);
        if (resources.ModelPath == null) throw new Exception("Actor model reference missing");
        var model = PortableModelImporter.Load(resources.ModelPath);
        if (model.Meshes.Count == 0 || model.TriangleCount == 0 || model.Bones.Count == 0) throw new Exception("Missing model geometry/skeleton");
        if (!model.Meshes.All(m => m.Material.Totk?.IsOot == true)) throw new Exception("OoT material routing failed");
        Console.WriteLine($"{model.Meshes.Count} meshes; {model.TriangleCount} triangles; {model.Bones.Count} bones; {model.ExternalTextureCount} external textures");
        foreach (var m in model.Meshes) Console.WriteLine($"{m.Name}: graph {m.Material.Totk!.Integers["ootGraph"]}; {m.Material.Totk.Textures.Count(t => t != null)} textures");
        foreach (var warning in model.Warnings) Console.WriteLine(warning);
        if (model.ExternalTextureCount == 0 || model.Warnings.Any(w => w.StartsWith("Texture "))) throw new Exception("Texture loading incomplete");
        Console.WriteLine("PASS OoT actor model and material import");
    }
}
