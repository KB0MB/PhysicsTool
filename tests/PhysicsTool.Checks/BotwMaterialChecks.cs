using HKCLTool;
using ResS = Syroot.NintenTools.NSW.Bfres;
using ResU = Syroot.NintenTools.Bfres;
using PhysicsTool.Desktop.Rendering;
using System.Numerics;

internal static class BotwMaterialChecks
{
    internal static void Run(string[] paths)
    {
        var mesh = new VisualModelMesh { Name = "UV fixture", Vertices = [0,0,0, 0,0,1, 0,0, 1,0,0, 0,0,1, 1,0, 0,1,0, 0,0,1, 0,1], Indices = [0,1,2],
            UvSets = [new() { Name = "_u0", Coordinates = [0,0, 1,0, 0,1] }, new() { Name = "_u1", Coordinates = [0,0, 0,1, 1,0] }] };
        var options = new Dictionary<string, string> { ["uking_texture0_texcoord"] = "1", ["uking_texture1_texcoord"] = "0", ["uking_texture2_texcoord"] = "1" };
        var warnings = new List<string>();
        ToolboxBotwMaterial.Apply("other_game", options, new Dictionary<string, string>(), mesh, warnings);
        Require(!mesh.Material.UseBotwPreviewShader, "unrelated games retain standard shader");
        ToolboxBotwMaterial.Apply("uking_mat", options, new Dictionary<string, string>(), mesh, warnings);
        Require(mesh.Material.UseBotwPreviewShader && mesh.Material.Clone().UseBotwPreviewShader, "BotW profile and cloning");
        Require(mesh.Material.AlbedoUvSet == "_u1" && mesh.Material.SpmUvSet == "_u0" && mesh.Material.NormalUvSet == "_u1", "per-texture UV channels");
        var vertices = ModelVertices.Build(mesh);
        Require(vertices[26] == 0 && vertices[27] == 1 && vertices[28] == 1 && vertices[29] == 0 && vertices[30] == 0 && vertices[31] == 1, "GPU vertex UV stream");
        Require(Vector3.Dot(new(vertices[16], vertices[17], vertices[18]), Vector3.UnitY) > .99f, "tangents use selected normal UVs");
        ToolboxBotwMaterial.Apply("uking_mat", options, new Dictionary<string, string> { ["_u1"] = "_u0" }, mesh, warnings);
        Require(mesh.Material.NormalUvSet == "_u0", "attribute assignment remapping");
        foreach (var invalid in new[] { "-1", "NaN", "Infinity", "0.5", "7", "bad", "2" })
        {
            options["uking_texture2_texcoord"] = invalid;
            ToolboxBotwMaterial.Apply("uking_mat", options, new Dictionary<string, string>(), mesh, warnings);
            Require(mesh.Material.NormalUvSet == "_u0", "unsupported UV preserves fallback");
        }
        Require(warnings.Count == 7, "unsupported UV warnings");
        foreach (var path in paths) Native(path);
        Console.WriteLine($"PASS: BotW shader routing, independent UVs, attribute remaps, normal tangents, invalid selectors, and {paths.Length} native models/actors.");
    }

