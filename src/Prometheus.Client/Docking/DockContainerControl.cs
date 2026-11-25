using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Prometheus.Client.Panels;
using Prometheus.Common.Docking;

namespace Prometheus.Client.Docking;

public class DockContainerControl : UserControl
{
    private readonly Panel _rootPanel;
    private readonly Border _contentHost;
    private readonly DockOverlayControl _overlay;
    private readonly Canvas _floatingCanvas;
    private readonly Dictionary<string, DockPanelHostControl> _activeHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FloatingPanelCardControl> _floatingCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(LayoutNode Node, Grid Grid)> _renderedSplits = new();

    private LayoutNode? _currentLayoutRoot;
    private string? _draggingPanelId;
    private int _currentTopZIndex = 1;

    private static double DockColumnMinimum(LayoutNode? node)
    {
        var panels = node?.GetAllPanelIds();
        if (panels?.Contains("timeline") == true) return 900;
        return panels?.Contains("properties") == true ? PropertiesPanelView.MinimumPanelWidth : 120;
    }

    public event Action<string>? FloatPanelRequested;
    public event Action<string>? MinimizePanelRequested;
    public event Action<string>? ClosePanelRequested;
    public event Action<string, Point>? DroppedAsFloatingRequested;
    public event Action<string, LayoutNode>? PanelDocked;
    public event Action? LayoutModified;
    public event Action<string, string>? ReplacePanelRequested;

    public DockContainerControl()
    {
        _rootPanel = new Panel();
        _contentHost = new Border { Background = new SolidColorBrush(Color.Parse("#000000")) };
        _overlay = new DockOverlayControl { IsVisible = false };
        _floatingCanvas = new Canvas { IsHitTestVisible = true };

        _rootPanel.Children.Add(_contentHost);
        _rootPanel.Children.Add(_overlay);
        _rootPanel.Children.Add(_floatingCanvas);

        Content = _rootPanel;
    }

