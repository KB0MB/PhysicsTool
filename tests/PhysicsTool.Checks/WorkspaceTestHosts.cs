using Avalonia.Controls;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;

// Standalone hosts keep the existing rendering checks reusable; the app embeds these views.
internal sealed class CollisionWindow : Window
{
    private readonly CollisionView _view;
    internal CollisionWindow(bool graphics = true)
    { Width = 1180; Height = 780; Content = _view = new CollisionView(graphics); }
    internal PhysicsViewport Viewport => _view.Viewport;
    internal ModelGlControl? ModelSurface => _view.ModelSurface;
    internal Task LoadPathAsync(string path, bool model) => _view.LoadPathAsync(path, model);
    internal Task PrepareAsync() => _view.PrepareAsync();
    internal Task WriteAsync(string path) => _view.WriteAsync(path);
}

internal sealed class BphscWindow : Window
{
    private readonly BphscView _view;
    internal BphscWindow() { Width = 1100; Height = 730; Content = _view = new BphscView(this); }
    internal BphscDocument? Document => _view.Document;
    internal Task LoadPathAsync(string path) => _view.LoadPathAsync(path);
    internal void Edit(Action<BphscDocument> edit) => _view.Edit(edit);
    internal void Undo() => _view.Undo();
    internal void Redo() => _view.Redo();
}
