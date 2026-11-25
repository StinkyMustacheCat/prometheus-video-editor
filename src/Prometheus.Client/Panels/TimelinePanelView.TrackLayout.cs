using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Prometheus.Core.Model;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView
{
    private readonly ScrollBar _videoTrackScroll = new() { Orientation = Orientation.Vertical, Width = 8, Minimum = 0, IsVisible = false };
    private readonly ScrollBar _audioTrackScroll = new() { Orientation = Orientation.Vertical, Width = 8, Minimum = 0, IsVisible = false };
    private bool _syncingTrackScroll;
    private string? _resizingTrackId;
    private bool _resizingDivider;
    private double _resizeOriginY;
    private double _resizeOriginValue;
    private double _trackResizeDividerY;

    private void SetupTrackLayout()
    {
        TrackScrollOverlay.Children.Add(_videoTrackScroll);
        TrackScrollOverlay.Children.Add(_audioTrackScroll);
        Canvas.SetLeft(_videoTrackScroll, 0);
        Canvas.SetLeft(_audioTrackScroll, 0);
        _videoTrackScroll.ValueChanged += (_, _) =>
        {
            if (!_syncingTrackScroll) TimelineCanvas.VideoScrollOffset = _videoTrackScroll.Value;
        };
        _audioTrackScroll.ValueChanged += (_, _) =>
        {
            if (!_syncingTrackScroll) TimelineCanvas.AudioScrollOffset = _audioTrackScroll.Value;
        };
        TimelineCanvas.TrackLayoutChanged += () =>
        {
            if (_controller?.CurrentProject.ActiveSequence is { } sequence) RefreshTrackHeaders(sequence);
        };
        TrackHeaderPanel.PointerPressed += TrackHeaderPointerPressed;
        TrackHeaderPanel.PointerMoved += TrackHeaderPointerMoved;
        TrackHeaderPanel.PointerReleased += TrackHeaderPointerReleased;
        TrackHeaderPanel.SizeChanged += (_, _) => UpdateTrackScrollbars();
    }

    private void UpdateTrackScrollbars()
    {
        _syncingTrackScroll = true;
        try
        {
            var divider = TimelineCanvas.DividerY;
            _videoTrackScroll.IsVisible = TimelineCanvas.VideoScrollMaximum > 1;
            _videoTrackScroll.Maximum = TimelineCanvas.VideoScrollMaximum;
            _videoTrackScroll.ViewportSize = divider;
            _videoTrackScroll.Height = Math.Max(20, divider);
            _videoTrackScroll.Value = TimelineCanvas.VideoScrollOffset;
            Canvas.SetTop(_videoTrackScroll, 0);

            var audioTop = divider + 8;
            _audioTrackScroll.IsVisible = TimelineCanvas.AudioScrollMaximum > 1;
            _audioTrackScroll.Maximum = TimelineCanvas.AudioScrollMaximum;
            _audioTrackScroll.ViewportSize = Math.Max(1, TimelineCanvas.Bounds.Height - audioTop);
            _audioTrackScroll.Height = Math.Max(20, TimelineCanvas.Bounds.Height - audioTop);
            _audioTrackScroll.Value = TimelineCanvas.AudioScrollOffset;
            Canvas.SetTop(_audioTrackScroll, audioTop);
        }
        finally { _syncingTrackScroll = false; }
    }

    private Prometheus.Core.Model.Track? HeightHandleAt(double y) => _controller?.CurrentProject.ActiveSequence?.Tracks
        .FirstOrDefault(track =>
        {
            var edge = TimelineCanvas.TrackTop(track.Id) + (track.Kind == "video" ? 0 : TimelineCanvas.TrackBaseHeight(track));
            var viewport = TimelineCanvas.TrackViewport(track.Kind);
            return edge >= viewport.Top && edge < viewport.Bottom - 4 && Math.Abs(edge - y) <= 5;
        });

    private void TrackHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(TrackHeaderPanel).Properties.IsLeftButtonPressed) return;
        var y = e.GetPosition(TrackHeaderPanel).Y;
        if (Math.Abs(y - TimelineCanvas.DividerY - 4) <= 6)
        {
            _resizingDivider = true;
            _resizeOriginY = y;
            _resizeOriginValue = TimelineCanvas.DividerY;
        }
        else if (HeightHandleAt(y) is { } track)
        {
            _resizingTrackId = track.Id;
            _resizeOriginY = y;
            _resizeOriginValue = TimelineCanvas.TrackBaseHeight(track);
            _trackResizeDividerY = TimelineCanvas.DividerY;
            // El separador no debe seguir la suma natural de alturas durante el gesto.
            TimelineCanvas.PreviewDivider(_trackResizeDividerY);
        }
        else return;
        e.Pointer.Capture(TrackHeaderPanel);
        e.Handled = true;
    }

    private void TrackHeaderPointerMoved(object? sender, PointerEventArgs e)
    {
        var y = e.GetPosition(TrackHeaderPanel).Y;
        if (_resizingDivider) TimelineCanvas.PreviewDivider(_resizeOriginValue + y - _resizeOriginY);
        else if (_resizingTrackId != null)
        {
            var kind = _controller?.CurrentProject.ActiveSequence?.Tracks.FirstOrDefault(t => t.Id == _resizingTrackId)?.Kind;
            TimelineCanvas.PreviewTrackHeight(_resizingTrackId, _resizeOriginValue + (kind == "video" ? _resizeOriginY - y : y - _resizeOriginY));
        }
        else TrackHeaderPanel.Cursor = Math.Abs(y - TimelineCanvas.DividerY - 4) <= 6 || HeightHandleAt(y) != null
            ? new Cursor(StandardCursorType.SizeNorthSouth) : new Cursor(StandardCursorType.Arrow);
    }

    private void TrackHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_resizingDivider && _resizingTrackId == null) return;
        var y = e.GetPosition(TrackHeaderPanel).Y;
        e.Pointer.Capture(null);
        if (_resizingDivider)
        {
            var next = Math.Clamp(_resizeOriginValue + y - _resizeOriginY, 48, Math.Max(48, TimelineCanvas.Bounds.Height - 48));
            _resizingDivider = false;
            TimelineCanvas.PreviewDivider(null);
            if (Math.Abs(next - _resizeOriginValue) > 1)
                _controller?.UndoRedo.Commit(p => p.ActiveSequence!.VideoAreaHeight = next, "Separar pistas de vídeo y audio");
        }
        else if (_resizingTrackId is { } id)
        {
            var kind = _controller?.CurrentProject.ActiveSequence?.Tracks.FirstOrDefault(t => t.Id == id)?.Kind;
            var next = (int)Math.Round(Math.Clamp(_resizeOriginValue + (kind == "video" ? _resizeOriginY - y : y - _resizeOriginY), 30, 180));
            _resizingTrackId = null;
            TimelineCanvas.PreviewTrackHeight(id, null);
            TimelineCanvas.PreviewDivider(null);
            if (Math.Abs(next - _resizeOriginValue) > 1)
                _controller?.UndoRedo.Commit(p =>
                {
                    var sequence = p.ActiveSequence!;
                    if (sequence.VideoAreaHeight == null) sequence.VideoAreaHeight = _trackResizeDividerY;
                    var track = sequence.Tracks.First(t => t.Id == id);
                    track.PresentationHeight = next;
                    track.Compact = false;
                }, "Cambiar altura de pista");
        }
        e.Handled = true;
    }
}
