using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;

namespace PhysicsTool.Desktop;

internal sealed class PlayerArmorWindow : Window
{
    private readonly Dictionary<PlayerArmorSlot, ComboBox> _slots = new();
    private readonly CancellationTokenSource _lifetime = new();

    internal PlayerArmorWindow(PlayerArmorCatalog catalog, IReadOnlyDictionary<PlayerArmorSlot, string> equipped,
        Func<IReadOnlyDictionary<PlayerArmorSlot, string>, CancellationToken, Task> apply,
        PlayerWeaponCatalog? weapons = null, IReadOnlyDictionary<PlayerWeaponKind, PlayerWeaponSelection>? selectedWeapons = null,
        Func<IReadOnlyDictionary<PlayerArmorSlot, string>, IReadOnlyDictionary<PlayerWeaponKind, PlayerWeaponSelection>, CancellationToken, Task>? applyEquipment = null)
    {
        Title = "Player equipment"; Width = 640; MinWidth = 560;
        SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(20) };
        var grid = new Grid { ColumnDefinitions = new("Auto,*"), RowDefinitions = new("Auto,Auto,Auto"), RowSpacing = 12, ColumnSpacing = 16 };
        var weaponPanel = weapons == null ? null : new PlayerWeaponsPanel(weapons, selectedWeapons ?? new Dictionary<PlayerWeaponKind, PlayerWeaponSelection>());
        var tabs = new TabControl { ItemsSource = new[] { new TabItem { Header = "Armor", Content = grid },
            new TabItem { Header = "Weapons", Content = weaponPanel, IsVisible = weaponPanel != null } } };
        grid.Margin = new Thickness(0, 16, 0, 0);
        panel.Children.Add(tabs);
        foreach (var slot in Enum.GetValues<PlayerArmorSlot>())
        {
            var label = new TextBlock { Text = slot switch { PlayerArmorSlot.Head => "Head", PlayerArmorSlot.Upper => "Upper body", _ => "Lower body" }, VerticalAlignment = VerticalAlignment.Center };
            var choices = catalog.Entries.Where(e => e.Slot == slot).ToArray();
            var selector = new ComboBox { Name = "PlayerArmor" + slot, ItemsSource = choices, MinWidth = 280,
                HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 360, IsTextSearchEnabled = true,
                SelectedItem = choices.FirstOrDefault(e => e.Name == equipped.GetValueOrDefault(slot)) };
            _slots.Add(slot, selector);
            Grid.SetRow(label, (int)slot); grid.Children.Add(label);
            Grid.SetRow(selector, (int)slot); Grid.SetColumn(selector, 1); grid.Children.Add(selector);
        }
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        panel.Children.Add(status);
        var buttons = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
        var defaults = new Button { Content = "Defaults" };
        var accept = new Button { Content = "Apply", Name = "ApplyPlayerArmor" };
        var close = new Button { Content = "Close" };
        buttons.Children.Add(defaults); Grid.SetColumn(accept, 2); buttons.Children.Add(accept);
        Grid.SetColumn(close, 3); buttons.Children.Add(close); panel.Children.Add(buttons);
        Content = panel;
        var current = new Dictionary<PlayerArmorSlot, string>(equipped);
        var currentWeapons = new Dictionary<PlayerWeaponKind, PlayerWeaponSelection>(selectedWeapons ?? new Dictionary<PlayerWeaponKind, PlayerWeaponSelection>());
        bool busy = false, closed = false;
        void UpdateApply() => accept.IsEnabled = !busy && _slots.All(p => p.Value.SelectedItem is PlayerArmorEntry) &&
            (_slots.Any(p => ((PlayerArmorEntry)p.Value.SelectedItem!).Name != current.GetValueOrDefault(p.Key)) ||
            (weaponPanel != null && (weaponPanel.Selection.Count != currentWeapons.Count || weaponPanel.Selection.Any(p => currentWeapons.GetValueOrDefault(p.Key) != p.Value))));
        foreach (var selector in _slots.Values) selector.SelectionChanged += (_, _) => UpdateApply();
        if (weaponPanel != null) weaponPanel.Changed += UpdateApply;
        defaults.Click += (_, _) => { foreach (var (slot, selector) in _slots) selector.SelectedItem = catalog.Default(slot); weaponPanel?.Clear(); };
        close.Click += (_, _) => Close();
        Closed += (_, _) => { closed = true; _lifetime.Cancel(); if (!busy) _lifetime.Dispose(); };
        accept.Click += async (_, _) =>
        {
            if (busy) return;
            var selection = _slots.ToDictionary(p => p.Key, p => ((PlayerArmorEntry)p.Value.SelectedItem!).Name);
            var weaponSelection = weaponPanel?.Selection ?? new();
            busy = true; tabs.IsEnabled = defaults.IsEnabled = false; UpdateApply();
            status.Text = "Loading equipment..."; status.IsVisible = true;
            try
            {
                if (applyEquipment != null) await applyEquipment(selection, weaponSelection, _lifetime.Token);
                else await apply(selection, _lifetime.Token);
                current = selection; currentWeapons = weaponSelection; status.Text = "Equipment applied.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { status.Text = UserFacingExceptionFormatter.Format(ex); }
            finally { busy = false; tabs.IsEnabled = defaults.IsEnabled = true; UpdateApply(); if (closed) _lifetime.Dispose(); }
        };
        UpdateApply();
    }
}
