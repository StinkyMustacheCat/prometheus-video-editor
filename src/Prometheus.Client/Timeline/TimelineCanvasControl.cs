using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Timeline;

public partial class TimelineCanvasControl : Control
{
    private static readonly IBrush VideoClipBrush = new SolidColorBrush(Color.Parse("#193240"));
    private static readonly IBrush VideoClipBorderBrush = new SolidColorBrush(Color.Parse("#2B536B"));
    private static readonly IBrush AudioClipBrush = new SolidColorBrush(Color.Parse("#1A382B"));
    private static readonly IBrush AudioClipBorderBrush = new SolidColorBrush(Color.Parse("#2C5E49"));
    private static readonly IBrush SelectedClipBorderBrush = new SolidColorBrush(Color.Parse("#FF8A00"));
    private static readonly IBrush SelectedGapBrush = new SolidColorBrush(Color.Parse("#55FF8A00"));
    private static readonly IBrush MicroGapArcBrush = new SolidColorBrush(Color.Parse("#DE3934"));
    private static readonly IBrush MicroGapCenterBrush = new SolidColorBrush(Color.Parse("#FFE2DD"));
    private static readonly IBrush RazorGuideBrush = new SolidColorBrush(Color.Parse("#FFA126"));
    private static readonly IBrush SnapGuideBrush = new SolidColorBrush(Color.Parse("#FFA126"));
    private static readonly IBrush MarqueeFillBrush = new SolidColorBrush(Color.Parse("#26FF8A00"));
    private static readonly IBrush MarqueeStrokeBrush = new SolidColorBrush(Color.Parse("#FF8A00"));
    private static readonly IBrush GridLineBrush = new SolidColorBrush(Color.Parse("#161616"));
    private static readonly IBrush TrackSeparatorBrush = new SolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly IBrush ShortTailBrush = new SolidColorBrush(Color.Parse("#33DE3934"));
    private static readonly Pen ShortTailPen = new Pen(new SolidColorBrush(Color.Parse("#DE3934")), 1);

    private static readonly IBrush RollPieBrush = new SolidColorBrush(Color.Parse("#C0834E"));
    private static readonly IBrush RollPieHoverBrush = new SolidColorBrush(Color.Parse("#FF8A00"));
    private static readonly IBrush RangeCutFillBrush = new SolidColorBrush(Color.Parse("#38DE3934"));
    private static readonly Pen RangeCutStrokePen = new Pen(new SolidColorBrush(Color.Parse("#DE3934")), 1.5, DashStyle.Dash);
    private static readonly Pen LockedBarPen = new Pen(new SolidColorBrush(Color.Parse("#1B1B1B")), 2);
    private static readonly Pen MouseGuidePen = new Pen(new SolidColorBrush(Color.Parse("#44FFA126")), 1);

    private Sequence? _sequence;
    private Project? _project;
    private readonly HashSet<Task<WaveformColumns?>> _pendingWaveforms = new();
    private readonly WaveformDrawingCache _waveformDrawing = new();
    private DispatcherTimer? _waveformRetryTimer;
    private long _waveformRetryAt;
    private TimelineSelection _selection = new();
    private TimelineTool _activeTool = TimelineTool.Selection;
    private bool _isSnappingEnabled = true;
    public int SnapScope { get; set; }
    private double _zoomPixelsPerSecond = 40.0;
    private double _horizontalOffsetSeconds = 0.0;
    private double _playheadSeconds = 0.0;
    private double _avDividerHeight = 8.0;

    // Estados de interacción
    private bool _isDraggingClip;
    private bool _isTrimmingLeft;
    private bool _isTrimmingRight;
    private bool _isSlippingClip;
    private bool _isMarqueeSelecting;
    private SelectedGap? _pendingGapClick;
    private bool _isHandPanning;
    private bool _isMiddlePanning;
    private bool _isZoomSelecting;
    private double _zoomStartX;
    private double _zoomEndX;
    private bool _isRolling;
    private bool _isRangeCutting;
    private bool _isDirectionalSelecting;
    private bool _isAltDuplicating;
    private bool _isCtrlHeld;
    private IPointer? _gesturePointer;
    private string[] _selectionBeforeGesture = Array.Empty<string>();
    private SelectedGap? _gapBeforeGesture;

    private Point _pointerStartPos;
    private bool _editGestureMoved;
    private double _dragStartTime;
    private string? _activeDragClipId;
    private double _activeDragOriginalStart;
    private double _activeDragOriginalDuration;
    private double _slipOriginalSourceIn;
    private double? _previewSlipSourceIn;
    private double? _previewTrimStart;
    private double? _previewTrimDuration;
    private double? _previewTrimSourceIn;
    private double? _previewRollCut;
    private SnapResult _currentSnap;
    private Rect _marqueeRect;

    private double? _hoverPointerX;
    private string? _hoverTrimClipId;
    private int _hoverTrimSide; // 1 = left, 2 = right
    private string? _hoverRollLeftId;
    private string? _hoverRollRightId;
    private string? _activeRollLeftId;
    private string? _activeRollRightId;
    private string? _dropPlaceholderId;
    private double _rollOriginalCutTime;

    private double _rangeCutStart;
    private double _rangeCutEnd;

    private Point _directionalAnchorPos;
    private double _directionalAnchorTime;
    private string? _directionalAnchorTrackId;
    private string _directionalMode = "forward";
    private string? _directionalAxis;
    private bool _directionalMoved;
    private bool _directionalAdditive;
    private readonly HashSet<string> _directionalPreviewIds = new(StringComparer.OrdinalIgnoreCase);
    private int _repeatedClickGeneration;

    private readonly Dictionary<string, double> _draggingClipOriginalStarts = new();
    private readonly Dictionary<string, string> _draggingClipOriginalTracks = new();
    private double _dragDeltaSeconds;
    private string? _dragTargetTrackId;
    private string? _autoCreateTrackKind;
    private string? _externalNewTrackKind;
    private double _newTrackPointerY;

    public event Action<double>? PlayheadMoved;
    public event Action<double?>? PointerHoveredXChanged;
    public event Action<Action<Project>, string>? CommitRequested;
    public event Action? SelectionChanged;
    public event Action? FocusedClipChanged;
    public string? FocusedClipId { get; private set; }
    public event Action<double, double>? VisibleRangeChanged;
    public event Action<Clip?, Track?, double, SelectedGap?>? TimelineMenuRequested;
    public event Action<string>? SelectedClipClickedAgain;
    public event Action<string>? NestedClipDoubleClicked;
    public event Action<string>? ClipSelectionCompleted;

