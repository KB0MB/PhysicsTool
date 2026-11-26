using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using System.Text.Json;

internal sealed class IconBrowserChecks : Application
{
    internal static string Botw = "", Totk = "", Output = "";
    internal static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new Window { Width = 1100, Height = 800, ShowInTaskbar = false };
        lifetime.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Output);
                string botwMod = Path.Combine(Output, "botw-mod"), totkMod = Path.Combine(Output, "totk-mod");
                string bRoot = Path.Combine(botwMod, "romfs"), tRoot = Path.Combine(totkMod, "romfs");
                Directory.CreateDirectory(Path.Combine(bRoot, "Actor/Pack")); Directory.CreateDirectory(Path.Combine(tRoot, "Pack/Actor"));
                File.Copy(Path.Combine(Botw, "Actor/Pack/Weapon_Sword_070.sbactorpack"), Path.Combine(bRoot, "Actor/Pack/Weapon_Sword_070.sbactorpack"), true);
                File.Copy(Path.Combine(Totk, "Pack/Actor/Weapon_Sword_070.pack.zs"), Path.Combine(tRoot, "Pack/Actor/Weapon_Sword_070.pack.zs"), true);
                string preferencesPath = Path.Combine(Output, "preferences.json"), foldersPath = Path.Combine(Output, "actor-folders.json");
                new IconToolPreferences { BotwMod = botwMod, TotkMod = totkMod }.Save(preferencesPath);
                File.WriteAllBytes(foldersPath, JsonSerializer.SerializeToUtf8Bytes(new ActorBrowserPaths { Botw = Botw, Totk = Totk }));
                var originalPaths = File.ReadAllBytes(foldersPath);
                var view = new IconToolView(window, graphics: false, botwMannequinRoot: Botw, preferencesPath: preferencesPath, actorFoldersPath: foldersPath);
                window.Content = view; window.UpdateLayout();
                Button FindButton(Control parent, string label) => parent.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label));
                void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                async Task Ready() { for (int i = 0; i < 600 && view.Busy; i++) await Task.Delay(50); Check(!view.Busy, "Browser/load timed out"); window.UpdateLayout(); }
                Click(FindButton(view, "Open mod actor")); await Ready();
                var browser = lifetime.Windows.Single(w => w.Title == "Icon Tool - Mod Actors");
                var game = browser.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconActorGame");
                var root = browser.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "IconActorRoot");
                var list = browser.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "IconActorResults");
                Check(root.Text == botwMod && list.Items.Count == 1, "BotW mod browser did not scan saved folder");
                game.SelectedIndex = 1; await Ready();
                Check(root.Text == totkMod && list.Items.Cast<IconActor>().Single().Path.EndsWith(".pack.zs"), "TotK mod folder was mixed with BotW");
                game.SelectedIndex = 0; await Ready();
                Check(root.Text == botwMod && list.Items.Count == 1, "Switching back lost BotW mod folder");
                browser.UpdateLayout();
                using (var screenshot = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)browser.Bounds.Width, (int)browser.Bounds.Height)))
                { screenshot.Render(browser); screenshot.Save(Path.Combine(Output, "mod-browser.png")); }
                list.SelectedIndex = 0; Click(FindButton(browser, "Open actor")); await Ready();
                Check(view.Asset?.Name == "Weapon_Sword_070" && view.Asset.Model.Meshes.Count > 0 && view.Asset.Reference != null, "Actor-only mod did not inherit models/textures/icon from dump");
                Check(!browser.IsVisible, "Loaded mod browser stayed open");
                var roots = BotwActorCatalog.FindResourceRoots([Directory.GetParent(Botw)!.FullName], new(), default);
                var compatible = IconCaptureAsset.CompatibleFallbackRoots(Path.Combine(bRoot, "Actor/Pack/Weapon_Sword_070.sbactorpack"), roots).ToArray();
                Check(compatible.Any(r => Path.GetFullPath(r) == Path.GetFullPath(Botw)) && compatible.Length < roots.Count, "Mixed-platform dump roots were not filtered");
                Check(File.ReadAllBytes(foldersPath).SequenceEqual(originalPaths), "Mod browser changed dump folders");
                Click(FindButton(view, "Open actor")); await Ready();
                var normal = lifetime.Windows.Single(w => w.Title == "Icon Tool - Actors");
                Check(normal.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "IconActorRoot").Text == Botw, "Normal actor browser reused mod folder");
                normal.Close(); await Task.Yield();
                var remembered = IconToolPreferences.Read(preferencesPath);
                Check(remembered.BotwMod == botwMod && remembered.TotkMod == totkMod, "Mod folders did not persist independently");
                foreach (var item in new[] { (false, true, IconImageFormat.Sbitemico, "romfs/UI/StockItem"), (false, false, IconImageFormat.Sbitemico, "content/UI/StockItem"), (true, true, IconImageFormat.BntxZs, "romfs/UI/Tex/Icon") })
                {
                    var empty = Path.Combine(Output, "empty-mod");
                    Check(Path.GetFullPath(IconToolPreferences.ModExportDirectory(empty, item.Item1, item.Item2, item.Item3, [Botw, Totk])) == Path.GetFullPath(Path.Combine(empty, item.Item4)), "Incorrect mod export path");
                }
                var template = File.ReadAllBytes(Path.Combine(Totk, "UI/Tex/Icon/Armor_001_Head.bntx.zs"));
                foreach (int size in new[] { 96, 144, 256, 512 })
                {
                    var pixels = Enumerable.Range(0, size * size * 4).Select(i => (byte)(i * 17 + i / (size * 4))).ToArray();
                    var encoded = IconItemTexture.EncodeBntx(template, "Roundtrip", pixels, size, size, true);
                    using var decompressor = new ZstdSharp.Decompressor();
                    var decoded = TexToGoTexture.DecodeBntx(decompressor.Unwrap(encoded).ToArray(), "Roundtrip");
                    Check(decoded.Width == size && decoded.Height == size && decoded.Pixels.SequenceEqual(pixels), "TotK BNTX template roundtrip failed at " + size);
                }
                Console.WriteLine("PASS shared actor/mod browser, separate BotW/TotK roots, dump fallback, mod export paths and TotK BNTX.ZS sizes");
                Result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { lifetime.Shutdown(Result); }
        };
        base.OnFrameworkInitializationCompleted();
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