    public void SetLayout(LayoutNode? rootNode)
    {
        CaptureSplitRatios();
        _renderedSplits.Clear();
        _currentLayoutRoot = rootNode;
        _activeHosts.Clear();

        if (rootNode == null)
        {
            _contentHost.Child = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#080808")),
                Child = new TextBlock
                {
                    Text = "No hay paneles acoplados. Selecciona un diseño desde el menú superior.",
                    Foreground = new SolidColorBrush(Color.Parse("#666666")),
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            return;
        }

        _contentHost.Child = RenderNode(rootNode);
    }

    public bool CaptureSplitRatios()
    {
        var changed = false;
        foreach (var (node, grid) in _renderedSplits)
        {
            if (node.Type != LayoutNodeType.Split) continue;
            var first = node.Orientation == SplitOrientation.Horizontal
                ? grid.ColumnDefinitions[0].ActualWidth : grid.RowDefinitions[0].ActualHeight;
            var second = node.Orientation == SplitOrientation.Horizontal
                ? grid.ColumnDefinitions[2].ActualWidth : grid.RowDefinitions[2].ActualHeight;
            var total = first + second;
            if (!double.IsFinite(total) || total <= 20) continue;
            var ratio = Math.Clamp(first / total, 0.10, 0.90);
            if (Math.Abs(node.Ratio - ratio) <= 0.0001) continue;
            node.Ratio = ratio;
            changed = true;
        }
        return changed;
    }

    public bool PulsePanelIfDocked(string panelId)
    {
        if (_activeHosts.TryGetValue(panelId, out var host))
        {
            host.TriggerGlowPulse();
            return true;
        }
        return false;
    }

    public void SyncFloatingCards(List<FloatingWindowDescriptor> floatingDescriptors)
    {
        _floatingCanvas.Children.Clear();
        _floatingCards.Clear();

        foreach (var desc in floatingDescriptors)
        {
            AddFloatingCard(desc);
        }
    }

    public void AddFloatingCard(FloatingWindowDescriptor desc)
    {
        if (_floatingCards.ContainsKey(desc.PanelId))
            return;

        var card = new FloatingPanelCardControl();
        card.Initialize(desc);

        card.MinimizeRequested += p => MinimizePanelRequested?.Invoke(p);
        card.CloseRequested += p => ClosePanelRequested?.Invoke(p);
        card.DragStarted += FloatingCard_DragStarted;
        card.DragDelta += FloatingCard_DragDelta;
        card.DragCompleted += FloatingCard_DragCompleted;
        card.CardMovedOrResized += () => LayoutModified?.Invoke();

        card.BringToFrontRequested += c =>
        {
            c.ZIndex = ++_currentTopZIndex;
        };

        card.ZIndex = ++_currentTopZIndex;
        _floatingCards[desc.PanelId] = card;
        _floatingCanvas.Children.Add(card);
    }

    public void DockPanel(string panelId)
    {
        RemoveFloatingCard(panelId);
        var newRoot = LayoutNode.Remove(_currentLayoutRoot, panelId);
        if (newRoot == null)
        {
            newRoot = LayoutNode.CreateLeaf(panelId);
        }
        else
        {
            newRoot = LayoutNode.CreateSplit(SplitOrientation.Vertical, newRoot, LayoutNode.CreateLeaf(panelId), 0.70);
        }
        SetLayout(newRoot);
        PanelDocked?.Invoke(panelId, newRoot);
        LayoutModified?.Invoke();
    }

    public void RemoveFloatingCard(string panelId)
    {
        if (_floatingCards.TryGetValue(panelId, out var card))
        {
            _floatingCanvas.Children.Remove(card);
            _floatingCards.Remove(panelId);
        }
    }

    public void PositionFloatingCardUnderNotch(string panelId)
    {
        if (_floatingCards.TryGetValue(panelId, out var card))
        {
            var containerWidth = Bounds.Width > 0 ? Bounds.Width : 1200;
            var targetX = Math.Max(20, (containerWidth - card.Width) / 2);
            var targetY = 12; // Cerca del notch, dentro del área de trabajo.
            card.MoveTo(targetX, targetY);

            // Traer al frente sin reordenar hijos visuales
            card.ZIndex = ++_currentTopZIndex;
        }
    }

    public List<FloatingWindowDescriptor> GetFloatingDescriptors()
    {
        var list = new List<FloatingWindowDescriptor>();
        foreach (var card in _floatingCards.Values)
        {
            list.Add(card.GetDescriptor());
        }
        return list;
    }

    private Control RenderNode(LayoutNode node)
    {
        if (node.Type == LayoutNodeType.Leaf)
        {
            var host = new DockPanelHostControl();
            var panelId = node.PanelId ?? string.Empty;
            host.Initialize(panelId);

            host.FloatRequested += p => FloatPanelRequested?.Invoke(p);
            host.MinimizeRequested += p => MinimizePanelRequested?.Invoke(p);
            host.CloseRequested += p => ClosePanelRequested?.Invoke(p);
            host.ReplaceRequested += (current, replacement) => ReplacePanelRequested?.Invoke(current, replacement);

            host.DragStarted += Host_DragStarted;
            host.DragDelta += Host_DragDelta;
            host.DragCompleted += Host_DragCompleted;

            _activeHosts[panelId] = host;
            return host;
        }

        // Split node
        var grid = new Grid();
        _renderedSplits.Add((node, grid));
        var isHorizontal = node.Orientation == SplitOrientation.Horizontal;

        var firstControl = node.FirstChild != null ? RenderNode(node.FirstChild) : new Panel();
        var secondControl = node.SecondChild != null ? RenderNode(node.SecondChild) : new Panel();

        if (isHorizontal)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(node.Ratio, GridUnitType.Star) { MinWidth = DockColumnMinimum(node.FirstChild) });
            grid.ColumnDefinitions.Add(new ColumnDefinition(4, GridUnitType.Pixel));
            grid.ColumnDefinitions.Add(new ColumnDefinition(1.0 - node.Ratio, GridUnitType.Star) { MinWidth = DockColumnMinimum(node.SecondChild) });

            Grid.SetColumn(firstControl, 0);
            grid.Children.Add(firstControl);

            var splitter = new GridSplitter
            {
                Width = 4,
                Background = new SolidColorBrush(Color.Parse("#1D1D1D")),
                ResizeDirection = GridResizeDirection.Columns,
                Cursor = new Cursor(StandardCursorType.SizeWestEast)
            };
            Grid.SetColumn(splitter, 1);
            grid.Children.Add(splitter);

            Grid.SetColumn(secondControl, 2);
            grid.Children.Add(secondControl);

            ObserveSplitterRelease(splitter);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition(node.Ratio, GridUnitType.Star) { MinHeight = node.FirstChild?.GetAllPanelIds().Contains("timeline") == true ? 270 : 80 });
            grid.RowDefinitions.Add(new RowDefinition(4, GridUnitType.Pixel));
            grid.RowDefinitions.Add(new RowDefinition(1.0 - node.Ratio, GridUnitType.Star) { MinHeight = node.SecondChild?.GetAllPanelIds().Contains("timeline") == true ? 270 : 80 });

