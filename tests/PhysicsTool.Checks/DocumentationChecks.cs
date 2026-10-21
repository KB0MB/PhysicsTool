using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using System.Text.Json;

internal static class DocumentationChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        UiLanguage.Set("en");
        var help = new DocumentationWindow { Width = 840, Height = 600 };
        help.Show(); Dispatcher.UIThread.RunJobs();
        var controls = help.GetVisualDescendants().ToArray();
        var list = controls.OfType<ListBox>().Single(c => c.Name == "DocumentationTopics");
        var search = controls.OfType<TextBox>().Single(c => c.Name == "DocumentationSearch");
        var heading = controls.OfType<TextBlock>().Single(c => c.Name == "DocumentationHeading");
        var body = controls.OfType<TextBlock>().Single(c => c.Name == "DocumentationBody");
        try
        {
            Require(ClothHelp.Topics[0].Title == "Start here" && ClothHelp.Topics[1].Title == "Shortcuts",
                "Shortcuts must appear immediately after Start here");
            foreach (var code in new[] { "fr", "es", "ja" })
            {
                var assembly = typeof(MainWindow).Assembly;
                var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith($"Documentation.{code}.json"));
                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var json = JsonDocument.Parse(stream);
                var names = json.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
                Require(names.Distinct().Count() == names.Length, $"Duplicate documentation key in {code}");
                Require(names.Count(n => n.StartsWith("Help.")) == ClothHelp.Topics.Count, $"Stale or missing topics in {code}");
                search.Text = "";
                list.SelectedIndex = 0;
                UiLanguage.Set(code); Dispatcher.UIThread.RunJobs();
                Require(heading.Text == UiLanguage.Text(ClothHelp.Topics[0].Title), $"Initial title not translated in {code}");
                for (var i = 0; i < ClothHelp.Topics.Count; i++)
                {
                    var topic = ClothHelp.Topics[i];
                    Require(json.RootElement.TryGetProperty("Help." + topic.Title, out var entry), $"Missing topic {code}: {topic.Title}");
                    Require(UiLanguage.Text(topic.Body) == entry.GetString() && UiLanguage.Text(topic.Body) != topic.Body, $"English body fallback in {code}: {topic.Title}");
                    list.SelectedIndex = i; Dispatcher.UIThread.RunJobs();
                    list.ScrollIntoView(i); Dispatcher.UIThread.RunJobs();
                    if (list.ContainerFromIndex(i) is ListBoxItem item)
                        Require(item.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == UiLanguage.Text(topic.Title)), $"Recycled topic label changed identity in {code}: {topic.Title}");
                    Require(body.Text == entry.GetString() && heading.Text == UiLanguage.Text(topic.Title), $"Wrong displayed article in {code}: {topic.Title}");
                    Require(body.Bounds.Width > 0 && body.Bounds.Width <= 840 - 260, $"Article overflows horizontally in {code}");
                    if (i == 0 || topic.Title == "Support and helper bones")
                    {
                        using var frame = help.CaptureRenderedFrame()!;
                        frame.Save(Path.Combine(output, $"documentation-{code}-{i}.png"));
                    }
                }
                foreach (var kind in ClothConstraintCatalog.Kinds)
                    Require(UiLanguage.Text(kind.Description) != kind.Description, $"English constraint description in {code}: {kind.Name}");
                search.Text = code switch { "fr" => "quaternions", "es" => "cuaterniones", _ => "クォータニオン" };
                Dispatcher.UIThread.RunJobs();
                Require(list.ItemCount == 1 && heading.Text == UiLanguage.Text("Bones and model binding"), $"Localized body search failed in {code}");
                UiLanguage.Set("en"); Dispatcher.UIThread.RunJobs();
                Require(list.ItemCount == 0 && heading.Text == "No matching topics", "Language switch did not refresh filtered results");
            }
            var supportIndex = ClothHelp.Topics.ToList().FindIndex(t => t.Title == "Support and helper bones");
            search.Text = ""; Dispatcher.UIThread.RunJobs(); list.SelectedIndex = supportIndex;
            UiLanguage.Set("fr"); Dispatcher.UIThread.RunJobs();
            Require(list.SelectedIndex == supportIndex && heading.Text == UiLanguage.Text("Support and helper bones"), "Language switch lost selected topic");
            UiLanguage.Set("en"); Dispatcher.UIThread.RunJobs();
            Require(body.Text == ClothHelp.Topics[supportIndex].Body, "English article restoration failed");
        }
        finally { help.Close(); UiLanguage.Set("en"); }
        Console.WriteLine("PASS: all documentation and constraint descriptions in French/Spanish/Japanese, localized search, live language switching and compact layouts.");
    }
}
