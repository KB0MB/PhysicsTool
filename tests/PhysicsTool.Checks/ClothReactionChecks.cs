using BymlLibrary;
using HKCLTool;
using System.Text.Json;
using Revrs;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class ClothReactionChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        foreach (var path in new[] { "Armor_179/Armor_179_Head.hkcl", "Armor_179\\Armor_179_Head.hkcl" })
            Require(BphysicsRuntimeProfileCatalog.TryGetCloth(path, "Hat_179_Havok", out var nativeProfile) && nativeProfile.WindDrag == 4f,
                "BotW profiles must accept game paths on every host platform");
        static Byml Map(params (string, Byml)[] pairs) => new(pairs.ToDictionary(p => p.Item1, p => p.Item2));
        static Byml Items(params Byml[] values) => new(values.ToList());
        var files = new Dictionary<string, byte[]>();
        void Add(string path, Byml value) => files[path + ".bgyml"] = value.ToBinary(Endianness.Little, 7);
        Add("Actor/Test.engine__actor__ActorParam", Map(("Components", Map(("PhysicsRef", new Byml("?physics.bgyml"))))));
        Add("physics", Map(("ControllerSetPath", new Byml("Work/controller.gyml"))));
        var references = Items(Map(("Name", new Byml("Link:Ribbon")), ("FilePath", new Byml("Work/child.gyml"))));
        Add("controller", Map(("Cloth", Items(Map(("FilePath", new Byml("Work/cloth.gyml"))))), ("ClothReaction", references)));
        Add("cloth", Map(("MeshList", Items(Map(("Name", new Byml("Link:Ribbon")), ("BaseBone", new Byml("Link:Root")))))));
        Add("parent", Map(("Frequency", new Byml(2f)), ("DragForce", new Byml(.2f)), ("UseStrongWind", new Byml(false)),
            ("ReactionParam", Items(Map(("Frequency", new Byml(4f)), ("DragForce", new Byml(.5f)), ("ReactionSpeed", new Byml(5f)))))));
        Add("child", Map(("$parent", new Byml("Work/parent.gyml")), ("Frequency", new Byml(3f))));
        var profiles = TotkClothReactionProfiles.ReadActor("Test", p => files.GetValueOrDefault(p));
        var reaction = profiles.Cloths["Ribbon"];
        Require(reaction.BaseBone == "Root" && reaction.Base.Frequency == 3 && reaction.Base.DragForce == .2f && reaction.Reactions.Single().Frequency == 4,
            "Reaction reference/inheritance/name matching failed");
        Require(reaction.Base.SwingFactor == null && reaction.Strong.Frequency == null, "Missing engine defaults were invented");
        Add("parent", Map(("$parent", new Byml("Work/child.gyml"))));
        try { TotkClothReactionProfiles.ReadActor("Test", p => files.GetValueOrDefault(p)); throw new Exception("Cyclic reaction was accepted"); } catch (InvalidDataException) { }
        Add("parent", Map(("$parent", new Byml("Work/../private.gyml"))));
        try { TotkClothReactionProfiles.ReadActor("Test", p => files.GetValueOrDefault(p)); throw new Exception("Unsafe reaction path was accepted"); } catch (InvalidDataException) { }
        files.Remove("parent.bgyml");
        try { TotkClothReactionProfiles.ReadActor("Test", p => files.GetValueOrDefault(p)); throw new Exception("Missing reaction parent was silently ignored"); } catch (InvalidDataException) { }
        var session = new EditorSession(); session.New("Ribbon", 2);
        var sidecar = session.Document.CreateBphysicsDocument("Test/Test.hkcl");
        Require(sidecar.SubWindSpeed == 25f && sidecar.SubWindFrequency == 5f &&
            sidecar.Cloths[0].WindFrequency == 4.5f && sidecar.Cloths[0].WindDrag == 8f &&
            sidecar.Cloths[0].WindMinSpeed == -2f && sidecar.Cloths[0].WindMaxSpeed == 12f &&
            sidecar.Cloths[0].SubWindFactorMain == 1f && sidecar.Cloths[0].SubWindFactorAdd == 0f,
            "Generic export did not use the balanced armor wind defaults");
        Require(session.Document.CreateBphysicsDocument("Armor_179/Armor_179_Head.hkcl").SubWindSpeed == 50f &&
            session.Document.CreateBphysicsDocument("Armor_182/Armor_182_Head.hkcl").SubWindSpeed == 0f,
            "New export defaults replaced verified native profiles");
        sidecar.Cloths[0] = sidecar.Cloths[0] with { WindMinSpeed = -3.75f, WindMaxSpeed = 9.25f };
        var target = Path.Combine(output, "test.bphysics"); BphysicsService.Save(target, sidecar);
        var loaded = BphysicsService.Load(target);
        Require(sidecar.Cloths.SequenceEqual(loaded.Cloths), "BPHYSICS round trip changed wind values");
        Require(loaded.SubWindSpeed == 25f && loaded.SubWindFrequency == 5f, "Shared export defaults failed to round trip");
        sidecar.SubWindSpeed = 50f;
        sidecar.SubWindFrequency = .8f;
        sidecar.Cloths[0] = sidecar.Cloths[0] with { WindFrequency = 3.5f };
        BphysicsService.Save(target, sidecar);
        loaded = BphysicsService.Load(target);
        Require(loaded.SubWindSpeed == 50f && loaded.SubWindFrequency == .8f && loaded.Cloths[0].WindFrequency == 3.5f,
            "Loading existing authored wind values substituted new-export defaults");
        var saved = File.ReadAllBytes(target);
        sidecar.Cloths[0] = sidecar.Cloths[0] with { WindDrag = float.NaN };
        try { BphysicsService.Save(target, sidecar); throw new Exception("Invalid wind values were saved"); } catch (InvalidOperationException) { }
        Require(saved.SequenceEqual(File.ReadAllBytes(target)), "Invalid export replaced the prior file");
        var before = session.Document.CaptureState();
        var window = new BphysicsExportWindow(session.Document, "Test"); window.Show(); Dispatcher.UIThread.RunJobs(); window.Import(profiles); Dispatcher.UIThread.RunJobs();
        var frequency = window.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "BphysicsWindfrequency0");
        Require(frequency.Value == 4.5m, "Import silently applied an uncalibrated frequency/drag conversion");
        Require(window.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "SubWindSpeed").Value == 25m,
            "Export dialog or reaction import changed the balanced shared wind speed");
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Use reaction frequency"));
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Require(frequency.Value == 4m, "Selected reaction frequency was not copied");
        using (var shot = window.CaptureRenderedFrame()!) shot.Save(Path.Combine(output, "bphysics-export.png"));
        window.Close(); Require(session.Document.CaptureState() == before, "Sidecar export edited cloth physics");
        Console.WriteLine("PASS: reaction references/inheritance, absent defaults, unsafe/missing resources, BPHYSICS round trips, failed-write preservation, export UI and source isolation.");
    }
    public static void Native(string output, string[] actors)
    {
        Directory.CreateDirectory(output);
        foreach (var path in actors)
        {
            var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
            var profiles = TotkClothReactionProfiles.Load(path);
            foreach (var p in profiles.Cloths.Values) Console.WriteLine($"{Path.GetFileName(path)}: {p.Name} -> {p.BaseBone} / {p.Resource}; {p.Reactions.Count} reactions; base frequency {p.Base.Frequency}");
            if (Path.GetFileName(path) == "Armor_1141_Upper.pack.zs")
            {
                Require(profiles.Cloths.Count == 5, "1141 cloth count incorrect");
                var ribbon = profiles.Cloths["SimulationMesh_Ribon_1141"];
                Require(ribbon.BaseBone == "Clavicle_R" && ribbon.Resource.StartsWith("Link_Muffler_A.") && ribbon.Reactions[1].Frequency == 2f,
                    "1141 ribbon resolved the wrong binding or reaction");
                Require(profiles.Cloths["SimulationMesh_Sleeve_L_1141"].Base.Frequency == 0f, "Inherited Tunic default missing");
            }
            var resources = new BotwActorResources(path);
            foreach (var file in resources.PhysicsFiles.Where(p => p.EndsWith(".bphcl")))
            {
                var temporary = Path.Combine(output, Path.GetFileName(file));
                File.WriteAllBytes(temporary, resources.Read(file)!);
                var source = new HkclService(); source.Load(temporary); var state = source.CaptureState();
                var document = source.CreateBphysicsDocument(Path.ChangeExtension(Path.GetFileName(file), ".hkcl"));
                var matched = 0;
                for (var i = 0; i < document.Cloths.Count; i++)
                {
                    var p = document.Cloths[i];
                    if (!profiles.Cloths.TryGetValue(p.Name, out var reaction)) continue;
                    if (!source.GetBoneRows(i).Any(b => TotkClothReactionProfiles.NormalizeName(b.Name) == reaction.BaseBone)) continue;
                    document.Cloths[i] = p with { BaseBone = reaction.BaseBone }; matched++;
                }
                Require(matched > 0, "No native reaction bound to the BPHCL skeleton");
                var sidecar = Path.ChangeExtension(temporary, ".bphysics"); BphysicsService.Save(sidecar, document);
                Require(document.Cloths.SequenceEqual(BphysicsService.Load(sidecar).Cloths), "Native sidecar lost cloth/base/wind data");
                Require(source.CaptureState() == state, "Native sidecar export changed BPHCL data");
                Console.WriteLine($"SIDECAR: {matched}/{document.Cloths.Count} native base bones matched and round-tripped.");
            }
            Require(before.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), "Actor file was changed");
        }
        Console.WriteLine("PASS: native actor reactions and source preservation.");
    }
    public static void InspectBotw(string path)
    {
        var resources = new BotwActorResources(path);
        var root = BotwAampReader.Read(resources.PhysicsParameters!).Root;
        foreach (var obj in BotwActorResources.Objects(root).Where(o => o.Find("wind_drag") != null))
            Console.WriteLine($"{obj.Find("name")?.AsString()}: min={obj.Find("wind_min_speed")?.AsSingle()}, mind={obj.Find("wind_mind_speed")?.AsSingle()}, max={obj.Find("wind_max_speed")?.AsSingle()}, drag={obj.Find("wind_drag")?.AsSingle()}");
    }
    public static void Survey(string root, string output)
    {
        Directory.CreateDirectory(output);
        var codec = new TotkResourceCompression();
        var reactions = new Dictionary<string, string>();
        var references = new List<object>();
        var errors = new List<string>();
        var count = 0;
        var files = Directory.EnumerateFiles(Path.Combine(root, "Pack", "Actor"), "*.pack.zs")
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "Pack"), "*.pack.zs").Where(p => Path.GetFileName(p).StartsWith("Bootup") || Path.GetFileName(p).StartsWith("ResidentCommon")));
        foreach (var file in files)
        {
            try
            {
                var pack = BotwSarcArchive.Parse(codec.Decode(File.ReadAllBytes(file), [root]));
                foreach (var entry in pack.Entries.Where(e => e.Name.Contains("ClothReaction/") || e.Name.Contains("ClothParam/") || e.Name.Contains("ControllerSetParam/") || e.Name.Contains("Component/Physics/")))
                {
                    if (!entry.Name.EndsWith(".bgyml")) continue;
                    var data = Byml.FromBinary(pack.Read(entry).ToArray());
                    if (entry.Name.Contains("ClothReaction/"))
                    {
                        var yaml = data.ToYaml();
                        if (reactions.TryGetValue(entry.Name, out var old) && old != yaml) errors.Add("Conflicting reaction: " + entry.Name);
                        reactions[entry.Name] = yaml;
                    }
                    else if (Path.GetFileName(file) is "Armor_1141_Upper.pack.zs" or "Item_Parasail.pack.zs" or "Npc_Musician_001.pack.zs" || Path.GetFileName(file).StartsWith("Bootup") || Path.GetFileName(file).StartsWith("ResidentCommon"))
                        references.Add(new { Actor = Path.GetFileName(file), entry.Name, Yaml = data.ToYaml() });
                }
            }
            catch (Exception ex) { errors.Add(Path.GetFileName(file) + ": " + ex.Message); }
            if (++count % 1000 == 0) Console.WriteLine($"Surveyed {count} actor packs; {reactions.Count} reaction resources.");
        }
        var json = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(output, "reactions.json"), JsonSerializer.Serialize(reactions, json));
        File.WriteAllText(Path.Combine(output, "references.json"), JsonSerializer.Serialize(references, json));
        File.WriteAllText(Path.Combine(output, "errors.json"), JsonSerializer.Serialize(errors, json));
        Console.WriteLine($"SURVEY: {count} packs, {reactions.Count} distinct reactions, {errors.Count} errors.");
    }
}
