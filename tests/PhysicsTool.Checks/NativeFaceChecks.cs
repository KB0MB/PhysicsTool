using HKCLTool;
using PhysicsTool.Core;

internal static class NativeFaceChecks
{
    internal static void Inspect(string botw, string totk, string riju)
    {
        foreach(var item in new[]{("BotW Link",botw,false),("TotK Link",totk,true),("Riju",riju,true)})
        {
            string root=IconCaptureAsset.Root(item.Item2);
            var armor=new BotwActorResources(Path.Combine(root,item.Item3?"Pack/Actor/Armor_001_Head.pack.zs":"Actor/Pack/Armor_001_Head.sbactorpack"),[root,IconCaptureAsset.Root(totk)]);
            var source=new PlayerEquipmentSources(armor).Find(item.Item3?"Player":"GameROMPlayer")!.Open(armor.Roots);
            source.PrepareTotkStrings();
            var native = new Dictionary<string, VisualModelMesh>();
            foreach(var reference in source.Models)
            {
                var doc=new ToolboxBfresDocument(source.ReadModel(reference.Resource)!);
                var model=new ToolboxActorModel(doc,reference.Model,new());
                foreach(var face in model.RestPose.Meshes.Where(m=>m.Name.EndsWith("__Mt_Face")||m.Name.EndsWith("__Mt_Head")))
                {
                    Dump(item.Item1+" native "+reference.Model,face);
                    native.TryAdd(face.Name.Split("__").Last(), face);
                }
            }
            var profile=item.Item1=="Riju"?IconCharacterProfile.LoadAll().Single(p=>p.Id=="Riju"):IconCharacterProfile.Link;
            var propDoc=new ToolboxBfresDocument(File.ReadAllBytes(profile.ModelPath));
            var props=new ToolboxActorModel(propDoc,(string)propDoc.Models[0].Name,new());
            foreach(var face in props.RestPose.Meshes.Where(m=>m.Name.StartsWith("Icon_Head_Face__")||m.Name.StartsWith("Icon_Head_Skull__"))) Dump(item.Item1+" prop",face);
            var loaded = IconArmorProps.Load(armor, CancellationToken.None, character: profile)!;
            foreach (var binding in new[] { ("Icon_Head_Face", "Mt_Face"), ("Icon_Head_Skull", "Mt_Head") })
            {
                var original = props.RestPose.Meshes.Single(m => m.Name.StartsWith(binding.Item1 + "__"));
                var bound = loaded.RestPose.Meshes.Single(m => m.Name.StartsWith(binding.Item1 + "__"));
                var donor = native[binding.Item2];
                Check((bound.Material.Totk != null) == item.Item3, "Wrong native shader family");
                var actualOptions = Options(bound);
                foreach (var option in Options(donor).Where(p => p.Key.Contains("modify")))
                    Check(actualOptions.GetValueOrDefault(option.Key) == option.Value, "Wrong native option: " + option.Key);
                var actualParameters = bound.Material.Totk?.Parameters ?? bound.Material.Botw!.Parameters;
                foreach (var parameter in (donor.Material.Totk?.Parameters ?? donor.Material.Botw!.Parameters).Where(p => p.Key.Contains("const_")))
                    Check(actualParameters[parameter.Key].SequenceEqual(parameter.Value), "Wrong native constant: " + parameter.Key);
                Check(Colors(original).SequenceEqual(Colors(bound)), "Native donor replaced mannequin vertex mask");
                Check(original.Vertices.SequenceEqual(bound.Vertices) && original.Indices.SequenceEqual(bound.Indices), "Native donor changed visible geometry");
                Check(bound.Texture?.Name == IconMannequinMaterial.TextureName, "Native donor replaced mannequin albedo");
                var offsets = ToolboxShadowGeometry.Offsets(bound);
                if (binding.Item2 == "Mt_Face")
                {
                    Check(offsets.Length == Colors(bound).Length, "Missing native shadow offsets");
                    for (int i = 0; i < offsets.Length; i++)
                        Check(System.Numerics.Vector3.Distance(offsets[i], new(0, 0, -.025f * (1 - Colors(bound)[i].X))) < .000001f,
                            "Face does not use its own multiply-only shadow formula");
                    if (item.Item1 == "Riju") Check(offsets.All(v => v == System.Numerics.Vector3.Zero), "Riju acquired another character's displacement");
                    else Check(offsets.Any(v => v.Length() > .02f), "Link lost his painted face displacement");
                }
                Console.WriteLine("PASS native binding, constants, geometry and mask: " + item.Item1 + " " + binding.Item1);
            }
        }
    }
    private static Dictionary<string, long> Options(VisualModelMesh mesh) => mesh.Material.Totk?.Options.ToDictionary(p => p.Key, p => p.Value)
        ?? mesh.Material.Botw!.Options.ToDictionary(p => p.Key, p => (long)p.Value);
    private static System.Numerics.Vector4[] Colors(VisualModelMesh mesh) => mesh.Material.Totk?.VertexColors ?? mesh.Material.Botw!.VertexColors;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Dump(string label,VisualModelMesh mesh)
    {
        Console.WriteLine(label+" "+mesh.Name);
        var options=mesh.Material.Totk?.Options.Select(p=>(p.Key,p.Value))??mesh.Material.Botw!.Options.Select(p=>(p.Key,(long)p.Value));
        foreach(var p in options.Where(p=>p.Key.Contains("modify"))) Console.WriteLine(p.Key+"="+p.Item2);
        foreach(var p in (mesh.Material.Totk?.Parameters??mesh.Material.Botw!.Parameters).Where(p=>p.Key.Contains("const_value2")||p.Key.Contains("const_value3")||p.Key.Contains("const_vector"))) Console.WriteLine(p.Key+"="+string.Join(',',p.Value));
        var colors=mesh.Material.Totk?.VertexColors??mesh.Material.Botw!.VertexColors;
        Console.WriteLine("mask count="+colors.Length+" min="+colors.Min(v=>v.X)+" max="+colors.Max(v=>v.X));
    }
}
