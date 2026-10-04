namespace HKCLTool;

internal static class VisualModelImporter
{
    public static Func<VisualModelTexture?>? EyeGlint { get; set; }
    public static VisualModelTexture? GetPackagedEyeGlintTexture() => EyeGlint?.Invoke();
}

internal static class AppLocalization
{
    public static string Translate(string text) => text;
}
