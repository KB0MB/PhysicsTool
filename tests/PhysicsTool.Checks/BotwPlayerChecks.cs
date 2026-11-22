using HKCLTool;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using PhysicsTool.Desktop;
using PhysicsTool.Core;

internal static class BotwPlayerChecks
{
    internal static void Run(string root)
    {
        PhysicsTool.Desktop.Rendering.PortableModelImporter.ConfigureBfres();
        var title = Path.Combine(root, "Pack", "TitleBG.pack");
        const string entry = "Actor/Pack/GameROMPlayer.sbactorpack";
        var player = new BotwActorResources(title, [root], entry);
        var catalog = new PlayerArmorCatalog(player);
        var weapons = new PlayerWeaponCatalog(player, catalog.Sources);
        Require(catalog.Entries.Count > 100 && Enum.GetValues<PlayerArmorSlot>().All(s => catalog.Default(s)?.Name == "Armor_Default_" + s), "BotW default armor catalog");
        Require(Enum.GetValues<PlayerWeaponKind>().All(k => weapons.Entries.Any(e => e.Kind == k)), "five weapon categories");
        var visibility = player.ReadEquipmentVisibility();
        Require(visibility.Groups["G_Head_All"].Contains("Mt_Head") && visibility.Groups["G_Upper"].Contains("Mt_Upper_Skin"), "native BFRES material groups");
        using (var scene = BotwActorScene.Load(title, [root], actorEntry: entry))
        {
            Require(scene.EquippedArmor.Count == 3 && scene.RestModel.Meshes.Any(m => m.Name.EndsWith("__Mt_Hair")), "default hair loaded");
            foreach (var mesh in scene.RestModel.Meshes) Console.WriteLine("MESH " + mesh.Name + " TEXTURE " + mesh.Texture?.Name);
            Require(scene.RestModel.Meshes.Count(m => m.Name.EndsWith("__Mt_Lower_Skin")) <= 1, "default trousers do not duplicate underlying skin");
            var head = scene.RestModel.Meshes.First(m => m.Name.EndsWith("__Mt_Head"));
            Require(head.Material.AmbientOcclusionUvSet == "_u1", "head AO UV routing");
            Console.WriteLine($"DEFAULT {scene.RestModel.Meshes.Count} meshes, {scene.ClothCount} cloths, {scene.Animations.Count} animations");
        }
        var armor = Enum.GetValues<PlayerArmorSlot>().ToDictionary(s => s, s => "Armor_001_" + s);
        using (var snow = BotwActorScene.Load(title, [root], actorEntry: entry,
            armor: Enum.GetValues<PlayerArmorSlot>().ToDictionary(s => s, s => "Armor_009_" + s)))
        {
            for (int m = 0; m < snow.RestModel.Meshes.Count; m++)
            {
                var rest = snow.RestModel.Meshes[m]; var current = snow.CurrentModel.Meshes[m];
                float max = 0;
                for (int v = 0; v < rest.Vertices.Length; v += 8)
                    max = Math.Max(max, System.Numerics.Vector3.Distance(new(rest.Vertices[v], rest.Vertices[v+1], rest.Vertices[v+2]), new(current.Vertices[v], current.Vertices[v+1], current.Vertices[v+2])));
                Require(max < .0001f, $"Armor 009 rest-pose support displaced {rest.Name} by {max}");
            }
            foreach (var bone in snow.CurrentModel.Bones)
            {
                var rest = snow.RestModel.Bones.First(b => b.Index == bone.Index);
                Require(System.Numerics.Vector3.Distance(rest.Position, bone.Position) < .0001f,
                    "Armor 009 rest-pose support displaced " + bone.Name);
            }
            var animation = snow.Animations.FirstOrDefault(a => a.Name == "Wait") ?? snow.Animations.First();
            foreach (var simulate in new[] { false, true })
                for (int frame = 0; frame < 30; frame++)
                    Require(snow.Update(animation, frame, simulate, 1f / 60, frame == 0)
                        .Meshes.All(m => m.Vertices.All(float.IsFinite)), "finite Armor 009 animated support and cloth");
            Console.WriteLine("PASS: Armor 009 support preserves the complete rest mesh and animates with/without simulation.");
        }
        var gear = new Dictionary<PlayerWeaponKind, PlayerWeaponSelection> { [PlayerWeaponKind.Sword] = new("Weapon_Sword_001", true),
            [PlayerWeaponKind.Shield] = new("Weapon_Shield_001", true), [PlayerWeaponKind.Bow] = new("Weapon_Bow_001") };
        using (var scene = BotwActorScene.Load(title, [root], actorEntry: entry, armor: armor, weapons: gear))
        {
            Require(!scene.RestModel.Meshes.Any(m => m.Name == "Skin__Mt_Upper_Skin__Mt_Upper_Skin"), "armor hides Link's underlying upper skin");
            Require(!scene.RestModel.Meshes.Any(m => m.Name.EndsWith("__Mt_Hair")), "head armor hides default hair");
            Require(scene.RestModel.Meshes.Where(m => m.Name.Contains("Hair") || m.Name.Contains("Skin")).All(m => m.Texture != null), "equipment resolves shared Link textures");
            foreach (var name in new[] { "Weapon_Sword_001", "Weapon_Shield_001", "Weapon_Bow_001", "Weapon_Sheath_001", "Weapon_Quiver_001" })
                Require(scene.EquipmentAttachments.Values.Any(a => a.Actor == name), "equipment attachment " + name);
            var animation = scene.Animations.FirstOrDefault(a => a.Name == "Wait") ?? scene.Animations.First();
            for (int frame = 0; frame < 12; frame++)
            {
                var posed = scene.Update(animation, frame, true, 1f / 60, frame == 0);
                Require(posed.Meshes.All(m => m.Vertices.All(float.IsFinite)), "finite animated equipment");
            }
            Console.WriteLine($"ARMOR {scene.RestModel.Meshes.Count} meshes, {scene.ClothCount} cloths, {scene.EquipmentAttachments.Count} attachments");
            foreach (var warning in scene.Resources.Warnings.Where(w => w.Contains("Missing equipment") || w.Contains("attachment bone"))) throw new Exception(warning);
        }
        gear.Clear(); gear.Add(PlayerWeaponKind.Bow, new("Weapon_Bow_001", true));
        using (var scene = BotwActorScene.Load(title, [root], actorEntry: entry, weapons: gear))
        {
            Require(scene.EquipmentAttachments.Values.Any(a => a.Actor == "NormalArrow" && a.Bone == "Weapon_R"), "drawn bow arrow");
            Require(scene.EquipmentAttachments.Values.Any(a => a.Actor == "Weapon_Bow_001" && a.Bone == "Weapon_L"), "drawn bow left hand");
        }
        Console.WriteLine("PASS: BotW player defaults, armor hiding, all weapon categories, sheath/quiver/arrow and animated physics.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static void PhysicsUi(string actorPath, string output)
    {
        Directory.CreateDirectory(output);
        var resources = new BotwActorResources(actorPath, actorEntry: Path.GetFileName(actorPath) == "TitleBG.pack" ? "Actor/Pack/GameROMPlayer.sbactorpack" : null);
        var entries = ActorResourceExport.List([resources]).Where(e => e.Kind != ActorExportKind.Bfres).ToArray();
        Require(entries.Any(e => e.Kind == ActorExportKind.Cloth), "physics dialog contains cloth");
        var window = new ActorPhysicsWindow(resources.ActorName, entries);
        window.Show(); Dispatcher.UIThread.RunJobs();
        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        var export = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportActorPhysics");
        var view = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ViewActorPhysics");
        Require(list.SelectedIndex == 0 && export.IsEnabled && view.IsEnabled, "initial physics selection and actions");
        list.SelectedIndex = -1;
        Require(!export.IsEnabled && view.IsEnabled, "view remains available without a selected export");
        list.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
        using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "physics-dialog.png"));
        foreach (var entry in entries)
        {
            var file = Path.Combine(output, entry.FileName);
            ActorResourceExport.Save(entry, file);
            Require(File.ReadAllBytes(file).AsSpan().SequenceEqual(resources.Read(entry.Resource)), "export preserves native physics bytes");
        }
        view.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Require(!window.IsVisible, "view dismisses physics dialog");
        Console.WriteLine($"PASS: {resources.ActorName} physics dialog, selection, native export and View actor action.");
    }
    internal static void Inspect(string root)
    {
        var title = Path.Combine(root, "Pack", "TitleBG.pack");
        string Label(uint hash) => hash.ToString("x8");
        var archive = BotwSarcArchive.OpenIndexed(title);
        foreach (var entry in archive.Entries.Where(e => e.Name.Contains("GameROMPlayer") || e.Name.Contains("Armor_Default") || e.Name.Contains("Armor_001") || e.Name.Contains("Weapon_Sword_001")))
            Console.WriteLine("TITLE " + entry.Name);
        foreach (string name in new[] { "GameROMPlayer", "Armor_Default_Head", "Armor_Default_Upper", "Armor_Default_Lower", "Armor_001_Head", "Armor_001_Upper", "Weapon_Sword_001", "Weapon_Bow_001", "Weapon_Shield_001" })
        {
            string path = Path.Combine(root, "Actor", "Pack", name + ".sbactorpack");
            string? sourceEntry = null;
            if (!File.Exists(path)) { path = title; sourceEntry = "Actor/Pack/" + name + ".sbactorpack"; }
            if (sourceEntry != null && !archive.Entries.Any(e => e.Name == sourceEntry)) continue;
            var resources = new BotwActorResources(path, [root], sourceEntry);
            Console.WriteLine("ACTOR " + name);
            var pack = sourceEntry == null ? BotwSarcArchive.OpenIndexed(path) : BotwSarcArchive.Parse(archive.Read(archive.Entries.Single(e => e.Name == sourceEntry)).Span);
            foreach (var p in pack.Entries.Where(e => e.Name.EndsWith(".bgparamlist") || e.Name.EndsWith(".bmodellist")))
                foreach (var obj in BotwActorResources.Objects(BotwAampReader.Read(pack.Read(p).ToArray()).Root))
                {
                    Console.WriteLine("OBJECT " + Label(obj.Hash));
                    foreach (var param in obj.Parameters)
                        Console.WriteLine($"PARAM {Label(param.Hash)} {param.Type} " + (param.Type is 7 or 8 or 15 or 20 ? param.AsString() : Convert.ToHexString(param.Bytes)));
                }
            foreach (var reference in resources.Models)
            {
                Console.WriteLine("MODEL " + reference);
                var document = new ToolboxBfresDocument(resources.ReadModel(reference.Resource)!);
                foreach (var prop in document.Resource.GetType().GetProperties().Where(p => p.Name.Contains("Vis") || p.Name.Contains("User") || p.Name.Contains("Anim")))
                {
                    Console.WriteLine("RESOURCE " + prop.Name);
                    try { foreach (dynamic item in ToolboxBfresDocument.Items(prop.GetValue(document.Resource))) Console.WriteLine("  " + item.Name); } catch { }
                }
                foreach (dynamic model in document.Models)
                {
                    Console.WriteLine("FMODEL " + model.Name);
                    var userdata = ((object)model).GetType().GetProperty(document.IsSwitch ? "UserData" : "UserData")?.GetValue(model);
                    foreach (dynamic value in ToolboxBfresDocument.Items(userdata))
                    {
                        Console.WriteLine("USER " + value.Name + " " + value.Type);
                        foreach (var method in ((object)value).GetType().GetMethods().Where(m => m.Name == "GetValueStringArray" && m.GetParameters().Length == 0))
                            try { Console.WriteLine(string.Join("|", (string[])method.Invoke(value, null)!)); } catch { }
                    }
                    foreach (dynamic material in ToolboxBfresDocument.Items((object)model.Materials))
                    foreach (dynamic value in ToolboxBfresDocument.Items(((object)material).GetType().GetProperty(document.IsSwitch ? "UserDatas" : "UserData")?.GetValue(material)))
                        Console.WriteLine("MATUSER " + material.Name + " " + value.Name + "=" + string.Join("|", (string[])value.GetValueStringArray()));
                }
            }
        }
    }
}
