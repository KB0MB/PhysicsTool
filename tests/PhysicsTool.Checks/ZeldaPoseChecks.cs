using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;

internal static class ZeldaPoseChecks
{
    internal static void Study(string path, string root, string dump)
    {
        var document = new ToolboxBfresDocument(File.ReadAllBytes(path));
        var props = new ToolboxActorModel(document, (string)document.Models[0].Name, new());
        foreach (dynamic clip in document.SkeletalAnimations)
        {
            var animation = new ToolboxActorAnimation((object)clip, path);
            Console.WriteLine("ANIMATION " + animation.Name + " frames=" + animation.FrameCount + " bones=" + string.Join(",", animation.BoneNames));
            Dump("PROP ANIM " + animation.Name, props.Pose(animation,0));
        }
        Dump("PROP REST",props.RestPose);
        var player=new BotwActorResources(Path.Combine(IconCaptureAsset.Root(root),"Pack/Actor/Player.pack.zs"),[IconCaptureAsset.Root(root),dump]);
        player.PrepareTotkStrings();
        var pr=player.Models.First();
        var pm=new ToolboxActorModel(new ToolboxBfresDocument(player.ReadModel(pr.Resource)!),pr.Model,new());
        var face=props.RestPose.Meshes.Single(m=>m.Name.StartsWith("Icon_Head_Face__"));
        var original=pm.RestPose.Meshes.First(m=>m.Name.EndsWith("__Mt_Face"));
        var ni=original.Indices.Distinct().ToArray();
        var distances=new List<float>();var dots=new List<float>();
        foreach(var i in face.Indices.Distinct())
        {
            var p=Point(face,i); var j=ni.MinBy(j=>Vector3.DistanceSquared(p,Point(original,j)));
            distances.Add(Vector3.Distance(p,Point(original,j)));
            var n=new Vector3(face.Vertices[i*8+3],face.Vertices[i*8+4],face.Vertices[i*8+5]);
            var on=new Vector3(original.Vertices[j*8+3],original.Vertices[j*8+4],original.Vertices[j*8+5]);
            dots.Add(Vector3.Dot(n,on));
        }
        Console.WriteLine($"NATIVE NORMALS matched={distances.Count(d=>d<.0001f)}/{distances.Count} maxDistance={distances.Max()} meanDot={dots.Average()}");
        Meshes(props.RestPose);
        var pose = new ToolboxActorAnimation((object)document.SkeletalAnimations.Single(), path);
        foreach (string name in new[] { "Armor_001_Upper", "Armor_005_Upper", "Armor_009_Upper" })
        {
            var resources = new BotwActorResources(Path.Combine(IconCaptureAsset.Root(root), "Pack/Actor/" + name + ".pack.zs"), [IconCaptureAsset.Root(root),dump]);
            resources.PrepareTotkStrings();
            foreach (var reference in resources.Models)
            {
                var model = new ToolboxActorModel(new ToolboxBfresDocument(resources.ReadModel(reference.Resource)!), reference.Model, new());
                Dump(name + " MODEL=" + model.Name + " REST",model.RestPose);
                Meshes(model.RestPose);
                Compare(props,model,pose);
            }
        }
    }
    private static Vector3 Point(VisualModelMesh m, uint i) => new(m.Vertices[i*8],m.Vertices[i*8+1],m.Vertices[i*8+2]);
    private static void Compare(ToolboxActorModel props, ToolboxActorModel armor, ToolboxActorAnimation pose)
    {
        var p=props.Pose(pose,0); var a=armor.Pose(pose,0);
        foreach(var mesh in props.RestPose.Meshes.Where(m=>m.Name.StartsWith("Icon_Upper_")))
        {
            var posed=p.Meshes.Single(m=>m.Name==mesh.Name);
            var matches=new List<float>();
            foreach(var i in mesh.Indices.Distinct())
            {
                var point=Point(mesh,i); float nearest=.001f; Vector3? target=null;
                foreach(var other in armor.RestPose.Meshes)
                foreach(var j in other.Indices.Distinct())
                {
                    float distance=Vector3.Distance(point,Point(other,j));
                    if(distance>=nearest) continue;
                    nearest=distance; target=Point(a.Meshes.Single(m=>m.Name==other.Name),j);
                }
                if(target is {} t) matches.Add(Vector3.Distance(Point(posed,i),t));
            }
            Console.WriteLine($"FIT {armor.Name} {mesh.Name} matching={matches.Count} max={(matches.Count==0?0:matches.Max())} mean={(matches.Count==0?0:matches.Average())}");
        }
    }
    private static void Meshes(VisualModelData model)
    {
        foreach(var mesh in model.Meshes)
        {
            var points=mesh.Indices.Distinct().Select(i=>new Vector3(mesh.Vertices[i*8],mesh.Vertices[i*8+1],mesh.Vertices[i*8+2])).ToArray();
            Console.WriteLine($"MESH {mesh.Name} min={points.Aggregate(Vector3.Min)} max={points.Aggregate(Vector3.Max)}");
        }
    }
    private static void Dump(string name, VisualModelData model)
    {
        Console.WriteLine(name);
        foreach(var bone in model.Bones.Where(b=>b.Name is "Skl_Root" or "Spine_1" or "Spine_2" or "Neck" or "Head" or "Wrist_L" or "Wrist_R" or "Finger_B_1_L" or "Finger_B_2_L" or "Finger_B_3_L"))
        {
            var parent=model.Bones.FirstOrDefault(b=>b.Index==bone.ParentIndex);
            var inverse=Matrix4x4.Identity; if(parent!=null) Matrix4x4.Invert(parent.WorldMatrix,out inverse);
            Matrix4x4.Decompose(bone.WorldMatrix*inverse,out var s,out var r,out var t);
            Console.WriteLine($"{bone.Name} pos={t} rot={r} scale={s}");
        }
    }
}