    public Clip? ClipAt(Point point) => HitTestClip(point);
    public string? DropPlaceholderId
    {
        get => _dropPlaceholderId;
        set { if (_dropPlaceholderId == value) return; _dropPlaceholderId = value; InvalidateVisual(); }
    }
    public void PreviewExternalNewTrack(string? kind, double y)
    {
        if (_externalNewTrackKind == kind && Math.Abs(_newTrackPointerY - y) < 0.5) return;
        _externalNewTrackKind = kind;
        _newTrackPointerY = y;
        InvalidateVisual();
    }

    public Sequence? Sequence
    {
        get => _sequence;
        set
        {
            _sequence = value;
            _drawingData = null;
            DrawingRevision++;
            if (_selection.CurrentGap is { } selectedGap &&
                (value == null || !value.Tracks.Any(t => t.Id == selectedGap.TrackId) ||
                !CollisionEngine.DetectGaps(value, selectedGap.TrackId).Any(g =>
                    Math.Abs(g.StartSeconds - selectedGap.StartSeconds) < 0.001 &&
                    Math.Abs(g.EndSeconds - selectedGap.EndSeconds) < 0.001)))
                _selection.Clear();
            ClampVerticalScroll();
            TrackLayoutChanged?.Invoke();
            InvalidateVisual();
        }
    }

    public Project? Project
    {
        get => _project;
        set { _project = value; _drawingData = null; InvalidateVisual(); }
    }

    public Track? TrackAtY(double y) => GetTrackAtY(y);

    public TimelineSelection Selection => _selection;

    public bool HasActiveEditGesture => _isDraggingClip || _isTrimmingLeft || _isTrimmingRight ||
        _isRolling || _isSlippingClip || _isRangeCutting;

    public TimelineTool ActiveTool
    {
        get => _activeTool;
        set
        {
            _activeTool = value;
            UpdateCursor();
            InvalidateVisual();
        }
    }

    public TimelineTool EffectiveTool =>
        (_activeTool != TimelineTool.Selection && _isCtrlHeld) ? TimelineTool.Selection : _activeTool;

    public bool IsSnappingEnabled
    {
        get => _isSnappingEnabled;
        set
        {
            _isSnappingEnabled = value;
            InvalidateVisual();
        }
    }

    public double ZoomPixelsPerSecond
    {
        get => _zoomPixelsPerSecond;
        set
        {
            _zoomPixelsPerSecond = Math.Clamp(value, MinimumZoomPixelsPerSecond, 1000.0);
            VisibleRangeChanged?.Invoke(_horizontalOffsetSeconds, Bounds.Width / _zoomPixelsPerSecond);
            InvalidateVisual();
        }
    }

    public bool AllowUnlimitedZoomOut { get; set; }

    public double MinimumZoomPixelsPerSecond
    {
        get
        {
            if (AllowUnlimitedZoomOut || _sequence == null || _sequence.Clips.Count == 0 || Bounds.Width <= 0)
                return 0.1;
            var end = _sequence.Clips.Max(c => c.EndSeconds);
            return Math.Clamp(Bounds.Width / (2 * Math.Max(5, end)), 0.1, 1000);
        }
    }

    public double HorizontalOffsetSeconds
    {
        get => _horizontalOffsetSeconds;
        set
        {
            _horizontalOffsetSeconds = Math.Max(0, value);
            VisibleRangeChanged?.Invoke(_horizontalOffsetSeconds, Bounds.Width / _zoomPixelsPerSecond);
            InvalidateVisual();
        }
    }

    public double PlayheadSeconds
    {
        get => _playheadSeconds;
        set
        {
            _playheadSeconds = Math.Max(0, value);
            PlayheadMoved?.Invoke(_playheadSeconds);
        }
    }

    public TimelineCanvasControl()
    {
        ClipToBounds = true;
        DetachedFromVisualTree += (_, _) => { _waveformRetryTimer?.Stop(); _waveformRetryAt = 0; };
        _selection.SelectionChanged += () =>
        {
            SelectionChanged?.Invoke();
            InvalidateVisual();
        };

        SizeChanged += (_, _) =>
        {
            ClampVerticalScroll();
            TrackLayoutChanged?.Invoke();
            VisibleRangeChanged?.Invoke(_horizontalOffsetSeconds, Bounds.Width / _zoomPixelsPerSecond);
        };
    }

