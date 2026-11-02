using HKCLTool;
using PhysicsTool.Core;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhysicsTool.Desktop;

internal static class ActorExportChecks
{
    internal static void Ui(string actor, string output)
    {
        Directory.CreateDirectory(output);
        var window = new MainWindow(graphics: false); window.Show();
        var button = (Button)typeof(MainWindow).GetField("_actorExport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
        if (button.IsVisible) throw new InvalidOperationException("Actor export shown outside actor mode");
        var load = window.LoadActorPathAsync(actor);
        while (!load.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        load.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        if (!window.IsActorMode || !button.IsVisible || !button.IsEnabled) throw new InvalidOperationException("Actor export button unavailable");
        var menu = window.CreateActorExportMenu();
        var isTotk = actor.EndsWith(".pack.zs", StringComparison.OrdinalIgnoreCase);
        var expected = isTotk ? "Helper bones" : "Support bones";
        var items = menu.Items.OfType<MenuItem>().ToArray();
        if (items.Length != (isTotk ? 3 : 4) || !Equals(items[0].Header, "BFRES") || !Equals(items[1].Header, "Cloth") || !Equals(items[2].Header, expected) || !items.All(i => i.IsEnabled) ||
            (!isTotk && !Equals(items[3].Header, "BPHYSICS")))
            throw new InvalidOperationException("Actor export menu categories incorrect");
        using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "actor-export.png"));
        window.ShowOverview();
        if (button.IsVisible) throw new InvalidOperationException("Actor export remained visible on exit");
        window.Close();
        Console.WriteLine("PASS actor export UI: loaded resources, game-specific categories, availability and exit");
    }

    internal static void Run(string actor, string output)
    {
        Directory.CreateDirectory(output);
        var source = File.ReadAllBytes(actor);
        var resources = new BotwActorResources(actor);
        var entries = ActorResourceExport.List([resources, resources]);
        var parameters = entries.Where(e => e.Kind == ActorExportKind.Bphysics).ToArray();
        if (resources.IsTotk ? parameters.Length != 0 : parameters.Length != (resources.PhysicsParameters != null ? 1 : 0))
            throw new InvalidOperationException("BPHYSICS export availability does not match actor/game");
        if (parameters.Length == 1 && parameters[0].Resource != resources.PhysicsParametersPath)
            throw new InvalidOperationException("BPHYSICS export lost its linked resource name");
        if (entries.Count == 0 || entries.Count != entries.Select(e => (e.Resource, e.Kind)).Distinct().Count())
            throw new InvalidOperationException("Actor export list is empty or duplicated.");
        foreach (var entry in entries)
        {
            var path = Path.Combine(output, entry.FileName);
            ActorResourceExport.Save(entry, path);
            var data = File.ReadAllBytes(path);
            if (entry.Kind == ActorExportKind.Bfres)
            {
                if (!data.AsSpan().StartsWith("FRES"u8)) throw new InvalidOperationException("BFRES not decompressed");
                resources.PrepareTotkStrings();
                var document = new ToolboxBfresDocument(data);
                if (document.Models.Length == 0) throw new InvalidOperationException("Exported BFRES has no models");
            }
            else
            {
                if (!data.AsSpan().SequenceEqual(resources.Read(entry.Resource))) throw new InvalidOperationException("Physics export changed native bytes");
                if (entry.Kind == ActorExportKind.Bphysics)
                {
                    if (!data.AsSpan().SequenceEqual(resources.PhysicsParameters)) throw new InvalidOperationException("BPHYSICS export changed loaded settings");
                    BphysicsEditingDocument.Load(path).Validate();
                }
                else { var document = new HkclService(); document.Load(path); }
            }
            Console.WriteLine($"PASS {entry.Kind}: {entry.FileName}, {data.Length:N0} bytes");
        }
        if (!source.AsSpan().SequenceEqual(File.ReadAllBytes(actor))) throw new InvalidOperationException("Actor pack changed during export");
        Console.WriteLine($"PASS actor export: {resources.ActorName}, {entries.Count} files, native physics preserved, models reopened, source unchanged");
    }
}
