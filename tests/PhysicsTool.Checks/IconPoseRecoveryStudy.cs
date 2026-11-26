using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using HKCLTool;
using PhysicsTool.Core;

internal static class IconPoseRecoveryStudy
{
    internal static void TrimWaist(string input, string output, float scale)
    {
        if (Path.GetFullPath(input) == Path.GetFullPath(output) || !float.IsFinite(scale) || scale <= 0 || scale > 1)
            throw new ArgumentException("Choose a separate output and a waist height scale in (0,1].");
        var doc = new ToolboxBfresDocument(File.ReadAllBytes(input));
        var helperType = doc.Resource.GetType().Assembly.GetTypes().Single(t => t.Name == "VertexBufferHelper");
        int changed = 0;
        foreach (dynamic model in doc.Models)
        foreach (dynamic shape in ToolboxBfresDocument.Items((object)model.Shapes))
        {
            if (!((string)shape.Name).Contains("Skin", StringComparison.Ordinal)) continue;
            object buffer = model.VertexBuffers[(int)shape.VertexBufferIndex];
            var ctor = helperType.GetConstructors().Single(c => c.GetParameters().Length > 0 && c.GetParameters()[0].ParameterType.IsInstanceOfType(buffer));
            var args = ctor.GetParameters().Select((p,i) => i == 0 ? buffer : p.DefaultValue).ToArray();
            dynamic helper = ctor.Invoke(args);
            var attributes = ToolboxBfresDocument.Items((object)helper.Attributes).ToDictionary(a => (string)a.Name);
            var positions = (Syroot.Maths.Vector4F[])attributes["_p0"].Data;
            float bottom = positions.Min(p => p.Y), top = positions.Max(p => p.Y);
            for (int i = 0; i < positions.Length; i++) positions[i].Y = bottom + (positions[i].Y - bottom) * scale;
            if (attributes.TryGetValue("_n0", out var normalAttribute))
            {
                var normals = (Syroot.Maths.Vector4F[])normalAttribute.Data;
                for (int i = 0; i < normals.Length; i++)
                {
                    var n = Vector3.Normalize(new(normals[i].X, normals[i].Y / scale, normals[i].Z));
                    normals[i].X=n.X; normals[i].Y=n.Y; normals[i].Z=n.Z;
                }
            }
            if (attributes.TryGetValue("_t0", out var tangentAttribute))
            {
                var tangents = (Syroot.Maths.Vector4F[])tangentAttribute.Data;
                for (int i = 0; i < tangents.Length; i++)
                {
                    var t = Vector3.Normalize(new(tangents[i].X, tangents[i].Y * scale, tangents[i].Z));
                    tangents[i].X=t.X; tangents[i].Y=t.Y; tangents[i].Z=t.Z;
                }
            }
            model.VertexBuffers[(int)shape.VertexBufferIndex] = helper.ToVertexBuffer();
            Console.WriteLine($"Waist skin: {positions.Length} vertices; {bottom}..{top} -> {bottom}..{bottom+(top-bottom)*scale}");
            changed++;
        }
        if (changed != 1) throw new InvalidDataException("Expected exactly one waist skin mesh.");
        ((dynamic)doc.Resource).Save(output);
        var source = new ToolboxBfresDocument(File.ReadAllBytes(input));
        var saved = new ToolboxBfresDocument(File.ReadAllBytes(output));
        var before = new ToolboxActorModel(source, (string)source.Models[0].Name, new()).RestPose;
        var after = new ToolboxActorModel(saved, (string)saved.Models[0].Name, new()).RestPose;
        if (before.Meshes.Count != after.Meshes.Count || before.Bones.Count != after.Bones.Count ||
            before.Bones.Zip(after.Bones).Any(p => p.First.Name != p.Second.Name || p.First.WorldMatrix != p.Second.WorldMatrix))
            throw new InvalidDataException("Waist trim changed the skeleton or mesh count.");
        foreach (var (a,b) in before.Meshes.Zip(after.Meshes))
        {
            if (!a.Indices.SequenceEqual(b.Indices) || a.Vertices.Length != b.Vertices.Length)
                throw new InvalidDataException("Waist trim changed topology.");
            if (!a.Name.Contains("Skin", StringComparison.Ordinal) && !a.Vertices.SequenceEqual(b.Vertices))
                throw new InvalidDataException("Waist trim changed the belt.");
        }
        Console.WriteLine("PASS trimmed mesh roundtrip: unchanged belt, topology and skeleton");
        InspectBelt(output);
    }
    internal static void InspectBelt(string path)
    {
        var doc = new ToolboxBfresDocument(File.ReadAllBytes(path));
        foreach (dynamic model in doc.Models)
        {
            Console.WriteLine("MODEL " + model.Name);
            foreach (dynamic shape in ToolboxBfresDocument.Items((object)model.Shapes)) Console.WriteLine($"SHAPE {shape.Name} skin={shape.VertexSkinCount}");
            foreach (dynamic material in ToolboxBfresDocument.Items((object)model.Materials))
            {
                Console.WriteLine("MATERIAL " + material.Name);
                foreach (dynamic tex in ToolboxBfresDocument.Items((object)material.TextureRefs)) Console.WriteLine("TEXTURE " + (tex is string s ? s : tex.Name));
            }
            var actor = new ToolboxActorModel(doc, (string)model.Name, new());
            Console.WriteLine($"BOUNDS {actor.RestPose.BoundsMinimum} {actor.RestPose.BoundsMaximum}");
            foreach (var mesh in actor.RestPose.Meshes)
            {
                var ys = Enumerable.Range(0, mesh.Vertices.Length / 8).Select(i => mesh.Vertices[i * 8 + 1]).ToArray();
                Console.WriteLine($"MESH {mesh.Name} vertices={mesh.Vertices.Length/8} triangles={mesh.Indices.Length/3} Y={ys.Min()}..{ys.Max()}");
                Console.WriteLine("Y LEVELS " + string.Join(", ", ys.Distinct().Order()));
            }
            Console.WriteLine("RESOURCE API " + string.Join(" | ", doc.Resource.GetType().GetMethods().Where(m => m.Name.Contains("Save")).Select(m => m.ToString())));
            var helperType = doc.Resource.GetType().Assembly.GetTypes().Single(t => t.Name == "VertexBufferHelper");
            Console.WriteLine("HELPER API " + string.Join(" | ", helperType.GetMethods().Where(m => m.Name.Contains("Buffer")).Select(m => m.ToString())));
            Console.WriteLine("BONES " + string.Join(", ",actor.RestPose.Bones.Select(b=>b.Name)));
        }
    }
    internal static VisualModelData Apply(IconCaptureAsset asset, string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var entries = json.RootElement.GetProperty("Bones");
        var models = ((ToolboxActorModel Model, VisualModelData Base)[])typeof(IconArmorPose).GetField("_models", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(asset.ArmorPose)!;
        var output = new VisualModelData { BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue) };
        foreach (var (model, idle) in models)
        {
            var rest = model.RestPose.Bones;
            var worlds = new Dictionary<int, Matrix4x4>();
            Matrix4x4 World(int i)
            {
                if (worlds.TryGetValue(i, out var cached)) return cached;
                var b = rest[i];
                var inverse = Matrix4x4.Identity;
                if (b.ParentIndex >= 0) Matrix4x4.Invert(rest[b.ParentIndex].WorldMatrix, out inverse);
                var local = b.WorldMatrix * inverse;
                if (entries.TryGetProperty(b.Name, out var entry))
                {
                    var q = entry.GetProperty("Rotation").EnumerateArray().Select(x => x.GetSingle()).ToArray();
                    var t = entry.GetProperty("Translation").EnumerateArray().Select(x => x.GetSingle()).ToArray();
                    var position = local.Translation + new Vector3(t[0], t[1], t[2]); local.Translation = Vector3.Zero;
                    local *= Matrix4x4.CreateFromQuaternion(new(q[0],q[1],q[2],q[3])); local.Translation = position;
                }
                else
                {
                    if (b.ParentIndex >= 0) Matrix4x4.Invert(idle.Bones[b.ParentIndex].WorldMatrix, out inverse);
                    local = idle.Bones[i].WorldMatrix * inverse;
                }
                return worlds[i] = local * (b.ParentIndex < 0 ? Matrix4x4.Identity : World(b.ParentIndex));
            }
            var overrides = rest.ToDictionary(b => b.Name, b => VisualBoneTransform.Override(b.Name, World(b.Index)));
            foreach (var mesh in model.Pose(null, 0, overrides).Meshes) ShrineSceneAssets.AddMesh(output, mesh, Matrix4x4.Identity, mesh.Name);
        }
        return output;
    }
    internal static void Export(string root, string output)
    {
        Directory.CreateDirectory(output);
        foreach (string name in new[] { "Armor_001_Upper", "Armor_001_Lower", "Mannequin_001_Upper", "Mannequin_001_Lower" })
        {
            bool totk = Directory.Exists(Path.Combine(root, "Pack", "Actor"));
            var resources = new BotwActorResources(Path.Combine(root, totk ? "Pack/Actor" : "Actor/Pack", name + (totk ? ".pack.zs" : ".sbactorpack")), [root]);
            resources.PrepareTotkStrings();
            var reference = resources.Models.First();
            var model = new ToolboxActorModel(new ToolboxBfresDocument(resources.ReadModel(reference.Resource)!), reference.Model, new());
            var animationData = resources.ReadModel(totk ? "Player_Animation.anim" : "Player_Animation");
            var animation = animationData == null ? null : new ToolboxBfresDocument(animationData).SkeletalAnimations.FirstOrDefault(a => (string)a.Name == "Nml_Wait");
            VisualModelData wait = animation == null ? model.RestPose : model.Pose(new ToolboxActorAnimation(animation, "Player_Animation"), 0);
            // The diagnostic exports the exact bindings already used by the preview.
            object Field(string field) => typeof(ToolboxActorModel).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(model)!;
            object Prop(object value, string property) => value.GetType().GetProperty(property)!.GetValue(value)!;
            float[] Matrix(Matrix4x4 m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
            var meshes = ((IEnumerable)Field("_meshes")).Cast<object>().Select(m => {
                var visual = (VisualModelMesh)Prop(m, "Visual");
                return new { visual.Name, Vertices = model.RestPose.Meshes.Single(v => v.Name == visual.Name).Vertices, visual.Indices,
                    Bones = (int[][])Prop(m, "Bones"), Weights = (float[][])Prop(m, "Weights") };
            }).ToArray();
            File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(new {
                Name = name, Meshes = meshes,
                Bones = model.RestPose.Bones.Select(b => new { b.Name, b.Index, b.ParentIndex, Rest = Matrix(b.WorldMatrix), Wait = Matrix(wait.Bones[b.Index].WorldMatrix) })
            }));
            Console.WriteLine("EXPORTED " + name);
        }
    }
}
