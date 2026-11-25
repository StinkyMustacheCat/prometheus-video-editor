using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Prometheus.Client.Views;

namespace Prometheus.Client.Panels;

public partial class MediaPoolPanelView
{
    private void UpdateResponsiveLayout()
    {
        var width = Bounds.Width;
        if (width <= 0) return;
        AddButton.Content = width < 300 ? "+" : "+ Medios";
        SearchBox.IsVisible = width >= 240;
        SearchButton.IsVisible = width < 240;
        FilterButton.IsVisible = width >= 210;
        FilterButton.Content = width < 390 ? "⛃" : _selectedFilter + " ▾";
        LinkShelfButton.Content = width < 340 ? "⌁" : "Enlaces";
        ViewButton.IsVisible = width >= 260;
        ViewButton.Content = width < 400 ? "▦" : "Vista ▾";
        CountLabel.IsVisible = width >= 500;
        LinkShelfOverlay.Width = Math.Max(200, Math.Min(460, width - 8));
        if (width >= 240) ExpandedSearchBox.IsVisible = false;
        RenderVisibleAssets(true);
    }

    private void ShowAddMenu()
    {
        AddButton.ContextMenu = new ContextMenu { ItemsSource = new MenuItem[] {
            Menu("Importar archivos…", async () => await ImportAsync()),
            Menu("Importar enlace…", async () => await ImportLinkAsync())
        }};
        AddButton.ContextMenu.Open(AddButton);
    }

    private void OpenLinkShelf()
    {
        AttachController();
        if (_controller == null) return;
        ShowLinkShelf(!LinkShelfOverlay.IsVisible);
    }

    public void ShowLinkShelf(bool visible, string? captured = null)
    {
        AttachController();
        if (_controller == null) return;
        if (visible && LinkShelfHost.Content is not LinkShelfView) LinkShelfHost.Content = new LinkShelfView(_controller);
        if (!string.IsNullOrWhiteSpace(captured) && LinkShelfHost.Content is LinkShelfView shelf) shelf.AddCaptured(captured);
        LinkShelfOverlay.IsVisible = visible;
        LinkShelfButton.Background = visible ? new SolidColorBrush(Color.Parse("#B56A00")) : Brushes.Black;
    }

    private void ShowFilterMenu(Control anchor)
    {
        var filters = new[] { "Todos", "Vídeo", "Audio", "Imagen", "Sin conexión", "Web pendientes", "Web resueltos" };
        var options = new List<MenuItem>();
        foreach (var filter in filters)
        {
            var item = Menu((_selectedFilter == filter ? "✓  " : "    ") + filter, () => {
                _selectedFilter = filter;
                FilterButton.Content = Bounds.Width < 320 ? "⛃" : filter + " ▾";
                Refresh();
                MediaScroll.Offset = new Vector(0, 0);
            });
            options.Add(item);
        }
        anchor.ContextMenu = new ContextMenu { ItemsSource = options };
        anchor.ContextMenu.Open(anchor);
    }

    private void ShowViewPopup(Control anchor)
    {
        var body = new StackPanel { Spacing = 3, Margin = new Thickness(7) };
        var flyout = new Flyout();
        var modes = new[] { "Lista", "Cuadrícula", "Lista compacta" };
        for (var i = 0; i < modes.Length; i++)
        {
            var index = i;
            var button = new Button { Content = (_viewMode == i ? "✓  " : "    ") + modes[i], HorizontalContentAlignment = HorizontalAlignment.Left, MinWidth = 180, Height = 26, Background = Brushes.Black };
            button.Click += (_, _) => { SetViewMode(index); flyout.Hide(); };
            body.Children.Add(button);
        }
        body.Children.Add(new TextBlock { Text = "Tamaño", Foreground = Brushes.LightGray, Margin = new Thickness(4, 8, 4, 0) });
        var slider = new Slider { Minimum = _viewMode == 2 ? 22 : _viewMode == 1 ? 120 : 56,
            Maximum = _viewMode == 2 ? 42 : _viewMode == 1 ? 240 : 100, Value = _viewSizes[_viewMode], Width = 180 };
        slider.ValueChanged += (_, _) => SetViewSize((int)Math.Round(slider.Value));
        body.Children.Add(slider);
        flyout.Content = new Border { Background = Brushes.Black, BorderBrush = new SolidColorBrush(Color.Parse("#333333")), BorderThickness = new Thickness(1), Child = body };
        flyout.ShowAt(anchor);
    }

    private void SetViewMode(int mode)
    {
        _viewMode = mode;
        ViewButton.Content = Bounds.Width < 400 ? mode == 2 ? "☷" : "▦" : "Vista ▾";
        RenderVisibleAssets(true);
    }

    private void SetViewSize(int size)
    {
        if (_viewSizes[_viewMode] == size) return;
        _viewSizes[_viewMode] = size;
        RenderVisibleAssets(true);
    }

    private void ShowMoreMenu()
    {
        var items = new List<MenuItem>();
        if (!FilterButton.IsVisible) items.Add(Menu("Filtrar…", () => ShowFilterMenu(MoreButton)));
        if (!ViewButton.IsVisible) items.Add(Menu("Vista y tamaño…", () => ShowViewPopup(MoreButton)));
        var sort = new MenuItem { Header = "Ordenar por" };
        var sortNames = new[] { "Nombre A–Z", "Nombre Z–A", "Tipo", "Duración" };
        var sortItems = new List<MenuItem>();
        for (var i = 0; i < sortNames.Length; i++)
        {
            var choice = i;
            sortItems.Add(Menu((_sortMode == i ? "✓  " : "    ") + sortNames[i], () => { _sortMode = choice; Refresh(); }));
        }
        sort.ItemsSource = sortItems;
        items.Add(sort);
        items.Add(Menu("Reconectar carpeta…", async () => await ReconnectFolderAsync()));
        MoreButton.ContextMenu = new ContextMenu { ItemsSource = items };
        MoreButton.ContextMenu.Open(MoreButton);
    }
}
