using Avalonia;

namespace PhysicsTool.Desktop;

internal static class DesktopPlatform
{
    internal static AppBuilder UsePhysicsToolRendering(this AppBuilder builder)
    {
        // EGL also supports OpenGL ES devices; GLX remains a desktop fallback.
        if (OperatingSystem.IsLinux())
            builder.With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software] });
        return builder;
    }
}
