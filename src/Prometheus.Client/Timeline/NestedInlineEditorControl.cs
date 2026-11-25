using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Timeline;

public sealed class NestedInlineEditorControl : Control
{
    private const double Header = 25;
    private const double Row = 30;
    private const double LabelWidth = 0;
    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#17191B"));
    private static readonly IBrush LaneA = new SolidColorBrush(Color.Parse("#202328"));
    private static readonly IBrush LaneB = new SolidColorBrush(Color.Parse("#25282D"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#FF8A00"));
    private readonly TimelineCanvasControl _timeline;
    private Project? _project;
    private Clip? _outer;
    private NestedContent? _content;
    private List<Track> _lanes = new();
    private string? _expandedChildId;
    private string? _lastPeerSwitchId;
    private DateTime _lastPeerSwitchAt;
    private Clip? _expandedChild;
    private NestedContent? _expandedContent;
    private List<Track> _expandedLanes = new();
    private string? _dragId;
    private string? _selectedChildId;
    private bool _middlePanning;
    private Point _panOrigin;
    private bool _dragDeep;
    private int _dragEdge;
    private Point _dragOrigin;
    private double _originalStart;
    private double _originalDuration;
    private string? _previewTrack;
    private double _previewStart;
    private double _previewDuration;

    public event Action? CloseRequested;
    public event Action<string, string, double, double, string>? ChildChanged;
    public event Action<string>? ChildPropertiesRequested;
    public event Action<string>? ChildInspectorRequested;
    public event Action<string>? ChildRemoveRequested;
    public event Action? LayoutChanged;
    public event Action<string>? NestedPeerSelected;

    public NestedInlineEditorControl(TimelineCanvasControl timeline)
    {
        _timeline = timeline;
        ClipToBounds = true;
        Focusable = true;
    }

    public string? OuterId => _outer?.Id;
    public string? SelectedChildId => _selectedChildId;
    public void DeleteSelectedChild()
    {
        if (_selectedChildId != null) ChildRemoveRequested?.Invoke(_selectedChildId);
    }
    public void ExpandChild(string childId)
    {
        if (_content?.Clips.FirstOrDefault(c => c.Id == childId)?.NestedContent == null) return;
        _lastPeerSwitchId = childId;
        _lastPeerSwitchAt = DateTime.UtcNow;
        _expandedChildId = childId;
        Set(_project, _outer?.Id);
        LayoutChanged?.Invoke();
    }
    public double DesiredHeight => _content == null ? 0 : Header + (_lanes.Count + _expandedLanes.Count) * Row + 2;

    public void Set(Project? project, string? outerId)
    {
        if (_outer?.Id != outerId) { _expandedChildId = null; _selectedChildId = null; }
        _project = project;
        _outer = project?.ActiveSequence?.Clips.FirstOrDefault(c => c.Id == outerId && c.NestedContent != null);
        _content = _outer?.NestedContent;
        _lanes = _content?.Tracks.Where(t => t.Kind == _outer?.NestedRole)
            .OrderBy(t => t.Kind == "video" ? -t.Order : t.Order).ToList() ?? new List<Track>();
        _expandedChild = _content?.Clips.FirstOrDefault(c => c.Id == _expandedChildId && c.NestedContent != null);
        _expandedContent = _expandedChild?.NestedContent;
        _expandedLanes = _expandedContent?.Tracks.Where(t => t.Kind == _expandedChild?.NestedRole)
            .OrderBy(t => t.Kind == "video" ? -t.Order : t.Order).ToList() ?? new List<Track>();
        if (_selectedChildId != null && _content?.Clips.Any(c => c.Id == _selectedChildId) != true &&
            _expandedContent?.Clips.Any(c => c.Id == _selectedChildId) != true) _selectedChildId = null;
        Height = DesiredHeight;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_outer == null || _content == null) return;
        context.FillRectangle(Background, Bounds);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#29231B")), new Rect(0, 0, Bounds.Width, Header));
        DrawText(context, _outer.Name + "  ·  minipistas", 8, 5, Brushes.Orange, 11);
        var close = CloseButtonRect();
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#282A2D")), new Pen(Brushes.Gray, 1), close, 3, 3);
        DrawText(context, "Cerrar ×", close.X + 5, close.Y + 4, Brushes.LightGray, 10);
        for (var i = 0; i < _lanes.Count; i++)
        {
            var lane = _lanes[i];
            var y = Header + i * Row;
            context.FillRectangle(i % 2 == 0 ? LaneA : LaneB, new Rect(0, y, Bounds.Width, Row));
            DrawText(context, lane.Name + "  ·  interna", 7, y + 8, Brushes.LightGray, 10);
            context.DrawLine(new Pen(Brushes.Black, 1), new Point(0, y + Row), new Point(Bounds.Width, y + Row));
        }
        for (var i = 0; i < _expandedLanes.Count; i++)
        {
            var lane = _expandedLanes[i];
            var y = Header + (_lanes.Count + i) * Row;
            context.FillRectangle(i % 2 == 0 ? LaneB : LaneA, new Rect(0, y, Bounds.Width, Row));
            DrawText(context, "  " + lane.Name + "  ·  nivel 2", 7, y + 8, Brushes.LightGray, 10);
            context.DrawLine(new Pen(Brushes.Black, 1), new Point(0, y + Row), new Point(Bounds.Width, y + Row));
        }
        var outerLeft = _timeline.TimeToPixels(_outer.TimelineStart.Seconds);
        var outerRight = _timeline.TimeToPixels(_outer.EndSeconds);
        using var clip = context.PushClip(new Rect(Math.Max(LabelWidth, outerLeft), Header,
            Math.Max(0, Math.Min(Bounds.Width, outerRight) - Math.Max(LabelWidth, outerLeft)), Math.Max(0, Bounds.Height - Header)));
        if (_expandedChild != null)
        {
            var span = ChildRect(_expandedChild);
            var width = Math.Max(0, span.Right - span.Left);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(44, 154, 91, 187)),
                new Rect(span.Left, Header + _lanes.Count * Row, width, _expandedLanes.Count * Row));
        }
        foreach (var child in _content.Clips.Where(c => _lanes.Any(t => t.Id == c.TrackId)))
        {
            var rect = ChildRect(child);
            if (rect.Right < LabelWidth || rect.Left > Bounds.Width) continue;
            var nested = NestedMediaOperations.IsNested(child);
            context.DrawRectangle(new SolidColorBrush(Color.Parse(nested ? "#4D395C" : _outer.NestedRole == "audio" ? "#234436" : "#694323")),
                new Pen(child.Id == _dragId || child.Id == _selectedChildId ? Accent : new SolidColorBrush(Color.Parse("#B87945")), child.Id == _dragId || child.Id == _selectedChildId ? 2 : 1), rect, 3, 3);
            var available = Math.Max(0, rect.Width - 10);
            var budget = Math.Max(0, (int)(available / 6));
            var name = child.Name.Length <= budget ? child.Name : budget > 1 ? child.Name[..(budget - 1)] + "…" : "";
            using var textClip = context.PushClip(new Rect(rect.Left + 4, rect.Top + 2, available, rect.Height - 4));
            DrawText(context, nested ? (child.Id == _expandedChildId ? "▾ " : "▸ ") + name : name, rect.Left + 5, rect.Top + 7, Brushes.White, 10);
        }
        if (_expandedContent != null)
        foreach (var child in _expandedContent.Clips.Where(c => _expandedLanes.Any(t => t.Id == c.TrackId)))
        {
            var rect = DeepChildRect(child);
            if (rect.Right < LabelWidth || rect.Left > Bounds.Width) continue;
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#51425D")),
                new Pen(child.Id == _dragId || child.Id == _selectedChildId ? Accent : new SolidColorBrush(Color.Parse("#B19BC3")), child.Id == _dragId || child.Id == _selectedChildId ? 2 : 1), rect, 3, 3);
            var available = Math.Max(0, rect.Width - 10);
            var budget = Math.Max(0, (int)(available / 6));
            var name = child.Name.Length <= budget ? child.Name : budget > 1 ? child.Name[..(budget - 1)] + "…" : "";
            using var textClip = context.PushClip(new Rect(rect.Left + 4, rect.Top + 2, available, rect.Height - 4));
            DrawText(context, name, rect.Left + 5, rect.Top + 7, Brushes.White, 10);
        }
    }

    private Rect ChildRect(Clip child)
    {
        var laneIndex = _lanes.FindIndex(t => t.Id == (child.Id == _dragId && !_dragDeep ? _previewTrack : child.TrackId));
        if (laneIndex < 0 || _outer == null) return new Rect(0, 0, 0, 0);
        var start = child.Id == _dragId ? _previewStart : child.TimelineStart.Seconds;
        var duration = child.Id == _dragId ? _previewDuration : child.Duration.Seconds;
        var globalStart = _outer.TimelineStart.Seconds + (start - _outer.SourceIn.Seconds) / _outer.Speed.Seconds;
        return new Rect(_timeline.TimeToPixels(globalStart), Header + laneIndex * Row + 3,
            Math.Max(5, duration / _outer.Speed.Seconds * _timeline.ZoomPixelsPerSecond), Row - 6);
    }

    private Rect DeepChildRect(Clip child)
    {
        var laneIndex = _expandedLanes.FindIndex(t => t.Id == (child.Id == _dragId && _dragDeep ? _previewTrack : child.TrackId));
        if (laneIndex < 0 || _outer == null || _expandedChild == null) return new Rect(0, 0, 0, 0);
        var start = child.Id == _dragId && _dragDeep ? _previewStart : child.TimelineStart.Seconds;
        var duration = child.Id == _dragId && _dragDeep ? _previewDuration : child.Duration.Seconds;
        var nestedTime = _expandedChild.TimelineStart.Seconds + (start - _expandedChild.SourceIn.Seconds) / _expandedChild.Speed.Seconds;
        var globalStart = _outer.TimelineStart.Seconds + (nestedTime - _outer.SourceIn.Seconds) / _outer.Speed.Seconds;
        return new Rect(_timeline.TimeToPixels(globalStart), Header + (_lanes.Count + laneIndex) * Row + 3,
            Math.Max(5, duration / (_outer.Speed.Seconds * _expandedChild.Speed.Seconds) * _timeline.ZoomPixelsPerSecond), Row - 6);
    }

    private Rect CloseButtonRect() => new(_outer == null ? 0 : _timeline.TimeToPixels(_outer.EndSeconds) + 5, 3, 59, 19);

    private bool WithinParent(Point point) => _outer != null &&
        point.X >= _timeline.TimeToPixels(_outer.TimelineStart.Seconds) && point.X <= _timeline.TimeToPixels(_outer.EndSeconds);

    private Clip? Hit(Point point) => !WithinParent(point) ? null : _content?.Clips.Where(c => _lanes.Any(t => t.Id == c.TrackId))
        .LastOrDefault(c => ChildRect(c).Contains(point));

    private Clip? HitDeep(Point point) => !WithinParent(point) || _expandedChild == null ||
        point.X < ChildRect(_expandedChild).Left || point.X > ChildRect(_expandedChild).Right
        ? null : _expandedContent?.Clips.Where(c => _expandedLanes.Any(t => t.Id == c.TrackId))
        .LastOrDefault(c => DeepChildRect(c).Contains(point));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_outer == null || _content == null) return;
        var point = e.GetPosition(this);
        var buttons = e.GetCurrentPoint(this).Properties;
        if (buttons.IsMiddleButtonPressed)
        {
            _middlePanning = true;
            _panOrigin = point;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (point.Y < Header)
        {
            if (buttons.IsLeftButtonPressed && CloseButtonRect().Contains(point)) CloseRequested?.Invoke();
            e.Handled = true;
            return;
        }
        var deep = HitDeep(point);
        var child = deep ?? Hit(point);
        if (child == null) { e.Handled = true; return; }
        _selectedChildId = child.Id;
        Focus();
        InvalidateVisual();
        if (deep == null && buttons.IsLeftButtonPressed && e.ClickCount > 1 && child.NestedContent != null)
        {
            if (_expandedChildId == child.Id && _lastPeerSwitchId == child.Id &&
                DateTime.UtcNow - _lastPeerSwitchAt < TimeSpan.FromMilliseconds(600))
            {
                e.Handled = true;
                return;
            }
            _expandedChildId = _expandedChildId == child.Id ? null : child.Id;
            Set(_project, _outer.Id);
            LayoutChanged?.Invoke();
            e.Handled = true;
            return;
        }
        if (buttons.IsRightButtonPressed)
        {
            var menu = new ContextMenu();
            var properties = new MenuItem { Header = "Propiedades del componente…" };
            properties.Click += (_, _) => ChildPropertiesRequested?.Invoke(child.Id);
            var inspector = new MenuItem { Header = "Editar en Controles de medio" };
            inspector.Click += (_, _) => ChildInspectorRequested?.Invoke(child.Id);
            var remove = new MenuItem { Header = "Quitar del medio anidado" };
            remove.Click += (_, _) => ChildRemoveRequested?.Invoke(child.Id);
            menu.Items.Add(properties);
            menu.Items.Add(inspector);
            menu.Items.Add(remove);
            ContextMenu = menu;
            menu.Open(this);
            e.Handled = true;
            return;
        }
        if (!buttons.IsLeftButtonPressed) return;
        ChildInspectorRequested?.Invoke(child.Id);
        var rect = deep == null ? ChildRect(child) : DeepChildRect(child);
        _dragId = child.Id;
        _dragDeep = deep != null;
        _dragOrigin = point;
        _dragEdge = point.X < rect.Left + Math.Min(7, rect.Width / 3) ? 1 : point.X > rect.Right - Math.Min(7, rect.Width / 3) ? 2 : 0;
        _originalStart = _previewStart = child.TimelineStart.Seconds;
        _originalDuration = _previewDuration = child.Duration.Seconds;
        _previewTrack = child.TrackId;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        if (_middlePanning)
        {
            _timeline.HorizontalOffsetSeconds = Math.Max(0, _timeline.HorizontalOffsetSeconds - (point.X - _panOrigin.X) / _timeline.ZoomPixelsPerSecond);
            if (_outer?.NestedRole == "video") _timeline.VideoScrollOffset += point.Y - _panOrigin.Y;
            else _timeline.AudioScrollOffset -= point.Y - _panOrigin.Y;
            _panOrigin = point;
            e.Handled = true;
            return;
        }
        if (_dragId == null || _outer == null || _content == null)
        {
            var deep = HitDeep(point);
            var hit = deep ?? Hit(point);
            if (hit != null)
            {
                var rect = deep == null ? ChildRect(hit) : DeepChildRect(hit);
                Cursor = new Cursor(point.X < rect.Left + 7 || point.X > rect.Right - 7
                    ? StandardCursorType.SizeWestEast : StandardCursorType.SizeAll);
            }
            else Cursor = new Cursor(StandardCursorType.Arrow);
            return;
        }
        var activeContent = _dragDeep ? _expandedContent! : _content;
        var activeLanes = _dragDeep ? _expandedLanes : _lanes;
        var child = activeContent.Clips.First(c => c.Id == _dragId);
        var stream = _project?.Assets.FirstOrDefault(a => a.Id == child.AssetId)?.Streams.FirstOrDefault(s => s.Id == child.StreamId);
        var still = stream?.Kind == "image";
        var fps = Math.Max(1, _project?.ActiveSequence?.Fps.Seconds ?? 60);
        var frame = 1.0 / fps;
        var nestedSpeed = _dragDeep ? _expandedChild!.Speed.Seconds : 1;
        var delta = Math.Round((point.X - _dragOrigin.X) / _timeline.ZoomPixelsPerSecond * _outer.Speed.Seconds * nestedSpeed / frame) * frame;
        if (_dragEdge == 1)
        {
            var earliest = still ? _originalStart : child.SourceIn.Seconds / child.Speed.Seconds;
            _previewStart = Math.Clamp(_originalStart + delta, Math.Max(0, _originalStart - earliest), _originalStart + _originalDuration - frame);
            _previewDuration = _originalDuration + _originalStart - _previewStart;
        }
        else if (_dragEdge == 2)
        {
            var maximum = still || stream == null || stream.Duration.Seconds <= 0
                ? 48 * 3600 : (stream.Duration.Seconds - child.SourceIn.Seconds) / child.Speed.Seconds;
            _previewDuration = Math.Clamp(_originalDuration + delta, frame, Math.Max(frame, maximum));
        }
        else
        {
            _previewStart = Math.Max(0, _originalStart + delta);
            var row = (int)((point.Y - Header) / Row) - (_dragDeep ? _lanes.Count : 0);
            if (row >= 0 && row < activeLanes.Count) _previewTrack = activeLanes[row].Id;
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_middlePanning && e.InitialPressMouseButton == MouseButton.Middle)
        {
            _middlePanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (_dragId == null || _outer == null || _previewTrack == null) return;
        var id = _dragId;
        var wasDeep = _dragDeep;
        var previousLane = (_dragDeep ? _expandedContent : _content)?.Clips.FirstOrDefault(c => c.Id == id)?.TrackId;
        var changed = Math.Abs(_previewStart - _originalStart) > 0.00001 || Math.Abs(_previewDuration - _originalDuration) > 0.00001 || previousLane != _previewTrack;
        _dragId = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
        if (changed)
            ChildChanged?.Invoke(_outer.Id, id, _previewStart, _previewDuration, _previewTrack);
        else if (!wasDeep && _expandedChild != null && _expandedChild.TrackId == previousLane && id != _expandedChild.Id &&
                 _content?.Clips.FirstOrDefault(c => c.Id == id)?.NestedContent != null)
            NestedPeerSelected?.Invoke(id);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragId = null;
        _middlePanning = false;
        InvalidateVisual();
    }

    private static void DrawText(DrawingContext context, string text, double x, double y, IBrush brush, double size)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Inter, Segoe UI, sans-serif"), size, brush);
        context.DrawText(formatted, new Point(x, y));
    }
}