            Grid.SetRow(firstControl, 0);
            grid.Children.Add(firstControl);

            var splitter = new GridSplitter
            {
                Height = 4,
                Background = new SolidColorBrush(Color.Parse("#1D1D1D")),
                ResizeDirection = GridResizeDirection.Rows,
                Cursor = new Cursor(StandardCursorType.SizeNorthSouth)
            };
            Grid.SetRow(splitter, 1);
            grid.Children.Add(splitter);

            Grid.SetRow(secondControl, 2);
            grid.Children.Add(secondControl);

            ObserveSplitterRelease(splitter);
        }

        return grid;
    }

    private void ObserveSplitterRelease(GridSplitter splitter)
    {
        // GridSplitter handles the pointer event itself. Observe the handled release
        // after layout settles, then persist the actual size rather than a stale star ratio.
        splitter.AddHandler(InputElement.PointerReleasedEvent, (_, _) =>
            Dispatcher.UIThread.Post(() => { if (CaptureSplitRatios()) LayoutModified?.Invoke(); }),
            RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void Host_DragStarted(string panelId, PointerEventArgs e)
    {
        _draggingPanelId = panelId;
        _overlay.IsVisible = true;
    }

    private void Host_DragDelta(string panelId, PointerEventArgs e)
    {
        var localPos = e.GetPosition(this);
        var totalSize = Bounds.Size;
        bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        _overlay.UpdateDragHover(localPos, totalSize, HitTestPanel, isCtrl, _draggingPanelId, isFloating: false);
    }

    private void Host_DragCompleted(string panelId, PointerReleasedEventArgs e)
    {
        var localPos = e.GetPosition(this);
        var totalSize = Bounds.Size;
        bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        var dropInfo = _overlay.UpdateDragHover(localPos, totalSize, HitTestPanel, isCtrl, _draggingPanelId, isFloating: false);

        _overlay.Reset();
        _overlay.IsVisible = false;

        if (string.IsNullOrEmpty(_draggingPanelId))
            return;

        var dragged = _draggingPanelId;
        _draggingPanelId = null;

        // Soltar como flotante salvo que Ctrl señale un destino de acople válido.
        if (!isCtrl || dropInfo.Type == DropTargetType.None)
        {
            DroppedAsFloatingRequested?.Invoke(dragged, localPos);
            return;
        }

        ApplyDrop(dragged, dropInfo, localPos, totalSize);
    }

    private void FloatingCard_DragStarted(string panelId, PointerEventArgs e)
    {
        _draggingPanelId = panelId;
        _overlay.IsVisible = true;
    }

    private void FloatingCard_DragDelta(string panelId, PointerEventArgs e)
    {
        var localPos = e.GetPosition(this);
        var totalSize = Bounds.Size;
        bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        _overlay.UpdateDragHover(localPos, totalSize, HitTestPanel, isCtrl, _draggingPanelId, isFloating: true);
    }

    private void FloatingCard_DragCompleted(string panelId, PointerReleasedEventArgs e)
    {
        var localPos = e.GetPosition(this);
        var totalSize = Bounds.Size;
        bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        var dropInfo = _overlay.UpdateDragHover(localPos, totalSize, HitTestPanel, isCtrl, _draggingPanelId, isFloating: true);

        _overlay.Reset();
        _overlay.IsVisible = false;
        _draggingPanelId = null;

        // El flotante solo se acopla con Ctrl sobre un destino válido.
        if (!isCtrl || dropInfo.Type == DropTargetType.None)
        {
            LayoutModified?.Invoke();
            return;
        }

        // Se soltó sobre un objetivo de acople: quitar de flotante y acoplar al árbol
        RemoveFloatingCard(panelId);
        ApplyDrop(panelId, dropInfo, localPos, totalSize);
    }

    private void ApplyDrop(string draggedPanelId, DropTargetInfo dropInfo, Point localPos, Size totalSize)
    {
        if (dropInfo.Type == DropTargetType.None)
        {
            DroppedAsFloatingRequested?.Invoke(draggedPanelId, localPos);
            return;
        }

        RemoveFloatingCard(draggedPanelId);
        var newRoot = LayoutNode.Remove(_currentLayoutRoot, draggedPanelId);

        switch (dropInfo.Type)
        {
            case DropTargetType.EdgeLeft:
                newRoot = newRoot == null
                    ? LayoutNode.CreateLeaf(draggedPanelId)
                    : LayoutNode.CreateSplit(SplitOrientation.Horizontal, LayoutNode.CreateLeaf(draggedPanelId), newRoot, 0.25);
                break;

            case DropTargetType.EdgeTop:
                newRoot = newRoot == null
                    ? LayoutNode.CreateLeaf(draggedPanelId)
                    : LayoutNode.CreateSplit(SplitOrientation.Vertical, LayoutNode.CreateLeaf(draggedPanelId), newRoot, 0.25);
                break;

            case DropTargetType.EdgeRight:
                newRoot = newRoot == null
                    ? LayoutNode.CreateLeaf(draggedPanelId)
                    : LayoutNode.CreateSplit(SplitOrientation.Horizontal, newRoot, LayoutNode.CreateLeaf(draggedPanelId), 0.75);
                break;

            case DropTargetType.EdgeBottom:
                newRoot = newRoot == null
                    ? LayoutNode.CreateLeaf(draggedPanelId)
                    : LayoutNode.CreateSplit(SplitOrientation.Vertical, newRoot, LayoutNode.CreateLeaf(draggedPanelId), 0.75);
                break;

            case DropTargetType.PanelCenter:
                if (!string.IsNullOrEmpty(dropInfo.TargetPanelId))
                {
                    var targetNode = newRoot?.FindNode(dropInfo.TargetPanelId);
                    if (targetNode != null)
                    {
                        targetNode.PanelId = draggedPanelId;
                        MinimizePanelRequested?.Invoke(dropInfo.TargetPanelId);
                    }
                }
                break;

            case DropTargetType.PanelLeft:
                if (!string.IsNullOrEmpty(dropInfo.TargetPanelId) && newRoot != null)
                {
                    newRoot.SplitLeaf(dropInfo.TargetPanelId, draggedPanelId, SplitOrientation.Horizontal, insertBefore: true, ratio: 0.5);
                }
                break;

            case DropTargetType.PanelRight:
                if (!string.IsNullOrEmpty(dropInfo.TargetPanelId) && newRoot != null)
                {
                    newRoot.SplitLeaf(dropInfo.TargetPanelId, draggedPanelId, SplitOrientation.Horizontal, insertBefore: false, ratio: 0.5);
                }
                break;

            case DropTargetType.PanelTop:
                if (!string.IsNullOrEmpty(dropInfo.TargetPanelId) && newRoot != null)
                {
                    newRoot.SplitLeaf(dropInfo.TargetPanelId, draggedPanelId, SplitOrientation.Vertical, insertBefore: true, ratio: 0.5);
                }
                break;

            case DropTargetType.PanelBottom:
                if (!string.IsNullOrEmpty(dropInfo.TargetPanelId) && newRoot != null)
                {
                    newRoot.SplitLeaf(dropInfo.TargetPanelId, draggedPanelId, SplitOrientation.Vertical, insertBefore: false, ratio: 0.5);
                }
                break;
        }

        SetLayout(newRoot);
        PanelDocked?.Invoke(draggedPanelId, newRoot!);
        LayoutModified?.Invoke();
    }

    private (string PanelId, Rect Bounds)? HitTestPanel(Point point)
    {
        foreach (var (panelId, host) in _activeHosts)
        {
            if (string.Equals(panelId, _draggingPanelId, StringComparison.OrdinalIgnoreCase))
                continue;

            var bounds = host.Bounds;
            var origin = host.TranslatePoint(new Point(0, 0), this);
            if (origin != null)
            {
                var rect = new Rect(origin.Value.X, origin.Value.Y, bounds.Width, bounds.Height);
                if (rect.Contains(point))
                {
                    return (panelId, rect);
                }
            }
        }

        return null;
    }
}
