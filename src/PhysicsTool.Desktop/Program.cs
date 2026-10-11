using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace PhysicsTool.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--version") { Console.WriteLine("PhysicsTool " + typeof(Program).Assembly.GetName().Version); return; }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().UsePhysicsToolRendering().WithInterFont().LogToTrace();
}

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary {
            ["PhysicsBackground"] = new SolidColorBrush(Color.Parse("#303234")),
            ["PhysicsButton"] = new SolidColorBrush(Color.Parse("#45494C")),
            ["PhysicsBorder"] = new SolidColorBrush(Color.Parse("#484B4D")) };
        Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary {
            ["PhysicsBackground"] = new SolidColorBrush(Color.Parse("#F3F4F5")),
            ["PhysicsButton"] = new SolidColorBrush(Color.Parse("#FFFFFF")),
            ["PhysicsBorder"] = new SolidColorBrush(Color.Parse("#CED2D6")) };
        Styles.Add(new Style(s => s.OfType<Window>()) { Setters = {
            new Setter(Window.BackgroundProperty, new DynamicResourceExtension("PhysicsBackground")) } });
        Styles.Add(new Style(s => s.OfType<NumericUpDown>()) { Setters = {
            new Setter(NumericUpDown.ShowButtonSpinnerProperty, false) } });
        Styles.Add(new Style(s => s.OfType<Button>()) { Setters = {
            new Setter(Button.BackgroundProperty, new DynamicResourceExtension("PhysicsButton")),
            new Setter(Button.BorderBrushProperty, new DynamicResourceExtension("PhysicsBorder")),
            new Setter(Button.BorderThicknessProperty, new Thickness(1)),
            new Setter(Button.CornerRadiusProperty, new CornerRadius(3)) } });
        var preferences = DesktopPreferences.Load();
        UiLanguage.Initialize(preferences.Language);
        RequestedThemeVariant = preferences.DarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(desktop.Args?.FirstOrDefault(a => !a.StartsWith('-')));
        base.OnFrameworkInitializationCompleted();
    }
}
