using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HKCLTool;
using PhysicsTool.Desktop;
using System.Text.Json;

internal static class ActorBrowserChecks
{
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run(string output)
    {
        var botw = Path.Combine(output, "actor-tab-botw"); var totk = Path.Combine(output, "actor-tab-totk");
        var models = Path.Combine(output, "actor-tab-models");
        Directory.CreateDirectory(botw); Directory.CreateDirectory(Path.Combine(totk, "Pack")); Directory.CreateDirectory(models);
        File.WriteAllBytes(Path.Combine(totk, "Pack", "ZsDic.pack.zs"), []);
        File.WriteAllBytes(Path.Combine(models, "test.bfres"), []);
        var settings = Path.Combine(output, "actor-tab-paths.json");
        File.WriteAllText(settings, JsonSerializer.Serialize(new[] { botw, totk, models }));
        var migrated = ActorBrowserPaths.Read(settings);
        Require(migrated.Botw == botw && migrated.Totk == totk, "Legacy paths did not split by game");
        var requests = new List<(string[] Roots, bool Totk, TaskCompletionSource<BotwActorCatalogResult> Pending)>();
        Task<BotwActorCatalogResult> Scan(string[] roots, bool isTotk, IProgress<string> progress, CancellationToken token)
        {
            var pending = new TaskCompletionSource<BotwActorCatalogResult>(); requests.Add((roots, isTotk, pending));
            token.Register(() => pending.TrySetCanceled(token)); return pending.Task;
        }
        var window = new ActorBrowserWindow(settings, Scan); window.Show(); Dispatcher.UIThread.RunJobs();
        var tabs = (TabControl)window.Content!;
        T Find<T>(int tab, string name) where T : Control => ((Control)((TabItem)tabs.Items[tab]!).Content!).GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);
        Require(tabs.Items.Cast<TabItem>().Select(t => t.Header).SequenceEqual(new[] { "BOTW", "TOTK" }), "Game tabs missing");
        Require(requests.Count == 1 && requests[0].Roots.SequenceEqual(new[] { botw }) && !requests[0].Totk, "BOTW scanned another game's folders");
        tabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        Require(requests.Count == 2 && requests[1].Roots.SequenceEqual(new[] { totk }) && requests[1].Totk, "TOTK scanned model/BOTW folders");
        var entries = new[] {
            new BotwActorCatalogEntry("BotwActor", "BotwActor.sbactorpack", null, botw, false, true, false),
            new BotwActorCatalogEntry("TotkActor", "TotkActor.pack.zs", null, totk, true, true, true) };
        requests[0].Pending.SetResult(new(entries, [])); Dispatcher.UIThread.RunJobs();
        Require(Find<ListBox>(1, "TotkActors").ItemCount == 0, "Background BOTW completion replaced TOTK results");
        requests[1].Pending.SetResult(new(entries, [])); Dispatcher.UIThread.RunJobs();
        var totkList = Find<ListBox>(1, "TotkActors"); Require(totkList.Items.Cast<BotwActorCatalogEntry>().Single().IsTotk, "TOTK includes BOTW actors");
        totkList.SelectedIndex = 0;
        Find<TextBox>(1, "TotkActorSearch").Text = "Totk";
        Require(window.ResourceRoots.SequenceEqual(new[] { totk }), "TOTK load roots include obsolete model override");
        Require(!window.GetLogicalDescendants().OfType<TextBox>().Any(c => c.Name == "TotkModelRoot"), "Model override still displayed");
        tabs.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
        Require(Find<ListBox>(0, "BotwActors").Items.Cast<BotwActorCatalogEntry>().All(a => !a.IsTotk), "BOTW includes TOTK actors");
        Require(window.ResourceRoots.SequenceEqual(new[] { botw }), "BOTW load roots include TOTK override");
        tabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        Require(totkList.SelectedIndex == 0 && Find<TextBox>(1, "TotkActorSearch").Text == "Totk" && requests.Count == 2,
            $"Switching tabs lost selection/search or rescanned: index={totkList.SelectedIndex}, search={Find<TextBox>(1, "TotkActorSearch").Text}, scans={requests.Count}");
        window.Width = 560; window.Height = 480; Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "actor-browser-tabs.png"));
        window.Close();
        var saved = ActorBrowserPaths.Read(settings);
        Require(saved.Botw == botw && saved.Totk == totk && saved.TotkSelected, "Game paths or selected tab did not persist");
        var reopening = new ActorBrowserWindow(settings, Scan); reopening.Show(); Dispatcher.UIThread.RunJobs();
        Require(((TabControl)reopening.Content!).SelectedIndex == 1 && requests.Count == 3, "Reopening did not restore selected game");
        reopening.Close(); Dispatcher.UIThread.RunJobs();
        Require(requests[2].Pending.Task.IsCanceled, "Closing did not cancel an active scan");
        Console.WriteLine("PASS: Actor BOTW/TOTK tabs, isolated paths/search/selection, concurrent scan completion, migration/persistence, compact layout and cancellation.");
    }
}
