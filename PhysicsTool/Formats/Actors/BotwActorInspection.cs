using System.Text.Json;

namespace HKCLTool;

internal static class BotwActorInspection
{
    public static void Check(string path, string output, string? actorEntry = null)
    {
        try
        {
            var refs = new BotwActorResources(path, actorEntry: actorEntry);
            var textures = new Dictionary<string, VisualModelTexture>();
            var documents = refs.Models.Select(m => m.Resource).Distinct().ToDictionary(n => n, n => new ToolboxBfresDocument(refs.ReadModel(n) ?? throw new FileNotFoundException(n)));
            foreach (var pair in documents)
            {
                pair.Value.ReadTextures(textures, refs.Warnings);
                foreach (var suffix in new[] { ".Tex", ".Tex1" })
                    if (refs.ReadModel(pair.Key, suffix) is { } data) new ToolboxBfresDocument(data).ReadTextures(textures, refs.Warnings);
            }
            var animations = new List<ToolboxActorAnimation>();
            foreach (var name in refs.AnimationResources)
                if (refs.ReadModel(name) is { } data)
                    animations.AddRange(new ToolboxBfresDocument(data).SkeletalAnimations.Select(a => new ToolboxActorAnimation(a, name)));
            var models = refs.Models.Select(m => new ToolboxActorModel(documents[m.Resource], m.Model, textures,
                ToolboxActorAppearance.Read(documents[m.Resource], refs.VariationMatAnim, refs.VariationMatAnimFrame, refs.Warnings), refs.Warnings)).ToArray();
            var result = new { refs.ActorName, refs.IsSwitch, refs.Models, refs.PhysicsFiles, refs.AnimationResources,
                AnimationCount = animations.Count, AnimationNames = animations.Select(a => a.Name), Textures = textures.Keys, refs.Warnings,
                ModelsLoaded = models.Select(m => new { m.Name, Bones = m.RestPose.Bones.Count, Meshes = m.RestPose.Meshes.Count,
                    m.RestPose.TriangleCount, Min = m.RestPose.BoundsMinimum.ToString(), Max = m.RestPose.BoundsMaximum.ToString(),
                    Animated = animations.Take(2).Select(a => new { a.Name, a.FrameCount, Frame10 = m.Pose(a, 10).BoundsMaximum.ToString() }) }) };
            File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { File.WriteAllText(output, ex.ToString()); Environment.ExitCode = 1; }
    }
    public static void Write(string path, string output)
    {
        var pack = BotwSarcArchive.Open(path);
        var entries = pack.Entries.Where(e =>
            e.Name.EndsWith(".bmodellist") || e.Name.EndsWith(".baslist") ||
            e.Name.EndsWith(".bphysics") || e.Name.EndsWith(".bas") ||
            e.Name.EndsWith(".bxml")).ToArray();
        object List(AampReadList list) => new
        {
            list.Hash,
            Objects = list.Objects.Select(o => new
            {
                o.Hash, Parameters = o.Parameters.Select(p => new
                {
                    p.Hash, p.Type,
                    Value = p.Type is 7 or 8 or 15 or 20 ? p.AsString() : Convert.ToHexString(p.Bytes)
                })
            }),
            Children = list.Children.Select(List)
        };
        var result = new
        {
            Files = pack.Entries.Select(e => e.Name),
            Parameters = entries.Select(e =>
            {
                try { return new { e.Name, Data = List(BotwAampReader.Read(pack.Read(e).ToArray()).Root) }; }
                catch (Exception ex) { return new { e.Name, Data = (object)ex.Message }; }
            })
        };
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
}
