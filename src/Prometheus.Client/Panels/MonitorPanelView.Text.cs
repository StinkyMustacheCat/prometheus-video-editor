using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Prometheus.Client.Services;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class MonitorPanelView
{
    private IPointer? _textPointer;
    private Point _textStart;
    private double _textTime;
    private string? _textProjectId;
    private string? _textSequenceId;
    private bool TextToolEnabled => ProjectController.Instance?.TextToolEnabled == true;

    private void SetupTextPlacement()
    {
        if (ProjectController.Instance is { } controller)
            controller.TextToolChanged += () =>
            {
                if (!_attached) return;
                CancelTextPlacement();
                CancelGizmoGesture();
                UpdateChromaPickState();
                UpdateGizmo();
                if (TextToolEnabled)
                    controller.ReportStatus("Texto · haz clic o arrastra un cuadro en Previsualización");
            };
        ViewportHost.AddHandler(PointerPressedEvent, BeginTextPlacement, RoutingStrategies.Tunnel);
        ViewportHost.PointerMoved += UpdateTextPlacement;
        ViewportHost.PointerReleased += EndTextPlacement;
        ViewportHost.PointerCaptureLost += (_, args) =>
        {
            if (_textPointer == args.Pointer) CancelTextPlacement();
        };
    }

    private void BeginTextPlacement(object? sender, PointerPressedEventArgs e)
    {
        if (!TextToolEnabled || ChromaColorPicker.IsPending || !e.GetCurrentPoint(ViewportHost).Properties.IsLeftButtonPressed)
            return;
        e.Handled = true;
        var controller = ProjectController.Instance;
        if (e.ClickCount > 1 || controller?.CurrentProject.ActiveSequence is not { } sequence ||
            !TextCanvasPoint(e, out var point, clamp: false)) return;
        _transport?.Pause();
        _textTime = _transport?.PositionSeconds ?? 0;
        _textProjectId = controller.CurrentProject.Id;
        _textSequenceId = sequence.Id;
        _textPointer = e.Pointer;
        _textStart = point;
        e.Pointer.Capture(ViewportHost);
    }

    private bool TextCanvasPoint(PointerEventArgs e, out Point result, bool clamp)
    {
        result = default;
        if (!double.IsFinite(SequenceFrame.Width) || !double.IsFinite(SequenceFrame.Height) ||
            SequenceFrame.Width <= 0 || SequenceFrame.Height <= 0) return false;
        var at = e.GetPosition(SequenceFrame);
        var x = at.X / SequenceFrame.Width;
        var y = at.Y / SequenceFrame.Height;
        if (!clamp && (x is < 0 or > 1 || y is < 0 or > 1)) return false;
        result = new Point(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
        return true;
    }

    private Rect TextDraftRect(Point end) => new(
        Math.Min(_textStart.X, end.X), Math.Min(_textStart.Y, end.Y),
        Math.Abs(_textStart.X - end.X), Math.Abs(_textStart.Y - end.Y));

    private bool IsTextDrag(Rect box) =>
        box.Width * SequenceFrame.Width >= 6 && box.Height * SequenceFrame.Height >= 6;

    private void UpdateTextPlacement(object? sender, PointerEventArgs e)
    {
        if (e.Pointer != _textPointer || !TextCanvasPoint(e, out var point, clamp: true)) return;
        var box = TextDraftRect(point);
        TextDraftRectangle.IsVisible = IsTextDrag(box);
        Canvas.SetLeft(TextDraftRectangle, box.X * SequenceFrame.Width);
        Canvas.SetTop(TextDraftRectangle, box.Y * SequenceFrame.Height);
        TextDraftRectangle.Width = box.Width * SequenceFrame.Width;
        TextDraftRectangle.Height = box.Height * SequenceFrame.Height;
        e.Handled = true;
    }

    private void EndTextPlacement(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer != _textPointer || e.InitialPressMouseButton != MouseButton.Left) return;
        e.Handled = true;
        var controller = ProjectController.Instance;
        if (TextToolEnabled && controller != null && controller.CurrentProject.Id == _textProjectId &&
            controller.CurrentProject.ActiveSequenceId == _textSequenceId &&
            TextCanvasPoint(e, out var point, clamp: true))
        {
            var box = TextDraftRect(point);
            var placement = IsTextDrag(box) ? new TitleBox(box.Center.X, box.Center.Y, box.Width, box.Height)
                : new TitleBox(_textStart.X, _textStart.Y, 0.48);
            CancelTextPlacement();
            controller.CreateTitle(placement, _textTime);
        }
        else CancelTextPlacement();
    }

    private void CancelTextPlacement()
    {
        var pointer = _textPointer;
        _textPointer = null;
        TextDraftRectangle.IsVisible = false;
        if (pointer?.Captured == ViewportHost) pointer.Capture(null);
    }
}
