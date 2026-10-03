using System.Numerics;
using System.Text;
using BymlLibrary;
using MIConvexHull;
using Revrs;

namespace HKCLTool;

internal sealed record TotkPolytopeDocument(Byml Root, IReadOnlyList<BotwCollisionMesh> Meshes);

internal static class TotkPolytopeBuilder
{
    private const int MaximumPoints = 100_000;
    private const int MaximumHulls = 4096;
    private sealed class Point(Vector3 value, Vector3 origin, double extent) : IVertex
    {
        public Vector3 Value { get; } = value;
        public double[] Position { get; } = [((double)value.X-origin.X)/extent, ((double)value.Y-origin.Y)/extent, ((double)value.Z-origin.Z)/extent];
    }

    public static TotkPolytopeDocument Create(IReadOnlyList<BotwCollisionMesh> meshes, float scale, Vector3 offset, bool blenderZUp)
    {
        if (!float.IsFinite(scale) || scale <= 0 || !Finite(offset))
            throw new InvalidDataException("Collision scale and offset must be finite, with a positive scale.");
        var hulls = new List<BotwCollisionMesh>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mesh in meshes.Where(m => m.Included))
        {
            var part = 0;
            foreach (var component in Components(mesh))
            {
                var points = component.Select(v => (blenderZUp ? new Vector3(v.X, v.Z, -v.Y) : v) * scale + offset).ToArray();
                var baseName = string.IsNullOrWhiteSpace(mesh.Name) ? "Polytope" : mesh.Name;
                var name = part++ == 0 ? baseName : baseName + "_part" + part;
                for (var suffix = 2; !names.Add(name); suffix++) name = baseName + "_" + suffix;
                hulls.Add(Hull(name, points, mesh.Material));
                if (hulls.Count > MaximumHulls) throw new InvalidDataException("Too many collision parts. Use a simpler collision model.");
            }
        }
        if (hulls.Count == 0) throw new InvalidDataException("Select at least one collision mesh.");

        var polytopes = new List<Byml>();
        var properties = hulls.Select(BoxProperties).ToArray();
        for (var i = 0; i < hulls.Count; i++)
        {
            var hull = hulls[i];
            var presets = hull.Material.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray();
            if (presets.Length == 0) throw new InvalidDataException($"Mesh '{hull.Name}' needs at least one material preset.");
            polytopes.Add(Map(
                ("AutoCalc", properties[i].Node()),
                ("MassDistributionFactor", new Byml(1.0f)),
                ("MaterialPresets", new Byml(presets.Select(p => new Byml(p)).ToList())),
                ("Name", new Byml(hull.Name)),
                ("OffsetRotation", Vec(Vector3.Zero)),
                ("OffsetTranslation", Vec(Vector3.Zero)),
                ("Vertices", new Byml(hull.Vertices.Select(Vec).ToList()))));
        }

