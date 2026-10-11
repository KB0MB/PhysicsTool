using BymlLibrary;
using HKCLTool;
using System.Numerics;
using System.Security.Cryptography;

internal static class TotkActorChecks
{
    public static void DumpTextures(string path, string output)
    {
        Directory.CreateDirectory(output);
        using var scene = BotwActorScene.Load(path);
        foreach (var mesh in scene.RestModel.Meshes)
        {
            var material = mesh.Material.Totk;
            if (material == null) continue;
            Console.WriteLine(mesh.Name);
            for (int i = 0; i < material.Textures.Length; i++)
            {
                if (material.Textures[i] is not { } texture) continue;
                Console.WriteLine($"  {i}: {texture.Name} {texture.Width}x{texture.Height} mapping={material.Integers[$"totkTextureMapping{i}"]} srt={material.Vectors[$"totkTextureSrt{i}"]}");
                using var bitmap = new SkiaSharp.SKBitmap(texture.Width, texture.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
                System.Runtime.InteropServices.Marshal.Copy(texture.Pixels, 0, bitmap.GetPixels(), texture.Pixels.Length);
                using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
                using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(output, texture.Name + ".png"), png.ToArray());
            }
        }
    }
    public static void Profile(string path)
    {
        using var scene = BotwActorScene.Load(path);
        var animation = scene.Animations.FirstOrDefault();
        double update = 0, geometry = 0; long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 35; i++)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var model = scene.Update(animation, i, true, 1f / 60, i == 0);
            double elapsed = timer.Elapsed.TotalMilliseconds; timer.Restart();
            foreach (var mesh in model.Meshes) _ = PhysicsTool.Desktop.Rendering.ModelVertices.Build(mesh);
            if (i >= 5) { update += elapsed; geometry += timer.Elapsed.TotalMilliseconds; }
        }
        Console.WriteLine($"PROFILE {scene.Resources.ActorName}: actor/solver={update / 30:F2} ms, render geometry/tangents={geometry / 30:F2} ms, allocations={(GC.GetAllocatedBytesForCurrentThread()-allocated)/35/1024:F0} KiB/frame");
    }
    public static void Run(string path, string models)
    {
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        using var scene = BotwActorScene.Load(path, new[] { models }, new Progress<string>(Console.WriteLine));
        Console.WriteLine($"LOADED {scene.Resources.ActorName}: {scene.RestModel.Meshes.Count} meshes, {scene.ClothCount} cloths, {scene.SupportCount} helpers, {scene.Animations.Count} clips");
        foreach (var warning in scene.Resources.Warnings) Console.WriteLine(warning);
        if (scene.ClothCount == 0 || scene.RestModel.Meshes.Count == 0) throw new Exception("Actor missing cloth or geometry");
        if (scene.TextureCount == 0 || scene.RestModel.Meshes.Any(m => m.Material.Totk == null)) throw new Exception("TotK textures or shader bindings missing");
        if (scene.Resources.Warnings.Any(w => w.StartsWith("Texture ") || w.StartsWith("Missing TexToGo/"))) throw new Exception("Native TexToGo decoding failed");
        Console.WriteLine($"TEXTOGO: {scene.TextureCount} decoded surfaces; {scene.RestModel.Meshes.Count(m => m.Material.UseNormal)} meshes with normal maps");
        foreach (var mesh in scene.RestModel.Meshes)
        {
            var bindings = mesh.Material.Totk!;
            Console.WriteLine($"MATERIAL {mesh.Name}: behavior={bindings.Integers["totkMaterialBehavior"]}, textures={bindings.Textures.Count(t => t != null)}, UVs={string.Join(",", Enumerable.Range(0, 8).Where(i => bindings.Textures[i] != null).Select(i => bindings.Integers[$"totkTextureMapping{i}"]))}");
        }
        var profiles = new List<ClothSolverProfile>();
        scene.ConfigureSimulation(s => { profiles.Add(s.Profile); s.RandomWindDirections = false; s.WindGustiness = 0; });
        if (profiles.Any(p => p != ClothSolverProfile.TotkExperimental)) throw new Exception("Actor used the BotW solver path");
        Dictionary<int, Vector3> Probe(bool wind)
        {
            scene.WindEnabled = wind; scene.Update(null, 0, true, 0, true);
            for (var frame = 0; frame < 120; frame++) scene.Update(null, 0, true, 1f / 60);
            return scene.Overlay.Particles.ToDictionary(p => p.Index, p => p.Position);
        }
        var calm = Probe(false); var windy = Probe(true);
        if (!windy.Any(p => Vector3.Distance(p.Value, calm[p.Key]) > .00001f)) throw new Exception("Actor wind did not change cloth positions");
        scene.WindEnabled = false;
        var animation = scene.Animations.FirstOrDefault();
        for (var frame = 0; frame < 120; frame++) scene.Update(animation, animation == null ? 0 : frame % Math.Max(1, animation.FrameCount), true, 1f / 60, frame == 0);
        if (scene.Overlay.Particles.Any(p => !float.IsFinite(p.Position.X) || !float.IsFinite(p.Position.Y) || !float.IsFinite(p.Position.Z))) throw new Exception("Nonfinite actor simulation");
        if (!hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)))) throw new Exception("Actor source changed");
        Console.WriteLine("PASS: native actor simulation, wind on/off response, animation loop, finite positions and source isolation.");
    }
    public static void Inspect(string path)
    {
        var codec = new TotkResourceCompression();
        var pack = BotwSarcArchive.Parse(codec.Decode(File.ReadAllBytes(path), TotkResourceCompression.Roots(path)));
        foreach (var entry in pack.Entries)
        {
            Console.WriteLine(entry.Name);
            if (entry.Name.EndsWith(".bgyml") && (entry.Name.Contains("Model") || entry.Name.Contains("Animation") || entry.Name.Contains("Actor/") || entry.Name.Contains("Cloth")))
                Console.WriteLine(Byml.FromBinary(pack.Read(entry).ToArray()).ToYaml());
        }
    }
}
