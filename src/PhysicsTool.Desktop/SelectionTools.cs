using Avalonia.Controls;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private readonly SelectionClipboard _selectionClipboard = new();
    private readonly WrapPanel _selectionTools = new();
    private readonly List<(MenuItem Menu, int Action)> _selectionMenus = new();
    private Button _copySelection = null!, _pasteSelection = null!, _mirrorSelection = null!;
    private Button _mirrorCloth = null!;
    private PhysicsSelectionKind SelectionKind => (PhysicsSelectionKind)_mode.SelectedIndex;
    private bool SelectionEditable => IsEditorVisible && !IsActorMode && !_timer.IsEnabled && !_viewport.TransformActive && Session.HasSelectedEntry && !Session.Document.IsHelperBoneSidecar && !Session.Document.IsHkrg;
    private HashSet<int> SelectionIndices => _items.SelectedItems?.Cast<Item>().Select(i => i.Index).ToHashSet() ?? [];
    private void ConfigureSelectionTools(WrapPanel footer)
    {
        _copySelection = ViewportTools.Icon("Copy", "Copy selected items (Ctrl/Command+C). Item values, not constraints.", () => Try(CopySelection)); _copySelection.Name = "CopySelection";
        _pasteSelection = ViewportTools.Icon("ClipboardPaste", "Paste selected items (Ctrl/Command+V). Shift adds mirrored paste on X.", () => Try(() => PasteSelection(false))); _pasteSelection.Name = "PasteSelection";
        _mirrorSelection = ViewportTools.Icon("FlipHorizontal2", "Mirror selected positions/geometry on world axes", () => _mirrorSelection.ContextMenu!.Open(_mirrorSelection)); _mirrorSelection.Name = "MirrorSelection";
        _mirrorSelection.ContextMenu = new ContextMenu { ItemsSource = MirrorMenus() };
        _selectionTools.Children.Add(_copySelection); _selectionTools.Children.Add(_pasteSelection); _selectionTools.Children.Add(_mirrorSelection); footer.Children.Add(_selectionTools);
        _mirrorCloth = Add(footer, "Mirror cloth X", async () => {
            if (!SelectionEditable) return;
            Session.Document.ValidateWholeClothMirror(Session.ClothIndex);
            if (await Dialogs.Confirm(this, "Mirror cloth", "Mirror this entire cloth across global X, including its bones, particles, collision shapes, skin references and left/right names?"))
                Edit((doc, cloth) => WholeClothMirror.Apply(doc, cloth));
        });
        _mirrorCloth.Name = "MirrorCloth";
        ToolTip.SetTip(_mirrorCloth, "Mirror the whole cloth, not just selected particles. Undo restores the complete graph. Shared or unvalidated layouts are rejected before changes.");
        MenuItem[] EditingMenus() => [SelectionMenu("Copy", 0, CopySelection), SelectionMenu("Paste", 1, () => PasteSelection(false)), SelectionMenu("Paste mirrored X", 1, () => PasteSelection(true)), .. MirrorMenus()];
        _items.ContextMenu = new ContextMenu { ItemsSource = EditingMenus() };
        var menu = _viewport.ContextMenu!;
        menu.ItemsSource = new object[] { new MenuItem { Header = "Edit selection", ItemsSource = EditingMenus() }, new Separator() }.Concat(menu.ItemsSource!.Cast<object>()).ToArray();
    }
    private MenuItem SelectionMenu(string text, int kind, Action action)
    {
        var menu = new MenuItem { Header = text }; menu.Click += (_, _) => Try(action); _selectionMenus.Add((menu, kind)); return menu;
    }
    private MenuItem[] MirrorMenus() => new[] { false, true }.Select(center => new MenuItem {
        Header = center ? "Mirror about selection center" : "Mirror about world origin",
        ItemsSource = Enumerable.Range(0, 3).Select(axis => SelectionMenu("XYZ"[axis].ToString(), 2, () => MirrorSelection(axis, center))).ToArray()
    }).ToArray();
    private void UpdateSelectionTools()
    {
        var supported = Session.Document.HasDocument && !Session.Document.IsHelperBoneSidecar && !Session.Document.IsHkrg;
        _selectionTools.IsVisible = supported && !IsActorMode;
        _mirrorCloth.IsVisible = supported && !IsActorMode;
        _mirrorCloth.IsEnabled = SelectionEditable;
        var copy = SelectionEditable && SelectionIndices.Count > 0 && SelectionClipboard.Supports(Session.Document, SelectionKind);
        var paste = SelectionEditable && _selectionClipboard.CanPaste(Session.Document, Session.ClothIndex, SelectionKind);
        var mirror = SelectionEditable && SelectionIndices.Count > 0;
        _copySelection.IsEnabled = copy; _pasteSelection.IsEnabled = paste; _mirrorSelection.IsEnabled = mirror;
        foreach (var (menu, action) in _selectionMenus) menu.IsEnabled = action switch { 0 => copy, 1 => paste, _ => mirror };
    }
    private void CopySelection()
    {
        if (!SelectionEditable) return;
        _selectionClipboard.Copy(Session.Document, Session.ClothIndex, SelectionKind, SelectionIndices);
        _status.Text = $"Copied {_selectionClipboard.Count} {SelectionKind.ToString().ToLowerInvariant()}."; UpdateSelectionTools();
    }
    private void PasteSelection(bool mirrored)
    {
        if (!SelectionEditable) return;
        if (!_selectionClipboard.CanPaste(Session.Document, Session.ClothIndex, SelectionKind)) throw new InvalidOperationException("Copy supported items from this cloth and tab before pasting.");
        int[] added;
        try { added = _selectionClipboard.Paste(Session, mirrored); } finally { Refresh(); }
        _items.SelectedItems!.Clear();
        foreach (var item in _items.ItemsSource!.Cast<Item>().Where(i => added.Contains(i.Index))) _items.SelectedItems.Add(item);
        SelectViewport(); UpdateActions();
        _status.Text = $"Pasted {added.Length} {SelectionKind.ToString().ToLowerInvariant()}{(mirrored ? " mirrored on X" : "")}.";
    }
    private void MirrorSelection(int axis, bool center)
    {
        if (!SelectionEditable) return;
        var selected = SelectionIndices; var kind = SelectionKind;
        Edit((doc, cloth) => SelectionMirror.Apply(doc, cloth, kind, selected, axis, center, _follow.IsChecked == true));
        _status.Text = $"Mirrored selected {kind.ToString().ToLowerInvariant()} positions on {"XYZ"[axis]}.";
    }
}
