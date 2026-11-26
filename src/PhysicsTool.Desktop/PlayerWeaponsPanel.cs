using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using HKCLTool;

namespace PhysicsTool.Desktop;

internal sealed class PlayerWeaponsPanel : Grid
{
    private readonly Dictionary<PlayerWeaponKind, (ComboBox Actor, ComboBox State)> _rows = new();
    private bool _updating;
    private readonly ComboBox? _arrows;
    internal event Action? Changed;
    internal Dictionary<PlayerWeaponKind, PlayerWeaponSelection> Selection => _rows
        .Where(p => p.Value.Actor.SelectedItem is PlayerWeaponEntry)
        .ToDictionary(p => p.Key, p => new PlayerWeaponSelection(((PlayerWeaponEntry)p.Value.Actor.SelectedItem!).Name, p.Value.State.SelectedIndex == 1,
            p.Key == PlayerWeaponKind.Bow && _arrows?.SelectedItem is PlayerArrowEntry arrow ? arrow.Name : "NormalArrow"));

    internal PlayerWeaponsPanel(PlayerWeaponCatalog catalog, IReadOnlyDictionary<PlayerWeaponKind, PlayerWeaponSelection> selected)
    {
        ColumnDefinitions = new("Auto,*,Auto"); RowDefinitions = new("Auto,Auto,Auto,Auto,Auto");
        ColumnSpacing = 12; RowSpacing = 12; Margin = new Thickness(0, 16, 0, 0);
        foreach (var kind in Enum.GetValues<PlayerWeaponKind>())
        {
            var label = new TextBlock { Text = kind switch { PlayerWeaponKind.Sword => "Swords", PlayerWeaponKind.LSword => "Longswords",
                PlayerWeaponKind.Spear => "Spears", PlayerWeaponKind.Shield => "Shields", _ => "Bows" }, VerticalAlignment = VerticalAlignment.Center };
            var choices = new object[] { UiLanguage.Text("None") }.Concat(catalog.Entries.Where(e => e.Kind == kind)).ToArray();
            var actor = new ComboBox { Name = "PlayerWeapon" + kind, ItemsSource = choices, MinWidth = 160,
                HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 360, IsTextSearchEnabled = true };
            actor.SelectedItem = choices.OfType<PlayerWeaponEntry>().FirstOrDefault(e => e.Name == selected.GetValueOrDefault(kind)?.Name) ?? choices[0];
            var state = new ComboBox { Name = "PlayerWeaponState" + kind, MinWidth = 140,
                ItemsSource = new[] { new ComboBoxItem { Content = "Unequipped" }, new ComboBoxItem { Content = "Equipped" } },
                SelectedIndex = selected.GetValueOrDefault(kind)?.Equipped == true ? 1 : 0, IsEnabled = actor.SelectedItem is PlayerWeaponEntry };
            ToolTip.SetTip(state, kind == PlayerWeaponKind.Bow ? "Unequipped: back. Equipped: bow in the left hand, arrow in the right. The quiver stays on the hip." :
                kind == PlayerWeaponKind.Shield ? "Unequipped: back. Equipped: left hand." : "Unequipped: back. Equipped: right hand. The sheath stays on the back.");
            foreach (var (control, column) in new (Control, int)[] { (label, 0), (actor, 1), (state, 2) })
            { SetRow(control, (int)kind); SetColumn(control, column); Children.Add(control); }
            _rows.Add(kind, (actor, state));
        }
        foreach (var (kind, row) in _rows)
        {
            row.Actor.SelectionChanged += (_, _) => Update(kind, true);
            row.State.SelectionChanged += (_, _) => Update(kind, false);
        }
        if (catalog.SupportsArrowTypes)
        {
            RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = "Arrows", VerticalAlignment = VerticalAlignment.Center };
            _arrows = new ComboBox { Name = "PlayerArrowType", ItemsSource = catalog.Arrows, MaxDropDownHeight = 360,
                HorizontalAlignment = HorizontalAlignment.Stretch, IsTextSearchEnabled = true,
                SelectedItem = catalog.Arrows.FirstOrDefault(a => a.Name == (selected.GetValueOrDefault(PlayerWeaponKind.Bow)?.Arrow ?? "NormalArrow")),
                IsEnabled = _rows[PlayerWeaponKind.Bow].Actor.SelectedItem is PlayerWeaponEntry };
            SetRow(label, 5); Children.Add(label); SetRow(_arrows, 5); SetColumn(_arrows, 1); Children.Add(_arrows);
            _arrows.SelectionChanged += (_, _) => { if (!_updating) Changed?.Invoke(); };
        }
    }
    internal void Clear()
    {
        _updating = true;
        try
        {
            foreach (var row in _rows.Values) { row.Actor.SelectedIndex = 0; row.State.SelectedIndex = 0; row.State.IsEnabled = false; }
            if (_arrows != null) { _arrows.SelectedItem = _arrows.Items.OfType<PlayerArrowEntry>().FirstOrDefault(a => a.Name == "NormalArrow"); _arrows.IsEnabled = false; }
        }
        finally { _updating = false; }
        Changed?.Invoke();
    }
    private void Update(PlayerWeaponKind kind, bool actorChanged)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var row = _rows[kind];
            if (_arrows != null) _arrows.IsEnabled = _rows[PlayerWeaponKind.Bow].Actor.SelectedItem is PlayerWeaponEntry;
            row.State.IsEnabled = row.Actor.SelectedItem is PlayerWeaponEntry;
            if (!row.State.IsEnabled) row.State.SelectedIndex = 0;
            else
            {
                if (actorChanged && kind <= PlayerWeaponKind.Spear)
                    foreach (var other in _rows.Where(p => p.Key <= PlayerWeaponKind.Spear && p.Key != kind).Select(p => p.Value))
                    { other.Actor.SelectedIndex = 0; other.State.SelectedIndex = 0; other.State.IsEnabled = false; }
                if (row.State.SelectedIndex == 1)
                    foreach (var other in _rows.Where(p => p.Key != kind && (kind == PlayerWeaponKind.Bow || p.Key == PlayerWeaponKind.Bow)).Select(p => p.Value))
                        other.State.SelectedIndex = 0;
            }
        }
        finally { _updating = false; }
        Changed?.Invoke();
    }
}
