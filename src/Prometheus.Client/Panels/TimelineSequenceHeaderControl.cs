using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Prometheus.Client.Services;
using Prometheus.Client.Views;
using Prometheus.Core.Model;

namespace Prometheus.Client.Panels;

public sealed class TimelineSequenceHeaderControl : UserControl
{
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 3 };
    private readonly ScrollViewer _scroll;
    private readonly Button _previous;
    private readonly Button _next;
    private ProjectController? _controller;

    public TimelineSequenceHeaderControl()
    {
        var add = new Button { Content = "+", Width = 24, Height = 22, Padding = new Thickness(0) };
        ToolTip.SetTip(add, "Nuevo montaje");
        add.Click += (_, _) => Owner()?.CreateSequence();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _previous = new Button { Content = "‹", FontSize = 17, Width = 24, Height = 24, Padding = new Thickness(0), CornerRadius = new CornerRadius(12) };
        _next = new Button { Content = "›", FontSize = 17, Width = 24, Height = 24, Padding = new Thickness(0), CornerRadius = new CornerRadius(12) };
        _scroll = new ScrollViewer
        {
            Content = _tabs, Width = 70, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        _previous.Click += (_, _) => Shift(-130);
        _next.Click += (_, _) => Shift(130);
        _scroll.SizeChanged += (_, _) => UpdateArrows();
        row.Children.Add(_previous);
        row.Children.Add(_scroll);
        row.Children.Add(_next);
        row.Children.Add(add);
        Content = row;
        Loaded += (_, _) =>
        {
            if (_controller == null && ProjectController.Instance is { } controller)
            {
                _controller = controller;
                controller.StateChanged += Refresh;
            }
            if (Parent is Border { Parent: Grid hostGrid })
                hostGrid.SizeChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateHeaderWidth);
            Refresh();
        };
    }

    private MainWindow? Owner() => TopLevel.GetTopLevel(this) as MainWindow;

    private void Shift(double pixels)
    {
        _scroll.Offset = new Vector(Math.Clamp(_scroll.Offset.X + pixels, 0, Math.Max(0, _scroll.Extent.Width - _scroll.Viewport.Width)), 0);
        UpdateArrows();
    }

    private void UpdateArrows()
    {
        var max = Math.Max(0, _scroll.Extent.Width - _scroll.Viewport.Width);
        _previous.IsVisible = _next.IsVisible = max > 1;
        _previous.IsEnabled = _scroll.Offset.X > 1;
        _next.IsEnabled = _scroll.Offset.X < max - 1;
    }

    private void Refresh()
    {
        if (_controller == null) return;
        _tabs.Children.Clear();
        var project = _controller.CurrentProject;
        foreach (var sequence in project.Sequences)
        {
            var id = sequence.Id;
            var active = id == project.ActiveSequenceId;
            var button = new Button
            {
                Content = new TextBlock { Text = sequence.Name, TextTrimming = TextTrimming.CharacterEllipsis },
                MaxWidth = 135, Height = 22, Padding = new Thickness(7, 1),
                Background = new SolidColorBrush(Color.Parse(active ? "#673307" : "#202020")),
                BorderBrush = new SolidColorBrush(Color.Parse(active ? "#FF8A00" : "#383838")),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10)
            };
            button.Click += (_, _) => Owner()?.SwitchSequence(id);
            button.ContextMenu = new ContextMenu { ItemsSource = new[]
            {
                Action("Renombrar", () => { if (Owner() is { } owner) _ = owner.RenameSequenceAsync(id); }),
                Action("Duplicar", () => Owner()?.DuplicateSequence(id)),
                Action("Eliminar", () => Owner()?.DeleteSequence(id))
            }};
            _tabs.Children.Add(button);
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(UpdateHeaderWidth);
    }

    private void UpdateHeaderWidth()
    {
        _tabs.Measure(new Size(double.PositiveInfinity, 24));
        var available = Parent is Border { Parent: Grid hostGrid } && hostGrid.ColumnDefinitions.Count > 2
            ? hostGrid.ColumnDefinitions[2].ActualWidth - 96 : 350;
        _scroll.Width = Math.Min(Math.Max(1, available), Math.Min(350, Math.Max(1, _tabs.DesiredSize.Width)));
        UpdateArrows();
    }

    private static MenuItem Action(string title, Action execute)
    {
        var item = new MenuItem { Header = title };
        item.Click += (_, _) => execute();
        return item;
    }
}
