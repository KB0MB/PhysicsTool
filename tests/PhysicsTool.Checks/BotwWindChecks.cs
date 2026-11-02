using System.Numerics;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class BotwWindChecks
{
    public static void ExportConvertedWind(string actorPath, string output)
    {
        Directory.CreateDirectory(output);
        var originalHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(actorPath));
        var resources = new BotwActorResources(actorPath);
        var resource = resources.PhysicsFiles.Single(p => p.EndsWith(".bphcl", StringComparison.OrdinalIgnoreCase));
        var sourcePath = Path.Combine(output, Path.GetFileName(resource));
        File.WriteAllBytes(sourcePath, resources.Read(resource)!);
        var source = new HkclService(); source.Load(sourcePath);
        var hkclPath = Path.ChangeExtension(sourcePath, ".hkcl");
        var result = new PhysicsConversion(source, sourcePath).Export(hkclPath, new(HkclPlatform.WiiU));
        var converted = new HkclService(); converted.Load(hkclPath);
        var name = Path.GetFileNameWithoutExtension(hkclPath);
        var directory = name.EndsWith("_Head", StringComparison.Ordinal) ? name[..^5] : name;
        var parameters = converted.CreateBphysicsDocument(directory + "/" + name + ".hkcl");
        var sidecarPath = Path.ChangeExtension(hkclPath, ".bphysics");
        BphysicsService.Save(sidecarPath, parameters);
        var reloaded = BphysicsService.Load(sidecarPath);
        if (result.Entries != source.GetClothSummaries().Count || reloaded.Cloths.Count != result.Entries ||
            !parameters.Cloths.SequenceEqual(reloaded.Cloths) || reloaded.HkclPath != parameters.HkclPath ||
            HkclService.DetectHkclPlatform(hkclPath) != HkclPlatform.WiiU ||
            !originalHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(actorPath))))
            throw new InvalidOperationException("Converted wind export verification failed.");
        Console.WriteLine($"Verified {result.Entries} cloths; Wii U HKCL; source actor unchanged.\n{sidecarPath}\nHKCL reference: {reloaded.HkclPath}");
        foreach (var profile in reloaded.Cloths)
            Console.WriteLine($"{profile.Name}: base={profile.BaseBone}, drag={profile.WindDrag:G7}");
    }

    public static void SurveyMass(string actorDirectory, string output)
    {
        Directory.CreateDirectory(output);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<object>();
        foreach (var actor in Directory.EnumerateFiles(actorDirectory, "Armor_*.sbactorpack").Order())
        {
            try
            {
                var resources = new BotwActorResources(actor);
                if (resources.PhysicsParameters == null) continue;
                var parametersPath = Path.Combine(output, "survey.bphysics");
                File.WriteAllBytes(parametersPath, resources.PhysicsParameters);
                var parameters = BphysicsService.Load(parametersPath);
                foreach (var resource in resources.PhysicsFiles.Where(p => p.EndsWith(".hkcl", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!seen.Add(resource)) continue;
                    var bytes = resources.Read(resource);
                    if (bytes == null) continue;
                    var path = Path.Combine(output, "survey.hkcl"); File.WriteAllBytes(path, bytes);
                    var service = new HkclService(); service.Load(path);
                    for (var i = 0; i < service.GetClothSummaries().Count; i++)
                    {
                        var name = service.GetClothName(i);
                        var profile = BotwClothWindProfile.Find(parameters, name)?.Cloth;
                        if (profile == null) continue;
                        var mass = service.GetSimulationRuntimeData(i).TotalMass;
                        rows.Add(new { Actor = Path.GetFileNameWithoutExtension(actor), Resource = resource, Name = name,
                            Mass = mass, Drag = profile.WindDrag, Enabled = profile.WindEnabled,
                            Rate = mass > 0 ? profile.WindDrag / mass : 0 });
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"SKIP {Path.GetFileName(actor)}: {ex.Message}"); }
        }
        File.WriteAllText(Path.Combine(output, "armor-wind-mass.json"), System.Text.Json.JsonSerializer.Serialize(rows,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Surveyed {rows.Count} matched cloth profiles from {seen.Count} unique resources.");
    }

    public static void Actor(string path)
    {
        using var scene = BotwActorScene.Load(path);
        var solvers = new List<HkclPreviewSimulator>();
        scene.ConfigureSimulation(s => solvers.Add(s));
        if (solvers.Count == 0 || solvers.Any(s => s.WindProfile == null)) throw new InvalidOperationException("Native actor has unmatched wind profiles");
        scene.WindEnabled = true;
        var clip = scene.Animations.FirstOrDefault(a => a.Name == "Act_Accordion");
        for (var frame = 0; frame < 180; frame++)
        {
            scene.Update(clip, frame, true, 1f / 60, frame == 0);
            if (solvers.SelectMany(s => s.GetPositions().Values).Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)))
                throw new InvalidOperationException("Nonfinite native actor wind simulation");
        }
        Console.WriteLine($"PASS native actor BPHYSICS: {solvers.Count} exact profiles, 180 animated wind frames; {Path.GetFileName(path)}");
    }

    public static void Ui(string output)
    {
        var session = new EditorSession(); session.New("Hair", 0);
        session.LoadWindParameters(Path.Combine(output, "wind.bphysics"));
        var window = new PreviewOptionsWindow(new(), _ => { }, session);
        window.Show(); Dispatcher.UIThread.RunJobs();
        var gust = window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "Gustiness");
        if (gust.IsEffectivelyEnabled) throw new InvalidOperationException("Generic gust control enabled for BPHYSICS");
        var clear = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(ToolTip.GetTip(b), "Clear wind parameters"));
        using (var shot = window.CaptureRenderedFrame()!) shot.Save(Path.Combine(output, "wind-options.png"));
        clear.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        if (!gust.IsEffectivelyEnabled || session.WindParametersPath != null) throw new InvalidOperationException("Clear wind profile UI");
        window.Close();
        Console.WriteLine("PASS BPHYSICS preview options UI");
    }

    public static void InspectConversion(string actorPath, string output)
    {
        Directory.CreateDirectory(output);
        var resources = new BotwActorResources(actorPath);
        foreach (var resource in resources.PhysicsFiles.Where(p => p.EndsWith(".bphcl", StringComparison.OrdinalIgnoreCase)))
        {
            var path = Path.Combine(output, Path.GetFileName(resource));
            File.WriteAllBytes(path, resources.Read(resource)!);
            var source = new HkclService(); source.Load(path);
            var suggestions = source.GetBphclConversionScaleSuggestions();
            for (var i = 0; i < source.GetClothSummaries().Count; i++)
            {
                var converted = source.CreateFreshHkclFromCurrentBphcl(i);
                var preview = converted.GetParticlePreview(0);
                var parameters = converted.CreateBphysicsDocument(Path.ChangeExtension(Path.GetFileName(resource), ".hkcl"));
                var p = parameters.Cloths.Single();
                var dragLimit = p.Name.Contains("Earring", StringComparison.OrdinalIgnoreCase) ? 5f : 8f;
                if (Math.Abs(p.WindDrag - Math.Min(dragLimit, 4f * preview.TotalMass!.Value)) > .00001f)
                    throw new InvalidOperationException("Converted wind did not use the exported HKCL mass.");
                var dynamicMass = preview.Particles.Where(p => !p.Fixed).Sum(p => p.Mass);
                var sum = preview.Particles.Sum(p => p.Mass);
                if (Math.Abs(preview.TotalMass!.Value - sum) > .00001f) throw new InvalidOperationException("Converted total mass does not match particles.");
                Console.WriteLine($"{p.Name}: total={preview.TotalMass:G7}, dynamic={dynamicMass:G7}, drag={p.WindDrag:G5}, drag/total={p.WindDrag / preview.TotalMass:G7}, subwind={parameters.SubWindSpeed:G5}, mainFactor={p.SubWindFactorMain:G5}; {suggestions[i]}");
            }
        }
    }

    private static void ExportMassChecks(string output)
    {
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        var doc = HkclService.CreateBlankCloth("Hair_Test", false);
        var initial = doc.GetParticleRows(0).Single();
        initial.Mass = 0; initial.InverseMass = 0; initial.Fixed = true;
        doc.UpdateParticleRows(0, [initial]);
        var zero = doc.CreateBphysicsDocument("Test.hkcl").Cloths.Single();
        Require(zero.WindDrag == 0 && !zero.WindEnabled, "Zero-mass cloth exported active wind");
        initial.Mass = .05f; initial.InverseMass = 20; initial.Fixed = false;
        doc.UpdateParticleRows(0, [initial]);
        doc.AddParticle(0, new ParticleEditRow { Mass = .05f, InverseMass = 20, Radius = .01f });
        var before = doc.CaptureState();
        var light = doc.CreateBphysicsDocument("Test.hkcl");
        Require(Math.Abs(light.Cloths[0].WindDrag - .4f) < .00001f, "Light cloth drag was not mass-aware");
        Require(doc.CaptureState() == before, "Wind export changed cloth mass or constraints");
        var rows = doc.GetParticleRows(0);
        foreach (var row in rows) { row.Mass = .1f; row.InverseMass = 10; }
        doc.UpdateParticleRows(0, rows);
        Require(Math.Abs(doc.CreateBphysicsDocument("Test.hkcl").Cloths[0].WindDrag - .8f) < .00001f,
            "Wind export ignored edited mass");
        foreach (var row in rows) { row.Mass = 5; row.InverseMass = .2f; }
        doc.UpdateParticleRows(0, rows);
        Require(doc.CreateBphysicsDocument("Test.hkcl").Cloths[0].WindDrag == 8, "Heavy cloth drag exceeded prior default");
        var earring = HkclService.CreateBlankCloth("Earring_Test", false);
        earring.AddParticle(0, new ParticleEditRow { Mass = 2, InverseMass = .5f, Radius = .01f });
        var accessory = earring.CreateBphysicsDocument("Test.hkcl").Cloths[0];
        Require(accessory.WindDrag == 5 && accessory.SubWindFactorMain == 0 && accessory.WindMaxSpeed == 8,
            "Accessory tuning changed");
        var native = HkclService.CreateBlankCloth("Hat_179_Havok", false).CreateBphysicsDocument("Armor_179/Armor_179_Head.hkcl");
        Require(native.Cloths[0].WindDrag == 4 && native.SubWindSpeed == 50, "Verified native wind was recalibrated");
        light.Cloths[0] = light.Cloths[0] with { WindDrag = 11.25f };
        var path = Path.Combine(output, "mass-aware.bphysics"); BphysicsService.Save(path, light);
        Require(BphysicsService.Load(path).Cloths[0].WindDrag == 11.25f, "Authored drag was recalibrated during save/load");
        Console.WriteLine("PASS mass-aware wind export: zero/light/edited/heavy masses, native profiles, accessories, authored settings and source isolation");
    }

    public static void Run(string output)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static bool Near(float a, float b) => Math.Abs(a - b) < .0001f;
        Directory.CreateDirectory(output);
        ExportMassChecks(output);
        var cloth = new BphysicsClothProfile("Hair", "Head", 0, 4, 5, 5, 0, 0);
        var profile = new BotwClothWindProfile(cloth, Vector3.UnitY, 0, 25);
        var wind = new BotwClothWind();
        var sample = wind.Step(profile, Vector3.UnitX * 15, 1f / 60, .5f);
        Check(sample.AirVelocity == Vector3.UnitX * 5 && Near(sample.Drag, 2), "Native wind air velocity/drag");
        Check(Near(wind.Step(profile, Vector3.UnitX * 50, 1f / 60, .5f).Drag, 4 * 20 / 30f), "Cloth-set world wind cap");
        var acceleration = sample.Acceleration(Vector3.UnitX, .5f, 2, 2, 1f / 60);
        Check(Near(acceleration.X, 4), "Native wind force / total mass");
        Check(Near(sample.Acceleration(Vector3.UnitX, 1, 1, 4, 1f / 60).X, 2), "Total mass must affect wind");
        Check(sample.Acceleration(Vector3.UnitX * 5, .5f, 2, 2, 1f / 60) == Vector3.Zero, "Wind must be relative to particle velocity");
        Check(sample.Acceleration(Vector3.Zero, 0, 0, 2, 1f / 60) == Vector3.Zero, "Fixed particles received wind");
        Check(wind.Step(profile, Vector3.Zero, .1f, .5f) == default, "Wind in calm conditions with no additive wind");
        Check(wind.Step(profile with { Cloth = cloth with { WindEnabled = false } }, Vector3.UnitX * 15, .1f, .5f) == default,
            "wind_enable was ignored");
        var pulse = profile with { Cloth = cloth with { WindDrag = 8, WindMinSpeed = 0, WindMaxSpeed = 0, SubWindFactorAdd = 1 },
            SubWindFrequency = 1, SubWindSpeed = 3 };
        wind.Reset();
        sample = wind.Step(pulse, Vector3.Zero, .25f, .5f);
        Check(Near(sample.AirVelocity.Y, 3) && Near(sample.Drag, .8f), "Sub-wind pulse/additive drag");
        wind.Reset();
        Check(wind.Step(pulse, Vector3.Zero, .25f, .5f) == sample, "Wind phase reset");
        wind.Reset();
        Check(wind.Step(pulse, Vector3.Zero, .75f, .5f).AirVelocity == Vector3.Zero, "Negative sub-wind half-cycle must be clipped");
        var parameters = new BphysicsDocument { HkclPath = "Test.hkcl" };
        parameters.Cloths.Add(cloth); parameters.Cloths.Add(cloth with { Name = "Hair_Back" });
        Check(BotwClothWindProfile.Find(parameters, "Link:Hair_Back")?.Cloth.Name == "Hair_Back", "Exact profile matching");
        Check(BotwClothWindProfile.Find(parameters, "Hair_Back_Extra") == null, "Substring profile matching must not occur");
        parameters.Cloths.Add(cloth with { Name = "Link:Hair" });
        Check(BotwClothWindProfile.Find(parameters, "Hair") == null, "Ambiguous profile matching");
        parameters.Cloths.RemoveAt(2);
        var path = Path.Combine(output, "wind.bphysics"); BphysicsService.Save(path, parameters);
        var session = new EditorSession(); session.New("Hair", 0); session.LoadWindParameters(path);
        Check(session.SelectedWindProfile?.Cloth.Name == "Hair", "Editor profile load");
        Check(session.CreateSimulator().WindProfile != null, "Editor simulator ignored BPHYSICS");
        Check(!session.Document.IsBphcl && session.Document.GetClothName(0) == "Hair", "Wind load changed cloth");
        var donor = new EditorSession(); donor.New("Other", 0); session.SwapWith(donor);
        Check(session.WindParametersPath == null && donor.WindParametersPath == path, "Wind profile did not follow swapped document");
        donor.Clear(); Check(donor.WindParametersPath == null, "Wind profile leaked across files");
        var data = new ParticlePreviewData { Gravity = Vector3.Zero, TotalMass = 2 };
        data.Particles.Add(new() { Index = 0, Mass = .5f, InverseMass = 2 });
        var solver = new HkclPreviewSimulator(data) { WindEnabled = true, WindSpeed = 15, RandomWindDirections = false };
        solver.SetWindProfile(profile); solver.Step(1f / 60);
        Check(solver.GetPositions()[0].X > 0, "Profiled wind did not reach integration");
        solver.Reset(); solver.WindEnabled = false; solver.Step(1f / 60);
        Check(solver.GetPositions()[0] == Vector3.Zero, "Disabled wind still affected integration");
        Console.WriteLine("PASS BotW BPHYSICS wind: native drag, mass, pulse, gating, matching, editor lifecycle and integration");
    }
}
