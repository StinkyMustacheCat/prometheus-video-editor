using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Prometheus.Client.Services;
using Prometheus.Client.Panels;
using Prometheus.Core.Model;

namespace Prometheus.Client.Views;

public sealed class LinkShelfView : UserControl
{
    private readonly ProjectController _controller;
    private readonly TextBox _input = new() { Watermark = "Pega enlaces o rutas de archivos, uno por línea", AcceptsReturn = true, MinHeight = 68, MaxHeight = 110, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _search = new() { Watermark = "Buscar en la bandeja" };
    private readonly StackPanel _items = new() { Spacing = 5 };
    private readonly TextBlock _count = new() { Foreground = Brushes.LightGray };
    private readonly TextBlock _shortcutHint = new() { Foreground = Brushes.LightGray };
    private readonly Dictionary<string, Bitmap> _bitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedIds = new();
    private readonly CheckBox _selectAll = new() { Content = "Seleccionar visibles", FontSize = 11 };
    private readonly Button _historyButton = new() { Content = "◷", Width = 32 };
    private readonly Button _addToggle = new() { Content = "+", Width = 32 };
    private readonly Button _toMedia = new() { Content = "A medios" };
    private readonly Button _toPlayhead = new() { Content = "Al lector" };
    private readonly Button _toLine = new() { Content = "A línea ▾" };
    private readonly StackPanel _addArea = new() { Spacing = 7, IsVisible = false };
    private double _lastWidth;
    private bool _draggingReference;
    private bool _pendingRefresh;
    private bool _syncingSelectAll;

    public LinkShelfView(ProjectController controller)
    {
        _controller = controller;
        Background = Brushes.Black;

        var add = Button("Añadir referencias", () => {
            if (LinkShelfService.AddText(controller, _input.Text) > 0) _addArea.IsVisible = false;
            _input.Text = string.Empty;
        });
        var files = Button("Archivos…", async () => {
            var picked = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Añadir a la bandeja", AllowMultiple = true });
            if (LinkShelfService.AddText(controller, string.Join(Environment.NewLine, picked.Select(x => x.Path.LocalPath))) > 0)
                _addArea.IsVisible = false;
        });
        var paste = Button("Desde portapapeles", async () => {
            var content = await AdaptiveClipboard.ReadAsync(this);
            if (LinkShelfService.AddText(controller, content.Files.Length > 0 ? string.Join(Environment.NewLine, content.Files) : content.Text) > 0)
                _addArea.IsVisible = false;
        });
        var top = new StackPanel { Spacing = 7 };
        UpdateShortcutHint();
        ToolTip.SetTip(_historyButton, "Historial de referencias usadas");
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        heading.Children.Add(_addToggle);
        _search.Margin = new Thickness(6, 0);
        Grid.SetColumn(_search, 1); heading.Children.Add(_search);
        Grid.SetColumn(_historyButton, 2); heading.Children.Add(_historyButton);
        top.Children.Add(heading);
        _addArea.Children.Add(_shortcutHint);
        _addArea.Children.Add(_input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        buttons.Children.Add(add);
        buttons.Children.Add(files);
        buttons.Children.Add(paste);
        _addArea.Children.Add(buttons);
        top.Children.Add(_addArea);
        var selectionHeading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 3, 0, 3) };
        selectionHeading.Children.Add(_selectAll);
        Grid.SetColumn(_count, 1); selectionHeading.Children.Add(_count);
        top.Children.Add(selectionHeading);

        var scroll = new ScrollViewer { Content = _items, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var button in new[] { _toMedia, _toPlayhead, _toLine }) bottom.Children.Add(button);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(10) };
        Grid.SetRow(top, 0);
        Grid.SetRow(scroll, 1);
        Grid.SetRow(bottom, 2);
        layout.Children.Add(top);
        layout.Children.Add(scroll);
        layout.Children.Add(bottom);
        Content = layout;
        _addToggle.Click += (_, _) => { _addArea.IsVisible = !_addArea.IsVisible; if (_addArea.IsVisible) _input.Focus(); };
        _historyButton.Click += (_, _) => ShowHistory();
        _selectAll.IsCheckedChanged += (_, _) => {
            if (_syncingSelectAll) return;
            var visible = VisibleItems().Select(x => x.Id).ToArray();
            if (_selectAll.IsChecked == true) foreach (var id in visible) _selectedIds.Add(id);
            else foreach (var id in visible) _selectedIds.Remove(id);
            Refresh();
        };
        _toMedia.Click += async (_, _) => await PromoteSelected();
        _toPlayhead.Click += async (_, _) => await InsertSelectedAtPlayhead();
        _toLine.Click += (_, _) => ShowPlacementMenu(_toLine);
        _search.TextChanged += (_, _) => Refresh();
        SizeChanged += (_, _) => { if (Math.Abs(Bounds.Width - _lastWidth) > 12) { _lastWidth = Bounds.Width; Refresh(); } };
        AttachedToVisualTree += (_, _) => { _controller.StateChanged += Refresh; TimelinePanelView.ShelfShortcutChanged += OnShortcutChanged; WebMediaService.StateChanged += OnWebStateChanged; Refresh(); LinkShelfService.ProbeMissing(_controller); };
        DetachedFromVisualTree += (_, _) => { _controller.StateChanged -= Refresh; TimelinePanelView.ShelfShortcutChanged -= OnShortcutChanged; WebMediaService.StateChanged -= OnWebStateChanged;
            foreach (var bitmap in _bitmaps.Values) bitmap.Dispose(); _bitmaps.Clear(); };
        Refresh();
    }

    public void AddCaptured(string text) => LinkShelfService.AddText(_controller, text);

    private void OnShortcutChanged(Key key, KeyModifiers modifiers) => UpdateShortcutHint();

    private void UpdateShortcutHint()
    {
        var shortcut = TimelinePanelView.GetShelfShortcut();
        _shortcutHint.Text = shortcut.Key == Key.None
            ? "Referencias rápidas · configura la captura global en Ajustes"
            : "Referencias rápidas · " +
              (shortcut.Modifiers.HasFlag(KeyModifiers.Control) ? "Ctrl+" : "") +
              (shortcut.Modifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : "") +
              (shortcut.Modifiers.HasFlag(KeyModifiers.Shift) ? "Mayús+" : "") +
              shortcut.Key + " captura la selección de otra aplicación";
    }

    private void OnWebStateChanged(string assetId)
    {
        if (_controller.CurrentProject.Assets.Any(a => a.Id == assetId)) Refresh();
    }

    private void Refresh()
    {
        if (_draggingReference) { _pendingRefresh = true; return; }
        _items.Children.Clear();
        var visible = VisibleItems();
        var visibleIds = visible.Select(x => x.Id).ToHashSet();
        _selectedIds.RemoveWhere(id => !visibleIds.Contains(id));
        var total = _controller.CurrentProject.LinkShelf.Count(x => x.PlacedAt == null);
        _count.Text = $"{visible.Length}/{total}";
        _toMedia.IsEnabled = _toPlayhead.IsEnabled = _toLine.IsEnabled = _selectedIds.Count > 0;
        _syncingSelectAll = true;
        _selectAll.IsChecked = visible.Length > 0 && visible.All(x => _selectedIds.Contains(x.Id));
        _syncingSelectAll = false;
        foreach (var item in visible) _items.Children.Add(CreateRow(item));
    }

    private LinkShelfItem[] VisibleItems()
    {
        var query = _search.Text?.Trim();
        return _controller.CurrentProject.LinkShelf.Where(x => x.PlacedAt == null &&
            (string.IsNullOrWhiteSpace(query) || x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             x.Source.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
    }

    private string[] SelectedIds() => _controller.CurrentProject.LinkShelf
        .Where(x => _selectedIds.Contains(x.Id)).Select(x => x.Id).ToArray();

    private Control CreateRow(LinkShelfItem item)
    {
        var asset = _controller.CurrentProject.Assets.FirstOrDefault(a => a.Source.Uri == item.Source);
        var selected = _selectedIds.Contains(item.Id);
        var check = new CheckBox { IsChecked = selected, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(check, "Seleccionar referencia");
        check.IsCheckedChanged += (_, _) => {
            if (check.IsChecked == true) _selectedIds.Add(item.Id);
            else _selectedIds.Remove(item.Id);
            Refresh();
        };
        var summary = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        summary.Children.Add(new TextBlock { Text = item.Name, FontWeight = FontWeight.SemiBold,
            FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        var metadata = item.WebMetadata ?? asset?.WebMetadata;
        var account = metadata?.AccountHandle ?? metadata?.AccountName ?? metadata?.CreatorName;
        var origin = item.Kind == "web" && Uri.TryCreate(item.Source, UriKind.Absolute, out var uri)
            ? uri.Host : Path.GetFileName(item.Source);
        summary.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(account) ? origin : account + " · " + origin,
            Foreground = Brushes.LightGray, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis });
        var status = item.MetadataStatus == "probing" ? "Consultando datos…" :
            item.MetadataStatus == "failed" ? "Datos no disponibles" :
            asset != null && WebMediaService.IsPending(asset) ? WebMediaService.GetState(asset.Id)?.Message :
            null;
        if (status != null) summary.Children.Add(new TextBlock { Text = status,
            Foreground = item.MetadataStatus == "failed" ? Brushes.IndianRed : Brushes.Gray,
            FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis });
        var imagePath = item.ThumbnailPath;
        if (imagePath == null && item.Kind == "local" &&
            new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" }.Contains(Path.GetExtension(item.Source).ToLowerInvariant()))
            imagePath = item.Source;
        Control thumb = new Border { Width = 50, Height = 42, Background = new SolidColorBrush(Color.Parse("#171717")),
            Child = new TextBlock { Text = item.MediaKind == "audio" ? "A" : item.MediaKind == "image" ? "I" : "V",
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray } };
        if (imagePath != null && File.Exists(imagePath))
        {
            try
            {
                if (!_bitmaps.TryGetValue(imagePath, out var bitmap))
                { using var stream = File.OpenRead(imagePath); _bitmaps[imagePath] = bitmap = Bitmap.DecodeToWidth(stream, 96); }
                thumb = new Image { Source = bitmap, Width = 50, Height = 42, Stretch = Stretch.UniformToFill };
            }
            catch { }
        }
        var dragSurface = new Grid { ColumnDefinitions = new ColumnDefinitions("50,*") };
        dragSurface.Children.Add(thumb);
        summary.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(summary, 1); dragSurface.Children.Add(summary);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*,Auto"),
            Margin = new Thickness(7, 5) };
        body.Children.Add(check);
        dragSurface.Margin = new Thickness(4, 0);
        Grid.SetColumn(dragSurface, 1); body.Children.Add(dragSurface);
        var duration = item.DurationSeconds ?? asset?.RecommendedDuration?.Seconds;
        var time = new TextBlock { Text = duration is > 0 ? TimeSpan.FromSeconds(duration.Value).ToString(@"hh\:mm\:ss") : "—",
            FontSize = 10, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(time, 2); body.Children.Add(time);
        Point dragStart = default;
        var dragStarted = false;
        dragSurface.PointerPressed += (_, e) => { dragStart = e.GetPosition(dragSurface); dragStarted = false; };
        dragSurface.PointerReleased += (_, _) => dragStarted = false;
        dragSurface.PointerMoved += async (_, e) => {
            if (dragStarted || !e.GetCurrentPoint(dragSurface).Properties.IsLeftButtonPressed) return;
            var current = e.GetPosition(dragSurface);
            if (Math.Abs(current.X - dragStart.X) + Math.Abs(current.Y - dragStart.Y) < 8) return;
            dragStarted = true;
            _draggingReference = true;
            var data = new DataObject();
            data.Set("Prometheus.LinkShelfId", item.Id);
            data.Set(DataFormats.Text, item.Source);
            try { await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy); }
            finally
            {
                _draggingReference = false;
                if (_pendingRefresh) { _pendingRefresh = false; Refresh(); }
            }
        };
        ToolTip.SetTip(dragSurface, "Arrastrar a Medios, una carpeta o la línea de tiempo");
        var row = new Border { Background = new SolidColorBrush(Color.Parse(selected ? "#291C12" : "#111111")),
            BorderBrush = new SolidColorBrush(Color.Parse(selected ? "#FF8A00" : "#303030")),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Child = body };
        row.ContextMenu = BuildItemMenu(item);
        return row;
    }

    private async System.Threading.Tasks.Task InsertSelectedAtPlayhead()
    {
        var ids = SelectedIds();
        if (ids.Length == 0) return;
        try
        {
            if (ids.Length == 1) await LinkShelfService.InsertAtAsync(_controller, ids[0], _controller.Transport.PositionSeconds);
            else await LinkShelfService.InsertBatchAtAsync(_controller, ids, _controller.Transport.PositionSeconds);
        }
        catch (Exception ex) { _controller.ReportStatus("No se pudieron colocar las referencias: " + ex.Message); }
    }

    private void ShowHistory()
    {
        var history = _controller.CurrentProject.LinkShelf.Where(x => x.PlacedAt != null)
            .OrderByDescending(x => x.PlacedAt).ToArray();
        var list = new StackPanel { Spacing = 5, Width = 340, Margin = new Thickness(8) };
        list.Children.Add(new TextBlock { Text = "Historial de enlaces", FontSize = 12,
            FontWeight = FontWeight.SemiBold, Margin = new Thickness(2, 0, 0, 5) });
        if (history.Length == 0) list.Children.Add(new TextBlock { Text = "Aún no hay enlaces utilizados.",
            Foreground = Brushes.Gray, FontSize = 11 });
        var flyout = new Flyout();
        foreach (var item in history)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(1, 2) };
            row.Children.Add(new TextBlock { Text = item.Name, FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var restore = new Button { Content = "Devolver", FontSize = 10, Padding = new Thickness(5, 2) };
            restore.Click += (_, _) => { LinkShelfService.Restore(_controller, [item.Id]); flyout.Hide(); };
            Grid.SetColumn(restore, 1); row.Children.Add(restore);
            list.Children.Add(row);
        }
        flyout.Content = new Border { Background = Brushes.Black,
            BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = list, MaxHeight = 350 } };
        flyout.ShowAt(_historyButton);
    }

    private void ShowPlacementMenu(Button anchor)
    {
        var ids = SelectedIds();
        if (ids.Length == 0) return;
        var point = new MenuItem { Header = "Elegir posición…" };
        point.Click += (_, _) => BeginPointPlacement(ids, 0, null);
        var options = new List<MenuItem> { point };
        if (ids.Length == 1 && _controller.CurrentProject.LinkShelf.FirstOrDefault(x => x.Id == ids[0]) is { } item)
        {
            var range = new MenuItem { Header = "Elegir tramo y posición…" };
            range.Click += (_, _) => ShowRangeFlyout(anchor, item);
            options.Add(range);
        }
        anchor.ContextMenu = new ContextMenu { ItemsSource = options };
        anchor.ContextMenu.Open(anchor);
    }

    private void ShowRangeFlyout(Button anchor, LinkShelfItem item)
    {
        var from = new TextBox { Width = 84, Text = (item.InSeconds ?? 0).ToString("0.###", CultureInfo.InvariantCulture), Watermark = "Inicio" };
        var knownDuration = item.DurationSeconds ?? _controller.CurrentProject.Assets
            .FirstOrDefault(a => a.Source.Uri == item.Source)?.RecommendedDuration?.Seconds;
        var to = new TextBox { Width = 84, Text = (item.OutSeconds ?? knownDuration)?.ToString("0.###", CultureInfo.InvariantCulture), Watermark = "Final" };
        var confirm = Button("Elegir punto", () => { });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock { Text = "Inicio", VerticalAlignment = VerticalAlignment.Center }); row.Children.Add(from);
        row.Children.Add(new TextBlock { Text = "Final", VerticalAlignment = VerticalAlignment.Center }); row.Children.Add(to);
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
        body.Children.Add(row); body.Children.Add(confirm);
        var flyout = new Flyout { Content = new Border { Background = Brushes.Black,
            BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = body } };
        void Accept()
        {
            if (!ParseTime(from.Text, out var start) || !ParseTime(to.Text, out var end) ||
                start < 0 || end <= start || knownDuration is > 0 && end > knownDuration.Value + 1.0 / 60)
            { _controller.ReportStatus("Indica un Inicio y Final válidos dentro del medio"); return; }
            flyout.Hide();
            BeginPointPlacement(new[] { item.Id }, start, end);
        }
        confirm.Click += (_, _) => Accept();
        to.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Accept(); e.Handled = true; } };
        flyout.ShowAt(anchor); from.Focus(); from.SelectAll();
    }

    private static bool ParseTime(string? text, out double seconds)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out seconds) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
            return double.IsFinite(seconds);
        var parts = text?.Split(':');
        if (parts is { Length: 2 or 3 } && parts.All(p => double.TryParse(p, NumberStyles.Float,
                CultureInfo.InvariantCulture, out _)))
        {
            var values = parts.Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            seconds = values.Length == 2 ? values[0] * 60 + values[1] : values[0] * 3600 + values[1] * 60 + values[2];
            return double.IsFinite(seconds);
        }
        return false;
    }

    private void BeginPointPlacement(string[] ids, double sourceIn, double? sourceOut)
    {
        if (PanelRegistry.GetExistingPanelContent("timeline") is TimelinePanelView timeline && timeline.IsVisible)
            timeline.BeginShelfPlacement(ids, sourceIn, sourceOut);
        else _controller.ReportStatus("Abre la línea de tiempo para elegir un punto");
    }

    private ContextMenu BuildItemMenu(LinkShelfItem item)
    {
        var options = new List<MenuItem>();
        void Add(string label, Action action) { var menu = new MenuItem { Header = label }; menu.Click += (_, _) => action(); options.Add(menu); }
        Add("Subir", () => Move(item.Id, -1));
        Add("Bajar", () => Move(item.Id, 1));
        if (item.Kind == "web" && item.MetadataStatus == "failed")
            Add("Reintentar datos", () => _ = WebShelfMetadataService.ProbeAsync(_controller, item.Id));
        if (item.PlacedAt != null) Add("Devolver a pendientes", () => LinkShelfService.Restore(_controller, new[] { item.Id }));
        Add("Quitar referencia", () => Remove(item.Id));
        return new ContextMenu { ItemsSource = options };
    }

    private async System.Threading.Tasks.Task PromoteSelected()
    {
        var ids = SelectedIds();
        var imported = 0;
        foreach (var id in ids)
        {
            try { if (await LinkShelfService.PromoteAsync(_controller, id) != null) imported++; }
            catch (Exception ex) { _controller.ReportStatus("No se pudo importar: " + ex.Message); }
        }
        _controller.ReportStatus($"Importados {imported} de {ids.Length} referencias a Medios");
    }

    private void Move(string id, int offset)
    {
        var items = _controller.CurrentProject.LinkShelf;
        var index = items.FindIndex(x => x.Id == id);
        var next = index + offset;
        if (index < 0 || next < 0 || next >= items.Count) return;
        _controller.UndoRedo.Commit(p => { var value = p.LinkShelf[index]; p.LinkShelf.RemoveAt(index); p.LinkShelf.Insert(next, value); }, "Reordenar bandeja");
    }

    private void Remove(string id) => _controller.UndoRedo.Commit(p => p.LinkShelf.RemoveAll(x => x.Id == id), "Quitar referencia");

    private static Button Button(string label, Action click)
    {
        var button = new Button { Content = label, Padding = new Thickness(8, 4), Margin = new Thickness(0, 0, 5, 5),
            Background = new SolidColorBrush(Color.Parse("#282828")) };
        button.Click += (_, _) => click();
        return button;
    }
}