        // Vanilla polytopes use unit-mass AABB inertia, not tetrahedral solid inertia.
        // Combine those authored approximations with volume weights and the parallel-axis theorem.
        var volume = properties.Sum(p => p.Volume);
        var center = Vector3.Zero;
        foreach (var p in properties) center += p.Center * (float)(p.Volume / volume);
        var tensor = Vector3.Zero;
        foreach (var p in properties)
        {
            var d = p.Center-center;
            tensor += (p.Tensor + new Vector3(d.Y*d.Y+d.Z*d.Z, d.X*d.X+d.Z*d.Z, d.X*d.X+d.Y*d.Y)) * (float)(p.Volume/volume);
        }
        var compound = new Properties(properties.Select(p => p.Min).Aggregate(Vector3.Min), properties.Select(p => p.Max).Aggregate(Vector3.Max), center, tensor, volume);
        var root = Map(("AutoCalc", compound.Node()), ("Polytope", new Byml(polytopes)));
        return new(root, hulls);
    }

    public static void Write(TotkPolytopeDocument document, string path)
    {
        var binary = document.Root.ToBinary(Endianness.Little, 7);
        var reparsed = Byml.FromBinary(binary, out var endian, out var version);
        if (version != 7 || endian != Endianness.Little || reparsed.GetMap()["Polytope"].GetArray().Count != document.Meshes.Count)
            throw new InvalidDataException("Generated ShapeParam failed binary verification.");
        var extension = Path.GetExtension(path).ToLowerInvariant();
        byte[] bytes;
        if (extension == ".bgyml") bytes = binary;
        else if (extension is ".yaml" or ".yml")
        {
            var text = document.Root.ToYaml();
            if (Byml.FromText(text).GetMap()["Polytope"].GetArray().Count != document.Meshes.Count)
                throw new InvalidDataException("Generated ShapeParam failed YAML verification.");
            bytes = new UTF8Encoding(false).GetBytes(text);
        }
        else throw new InvalidDataException("Use .bgyml, .yaml, or .yml for TotK ShapeParam output.");
        AtomicFile.WriteAllBytes(path, bytes);
    }

    internal static BotwCollisionMesh Hull(string name, IEnumerable<Vector3> source, string material)
    {
        var points = source.Distinct().ToArray();
        if (points.Length < 4 || points.Any(v => !Finite(v)))
            throw new InvalidDataException($"Mesh '{name}' needs at least four finite, non-coplanar points. Give flat surfaces thickness.");
        if (points.Length > MaximumPoints) throw new InvalidDataException($"Mesh '{name}' exceeds the 100,000-point tool safety limit. Simplify or split the mesh.");
        var min = points.Aggregate(Vector3.Min); var max = points.Aggregate(Vector3.Max);
        var extent = Math.Max((double)max.X-min.X, Math.Max((double)max.Y-min.Y, (double)max.Z-min.Z));
        if (!double.IsFinite(extent) || extent <= 1e-8) throw new InvalidDataException($"Mesh '{name}' has no usable volume.");
        var origin = min * 0.5f + max * 0.5f;
        var result = ConvexHull.Create(points.Select(v => new Point(v, origin, extent)).ToArray(), 1e-9);
        if (result.Result == null)
            throw new InvalidDataException($"Mesh '{name}' cannot form a solid convex hull. Remove degenerate geometry or give flat surfaces thickness.");
        var vertices = result.Result.Points.ToArray();
        if (vertices.Length > 1024) throw new InvalidDataException($"Mesh '{name}' exceeds the 1,024-hull-vertex tool safety limit. Simplify or split the mesh.");
        var lookup = vertices.Select((v,i) => (v,i)).ToDictionary(x => x.v, x => x.i);
        var indices = new List<int>();
        double volume6 = 0;
        foreach (var face in result.Result.Faces)
        {
            var a = lookup[face.Vertices[0]]; var b = lookup[face.Vertices[1]]; var c = lookup[face.Vertices[2]];
            var pa = new Vector3((float)vertices[a].Position[0], (float)vertices[a].Position[1], (float)vertices[a].Position[2]);
            var pb = new Vector3((float)vertices[b].Position[0], (float)vertices[b].Position[1], (float)vertices[b].Position[2]);
            var pc = new Vector3((float)vertices[c].Position[0], (float)vertices[c].Position[1], (float)vertices[c].Position[2]);
            var normal = new Vector3((float)face.Normal[0], (float)face.Normal[1], (float)face.Normal[2]);
            if (Vector3.Dot(Vector3.Cross(pb-pa, pc-pa), normal) < 0) { (b,c)=(c,b); (pb,pc)=(pc,pb); }
            volume6 += Vector3.Dot(pa, Vector3.Cross(pb,pc));
            indices.AddRange([a,b,c]);
        }
        if (!double.IsFinite(volume6) || volume6 <= 1e-10) throw new InvalidDataException($"Mesh '{name}' is flat or too thin for a solid polytope.");
        return new() { Name=name, Material=material, Vertices=vertices.Select(v => v.Value).ToArray(), Indices=indices };
    }

    private static IEnumerable<Vector3[]> Components(BotwCollisionMesh mesh)
    {
        if (mesh.Indices.Count == 0 || mesh.Indices.Count % 3 != 0) throw new InvalidDataException($"Mesh '{mesh.Name}' has no valid triangle topology.");
        var ids = new Dictionary<Vector3,int>();
        var vertices = new List<Vector3>();
        var remap = new Dictionary<int,int>();
        foreach (var index in mesh.Indices.Distinct())
        {
            if ((uint)index >= mesh.Vertices.Count) throw new InvalidDataException($"Mesh '{mesh.Name}' has an invalid triangle index.");
            var v = mesh.Vertices[index];
            if (!Finite(v)) throw new InvalidDataException($"Mesh '{mesh.Name}' contains a non-finite vertex.");
            if (!ids.TryGetValue(v,out var id)) { id=vertices.Count; ids[v]=id; vertices.Add(v); }
            remap[index]=id;
        }
        var parents = Enumerable.Range(0,vertices.Count).ToArray();
        int Find(int x) { while (parents[x]!=x) { parents[x]=parents[parents[x]]; x=parents[x]; } return x; }
        for (var i=0;i<mesh.Indices.Count;i+=3)
        {
            var a=Find(remap[mesh.Indices[i]]);
            parents[Find(remap[mesh.Indices[i+1]])]=a;
            parents[Find(remap[mesh.Indices[i+2]])]=a;
        }
        return vertices.Select((v,i)=>(v,i)).GroupBy(x=>Find(x.i)).Select(g=>g.Select(x=>x.v).ToArray());
    }

    private sealed record Properties(Vector3 Min, Vector3 Max, Vector3 Center, Vector3 Tensor, double Volume)
    {
        public Byml Node()
        {
            if (!Finite(Min) || !Finite(Max) || !Finite(Center) || !Finite(Tensor) || !float.IsFinite((float)Volume) || (float)Volume <= 0)
                throw new InvalidDataException("Collision bounds or mass properties overflowed. Reduce the model scale.");
            return Map(("Axis",Vec(Vector3.Zero)),("Center",Vec(Center)),("Min",Vec(Min)),("Max",Vec(Max)),("Tensor",Vec(Tensor)),("Volume",new Byml((float)Volume)));
        }
    }

    private static Properties BoxProperties(BotwCollisionMesh mesh)
    {
        var min=mesh.Vertices.Aggregate(Vector3.Min); var max=mesh.Vertices.Aggregate(Vector3.Max);
        var x=(double)max.X-min.X; var y=(double)max.Y-min.Y; var z=(double)max.Z-min.Z;
        return new(min,max,min*0.5f+max*0.5f,new((float)((y*y+z*z)/12),(float)((x*x+z*z)/12),(float)((x*x+y*y)/12)),x*y*z);
    }

    internal static Byml Map(params (string Key, Byml Value)[] values) => new(values.ToDictionary(v=>v.Key,v=>v.Value,StringComparer.Ordinal));
    internal static Byml Vec(Vector3 v) => Map(("X",new Byml(v.X)),("Y",new Byml(v.Y)),("Z",new Byml(v.Z)));
    internal static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
