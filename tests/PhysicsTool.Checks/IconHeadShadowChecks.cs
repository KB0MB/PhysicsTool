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

internal sealed class IconHeadShadowChecks : Application
{
    internal static string Root="", Dump="", Output="";
    internal static int Result=1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime=(IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window=new Window { Width=1150, Height=850, ShowInTaskbar=false };
        Directory.CreateDirectory(Output);
        string folders=Path.Combine(Output,"folders.json");
        File.WriteAllText(folders,System.Text.Json.JsonSerializer.Serialize(new ActorBrowserPaths { Totk=Dump }));
        var view=new IconToolView(window,actorFoldersPath:folders); window.Content=view; lifetime.MainWindow=window;
        window.Opened+=async (_,_)=>
        {
            try
            {
                CheckOffsetMath();
                var profile=IconCharacterProfile.LoadAll().Single(p=>p.Id=="Zelda");
                await view.SelectCharacterAsync(profile);
                var actor=IconCaptureAsset.Catalog(Root).Single(a=>a.Name=="Armor_001_Head");
                await view.LoadPathAsync(actor.Path,actor.ResourceRoot);
                Check(view.Asset?.Name==actor.Name,"Could not load Zelda head");
                var model=view.CaptureModel!;
                var face=model.Meshes.Single(m=>m.Name==IconArmorProps.Prefix+"Icon_Head_Face");
                Check(face.Material.Totk?.Native==true,"Face still uses the placeholder shader");
                Check(face.Texture?.Name==IconMannequinMaterial.TextureName,"Native material lost charcoal albedo");
                Check(face.ShadowOffsets.Length==face.Vertices.Length/8 && face.ShadowOffsets.Any(o=>o.Length()>.012f),"Missing authored shadow offsets");
                foreach(var eye in model.Meshes.Where(m=>IconMannequinEyes.IsMesh(m.Name)))
                    Check(!ReferenceEquals(eye.Material.Totk,face.Material.Totk),"Eye settings mutate the face material");
                var vertices=face.Vertices.ToArray(); var offsets=face.ShadowOffsets.ToArray();
                var rotated=IconCaptureAsset.Rotate(model,new(0,90,0));
                var rotatedFace=rotated.Meshes.Single(m=>m.Name==face.Name);
                for(int i=0;i<offsets.Length;i++)
                    Check(Vector3.Distance(rotatedFace.ShadowOffsets[i],Vector3.TransformNormal(offsets[i],Matrix4x4.CreateRotationY(MathF.PI/2)))<1e-6f,"Actor rotation lost shadow offsets");
                var rebound=view.Asset!.ArmorPose!.RebindMaterials(model).Meshes.Single(m=>m.Name==face.Name);
                Check(rebound.ShadowOffsets.SequenceEqual(offsets),"Dye rebind lost shadow geometry");
                view.SetPoseAdjustment("Head",new(8,12,0));
                Check(!view.CaptureModel!.Meshes.Single(m=>m.Name==face.Name).ShadowOffsets.SequenceEqual(offsets),"Shadow offsets did not follow head skinning");
                view.SetPoseAdjustment("Head",default);
                var shadows=view.GetVisualDescendants().OfType<CheckBox>().Single(c=>c.Name=="IconCastShadows");
                shadows.IsChecked=true;
                var corrected=await view.CapturePngAsync(1024);
                File.WriteAllBytes(Path.Combine(Output,"shadow.png"),corrected);
                var original=view.Viewport.Model!;
                var unmodified=IconCaptureAsset.Rotate(original,Vector3.Zero);
                foreach(var mesh in unmodified.Meshes) Array.Clear(mesh.ShadowOffsets);
                view.Viewport.SetModelPose(unmodified);
                var old=await view.CapturePngAsync(1024);
                File.WriteAllBytes(Path.Combine(Output,"unadjusted-caster.png"),old);
                view.Viewport.SetModelPose(original);
                using(var a=SKBitmap.Decode(corrected))
                using(var b=SKBitmap.Decode(old))
                {
                    int lighter=0;
                    for(int y=350;y<530;y++) for(int x=360;x<550;x++)
                        if(a.GetPixel(x,y).Red>b.GetPixel(x,y).Red+15) lighter++;
                    Check(lighter>100,"Native caster adjustment did not remove facial shadow patches: "+lighter);
                    Console.WriteLine("Corrected facial shadow pixels: "+lighter);
                }
                shadows.IsChecked=false;
                File.WriteAllBytes(Path.Combine(Output,"no-shadow.png"),await view.CapturePngAsync(1024));
                Check(face.Vertices.SequenceEqual(vertices),"Shadow pass changed visible geometry");
                shadows.IsChecked=true;
                var repeated=await view.CapturePngAsync(1024);
                Check(repeated.SequenceEqual(corrected),"Shadow/capture toggle leaked caster geometry into the visible pass");
                var saved=view.SavePresetBytes("Unrelated capture name");
                var next=IconCaptureAsset.Catalog(Root).Single(a=>a.Name=="Armor_005_Head");
                await view.LoadPathAsync(next.Path,next.ResourceRoot,preserveCapture:true);
                var expected=await Task.Run(()=>new BotwActorResources(next.Path,[next.ResourceRoot,Dump]).ArmorEarRotationDegrees);
                Check(expected!=new Vector3(0,50,0),"Ear regression needs two different native offsets");
                await view.LoadPresetBytesAsync(saved);
                Check(view.Asset?.ArmorPose?.EarRotationDegrees==expected,"Capture preset overwrote the actor's ear rotation");
                Console.WriteLine("PASS native shadow offsets, skinned/rotated/dye geometry, isolated materials, capture state and actor-driven ears"); Result=0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);}
            finally{window.Close();lifetime.Shutdown(Result);}
        };
        base.OnFrameworkInitializationCompleted();
    }
    private static void CheckOffsetMath()
    {
        var material=new TotkPreviewMaterial { Native=true, VertexColors=[Vector4.Zero,new(.5f),Vector4.One] };
        foreach(var p in new Dictionary<string,long> {
            ["o_modify_position_type"]=1,["o_vtx_position_modify_shadow_only"]=1,["o_vtx_position_modify_calc_type"]=8,
            ["o_vtx_position_modify_vector"]=114,["o_vtx_position_modify_A"]=300,["o_vtx_position_modify_A_channel"]=1,
            ["o_vtx_position_modify_B"]=106,["o_vtx_position_modify_C"]=107,["o_vtx_position_modify_C_channel"]=1 }) material.Options[p.Key]=p.Value;
        material.Parameters["p_const_value2"]=[-.025f]; material.Parameters["p_const_value3"]=[1.013f]; material.Parameters["p_const_vector0"]=[0,0,1,0];
        var mesh=new VisualModelMesh { Vertices=new float[24], Material=new() { Totk=material } };
        var offsets=ToolboxShadowGeometry.Offsets(mesh);
        for(int i=0;i<3;i++) Check(Vector3.Distance(offsets[i],new(0,0,-.025f*(1-i*.5f)-.013f))<1e-6f,"Incorrect native multiply-add/channel evaluation");
        material.Options["o_vtx_position_modify_shadow_only"]=0;
        Check(ToolboxShadowGeometry.Offsets(mesh).Length==0,"Visible displacement was applied to the caster twice");
        var botw=new BotwPreviewMaterial { VertexColors=material.VertexColors };
        foreach(var p in material.Options) botw.Options["uking_"+p.Key[2..]]=(int)p.Value;
        botw.Options["uking_modify_position_type"]=2; botw.Options["uking_vtx_position_modify_shadow_only"]=1; botw.Options["uking_vtx_position_modify_calc_type"]=2;
        botw.Parameters["const_value2"]=[.025f]; botw.Parameters["const_vector0"]=[0,0,-1,0];
        mesh.Material.Totk=null; mesh.Material.Botw=botw;
        offsets=ToolboxShadowGeometry.Offsets(mesh);
        for(int i=0;i<3;i++) Check(Vector3.Distance(offsets[i],new(0,0,-.025f*(1-i*.5f)))<1e-6f,"Incorrect BotW shadow mask");
    }
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
}
