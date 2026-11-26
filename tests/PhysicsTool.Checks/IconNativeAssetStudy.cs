using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BymlLibrary;
using HKCLTool;
using PhysicsTool.Core;

internal static class IconNativeAssetStudy
{
    internal static void Run(string root, string output)
    {
        Directory.CreateDirectory(output);
        bool totk = Directory.Exists(Path.Combine(root, "Pack", "Actor"));
        var names = new[] { "Armor_001_Upper", "Armor_001_Lower", "Armor_044_Upper", "Armor_009_Head", "Armor_009_Upper", "Armor_009_Lower", "Armor_Default_Head", "Armor_Default_Upper",
            "Mannequin_001_Head", "Mannequin_001_Upper", "Mannequin_001_Lower", "Mannequin_003_Head", "Mannequin_003_Upper", "Mannequin_009_Upper",
            "PouchPlayer", "Player", "GameROMPlayer", "TwnObj_Village_HatenoTailorMannequin_A_01" };
        var animations = new HashSet<string>();
        foreach (string name in names)
        {
            string path = Path.Combine(root, totk ? "Pack/Actor" : "Actor/Pack", name + (totk ? ".pack.zs" : ".sbactorpack"));
            string? actorEntry = null;
            if (!File.Exists(path) && !totk && name == "GameROMPlayer") { path = Path.Combine(root, "Pack", "TitleBG.pack"); actorEntry = "Actor/Pack/GameROMPlayer.sbactorpack"; }
            if (!File.Exists(path)) continue;
            try
            {
                var resources = new BotwActorResources(path, [root], actorEntry); resources.PrepareTotkStrings();
                var text = new StringBuilder();
                text.AppendLine("ACTOR " + name);
                text.AppendLine("MODELS " + JsonSerializer.Serialize(resources.Models));
                text.AppendLine("ANIMATION RESOURCES " + string.Join(", ", resources.AnimationResources));
                text.AppendLine("ANIMATION STATES " + JsonSerializer.Serialize(resources.Animations));
                if (totk)
                {
                    var capture = resources.EquipmentParameters("CaptureRef");
                    foreach (var pair in capture) text.AppendLine("CAPTURE " + pair.Key + ": " + pair.Value.ToYaml());
                }
                var data = File.ReadAllBytes(path);
                if (totk) data = new TotkResourceCompression().Decode(data, [root]);
                var archive = BotwSarcArchive.Parse(data);
                if (actorEntry != null) archive = BotwSarcArchive.Parse(archive.Read(archive.Entries.Single(e => e.Name == actorEntry)).Span);
                foreach (var entry in archive.Entries)
                {
                    if (Regex.IsMatch(entry.Name, "Capture|ModelInfo|ASParam|ModelList|ASList|ActorLink|GeneralParamList", RegexOptions.IgnoreCase))
                    {
                        text.AppendLine("ENTRY " + entry.Name);
                        var bytes = archive.Read(entry).ToArray();
                        if (entry.Name.EndsWith(".bgyml")) text.AppendLine(Byml.FromBinary(bytes).ToYaml());
                        else text.AppendLine(string.Join(" | ", Strings(bytes)));
                        if (entry.Name.EndsWith(".bgparamlist"))
                            DumpArmorParameters(BotwAampReader.Read(bytes).Root, text);
                    }
                    else if (entry.Name.EndsWith(".bgyml"))
                    {
                        var yaml = Byml.FromBinary(archive.Read(entry).ToArray()).ToYaml();
                        if (Regex.IsMatch(yaml, "Capture|Torso|Mannequin|BlackSkin", RegexOptions.IgnoreCase))
                            text.AppendLine("RELATED " + entry.Name + "\n" + yaml);
                    }
                }
                foreach (var group in resources.Models.GroupBy(m => m.Resource))
                {
                    var bytes = resources.ReadModel(group.Key); if (bytes == null) continue;
                    var document = new ToolboxBfresDocument(bytes);
                    text.AppendLine("BFRES " + group.Key);
                    Dump(document, text, true);
                    foreach (var reference in group)
                    {
                        var visual = new ToolboxActorModel(document, reference.Model, new()).RestPose;
                        foreach (var mesh in visual.Meshes)
                            text.AppendLine($"GEOMETRY {mesh.Name} vertices={mesh.Vertices.Length / 8} triangles={mesh.Indices.Length / 3}");
                    }
                }
                File.WriteAllText(Path.Combine(output, name + ".txt"), text.ToString());
                foreach (var resource in resources.AnimationResources)
                {
                    if (!animations.Add(resource)) continue;
                    var bytes = resources.ReadModel(resource); if (bytes == null) continue;
                    var document = new ToolboxBfresDocument(bytes);
                    var report = new StringBuilder(); Dump(document, report, false);
                    File.WriteAllText(Path.Combine(output, resource + "-animations.txt"), report.ToString());
                }
                Console.WriteLine("INSPECTED " + name);
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, name + "-error.txt"), ex.ToString()); Console.WriteLine("ERROR " + name + ": " + ex.Message); }
        }
        if (totk)
        {
            var captureReport = new StringBuilder();
            int count = 0;
            foreach (string pack in Directory.EnumerateFiles(Path.Combine(root, "Pack", "Actor"), "Armor_*.pack.zs"))
            {
                var archive = BotwSarcArchive.Parse(new TotkResourceCompression().Decode(File.ReadAllBytes(pack), [root]));
                foreach (var entry in archive.Entries.Where(e => e.Name.StartsWith("Component/CaptureParam/")))
                {
                    count++;
                    captureReport.AppendLine(Path.GetFileName(pack) + " -> " + entry.Name);
                    captureReport.AppendLine(Byml.FromBinary(archive.Read(entry).ToArray()).ToYaml());
                }
            }
            File.WriteAllText(Path.Combine(output, "all-armor-capture-params.txt"), captureReport.ToString());
            Console.WriteLine("CAPTURE ENTRIES " + count);
            foreach (string graph in new[] { "Player", "PouchPlayer", "NewMannequin" })
            {
                string path = Path.Combine(root, "AS", graph + ".root.asb.zs");
                if (File.Exists(path)) File.WriteAllLines(Path.Combine(output, graph + "-AS-strings.txt"),
                    Strings(new TotkResourceCompression().Decode(File.ReadAllBytes(path), [root])));
            }
        }
    }
    private static IEnumerable<string> Strings(byte[] bytes) => Regex.Matches(Encoding.Latin1.GetString(bytes), "[ -~]{4,}").Select(m => m.Value);
    private static void DumpArmorParameters(AampReadList list, StringBuilder text)
    {
        foreach (var obj in list.Objects)
            foreach (string name in new[] { "StarNum", "NextRankName", "DefenceAddLevel", "EffectType", "EffectLevel" })
                if (obj.Find(name) is { } value)
                    text.AppendLine($"ARMOR {name}=" + (value.Type == 2 ? value.AsInt32().ToString() : value.AsString()));
        foreach (var child in list.Children) DumpArmorParameters(child, text);
    }
    private static void Dump(ToolboxBfresDocument document, StringBuilder text, bool models)
    {
        foreach (dynamic a in document.SkeletalAnimations) text.AppendLine($"SKELETAL {a.Name} frames={a.FrameCount} tracks={ToolboxBfresDocument.Items((object)a.BoneAnims).Length}");
        foreach (var p in document.Resource.GetType().GetProperties().Where(p => p.Name.Contains("Anim")))
        {
            if (p.Name == "SkeletalAnims") continue;
            try { foreach (dynamic a in ToolboxBfresDocument.Items(p.GetValue(document.Resource))) text.AppendLine($"{p.Name} {a.Name}"); } catch { }
        }
        if (!models) return;
        foreach (dynamic model in document.Models)
        {
            text.AppendLine("MODEL " + model.Name);
            foreach (dynamic shape in ToolboxBfresDocument.Items((object)model.Shapes)) text.AppendLine($"SHAPE {shape.Name} material={shape.MaterialIndex} bone={shape.BoneIndex} skin={shape.VertexSkinCount}");
            foreach (dynamic material in ToolboxBfresDocument.Items((object)model.Materials))
            {
                text.AppendLine("MATERIAL " + material.Name);
                foreach (dynamic texture in ToolboxBfresDocument.Items((object)material.TextureRefs)) text.AppendLine("TEXTURE " + (texture is string s ? s : (string)texture.Name));
                foreach (dynamic param in ToolboxBfresDocument.Items((object)material.ShaderParams))
                {
                    if (!Regex.IsMatch((string)param.Name, "color|skin|alpha|value", RegexOptions.IgnoreCase)) continue;
                    try { text.AppendLine("PARAM " + param.Name + "=" + JsonSerializer.Serialize((object)param.DataValue)); } catch { }
                }
            }
        }
    }
}