    public double TimeToPixels(double seconds) => (seconds - _horizontalOffsetSeconds) * _zoomPixelsPerSecond;
    public double PixelsToTime(double x) => Math.Max(0, (x / _zoomPixelsPerSecond) + _horizontalOffsetSeconds);

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0 || _sequence == null)
            return;

        // 1. Fondo de lienzo
        context.FillRectangle(new SolidColorBrush(Color.Parse("#090909")), bounds);

        // 2. Líneas de cuadrícula temporal
        DrawTimeGrid(context, bounds);

        // 3. Pistas y Clips
        var videoTracks = _sequence.Tracks.Where(t => t.Kind == "video").OrderByDescending(t => t.Order).ToList();
        var audioTracks = _sequence.Tracks.Where(t => t.Kind == "audio").OrderBy(t => t.Order).ToList();

        // Renderizar pistas de vídeo
        foreach (var track in videoTracks)
        {
            DrawTrack(context, track, GetTrackY(track.Id, videoTracks, audioTracks), bounds.Width);
        }

        // Divisor A/V
        var dividerY = DividerY;
        context.FillRectangle(new SolidColorBrush(Color.Parse("#242424")), new Rect(0, dividerY, bounds.Width, _avDividerHeight));
        context.DrawLine(new Pen(TrackSeparatorBrush, 1), new Point(0, dividerY), new Point(bounds.Width, dividerY));
        context.DrawLine(new Pen(TrackSeparatorBrush, 1), new Point(0, dividerY + _avDividerHeight), new Point(bounds.Width, dividerY + _avDividerHeight));

        // Renderizar pistas de audio
        foreach (var track in audioTracks)
        {
            DrawTrack(context, track, GetTrackY(track.Id, videoTracks, audioTracks), bounds.Width);
        }

        // 3.5. Espacio vacío (Gap) seleccionado
        if (_selection.CurrentGap != null)
        {
            DrawSelectedGap(context, _selection.CurrentGap, videoTracks, audioTracks);
        }

        // 3.6. Indicador visual de auto-creación de nueva pista al arrastrar fuera de límites
        if ((_autoCreateTrackKind ?? _externalNewTrackKind) is { } newTrackKind)
        {
            DrawNewTrackIndicator(context, newTrackKind, bounds.Width, bounds.Height);
        }

        // 3.7. Cápsulas en arrastre (Z-Index superior: renderizadas siempre al frente)
        if (_isDraggingClip && _draggingClipOriginalStarts.Count > 0)
        {
            DrawMovingClips(context, videoTracks, audioTracks, bounds.Width, bounds.Height);
        }

        DrawTimelineMarks(context, bounds);

        // 4. Micro-gaps con arco rojo
        DrawMicroGaps(context, videoTracks, audioTracks);

        // 5. Tiradores de semicírculo para Roll Edit entre clips adyacentes
        DrawRollEditHandles(context, videoTracks, audioTracks);
        DrawTransitions(context);

        // 6. Área de corte de rango con cuchilla (C)
        if (_isRangeCutting)
        {
            DrawRangeCutOverlay(context, bounds);
        }

        // 7. Flecha de vector direccional para herramienta A
        if (_isDirectionalSelecting)
        {
            DrawDirectionalVector(context);
        }

        // 8. Línea de Snapping magnético
        if (_currentSnap.IsSnapped)
        {
            double snapX = TimeToPixels(_currentSnap.SnappedTime);
            if (snapX >= 0 && snapX <= bounds.Width)
            {
                context.DrawLine(new Pen(SnapGuideBrush, 1.5, DashStyle.Dash), new Point(snapX, 0), new Point(snapX, bounds.Height));
            }
        }

        // 9. Línea guía de cuchilla si se sitúa el ratón
        if (EffectiveTool == TimelineTool.Razor && _pointerStartPos.X >= 0 && !_isRangeCutting)
        {
            context.DrawLine(new Pen(RazorGuideBrush, 1.5), new Point(_pointerStartPos.X, 0), new Point(_pointerStartPos.X, bounds.Height));
        }

        // 10. Rectángulo Marquee
        if (_isMarqueeSelecting && _marqueeRect.Width > 0 && _marqueeRect.Height > 0)
        {
            context.FillRectangle(MarqueeFillBrush, _marqueeRect);
            context.DrawRectangle(new Pen(MarqueeStrokeBrush, 1), _marqueeRect);
        }

        // 11. Línea guía vertical de ratón
        if (_isZoomSelecting && Math.Abs(_zoomEndX - _zoomStartX) > 3)
        {
            var left = Math.Min(_zoomStartX, _zoomEndX);
            var rect = new Rect(left, 0, Math.Abs(_zoomEndX - _zoomStartX), bounds.Height);
            context.FillRectangle(MarqueeFillBrush, rect);
            context.DrawRectangle(new Pen(MarqueeStrokeBrush, 1), rect);
        }

        // 11. Línea guía vertical de ratón
        if (_hoverPointerX.HasValue && _hoverPointerX.Value >= 0 && _hoverPointerX.Value <= bounds.Width)
        {
            context.DrawLine(MouseGuidePen, new Point(_hoverPointerX.Value, 0), new Point(_hoverPointerX.Value, bounds.Height));
        }

    }

    private void DrawTimelineMarks(DrawingContext context, Rect bounds)
    {
        if (_sequence == null) return;
        if (_sequence.MarkInSeconds is { } markIn && _sequence.MarkOutSeconds is { } markOut && markOut > markIn)
        {
            var left = Math.Clamp(TimeToPixels(markIn), 0, bounds.Width);
            var right = Math.Clamp(TimeToPixels(markOut), 0, bounds.Width);
            if (right > left) context.FillRectangle(new SolidColorBrush(Color.Parse("#0D80BCE8")),
                new Rect(left, 0, right - left, bounds.Height));
        }
        foreach (var marker in _sequence.Markers)
        {
            var x = TimeToPixels(marker.Time.Seconds);
            if (x < 0 || x > bounds.Width) continue;
            var color = Color.FromArgb(80, (byte)(marker.ColorArgb >> 16),
                (byte)(marker.ColorArgb >> 8), (byte)marker.ColorArgb);
            context.DrawLine(new Pen(new SolidColorBrush(color), 1), new Point(x, 0), new Point(x, bounds.Height));
        }
        if (_sequence.MarkInSeconds is { } start) DrawRangeLine(start, "#A8E4A8");
        if (_sequence.MarkOutSeconds is { } end) DrawRangeLine(end, "#83C8E9");

        void DrawRangeLine(double at, string color)
        {
            var x = TimeToPixels(at);
            if (x >= 0 && x <= bounds.Width)
                context.DrawLine(new Pen(new SolidColorBrush(Color.Parse(color)), 1.5),
                    new Point(x, 0), new Point(x, bounds.Height));
        }
    }

    private void DrawTimeGrid(DrawingContext context, Rect bounds)
    {
        double stepSeconds = TimelineRulerStep.ForZoom(_zoomPixelsPerSecond);

        double firstTime = Math.Floor(_horizontalOffsetSeconds / stepSeconds) * stepSeconds;
        double maxTime = PixelsToTime(bounds.Width);

        var pen = new Pen(GridLineBrush, 1);

        for (double t = firstTime; t <= maxTime && t < firstTime + stepSeconds * 300; t += stepSeconds)
        {
            double x = TimeToPixels(t);
            if (x >= 0 && x <= bounds.Width)
            {
                context.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));
            }
        }
    }

    private void DrawTrack(DrawingContext context, Track track, double y, double totalWidth)
    {
        var height = BaseTrackHeight(track);
        var trackHeight = TrackHeight(track);
        var trackRect = new Rect(0, y, totalWidth, trackHeight);
        if (!trackRect.Intersects(TrackViewport(track.Kind))) return;
        using var viewportScope = context.PushClip(TrackViewport(track.Kind));
        var background = track.Role == "subtitle" ? "#201B2A" : track.Locked ? "#090909" : "#101010";
        var bgBrush = new SolidColorBrush(Color.Parse(background));

        context.FillRectangle(bgBrush, trackRect);

        // Si la pista está bloqueada, dibujar barras de bloqueo diagonales
        if (track.Locked)
        {
            DrawLockedTrackBars(context, trackRect);
        }

        context.DrawLine(new Pen(TrackSeparatorBrush, 1), new Point(0, y + trackHeight), new Point(totalWidth, y + trackHeight));

        if (_sequence == null)
            return;

        var drawing = GetDrawingData();
        // During trim/roll, a clip outside the old range can enter the viewport.
        var updatingBounds = _isTrimmingLeft || _isTrimmingRight || _isRolling;
        var clips = updatingBounds ? drawing.OnTrack(track.Id) :
            drawing.VisibleClips(track.Id, PixelsToTime(-4), PixelsToTime(totalWidth));

        // Si la pista está deshabilitada/oculta, atenuar clips a 35% de opacidad
        using var opacityScope = !track.Enabled ? context.PushOpacity(0.35) : default;

        var trimming = (_isTrimmingLeft || _isTrimmingRight) && _activeDragClipId != null &&
            _previewTrimStart.HasValue && _previewTrimDuration.HasValue;
        var trimAnchor = trimming ? _sequence.Clips.FirstOrDefault(c => c.Id == _activeDragClipId) : null;
        var trimTargets = trimAnchor == null ? null :
            EditGroupOperations.AlignedTrimTargets(_sequence, trimAnchor.Id, _isTrimmingLeft).ToHashSet();

        foreach (var clip in clips)
        {
            bool isMoving = _isDraggingClip && _draggingClipOriginalStarts.ContainsKey(clip.Id);
            if (isMoving && !_isAltDuplicating)
            {
                // Si se desplaza sin Alt, se renderiza al frente en la capa superior (DrawMovingClips)
                continue;
            }

            using var altGhostScope = (isMoving && _isAltDuplicating) ? context.PushOpacity(0.55) : default;

            double previewStart = clip.TimelineStart.Seconds;
            double previewDuration = clip.Duration.Seconds;
            if ((_isTrimmingLeft || _isTrimmingRight) && clip.Id == _activeDragClipId && _previewTrimStart.HasValue)
            {
                previewStart = _previewTrimStart.Value;
                previewDuration = _previewTrimDuration ?? previewDuration;
            }
            else if (trimAnchor != null && trimTargets?.Contains(clip.Id) == true)
            {
                var delta = _isTrimmingLeft ? _previewTrimStart!.Value - trimAnchor.TimelineStart.Seconds :
                    _previewTrimStart!.Value + _previewTrimDuration!.Value - trimAnchor.EndSeconds;
                if (_isTrimmingLeft) previewStart += delta;
                previewDuration = Math.Max(1.0 / Math.Max(1, _sequence.Fps.Seconds), previewDuration + (_isTrimmingLeft ? -delta : delta));
            }
            if (_isRolling && _previewRollCut.HasValue)
            {
                if (clip.Id == _activeRollLeftId) previewDuration = _previewRollCut.Value - previewStart;
                else if (clip.Id == _activeRollRightId)
                {
                    previewDuration += previewStart - _previewRollCut.Value;
                    previewStart = _previewRollCut.Value;
                }
            }
            double clipX = TimeToPixels(previewStart);
            double clipW = previewDuration * _zoomPixelsPerSecond;

            if (clipX + clipW < 0 || clipX > totalWidth)
                continue;

            var clipRect = new Rect(clipX, y + 2, Math.Max(4, clipW), height - 4);
            bool isSelected = _selection.IsSelected(clip.Id) || (_isDirectionalSelecting && _directionalPreviewIds.Contains(clip.Id));

            var asset = drawing.AssetFor(clip.AssetId);
            var label = asset?.Source.Kind == "web" && WebMediaService.GetState(asset.Id)?.Stage == "failed"
                ? Color.Parse("#C96E6E") : MediaLabelPalette.ForClip(clip, asset, track.Kind);
            var fillBrush = new SolidColorBrush(Color.FromArgb(145, label.R, label.G, label.B));
            var borderPen = isSelected
                ? new Pen(SelectedClipBorderBrush, 2)
                : new Pen(new SolidColorBrush(Color.FromArgb(190, label.R, label.G, label.B)), 1);

            if (clipW < 5 && track.Role == "subtitle" && !isSelected && !isMoving && !updatingBounds)
            {
                context.FillRectangle(fillBrush, new Rect(clipX, y + 3, Math.Max(1, clipW), height - 6));
                continue;
            }

            context.DrawRectangle(fillBrush, borderPen, clipRect, 3, 3);
            context.FillRectangle(new SolidColorBrush(label), new Rect(clipRect.Left + 2, clipRect.Top + 2, Math.Max(0, clipRect.Width - 4), 3));

            // Glow interno en el borde que se está recortando
            if (string.Equals(_hoverTrimClipId, clip.Id, StringComparison.OrdinalIgnoreCase) && _hoverTrimSide > 0 && !track.Locked)
            {
                DrawTrimHoverCue(context, clipRect, _hoverTrimSide);
            }

            // Título de clip
            var clipTitle = asset?.Source.Kind == "web" && WebMediaService.IsPending(asset) &&
                clip.Name == "Preparando medio…"
                ? WebMediaService.GetState(asset.Id)?.Message ?? "Preparando medio…" : clip.Name;
            var titleSpace = Math.Max(0, clipRect.Width - 12);
            var characterBudget = Math.Max(0, (int)(titleSpace / 6.2));
            var displayName = clipTitle.Length <= characterBudget ? clipTitle
                : characterBudget >= 2 ? clipTitle[..(characterBudget - 1)] + "…" : string.Empty;
            if (track.ShowNames && displayName.Length > 0)
            {
                var formattedText = new FormattedText(displayName, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Inter, Segoe UI, sans-serif"), 10,
                    track.Locked ? Brushes.Gray : Brushes.White);
                using var titleClip = context.PushClip(new Rect(clipRect.Left + 4, clipRect.Top + 2, titleSpace, Math.Min(18, clipRect.Height - 2)));
                context.DrawText(formattedText, new Point(clipX + 6, y + 6));
            }
            var editGroup = drawing.GroupFor(clip.Id);
            if (editGroup != null)
            {
                var colors = new[] { "#5BC7F0", "#8FDB82", "#F1C053", "#D98DDE", "#F37F7D", "#69DCC1" };
                var groupColor = Color.Parse(colors[(Math.Max(1, editGroup.Number) - 1) % colors.Length]);
                context.FillRectangle(new SolidColorBrush(groupColor), new Rect(clipRect.Left + 3, clipRect.Bottom - 5, Math.Max(0, clipRect.Width - 6), 3));
                if (clipRect.Width >= 85)
                {
                    var badge = new FormattedText($"G{editGroup.Number}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Inter, Segoe UI, sans-serif"), 9, new SolidColorBrush(groupColor));
                    context.FillRectangle(new SolidColorBrush(Color.Parse("#202020")), new Rect(clipRect.Right - 29, clipRect.Top + 5, 25, 14));
                    context.DrawText(badge, new Point(clipRect.Right - 27, clipRect.Top + 5));
                }
            }
            if (clip.Id == _dropPlaceholderId)
            {
                context.DrawRectangle(null, new Pen(SelectedClipBorderBrush, 2, DashStyle.Dash), clipRect, 3, 3);
                var hint = new FormattedText("Conectar medio", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Inter, Segoe UI, sans-serif"), 10, Brushes.Orange);
                if (clipRect.Width > 95 && clipRect.Height > 28)
                    context.DrawText(hint, new Point(clipRect.Left + 6, clipRect.Bottom - 17));
            }
            if (asset != null && PlaceholderService.IsPlaceholder(asset) && _project != null &&
                WebMediaService.GetPlaceholderState(_project.Id, clip.Id) is { } pending)
            {
                var stateText = new FormattedText(pending.Message, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Inter, Segoe UI, sans-serif"), 10, Brushes.Orange);
                using var stateClip = context.PushClip(new Rect(clipRect.Left + 4, clipRect.Top + 17,
                    Math.Max(0, clipRect.Width - 8), Math.Max(0, clipRect.Height - 19)));
                context.DrawText(stateText, new Point(clipRect.Left + 6, clipRect.Top + 19));
            }
            if (track.Kind == "audio" && clipW > 6) DrawRealWaveform(context, clip, asset, track, clipX, y, clipW, height);
            if (_isSlippingClip && clip.Id == _activeDragClipId && _previewSlipSourceIn.HasValue)
            {
                var slipText = new FormattedText($"Fuente {_previewSlipSourceIn.Value:0.00} s", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Inter, Segoe UI, sans-serif"), 10, Brushes.Orange);
                context.DrawText(slipText, new Point(clipX + 6, y + 22));
            }

            // Indicador de Short Tail
            if (clip.AvailableSourceDurationSeconds.HasValue && !StillImageRules.IsStill(_project, clip))
            {
                var shortTail = CollisionEngine.CheckShortTail(clip, clip.AvailableSourceDurationSeconds.Value);
                if (shortTail.HasShortTail)
                {
                    double availW = shortTail.AvailableMediaSeconds * _zoomPixelsPerSecond;
                    double missingW = shortTail.MissingDurationSeconds * _zoomPixelsPerSecond;
                    var hatchRect = new Rect(clipX + availW, y + 2, Math.Max(2, missingW), height - 4);
                    DrawShortTailHatch(context, hatchRect);
                }
            }
        }
    }

    private void DrawLockedTrackBars(DrawingContext context, Rect rect)
    {
        using (context.PushClip(rect))
        {
            double step = 20.0;
            for (double d = -rect.Height; d < rect.Width + rect.Height; d += step)
            {
                var p1 = new Point(rect.Left + d, rect.Bottom);
                var p2 = new Point(rect.Left + d + rect.Height, rect.Top);
                context.DrawLine(LockedBarPen, p1, p2);
            }
        }
    }

    private void DrawTrimHoverCue(DrawingContext context, Rect clipRect, int side)
    {
        double width = Math.Min(12, clipRect.Width / 2);
        double x = side == 1 ? clipRect.Left : clipRect.Right - width;
        var handle = new Rect(x, clipRect.Top + 2, width, clipRect.Height - 4);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#66FF8A00")), handle, 2);
        double edgeX = side == 1 ? clipRect.Left : clipRect.Right;
        context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#FFB347")), 2),
            new Point(edgeX, clipRect.Top + 3), new Point(edgeX, clipRect.Bottom - 3));
        if (width >= 8)
        {
            double gripX = side == 1 ? x + width - 4 : x + 4;
            var gripPen = new Pen(new SolidColorBrush(Color.Parse("#FFF0D0")), 1.5);
            context.DrawLine(gripPen, new Point(gripX, clipRect.Center.Y - 5), new Point(gripX, clipRect.Center.Y + 5));
        }
    }

    private const double MinimumRollWidth = 20;
    private bool CanShowRoll(Clip left, Clip right) =>
        Math.Min(left.Duration.Seconds, right.Duration.Seconds) * _zoomPixelsPerSecond >= MinimumRollWidth;

    private void DrawRollEditHandles(DrawingContext context, List<Track> videoTracks, List<Track> audioTracks)
    {
        if (_sequence == null) return;
        double radius = 7.0;

        foreach (var track in _sequence.Tracks)
        {
            if (track.Locked) continue;
            double trackY = GetTrackY(track.Id, videoTracks, audioTracks);
            if (trackY + TrackHeight(track) <= TrackViewport(track.Kind).Top || trackY >= TrackViewport(track.Kind).Bottom) continue;
            using var viewportScope = context.PushClip(TrackViewport(track.Kind));

            var nearStart = PixelsToTime(-radius);
            var nearEnd = PixelsToTime(Bounds.Width + radius);
            var candidates = _isDraggingClip && !_isAltDuplicating ? _sequence.Clips :
                GetDrawingData().VisibleClips(track.Id, nearStart, nearEnd);
            var trackClips = candidates
                .Select(c => (Clip: c, IsMoving: _isDraggingClip && !_isAltDuplicating && _draggingClipOriginalStarts.ContainsKey(c.Id)))
                .Select(item => (item.Clip, item.IsMoving,
                    TrackId: item.IsMoving ? MovingTrackId(item.Clip) : item.Clip.TrackId,
                    Start: item.IsMoving ? Math.Max(0, _draggingClipOriginalStarts[item.Clip.Id] + _dragDeltaSeconds) : item.Clip.TimelineStart.Seconds))
                .Where(item => string.Equals(item.TrackId, track.Id, StringComparison.OrdinalIgnoreCase) &&
                    item.Start <= nearEnd && item.Start + item.Clip.Duration.Seconds >= nearStart)
                .OrderBy(item => item.Start)
                .ToList();

            for (int i = 0; i < trackClips.Count - 1; i++)
            {
                var left = trackClips[i];
                var right = trackClips[i + 1];
                if (!CanShowRoll(left.Clip, right.Clip) ||
                    Math.Abs(left.Start + left.Clip.Duration.Seconds - right.Start) > 0.001)
                    continue;

                double cutTime = _isRolling && left.Clip.Id == _activeRollLeftId && right.Clip.Id == _activeRollRightId
                    ? _previewRollCut ?? left.Start + left.Clip.Duration.Seconds : left.Start + left.Clip.Duration.Seconds;
                double cutX = TimeToPixels(cutTime);
                if (cutX < 0 || cutX > Bounds.Width) continue;

                bool isHovered = (string.Equals(_hoverRollLeftId, left.Clip.Id) && string.Equals(_hoverRollRightId, right.Clip.Id)) ||
                                 (_isRolling && string.Equals(_activeRollLeftId, left.Clip.Id));

                double bottomY = trackY + TrackBaseHeight(track) - 2;

                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(cutX - radius, bottomY), isFilled: true);
                    ctx.ArcTo(new Point(cutX + radius, bottomY), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                    ctx.EndFigure(isClosed: true);
                }

                var brush = isHovered ? RollPieHoverBrush : RollPieBrush;
                var pen = isHovered ? new Pen(Brushes.White, 1.5) : new Pen(new SolidColorBrush(Color.Parse("#442F2824")), 1);

                context.DrawGeometry(brush, pen, geometry);
            }
        }
    }

    private string? MovingTrackId(Clip clip)
    {
        if (_sequence == null || _activeDragClipId == null || _dragTargetTrackId == null) return null;
        return ClipMoveTargets.TryResolve(_sequence, _draggingClipOriginalStarts.Keys, _activeDragClipId,
            _dragTargetTrackId, out var targets) && targets.TryGetValue(clip.Id, out var target) ? target : null;
    }

    private void DrawRangeCutOverlay(DrawingContext context, Rect bounds)
    {
        double startX = TimeToPixels(Math.Min(_rangeCutStart, _rangeCutEnd));
        double endX = TimeToPixels(Math.Max(_rangeCutStart, _rangeCutEnd));
        double width = Math.Max(2, endX - startX);

        var rangeRect = new Rect(startX, 0, width, bounds.Height);
        context.FillRectangle(RangeCutFillBrush, rangeRect);
        context.DrawRectangle(RangeCutStrokePen, rangeRect);

        // Líneas guía en los bordes con tijeras
        context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#DE3934")), 2), new Point(startX, 0), new Point(startX, bounds.Height));
        context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#DE3934")), 2), new Point(endX, 0), new Point(endX, bounds.Height));
    }

    private void DrawDirectionalVector(DrawingContext context)
    {
        var pen = new Pen(new SolidColorBrush(Color.Parse("#FF8A00")), 2);
        context.DrawLine(pen, _directionalAnchorPos, _pointerStartPos);

        // Dibujar círculo en origen
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#FF8A00")), null, _directionalAnchorPos, 4, 4);

        // Flecha y badge informativo
        string badgeText = _directionalMode switch
        {
            "backward" => "⇤ SELECCIÓN HACIA ATRÁS",
            "up" => "↑ SELECCIÓN PISTAS SUPERIORES",
            "down" => "↓ SELECCIÓN PISTAS INFERIORES",
            _ => "⇥ SELECCIÓN HACIA ADELANTE"
        };

        var text = new FormattedText(
            badgeText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter, Segoe UI, sans-serif"),
            10,
            Brushes.White);

        var badgeRect = new Rect(_pointerStartPos.X + 8, _pointerStartPos.Y - 18, text.Width + 12, 20);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#E6202020")), badgeRect, 3);
        context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#FF8A00")), 1), badgeRect, 3);
        context.DrawText(text, new Point(badgeRect.Left + 6, badgeRect.Top + 3));
    }

    private void DrawRealWaveform(DrawingContext context, Clip clip, Asset? asset, Track track,
        double x, double y, double width, double height)
    {
        var stream = asset?.Streams.FirstOrDefault(s => s.Id == clip.StreamId && s.Kind == "audio");
        var path = MgtProjectAudio.Resolve(asset) ?? asset?.EffectiveLocator?.Path;
        if (stream == null || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var source = WaveformPeakCache.SourceKey(path, stream.Ordinal, stream.SampleRate ?? 48000);
        if (source == null) return;
        var from = (int)Math.Ceiling(Math.Max(x + 3, 0));
        var to = (int)Math.Floor(Math.Min(x + width - 3, Bounds.Width));
        if (to <= from) return;
        var key = new WaveformDrawingKey(source, clip.TimelineStart.Seconds, clip.Duration.Seconds,
            clip.SourceIn.Seconds, clip.Speed.Seconds, _horizontalOffsetSeconds, _zoomPixelsPerSecond, from, to);
        var task = _waveformDrawing.Request(key, () => WaveformPeakCache.Request(path, stream.Ordinal, stream.SampleRate ?? 48000));
        if (!task.IsCompleted)
        {
            if (_pendingWaveforms.Add(task)) task.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
            {
                _pendingWaveforms.Remove(task);
                InvalidateVisual();
            }));
            return;
        }
        if (!task.IsCompletedSuccessfully || task.Result is not { } waveform)
        {
            var delay = _waveformDrawing.RetryDelay(key);
            var sourceDelay = WaveformPeakCache.RetryDelay(source);
            ScheduleWaveformRetry(delay > sourceDelay ? delay : sourceDelay);
            return;
        }
        var top = y + Math.Min(19, height * 0.4);
        var bottom = y + height - 3;
        if (bottom - top < 3) return;
        var middle = (top + bottom) / 2;
        var half = (bottom - top) / 2;
        var gain = Math.Pow(10, (clip.GainDb + track.GainDb) / 20);
        var pen = new Pen(new SolidColorBrush(Color.Parse("#70BFA0")), 1);
        var overrangePen = new Pen(new SolidColorBrush(Color.Parse("#FF9A4D")), 1);
        for (var i = 0; i < waveform.Columns.Length; i++)
        {
            var peak = waveform.Columns[i];
            if (!peak.HasSamples) continue;
            var px = waveform.FirstPixel + i;
            var minimum = peak.Minimum * gain;
            var maximum = peak.Maximum * gain;
            var upper = middle - Math.Clamp(maximum, -1, 1) * half;
            var lower = middle - Math.Clamp(minimum, -1, 1) * half;
            context.DrawLine(pen, new Point(px + 0.5, upper), new Point(px + 0.5, lower));
            if (maximum > 1) context.DrawLine(overrangePen, new Point(px, top), new Point(px + 1, top));
            if (minimum < -1) context.DrawLine(overrangePen, new Point(px, bottom), new Point(px + 1, bottom));
        }
    }

    private void ScheduleWaveformRetry(TimeSpan delay)
    {
        if (this.GetVisualRoot() == null || !IsEffectivelyVisible) return;
        var milliseconds = Math.Max(100, (long)Math.Ceiling(delay.TotalMilliseconds));
        var at = Environment.TickCount64 + milliseconds;
        if (_waveformRetryTimer?.IsEnabled == true && _waveformRetryAt <= at) return;
        if (_waveformRetryTimer == null)
        {
            _waveformRetryTimer = new DispatcherTimer();
            _waveformRetryTimer.Tick += (_, _) =>
            {
                _waveformRetryTimer.Stop(); _waveformRetryAt = 0;
                if (this.GetVisualRoot() != null && IsEffectivelyVisible) InvalidateVisual();
            };
        }
        _waveformRetryTimer.Stop();
        _waveformRetryAt = at;
        _waveformRetryTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _waveformRetryTimer.Start();
    }

    private void DrawShortTailHatch(DrawingContext context, Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        context.FillRectangle(ShortTailBrush, rect);

        using (context.PushClip(rect))
        {
            double step = 8.0;
            for (double d = -rect.Height; d < rect.Width + rect.Height; d += step)
            {
                var p1 = new Point(rect.Left + d, rect.Bottom);
                var p2 = new Point(rect.Left + d + rect.Height, rect.Top);
                context.DrawLine(ShortTailPen, p1, p2);
            }
        }
    }

    private void DrawMicroGaps(DrawingContext context, List<Track> videoTracks, List<Track> audioTracks)
    {
        if (_sequence == null)
            return;

        var microGaps = GetDrawingData().MicroGaps;

        foreach (var gap in microGaps)
        {
            if (TimeToPixels(gap.EndSeconds) < -6 || TimeToPixels(gap.StartSeconds) > Bounds.Width) continue;
            var track = _sequence.Tracks.FirstOrDefault(t => t.Id == gap.TrackId);
            if (track == null) continue;
            double trackY = GetTrackY(gap.TrackId, videoTracks, audioTracks);
            if (trackY + TrackHeight(track) <= TrackViewport(track.Kind).Top || trackY >= TrackViewport(track.Kind).Bottom) continue;
            using var viewportScope = context.PushClip(TrackViewport(track.Kind));

            double startX = TimeToPixels(gap.StartSeconds);
            double endX = TimeToPixels(gap.EndSeconds);
            double width = Math.Max(6, endX - startX);

            if (_selection.CurrentGap != null &&
                string.Equals(_selection.CurrentGap.TrackId, gap.TrackId, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(_selection.CurrentGap.StartSeconds - gap.StartSeconds) < 0.001)
            {
                context.FillRectangle(SelectedGapBrush, new Rect(startX, trackY + 2, width, TrackBaseHeight(track) - 4));
            }

            double arcX = startX + (width / 2.0);
            double arcY = trackY + TrackBaseHeight(track) - 8;

            context.DrawEllipse(MicroGapArcBrush, null, new Point(arcX, arcY), 5, 5);
            context.DrawEllipse(MicroGapCenterBrush, null, new Point(arcX, arcY), 2, 2);
        }
    }

    private double GetTrackY(string trackId, List<Track> videoTracks, List<Track> audioTracks)
    {
        double y = DividerY - videoTracks.Sum(TrackHeight) + _videoScroll;
        foreach (var t in videoTracks)
        {
            if (string.Equals(t.Id, trackId, StringComparison.OrdinalIgnoreCase))
                return y;
            y += TrackHeight(t);
        }

        y = DividerY + _avDividerHeight - _audioScroll;

        foreach (var t in audioTracks)
        {
            if (string.Equals(t.Id, trackId, StringComparison.OrdinalIgnoreCase))
                return y;
            y += TrackHeight(t);
        }

        return -1;
    }

    private Track? GetTrackAtY(double y)
    {
        if (_sequence == null) return null;
        var videoTracks = _sequence.Tracks.Where(t => t.Kind == "video").OrderByDescending(t => t.Order).ToList();
        var audioTracks = _sequence.Tracks.Where(t => t.Kind == "audio").OrderBy(t => t.Order).ToList();

        if (y < 0 || y > Bounds.Height) return null;
        var kind = y < DividerY ? "video" : y >= DividerY + _avDividerHeight ? "audio" : null;
        if (kind == null) return null;
        double currentY = kind == "video" ? DividerY - videoTracks.Sum(TrackHeight) + _videoScroll : DividerY + _avDividerHeight - _audioScroll;
        foreach (var t in kind == "video" ? videoTracks : audioTracks)
        {
            if (y >= currentY && y < currentY + TrackHeight(t)) return t;
            currentY += TrackHeight(t);
        }

        return null;
    }

    private void UpdateCursor()
    {
        Cursor = EffectiveTool switch
        {
            TimelineTool.Razor => new Cursor(StandardCursorType.Cross),
            TimelineTool.Hand => new Cursor(StandardCursorType.Hand),
            TimelineTool.Zoom => new Cursor(StandardCursorType.SizeAll),
            TimelineTool.Sam => new Cursor(StandardCursorType.Cross),
            TimelineTool.SelectForward => new Cursor(StandardCursorType.RightSide),
            TimelineTool.SelectBackward => new Cursor(StandardCursorType.LeftSide),
            TimelineTool.Slip => new Cursor(StandardCursorType.SizeWestEast),
            _ => new Cursor(StandardCursorType.Arrow)
        };
    }

    private void DrawSelectedGap(DrawingContext context, SelectedGap gap, List<Track> videoTracks, List<Track> audioTracks)
    {
        var track = _sequence?.Tracks.FirstOrDefault(t => t.Id == gap.TrackId);
        if (track == null) return;
        double trackY = GetTrackY(gap.TrackId, videoTracks, audioTracks);
        if (trackY + TrackHeight(track) <= TrackViewport(track.Kind).Top || trackY >= TrackViewport(track.Kind).Bottom) return;
        using var viewportScope = context.PushClip(TrackViewport(track.Kind));

        double startX = TimeToPixels(gap.StartSeconds);
        double endX = TimeToPixels(gap.EndSeconds);
        double width = Math.Max(4, endX - startX);

        var gapRect = new Rect(startX, trackY + 2, width, TrackBaseHeight(track) - 4);
        var gapFill = new SolidColorBrush(Color.FromArgb(0x35, 0xFF, 0x8A, 0x00));
        var gapBorder = new Pen(new SolidColorBrush(Color.Parse("#FF8A00")), 1.5);

        context.FillRectangle(gapFill, gapRect, 2);
        context.DrawRectangle(gapBorder, gapRect, 2);

        // Rayado diagonal ámbar dentro del hueco
        using (context.PushClip(gapRect))
        {
            var hatchPen = new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x8A, 0x00)), 1);
            for (double d = -gapRect.Height; d < gapRect.Width + gapRect.Height; d += 12)
            {
                context.DrawLine(hatchPen, new Point(gapRect.Left + d, gapRect.Bottom), new Point(gapRect.Left + d + gapRect.Height, gapRect.Top));
            }
        }
    }

    private void DrawNewTrackIndicator(DrawingContext context, string kind, double totalWidth, double currentY)
    {
        var firstVideoTop = DividerY - (_sequence?.Tracks.Where(t => t.Kind == "video").Sum(TrackHeight) ?? 0) + _videoScroll;
        var lastAudioBottom = DividerY + _avDividerHeight + (_sequence?.Tracks.Where(t => t.Kind == "audio").Sum(TrackHeight) ?? 0) - _audioScroll;
        double y = kind == "video" ? Math.Clamp(_newTrackPointerY - 20, 0, Math.Max(0, firstVideoTop - 8))
            : Math.Clamp(Math.Max(lastAudioBottom, _newTrackPointerY - 20), 0, Math.Max(0, currentY - 40));
        var rect = new Rect(0, y, totalWidth, 40);
        var pen = new Pen(new SolidColorBrush(Color.Parse("#FFA126")), 2, DashStyle.Dash);
        var fill = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xA1, 0x26));

        context.FillRectangle(fill, rect, 3);
        context.DrawRectangle(pen, rect, 3);

        string label = kind == "video" ? "+ Soltar para crear nueva pista de Vídeo" : "+ Soltar para crear nueva pista de Audio";
        var ft = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter, Segoe UI, sans-serif"),
            11,
            Brushes.White);
        context.DrawText(ft, new Point(20, y + 12));
    }

    private void DrawMovingClips(DrawingContext context, List<Track> videoTracks, List<Track> audioTracks, double totalWidth, double totalTracksY)
    {
        if (_sequence == null) return;

        foreach (var kvp in _draggingClipOriginalStarts)
        {
            var originalClip = _sequence.Clips.Find(c => c.Id == kvp.Key);
            if (originalClip == null) continue;

            double movingStart = Math.Max(0, kvp.Value + _dragDeltaSeconds);
            double clipX = TimeToPixels(movingStart);
            double clipW = originalClip.Duration.Seconds * _zoomPixelsPerSecond;

            string targetTrackId = MovingTrackId(originalClip) ?? originalClip.TrackId;

            double trackY = GetTrackY(targetTrackId, videoTracks, audioTracks);
            if (_autoCreateTrackKind != null && originalClip.Id == _activeDragClipId)
            {
                trackY = _autoCreateTrackKind == "video" ? Math.Max(0, _newTrackPointerY - 20) : Math.Min(totalTracksY - 42, _newTrackPointerY - 20);
            }
            if (trackY < 0) trackY = 0;

            var clipRect = new Rect(clipX, trackY + 2, Math.Max(4, clipW),
                (_sequence.Tracks.FirstOrDefault(t => t.Id == targetTrackId) is { } destination ? TrackBaseHeight(destination) : 44) - 4);

            // Sombra para Z-Index superior
            var shadowRect = new Rect(clipRect.Left + 2, clipRect.Top + 2, clipRect.Width, clipRect.Height);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00)), shadowRect, 3);

            // Cuerpo del clip
            var track = _sequence.Tracks.Find(t => t.Id == targetTrackId);
            bool isVideo = track != null ? track.Kind == "video" : (_autoCreateTrackKind == "video" || originalClip.TrackId.StartsWith("V"));
            var asset = _project?.Assets.FirstOrDefault(a => a.Id == originalClip.AssetId);
            var color = MediaLabelPalette.ForClip(originalClip, asset, isVideo ? "video" : "audio");
            var fillBrush = new SolidColorBrush(Color.FromArgb(165, color.R, color.G, color.B));
            var borderPen = new Pen(SelectedClipBorderBrush, 2.5);

            context.FillRectangle(fillBrush, clipRect, 3);
            context.DrawRectangle(borderPen, clipRect, 3);

            // Título
            string displayName = _isAltDuplicating ? $"{originalClip.Name} (Copia)" : originalClip.Name;
            var maxCharacters = Math.Max(0, (int)((clipRect.Width - 12) / 6.2));
            if (displayName.Length > maxCharacters) displayName = maxCharacters >= 2 ? displayName[..(maxCharacters - 1)] + "…" : string.Empty;
            var formattedText = new FormattedText(
                displayName,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter, Segoe UI, sans-serif"),
                10,
                Brushes.White);
            using var movingTitleClip = context.PushClip(new Rect(clipRect.Left + 4, clipRect.Top + 2, Math.Max(0, clipRect.Width - 12), Math.Min(18, clipRect.Height - 2)));
            context.DrawText(formattedText, new Point(clipX + 6, trackY + 6));
        }
    }
}
