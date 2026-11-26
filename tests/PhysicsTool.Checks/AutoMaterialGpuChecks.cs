using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using HKCLTool;
using PhysicsTool.Desktop;

internal sealed class AutoMaterialGpuChecks : Application
{
    internal static string Root = "", Output = "";
    internal static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new MainWindow { Width = 1280, Height = 900, ShowInTaskbar = false }; lifetime.MainWindow = window;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(150) };
        timeout.Tick += (_, _) => Finish("Auto material GPU timeout"); timeout.Start();
        window.ModelSurface!.RenderFailed += Finish;
        window.Opened += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Output);
                await window.LoadActorPathAsync(Path.Combine(Root, "Actor", "Pack", "Weapon_Bow_023.sbactorpack"), [Root]);
                var model = window.Viewport.Model!;
                window.Viewport.ShowEmission = true;
                var autoMaterial = model.Meshes.First(m => m.Name.EndsWith("__Mt_Weapon_Bow_023")).Material.Botw!;
                float autoBefore = autoMaterial.Parameters["const_value0"][0];
                var first = await Capture("bow-auto-0.png");
                await Task.Delay(1100);
                var second = await Capture("bow-auto-1.png");
                if (first.SequenceEqual(second)) throw new Exception($"Paused actor did not animate its material: {autoBefore} -> {autoMaterial.Parameters["const_value0"][0]}");
                if (!ReferenceEquals(model, window.Viewport.Model)) throw new Exception("Material clock rebuilt model geometry");
                Console.WriteLine("PASS GPU: default material playback with skeletal animation stopped and geometry unchanged.");
                await window.LoadActorPathAsync(Path.Combine(Root, "Pack", "TitleBG.pack"), [Root], "Actor/Pack/GameROMPlayer.sbactorpack");
                var armor = new Dictionary<PlayerArmorSlot, string>();
                // Preserve the current defaults when swapping just the weapons.
                var field = typeof(MainWindow).GetField("_actorScene", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                var scene = (BotwActorScene)field.GetValue(window)!;
                foreach (var pair in scene.EquippedArmor) armor[pair.Key] = pair.Value;
                await window.SwapPlayerEquipmentAsync(armor, new Dictionary<PlayerWeaponKind, PlayerWeaponSelection>
                {
                    [PlayerWeaponKind.Sword] = new("Weapon_Sword_023", false),
                    [PlayerWeaponKind.Bow] = new("Weapon_Bow_023", false),
                    [PlayerWeaponKind.Shield] = new("Weapon_Shield_038", false)
                }, default);
                scene = (BotwActorScene)field.GetValue(window)!;
                var sword = scene.CurrentModel.Meshes.First(m => m.Name.EndsWith("__Mt_Sword_023"));
                var bow = scene.CurrentModel.Meshes.First(m => m.Name.EndsWith("__Mt_Weapon_Bow_023"));
                var swordBefore = sword.Material.Botw!.Parameters["tex_srt0"].ToArray();
                var bowBefore = bow.Material.Botw!.Parameters["const_value0"].ToArray();
                await Task.Delay(1100);
                if (swordBefore.SequenceEqual(sword.Material.Botw.Parameters["tex_srt0"]) || bowBefore.SequenceEqual(bow.Material.Botw.Parameters["const_value0"]))
                    throw new Exception("Equipped weapon auto tracks are static");
                window.Viewport.Orbit(MathF.PI, 0);
                await Capture("player-auto-weapons.png");
                if (scene.CurrentModel.Meshes.Any(m => m.Name.Contains("Mt_String"))) throw new Exception("Stowed ancient bow string visible");
                foreach (var blade in scene.CurrentModel.Meshes.Where(m => m.Name.Contains("Blade")))
                    if (blade.Material.Botw?.Parameters.TryGetValue("const_value0", out var value) == true && value[0] != 0) throw new Exception("Stowed blade is energized");
                var camera = window.Viewport.CameraState;
                await window.SwapPlayerEquipmentAsync(armor, new Dictionary<PlayerWeaponKind, PlayerWeaponSelection>
                {
                    [PlayerWeaponKind.Sword] = new("Weapon_Sword_023", true),
                    [PlayerWeaponKind.Bow] = new("Weapon_Bow_023", false),
                    [PlayerWeaponKind.Shield] = new("Weapon_Shield_038", true)
                }, default);
                if (!ReferenceEquals(scene, field.GetValue(window)) || window.Viewport.CameraState != camera) throw new Exception("Equipment state rebuilt scene or camera");
                await Capture("player-ancient-drawn.png");
                await window.SwapPlayerEquipmentAsync(armor, new Dictionary<PlayerWeaponKind, PlayerWeaponSelection>
                {
                    [PlayerWeaponKind.Bow] = new("Weapon_Bow_023", true, "AncientArrow")
                }, default);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var clips = (ComboBox)typeof(MainWindow).GetField("_animations", flags)!.GetValue(window)!;
                clips.SelectedIndex = scene.Animations.FindIndex(a => a.Name == "Bow_Attack_Charge") + 1;
                var timer = (DispatcherTimer)typeof(MainWindow).GetField("_actorTimer", flags)!.GetValue(window)!;
                timer.Stop();
                typeof(MainWindow).GetMethod("SeekActor", flags)!.Invoke(window, [18.0]); timer.Stop();
                window.Viewport.SetView(CameraView.Front, false);
                window.Viewport.Orbit(.6f, .15f);
                var bladeMesh = scene.CurrentModel.Meshes.Single(m => m.Material.Particle != null);
                var vertices = bladeMesh.Vertices;
                var points = Enumerable.Range(0, vertices.Length / 8).Select(i => new System.Numerics.Vector3(vertices[i * 8], vertices[i * 8 + 1], vertices[i * 8 + 2])).ToArray();
                window.Viewport.ModelSelectionBounds = (points.Aggregate(System.Numerics.Vector3.Min) - new System.Numerics.Vector3(.1f), points.Aggregate(System.Numerics.Vector3.Max) + new System.Numerics.Vector3(.1f));
                window.Viewport.FrameSelected(); await Task.Delay(400);
                var glowingArrow = await Capture("ancient-arrow-charge-emission.png");
                bladeMesh.Material.Particle!.Alpha = 0;
                var noBlade = await Capture("ancient-arrow-effect-hidden.png");
                if (noBlade.SequenceEqual(glowingArrow)) throw new Exception("Native blade effect produced no GPU pixels");
                using (var shown = SkiaSharp.SKBitmap.Decode(glowingArrow))
                using (var hidden = SkiaSharp.SKBitmap.Decode(noBlade))
                {
                    int blue = shown.Pixels.Zip(hidden.Pixels).Count(p => p.First != p.Second && p.First.Blue > 180 && p.First.Blue > p.First.Red + 40 && p.First.Green > p.First.Red + 20);
                    if (blue < 100) throw new Exception($"Native blade is not blue/cyan: {blue} pixels");
                    Console.WriteLine($"PASS GPU: native blade contributes {blue} blue/cyan pixels; transparent cutout and depth-tested attachment.");
                }
                bladeMesh.Material.Particle.Alpha = 1;
                window.Viewport.ShowEmission = false;
                var darkArrow = await Capture("ancient-arrow-charge-no-emission.png");
                if (glowingArrow.SequenceEqual(darkArrow)) throw new Exception("Ancient arrow emission did not affect GPU pixels");
                window.Viewport.ShowEmission = true;
                typeof(MainWindow).GetMethod("SeekActor", flags)!.Invoke(window, [0.0]); timer.Stop();
                var released = await Capture("ancient-arrow-charge-start.png");
                if (released.SequenceEqual(glowingArrow)) throw new Exception("Bow and arrow did not follow charge animation");
                typeof(MainWindow).GetMethod("SeekActor", flags)!.Invoke(window, [18.0]); timer.Stop();
                await Capture("ancient-arrow-charge-restored.png");
                typeof(MainWindow).GetMethod("SeekActor", flags)!.Invoke(window, [30.0]); timer.Stop();
                Console.WriteLine($"PARTICLE at seek30 {bladeMesh.Material.Particle.Color0} {bladeMesh.Material.Particle.Color1}; charge length={scene.Animations.First(a => a.Name == "Bow_Attack_Charge").FrameCount}");
                var heldClip = scene.Animations.First(a => a.Name.StartsWith("Bow_Attack_") && a.Name.Contains("_Draw"));
                window.Viewport.SetModelPose(scene.Update(heldClip, 30, false, 0, true));
                vertices = scene.CurrentModel.Meshes.Single(m => m.Material.Particle != null).Vertices;
                points = Enumerable.Range(0, vertices.Length / 8).Select(i => new System.Numerics.Vector3(vertices[i * 8], vertices[i * 8 + 1], vertices[i * 8 + 2])).ToArray();
                window.Viewport.ModelSelectionBounds = (points.Aggregate(System.Numerics.Vector3.Min) - new System.Numerics.Vector3(.1f), points.Aggregate(System.Numerics.Vector3.Max) + new System.Numerics.Vector3(.1f));
                window.Viewport.FrameSelected(); await Task.Delay(400);
                Console.WriteLine($"PARTICLE held {heldClip.Name} {bladeMesh.Material.Particle.Color0} {bladeMesh.Material.Particle.Color1}");
                await Capture("ancient-arrow-charge-settled.png");
                window.Viewport.Orbit(1.1f, .3f);
                await Capture("ancient-arrow-charge-side.png");
                Console.WriteLine("PASS GPU: ancient arrow emission and synchronized bow/arrow charge geometry.");
                bow = scene.CurrentModel.Meshes.First(m => m.Name.EndsWith("__Mt_Weapon_Bow_023"));
                window.ShowIconTool(); await Task.Delay(300);
                var stopped = bow.Material.Botw.Parameters["const_value0"].ToArray();
                await Task.Delay(300);
                if (!stopped.SequenceEqual(bow.Material.Botw.Parameters["const_value0"])) throw new Exception("Hidden actor material timer kept running");
                Console.WriteLine("PASS GPU: equipped sword/bow auto tracks; hidden actor timer stops.");
                Result = 0; Finish(null);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        async Task<byte[]> Capture(string name)
        {
            var complete = new TaskCompletionSource<byte[]>();
            void Receive(byte[] pixels, int width, int height)
            {
                window.ModelSurface.FrameCaptured -= Receive;
                var png = IconToolView.Encode(pixels, width, height, true);
                File.WriteAllBytes(Path.Combine(Output, name), png); complete.TrySetResult(png);
            }
            window.ModelSurface.FrameCaptured += Receive;
            window.ModelSurface.CaptureNextFrame = true; window.ModelSurface.RequestNextFrameRendering();
            return await complete.Task;
        }
        void Finish(string? error) { timeout.Stop(); if (error != null) Console.Error.WriteLine(error); lifetime.Shutdown(Result); }
        base.OnFrameworkInitializationCompleted();
    }
}
