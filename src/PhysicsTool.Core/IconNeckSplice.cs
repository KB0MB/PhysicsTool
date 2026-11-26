using System.Numerics;
using HKCLTool;

namespace PhysicsTool.Core;

// Use the garment's own weighted neckline, retaining its collar fit, and close
// the upper boundary. The cap follows the skinned rim, including imported poses.
internal sealed class IconNeckSplice
{
    private readonly string _source;
    private readonly int[] _rim;
    private const string Target = IconArmorProps.Prefix + "Icon_Upper_Neck";

    internal IconNeckSplice(VisualModelMesh source)
    {
        _source = source.Name;
        var points = new List<Vector3>();
        var representatives = new List<int>();
        var weld = new Dictionary<int, int>();
        foreach (int index in source.Indices.Select(i => (int)i).Distinct())
        {
            var p = Point(source, index);
            int shared = points.FindIndex(q => Vector3.DistanceSquared(p, q) < 1e-10f);
            if (shared < 0) { shared = points.Count; points.Add(p); representatives.Add(index); }
            weld[index] = shared;
        }
        var edges = new Dictionary<(int, int), (int Count, int A, int B)>();
        void Edge(int a, int b)
        {
            if (a == b) return;
            var key = a < b ? (a,b) : (b,a);
            edges[key] = (edges.GetValueOrDefault(key).Count + 1,a,b);
        }
        for (int i=0;i<source.Indices.Length;i+=3)
        {
            int a=weld[(int)source.Indices[i]], b=weld[(int)source.Indices[i+1]], c=weld[(int)source.Indices[i+2]];
            if (a==b || b==c || c==a) continue;
            Edge(a,b); Edge(b,c); Edge(c,a);
        }
        var boundary = edges.Values.Where(e=>e.Count==1).ToList();
        var loops = new List<List<int>>();
        while(boundary.Count>0)
        {
            var first=boundary[0]; boundary.RemoveAt(0);
            var loop=new List<int>{first.A}; int next=first.B;
            while(next!=first.A)
            {
                loop.Add(next);
                int edge=boundary.FindIndex(e=>e.A==next);
                if(edge<0) throw new InvalidDataException("The neck splice has an open or inconsistent boundary: " + _source);
                next=boundary[edge].B; boundary.RemoveAt(edge);
            }
            if(loop.Count>=3) loops.Add(loop);
        }
        var top=loops.OrderByDescending(l=>l.Average(i=>points[i].Y)).FirstOrDefault()
            ?? throw new InvalidDataException("No open neck rim in " + _source);
        // Reverse the existing boundary so the cap faces away from the skin.
        top.Reverse();
        var center=top.Aggregate(Vector3.Zero,(sum,i)=>sum+points[i])/top.Count;
        var normal=Vector3.Normalize(top.Select((v,i)=>Vector3.Cross(points[v]-center,points[top[(i+1)%top.Count]]-center)).Aggregate(Vector3.Zero,(a,b)=>a+b));
        if(!float.IsFinite(normal.Y) || normal.Y < .5f || top.Where((v,i)=>Vector3.Dot(Vector3.Cross(points[v]-center,points[top[(i+1)%top.Count]]-center),normal)<-1e-8f).Any())
            throw new InvalidDataException("The neck rim cannot be capped as a convex upper boundary: " + _source);
        _rim=top.Select(i=>representatives[i]).ToArray();
    }

    internal void Apply(VisualModelData model)
    {
        var source=model.Meshes.Single(m=>m.Name==_source);
        var appearance=model.Meshes.Single(m=>m.Name==Target);
        int count=source.Vertices.Length/8;
        var vertices=new float[(count+_rim.Length+1)*8]; source.Vertices.CopyTo(vertices,0);
        var points=_rim.Select(i=>Point(source,i)).ToArray();
        var center=points.Aggregate(Vector3.Zero,(a,b)=>a+b)/points.Length;
        var normal=Vector3.Normalize(points.Select((p,i)=>Vector3.Cross(p-center,points[(i+1)%points.Length]-center)).Aggregate(Vector3.Zero,(a,b)=>a+b));
        for(int i=0;i<=points.Length;i++)
        {
            var p=i==points.Length?center:points[i]; int offset=(count+i)*8;
            vertices[offset]=p.X; vertices[offset+1]=p.Y; vertices[offset+2]=p.Z;
            vertices[offset+3]=normal.X; vertices[offset+4]=normal.Y; vertices[offset+5]=normal.Z;
            vertices[offset+6]=vertices[offset+7]=.5f;
        }
        var indices=source.Indices.ToList();
        for(int i=0;i<points.Length;i++) indices.AddRange([(uint)(count+i),(uint)(count+(i+1)%points.Length),(uint)(count+points.Length)]);
        var mesh=new VisualModelMesh { Name=Target, Vertices=vertices, Indices=indices.ToArray(),
            BaseColor=appearance.BaseColor, CullingMode=appearance.CullingMode, Texture=appearance.Texture,
            Material=appearance.Material.Clone() };
        model.Meshes[model.Meshes.IndexOf(appearance)]=mesh;
        model.Meshes.Remove(source);
        model.TriangleCount=model.Meshes.Sum(m=>m.Indices.Length/3);
    }
    private static Vector3 Point(VisualModelMesh mesh,int i) => new(mesh.Vertices[i*8],mesh.Vertices[i*8+1],mesh.Vertices[i*8+2]);
}
