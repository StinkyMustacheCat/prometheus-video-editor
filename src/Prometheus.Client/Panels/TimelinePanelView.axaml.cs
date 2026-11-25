using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;
using Prometheus.Client.Timeline;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView : UserControl
{
    public TimelineSequenceHeaderControl HeaderTabs { get; } = new();
    private bool _isScrubbingRuler;
    private ProjectController? _controller;
    private readonly List<TextBlock> _rulerLabels = new();
    private readonly List<Line> _rulerTicks = new();
    private readonly List<Control> _rulerMarks = new();
    private string? _draggingMarkerId;
    private double _markerDragTime;
    private double _markerDragStartX;
    private Line? _rulerMouseGuideLine;
    private bool _isUpdatingZoom;
    private int _precisionFrames = 1;
    private int _snapScope;
    private Flyout? _timelineSettingsFlyout;
    private ComboBox? _gapModePicker;
    private bool _refreshingGapMode;
    private (string[] ItemIds, double SourceIn, double? SourceOut, string ProjectId, string SequenceId)? _pendingShelfInsertion;

    private PlaybackTransport? _transport;
    private bool _applyingTransportPosition;
    private string? _displayedSequenceId;

    public TimelinePanelView()
    {
        InitializeComponent();
        SetupTimelineIcons();

        SetupToolbar();
        SetupTimecodeEditor();
        SetupTrackLayout();
        SetupNestedInline();
        SetupRuler();
        InitializeTimelineFeatures();

        TimelineCanvas.PlayheadMoved += OnPlayheadMoved;
        TimelineCanvas.SelectionChanged += OnSelectionChanged;
        TimelineCanvas.FocusedClipChanged += OnSelectionChanged;
        TimelineCanvas.PointerHoveredXChanged += UpdateRulerMouseGuide;

        // Sincronización continua de cabezal y regla ante zoom / desplazamiento (Previene desfasaje Img 3)
        TimelineCanvas.VisibleRangeChanged += (offset, span) =>
        {
            ZoomSlider.Minimum = TimelineCanvas.MinimumZoomPixelsPerSecond;
            if (!_isUpdatingZoom)
            {
                _isUpdatingZoom = true;
                try
                {
                    if (Math.Abs(ZoomSlider.Value - TimelineCanvas.ZoomPixelsPerSecond) > 0.5)
                    {
                        ZoomSlider.Value = TimelineCanvas.ZoomPixelsPerSecond;
                    }
                }
                finally
                {
                    _isUpdatingZoom = false;
                }
            }

            UpdatePlayheadUI(TimelineCanvas.PlayheadSeconds);
            UpdateRulerVisuals();
            OverviewScrollBar.InvalidateVisual();
        };

        OverviewScrollBar.Canvas = TimelineCanvas;

        AddHandler(KeyDownEvent, Timeline_KeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        Loaded += (_, _) =>
        {
            if (_controller == null && ProjectController.Instance != null)
            {
                AttachController(ProjectController.Instance);
            }
            OverviewScrollBar.Canvas = TimelineCanvas;
            UpdateRulerVisuals();
            UpdatePlayheadUI(TimelineCanvas.PlayheadSeconds);
        };
    }

    public void AttachController(ProjectController controller)
    {
        _controller = controller;
        controller.SamToolChanged += () =>
        {
            if (!controller.SamToolEnabled && controller.ShapeMaskTool == null &&
                TimelineCanvas.ActiveTool == TimelineTool.Sam)
                SetTool(TimelineTool.Selection);
        };
        controller.TextToolChanged += () =>
        {
            if (!controller.TextToolEnabled && TimelineCanvas.ActiveTool == TimelineTool.Text)
                SetTool(TimelineTool.Selection);
        };
        controller.TitleCreated += clipId =>
        {
            TimelineCanvas.Selection.SelectClip(clipId);
            var sequence = controller.CurrentProject.ActiveSequence;
            var topTrack = sequence?.Tracks.Where(track => track.Kind == "video" && track.Role != "subtitle")
                .MaxBy(track => track.Order);
            if (topTrack != null && sequence!.Clips.Any(clip => clip.Id == clipId && clip.TrackId == topTrack.Id))
                TimelineCanvas.VideoScrollOffset = TimelineCanvas.VideoScrollMaximum;
        };
        controller.CaptureSequenceView = () => (TimelineCanvas.PlayheadSeconds, TimelineCanvas.HorizontalOffsetSeconds, TimelineCanvas.ZoomPixelsPerSecond);
        _transport = controller.Transport;
        _transport.PositionChanged += OnTransportPositionChanged;
        _transport.PlayingChanged += UpdatePlayIcon;
        _controller.StateChanged += RefreshFromProject;
        _controller.ClipSelectionChanged += RefreshTransitionSelection;
        _controller.ClipLocateRequested += (clipId, at) =>
        {
            TimelineCanvas.Selection.SelectClip(clipId);
            TimelineCanvas.HorizontalOffsetSeconds = Math.Max(0, at - 1);
            TimelineCanvas.Focus();
        };
        WebMediaService.StateChanged += _ => TimelineCanvas.InvalidateVisual();

        TimelineCanvas.CommitRequested += (action, desc) =>
        {
            if (!CommitTimelineEdit(action, desc)) RefreshFromProject();
        };

        RefreshFromProject();
    }

    private bool CommitTimelineEdit(Action<Project> action, string description)
    {
        if (_controller == null) return false;
        try { _controller.UndoRedo.Commit(action, description); return true; }
        catch (Exception ex) { _controller.ReportStatus("No se pudo completar " + description + ": " + ex.Message); return false; }
    }

    private void RefreshFromProject()
    {
        if (_controller == null)
            return;

        var seq = _controller.CurrentProject.ActiveSequence;
        if (_pendingShelfInsertion is { } pending &&
            (pending.ProjectId != _controller.CurrentProject.Id || pending.SequenceId != seq?.Id)) CancelShelfPlacement();
        if (seq != null && seq.Id != _displayedSequenceId)
        {
            _displayedSequenceId = seq.Id;
            TimelineCanvas.ZoomPixelsPerSecond = seq.ViewZoomPixelsPerSecond;
            TimelineCanvas.HorizontalOffsetSeconds = seq.ViewOffsetSeconds;
            _transport?.Seek(seq.ViewPlayheadSeconds);
            TimelineCanvas.VideoScrollOffset = 0;
            TimelineCanvas.AudioScrollOffset = 0;
            TimelineCanvas.Selection.Clear();
        }
        TimelineCanvas.Project = _controller.CurrentProject;
        TimelineCanvas.Sequence = seq;
        TimelineCanvas.SubtitleLaneExpanded = seq?.SubtitleLaneExpanded == true;
        SubtitleLaneBtn.Classes.Set("nle-accent", TimelineCanvas.SubtitleLaneExpanded);
        SubtitleLaneBtn.Classes.Set("nle-btn", !TimelineCanvas.SubtitleLaneExpanded);
        ZoomSlider.Minimum = TimelineCanvas.MinimumZoomPixelsPerSecond;
        if (TimelineCanvas.ZoomPixelsPerSecond < ZoomSlider.Minimum)
            TimelineCanvas.ZoomPixelsPerSecond = ZoomSlider.Minimum;
        RefreshNestedInline();
        OnTransportPositionChanged(_transport?.PositionSeconds ?? 0);

        if (seq != null)
        {
            RefreshTrackHeaders(seq);
        }

        UpdatePlayheadUI(TimelineCanvas.PlayheadSeconds);
        UpdateRulerVisuals();
        OverviewScrollBar.InvalidateVisual();
    }

    private void SetupToolbar()
    {
        ToolTextBtn.Click += (_, _) => SetTool(TimelineCanvas.ActiveTool == TimelineTool.Text
            ? TimelineTool.Selection : TimelineTool.Text);
        SubtitleLaneBtn.Click += (_, _) => ToggleSubtitleLane();
        ToolVBtn.Click += (_, _) => SetTool(TimelineTool.Selection);
        ToolCBtn.Click += (_, _) => SetTool(TimelineTool.Razor);
        ToolABtn.Click += (_, _) => SetTool(TimelineTool.SelectForward);
        ToolHBtn.Click += (_, _) => SetTool(TimelineTool.Hand);
        ToolZBtn.Click += (_, _) => SetTool(TimelineTool.Zoom);
        ToolSamBtn.Click += (_, _) => SetTool(TimelineCanvas.ActiveTool == TimelineTool.Sam
            ? TimelineTool.Selection : TimelineTool.Sam);
        var maskMenu = new ContextMenu();
        foreach (var choice in new[] {
            ("Rectángulo", "rectangle"), ("Elipse", "ellipse"),
            ("Pluma", "polygon"), ("Recorte inteligente SAM", "sam") })
        {
            var item = new MenuItem { Header = choice.Item1 };
            item.Click += (_, _) => SelectMaskTool(choice.Item2);
            maskMenu.Items.Add(item);
        }
        ToolMaskMenuBtn.Click += (_, _) => maskMenu.Open(ToolMaskMenuBtn);
        AttachShortcutMenu(ToolVBtn, "tool.select");
        AttachShortcutMenu(ToolCBtn, "tool.razor");
        AttachShortcutMenu(ToolABtn, "tool.direction");
        AttachShortcutMenu(ToolHBtn, "tool.hand");
        AttachShortcutMenu(ToolZBtn, "tool.zoom");
        AttachShortcutMenu(ToolSamBtn, "tool.sam");
        AttachShortcutMenu(SnapToggle, "timeline.snap");
        UpdateShortcutTooltips();

        PlayPauseBtn.Click += (_, _) => _transport?.TogglePlayback();
        LoopToggle.IsCheckedChanged += (_, _) => { if (_transport != null) _transport.LoopEnabled = LoopToggle.IsChecked == true; };
        PreviewSpeedBox.SelectionChanged += (_, _) =>
        {
            var speeds = new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0 };
            if (_transport != null && PreviewSpeedBox.SelectedIndex >= 0 && PreviewSpeedBox.SelectedIndex < speeds.Length)
                _transport.SetPreviewSpeed(speeds[PreviewSpeedBox.SelectedIndex]);
        };
        TimelineSettingsButton.Click += (_, _) => ShowTimelineSettings();
        FitContentButton.Click += (_, _) => FitTimeline(false);
        FitSelectionButton.Click += (_, _) => FitTimeline(true);

        SnapToggle.IsCheckedChanged += (_, _) =>
        {
            TimelineCanvas.IsSnappingEnabled = SnapToggle.IsChecked == true;
        };

        ZoomSlider.ValueChanged += (_, _) =>
        {
            if (_isUpdatingZoom) return;
            _isUpdatingZoom = true;
            try
            {
                double newZoom = ZoomSlider.Value;
                double playhead = TimelineCanvas.PlayheadSeconds;
                double viewportWidth = TimelineCanvas.Bounds.Width > 0 ? TimelineCanvas.Bounds.Width : 800;

                // Anclar zoom de manera suave sobre la posición del lector
                TimelineCanvas.ZoomPixelsPerSecond = newZoom;
                TimelineCanvas.HorizontalOffsetSeconds = Math.Max(0, playhead - (viewportWidth / 2.0 / newZoom));

                UpdatePlayheadUI(playhead);
                UpdateRulerVisuals();
                OverviewScrollBar.InvalidateVisual();
            }
            finally
            {
                _isUpdatingZoom = false;
            }
        };
    }

    private void ShowTimelineSettings()
    {
        if (_timelineSettingsFlyout == null)
        {
            var panel = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
            panel.Children.Add(new TextBlock { Text = "Encajar a", FontSize = 11, Foreground = Brushes.White });
            var scope = new ComboBox
            {
                ItemsSource = new[] { "Medios · todas las pistas", "Pista · pista actual", "Lector · cabezal e inicio" },
                SelectedIndex = _snapScope,
                MinWidth = 195
            };
            ToolTip.SetTip(scope, "Puntos de encaje cuando el imán está activo");
            scope.SelectionChanged += (_, _) => TimelineCanvas.SnapScope = _snapScope = Math.Max(0, scope.SelectedIndex);
            panel.Children.Add(scope);
            panel.Children.Add(new TextBlock { Text = "Paso con flechas izquierda/derecha", FontSize = 11, Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 0) });
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            var slider = new Slider { Minimum = 1, Maximum = 30, Value = _precisionFrames, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 160 };
            var value = new TextBlock { Text = $"{_precisionFrames} f", FontSize = 11, Foreground = Brushes.LightGray, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            slider.ValueChanged += (_, _) => { _precisionFrames = (int)Math.Round(slider.Value); value.Text = $"{_precisionFrames} f"; };
            row.Children.Add(slider);
            row.Children.Add(value);
            panel.Children.Add(row);
            var unlimitedZoom = new CheckBox { Content = "Zoom de salida sin límite", IsChecked = TimelineCanvas.AllowUnlimitedZoomOut };
            unlimitedZoom.IsCheckedChanged += (_, _) =>
            {
                TimelineCanvas.AllowUnlimitedZoomOut = unlimitedZoom.IsChecked == true;
                ZoomSlider.Minimum = TimelineCanvas.MinimumZoomPixelsPerSecond;
                TimelineCanvas.ZoomPixelsPerSecond = TimelineCanvas.ZoomPixelsPerSecond;
            };
            panel.Children.Add(unlimitedZoom);
            panel.Children.Add(new TextBlock { Text = "Eliminar hueco", FontSize = 11, Foreground = Brushes.White });
            _gapModePicker = new ComboBox { ItemsSource = new[] { "Por pista", "Clásico · todas las pistas" } };
            _gapModePicker.SelectionChanged += (_, _) =>
            {
                if (_refreshingGapMode || _gapModePicker.SelectedIndex < 0) return;
                var allTracks = _gapModePicker.SelectedIndex == 1;
                CommitTimelineEdit(p => p.ActiveSequence!.CloseGapAllTracks = allTracks, "Cambiar el modo de eliminar huecos");
            };
            panel.Children.Add(_gapModePicker);
            var shortcutButton = new Button { Content = "Atajos de teclado…" };
            shortcutButton.Click += (_, _) => { _timelineSettingsFlyout?.Hide(); ShowShortcutSettings(); };
            panel.Children.Add(shortcutButton);
            _timelineSettingsFlyout = new Flyout
            {
                Content = new Border { Background = Brushes.Black, BorderBrush = new SolidColorBrush(Color.Parse("#333333")), BorderThickness = new Thickness(1), Child = panel }
            };
        }
        _refreshingGapMode = true;
        _gapModePicker!.SelectedIndex = _controller?.CurrentProject.ActiveSequence?.CloseGapAllTracks == true ? 1 : 0;
        _refreshingGapMode = false;
        _timelineSettingsFlyout.ShowAt(TimelineSettingsButton);
    }

    private void OnTransportPositionChanged(double time)
    {
        if (Math.Abs(TimelineCanvas.PlayheadSeconds - time) < 0.000001)
        {
            UpdatePlayheadUI(time);
            return;
        }
        _applyingTransportPosition = true;
        try { TimelineCanvas.PlayheadSeconds = time; }
        finally { _applyingTransportPosition = false; }
        if (_transport?.IsPlaying == true && !TimelineCanvas.HasActiveEditGesture) TimelineCanvas.EnsurePlayheadVisible();
    }

    private void FitTimeline(bool selected)
    {
        var sequence = _controller?.CurrentProject.ActiveSequence;
        if (sequence == null) return;
        var clips = selected
            ? sequence.Clips.Where(c => TimelineCanvas.Selection.SelectedClipIds.Contains(c.Id)).ToList()
            : sequence.Clips;
        if (selected && clips.Count == 0) { FitTimeline(false); return; }
        var end = clips.Select(c => c.EndSeconds)
            .Concat(selected ? Array.Empty<double>() : sequence.Markers.Select(m => m.Time.Seconds))
            .Append(selected ? 0 : sequence.MarkOutSeconds ?? 0).DefaultIfEmpty(30).Max();
        TimelineCanvas.FitTimeRange(selected ? clips.Min(c => c.TimelineStart.Seconds) : 0, end);
    }

    private void SetTool(TimelineTool tool)
    {
        var controller = _controller ?? ProjectController.Instance;
        if (controller != null)
        {
            controller.ShapeMaskTool = tool == TimelineTool.Sam && _selectedMaskTool != "sam"
                ? _selectedMaskTool : null;
            controller.SamToolEnabled = tool == TimelineTool.Sam && _selectedMaskTool == "sam";
        }
        if (tool == TimelineTool.Sam && controller?.SamToolEnabled == false &&
            controller?.ShapeMaskTool == null)
            tool = TimelineTool.Selection;
        TimelineCanvas.ActiveTool = tool;
        if (controller != null) controller.TextToolEnabled = tool == TimelineTool.Text;

        ToolVBtn.Classes.Set("nle-accent", tool == TimelineTool.Selection);
        ToolVBtn.Classes.Set("nle-btn", tool != TimelineTool.Selection);

        ToolCBtn.Classes.Set("nle-accent", tool == TimelineTool.Razor);
        ToolCBtn.Classes.Set("nle-btn", tool != TimelineTool.Razor);

        ToolABtn.Classes.Set("nle-accent", tool == TimelineTool.SelectForward || tool == TimelineTool.SelectBackward);
        ToolABtn.Classes.Set("nle-btn", tool != TimelineTool.SelectForward && tool != TimelineTool.SelectBackward);

        ToolHBtn.Classes.Set("nle-accent", tool == TimelineTool.Hand);
        ToolHBtn.Classes.Set("nle-btn", tool != TimelineTool.Hand);

        ToolZBtn.Classes.Set("nle-accent", tool == TimelineTool.Zoom);
        ToolZBtn.Classes.Set("nle-btn", tool != TimelineTool.Zoom);

        ToolSamBtn.Classes.Set("nle-accent", tool == TimelineTool.Sam);
        ToolSamBtn.Classes.Set("nle-btn", tool != TimelineTool.Sam);
        ToolTextBtn.Classes.Set("nle-accent", tool == TimelineTool.Text);
        ToolTextBtn.Classes.Set("nle-btn", tool != TimelineTool.Text);
    }

    private void SetupRuler()
    {
        RulerCanvas.PointerPressed += Ruler_PointerPressed;
        RulerCanvas.PointerMoved += Ruler_PointerMoved;
        RulerCanvas.PointerReleased += Ruler_PointerReleased;
        RulerCanvas.PointerCaptureLost += (_, _) => { _isScrubbingRuler = false; _draggingMarkerId = null; UpdateRulerVisuals(); };
        RulerCanvas.PointerExited += (_, _) => UpdateRulerMouseGuide(null);
        RulerCanvas.SizeChanged += (_, _) =>
        {
            UpdateRulerVisuals();
            UpdatePlayheadUI(TimelineCanvas.PlayheadSeconds);
        };
    }

    private void Ruler_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(RulerCanvas);
        if (e.GetCurrentPoint(RulerCanvas).Properties.IsRightButtonPressed)
        {
            ShowRulerContext(pos);
            e.Handled = true;
            return;
        }
        if (e.GetCurrentPoint(RulerCanvas).Properties.IsLeftButtonPressed)
        {
            if (MarkerAt(pos.X) is { } marker)
            {
                if (e.ClickCount > 1)
                {
                    _ = RenameMarkerAsync(marker.Id);
                    e.Handled = true;
                    return;
                }
                _draggingMarkerId = marker.Id;
                _markerDragTime = marker.Time.Seconds;
                _markerDragStartX = pos.X;
                e.Pointer.Capture(RulerCanvas);
                e.Handled = true;
                return;
            }
            _isScrubbingRuler = true;
            e.Pointer.Capture(RulerCanvas);
            ScrubToPosition(pos.X);
            e.Handled = true;
        }
    }

    private void Ruler_PointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(RulerCanvas);
        if (_draggingMarkerId != null)
        {
            _markerDragTime = SnapMarkerTime(TimelineCanvas.PixelsToTime(pos.X), _draggingMarkerId);
            UpdateRulerVisuals();
            e.Handled = true;
        }
        else if (_isScrubbingRuler)
        {
            ScrubToPosition(pos.X);
            e.Handled = true;
        }
        else
        {
            UpdateRulerMouseGuide(pos.X);
        }
    }

    private void Ruler_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingMarkerId is { } id)
        {
            var time = _markerDragTime;
            var moved = Math.Abs(e.GetPosition(RulerCanvas).X - _markerDragStartX) > 3;
            _draggingMarkerId = null;
            e.Pointer.Capture(null);
            if (moved) _controller?.UndoRedo.Commit(p => MarkerOperations.Move(p.ActiveSequence!, id, time), "Mover marcador");
            else { _transport?.Pause(); _transport?.Seek(time); }
            UpdateRulerVisuals();
            e.Handled = true;
            return;
        }
        if (_isScrubbingRuler)
        {
            _isScrubbingRuler = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void UpdateRulerMouseGuide(double? x)
    {
        if (_rulerMouseGuideLine == null)
        {
            _rulerMouseGuideLine = new Line
            {
                Stroke = new SolidColorBrush(Color.Parse("#77FFA126")),
                StrokeThickness = 1,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 2, 2 },
                IsHitTestVisible = false
            };
            RulerCanvas.Children.Add(_rulerMouseGuideLine);
        }

        if (x.HasValue && x.Value >= 0 && x.Value <= RulerCanvas.Bounds.Width)
        {
            _rulerMouseGuideLine.StartPoint = new Point(x.Value, 0);
            _rulerMouseGuideLine.EndPoint = new Point(x.Value, RulerCanvas.Bounds.Height);
            _rulerMouseGuideLine.IsVisible = true;
        }
        else
        {
            _rulerMouseGuideLine.IsVisible = false;
        }
    }

    private void ScrubToPosition(double x)
    {
        double time = TimelineCanvas.PixelsToTime(x);
        TimelineCanvas.PlayheadSeconds = time;
        OverviewScrollBar.InvalidateVisual();
    }

    private void OnPlayheadMoved(double time)
    {
        if (!_applyingTransportPosition)
        {
            _transport?.Pause();
            _transport?.Seek(time);
        }
        UpdatePlayheadUI(time);
    }

    private void UpdatePlayheadUI(double time)
    {
        double x = TimelineCanvas.TimeToPixels(time);
        Canvas.SetLeft(PlayheadHead, x - 6);
        PlayheadLine.IsVisible = TimelineCanvas.Sequence != null;
        if (PlayheadLine.RenderTransform is TranslateTransform line)
            line.X = x - 1;
        if (!_editingTimecode)
            TimecodeDisplay.Text = FormatTimecode(time, _transport?.FrameRate ?? 60);
    }

    private void UpdateRulerVisuals()
    {
        foreach (var lbl in _rulerLabels)
            RulerCanvas.Children.Remove(lbl);
        foreach (var tick in _rulerTicks)
            RulerCanvas.Children.Remove(tick);
        foreach (var mark in _rulerMarks)
            RulerCanvas.Children.Remove(mark);

        _rulerLabels.Clear();
        _rulerTicks.Clear();
        _rulerMarks.Clear();

        double width = RulerCanvas.Bounds.Width;
        if (width <= 0) width = 1200;

        double stepSeconds = TimelineRulerStep.ForZoom(TimelineCanvas.ZoomPixelsPerSecond);

        double firstTime = Math.Floor(TimelineCanvas.HorizontalOffsetSeconds / stepSeconds) * stepSeconds;
        double maxTime = TimelineCanvas.PixelsToTime(width);

        var tickBrush = new SolidColorBrush(Color.Parse("#444444"));
        var textBrush = new SolidColorBrush(Color.Parse("#888888"));

        for (double t = firstTime; t <= maxTime && t < firstTime + stepSeconds * 300; t += stepSeconds)
        {
            double x = TimelineCanvas.TimeToPixels(t);
            if (x < 0 || x > width) continue;

            var tickLine = new Line
            {
                StartPoint = new Point(x, 14),
                EndPoint = new Point(x, 22),
                Stroke = tickBrush,
                StrokeThickness = 1
            };
            RulerCanvas.Children.Add(tickLine);
            _rulerTicks.Add(tickLine);

            var label = new TextBlock
            {
                Text = FormatTimecode(t),
                FontSize = 9,
                Foreground = textBrush
            };
            Canvas.SetLeft(label, x + 4);
            Canvas.SetTop(label, 2);
            RulerCanvas.Children.Add(label);
            _rulerLabels.Add(label);
        }

        DrawRulerMarks();
        PlayheadHead.ZIndex = 100;
    }

    private void OnSelectionChanged()
    {
        var selected = TimelineCanvas.Selection.SelectedClipIds;
        var focused = TimelineCanvas.FocusedClipId;
        var id = focused != null && selected.Contains(focused) ? focused :
            _controller?.CurrentProject.ActiveSequence?.Clips.FirstOrDefault(c => selected.Contains(c.Id) &&
                _controller.CurrentProject.ActiveSequence.Tracks.Any(t => t.Id == c.TrackId && t.Kind == "video"))?.Id
            ?? selected.FirstOrDefault();
        _controller?.SetSelectedClip(id);
    }

    private void Timeline_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key == Key.Escape && _pendingShelfInsertion != null)
        { CancelShelfPlacement(); _controller?.ReportStatus("Inserción desde la bandeja cancelada"); e.Handled = true; return; }
        if (SpaceShortcutPolicy.IsTextInput(e.Source) || e.Source is ComboBox)
            return;
        if (_controller?.SelectedTransitionId is { } transitionId && e.Key == Key.Delete)
        {
            RemoveTransition(transitionId);
            e.Handled = true;
            return;
        }
        if (e.Source is NestedInlineEditorControl nested && e.Key is Key.Delete or Key.Back)
        {
            nested.DeleteSelectedChild();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && TimelineCanvas.CancelGesture()) { e.Handled = true; return; }
        if (TryHandleTimelineShortcut(e)) e.Handled = true;
    }

    public void BeginShelfPlacement(string[] itemIds, double sourceIn, double? sourceOut)
    {
        if (itemIds.Length == 0 || _controller?.CurrentProject.ActiveSequence is not { } sequence) return;
        _pendingShelfInsertion = (itemIds, sourceIn, sourceOut, _controller.CurrentProject.Id, sequence.Id);
        TimelineCanvas.ExternalPointClick = at =>
        {
            var pending = _pendingShelfInsertion;
            CancelShelfPlacement();
            if (pending == null || _controller.CurrentProject.Id != pending.Value.ProjectId ||
                _controller.CurrentProject.ActiveSequence?.Id != pending.Value.SequenceId) return true;
            _controller.ReportStatus("Colocando referencias en la posición elegida…");
            _ = InsertShelfAtPointAsync(pending.Value.ItemIds, at, pending.Value.SourceIn, pending.Value.SourceOut);
            return true;
        };
        _controller.ReportStatus("Haz clic en un punto de la línea de tiempo · Escape cancela");
    }

    private async System.Threading.Tasks.Task InsertShelfAtPointAsync(string[] itemIds, double at, double sourceIn, double? sourceOut)
    {
        try
        {
            if (itemIds.Length == 1) await LinkShelfService.InsertAtAsync(_controller!, itemIds[0], at, sourceIn, sourceOut);
            else await LinkShelfService.InsertBatchAtAsync(_controller!, itemIds, at);
        }
        catch (Exception ex) { _controller?.ReportStatus("No se pudieron colocar las referencias: " + ex.Message); }
    }

    private void CancelShelfPlacement()
    {
        _pendingShelfInsertion = null;
        TimelineCanvas.ExternalPointClick = null;
    }

    public static string FormatTimecode(double seconds, double fps = 60.0)
    {
        long totalFrames = (long)Math.Round(Math.Max(0, seconds) * fps);
        long fpsLong = (long)Math.Round(fps);

        long hours = totalFrames / (3600 * fpsLong);
        long remFrames = totalFrames % (3600 * fpsLong);
        long minutes = remFrames / (60 * fpsLong);
        remFrames %= (60 * fpsLong);
        long secs = remFrames / fpsLong;
        long frames = remFrames % fpsLong;

        return $"{hours:D2}:{minutes:D2}:{secs:D2}:{frames:D2}";
    }
}
