using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class MonitorPanelView
{
    private readonly List<ShapeMaskPoint> _shapePoints = new();
    private bool _shapeDragging;
    private bool _showingExistingShape;
    private int _shapeDragIndex = 1;
    private string? _editingShapeLayerId;
    private string? _outlineShapeId;
    private string? ShapeToolKind => ProjectController.Instance?.ShapeMaskTool;
    private bool ShapeToolEnabled => ShapeToolKind != null;
    private static readonly IBrush ShapeStroke = new SolidColorBrush(Color.Parse("#FFAC45"));

    private void SetupShapeSelection()
    {
        if (ProjectController.Instance is { } controller)
            controller.ShapeMaskToolChanged += OnShapeToolChanged;
        ViewportHost.AddHandler(PointerPressedEvent, OnShapePressed,
            RoutingStrategies.Tunnel, true);
        ViewportHost.AddHandler(PointerMovedEvent, OnShapeMoved,
            RoutingStrategies.Tunnel, true);
        ViewportHost.AddHandler(PointerReleasedEvent, OnShapeReleased,
            RoutingStrategies.Tunnel, true);
    }

    private void OnShapeToolChanged()
    {
        ClearSamMaskOverlay();
        if (!ShapeToolEnabled || !_attached) return;
        RefreshShapeOutline();
        ProjectController.Instance?.ReportStatus(ShapeToolKind == "polygon"
            ? "Pluma: clic añade puntos; doble clic o Enter cierra; clic derecho deshace un punto"
            : "Máscara de forma: arrastra sobre el medio y pulsa Aplicar máscara");
    }

    private (NestedRenderLayer Layer, GpuVideoSurface Gpu, string Key,
        PreviewRectangle Rect, ClipVisualState State, ShapeMaskPoint Point)? ShapeSource(PointerEventArgs e)
    {
        var visible = VisibleSamSource();
        if (visible == null) return null;
        var state = DynamicMotionEffect.Evaluate(visible.Value.Layer.Clip,
            visible.Value.Layer.LocalTimelineSeconds);
        var rect = PreviewGeometry.PlaceLayer(SequenceFrame.Width, SequenceFrame.Height,
            visible.Value.Gpu.Width, visible.Value.Gpu.Height, state);
        var pointer = e.GetPosition(SequenceFrame);
        var mapped = PreviewGeometry.MapDisplayPointToSource(rect, state, pointer.X, pointer.Y);
        return mapped == null ? null : (visible.Value.Layer, visible.Value.Gpu,
            visible.Value.Key, rect, state, new ShapeMaskPoint(mapped.Value.X, mapped.Value.Y));
    }

    private void OnShapePressed(object? sender, PointerPressedEventArgs e)
    {
        var kind = ShapeToolKind;
        if (kind == null) return;
        var properties = e.GetCurrentPoint(ViewportHost).Properties;
        if (!properties.IsLeftButtonPressed && !properties.IsRightButtonPressed) return;
        e.Handled = true;
        if (_transport?.IsPlaying == true)
        {
            ProjectController.Instance?.ReportStatus("Pausa el montaje para dibujar la máscara");
            return;
        }
        if (kind == "polygon" && properties.IsRightButtonPressed)
        {
            if (_shapePoints.Count > 0) _shapePoints.RemoveAt(_shapePoints.Count - 1);
            var current = ShapeSource(e);
            if (current != null) DrawShapeDraft(current.Value.Rect, current.Value.State);
            return;
        }
        if (!properties.IsLeftButtonPressed) return;
        var source = ShapeSource(e);
        if (source == null)
        {
            ProjectController.Instance?.ReportStatus("Selecciona un clip y dibuja dentro de su imagen");
            return;
        }
        if (_pendingShapeMask != null) ClearSamMaskOverlay();
        if (_showingExistingShape)
        {
            var pointer = e.GetPosition(SequenceFrame);
            var hit = _shapePoints.FindIndex(point =>
            {
                var display = ShapeDisplayPoint(point, source.Value.Rect, source.Value.State);
                return Math.Abs(display.X - pointer.X) <= 10 &&
                    Math.Abs(display.Y - pointer.Y) <= 10;
            });
            if (hit >= 0)
            {
                _editingShapeLayerId = ClipMaskStack.Read(source.Value.Layer.Clip)
                    .FirstOrDefault(layer => layer.Id == ProjectController.Instance?.SelectedMaskId &&
                        layer.Kind == "shape")?.Id ??
                    ClipMaskStack.Read(source.Value.Layer.Clip)
                        .LastOrDefault(layer => layer.Kind == "shape")?.Id;
                _showingExistingShape = false;
                _shapeDragging = true;
                _shapeDragIndex = hit;
                e.Pointer.Capture(ViewportHost);
                return;
            }
            _shapePoints.Clear();
            _showingExistingShape = false;
            _editingShapeLayerId = null;
        }
        if (kind == "polygon")
        {
            if (_shapePoints.Count == 64)
            {
                CompleteShapePolygon();
                return;
            }
            if (_shapePoints.Count == 0 || Distance(_shapePoints[^1], source.Value.Point) > 0.003)
                _shapePoints.Add(source.Value.Point);
            DrawShapeDraft(source.Value.Rect, source.Value.State);
            if (e.ClickCount >= 2) CompleteShapePolygon();
            return;
        }
        _shapePoints.Clear();
        _editingShapeLayerId = null;
        _outlineShapeId = null;
        _shapePoints.Add(source.Value.Point);
        _shapePoints.Add(source.Value.Point);
        _shapeDragging = true;
        _shapeDragIndex = 1;
        e.Pointer.Capture(ViewportHost);
        DrawShapeDraft(source.Value.Rect, source.Value.State);
    }

    private void OnShapeMoved(object? sender, PointerEventArgs e)
    {
        if (!_shapeDragging || _shapeDragIndex >= _shapePoints.Count) return;
        var source = ShapeSource(e);
        if (source == null) return;
        _shapePoints[_shapeDragIndex] = source.Value.Point;
        DrawShapeDraft(source.Value.Rect, source.Value.State);
        e.Handled = true;
    }

    private void OnShapeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_shapeDragging) return;
        _shapeDragging = false;
        e.Pointer.Capture(null);
        var source = ShapeSource(e);
        if (source == null || _shapeDragIndex >= _shapePoints.Count)
        {
            ClearShapeDraft();
            return;
        }
        _shapePoints[_shapeDragIndex] = source.Value.Point;
        CompleteShape(new ShapeMask { Kind = ShapeToolKind!,
            Points = _shapePoints.Select(p => new ShapeMaskPoint(p.X, p.Y)).ToList() },
            source.Value.Layer, source.Value.Gpu, source.Value.Key,
            source.Value.Rect, source.Value.State);
        e.Handled = true;
    }

    private void CompleteShapePolygon()
    {
        if (ShapeToolKind != "polygon" || _shapePoints.Count < 3)
        {
            ProjectController.Instance?.ReportStatus("Pluma: añade al menos tres puntos");
            return;
        }
        var source = VisibleSamSource();
        if (source == null) return;
        var state = DynamicMotionEffect.Evaluate(source.Value.Layer.Clip,
            source.Value.Layer.LocalTimelineSeconds);
        var rect = PreviewGeometry.PlaceLayer(SequenceFrame.Width, SequenceFrame.Height,
            source.Value.Gpu.Width, source.Value.Gpu.Height, state);
        CompleteShape(new ShapeMask { Kind = "polygon",
            Points = _shapePoints.Select(p => new ShapeMaskPoint(p.X, p.Y)).ToList() },
            source.Value.Layer, source.Value.Gpu, source.Value.Key, rect, state);
    }

    private void CompleteShape(ShapeMask shape, NestedRenderLayer layer, GpuVideoSurface gpu,
        string key, PreviewRectangle rect, ClipVisualState state)
    {
        try
        {
            var raster = ShapeMaskRaster.ForSurface(shape, gpu.Width, gpu.Height);
            ShowSamMask(new Sam2MaskFrame(gpu.Width, gpu.Height, raster.DecodeAlpha(), 1),
                rect, key, layer);
            _pendingShapeMask = shape;
            _showingExistingShape = false;
            SamMaskCanvas.IsVisible = false;
            _shapePoints.Clear();
            _shapePoints.AddRange(shape.Points);
            DrawShapeDraft(rect, state, close: true);
            ProjectController.Instance?.ReportStatus("Forma lista · pulsa Aplicar máscara para guardarla en todo el clip");
        }
        catch (Exception ex)
        {
            ClearShapeDraft();
            ProjectController.Instance?.ReportStatus("Máscara: " + ex.Message);
        }
    }

    private void DrawShapeDraft(PreviewRectangle rect, ClipVisualState state, bool close = false)
    {
        ShapeDraftCanvas.Children.Clear();
        ShapeDraftCanvas.IsVisible = _shapePoints.Count > 0;
        if (_shapePoints.Count == 0) return;
        var kind = _pendingShapeMask?.Kind ?? ShapeToolKind;
        var points = new List<ShapeMaskPoint>();
        if (kind == "ellipse" && _shapePoints.Count == 2)
        {
            var a = _shapePoints[0]; var b = _shapePoints[1];
            var cx = (a.X + b.X) / 2; var cy = (a.Y + b.Y) / 2;
            var rx = Math.Abs(a.X - b.X) / 2; var ry = Math.Abs(a.Y - b.Y) / 2;
            for (var i = 0; i < 48; i++)
            {
                var angle = 2 * Math.PI * i / 48;
                points.Add(new ShapeMaskPoint(cx + rx * Math.Cos(angle),
                    cy + ry * Math.Sin(angle)));
            }
            close = true;
        }
        else if (kind == "rectangle" && _shapePoints.Count == 2)
        {
            var a = _shapePoints[0]; var b = _shapePoints[1];
            points.AddRange([a, new ShapeMaskPoint(b.X, a.Y), b,
                new ShapeMaskPoint(a.X, b.Y)]);
            close = true;
        }
        else points.AddRange(_shapePoints);
        var display = new Points();
        foreach (var point in points)
        {
            display.Add(ShapeDisplayPoint(point, rect, state));
        }
        if (close && display.Count > 0) display.Add(display[0]);
        ShapeDraftCanvas.Children.Add(new Polyline { Points = display,
            Stroke = ShapeStroke, StrokeThickness = 2,
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 5, 3 } });
        foreach (var sourcePoint in _shapePoints)
        {
            var point = ShapeDisplayPoint(sourcePoint, rect, state);
            var handle = new Ellipse { Width = 7, Height = 7,
                Fill = ShapeStroke, Stroke = Brushes.Black, StrokeThickness = 1 };
            Canvas.SetLeft(handle, point.X - 3.5);
            Canvas.SetTop(handle, point.Y - 3.5);
            ShapeDraftCanvas.Children.Add(handle);
        }
    }

    private void ClearShapeDraft()
    {
        _shapeDragging = false;
        _showingExistingShape = false;
        _editingShapeLayerId = null;
        _outlineShapeId = null;
        _shapePoints.Clear();
        if (ShapeDraftCanvas == null) return;
        ShapeDraftCanvas.Children.Clear();
        ShapeDraftCanvas.IsVisible = false;
    }

    private static double Distance(ShapeMaskPoint a, ShapeMaskPoint b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static Point ShapeDisplayPoint(ShapeMaskPoint point, PreviewRectangle rect,
        ClipVisualState state)
    {
        var x = state.FlipX ? 1 - point.X : point.X;
        var y = state.FlipY ? 1 - point.Y : point.Y;
        var position = PreviewGeometry.RotateLayerPoint(rect, state, x, y);
        return new Point(position.X, position.Y);
    }

    private static double ShapeSourceSeconds(NestedRenderLayer layer)
    {
        var controller = ProjectController.Instance;
        var clip = layer.Clip;
        return controller != null && StillImageRules.IsStill(controller.CurrentProject, clip)
            ? clip.SourceIn.Seconds + Math.Max(0,
                layer.LocalTimelineSeconds - clip.TimelineStart.Seconds) * clip.Speed.Seconds
            : layer.SourceSeconds;
    }

    private void RefreshShapeOutline()
    {
        if (!ShapeToolEnabled || _pendingShapeMask != null || _shapeDragging ||
            (_shapePoints.Count > 0 && !_showingExistingShape)) return;
        var source = VisibleSamSource();
        var layer = source == null ? null : (ClipMaskStack.Read(source.Value.Layer.Clip)
            .FirstOrDefault(layer => layer.Id == ProjectController.Instance?.SelectedMaskId) ??
            ClipMaskStack.Read(source.Value.Layer.Clip).LastOrDefault());
        var shape = source == null || layer == null ? null : ShapeMaskAnimation.Evaluate(layer,
            ShapeSourceSeconds(source.Value.Layer),
            ProjectController.Instance?.CurrentProject.ActiveSequence?.Fps.Seconds ?? 60);
        if (source == null || shape == null || shape.Kind != ShapeToolKind)
        {
            if (_showingExistingShape) ClearShapeDraft();
            return;
        }
        if (!_showingExistingShape || _outlineShapeId != shape.Id)
        {
            _shapePoints.Clear();
            _shapePoints.AddRange(shape.Points.Select(p => new ShapeMaskPoint(p.X, p.Y)));
            _showingExistingShape = true;
            _outlineShapeId = shape.Id;
        }
        var state = DynamicMotionEffect.Evaluate(source.Value.Layer.Clip,
            source.Value.Layer.LocalTimelineSeconds);
        var rect = PreviewGeometry.PlaceLayer(SequenceFrame.Width, SequenceFrame.Height,
            source.Value.Gpu.Width, source.Value.Gpu.Height, state);
        DrawShapeDraft(rect, state, close: true);
    }
}
