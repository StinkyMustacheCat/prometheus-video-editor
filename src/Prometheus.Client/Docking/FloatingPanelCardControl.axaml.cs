using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Prometheus.Client.Panels;
using Prometheus.Common.Docking;

namespace Prometheus.Client.Docking;

public partial class FloatingPanelCardControl : UserControl
{
    private Point _dragStartPoint;
    private bool _isDragging;
    private bool _headerPressActive;
    private bool _isResizing;
    private Point _resizeStartPoint;
    private double _origWidth;
    private double _origHeight;

    public string PanelId { get; private set; } = string.Empty;

    public event Action<string>? MinimizeRequested;
    public event Action<string>? CloseRequested;
    public event Action<FloatingPanelCardControl>? BringToFrontRequested;
    public event Action<string, PointerEventArgs>? DragStarted;
    public event Action<string, PointerEventArgs>? DragDelta;
    public event Action<string, PointerReleasedEventArgs>? DragCompleted;
    public event Action? CardMovedOrResized;

    public FloatingPanelCardControl()
    {
        InitializeComponent();
    }

    public void Initialize(FloatingWindowDescriptor descriptor)
    {
        PanelId = descriptor.PanelId;
        TitleText.Text = PanelRegistry.GetPanelTitle(PanelId);

        if (PanelId == "timeline") { MinWidth = 900; MinHeight = 270; }
        if (PanelId == "transcript") { MinWidth = 380; MinHeight = 420; }
        if (PanelId == "properties") MinWidth = PropertiesPanelView.MinimumPanelWidth;
        Width = Math.Max(MinWidth, descriptor.Width);
        Height = Math.Max(MinHeight, descriptor.Height);

        Canvas.SetLeft(this, descriptor.X);
        Canvas.SetTop(this, descriptor.Y);

        var content = PanelRegistry.GetOrCreatePanelContent(PanelId);
        ContentContainer.Child = content;
        if (content is TimelinePanelView timeline)
        {
            if (timeline.HeaderTabs.Parent is Decorator oldSlot) oldSlot.Child = null;
            SequenceHeaderSlot.Child = timeline.HeaderTabs;
            SequenceHeaderSlot.IsVisible = true;
        }

        MinimizeButton.Click += (_, _) => MinimizeRequested?.Invoke(PanelId);
        CloseButton.Click += (_, _) => CloseRequested?.Invoke(PanelId);

        HeaderBorder.PointerPressed += Header_PointerPressed;
        HeaderBorder.PointerMoved += Header_PointerMoved;
        HeaderBorder.PointerReleased += Header_PointerReleased;

        ResizeHandle.PointerPressed += ResizeHandle_PointerPressed;
        ResizeHandle.PointerMoved += ResizeHandle_PointerMoved;
        ResizeHandle.PointerReleased += ResizeHandle_PointerReleased;

        PointerPressed += (_, _) => BringToFrontRequested?.Invoke(this);
    }

    public FloatingWindowDescriptor GetDescriptor()
    {
        return new FloatingWindowDescriptor(
            PanelId,
            Canvas.GetLeft(this),
            Canvas.GetTop(this),
            Width,
            Height);
    }

    public void MoveTo(double x, double y)
    {
        Canvas.SetLeft(this, Math.Max(10, x));
        Canvas.SetTop(this, Math.Max(10, y));
        CardMovedOrResized?.Invoke();
    }

    private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsSequenceControl(e.Source)) return;
        _headerPressActive = true;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(this);
            _isDragging = false;
            e.Pointer.Capture(HeaderBorder);
            BringToFrontRequested?.Invoke(this);
            e.Handled = true;
        }
    }

    private bool IsSequenceControl(object? source)
    {
        for (var control = source as Control; control != null; control = control.Parent as Control)
            if (control == SequenceHeaderSlot) return true;
        return false;
    }

    private void Header_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_headerPressActive) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var curPosInThis = e.GetPosition(this);
            var delta = curPosInThis - _dragStartPoint;

            if (!_isDragging && (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4))
            {
                _isDragging = true;
                DragStarted?.Invoke(PanelId, e);
            }

            if (_isDragging && Parent is Control parentControl)
            {
                var currentPosInParent = e.GetPosition(parentControl);
                var newX = Math.Max(0, currentPosInParent.X - _dragStartPoint.X);
                var newY = Math.Max(0, currentPosInParent.Y - _dragStartPoint.Y);

                Canvas.SetLeft(this, newX);
                Canvas.SetTop(this, newY);

                DragDelta?.Invoke(PanelId, e);
                e.Handled = true;
            }
        }
    }

    private void Header_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_headerPressActive) return;
        _headerPressActive = false;
        e.Pointer.Capture(null);
        if (_isDragging)
        {
            _isDragging = false;
            DragCompleted?.Invoke(PanelId, e);
            CardMovedOrResized?.Invoke();
            e.Handled = true;
        }
    }

    private void ResizeHandle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isResizing = true;
            _resizeStartPoint = e.GetPosition(Parent as Visual);
            _origWidth = Width;
            _origHeight = Height;
            e.Pointer.Capture(ResizeHandle);
            BringToFrontRequested?.Invoke(this);
            e.Handled = true;
        }
    }

    private void ResizeHandle_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isResizing && Parent is Visual parentVisual)
        {
            var curPos = e.GetPosition(parentVisual);
            var deltaX = curPos.X - _resizeStartPoint.X;
            var deltaY = curPos.Y - _resizeStartPoint.Y;

            Width = Math.Max(MinWidth, _origWidth + deltaX);
            Height = Math.Max(MinHeight, _origHeight + deltaY);
            e.Handled = true;
        }
    }

    private void ResizeHandle_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            e.Pointer.Capture(null);
            CardMovedOrResized?.Invoke();
            e.Handled = true;
        }
    }
}
