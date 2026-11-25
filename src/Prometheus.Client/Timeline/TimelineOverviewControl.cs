using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Prometheus.Core.Model;

namespace Prometheus.Client.Timeline;

public class TimelineOverviewControl : Control
{
    private TimelineCanvasControl? _canvas;
    private bool _isDraggingThumb;
    private double _dragStartMouseX;
    private double _dragStartOffsetSeconds;
    private int _geometryRevision = -1;
    private double _geometryWidth;
    private double _geometryDuration;
    private Rect[] _videoBars = [];
    private Rect[] _audioBars = [];
    private int _durationRevision = -1;
    private double _contentEnd;

    private static readonly IBrush TrackBgBrush = new SolidColorBrush(Color.Parse("#121212"));
    private static readonly IBrush VideoMiniBrush = new SolidColorBrush(Color.Parse("#2B536B"));
    private static readonly IBrush AudioMiniBrush = new SolidColorBrush(Color.Parse("#2C5E49"));
    private static readonly IBrush ThumbFillBrush = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
    private static readonly Pen ThumbBorderPen = new Pen(new SolidColorBrush(Color.Parse("#FF8A00")), 1.5);
    private static readonly Pen PlayheadPen = new Pen(new SolidColorBrush(Color.Parse("#FF8A00")), 1.5);

    public TimelineCanvasControl? Canvas
    {
        get => _canvas;
        set
        {
            if (_canvas != null)
            {
                _canvas.VisibleRangeChanged -= OnCanvasVisibleRangeChanged;
                _canvas.PlayheadMoved -= OnCanvasPlayheadMoved;
            }

            _canvas = value;
            _durationRevision = _geometryRevision = -1;

            if (_canvas != null)
            {
                _canvas.VisibleRangeChanged += OnCanvasVisibleRangeChanged;
                _canvas.PlayheadMoved += OnCanvasPlayheadMoved;
            }

            InvalidateVisual();
        }
    }

    public TimelineOverviewControl()
    {
        Height = 18;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    private void OnCanvasVisibleRangeChanged(double offset, double span) => InvalidateVisual();
    private void OnCanvasPlayheadMoved(double time) => InvalidateVisual();

    private double GetTotalDuration()
    {
        if (_canvas?.Sequence == null)
            return 30.0;

        if (_durationRevision != _canvas.DrawingRevision)
        {
            _durationRevision = _canvas.DrawingRevision;
            _contentEnd = _canvas.Sequence.Clips.Select(c => c.EndSeconds)
                .Concat(_canvas.Sequence.Markers.Select(m => m.Time.Seconds))
                .Append(_canvas.Sequence.MarkOutSeconds ?? 0).Max();
        }
        return Math.Max(30.0, Math.Max(_contentEnd + 15.0,
            _canvas.HorizontalOffsetSeconds + Bounds.Width / _canvas.ZoomPixelsPerSecond));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        // 1. Fondo de barra
        context.FillRectangle(TrackBgBrush, bounds);
        context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#1A1A1A")), 1), new Point(0, 0), new Point(bounds.Width, 0));

        if (_canvas?.Sequence == null)
            return;

        double totalDuration = GetTotalDuration();
        double w = bounds.Width;

        // 2. Previsualización en miniatura de pistas y clips
        UpdateBars(_canvas.Sequence, _canvas.DrawingRevision, w, totalDuration);
        foreach (var rect in _videoBars) context.FillRectangle(VideoMiniBrush, rect, 1);
        foreach (var rect in _audioBars) context.FillRectangle(AudioMiniBrush, rect, 1);

        // 3. Mini cabezal de reproducción
        double playheadX = (_canvas.PlayheadSeconds / totalDuration) * w;
        if (playheadX >= 0 && playheadX <= w)
        {
            context.DrawLine(PlayheadPen, new Point(playheadX, 0), new Point(playheadX, bounds.Height));
        }

        // 4. Ventana / Thumb de visibilidad actual
        double visibleSpan = bounds.Width / _canvas.ZoomPixelsPerSecond;
        double thumbX = (_canvas.HorizontalOffsetSeconds / totalDuration) * w;
        double thumbW = Math.Max(24.0, (visibleSpan / totalDuration) * w);

        var thumbRect = new Rect(Math.Max(0, thumbX), 1, Math.Min(w - thumbX, thumbW), bounds.Height - 2);
        context.FillRectangle(ThumbFillBrush, thumbRect, 2);
        context.DrawRectangle(ThumbBorderPen, thumbRect, 2);
    }

    private void UpdateBars(Sequence sequence, int revision, double width, double duration)
    {
        if (_geometryRevision == revision && _geometryWidth == width && _geometryDuration == duration) return;
        _geometryRevision = revision;
        _geometryWidth = width;
        _geometryDuration = duration;

        var tracks = sequence.Tracks.ToLookup(track => track.Id, StringComparer.Ordinal);
        var clips = sequence.Clips.Where(clip => tracks.Contains(clip.TrackId)).ToLookup(clip =>
            tracks[clip.TrackId].First().Kind == "video");
        _videoBars = Bars(clips[true], 3);
        _audioBars = Bars(clips[false], 10);

        Rect[] Bars(System.Collections.Generic.IEnumerable<Clip> lane, double y)
        {
            var bars = new System.Collections.Generic.List<Rect>();
            var scale = width / duration;
            foreach (var clip in lane.OrderBy(clip => clip.TimelineStart.Seconds))
            {
                var left = clip.TimelineStart.Seconds * scale;
                var right = left + Math.Max(2, clip.Duration.Seconds * scale);
                if (bars.Count > 0 && bars[^1].Right >= left)
                {
                    var previous = bars[^1];
                    bars[^1] = new Rect(previous.X, y, Math.Max(previous.Right, right) - previous.X, 5);
                }
                else bars.Add(new Rect(left, y, right - left, 5));
            }
            return bars.ToArray();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_canvas == null) return;

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            _isDraggingThumb = true;
            e.Pointer.Capture(this);

            double totalDuration = GetTotalDuration();
            double clickedTime = (point.Position.X / Bounds.Width) * totalDuration;
            double visibleSpan = _canvas.Bounds.Width / _canvas.ZoomPixelsPerSecond;

            // Centrar ventana en clic
            _canvas.HorizontalOffsetSeconds = Math.Max(0, clickedTime - (visibleSpan / 2.0));
            _dragStartMouseX = point.Position.X;
            _dragStartOffsetSeconds = _canvas.HorizontalOffsetSeconds;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isDraggingThumb && _canvas != null)
        {
            var pos = e.GetPosition(this);
            double deltaPixels = pos.X - _dragStartMouseX;
            double totalDuration = GetTotalDuration();
            double deltaSeconds = (deltaPixels / Bounds.Width) * totalDuration;

            _canvas.HorizontalOffsetSeconds = Math.Max(0, _dragStartOffsetSeconds + deltaSeconds);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDraggingThumb)
        {
            _isDraggingThumb = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
}
