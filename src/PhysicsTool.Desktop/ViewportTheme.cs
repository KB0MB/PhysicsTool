using Avalonia.Media;
using Avalonia.Styling;

namespace PhysicsTool.Desktop;

public sealed partial class PhysicsViewport
{
    internal bool IsLightViewport => ActualThemeVariant == ThemeVariant.Light;
    internal Color ViewportBackground => ViewportColor("#242829", "#E8ECEE");
    private Color ViewportColor(string dark, string light) => Color.Parse(IsLightViewport ? light : dark);
    private IBrush SelectionBrush => new SolidColorBrush(ViewportColor("#FFFFFF", "#183B56"));

}
