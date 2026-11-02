using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private Control _mainContent = null!;
    private CollisionView? _collisionView;
    private BphscView? _bphscView;
    private ShrineSceneView? _shrineView;
    private BphysicsView? _bphysicsView;
    internal BphysicsView? BphysicsWorkspace => _bphysicsView;
    private Func<Task>? _compoundBack;
    private bool _confirmingClose;
    internal bool IsToolWorkspaceVisible => !ReferenceEquals(Content, _mainContent);
    private bool ToolWorkspaceBusy => _collisionView?.Busy == true || _bphscView?.Busy == true || _shrineView?.Busy == true;
    internal CollisionView? CollisionWorkspace => _collisionView;

    private void ShowBphysics(string path)
    {
        if (_bphysicsView?.Dirty == true) throw new InvalidOperationException("Save or close the current BPHYSICS editor before opening another file.");
        var view = new BphysicsView(this, path);
        PauseEditorAnimation(); _actorPlaying = false; StopSimulation();
        _bphysicsView = view;
        ShowToolWorkspace(view, "BPHYSICS", async () =>
        {
            if (!await view.CanCloseAsync()) return;
            _bphysicsView = null; Content = _mainContent;
        });
    }

    internal void ShowCollision(bool graphics = true)
    {
        PauseEditorAnimation(); _actorPlaying = false; StopSimulation();
        _collisionView ??= new CollisionView(graphics, _actorScene?.Resources.SourcePath, _actorScene?.Resources.SourceEntry)
        { OpenStaticCompound = path => { _compoundBack = null; return ShowStaticCompoundAsync(path); }, OpenShrineScene = ShowShrineScene };
        _collisionView.SetActorSource(_actorScene?.Resources.SourcePath, _actorScene?.Resources.SourceEntry);
        ShowToolWorkspace(_collisionView, "Collision", () => { Content = _mainContent; return Task.CompletedTask; });
    }

    internal async Task ShowStaticCompoundAsync(string? path)
    {
        if (path != null && _bphscView != null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(_bphscView.SourcePath.Length == 0 ? "." : _bphscView.SourcePath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) path = null;
        if (path != null && _bphscView != null && !await _bphscView.CanCloseAsync()) return;
        _bphscView ??= new BphscView(this);
        if (path != null) await _bphscView.LoadPathAsync(path);
        ShowToolWorkspace(_bphscView, "Static compound (BPHSC)", _compoundBack ?? (() => { ShowCollision(ModelSurface != null); return Task.CompletedTask; }));
    }

    internal void ShowShrineScene()
    {
        _shrineView ??= new ShrineSceneView(this, ModelSurface != null) { OpenCompound = path =>
        {
            _compoundBack = () => { ShowShrineScene(); return Task.CompletedTask; };
            return ShowStaticCompoundAsync(path);
        } };
        ShowToolWorkspace(_shrineView, "Shrine scene", () => { ShowCollision(ModelSurface != null); return Task.CompletedTask; });
    }

    private void ShowToolWorkspace(Control view, string title, Func<Task> back)
    {
        // Detach retained controls from their old host before reusing the workspace.
        if (view.Parent is Panel parent) parent.Children.Remove(view);
        var shell = new DockPanel();
        var footer = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(12, 6) };
        footer.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Name = "WorkspaceBack", Content = "Back", MinWidth = 90 };
        button.Click += async (_, _) => { if (!ToolWorkspaceBusy) await back(); };
        Grid.SetColumn(button, 1); footer.Children.Add(button); DockPanel.SetDock(footer, Dock.Bottom); shell.Children.Add(footer);
        shell.Children.Add(view); Content = shell;
    }
}
