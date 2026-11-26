using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using SkiaSharp;
using System.Numerics;

internal sealed class IconZeldaChecks : Application
{
    internal static string Root = "", Dump = "", Botw = "", Output = "";
    internal static int Result = 1;
    internal static void Inspect(string root, string dump)
    {
        root = IconCaptureAsset.Root(root);
        var resources = new BotwActorResources(Path.Combine(root, "Pack/Actor/Player.pack.zs"), [root,dump]);
        resources.PrepareTotkStrings();
        foreach (var reference in resources.Models)
        {
            Console.WriteLine("RESOURCE " + reference.Resource);
            var document = new ToolboxBfresDocument(resources.ReadModel(reference.Resource)!);
            foreach (dynamic model in document.Models)
            foreach (dynamic material in ToolboxBfresDocument.Items((object)model.Materials))
            {
                Console.WriteLine("MATERIAL " + material.Name);
                foreach (dynamic texture in ToolboxBfresDocument.Items((object)material.TextureRefs))
                    Console.WriteLine("TEXTURE " + (texture is string text ? text : (string)texture.Name));
            }
        }
    }
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new Window { Width = 1150, Height = 850, ShowInTaskbar = false };
        Directory.CreateDirectory(Output);
        string folders = Path.Combine(Output,"actor-folders.json");
        File.WriteAllText(folders, System.Text.Json.JsonSerializer.Serialize(new ActorBrowserPaths { Totk=Dump, Botw=Botw }));
        var view = new IconToolView(window, actorFoldersPath: folders); window.Content = view; lifetime.MainWindow = window;
        window.Opened += async (_,_) =>
        {
            try
            {
                var profile = IconCharacterProfile.LoadAll().Single(p => p.Id == "Zelda");
                await view.SelectCharacterAsync(profile);
                var actors = IconCaptureAsset.Catalog(Root);
                foreach (var name in new[] { "Armor_001_Head", "Armor_001_Upper", "Armor_005_Upper", "Armor_009_Upper", "Armor_005_Lower" })
                {
                    var actor = actors.Single(a => a.Name == name);
                    await view.LoadPathAsync(actor.Path,actor.ResourceRoot);
                    if (view.Asset?.Name != name) throw new Exception(string.Join(" | ", view.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text)));
                    Check(view.Asset.PoseName == "Icon_Pose", "Embedded character pose was ignored");
                    Check(!view.Asset.ArmorPose!.UsesRecoveredIconPose, "Link pose overrides Zelda pose");
                    if(name.EndsWith("Upper")) await Task.Run(() => CheckAuthoredPose(view.Asset, profile));
                    var props=view.CaptureModel!.Meshes.Where(m=>IconArmorProps.IsProp(m.Name)).ToArray();
                    Check(props.Length == (name.EndsWith("Head") ? 9 : name.EndsWith("Upper") ? 6 : 1), "Wrong Zelda props");
                    if(name.EndsWith("Head"))
                        foreach(var suffix in new[]{"A","B","C"})
                        {
                            int index=view.CaptureModel.Meshes.FindIndex(m=>m.Name==IconArmorProps.Prefix+"Upper_Skin_RaulSkin_"+suffix);
                            Check(index>=0 && !view.Viewport.HiddenMeshes.Contains(index),"Head must include visible RaulSkin " + suffix);
                        }
                    Check(props.All(m=>m.Texture!=null && m.Vertices.All(float.IsFinite)), "Invalid Zelda props");
                    if (name.EndsWith("Head")) Check(props.Single(m=>m.Name.EndsWith("Head_Default")).Texture!.Name=="Armor_Default_Shirt","Default shirt lost its texture");
                    if (name.EndsWith("Lower")) Check(props.Single().NormalTexture?.Name=="Z_Skin_NRM","Wrong Zelda skin atlas");
                    foreach (var prop in props) Console.WriteLine(prop.Name + " albedo=" + prop.Texture?.Name + " normal=" + prop.NormalTexture?.Name);
                    var png=await view.CapturePngAsync(512);
                    using var bitmap=SKBitmap.Decode(png);
                    Check(bitmap.Pixels.Count(p=>p.Alpha>32)>1000,"Blank capture");
                    File.WriteAllBytes(Path.Combine(Output,name+".png"),png);
                    var saved=view.SavePresetBytes("Zelda " + name);
                    await view.SelectCharacterAsync(IconCharacterProfile.Link);
                    await view.LoadPresetBytesAsync(saved);
                    Check(view.Asset!.CharacterId=="Zelda","Preset lost Zelda");
                    if(view.Asset.WithDye!=null) Check(view.Asset.WithDye(view.Asset.Dyes!.Choices.Last()).CharacterId=="Zelda","Dye lost Zelda");
                }
                Console.WriteLine("PASS Zelda head/upper/lower, textures, capture and preset round trips"); Result=0;
            }
            catch(Exception ex) { Console.Error.WriteLine(ex); }
            finally { window.Close(); lifetime.Shutdown(Result); }
        };
        base.OnFrameworkInitializationCompleted();
    }
    private static void CheckAuthoredPose(IconCaptureAsset asset, IconCharacterProfile profile)
    {
        var document = new ToolboxBfresDocument(File.ReadAllBytes(profile.ModelPath));
        var source = new ToolboxActorModel(document, (string)document.Models[0].Name, new());
        var animation = new ToolboxActorAnimation((object)document.SkeletalAnimations.Single(a => (string)a.Name == "Icon_Pose"), profile.ModelPath);
        var expected = source.Pose(animation, 0);
        var actual = asset.ArmorPose!.Apply(new Dictionary<string, Vector3>(), recoveredPose:true);
        foreach(var name in new[]{"Icon_Upper_Neck","Icon_Upper_Fingers_L","Icon_Upper_Fingers_R"})
        {
            if(name=="Icon_Upper_Neck" && asset.ArmorPose.NeckSplice!=null) continue;
            var mesh = actual.Meshes.Single(m => m.Name == IconArmorProps.Prefix + name);
            var reference = expected.Meshes.Single(m => m.Name.StartsWith(name + "__"));
            Check(mesh.Vertices.Length == reference.Vertices.Length, "Character geometry changed");
            for(int i=0; i<mesh.Vertices.Length; i+=8)
                for(int j=0;j<3;j++) Check(MathF.Abs(mesh.Vertices[i+j]-reference.Vertices[i+j]) < .00001f,"Prop does not follow its authored pose: " + name);
        }
        if(asset.ArmorPose.NeckSplice!=null)
        {
            Check(!actual.Meshes.Any(m=>m.Name==profile.NeckSplices[asset.Name]),"Splice duplicates the original skin");
            var neck=actual.Meshes.Single(m=>m.Name==IconArmorProps.Prefix+"Icon_Upper_Neck");
            Check(neck.Vertices.All(float.IsFinite),"Nonfinite neck cap");
            Check(neck.Vertices.Length > expected.Meshes.Single(m=>m.Name.StartsWith("Icon_Upper_Neck__")).Vertices.Length,"Missing chest section");
            var resources=new BotwActorResources(Path.Combine(IconCaptureAsset.Root(Root),"Pack/Actor/"+asset.Name+".pack.zs"),[IconCaptureAsset.Root(Root),Dump]);
            resources.PrepareTotkStrings();
            var reference=resources.Models.First();
            var garment=new ToolboxActorModel(new ToolboxBfresDocument(resources.ReadModel(reference.Resource)!),reference.Model,new());
            var native=garment.Pose(animation,0).Meshes.Single(m=>m.Name==profile.NeckSplices[asset.Name]);
            Check(neck.Indices.Take(native.Indices.Length).SequenceEqual(native.Indices),"Splice changed the native neckline topology");
            for(int i=0;i<native.Vertices.Length;i+=8)
                for(int j=0;j<3;j++) Check(MathF.Abs(neck.Vertices[i+j]-native.Vertices[i+j])<.00001f,"Splice moved the native collar skin");
            int count=native.Vertices.Length/8, rim=neck.Vertices.Length/8-count-1;
            Check(rim>=3 && neck.Indices.Length-native.Indices.Length==rim*3,"Neck cap is incomplete");
            for(int i=count;i<count+rim;i++)
            {
                var p=new Vector3(neck.Vertices[i*8],neck.Vertices[i*8+1],neck.Vertices[i*8+2]);
                Check(Enumerable.Range(0,count).Any(j=>Vector3.DistanceSquared(p,new(native.Vertices[j*8],native.Vertices[j*8+1],native.Vertices[j*8+2]))<1e-10f),"Neck cap is not joined to the skinned rim");
            }
        }
        if(asset.WithDye != null)
        {
            var dyed = asset.WithDye(asset.Dyes!.Choices.Last()).ArmorPose!.Apply(new Dictionary<string, Vector3>());
            foreach(var mesh in actual.Meshes.Where(m=>IconArmorProps.IsProp(m.Name)))
                Check(mesh.Vertices.SequenceEqual(dyed.Meshes.Single(m=>m.Name==mesh.Name).Vertices), "Dye changed the character pose");
        }
        var wrist = actual.Bones.Single(b=>b.Name=="Wrist_L");
        var overrides = new Dictionary<string, VisualBoneOverride> { [wrist.Name] = VisualBoneTransform.Override(wrist.Name,wrist.WorldMatrix * Matrix4x4.CreateTranslation(.01f,0,0)) };
        var settled = asset.ArmorPose.Apply(new Dictionary<string,Vector3>(), finalOverrides:overrides);
        Check(settled.Bones.Single(b=>b.Name==wrist.Name).WorldMatrix == overrides[wrist.Name].WorldMatrix,"Simulation override was ignored");
        Console.WriteLine("PASS authored neck/finger pose, dye retention and simulation override: " + asset.Name);
    }
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
}
