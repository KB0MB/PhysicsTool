namespace PhysicsTool.Core;

internal enum IconImageFormat { Sbitemico, Bntx, BntxZs, Png }

internal static class IconImageFormats
{
    internal static string Extension(this IconImageFormat format) => format switch
    {
        IconImageFormat.Sbitemico => ".sbitemico", IconImageFormat.Bntx => ".bntx",
        IconImageFormat.BntxZs => ".bntx.zs", _ => ".png"
    };
    internal static string Label(this IconImageFormat format) => format switch
    {
        IconImageFormat.Sbitemico => "BotW (.sbitemico)", IconImageFormat.Bntx => "BNTX (.bntx)",
        IconImageFormat.BntxZs => "TotK (.bntx.zs)", _ => "PNG"
    };
}
