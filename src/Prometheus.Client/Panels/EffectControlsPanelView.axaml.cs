using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Prometheus.Client.Services;

namespace Prometheus.Client.Panels;

public partial class EffectControlsPanelView : UserControl
{
    private readonly EffectLibrarySettings _settings = EffectLibrarySettings.Load();
    private readonly HashSet<string> _selectedEffects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Border> _rows = new(StringComparer.Ordinal);
    private EffectCatalogEntry[] _visibleEffects = [];
    private string? _selectionAnchor;
    private string _activeTab = "all";

    public EffectControlsPanelView()
    {
        InitializeComponent();
        ImportMgtTransition.Click += async (_, _) => await ImportMgtTransitionAsync();
        AttachedToVisualTree += (_, _) => AttachTransitionLibrary();
        DetachedFromVisualTree += (_, _) => DetachTransitionLibrary();
        SearchBox.TextChanged += (_, _) => RefreshEffects();
        PreviousTabButton.Click += (_, _) => ScrollTabs(-150);
        NextTabButton.Click += (_, _) => ScrollTabs(150);
        AddTabButton.Click += (_, _) => ShowTabEditor(null);
        SizeChanged += (_, _) => UpdateTabNavigation();
        RefreshTabs();
        RefreshEffects();
    }

    private void ScrollTabs(double distance)
    {
        var maximum = Math.Max(0, TabScroll.Extent.Width - TabScroll.Viewport.Width);
        TabScroll.Offset = new Vector(Math.Clamp(TabScroll.Offset.X + distance, 0, maximum), 0);
    }

    private static string TabName(string id) => id switch
    {
        "all" => "Todos", "favorites" => "Favoritos", "personalized" => "Personalizado", "video" => "Vídeo",
        "transitions" => "Transiciones", "audio" => "Audio", _ => id
    };

    private void RefreshTabs()
    {
        TabBar.Children.Clear();
        AddTab("all", "Todos");
        foreach (var id in _settings.TabOrder)
            AddTab(id, _settings.CustomTabs.FirstOrDefault(t => t.Id == id)?.Name ?? TabName(id));
        _tabReorder = new TabReorderGesture(TabBar, TabBar.Children.OfType<Button>(), 1,
            (oldIndex, newIndex) =>
            {
                var id = _settings.TabOrder[oldIndex - 1];
                _settings.TabOrder.RemoveAt(oldIndex - 1);
                _settings.TabOrder.Insert(newIndex - 1, id);
                SaveSettings();
                RefreshTabs();
            });
        Dispatcher.UIThread.Post(UpdateTabNavigation, DispatcherPriority.Background);
    }

    private TabReorderGesture? _tabReorder;

    private void UpdateTabNavigation()
    {
        if (Bounds.Width <= 0) return;
        var available = Math.Max(70, Bounds.Width - 105);
        TabScroll.MaxWidth = available;
        TabBar.Measure(new Size(double.PositiveInfinity, 32));
        var overflow = TabBar.DesiredSize.Width > available + 1;
        PreviousTabButton.IsVisible = NextTabButton.IsVisible = overflow;
        if (!overflow) TabScroll.Offset = new Vector(0, 0);
    }

