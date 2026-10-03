using System.Drawing;
using System.Numerics;
using System.Reflection;
using BymlLibrary;
using Revrs;

namespace HKCLTool;

internal static class TotkPolytopeChecks
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            var mesh=Box("Quoted 'box'",new(2,4,6),Vector3.Zero);
            var doc=TotkPolytopeBuilder.Create([mesh],1,Vector3.Zero,false);
            Require(doc.Meshes.Count==1 && doc.Meshes[0].Vertices.Count==8,"Box hull");
            var props=doc.Root.GetMap()["AutoCalc"].GetMap();
            Near(props["Volume"].GetFloat(),48,"Box volume");
            Near(props["Tensor"].GetMap()["X"].GetFloat(),52f/12,"Box inertia");
            var scaled=TotkPolytopeBuilder.Create([mesh],2,new(7,8,9),false);
            Near(scaled.Root.GetMap()["AutoCalc"].GetMap()["Volume"].GetFloat(),384,"Cubic scaling");
            Near(scaled.Root.GetMap()["AutoCalc"].GetMap()["Tensor"].GetMap()["X"].GetFloat(),52f/3,"Quadratic inertia scaling");
            var box2=Box("Second",new(2,4,6),new(10,0,0));
            var merged=new BotwCollisionMesh { Name="Disconnected", Vertices=mesh.Vertices.Concat(box2.Vertices).ToArray(), Indices=mesh.Indices.Concat(box2.Indices.Select(i=>i+mesh.Vertices.Count)).ToArray() };
            var parts=TotkPolytopeBuilder.Create([merged],1,Vector3.Zero,false);
            Require(parts.Meshes.Count==2,"Disconnected parts are separate hulls");
            Near(parts.Root.GetMap()["AutoCalc"].GetMap()["Center"].GetMap()["X"].GetFloat(),5,"Compound center");
            Near(parts.Root.GetMap()["AutoCalc"].GetMap()["Tensor"].GetMap()["Y"].GetFloat(),40f/12+25,"Parallel-axis inertia");
            var duplicate=TotkPolytopeBuilder.Create([mesh,mesh],1,Vector3.Zero,false);
            Require(duplicate.Meshes.Select(m=>m.Name).Distinct().Count()==2,"Unique names");
            var zUp=TotkPolytopeBuilder.Create([Box("Axis",new(2,4,6),new(1,2,3))],1,Vector3.Zero,true);
            var center=zUp.Root.GetMap()["AutoCalc"].GetMap()["Center"].GetMap();
            Near(center["X"].GetFloat(),1,"X preserved"); Near(center["Y"].GetFloat(),3,"Z to Y"); Near(center["Z"].GetFloat(),-2,"Y to negative Z");
            foreach (var extension in new[]{"bgyml","yaml"})
            {
                var path=Path.Combine(output,"test.phive__ShapeParam."+extension);
                TotkPolytopeBuilder.Write(duplicate,path);
                var reloaded=TotkPolytopeImporter.Import(path);
                Require(reloaded.Count==2 && reloaded[0].Name==mesh.Name,"Round trip " + extension);
                Require(reloaded[0].Vertices.ToHashSet().SetEquals(doc.Meshes[0].Vertices),"Coordinates round trip " + extension);
            }
            Byml.FromBinary(File.ReadAllBytes(Path.Combine(output,"test.phive__ShapeParam.bgyml")),out var endian,out var version);
            Require(endian==Endianness.Little && version==7,"Native BYML header");
            Reject(()=>TotkPolytopeBuilder.Create([mesh],float.NaN,Vector3.Zero,false),"NaN");
            Reject(()=>TotkPolytopeBuilder.Create([mesh],0,Vector3.Zero,false),"Zero scale");
            Reject(()=>TotkPolytopeBuilder.Create([],1,Vector3.Zero,false),"Empty selection");
            Reject(()=>TotkPolytopeBuilder.Hull("Flat",[Vector3.Zero,Vector3.UnitX,Vector3.UnitY,new(1,1,0)],"Material_Wood"),"Planar hull");
            var excluded=Box("Excluded",Vector3.One,Vector3.Zero); excluded.Included=false;
            Reject(()=>TotkPolytopeBuilder.Create([excluded],1,Vector3.Zero,false),"Excluded selection");
            var bad= new BotwCollisionMesh { Name="Invalid", Vertices=mesh.Vertices, Indices=[0,1,999] };
            Reject(()=>TotkPolytopeBuilder.Create([bad],1,Vector3.Zero,false),"Bad index");
            var parent=Byml.FromBinary(doc.Root.ToBinary(Endianness.Little,7)); parent.GetMap()["$parent"]=new Byml("Parent.gyml");
            Reject(()=>TotkPolytopeImporter.Read(parent),"Unresolved parent");
            var mixed=Byml.FromBinary(doc.Root.ToBinary(Endianness.Little,7)); mixed.GetMap()["Sphere"]=new Byml(new List<Byml>{TotkPolytopeBuilder.Map(("Radius",new Byml(1f)))});
            Reject(()=>TotkPolytopeImporter.Read(mixed),"Mixed shapes");
            var transformed=Byml.FromBinary(doc.Root.ToBinary(Endianness.Little,7));
            var shape=transformed.GetMap()["Polytope"].GetArray()[0].GetMap();
            shape["OffsetTranslation"]=TotkPolytopeBuilder.Vec(new(8,9,10));
            shape["OffsetRotation"]=TotkPolytopeBuilder.Vec(new(0,0,90));
            shape["OffsetScale"]=TotkPolytopeBuilder.Vec(new(2,1,1));
            var transformedMesh=TotkPolytopeImporter.Read(transformed)[0];
            var min=transformedMesh.Vertices.Aggregate(Vector3.Min); var max=transformedMesh.Vertices.Aggregate(Vector3.Max);
            Near(min.X,6,"Imported rotation and translation"); Near(max.Y,11,"Imported scale");
            foreach (var extension in new[]{"dae","fbx"})
            {
                var modelPath=Path.Combine(output,"hull."+extension);
                BotwCollisionExporter.Export(doc.Meshes,modelPath);
                var imported=BotwCollisionMeshImporter.Import(modelPath);
                var regenerated=TotkPolytopeBuilder.Create(imported,1,Vector3.Zero,false);
                Near(regenerated.Root.GetMap()["AutoCalc"].GetMap()["Volume"].GetFloat(),48,"Model import/export "+extension);
            }
            ApplicationConfiguration.Initialize();
            AppLocalization.Initialize();
            var language=AppLocalization.CurrentLanguage;
            foreach (var code in new[]{"en","fr","es","ja"})
            {
                typeof(AppLocalization).GetProperty(nameof(AppLocalization.CurrentLanguage))!.SetValue(null,code);
                using var dialog=new BotwCollisionDialog();
                var target=(ComboBox)typeof(BotwCollisionDialog).GetField("_target",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(dialog)!;
                var path=(TextBox)typeof(BotwCollisionDialog).GetField("_outputPath",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(dialog)!;
                path.Text="example.hksc";
                target.SelectedIndex=2; target.SelectedIndex=1; target.SelectedIndex=2;
                Require(path.Text=="example.phive__ShapeParam.bgyml","Stable output suffix");
                typeof(BotwCollisionDialog).GetField("_meshes",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(dialog,doc.Meshes);
                typeof(BotwCollisionDialog).GetMethod("PopulateMeshes",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialog,null);
                typeof(BotwCollisionDialog).GetMethod("SetBusy",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialog,new object?[]{false,null});
                dialog.Show(); Application.DoEvents();
                using var bitmap=new Bitmap(dialog.Width,dialog.Height);
                dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                bitmap.Save(Path.Combine(output,"dialog-"+code+".png"));
                dialog.Close();
            }
            typeof(AppLocalization).GetProperty(nameof(AppLocalization.CurrentLanguage))!.SetValue(null,language);
            using (var preview=new BotwCollisionPreviewForm(BotwCollisionVisualModel.Create(doc.Meshes,"test")))
            {
                preview.Show(); Application.DoEvents(); preview.Refresh();
                var view=(ParticlePreviewControl)typeof(BotwCollisionPreviewForm).GetField("_preview",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(preview)!;
                view.MakeCurrent(); view.Refresh(); OpenTK.Graphics.OpenGL4.GL.Finish();
                var pixels=new byte[view.Width*view.Height*4];
                OpenTK.Graphics.OpenGL4.GL.ReadBuffer(OpenTK.Graphics.OpenGL4.ReadBufferMode.Front);
                OpenTK.Graphics.OpenGL4.GL.ReadPixels(0,0,view.Width,view.Height,OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte,pixels);
                using var image=new Bitmap(view.Width,view.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var bits=image.LockBits(new Rectangle(Point.Empty,image.Size),System.Drawing.Imaging.ImageLockMode.WriteOnly,image.PixelFormat);
                System.Runtime.InteropServices.Marshal.Copy(pixels,0,bits.Scan0,pixels.Length);
                image.UnlockBits(bits); image.RotateFlip(RotateFlipType.RotateNoneFlipY); image.Save(Path.Combine(output,"hull-preview.png"));
                Require(Enumerable.Range(0,pixels.Length/4).Count(i=>pixels[i*4+2]>pixels[i*4]+15 && pixels[i*4+2]>50)>5000,"Visible wood-colored hull preview");
                preview.Close();
            }
            File.WriteAllText(Path.Combine(output,"result.txt"),"PASS: hulls, disconnected parts, names, compound inertia, scale/axis transforms, BYML v7/YAML and DAE/FBX round trips, malformed input rejection, four-language dialog layouts, and OpenGL hull preview.");
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(output,"result.txt"),ex.ToString());
            Environment.ExitCode=1;
        }
    }

    private static BotwCollisionMesh Box(string name,Vector3 size,Vector3 offset)
    {
        var vertices=new List<Vector3>();
        for(var z=-1;z<=1;z+=2) for(var y=-1;y<=1;y+=2) for(var x=-1;x<=1;x+=2) vertices.Add(new Vector3(x,y,z)*size/2+offset);
        return new() {Name=name,Vertices=vertices,Indices=[0,2,3,0,3,1,4,5,7,4,7,6,0,1,5,0,5,4,2,6,7,2,7,3,0,4,6,0,6,2,1,3,7,1,7,5]};
    }
    private static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    private static void Near(float actual,float expected,string name) => Require(Math.Abs(actual-expected)<0.001*Math.Max(1,Math.Abs(expected)),name+$": {actual} != {expected}");
    private static void Reject(Action action,string name) { try { action(); } catch(InvalidDataException) { return; } throw new InvalidOperationException("Expected rejection: "+name); }
}
