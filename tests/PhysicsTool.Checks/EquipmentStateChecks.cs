using HKCLTool;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhysicsTool.Desktop;
using System.Reflection;

internal static class EquipmentStateChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static void Ui(string path)
    {
        UiLanguage.Initialize("en");
        var window = new MainWindow(graphics: false); window.Show();
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        void Wait(Task task)
        {
            // Headless RunJobs drains timer work without the real input queue's fairness.
            typeof(MainWindow).GetField("_timelineScrubbing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            Field<DispatcherTimer>("_actorTimer").Stop();
            var timeout = DateTime.UtcNow.AddMinutes(2);
            while (!task.IsCompleted && DateTime.UtcNow < timeout) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            typeof(MainWindow).GetField("_timelineScrubbing", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
            Require(task.IsCompleted, "Actor UI task timed out"); task.GetAwaiter().GetResult();
        }
        Wait(window.LoadActorPathAsync(path, entry: Path.GetFileName(path) == "TitleBG.pack" ? "Actor/Pack/GameROMPlayer.sbactorpack" : null));
        var scene = Field<BotwActorScene>("_actorScene");
        var clips = Field<ComboBox>("_animations");
        var play = Field<Button>("_actorPlay");
        var clock = Field<DispatcherTimer>("_actorTimer");
        var idle = scene.Animations.FindIndex(a => a.Name.Equals("Nml_Wait", StringComparison.OrdinalIgnoreCase));
        Require(idle >= 0, "No wait animation fixture");
        clips.SelectedIndex = idle + 1;
        play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Field<bool>("_actorPlaying"), "Cannot start playback");
        clips.SelectedIndex = idle == 0 ? 2 : 1;
        Require(Field<bool>("_actorPlaying") && Field<double>("_actorFrame") == 0 && clock.IsEnabled, "Changing clip stopped playback");
        play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        clips.SelectedIndex = idle + 1;
        Require(!Field<bool>("_actorPlaying"), "Selecting a clip started paused playback");
        play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        clips.SelectedIndex = 0;
        Require(!Field<bool>("_actorPlaying"), "Rest pose left playback running");
        clips.SelectedIndex = idle + 1;
        var selection = new Dictionary<PlayerWeaponKind, PlayerWeaponSelection> { [PlayerWeaponKind.Sword] = new("Weapon_Sword_001") };
        Wait(window.SwapPlayerEquipmentAsync(scene.EquippedArmor, selection, CancellationToken.None));
        Require(ReferenceEquals(scene, Field<BotwActorScene>("_actorScene")), "Weapon selection replaced whole scene");
        var camera = window.Viewport.CameraState;
        typeof(MainWindow).GetField("_actorPlaying", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        typeof(MainWindow).GetField("_actorFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 12.0);
        selection[PlayerWeaponKind.Sword] = new("Weapon_Sword_001", true);
        Wait(window.SwapPlayerEquipmentAsync(scene.EquippedArmor, selection, CancellationToken.None));
        Require(ReferenceEquals(scene, Field<BotwActorScene>("_actorScene")), "Equip toggle replaced whole scene");
        Require(Field<bool>("_actorPlaying") && Field<double>("_actorFrame") >= 12 && clips.SelectedIndex == idle + 1,
            $"Equip toggle reset playback: playing={Field<bool>("_actorPlaying")}, frame={Field<double>("_actorFrame")}, selected={clips.SelectedIndex}/{idle+1}, length={scene.Animations[idle].FrameCount}, status={Field<TextBlock>("_status").Text}");
        Require(window.Viewport.CameraState == camera, "Equip toggle changed camera");
        Require(scene.EquipmentAttachments.Values.Any(a => a.Actor == "Weapon_Sword_001" && a.Bone == "Weapon_R"), "Equip toggle failed to bind the hand");
        clock.Stop(); window.Close();
        Console.WriteLine("PASS: playing/paused clip switching, rest pose, equipment-only refresh, camera and playback retention.");
    }
    internal static void Check(string root)
    {
        PhysicsTool.Desktop.Rendering.PortableModelImporter.ConfigureBfres();
        var path = Path.Combine(root, "Pack/TitleBG.pack");
        var player = new BotwActorResources(path, actorEntry: "Actor/Pack/GameROMPlayer.sbactorpack");
        var sources = new PlayerEquipmentSources(player);
        foreach (string name in new[] { "Weapon_Sword_023", "Weapon_Bow_023", "Weapon_Shield_038", "Weapon_Lsword_023", "Weapon_Spear_023" })
        {
            var resources = sources.Find(name)!.Open(player.Roots);
            var docs = resources.Models.Select(m => m.Resource).Concat(resources.AnimationResources).Distinct()
                .Select(n => (Name: n, Bytes: resources.ReadModel(n))).Where(p => p.Bytes != null)
                .ToDictionary(p => p.Name, p => new ToolboxBfresDocument(p.Bytes!));
            foreach (bool equipped in new[] { false, true })
            {
                var state = new PlayerEquipmentState(resources, docs, equipped);
                Require(state.Animation != null, name + " native state has no skeletal animation");
                Console.WriteLine($"STATE {name} equipped={equipped} {state.Animation.Name}@{state.Frame}; hidden={string.Join(',', state.HiddenMaterials)}");
                if (name == "Weapon_Bow_023")
                    Require(state.Frame == (equipped ? 15 : 1) && state.HiddenMaterials.Contains("Mt_String") != equipped, "Bow native string/pose state wrong");
                else if (!equipped) Require(state.Frame == 0, "Stowed native frame is not closed");
                foreach (var reference in resources.Models)
                {
                    var model = new ToolboxActorModel(docs[reference.Resource], reference.Model, new(), new Dictionary<(string Material, string Sampler), string>(), resources.Warnings, state.HiddenMaterials);
                    state.ApplyMaterials(model, resources.Warnings);
                    foreach (double seconds in new[] { 0.0, .5, 2.0 })
                    {
                        model.AutoMaterials.Sample(seconds);
                        var materials = model.RestPose.Meshes.Where(m => m.Name.Contains("Blade", StringComparison.OrdinalIgnoreCase)).Select(m => m.Material.Botw).Where(m => m != null && m.Parameters.ContainsKey("const_value0")).ToArray();
                        if (name != "Weapon_Bow_023") Require(materials.Length > 0, "Blade state fixture missing");
                        foreach (var material in materials)
                            Require(Math.Abs(material!.Parameters["const_value0"][0] - (equipped ? 1 : 0)) < .001, name + " energy material did not follow native state");
                    }
                }
            }
        }
        var selection = new Dictionary<PlayerWeaponKind, PlayerWeaponSelection>
        {
            [PlayerWeaponKind.Sword] = new("Weapon_Sword_023"),
            [PlayerWeaponKind.Bow] = new("Weapon_Bow_023"),
            [PlayerWeaponKind.Shield] = new("Weapon_Shield_038")
        };
        using var scene = BotwActorScene.Load(path, actorEntry: "Actor/Pack/GameROMPlayer.sbactorpack", weapons: selection);
        var animation = scene.Animations.First(a => a.Name == "Nml_Wait" || a.Name == "Nml_wait");
        var seekWatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 30; i++) scene.Update(animation, i, true, 0, true);
        Console.WriteLine($"SEEK COST: {seekWatch.Elapsed.TotalMilliseconds / 30:F2} ms/frame");
        seekWatch.Restart();
        for (int i = 0; i < 30; i++) scene.Update(animation, i, true, 0, true, deferPhysics: true);
        Console.WriteLine($"DEFERRED SEEK COST: {seekWatch.Elapsed.TotalMilliseconds / 30:F2} ms/frame");
        scene.Update(animation, 12, true, 1f / 60, true);
        var overlay = scene.Overlay;
        var layersField = typeof(BotwActorScene).GetField("_layers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var layers = ((System.Collections.IEnumerable)layersField.GetValue(scene)!).Cast<object>().ToArray();
        var bodyMesh = scene.Meshes.First(m => !m.Name.Contains("Weapon_"));
        scene.SetMeshVisible(bodyMesh.Index, false);
        Require(!scene.RestModel.Meshes.Any(m => m.Name.Contains("Mt_String")), "Stowed bow string still visible");
        foreach (var arrow in new PlayerWeaponCatalog(player).Arrows)
        {
            selection[PlayerWeaponKind.Bow] = new("Weapon_Bow_023", true, arrow.Name);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            scene.ChangeWeapons(selection);
            scene.Update(animation, 12, true, 0);
            Require(ReferenceEquals(scene.Overlay, overlay) && layers.SequenceEqual(((System.Collections.IEnumerable)layersField.GetValue(scene)!).Cast<object>()), "Equipment change rebuilt physics");
            Require(!scene.Meshes.First(m => m.Name == bodyMesh.Name).Visible, "Equipment change reset mesh visibility");
            Require(scene.EquipmentAttachments.Values.Any(a => a.Actor == arrow.Name && a.Bone == "Weapon_R"), "Selected arrow not attached");
            Require(scene.RestModel.Meshes.Any(m => m.Name.Contains("Mt_String")), "Drawn bow string missing");
            if (arrow.Name == "AncientArrow")
            {
                Require(scene.CurrentModel.Meshes.Single(m => m.Name.Contains("Mt_Arrow_007_Body")).Material.Botw!.Parameters["const_value0"][0] > 0,
                    "Ancient arrow external automatic emission was not applied");
                var charge = scene.Animations.First(a => a.Name == "Bow_Attack_Charge");
                var stateField = typeof(BotwActorScene).GetField("_equipmentStates", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var states = (Dictionary<int, PlayerEquipmentState>)stateField.GetValue(scene)!;
                var bowState = states[scene.EquipmentAttachments.Single(p => p.Value.Actor == "Weapon_Bow_023").Key];
                var arrowState = states[scene.EquipmentAttachments.Single(p => p.Value.Actor == "AncientArrow").Key];
                Require(arrowState.ArrowEffect != null, "Native arrow effect missing: " + string.Join("; ", scene.Resources.Warnings));
                var effect = arrowState.ArrowEffect!;
                Console.WriteLine($"EFFECT: {effect.Binding}; textures={effect.Mask.Width}x{effect.Mask.Height}/{effect.Noise.Width}x{effect.Noise.Height}");
                Require(effect.Binding.Bone == "Blade_Base" && effect.Binding.StartFrame == 10, "Wrong native effect binding/event");
                Require(effect.Mask.Pixels.Where((_, i) => i % 4 == 3).Any(a => a == 0) && effect.Mask.Pixels.Where((_, i) => i % 4 == 3).Any(a => a > 200), "Blade alpha cutout was not decoded");
                Console.WriteLine("EFFECT TEXTURES " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(effect.Mask.Pixels)) + " / " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(effect.Noise.Pixels)));
                Require(arrowState.EffectFrameFor(charge, 9) == null && arrowState.EffectFrameFor(charge, 10) == 0, "Effect fired before its native ELink event");
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "ancient-arrow-native-mask.png"), IconToolView.Encode(effect.Mask.Pixels, effect.Mask.Width, effect.Mask.Height, false));
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "ancient-arrow-native-noise.png"), IconToolView.Encode(effect.Noise.Pixels, effect.Noise.Width, effect.Noise.Height, false));
                Require(bowState.PoseFor(charge, 9).Animation?.Name == "Weapon_Bow_Attack_Draw" && bowState.PoseFor(charge, 9).Frame == 9, "Bow draw does not follow Link's timeline");
                Require(arrowState.PoseFor(charge, 9).Animation?.Name == "Blade_Open" && arrowState.PoseFor(charge, 9).Frame == 9, "Ancient arrow does not follow Link's timeline");
                var models = (List<ToolboxActorModel>)typeof(BotwActorScene).GetField("_models", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                foreach (var (actorName, state, materialName) in new[] { ("Weapon_Bow_023", bowState, "Mt_String"), ("AncientArrow", arrowState, "Mt_Arrow_007_Body") })
                {
                    var model = models[scene.EquipmentAttachments.Single(p => p.Value.Actor == actorName).Key];
                    var native = state.PoseFor(charge, 18).Animation!;
                    var before = model.Pose(native, 0).Meshes.Single(m => m.Name.Contains(materialName)).Vertices;
                    var after = model.Pose(native, 18).Meshes.Single(m => m.Name.Contains(materialName)).Vertices;
                    Require(!before.SequenceEqual(after), actorName + " local deformation did not change independently of Link");
                    Console.WriteLine("PASS: local deformation " + actorName + "/" + materialName);
                }
                var initial = scene.Update(charge, 0, false, 0, true).Meshes.SelectMany(m => m.Vertices).ToArray();
                var drawn = scene.Update(charge, 18, false, 0, true).Meshes.SelectMany(m => m.Vertices).ToArray();
                var particle = scene.CurrentModel.Meshes.Single(m => m.Material.Particle != null);
                Require(particle.Material.Particle!.Alpha > 0, "Native blade invisible after trigger");
                var color = particle.Material.Particle.Color0;
                var uv = particle.Material.Particle.Texture1Srt;
                scene.Update(charge, 12, false, 0, true);
                scene.Update(charge, 18, false, 0, true);
                Require(particle.Material.Particle.Color0 == color && particle.Material.Particle.Texture1Srt == uv, "Effect clock is not deterministic when scrubbing");
                Require(!initial.SequenceEqual(drawn), "Bow draw geometry stayed static");
                Require(initial.SequenceEqual(scene.Update(charge, 0, false, 0, true).Meshes.SelectMany(m => m.Vertices)), "Backward bow scrubbing did not restore the initial pose");
                scene.Update(animation, 12, true, 0, true);
                Require(scene.CurrentModel.Meshes.Single(m => m.Material.Particle != null).Material.Particle!.Alpha == 0, "Blade stayed visible after leaving bow animation");
            }
            PlayerWeaponChecks.Verify(scene);
            Console.WriteLine($"PASS: live equipment state with {arrow.DisplayName}, {watch.ElapsedMilliseconds} ms; cloth and mesh visibility retained");
        }
        scene.ChangeWeapons(new Dictionary<PlayerWeaponKind, PlayerWeaponSelection>());
        scene.Update(animation, 12, false, 0);
        Require(scene.EquipmentAttachments.Count == 0, "Cleared weapons remain attached");
        Console.WriteLine("PASS: closed native skeletal/material/visibility states and live equipment changes.");
    }
    internal static void Inspect(string root)
    {
        var player = new BotwActorResources(Path.Combine(root, "Pack/TitleBG.pack"), actorEntry: "Actor/Pack/GameROMPlayer.sbactorpack");
        var sources = new PlayerEquipmentSources(player);
        foreach (string name in new[] { "AncientArrow", "ElectricArrow", "FireArrow", "IceArrow", "BombArrow_A", "NormalArrow" })
        {
            try
            {
            var resources = sources.Find(name)!.Open(player.Roots);
            Console.WriteLine("ACTOR " + name);
            if (name == "AncientArrow")
            {
                var actorPack = (BotwSarcArchive)typeof(BotwActorResources).GetField("_pack", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(resources)!;
                foreach (var entry in actorPack.Entries.Where(e => e.Name.Contains("Effect") || e.Name.Contains("ELink")))
                {
                    var bytes = actorPack.Read(entry).ToArray();
                    Console.WriteLine("EFFECTFILE " + entry.Name + " " + bytes.Length);
                    foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(bytes), "[ -~]{5,}"))
                        Console.WriteLine("EFFECTSTRING " + match.Value);
                }
                var link = BotwAampReader.Read(resources.Read("Actor/ActorLink/AncientArrow.bxml")!).Root;
                foreach (var obj in BotwActorResources.Objects(link))
                    foreach (var key in new[] { "ElinkUser", "ELUser", "EffectUser", "ELinkUser", "ModelUser", "ASUser" })
                        if (obj.Find(key) is { } value) Console.WriteLine("LINK " + key + "=" + value.AsString());
                foreach (var packPath in new[] { "Pack/Bootup.pack", "Pack/TitleBG.pack" })
                {
                    var effectPack = BotwSarcArchive.Open(Path.Combine(root, packPath));
                    foreach (var entry in effectPack.Entries.Where(e => e.Name.Contains("Effect") || e.Name.Contains("ELink")))
                    {
                        var bytes = effectPack.Read(entry).ToArray();
                        if (bytes.AsSpan().StartsWith("Yaz0"u8)) bytes = BotwYaz0.Decode(bytes);
                        Console.WriteLine("EFFECTFILE " + entry.Name + " " + bytes.Length);
                        if (entry.Name is "Effect/GameResident.sesetlist" or "ELink2/ELink2DB.sbelnk")
                            File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "arrow-" + Path.GetFileName(entry.Name)), bytes);
                        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(bytes), "[ -~]{5,}"))
                            if (match.Value.Contains("Arrow", StringComparison.OrdinalIgnoreCase) || match.Value.Contains("Ancient", StringComparison.OrdinalIgnoreCase))
                                Console.WriteLine("EFFECTSTRING " + match.Value);
                    }
                }
            }
            foreach (var state in resources.Animations) Console.WriteLine("STATE " + state.State + " -> " + state.Clip);
            foreach (var resource in resources.Models.Select(m => m.Resource).Concat(resources.AnimationResources).Distinct())
            {
                if (resources.ReadModel(resource) is not { } bytes) continue;
                var doc = new ToolboxBfresDocument(bytes);
                foreach (dynamic model in doc.Models)
                {
                    var preview = new ToolboxActorModel(doc, (string)model.Name, new(), new Dictionary<(string Material, string Sampler), string>(), resources.Warnings, new HashSet<string>());
                    foreach (var mesh in preview.RestPose.Meshes)
                    {
                        Console.WriteLine("MESH " + mesh.Name);
                        if (mesh.Material.Botw is not { } material) continue;
                        foreach (var pair in material.Options) Console.WriteLine("OPTION " + pair.Key + "=" + pair.Value);
                        foreach (var pair in material.Parameters.Where(p => p.Key.Contains("color") || p.Key.Contains("value") || p.Key.Contains("emission"))) Console.WriteLine("VALUE " + pair.Key + "=" + string.Join(',', pair.Value));
                    }
                }
                if (!doc.IsSwitch && name == "Weapon_Bow_023")
                    foreach (var p in doc.Resource.GetType().GetProperties().Where(p => p.Name.Contains("Vis")))
                    {
                        Console.WriteLine("VISPROP " + p.Name);
                        foreach (object v in ToolboxBfresDocument.Items(p.GetValue(doc.Resource)))
                            foreach (var q in v.GetType().GetProperties()) Console.WriteLine("VISFIELD " + q.Name + " " + q.PropertyType + "=" + (q.GetValue(v) is Array a ? string.Join(",", a.Cast<object>()) : q.GetValue(v)));
                    }
                foreach (dynamic anim in doc.SkeletalAnimations) Console.WriteLine($"SKEL {resource}: {anim.Name} frames={anim.FrameCount}");
                if (doc.Resource is Syroot.NintenTools.NSW.Bfres.ResFile nx)
                    foreach (var anim in nx.MaterialAnims)
                    {
                        Console.WriteLine($"MAT {anim.Name} frames={anim.FrameCount}");
                        foreach (var track in anim.MaterialAnimDataList)
                        {
                            Console.WriteLine("  MATERIAL " + track.Name);
                            foreach (var info in track.ParamAnimInfos) Console.WriteLine("  PARAM " + info.Name);
                            foreach (var p in track.GetType().GetProperties()) if (p.Name.Contains("Vis") || p.Name.Contains("Base")) Console.WriteLine("  PROP " + p.Name + "=" + p.GetValue(track));
                            foreach (var curve in track.Curves)
                            {
                                Console.WriteLine($"  CURVE {curve.CurveType} offset={curve.AnimDataOffset} first={curve.Keys[0,0]} last={curve.Keys[curve.Keys.GetLength(0)-1,0]}");
                                if (curve.CurveType.ToString() == "StepBool")
                                    foreach (var p in curve.GetType().GetProperties()) Console.WriteLine("  CURVEPROP " + p.Name + " " + p.PropertyType + "=" + (p.GetValue(curve) is Array a ? string.Join(",", a.Cast<object>()) : p.GetValue(curve)));
                            }
                        }
                    }
            }
            var pack = BotwSarcArchive.Open(sources.Find(name)!.Path);
            foreach (var entry in pack.Entries.Where(e => e.Name.EndsWith(".bas")))
            {
                Console.WriteLine("BAS " + entry.Name);
                if (entry.Name.EndsWith("_Off.bas") || entry.Name.EndsWith("_Hold.bas")) Dump(BotwAampReader.Read(pack.Read(entry).ToArray()).Root, "");
                foreach (var obj in BotwActorResources.Objects(BotwAampReader.Read(pack.Read(entry).ToArray()).Root))
                {
                    Console.WriteLine("  OBJECT " + obj.Hash);
                    foreach (string key in new[] { "FileName", "StartFrame", "EndFrame", "PlayRate", "Loop", "TypeIndex" })
                        if (obj.Find(key) is { } p) Console.WriteLine("  " + key + " type=" + p.Type + " value=" + (p.Type == 1 ? p.AsSingle().ToString() : p.Type == 2 ? p.AsInt32().ToString() : p.Type is 7 or 8 or 15 or 20 ? p.AsString() : "?"));
                }
            }
            }
            catch (Exception ex) { Console.WriteLine("ERROR " + name + ": " + ex.Message); }
        }
        foreach (var actor in sources.Actors.Values.Where(s => s.Name.Contains("Arrow"))) Console.WriteLine("ARROW " + actor.Name);
    }
    private static void Dump(AampReadList list, string indent)
    {
        Console.WriteLine(indent + "LIST " + list.Hash);
        foreach (var obj in list.Objects)
        {
            Console.WriteLine(indent + "OBJ " + obj.Hash);
            foreach (var p in obj.Parameters) Console.WriteLine(indent + "  PARAM " + p.Hash + " T=" + p.Type + " " + (p.Type == 1 ? p.AsSingle().ToString() : p.Type == 2 ? p.AsInt32().ToString() : p.Type is 7 or 8 or 15 or 20 ? p.AsString() : "?"));
        }
        foreach (var child in list.Children) Dump(child, indent + "  ");
    }
}