    private void AddTab(string id, string title)
    {
        var selected = _activeTab == id;
        var button = new Button { Content = title, FontSize = 11,
            Foreground = selected ? Brushes.Black : Brushes.LightGray,
            Padding = new Thickness(11, 4, 11, 2), MinHeight = 29,
            CornerRadius = new CornerRadius(11, 11, 0, 0),
            Background = new SolidColorBrush(Color.Parse(selected ? "#FF8A00" : "#181818")),
            BorderBrush = new SolidColorBrush(Color.Parse(selected ? "#FF8A00" : "#5A5A5A")),
            BorderThickness = new Thickness(1, 1, 1, 0),
            Margin = new Thickness(id == "all" ? 0 : -4, 0, 0, 0),
            ZIndex = selected ? 10 : 0 };
        button.Click += (_, _) => { if (_tabReorder?.SuppressClick == true) return;
            _activeTab = id; _selectedEffects.Clear(); _selectionAnchor = null;
            RefreshTabs(); RefreshEffects(); };
        var custom = _settings.CustomTabs.FirstOrDefault(t => t.Id == id);
        if (id == "personalized")
        {
            var edit = new MenuItem { Header = "Elegir efectos…" };
            edit.Click += (_, _) => ShowPersonalizedEditor(button);
            button.ContextMenu = new ContextMenu { ItemsSource = new[] { edit } };
        }
        if (custom != null)
        {
            var edit = new MenuItem { Header = "Editar pestaña…" };
            edit.Click += (_, _) => ShowTabEditor(custom);
            var remove = new MenuItem { Header = "Eliminar pestaña" };
            remove.Click += (_, _) =>
            {
                _settings.CustomTabs.RemoveAll(t => t.Id == id);
                _settings.TabOrder.Remove(id);
                if (_activeTab == id) _activeTab = "all";
                SaveSettings(); RefreshTabs(); RefreshEffects();
            };
            button.ContextMenu = new ContextMenu { ItemsSource = new[] { edit, remove } };
        }
        TabBar.Children.Add(button);
    }

    private void ShowPersonalizedEditor(Control anchor)
    {
        var search = new TextBox { Watermark = "Buscar efecto…", Width = 255 };
        var rows = new StackPanel { Spacing = 3 };
        var list = new ScrollViewer { MaxHeight = 310, Content = rows };
        var body = new StackPanel { Spacing = 7, Children =
        {
            new TextBlock { Text = "Efectos de Personalizado", FontWeight = FontWeight.SemiBold }, search, list
        } };
        var flyout = new Flyout { Content = new Border { Background = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.Parse("#FF8A00")), BorderThickness = new Thickness(1),
            Padding = new Thickness(10), Child = body } };
        void Refresh()
        {
            rows.Children.Clear();
            var query = search.Text?.Trim();
            foreach (var effect in EffectCatalog.Available.Where(effect => string.IsNullOrEmpty(query) ||
                effect.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                var check = new CheckBox { Content = effect.Name, IsChecked = _settings.Personalized.Contains(effect.Id),
                    FontSize = 11 };
                check.IsCheckedChanged += (_, _) =>
                {
                    if (check.IsChecked == true && !_settings.Personalized.Contains(effect.Id))
                        _settings.Personalized.Add(effect.Id);
                    else if (check.IsChecked != true) _settings.Personalized.Remove(effect.Id);
                    SaveSettings();
                    if (_activeTab == "personalized") RefreshEffects();
                };
                rows.Children.Add(check);
            }
        }
        search.TextChanged += (_, _) => Refresh();
        Refresh();
        flyout.ShowAt(anchor);
        search.Focus();
    }

    private void MoveTab(string source, string target, bool after)
    {
        if (source == target || !_settings.TabOrder.Contains(source) || !_settings.TabOrder.Contains(target)) return;
        _settings.TabOrder.Remove(source);
        var index = _settings.TabOrder.IndexOf(target) + (after ? 1 : 0);
        _settings.TabOrder.Insert(index, source);
        SaveSettings(); RefreshTabs();
    }