    private static void Native(string path)
    {
        ToolboxBfresDocument[] documents;
        if (Path.GetExtension(path) is ".bfres" or ".sbfres") documents = [new(File.ReadAllBytes(path))];
        else
        {
            var resources = new BotwActorResources(path);
            documents = resources.Models.Select(m => m.Resource).Distinct().Select(n => new ToolboxBfresDocument(resources.ReadModel(n)!)).ToArray();
        }
        var checkedMaterials = 0;
        foreach (var document in documents)
        foreach (dynamic model in document.Models)
        {
            var imported = new ToolboxActorModel(document, (string)model.Name, new());
            foreach (object material in ToolboxBfresDocument.Items((object)model.Materials))
            {
                string name; string archive; Dictionary<string, string> options;
                if (material is ResS.Material nx)
                {
                    name = nx.Name; archive = nx.ShaderAssign.ShaderArchiveName;
                    options = Enumerable.Range(0, nx.ShaderAssign.ShaderOptions.Count).ToDictionary(i => nx.ShaderAssign.ShaderOptionDict.GetKey(i), i => nx.ShaderAssign.ShaderOptions[i]);
                }
                else
                {
                    var wii = (ResU.Material)material;
                    name = wii.Name; archive = wii.ShaderAssign.ShaderArchiveName;
                    options = wii.ShaderAssign.ShaderOptions.Keys.ToDictionary(k => k, k => wii.ShaderAssign.ShaderOptions[k].String);
                }
                if (!archive.StartsWith("uking_")) continue;
                foreach (var mesh in imported.RestPose.Meshes.Where(m => m.Name.EndsWith("__" + name)))
                {
                    Require(mesh.Material.UseBotwPreviewShader, "native BotW detection");
                    var uv = new[] { mesh.Material.AlbedoUvSet, mesh.Material.SpmUvSet, mesh.Material.NormalUvSet };
                    for (var slot = 0; slot < uv.Length; slot++)
                        if (options.TryGetValue($"uking_texture{slot}_texcoord", out var channel) && mesh.UvSets.Any(u => u.Name == "_u" + channel))
                            Require(uv[slot] == "_u" + channel, "native material UV choice");
                    Require(ModelVertices.Build(mesh).All(float.IsFinite), "finite native GPU vertex data");
                    checkedMaterials++;
                }
            }
        }
        Require(checkedMaterials > 0, "native BotW materials tested");
        Console.WriteLine($"  {Path.GetFileName(path)}: {checkedMaterials} material/mesh bindings verified");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("BotW material: " + message);
    }

    internal static void Survey(string path)
    {
        var resources = new BotwActorResources(path);
        resources.PrepareTotkStrings();
        foreach (var modelRef in resources.Models)
        {
            var doc = new ToolboxBfresDocument(resources.ReadModel(modelRef.Resource)!);
            var model = doc.Models.First(m => (string)m.Name == modelRef.Model);
            foreach (object material in ToolboxBfresDocument.Items((object)model.Materials))
            {
                if (material is ResS.Material nx)
                {
                    Console.WriteLine(nx.Name + " " + nx.ShaderAssign.ShaderArchiveName + " " + nx.ShaderAssign.ShadingModelName);
                    for (var i = 0; i < nx.ShaderAssign.ShaderOptions.Count; i++)
                        Console.WriteLine(nx.ShaderAssign.ShaderOptionDict.GetKey(i) + "=" + nx.ShaderAssign.ShaderOptions[i]);
                    for (var i = 0; i < nx.ShaderAssign.SamplerAssigns.Count; i++)
                        Console.WriteLine("sampler " + nx.ShaderAssign.SamplerAssignDict.GetKey(i) + "=" + nx.ShaderAssign.SamplerAssigns[i]);
                    for (var i = 0; i < nx.ShaderAssign.AttribAssigns.Count; i++)
                        Console.WriteLine("attrib " + nx.ShaderAssign.AttribAssignDict.GetKey(i) + "=" + nx.ShaderAssign.AttribAssigns[i]);
                    for (var i = 0; i < nx.TextureRefs.Count; i++) Console.WriteLine("texture " + nx.SamplerDict.GetKey(i) + "=" + nx.TextureRefs[i]);
                    foreach (var r in nx.RenderInfos) Console.WriteLine("render " + r.Name + "=" + (r.Type == ResS.RenderInfoType.String ? string.Join(",", r.GetValueStrings()) : r.Type == ResS.RenderInfoType.Single ? string.Join(",", r.GetValueSingles()) : string.Join(",", r.GetValueInt32s())));
                    foreach (var p in nx.ShaderParams)
                    {
                        var values = Enumerable.Range(0, (int)p.DataSize / 4).Select(i => BitConverter.ToSingle(nx.ShaderParamData, p.DataOffset + i * 4));
                        Console.WriteLine("param " + p.Name + " (" + p.Type + ")=" + string.Join(",", values));
                    }
                }
                else if (material is ResU.Material wii)
                {
                    Console.WriteLine(wii.Name + " " + wii.ShaderAssign.ShaderArchiveName + " " + wii.ShaderAssign.ShadingModelName);
                    foreach (var pair in wii.ShaderAssign.ShaderOptions) Console.WriteLine(pair.Key + "=" + pair.Value.String);
                    foreach (var pair in wii.ShaderAssign.SamplerAssigns) Console.WriteLine("sampler " + pair.Key + "=" + pair.Value.String);
                    foreach (var pair in wii.ShaderAssign.AttribAssigns) Console.WriteLine("attrib " + pair.Key + "=" + pair.Value.String);
                }
            }
        }
    }
}
