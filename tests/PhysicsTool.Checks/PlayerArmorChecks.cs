using HKCLTool;
using BymlLibrary;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Input;
using System.Numerics;
using PhysicsTool.Desktop;

internal static class PlayerArmorChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Ui(string path, string output)
    {
        Directory.CreateDirectory(output);
        var catalog = new PlayerArmorCatalog(new(path));
        Require(catalog.Entries.Any(e => e.Name == "Armor_1151_Head") && catalog.RightHandPath != null, "Default head alternative or arm actor missing");
        var equipped = Enum.GetValues<PlayerArmorSlot>().ToDictionary(s => s, PlayerArmorCatalog.DefaultName);
        foreach (var language in new[] { "en", "fr", "es", "ja" })
        {
            UiLanguage.Initialize(language);
            var calls = new List<Dictionary<PlayerArmorSlot, string>>();
            var weapons = new PlayerWeaponCatalog(new(path));
            var weaponCalls = new List<Dictionary<PlayerWeaponKind, PlayerWeaponSelection>>();
            var window = new PlayerArmorWindow(catalog, equipped, (choice, _) => { calls.Add(new(choice)); return Task.CompletedTask; }, weapons, null,
                (choice, gear, _) => { calls.Add(new(choice)); weaponCalls.Add(new(gear)); return Task.CompletedTask; });
            window.Width = 480; window.Show(); Dispatcher.UIThread.RunJobs();
            var selectors = window.GetVisualDescendants().OfType<ComboBox>().ToArray();
            Require(selectors.Length == 3 && selectors.All(s => s.SelectedItem is PlayerArmorEntry), "Armor slots missing defaults");
            var accept = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ApplyPlayerArmor");
            Require(!accept.IsEnabled, "Unchanged armor can be applied");
            var upper = selectors.Single(s => s.Name == "PlayerArmorUpper");
            upper.Focus();
            upper.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "116" });
            Dispatcher.UIThread.RunJobs();
            Require(upper.SelectedItem is PlayerArmorEntry typed && typed.Name == "Armor_116_Upper", "Typing the armor number does not jump to it");
            Require(upper.Items.Cast<PlayerArmorEntry>().All(e => e.Slot == PlayerArmorSlot.Upper), "Wrong armor in upper slot");
            var numbers = upper.Items.Cast<PlayerArmorEntry>().Select(e => PlayerArmorCatalog.SortNumber(e.Name)).ToArray();
            Require(numbers.SequenceEqual(numbers.Order()), "Armor dropdown reverted to alphabetical ordering");
            upper.SelectedItem = catalog.Find(PlayerArmorSlot.Upper, "Armor_001_Upper");
            Require(accept.IsEnabled, "Changed armor cannot be applied");
            accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Require(calls.Count == 1 && calls[0][PlayerArmorSlot.Head] == "Armor_999_Head" && calls[0][PlayerArmorSlot.Upper] == "Armor_001_Upper" && !accept.IsEnabled, "Armor picker changed unselected slots or failed to apply");
            using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "armor-" + language + ".png"));
            var defaults = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, UiLanguage.Text("Defaults")));
            defaults.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Require(accept.IsEnabled && selectors.All(s => ((PlayerArmorEntry)s.SelectedItem!).Name == equipped[((PlayerArmorEntry)s.SelectedItem!).Slot]), "Defaults did not restore all three slots");
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            var panel = window.GetVisualDescendants().OfType<PlayerWeaponsPanel>().Single();
            var combos = panel.GetVisualDescendants().OfType<ComboBox>().ToDictionary(c => c.Name!);
            ComboBox Actor(PlayerWeaponKind kind) => combos["PlayerWeapon" + kind];
            ComboBox State(PlayerWeaponKind kind) => combos["PlayerWeaponState" + kind];
            Require(panel.Selection.Count == 0 && Enum.GetValues<PlayerWeaponKind>().All(k => !State(k).IsEnabled && State(k).SelectedIndex == 0), "Weapons not empty/unequipped by default");
            Actor(PlayerWeaponKind.Sword).SelectedItem = weapons.Find(PlayerWeaponKind.Sword, "Weapon_Sword_070");
            State(PlayerWeaponKind.Sword).SelectedIndex = 1;
            Actor(PlayerWeaponKind.Bow).SelectedItem = weapons.Find(PlayerWeaponKind.Bow, "Weapon_Bow_001");
            State(PlayerWeaponKind.Bow).SelectedIndex = 1;
            Require(State(PlayerWeaponKind.Sword).SelectedIndex == 0 && panel.Selection[PlayerWeaponKind.Bow].Equipped, "Bow did not free both hands");
            Actor(PlayerWeaponKind.LSword).SelectedItem = weapons.Find(PlayerWeaponKind.LSword, "Weapon_LSword_001");
            Require(!panel.Selection.ContainsKey(PlayerWeaponKind.Sword), "Melee types stack in same slot");
            State(PlayerWeaponKind.LSword).SelectedIndex = 1;
            Require(State(PlayerWeaponKind.Bow).SelectedIndex == 0, "Drawn melee did not stow bow");
            accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Require(weaponCalls.Last().Count == 2 && !accept.IsEnabled, "Weapon choices not applied atomically");
            using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "weapons-" + language + ".png"));
            defaults.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Require(panel.Selection.Count == 0 && accept.IsEnabled, "Defaults did not clear equipment");
            window.Close();
        }
        UiLanguage.Initialize("en");
        Console.WriteLine("PASS: armor picker slots, validation, apply, defaults, compact layout and four UI languages.");
    }
    public static void Synthetic()
    {
        var owner = ActorEquipmentVisibility.Read(new(Byml.FromText("HiddenMaterialGroupList:\n- GroupName: Body\n  MaterialNameList: [Skin, Belt]\n- GroupName: Hair\n  MaterialNameList: [Hair]\n").GetMap()));
        var armor = ActorEquipmentVisibility.Read(new(Byml.FromText("HideMaterialGroupNameList: [Body, Missing]\n").GetMap()));
        Require(owner.HiddenBy([armor]).SetEquals(["Skin", "Belt"]), "Equipment did not resolve exact hide-group materials");
        Require(owner.HiddenBy([]).Count == 0 && armor.HiddenBy([owner]).Count == 0, "Hide requests leaked across owners");
        Require(PlayerArmorCatalog.DefaultName(PlayerArmorSlot.Head) == "Armor_999_Head", "Default head is not the loose-hair actor");
        Require(PlayerArmorCatalog.DefaultName(PlayerArmorSlot.Upper) == "Armor_Default_Upper" && PlayerArmorCatalog.DefaultName(PlayerArmorSlot.Lower) == "Armor_Default_Lower", "Default armor IDs changed");
        var ordered = new[] { "Armor_1000_Head", "Armor_999_Head", "Armor_116_Head", "Armor_001_Head", "Armor_1151_Head" }.OrderBy(PlayerArmorCatalog.SortNumber);
        Require(ordered.SequenceEqual(new[] { "Armor_001_Head", "Armor_116_Head", "Armor_999_Head", "Armor_1000_Head", "Armor_1151_Head" }), "Armor ordering is not numeric");
        var aliases = new ParticlePreviewData();
        foreach (var name in new[] { "Link:Head", "Link:Waist", "A:Finger", "Unknown:Finger", "Link:Missing" }) aliases.Bones.Add(new() { Name = name });
        BotwActorScene.BindPreviewBoneNames(aliases, new[] { "Head", "Waist", "A:Finger", "B:Finger" }.Select(n => new VisualModelBone { Name = n }));
        Require(aliases.Bones.Select(b => b.Name).SequenceEqual(new[] { "Head", "Waist", "A:Finger", "Unknown:Finger", "Link:Missing" }), "Actor bone alias resolution is missing or ambiguous");
        Console.WriteLine("PASS: armor material hide groups, owner isolation, and default actor IDs.");
    }
    public static void Native(string path, string? set = null)
    {
        PhysicsTool.Desktop.Rendering.PortableModelImporter.ConfigureBfres();
        var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
        var ids = set?.Split(',');
        var armor = set == null ? null : Enum.GetValues<PlayerArmorSlot>().ToDictionary(s => s, s => "Armor_" + ids![ids.Length == 3 ? (int)s : 0] + "_" + s);
        using var scene = BotwActorScene.Load(path, progress: new Progress<string>(Console.WriteLine), armor: armor);
        Console.WriteLine($"OUTFIT {string.Join(", ", scene.EquippedArmor.Values)}: {scene.RestModel.Meshes.Count} meshes, {scene.ClothCount} cloths, {scene.Animations.Count} animations");
        foreach (var mesh in scene.RestModel.Meshes) Console.WriteLine("MESH " + mesh.Name);
        foreach (var warning in scene.Resources.Warnings.Distinct()) Console.WriteLine("WARNING " + warning);
        if (scene.EquippedArmor.Count != 3 || scene.ClothCount == 0 || scene.RestModel.Meshes.Count == 0) throw new Exception("Player outfit is incomplete");
        Require(scene.RestModel.Meshes.Any(m => m.Name.Contains("Mt_Upper_Skin_RaulSkin_A")), "Player right hand is missing");
        if (set == "001") Require(!scene.RestModel.Meshes.Any(m => m.Name.Contains("Mt_Upper_Skin_RaulSkin_B") || m.Name.Contains("Mt_Upper_Skin_RaulSkin_C")), "Sleeves did not hide the covered right-arm sections");
        var clip = scene.Animations.FirstOrDefault(a => a.Name == "Assassin_DotonCharge") ?? scene.Animations.FirstOrDefault(a => a.Name.Contains("Wait")) ?? scene.Animations.FirstOrDefault();
        for (int frame = 0; frame < 12; frame++) scene.Update(clip, frame, true, 1f / 60, frame == 0);
        var layers = ((System.Collections.IEnumerable)typeof(BotwActorScene).GetField("_layers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(scene)!).Cast<object>().ToArray();
        var outputs = layers.SelectMany(l => { var data = (ParticlePreviewData)l.GetType().GetProperty("Source")!.GetValue(l)!; var solver = (HkclPreviewSimulator)l.GetType().GetProperty("Solver")!.GetValue(l)!; return data.Bones.Where(b => solver.DrivenBoneIndices.Contains(b.Index)).Select(b => b.Name); }).ToArray();
        foreach (var layer in layers)
        {
            var data = (ParticlePreviewData)layer.GetType().GetProperty("Source")!.GetValue(layer)!;
            var solver = (HkclPreviewSimulator)layer.GetType().GetProperty("Solver")!.GetValue(layer)!;
            Console.WriteLine($"LAYER {data.Particles.Count} particles: {string.Join(",", data.Bones.Where(b => solver.DrivenBoneIndices.Contains(b.Index)).Select(b => b.Name))}");
            Console.WriteLine("UNMATCHED " + string.Join(",", data.Bones.Where(b => !scene.RestModel.Bones.Any(m => m.Name == b.Name)).Select(b => b.Name)));
            Require(data.Bones.All(b => scene.RestModel.Bones.Any(m => m.Name == b.Name)), "Outfit cloth has unmatched model bones");
            Require(data.Links.Where(l => l.Kind.Contains("CompressibleLink")).All(l => l.CompressionLength.HasValue && l.RestLength.HasValue && l.Stiffness.HasValue), "Native compressible-link parameters were lost");
        }
        Require(!outputs.GroupBy(n => n).Any(g => g.Count() > 1), "Equipped cloths compete for the same output bones");
        scene.Update(clip, 8, false, 0, true);
        Require(!scene.CurrentModel.Bones.GroupBy(b => b.Name).Any(g => g.Skip(1).Any(b => System.Numerics.Vector3.Distance(b.Position, g.First().Position) > .001)), "Armor and Player animation bones disagree");
        var rest = scene.RestModel.Bones.GroupBy(b => b.Name).ToDictionary(g => g.Key, g => g.First());
        var services = (List<HkclService>)typeof(BotwActorScene).GetField("_supports", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(scene)!;
        scene.Update(clip, 24, false, 0, true);
        foreach (var service in services)
        {
            var evaluation = service.EvaluateSidecarPose(scene.CurrentModel.Bones, scene.RestModel.Bones);
            Require(evaluation.MissingDriverBones.Count == 0, "Equipped helper driver is unbound");
            Require(evaluation.Poses.Where(p => p.IsDriven).All(p => scene.CurrentModel.Bones.Any(b => b.Name == p.Name)), "Equipped helper output is unbound");
        }
        var maxAnchorError = 0f;
        var movedHair = false;
        var movedSkirt = false;
        for (var frame = 0; frame < 90; frame++)
        {
            scene.Update(clip, frame % Math.Max(1, clip!.FrameCount), true, 1f / 60, frame == 0);
            var current = scene.CurrentModel.Bones.GroupBy(b => b.Name).ToDictionary(g => g.Key, g => g.First());
            foreach (var layer in layers)
            {
                var data = (ParticlePreviewData)layer.GetType().GetProperty("Source")!.GetValue(layer)!;
                var solver = (HkclPreviewSimulator)layer.GetType().GetProperty("Solver")!.GetValue(layer)!;
                var positions = solver.GetPositions();
                Require(solver.NumericalResetCount == 0 && positions.Values.All(p => float.IsFinite(p.LengthSquared())), "Outfit simulation required a numerical reset");
                foreach (var p in data.Particles.Where(p => p.Fixed))
                {
                    var weights = data.ParticleAttachments.Single(a => a.ParticleIndex == p.Index).BoneWeights;
                    var expected = Vector3.Zero;
                    foreach (var w in weights)
                    {
                        var name = data.Bones.Single(b => b.Index == w.BoneIndex).Name;
                        Matrix4x4.Invert(BotwActorScene.Matrix(rest[name]), out var inverse);
                        expected += Vector3.Transform(w.ReferencePosition ?? p.Position, inverse * BotwActorScene.Matrix(current[name])) * w.Weight;
                    }
                    maxAnchorError = Math.Max(maxAnchorError, Vector3.Distance(expected / weights.Sum(w => w.Weight), positions[p.Index]));
                }
                foreach (var b in data.Bones.Where(b => solver.DrivenBoneIndices.Contains(b.Index)))
                {
                    var moved = Vector3.Distance(current[b.Name].Position, rest[b.Name].Position) > .01f;
                    movedHair |= moved && b.Name.StartsWith("Hair_");
                    movedSkirt |= moved && b.Name.StartsWith("Skirt_");
                }
            }
        }
        Console.WriteLine($"ANCHORS max error={maxAnchorError:F7}; moving hair={movedHair}, skirt={movedSkirt}");
        Require(maxAnchorError < .0001f, "Outfit anchors did not follow the animated model");
        if (set == "1151,116,001") Require(movedHair && movedSkirt, "Reported head/upper outfit does not animate its cloth outputs");
        Require(!scene.Resources.Warnings.Any(w => w.Contains("hclCompressibleLinkConstraintSet")), "Compressible-link cloth was skipped");
        if (scene.Overlay.Particles.Any(p => !float.IsFinite(p.Position.LengthSquared()))) throw new Exception("Nonfinite player simulation");
        Require(hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), "Player source pack was modified");
        Console.WriteLine("PASS: Player default armor and simulation.");
    }
    public static void Inspect(string path)
    {
        var resources = new BotwActorResources(path);
        Console.WriteLine($"ACTOR {resources.ActorName}: {string.Join(", ", resources.Models)}");
        foreach (var component in new[] { "WeaponRef", "BowRef", "ShieldRef", "SheathRef", "EquipmentUserRef" })
            Console.WriteLine("RESOLVED " + component + "\n" + new Byml(resources.EquipmentParameters(component)).ToYaml());
        var pack = BotwSarcArchive.Parse(new TotkResourceCompression().Decode(File.ReadAllBytes(path), resources.Roots));
        foreach (var e in pack.Entries.Where(e => e.Name.EndsWith(".bgyml") && (e.Name.StartsWith("Actor/") || e.Name.Contains("ModelInfo") || e.Name.Contains("ArmorParam") || e.Name.Contains("EquipmentUserParam") || e.Name.Contains("Attachment") || e.Name.Contains("ActorPositionCalculator") || e.Name.Contains("WeaponParam") || e.Name.Contains("BowParam") || e.Name.Contains("ShieldParam"))))
            Console.WriteLine(e.Name + "\n" + Byml.FromBinary(pack.Read(e).ToArray()).ToYaml());
    }
}