    private void ShowTabEditor(EffectLibraryCustomTab? existing)
    {
        var families = EffectCatalog.Available.Select(e => e.FamilyKey).Distinct().ToArray();
        var name = new TextBox { Text = existing?.Name ?? NextTabName(), Watermark = "Nombre de pestaña", Width = 210 };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = existing == null ? "Nueva pestaña" : "Editar pestaña", FontWeight = FontWeight.SemiBold });
        body.Children.Add(name);
        body.Children.Add(new TextBlock { Text = "Familias visibles", FontSize = 10, Foreground = Brushes.Gray });
        var checks = new List<(string Family, CheckBox Check)>();
        foreach (var family in families)
        {
            var label = family.Replace("video:", "Vídeo · ").Replace("audio:", "Audio · ")
                .Replace("transitions:", "Transiciones · ");
            var check = new CheckBox { Content = label, IsChecked = existing?.Families.Contains(family) == true || existing == null };
            checks.Add((family, check)); body.Children.Add(check);
        }
        var save = new Button { Content = existing == null ? "Crear pestaña" : "Guardar", HorizontalAlignment = HorizontalAlignment.Right };
        body.Children.Add(save);
        var flyout = new Flyout { Content = new Border { Background = Brushes.Black, Padding = new Thickness(12),
            BorderBrush = new SolidColorBrush(Color.Parse("#FF8A00")), BorderThickness = new Thickness(1), Child = body } };
        save.Click += (_, _) =>
        {
            var title = name.Text?.Trim();
            var selected = checks.Where(x => x.Check.IsChecked == true).Select(x => x.Family).ToList();
            if (string.IsNullOrWhiteSpace(title) || selected.Count == 0) return;
            var tab = existing ?? new EffectLibraryCustomTab();
            tab.Name = title;
            tab.Families = selected;
            if (existing == null) { _settings.CustomTabs.Add(tab); _settings.TabOrder.Add(tab.Id); }
            _activeTab = tab.Id;
            SaveSettings(); RefreshTabs(); RefreshEffects(); flyout.Hide();
        };
        flyout.ShowAt(AddTabButton);
        name.Focus(); name.SelectAll();
    }

    private string NextTabName()
    {
        for (var number = 1; ; number++)
        {
            var candidate = "Mi pestaña " + number;
            if (_settings.CustomTabs.All(t => !string.Equals(t.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    private void SaveSettings()
    {
        try { _settings.Save(); }
        catch (Exception ex) { LibraryStatus.Text = "No se guardó la biblioteca: " + ex.Message; }
    }

    private void RefreshEffects()
    {
        EffectList.Children.Clear();
        _rows.Clear();
        var project = ProjectController.Instance?.CurrentProject;
        ImportMgtTransition.IsVisible = _activeTab == "transitions" && MgtTransitionCatalog.ImportEnabled;
        var items = EffectCatalog.Available.Concat(project?.MgtTransitionLibrary.Select(MgtTransitionCatalog.Entry) ?? []);
        items = _activeTab switch
        {
            "favorites" => items.Where(e => _settings.Favorites.Contains(e.Id)),
            "personalized" => items.Where(e => _settings.Personalized.Contains(e.Id)),
            "video" or "audio" or "transitions" => items.Where(e => e.Kind == _activeTab),
            "all" => items,
            _ => items.Where(e => _settings.CustomTabs.FirstOrDefault(t => t.Id == _activeTab)?.Families.Contains(e.FamilyKey) == true)
        };
        var query = SearchBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(query)) items = items.Where(e =>
            e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.Family.Contains(query, StringComparison.OrdinalIgnoreCase));
        var visible = items.ToArray();
        _visibleEffects = visible;
        _selectedEffects.RemoveWhere(id => !visible.Any(effect => effect.Id == id));
        var transitionsTab = _activeTab == "transitions";
        var item = transitionsTab ? "transición" : "efecto";
        var itemsPlural = transitionsTab ? "transiciones" : "efectos";
        var selected = transitionsTab ? "seleccionadas" : "seleccionados";
        LibraryStatus.Text = (visible.Length == 1 ? $"1 {item} disponible" : $"{visible.Length} {itemsPlural} disponibles") +
            (_selectedEffects.Count > 0 ? $" · {_selectedEffects.Count} {selected}" : "");
        if (visible.Length == 0)
        {
            EffectList.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(query)
                ? _activeTab == "personalized" ? "Clic derecho en Personalizado para elegir efectos" :
                    "No hay efectos disponibles en esta pestaña" : "No hay resultados para esta búsqueda",
                Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(5, 15) });
            return;
        }
        foreach (var group in visible.GroupBy(e => e.FamilyKey))
        {
            EffectList.Children.Add(new TextBlock { Text = (group.First().Kind == "video" ? "VÍDEO" :
                    group.First().Kind == "audio" ? "AUDIO" : "TRANSICIONES") + " / " + group.First().Family.ToUpperInvariant(),
                FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#FF8A00")),
                Margin = new Thickness(3, 7, 0, 0) });
            foreach (var effect in group) EffectList.Children.Add(CreateRow(effect));
        }
    }

    private Control CreateRow(EffectCatalogEntry effect)
    {
        var favorite = new Button { Content = _settings.Favorites.Contains(effect.Id) ? "★" : "☆",
            Width = 26, Height = 25, Padding = new Thickness(0), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(Color.Parse("#FF8A00")) };
        ToolTip.SetTip(favorite, "Añadir o quitar de Favoritos");
        favorite.Click += (_, _) =>
        {
            if (!_settings.Favorites.Remove(effect.Id)) _settings.Favorites.Add(effect.Id);
            SaveSettings(); RefreshEffects();
        };
        var label = new TextBlock { Text = effect.Name, FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(label, effect.Description);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(favorite);
        Grid.SetColumn(label, 1); grid.Children.Add(label);
        var row = new Border { Background = Brushes.Transparent, BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent, CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1), MinHeight = 28, Child = grid };
        _rows[effect.Id] = row;
        SetRowSelection(row, _selectedEffects.Contains(effect.Id));
        Point dragStart = default;
        var pressed = false;
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed || e.GetPosition(row).X <= 33) return;
            pressed = true;
            dragStart = e.GetPosition(row);
            SelectEffect(effect.Id, e.KeyModifiers);
        };
        row.PointerReleased += (_, _) => pressed = false;
        row.PointerMoved += async (_, e) =>
        {
            if (!pressed || !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            var point = e.GetPosition(row);
            if (Math.Abs(point.X - dragStart.X) + Math.Abs(point.Y - dragStart.Y) < 8) return;
            pressed = false;
            var ids = _visibleEffects.Where(item => _selectedEffects.Contains(item.Id))
                .Select(item => item.Id).ToArray();
            if (ids.Length == 0) ids = [effect.Id];
            var data = new DataObject(); data.Set(EffectApplicationService.DragFormat, ids);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        };
        return row;
    }

    private void SelectEffect(string id, KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Shift) && _selectionAnchor != null)
        {
            if (!modifiers.HasFlag(KeyModifiers.Control)) _selectedEffects.Clear();
            var start = Array.FindIndex(_visibleEffects, item => item.Id == _selectionAnchor);
            var end = Array.FindIndex(_visibleEffects, item => item.Id == id);
            if (start >= 0 && end >= 0)
                for (var index = Math.Min(start, end); index <= Math.Max(start, end); index++)
                    _selectedEffects.Add(_visibleEffects[index].Id);
        }
        else if (modifiers.HasFlag(KeyModifiers.Control))
        {
            if (!_selectedEffects.Add(id)) _selectedEffects.Remove(id);
            _selectionAnchor = id;
        }
        else { _selectedEffects.Clear(); _selectedEffects.Add(id); _selectionAnchor = id; }
        foreach (var (effectId, row) in _rows) SetRowSelection(row, _selectedEffects.Contains(effectId));
        LibraryStatus.Text = $"{_visibleEffects.Length} efectos disponibles · {_selectedEffects.Count} seleccionados";
    }

    private static void SetRowSelection(Border row, bool selected)
    {
        row.Background = new SolidColorBrush(Color.Parse(selected ? "#332313" : "#080808"));
        row.BorderBrush = new SolidColorBrush(Color.Parse(selected ? "#FF8A00" : "#202020"));
    }
}
