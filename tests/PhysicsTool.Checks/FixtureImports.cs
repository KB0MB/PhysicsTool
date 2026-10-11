using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;

// Generated Assimp fixtures exercise skinning/transport independently of the BFRES-only file pickers.
internal static class FixtureImports
{
    internal static Task Model(MainWindow window, string path) => window.LoadModelAsync(() => PortableModelImporter.LoadRig(path), path);
    internal static Task Animation(MainWindow window, string path) => window.LoadAnimationsAsync(() => ModelAnimationClip.Load(path), path);
}
