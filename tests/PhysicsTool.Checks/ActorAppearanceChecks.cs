using HKCLTool;
using System.Security.Cryptography;

internal static class ActorAppearanceChecks
{
    internal static void Run(string[] packs)
    {
        var frames = new float[] { 0, 1, 2, 3 };
        var keys = new float[,] { { -7 }, { -4 }, { -8 }, { 9 } };
        Require(ToolboxActorAppearance.Sample(frames, keys, 8, 0) == 1, "first step");
        Require(ToolboxActorAppearance.Sample(frames, keys, 8, 1.9) == 4, "hold step and integer offset");
        Require(ToolboxActorAppearance.Sample(frames, keys, 8, 30) == 17, "hold final step");
        Require(ToolboxActorAppearance.Sample(frames, keys, 8, -1) == 1, "clamp before first step");
        foreach (var path in packs) Check(path);
        Console.WriteLine($"PASS: actor texture-pattern sampling and {packs.Length} native actor variants.");
    }

    private static void Check(string path)
    {
        var originalHash = SHA256.HashData(File.ReadAllBytes(path));
        var resources = new BotwActorResources(path);
        var modelRef = resources.Models.First();
        var source = resources.ReadModel(modelRef.Resource)!;
        var sourceHash = SHA256.HashData(source);
        var document = new ToolboxBfresDocument(source);
        var textures = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
        document.ReadTextures(textures, resources.Warnings);
        new ToolboxBfresDocument(resources.ReadModel(modelRef.Resource, document.IsSwitch ? ".Tex" : ".Tex1")!).ReadTextures(textures, resources.Warnings);
        var bindings = ToolboxActorAppearance.Read(document, resources.VariationMatAnim, resources.VariationMatAnimFrame, resources.Warnings);
        Require(bindings.Count > 0, "native texture variation exists");
        var model = new ToolboxActorModel(document, modelRef.Model, textures, bindings, resources.Warnings);
        var plain = new ToolboxActorModel(document, modelRef.Model, textures);
        var color = Path.GetFileNameWithoutExtension(path) switch
        {
            "Enemy_Bokoblin_Junior" => "Red", "Enemy_Bokoblin_Middle" => "Blue",
            "Enemy_Bokoblin_Senior" => "Black", "Enemy_Bokoblin_Dark" => "Curse",
            _ => throw new Exception("Unknown native appearance fixture: " + path)
        };
        var expectedBody = $"Bokoblin_{color}_Body_Alb";
        var body = model.RestPose.Meshes.First(m => m.Name.EndsWith("__Mt_Skin"));
        Require(body.Texture?.Name == expectedBody, "body texture " + expectedBody + ", actual " + body.Texture?.Name + "; bindings: " + string.Join(", ", bindings) + "; warnings: " + string.Join("; ", resources.Warnings));
        Require(model.RestPose.Meshes.First(m => m.Name.EndsWith("__Mt_Eye")).Texture?.Name == $"Bokoblin_{color}_Eye_Alb", "eye texture");
        var necklace = model.RestPose.Meshes.First(m => m.Name.EndsWith("__Mt_Necklace"));
        Require(necklace.SpecularTexture?.Name == $"Bokoblin_{(color is "Black" or "Curse" ? "Black" : "Red")}_Necklace_Spm", "variant SPM and final key hold");
        Require(plain.RestPose.Meshes.First(m => m.Name.EndsWith("__Mt_Skin")).Texture?.Name == "Bokoblin_Red_Body_Alb", "shared BFRES bindings unmodified");
        Require(body.Vertices.SequenceEqual(plain.RestPose.Meshes.First(m => m.Name == body.Name).Vertices), "identical shared geometry");
        var missing = new Dictionary<string, VisualModelTexture>(textures);
        missing.Remove(expectedBody);
        var missingModel = new ToolboxActorModel(document, modelRef.Model, missing, bindings, new());
        Require(missingModel.RestPose.Meshes.First(m => m.Name == body.Name).Texture == null, "missing selected albedo stays empty");
        using var scene = BotwActorScene.Load(path);
        Require(scene.RestModel.Meshes.First(m => m.Name == body.Name).Texture?.Name == expectedBody, "actor scene appearance wiring");
        var animation = scene.Animations.FirstOrDefault();
        var pose = scene.Update(animation, 5, false, 0, true);
        Require(pose.Meshes.First(m => m.Name == body.Name).Texture?.Name == expectedBody, "appearance independent of skeletal frame");
        pose = scene.Update(animation, 6, true, 1f / 30f, false);
        Require(pose.Meshes.First(m => m.Name == body.Name).Texture?.Name == expectedBody, "appearance retained during simulation");
        Require(SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(originalHash), "actor source unchanged");
        Require(SHA256.HashData(resources.ReadModel(modelRef.Resource)!).SequenceEqual(sourceHash), "model source unchanged");
        Require(!resources.Warnings.Any(w => w.Contains("variation", StringComparison.OrdinalIgnoreCase)), string.Join("; ", resources.Warnings));
        CheckConstants(document, resources.VariationMatAnim);
        Console.WriteLine($"  {(resources.IsSwitch ? "Switch" : "Wii U")} {resources.ActorName}: {expectedBody}, {bindings.Count} sampler overrides");
    }

    private static void CheckConstants(ToolboxBfresDocument document, string name)
    {
        if (document.Resource is Syroot.NintenTools.NSW.Bfres.ResFile nx)
        {
            var material = nx.MaterialAnims.Single(a => a.Name == name + "_ftp").MaterialAnimDataList.Single(m => m.Name == "Mt_Skin");
            var info = material.TexturePatternAnimInfos[0];
            info.CurveIndex = ushort.MaxValue;
            material.Constants ??= [];
            info.BeginConstant = checked((ushort)material.Constants.Count);
            material.Constants.Add(new() { Value = 4 });
        }
        else if (document.Resource is Syroot.NintenTools.Bfres.ResFile wii)
        {
            var material = wii.TexPatternAnims.Values.Single(a => a.Name == name).TexPatternMatAnims.Single(m => m.Name == "Mt_Skin");
            material.PatternAnimInfos[0].CurveIndex = -1;
            material.BaseDataList[0] = 4;
        }
        var warnings = new List<string>();
        Require(ToolboxActorAppearance.Read(document, name, 3, warnings)[("Mt_Skin", "_a0")] == "Bokoblin_Blue_Body_Alb", "constant track fallback");
        Require(warnings.Count == 0, "valid constants produce no warnings");
        Require(ToolboxActorAppearance.Read(document, "", 0, warnings).Count == 0, "unconfigured variant stays default");
        Require(ToolboxActorAppearance.Read(document, "missing", 0, warnings).Count == 0, "unavailable variant stays default");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Actor appearance: " + message);
    }
}
