using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class MonitorPanelView : UserControl
{
    private bool _busy;
    private bool _compositeIncomplete;
    private double _requestedTime;
    private double _displayedTime;
    private Bitmap? _image;
    private int _nestedWarmupRetries;
    private readonly PlaybackTransport? _transport;
    private bool _attached;
    private TopLevel? _anchorInputRoot;
    private bool _fitView = true;
    private bool _updatingZoomControl;
    private double _viewScale = 1;
    private Vector _viewPan;
    private IPointer? _panPointer;
    private Point _panStart;
    private Vector _panOrigin;
    private long _nextPlaybackRefreshTimestamp;
    private bool _qualityRefreshPending;
    private bool _refreshQueued;
    private bool _refreshAgain;
    private bool _warmupRetryQueued;
    private bool _releaseWhenIdle;
    private int PreviewLongEdge => PreviewUserSettings.QualityIndex switch { 0 => 320, 1 => 640, _ => 0 };
    private int PreviewFps => PreviewUserSettings.RefreshRates[PreviewUserSettings.RefreshIndex];
    private bool UsesDynamicPreview => PreviewUserSettings.QualityIndex == PreviewUserSettings.DynamicQualityIndex;
    private readonly Stopwatch _previewClock = Stopwatch.StartNew();
    private (string? SequenceId, int Width, int Height, double Fps) _previewCanvas;
    private int _previewRevision;

    public MonitorPanelView()
    {
        InitializeComponent();
        SetupMonitorContextMenu();
        var controller = ProjectController.Instance;
        _transport = controller?.Transport;
        if (controller != null)
        {
            controller.StateChanged += () => { if (_attached) ShowTime(_transport?.PositionSeconds ?? _requestedTime); };
            controller.ClipSelectionChanged += () =>
            {
                if (!_attached) return;
                UpdateGizmo();
                ClearSamMaskOverlay();
                ScheduleSamPreparation();
                UpdateSamRemoveButton();
            };
            controller.MaskSelectionChanged += () =>
            {
                if (!_attached) return;
                ClearSamMaskOverlay();
                UpdateSamRemoveButton();
            };
        }
        if (_transport != null)
        {
            _transport.PositionChanged += OnTransportPositionChanged;
            _transport.PlayingChanged += playing =>
            {
                _nextPlaybackRefreshTimestamp = 0;
                ResetDynamicPreview();
                if (playing) CancelSamPreparation();
                else if (_attached) ShowTime(_transport.PositionSeconds);
            };
        }
        MonitorFitButton.Click += (_, _) => FitView();
        CaptureFrameButton.Click += (_, _) => ShowCaptureButtonMenu();
        MonitorZoomSlider.ValueChanged += (_, e) =>
        {
            if (!_updatingZoomControl) SetZoom(e.NewValue / 100,
                new Point(ViewportHost.Bounds.Width / 2, ViewportHost.Bounds.Height / 2));
        };
        ViewportHost.PointerWheelChanged += OnViewportWheel;
        ViewportHost.PointerPressed += OnViewportPressed;
        ViewportHost.AddHandler(PointerPressedEvent, OnChromaPickPressed, RoutingStrategies.Tunnel, true);
        SetupTextPlacement();
        SetupSamSelection();
        SetupShapeSelection();
        ViewportHost.PointerMoved += OnViewportMoved;
        ViewportHost.PointerReleased += OnViewportReleased;
        ViewportHost.PointerCaptureLost += (_, _) => _panPointer = null;
        TransformGizmoToggle.IsCheckedChanged += (_, _) =>
        {
            if (TransformGizmoToggle.IsChecked != true && AnchorGizmoToggle.IsChecked != true) CancelGizmoGesture();
            UpdateGizmo();
        };
        AnchorGizmoToggle.IsCheckedChanged += (_, _) =>
        {
            if (TransformGizmoToggle.IsChecked != true && AnchorGizmoToggle.IsChecked != true) CancelGizmoGesture();
            UpdateGizmo();
        };
        ViewportHost.PointerMoved += (_, e) =>
        {
            var show = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (_temporaryAnchorVisible == show) return;
            _temporaryAnchorVisible = show;
            UpdateGizmo();
        };
        ViewportHost.PointerExited += (_, _) => { _temporaryAnchorVisible = false; UpdateGizmo(); };
        TransformOverlay.PointerPressed += OnGizmoPressed;
        TransformOverlay.PointerMoved += OnGizmoMoved;
        TransformOverlay.PointerReleased += OnGizmoReleased;
        TransformOverlay.PointerCaptureLost += (_, _) => CancelGizmoGesture();
        SequenceFrame.SizeChanged += (_, _) => UpdateGizmo();
        ViewportHost.SizeChanged += (_, _) => FitSequenceFrame();
        NestedLayerHost.SizeChanged += (_, _) => { if (_attached) ShowTime(_requestedTime); };
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            PreviewUserSettings.Changed += SyncPreviewSettings;
            ChromaColorPicker.StateChanged += UpdateChromaPickState;
            UpdateChromaPickState();
            _anchorInputRoot = TopLevel.GetTopLevel(this);
            _anchorInputRoot?.AddHandler(KeyDownEvent, OnAnchorKeyDown, RoutingStrategies.Tunnel, true);
            _anchorInputRoot?.AddHandler(KeyUpEvent, OnAnchorKeyUp, RoutingStrategies.Tunnel, true);
            OnTransportPositionChanged(_transport?.PositionSeconds ?? _requestedTime);
            if (SamToolEnabled) OnSamToolChanged();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            PreviewUserSettings.Changed -= SyncPreviewSettings;
            ResetDynamicPreview();
            CancelGizmoGesture();
            ChromaColorPicker.StateChanged -= UpdateChromaPickState;
            _anchorInputRoot?.RemoveHandler(KeyDownEvent, OnAnchorKeyDown);
            _anchorInputRoot?.RemoveHandler(KeyUpEvent, OnAnchorKeyUp);
            _anchorInputRoot = null;
            if (_busy || _gizmoGroupRendering) _releaseWhenIdle = true;
            else ReleaseNestedLayers();
            StopSamSelection();
            CancelTextPlacement();
        };
    }

    private void OnAnchorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _gizmoPointer != null)
        {
            CancelGizmoGesture();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && ChromaColorPicker.IsPending)
        {
            ChromaColorPicker.Cancel();
            ProjectController.Instance?.ReportStatus("Gotero cancelado");
            e.Handled = true;
            return;
        }
        if (ShapeToolEnabled && e.Key == Key.Escape)
        {
            ClearSamMaskOverlay();
            e.Handled = true;
            return;
        }
        if (TextToolEnabled && e.Key == Key.Escape && (_textPointer != null || e.Source is not TextBox))
        {
            if (_textPointer != null) CancelTextPlacement();
            else if (ProjectController.Instance is { } controller) controller.TextToolEnabled = false;
            e.Handled = true;
            return;
        }
        if (ShapeToolEnabled && e.Key == Key.Enter && ShapeToolKind == "polygon")
        {
            CompleteShapePolygon();
            e.Handled = true;
            return;
        }
        if (!ViewportHost.IsPointerOver || !e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
            _temporaryAnchorVisible) return;
        _temporaryAnchorVisible = true;
        UpdateGizmo();
    }

    private void UpdateChromaPickState()
    {
        ChromaPickHint.IsVisible = ChromaColorPicker.IsPending;
        ViewportHost.Cursor = ChromaColorPicker.IsPending || TextToolEnabled ? new Cursor(StandardCursorType.Cross) : null;
    }

    private void OnAnchorKeyUp(object? sender, KeyEventArgs e)
    {
        if (!_temporaryAnchorVisible || e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            e.Key is not Key.LeftCtrl and not Key.RightCtrl) return;
        _temporaryAnchorVisible = false;
        UpdateGizmo();
    }

    private void SyncPreviewSettings()
    {
        ResetDynamicPreview();
        _previewLoad.Observe(false, false, TimeSpan.Zero, PreviewFps);
        _nextPlaybackRefreshTimestamp = 0;
        UpdateDynamicResolutionLabel();
        if (_busy) { _qualityRefreshPending = true; return; }
        if (_attached) ShowTime(_transport?.PositionSeconds ?? _requestedTime);
    }

    public void ShowTime(double timelineSeconds)
    {
        if (Math.Abs(_requestedTime - timelineSeconds) > 1.0 / 120.0)
        {
            _nestedWarmupRetries = 0;
            ClearSamMaskOverlay();
        }
        _requestedTime = timelineSeconds;
        if (_transport?.IsPlaying != true) FitSequenceFrame();
        if (_gizmoPointer != null) UpdateGizmo();
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (!_attached) return;
        if (_busy || _gizmoGroupRendering) { _refreshAgain = true; return; }
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            if (_attached && !_busy && !_gizmoGroupRendering) _ = RefreshAsync();
            else if (_attached) _refreshAgain = true;
        }, DispatcherPriority.Input);
    }

    private void FitSequenceFrame()
    {
        var sequence = ProjectController.Instance?.CurrentProject.ActiveSequence;
        if (sequence == null) return;
        var viewportWidth = ViewportHost.Bounds.Width;
        var viewportHeight = ViewportHost.Bounds.Height;
        var rect = _fitView
            ? PreviewGeometry.FitCanvas(sequence.Width, sequence.Height, viewportWidth, viewportHeight)
            : PreviewGeometry.ZoomCanvas(sequence.Width, sequence.Height, viewportWidth, viewportHeight,
                _viewScale, _viewPan.X, _viewPan.Y);
        if (rect.Width <= 0 || rect.Height <= 0) return;
        if (double.IsNaN(SequenceFrame.Width) || Math.Abs(SequenceFrame.Width - rect.Width) > 0.01) SequenceFrame.Width = rect.Width;
        if (double.IsNaN(SequenceFrame.Height) || Math.Abs(SequenceFrame.Height - rect.Height) > 0.01) SequenceFrame.Height = rect.Height;
        var margin = new Thickness(rect.X, rect.Y, 0, 0);
        if (SequenceFrame.Margin != margin) SequenceFrame.Margin = margin;
        var percentage = _fitView ? rect.Width / sequence.Width * 100 : _viewScale * 100;
        MonitorZoomValue.Text = _fitView ? $"Fit ({percentage:0}%)" : $"{percentage:0}%";
        _updatingZoomControl = true;
        MonitorZoomSlider.Value = Math.Clamp(percentage, MonitorZoomSlider.Minimum, MonitorZoomSlider.Maximum);
        _updatingZoomControl = false;
    }

    private void FitView()
    {
        _fitView = true;
        _viewPan = default;
        FitSequenceFrame();
    }

    private void SetZoom(double scale, Point pivot)
    {
        var sequence = ProjectController.Instance?.CurrentProject.ActiveSequence;
        if (sequence == null || ViewportHost.Bounds.Width <= 0 || ViewportHost.Bounds.Height <= 0) return;
        var previous = new PreviewRectangle(SequenceFrame.Margin.Left, SequenceFrame.Margin.Top,
            SequenceFrame.Width, SequenceFrame.Height);
        if (previous.Width <= 0 || previous.Height <= 0) return;
        _viewScale = Math.Clamp(scale, 0.1, 4);
        _fitView = false;
        var pan = PreviewGeometry.PanForZoom(previous, sequence.Width, sequence.Height,
            ViewportHost.Bounds.Width, ViewportHost.Bounds.Height, _viewScale, pivot.X, pivot.Y);
        _viewPan = new Vector(pan.X, pan.Y);
        FitSequenceFrame();
    }

    private void OnViewportWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        var sequence = ProjectController.Instance?.CurrentProject.ActiveSequence;
        if (sequence == null) return;
        var current = _fitView && sequence.Width > 0 ? SequenceFrame.Width / sequence.Width : _viewScale;
        SetZoom(current * (e.Delta.Y > 0 ? 1.12 : 1 / 1.12), e.GetPosition(ViewportHost));
        e.Handled = true;
    }

    private void OnViewportPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(ViewportHost).Properties.IsMiddleButtonPressed) return;
        if (_fitView)
        {
            var sequence = ProjectController.Instance?.CurrentProject.ActiveSequence;
            if (sequence == null || sequence.Width <= 0) return;
            _viewScale = SequenceFrame.Width / sequence.Width;
            _fitView = false;
        }
        _panPointer = e.Pointer;
        _panStart = e.GetPosition(ViewportHost);
        _panOrigin = _viewPan;
        e.Pointer.Capture(ViewportHost);
        e.Handled = true;
    }

    private void OnChromaPickPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ChromaColorPicker.IsPending) return;
        if (e.GetCurrentPoint(ViewportHost).Properties.IsRightButtonPressed)
        {
            ChromaColorPicker.Cancel();
            ProjectController.Instance?.ReportStatus("Gotero cancelado");
            e.Handled = true;
            return;
        }
        if (!e.GetCurrentPoint(ViewportHost).Properties.IsLeftButtonPressed) return;
        if (ChromaColorPicker.SampleComposite)
        {
            var compositePoint = e.GetPosition(SequenceFrame);
            if (_compositeIncomplete || compositePoint.X < 0 || compositePoint.Y < 0 ||
                compositePoint.X >= SequenceFrame.Bounds.Width || compositePoint.Y >= SequenceFrame.Bounds.Height)
            {
                ProjectController.Instance?.ReportStatus("Elige un punto del fotograma visible");
                e.Handled = true;
                return;
            }
            try
            {
                var topLevel = TopLevel.GetTopLevel(this)
                    ?? throw new InvalidOperationException("No hay monitor visible.");
                var screen = topLevel.PointToScreen(e.GetPosition(topLevel));
                ChromaColorPicker.Complete(ScreenColorSampler.Read(screen.X, screen.Y,
                    e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 2 : 0));
                ProjectController.Instance?.ReportStatus("Color tomado de la previsualización");
            }
            catch (Exception ex) { ProjectController.Instance?.ReportStatus("Gotero: " + ex.Message); }
            e.Handled = true;
            return;
        }
        var clipId = ChromaColorPicker.ClipId;
        var entry = _nestedLayers.LastOrDefault(layer => layer.ClipId == clipId && layer.Gpu != null);
        var project = ProjectController.Instance?.CurrentProject;
        var layer = project == null ? null : SubtitleRenderPlan.At(project, _displayedTime, "video")
            .FirstOrDefault(item => item.Clip.Id == clipId);
        if (entry?.Gpu == null || layer == null || _compositeIncomplete ||
            Math.Abs(_displayedTime - (_transport?.PositionSeconds ?? _requestedTime)) > 0.1)
        {
            ProjectController.Instance?.ReportStatus("Espera a que el fotograma del medio esté listo");
            e.Handled = true;
            return;
        }
        var state = DynamicMotionEffect.Evaluate(layer.Clip, layer.LocalTimelineSeconds);
        var rect = PreviewGeometry.PlaceLayer(SequenceFrame.Width, SequenceFrame.Height,
            entry.Gpu.Width, entry.Gpu.Height, state);
        var point = e.GetPosition(SequenceFrame);
        var source = PreviewGeometry.MapDisplayPointToSource(rect, state, point.X, point.Y);
        if (source == null)
        {
            ProjectController.Instance?.ReportStatus("Elige un punto dentro del medio seleccionado");
            e.Handled = true;
            return;
        }
        try
        {
            var color = entry.Gpu.ReadSourcePixel((int)(source.Value.X * entry.Gpu.SourceWidth),
                (int)(source.Value.Y * entry.Gpu.SourceHeight),
                e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 5 : 1);
            ChromaColorPicker.Complete(Avalonia.Media.Color.FromRgb(color.Red, color.Green, color.Blue));
            ProjectController.Instance?.ReportStatus("Color tomado del fotograma original");
        }
        catch (Exception ex) { ProjectController.Instance?.ReportStatus("Gotero: " + ex.Message); }
        e.Handled = true;
    }

    private void OnViewportMoved(object? sender, PointerEventArgs e)
    {
        if (_panPointer != e.Pointer) return;
        var position = e.GetPosition(ViewportHost);
        _viewPan = _panOrigin + (position - _panStart);
        FitSequenceFrame();
        e.Handled = true;
    }

    private void OnViewportReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_panPointer != e.Pointer || e.InitialPressMouseButton != MouseButton.Middle) return;
        _panPointer = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnTransportPositionChanged(double time)
    {
        if (!_attached) return;
        if (_transport?.IsPlaying == true)
        {
            var now = Stopwatch.GetTimestamp();
            var interval = Stopwatch.Frequency / PreviewFps;
            if (now + Stopwatch.Frequency / 1000 < _nextPlaybackRefreshTimestamp)
            {
                _requestedTime = time;
                return;
            }
            _nextPlaybackRefreshTimestamp = _nextPlaybackRefreshTimestamp == 0
                ? now + interval : Math.Max(now, _nextPlaybackRefreshTimestamp + interval);
        }
        ShowTime(time);
    }

    private async Task RefreshAsync()
    {
        _busy = true;
        _refreshAgain = false;
        var time = _requestedTime;
        var project = ProjectController.Instance?.CurrentProject;
        var sequence = project?.ActiveSequence;
        var measurement = _transport?.IsPlaying == true ? _transport.Diagnostics.Current : null;
        var completed = false;
        try
        {
            if (_layerProjectId != project?.Id)
            {
                ReleaseNestedLayers();
                _layerProjectId = project?.Id;
            }
            var canvas = (sequence?.Id, sequence?.Width ?? 0, sequence?.Height ?? 0, sequence?.Fps.Seconds ?? 0);
            if (_previewCanvas != canvas)
            {
                _previewCanvas = canvas;
                ResetDynamicPreview();
            }
            var previewRevision = _previewRevision;
            var renderStarted = Stopwatch.GetTimestamp();
            NestedLayerHost.IsVisible = true;
            SyncPrewarmPosition(project, time);
            var layers = project == null ? Array.Empty<NestedRenderLayer>() : SubtitleRenderPlan.At(project, time, "video");
            if (layers.Count > 0)
            {
                if (!await ShowNestedLayersAsync(project!, layers))
                {
                    measurement?.Count("not_ready");
                    _compositeIncomplete = true;
                    if (!_warmupRetryQueued && _nestedWarmupRetries++ < (_transport?.IsPlaying == true ? 8 : 75))
                        _ = RetryNestedWarmupAsync();
                    return;
                }
                measurement?.Present(time, sequence!.Fps.Seconds, PreviewLongEdge, PreviewFps,
                    _groupLayer?.Gpu.Width ?? 0, _groupLayer?.Gpu.Height ?? 0,
                    _groupLayer?.Gpu.LastCompositionWork ?? TimeSpan.Zero);
                if (UsesDynamicPreview && _transport?.IsPlaying == true && _nestedLayers.Count > 0 &&
                    previewRevision == _previewRevision)
                    ObserveDynamicPreview(renderStarted, sequence!.Fps.Seconds);
                _nestedWarmupRetries = 0;
                PreviewImage.IsVisible = false;
                SetImage(null, null);
            }
            else
            {
                _previewLoad.Observe(false, false, TimeSpan.Zero, PreviewFps);
                _dynamicPreview.ResetEvidence();
                ReleaseActiveLayers(retainSources: true);
                PreviewImage.IsVisible = true;
                SetImage(null, "Sin imagen en esta posición");
            }
            if (project != null) UpdatePrewarm(project, time, layers);
            _compositeIncomplete = false;
            _displayedTime = time;
            completed = true;
        }
        catch (Exception error)
        {
            _compositeIncomplete = true;
            if (project != null)
                ProjectController.Instance?.ReportPlaybackFailure(project.Id, "monitor", error.Message);
        }
        finally
        {
            _busy = false;
            if (_releaseWhenIdle)
            {
                _releaseWhenIdle = false;
                ReleaseNestedLayers();
                if (_attached) QueueRefresh();
            }
            if (_transport?.IsPlaying != true || _gizmoPointer != null || _temporaryAnchorVisible ||
                TransformGizmoToggle.IsChecked == true || AnchorGizmoToggle.IsChecked == true)
                UpdateGizmo();
            if (_gizmoGroupPending && _gizmoPointer != null) _ = RefreshGizmoGroupAsync();
            if (_qualityRefreshPending)
            {
                _qualityRefreshPending = false;
                if (_attached) ShowTime(_transport?.PositionSeconds ?? _requestedTime);
            }
            if (completed && Math.Abs(_requestedTime - time) >= 0.25 / Math.Max(1, sequence?.Fps.Seconds ?? 60))
                QueueRefresh();
            if (_refreshAgain) QueueRefresh();
            if (_transport?.IsPlaying != true || SamToolEnabled)
            {
                ScheduleSamPreparation();
                UpdateSamRemoveButton();
            }
        }
    }

    private async Task RetryNestedWarmupAsync()
    {
        _warmupRetryQueued = true;
        await Task.Delay(_transport?.IsPlaying == true ? 100 : 32);
        _warmupRetryQueued = false;
        if (_attached && _compositeIncomplete) QueueRefresh();
    }

    private void SetImage(Bitmap? bitmap, string? message)
    {
        PreviewImage.Source = bitmap;
        PreviewMessage.Text = message ?? "";
        _image?.Dispose();
        _image = bitmap;
    }

    private static async Task<Bitmap> DecodeFrameAsync(string path, double seconds)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        }};
        foreach (var arg in new[] { "-v", "error", "-ss", seconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                     "-i", path, "-frames:v", "1", "-vf", "scale=640:-2", "-f", "image2pipe", "-vcodec", "mjpeg", "pipe:1" })
            process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        using var buffer = new MemoryStream();
        var readError = process.StandardError.ReadToEndAsync();
        await process.StandardOutput.BaseStream.CopyToAsync(buffer);
        await process.WaitForExitAsync();
        var error = await readError;
        if (process.ExitCode != 0 || buffer.Length == 0) throw new InvalidOperationException(error.Trim());
        buffer.Position = 0;
        return new Bitmap(buffer);
    }
}
