using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class MonitorPanelView
{
    private string? _gizmoClipId;
    private PreviewRectangle _gizmoRect;
    private PreviewRectangle _gizmoStartRect;
    private IPointer? _gizmoPointer;
    private Point _gizmoStartPoint;
    private ClipVisualState? _gizmoStartState;
    private ClipVisualState? _gizmoPreviewState;
    private Clip? _gizmoSourceClip;
    private double _gizmoTime;
    private bool _gizmoScaling;
    private bool _temporaryAnchorVisible;
    private enum GizmoHandle { Move, Anchor, Rotate, NW, N, NE, E, SE, S, SW, W }
    private GizmoHandle _gizmoHandle;

    private void UpdateGizmo()
    {
        var controller = ProjectController.Instance;
        if (_gizmoPointer != null)
        {
            var current = controller?.CurrentProject.ActiveSequence;
            var editing = current?.Clips.FirstOrDefault(c => c.Id == controller?.SelectedClipId);
            if (!ReferenceEquals(editing, _gizmoSourceClip) || editing == null || !editing.Enabled ||
                current!.Tracks.FirstOrDefault(t => t.Id == editing.TrackId) is not { Enabled: true, Locked: false } ||
                Math.Abs(_requestedTime - _gizmoTime) > 1e-8)
                CancelGizmoGesture();
            return;
        }
        var sequence = controller?.CurrentProject.ActiveSequence;
        var clip = sequence?.Clips.FirstOrDefault(c => c.Id == controller?.SelectedClipId);
        var track = clip == null ? null : sequence!.Tracks.FirstOrDefault(t => t.Id == clip.TrackId);
        if (TextToolEnabled ||
            TransformGizmoToggle.IsChecked != true && AnchorGizmoToggle.IsChecked != true && !_temporaryAnchorVisible ||
            clip == null || track is not { Kind: "video", Enabled: true } ||
            !clip.Enabled || _requestedTime < clip.TimelineStart.Seconds ||
            _requestedTime >= clip.EndSeconds || SequenceFrame.Width <= 0 || SequenceFrame.Height <= 0)
        {
            if (_gizmoPointer == null) TransformOverlay.IsVisible = false;
            return;
        }

        var (sourceWidth, sourceHeight) = GizmoSourceSize(controller!.CurrentProject, sequence!, clip);
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            if (_gizmoPointer == null) TransformOverlay.IsVisible = false;
            return;
        }

        _gizmoClipId = clip.Id;
        var displayedState = DynamicMotionEffect.Evaluate(clip, _requestedTime);
        _gizmoRect = PreviewGeometry.PlaceLayer(SequenceFrame.Width, SequenceFrame.Height,
            sourceWidth, sourceHeight, _gizmoPointer != null ? _gizmoPreviewState ?? displayedState : displayedState);
        TransformOverlay.IsVisible = true;
        var color = track.Locked ? Avalonia.Media.Brushes.Gray : Avalonia.Media.Brushes.Orange;
        TransformOutline.Stroke = color;
        TransformOutline.IsVisible = TransformGizmoToggle.IsChecked == true;
        foreach (var handle in new[] { TransformHandleNW, TransformHandleNE, TransformHandleSW,
                     TransformHandleSE, TransformHandleN, TransformHandleS, TransformHandleW,
                     TransformHandleE, TransformRotateHandle })
        {
            handle.Background = color;
            handle.IsVisible = TransformGizmoToggle.IsChecked == true;
        }
        TransformAnchorHandle.IsVisible = AnchorGizmoToggle.IsChecked == true || _temporaryAnchorVisible;
        TransformAnchorHandle.Background = color;
        TransformRotateStem.IsVisible = TransformGizmoToggle.IsChecked == true;
        TransformRotateStem.Stroke = color;
        PlaceGizmo(_gizmoRect);
    }

    private void PlaceGizmo(PreviewRectangle rect)
    {
        var state = _gizmoPointer != null ? _gizmoPreviewState :
            ProjectController.Instance?.CurrentProject.ActiveSequence?.Clips
                .FirstOrDefault(c => c.Id == _gizmoClipId) is { } clip
                    ? DynamicMotionEffect.Evaluate(clip, _requestedTime) : null;
        if (state == null) return;
        TransformOutline.Width = rect.Width;
        TransformOutline.Height = rect.Height;
        TransformOutline.RenderTransformOrigin = new RelativePoint(state.FlipX ? 1 - state.AnchorX : state.AnchorX,
            state.FlipY ? 1 - state.AnchorY : state.AnchorY, RelativeUnit.Relative);
        TransformOutline.RenderTransform = new Avalonia.Media.RotateTransform(state.RotationDegrees);
        Canvas.SetLeft(TransformOutline, rect.X);
        Canvas.SetTop(TransformOutline, rect.Y);
        void Place(Border handle, double x, double y)
        {
            var rotated = PreviewGeometry.RotateLayerPoint(rect, state, x, y);
            PlaceHandle(handle, rotated.X, rotated.Y);
        }
        Place(TransformHandleNW, 0, 0);
        Place(TransformHandleNE, 1, 0);
        Place(TransformHandleSW, 0, 1);
        Place(TransformHandleSE, 1, 1);
        Place(TransformHandleN, 0.5, 0);
        Place(TransformHandleS, 0.5, 1);
        Place(TransformHandleW, 0, 0.5);
        Place(TransformHandleE, 1, 0.5);
        var top = PreviewGeometry.RotateLayerPoint(rect, state, 0.5, 0);
        var rotation = PreviewGeometry.RotateLayerPoint(rect, state, 0.5, -24 / Math.Max(1, rect.Height));
        TransformRotateStem.StartPoint = new Point(top.X, top.Y);
        TransformRotateStem.EndPoint = new Point(rotation.X, rotation.Y);
        PlaceHandle(TransformRotateHandle, rotation.X, rotation.Y);
        var anchor = PreviewGeometry.RotateLayerPoint(rect, state,
            state.FlipX ? 1 - state.AnchorX : state.AnchorX,
            state.FlipY ? 1 - state.AnchorY : state.AnchorY);
        PlaceHandle(TransformAnchorHandle, anchor.X, anchor.Y);
    }

    private static void PlaceHandle(Border handle, double x, double y)
    {
        Canvas.SetLeft(handle, x - handle.Width / 2);
        Canvas.SetTop(handle, y - handle.Height / 2);
    }

    private void OnGizmoPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(TransformOverlay).Properties.IsLeftButtonPressed || _gizmoClipId == null) return;
        var sequence = ProjectController.Instance?.CurrentProject.ActiveSequence;
        var clip = sequence?.Clips.FirstOrDefault(c => c.Id == _gizmoClipId);
        var track = clip == null ? null : sequence!.Tracks.FirstOrDefault(t => t.Id == clip.TrackId);
        if (clip == null || track?.Locked != false) return;

        var point = e.GetPosition(TransformOverlay);
        var rect = _gizmoRect;
        var displayed = DynamicMotionEffect.Evaluate(clip, _requestedTime);
        var displayedAnchorX = displayed.FlipX ? 1 - displayed.AnchorX : displayed.AnchorX;
        var displayedAnchorY = displayed.FlipY ? 1 - displayed.AnchorY : displayed.AnchorY;
        var anchorPoint = PreviewGeometry.RotateLayerPoint(rect, displayed, displayedAnchorX, displayedAnchorY);
        _gizmoHandle = TransformAnchorHandle.IsVisible && Math.Abs(point.X - anchorPoint.X) <= 11 &&
            Math.Abs(point.Y - anchorPoint.Y) <= 11 ? GizmoHandle.Anchor : HitHandle(rect, displayed, point);
        if (_gizmoHandle != GizmoHandle.Anchor && TransformGizmoToggle.IsChecked != true) return;
        _gizmoScaling = _gizmoHandle is not GizmoHandle.Move and not GizmoHandle.Anchor and not GizmoHandle.Rotate;
        var local = PreviewGeometry.RotateDelta(point.X - anchorPoint.X,
            point.Y - anchorPoint.Y, -displayed.RotationDegrees);
        var unrotatedX = anchorPoint.X + local.X;
        var unrotatedY = anchorPoint.Y + local.Y;
        var inside = unrotatedX >= rect.X && unrotatedX <= rect.X + rect.Width &&
            unrotatedY >= rect.Y && unrotatedY <= rect.Y + rect.Height;
        if ((_gizmoHandle == GizmoHandle.Move || _gizmoHandle == GizmoHandle.Anchor) && !inside) return;

        _gizmoPointer = e.Pointer;
        _gizmoStartPoint = point;
        _gizmoStartRect = rect;
        _gizmoStartState = CopyVisual(displayed);
        _gizmoPreviewState = CopyVisual(displayed);
        _gizmoSourceClip = clip;
        _gizmoTime = _requestedTime;
        e.Pointer.Capture(TransformOverlay);
        e.Handled = true;
    }

    private static GizmoHandle HitHandle(PreviewRectangle rect, ClipVisualState state, Point point)
    {
        bool Near(double x, double y)
        {
            var handle = PreviewGeometry.RotateLayerPoint(rect, state, x, y);
            return Math.Abs(point.X - handle.X) <= 11 && Math.Abs(point.Y - handle.Y) <= 11;
        }
        if (Near(0.5, -24 / Math.Max(1, rect.Height))) return GizmoHandle.Rotate;
        if (Near(0, 0)) return GizmoHandle.NW;
        if (Near(1, 0)) return GizmoHandle.NE;
        if (Near(1, 1)) return GizmoHandle.SE;
        if (Near(0, 1)) return GizmoHandle.SW;
        if (Near(0.5, 0)) return GizmoHandle.N;
        if (Near(1, 0.5)) return GizmoHandle.E;
        if (Near(0.5, 1)) return GizmoHandle.S;
        if (Near(0, 0.5)) return GizmoHandle.W;
        return GizmoHandle.Move;
    }

    private void OnGizmoMoved(object? sender, PointerEventArgs e)
    {
        if (_gizmoPointer != e.Pointer || _gizmoStartState == null || _gizmoPreviewState == null) return;
        var point = e.GetPosition(TransformOverlay);
        var dx = point.X - _gizmoStartPoint.X;
        var dy = point.Y - _gizmoStartPoint.Y;
        if (_gizmoHandle == GizmoHandle.Rotate) RotateGizmo(point, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        else if (_gizmoHandle == GizmoHandle.Anchor) MoveAnchor(dx, dy);
        else if (_gizmoScaling) ResizeGizmo(dx, dy, e.KeyModifiers.HasFlag(KeyModifiers.Control));
        else
        {
            _gizmoPreviewState.PositionX = Math.Clamp(_gizmoStartState.PositionX + dx / SequenceFrame.Width, -1, 1);
            _gizmoPreviewState.PositionY = Math.Clamp(_gizmoStartState.PositionY + dy / SequenceFrame.Height, -1, 1);
        }
        PreviewGizmoVisual();
        e.Handled = true;
    }

    private void RotateGizmo(Point point, bool snap)
    {
        if (_gizmoStartState == null || _gizmoPreviewState == null) return;
        var anchorX = _gizmoStartState.FlipX ? 1 - _gizmoStartState.AnchorX : _gizmoStartState.AnchorX;
        var anchorY = _gizmoStartState.FlipY ? 1 - _gizmoStartState.AnchorY : _gizmoStartState.AnchorY;
        var pivotX = _gizmoStartRect.X + anchorX * _gizmoStartRect.Width;
        var pivotY = _gizmoStartRect.Y + anchorY * _gizmoStartRect.Height;
        var initial = Math.Atan2(_gizmoStartPoint.Y - pivotY, _gizmoStartPoint.X - pivotX);
        var current = Math.Atan2(point.Y - pivotY, point.X - pivotX);
        var degrees = _gizmoStartState.RotationDegrees + (current - initial) * 180 / Math.PI;
        if (snap) degrees = Math.Round(degrees / 15) * 15;
        _gizmoPreviewState.RotationDegrees = ((degrees + 180) % 360 + 360) % 360 - 180;
    }

    private void MoveAnchor(double dx, double dy)
    {
        if (_gizmoStartState == null || _gizmoPreviewState == null) return;
        var before = _gizmoStartState;
        var next = _gizmoPreviewState;
        var width = Math.Max(1, _gizmoStartRect.Width);
        var height = Math.Max(1, _gizmoStartRect.Height);
        var local = PreviewGeometry.RotateDelta(dx, dy, -before.RotationDegrees);
        next.AnchorX = Math.Clamp(before.AnchorX + local.X / width * (before.FlipX ? -1 : 1), 0, 1);
        next.AnchorY = Math.Clamp(before.AnchorY + local.Y / height * (before.FlipY ? -1 : 1), 0, 1);
        var deltaX = (before.FlipX ? before.AnchorX - next.AnchorX : next.AnchorX - before.AnchorX) * width;
        var deltaY = (before.FlipY ? before.AnchorY - next.AnchorY : next.AnchorY - before.AnchorY) * height;
        var moved = PreviewGeometry.RotateDelta(deltaX, deltaY, before.RotationDegrees);
        next.PositionX = before.PositionX + moved.X / SequenceFrame.Width;
        next.PositionY = before.PositionY + moved.Y / SequenceFrame.Height;
    }

    private void ResizeGizmo(double dx, double dy, bool freeAspect)
    {
        if (_gizmoStartState == null || _gizmoPreviewState == null) return;
        var local = PreviewGeometry.RotateDelta(dx, dy, -_gizmoStartState.RotationDegrees);
        dx = local.X;
        dy = local.Y;
        var rect = _gizmoStartRect;
        var xHandle = _gizmoHandle is GizmoHandle.NW or GizmoHandle.W or GizmoHandle.SW ? -1 :
            _gizmoHandle is GizmoHandle.NE or GizmoHandle.E or GizmoHandle.SE ? 1 : 0;
        var yHandle = _gizmoHandle is GizmoHandle.NW or GizmoHandle.N or GizmoHandle.NE ? -1 :
            _gizmoHandle is GizmoHandle.SW or GizmoHandle.S or GizmoHandle.SE ? 1 : 0;
        var signedWidth = xHandle == -1 ? rect.Width - dx : xHandle == 1 ? rect.Width + dx : rect.Width;
        var signedHeight = yHandle == -1 ? rect.Height - dy : yHandle == 1 ? rect.Height + dy : rect.Height;
        var ratioX = xHandle == 0 ? 1 : Math.Abs(signedWidth) / Math.Max(1, rect.Width);
        var ratioY = yHandle == 0 ? 1 : Math.Abs(signedHeight) / Math.Max(1, rect.Height);
        if (_gizmoStartState.ScaleLinked && !freeAspect)
        {
            var ratio = xHandle == 0 ? ratioY : yHandle == 0 ? ratioX :
                Math.Abs(ratioX - 1) >= Math.Abs(ratioY - 1) ? ratioX : ratioY;
            ratioX = ratioY = ratio;
        }
        var scaleX = Math.Clamp(_gizmoStartState.ScaleX * ratioX, 0.01, 4);
        var scaleY = Math.Clamp(_gizmoStartState.ScaleY * ratioY, 0.01, 4);
        var width = rect.Width * scaleX / Math.Max(0.01, _gizmoStartState.ScaleX);
        var height = rect.Height * scaleY / Math.Max(0.01, _gizmoStartState.ScaleY);
        var signX = signedWidth < 0 ? -1 : 1;
        var signY = signedHeight < 0 ? -1 : 1;
        var centerX = xHandle == -1 ? rect.X + rect.Width - signX * width / 2 :
            xHandle == 1 ? rect.X + signX * width / 2 : rect.X + rect.Width / 2;
        var centerY = yHandle == -1 ? rect.Y + rect.Height - signY * height / 2 :
            yHandle == 1 ? rect.Y + signY * height / 2 : rect.Y + rect.Height / 2;
        _gizmoPreviewState.ScaleX = scaleX;
        _gizmoPreviewState.ScaleY = scaleY;
        _gizmoPreviewState.FlipX = _gizmoStartState.FlipX ^ (signX < 0);
        _gizmoPreviewState.FlipY = _gizmoStartState.FlipY ^ (signY < 0);
        var visualAnchorX = _gizmoPreviewState.FlipX ? 1 - _gizmoPreviewState.AnchorX : _gizmoPreviewState.AnchorX;
        var visualAnchorY = _gizmoPreviewState.FlipY ? 1 - _gizmoPreviewState.AnchorY : _gizmoPreviewState.AnchorY;
        var oldAnchorX = _gizmoStartState.FlipX ? 1 - _gizmoStartState.AnchorX : _gizmoStartState.AnchorX;
        var oldAnchorY = _gizmoStartState.FlipY ? 1 - _gizmoStartState.AnchorY : _gizmoStartState.AnchorY;
        var moved = PreviewGeometry.RotateDelta(
            centerX + (visualAnchorX - 0.5) * width - (rect.X + oldAnchorX * rect.Width),
            centerY + (visualAnchorY - 0.5) * height - (rect.Y + oldAnchorY * rect.Height),
            _gizmoStartState.RotationDegrees);
        _gizmoPreviewState.PositionX = Math.Clamp(_gizmoStartState.PositionX + moved.X / SequenceFrame.Width, -1, 1);
        _gizmoPreviewState.PositionY = Math.Clamp(_gizmoStartState.PositionY + moved.Y / SequenceFrame.Height, -1, 1);
    }

    private void PreviewGizmoVisual()
    {
        if (_gizmoPreviewState == null || _gizmoClipId == null) return;
        var controller = ProjectController.Instance;
        var sequence = controller?.CurrentProject.ActiveSequence;
        var clip = sequence?.Clips.FirstOrDefault(c => c.Id == _gizmoClipId);
        if (clip == null) return;
        var (width, height) = GizmoSourceSize(controller!.CurrentProject, sequence!, clip);
        if (width <= 0 || height <= 0) return;
        _gizmoRect = PreviewGeometry.PlaceLayer(SequenceFrame.Width, SequenceFrame.Height, width, height, _gizmoPreviewState);
        PlaceGizmo(_gizmoRect);
        // All visible layers use the shared compositor, including regular video, text and MGT.
        _ = RefreshGizmoGroupAsync();
    }

    private (int Width, int Height) GizmoSourceSize(Project project, Sequence sequence, Clip clip)
    {
        if (clip.NestedContent != null) return (sequence.Width, sequence.Height);
        var entry = _nestedLayers.FirstOrDefault(layer => layer.ClipId == clip.Id);
        var asset = project.Assets.FirstOrDefault(a => a.Id == clip.AssetId);
        var stream = asset?.Streams.FirstOrDefault(s => s.Id == clip.StreamId) ??
            asset?.Streams.FirstOrDefault(s => s.Kind is "video" or "image");
        return (entry?.Gpu?.Width ?? entry?.Mgt?.Width ?? stream?.Width ?? 0,
            entry?.Gpu?.Height ?? entry?.Mgt?.Height ?? stream?.Height ?? 0);
    }

    private void OnGizmoReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_gizmoPointer != e.Pointer || e.InitialPressMouseButton != MouseButton.Left) return;
        var id = _gizmoClipId;
        var next = _gizmoPreviewState;
        var before = _gizmoStartState;
        var time = _gizmoTime;
        _gizmoPointer = null;
        _gizmoPreviewState = null;
        _gizmoStartState = null;
        _gizmoSourceClip = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        if (id == null || next == null || before == null) return;
        if (Math.Abs(next.PositionX - before.PositionX) < 0.0001 &&
            Math.Abs(next.PositionY - before.PositionY) < 0.0001 &&
            Math.Abs(next.ScaleX - before.ScaleX) < 0.0001 &&
            Math.Abs(next.ScaleY - before.ScaleY) < 0.0001 &&
            next.FlipX == before.FlipX && next.FlipY == before.FlipY &&
            Math.Abs(next.AnchorX - before.AnchorX) < 0.0001 &&
            Math.Abs(next.AnchorY - before.AnchorY) < 0.0001 &&
            Math.Abs(next.RotationDegrees - before.RotationDegrees) < 0.0001) { UpdateGizmo(); QueueRefresh(); return; }
        try
        {
            ProjectController.Instance?.UndoRedo.Commit(project =>
            {
                var sequence = project.ActiveSequence ?? throw new InvalidOperationException("Montaje no disponible.");
                var clip = sequence.Clips.FirstOrDefault(c => c.Id == id) ?? throw new InvalidOperationException("Medio no disponible.");
                if (sequence.Tracks.FirstOrDefault(t => t.Id == clip.TrackId)?.Locked == true)
                    throw new InvalidOperationException("La pista está bloqueada.");
                ApplyGizmoDelta(clip, before, next, time);
            }, _gizmoHandle == GizmoHandle.Rotate ? "Rotar medio en previsualización" :
                _gizmoHandle == GizmoHandle.Anchor ? "Mover anclaje del medio" :
                _gizmoScaling ? "Escalar medio en previsualización" : "Mover medio en previsualización");
        }
        catch (Exception ex) { ProjectController.Instance?.ReportStatus("Transformación: " + ex.Message); }
        UpdateGizmo();
        QueueRefresh();
    }

    private void CancelGizmoGesture()
    {
        if (_gizmoPointer == null) return;
        var pointer = _gizmoPointer;
        _gizmoPointer = null;
        _gizmoPreviewState = null;
        _gizmoStartState = null;
        _gizmoSourceClip = null;
        pointer.Capture(null);
        UpdateGizmo();
        QueueRefresh();
    }

    private static ClipVisualState CopyVisual(ClipVisualState state) => new()
    {
        Framing = state.Framing, PositionX = state.PositionX, PositionY = state.PositionY,
        ScaleX = state.ScaleX, ScaleY = state.ScaleY, ScaleLinked = state.ScaleLinked,
        FlipX = state.FlipX, FlipY = state.FlipY, Opacity = state.Opacity, BlendMode = state.BlendMode,
        AnchorX = state.AnchorX, AnchorY = state.AnchorY,
        RotationDegrees = state.RotationDegrees
    };

    private static void ApplyGizmoDelta(Clip clip, ClipVisualState before, ClipVisualState after, double time)
    {
        void Adjust(string target, double oldValue, double newValue, Func<ClipVisualState, double> get,
            Action<ClipVisualState, double> set, double min, double max)
        {
            var delta = newValue - oldValue;
            if (Math.Abs(delta) < 1e-8) return;
            var current = KeyframeAnimation.Evaluate(clip, target, time, get(clip.Visual));
            var adjusted = Math.Clamp(current + delta, min, max);
            if (KeyframeAnimation.Find(clip, target) != null)
                KeyframeAnimation.Set(clip, target, time, adjusted);
            else set(clip.Visual, adjusted);
        }
        Adjust("visual.position_x", before.PositionX, after.PositionX, state => state.PositionX,
            (state, value) => state.PositionX = value, -1, 1);
        Adjust("visual.position_y", before.PositionY, after.PositionY, state => state.PositionY,
            (state, value) => state.PositionY = value, -1, 1);
        Adjust("visual.scale_x", before.ScaleX, after.ScaleX, state => state.ScaleX,
            (state, value) => state.ScaleX = value, 0.01, 4);
        Adjust("visual.scale_y", before.ScaleY, after.ScaleY, state => state.ScaleY,
            (state, value) => state.ScaleY = value, 0.01, 4);
        Adjust("visual.rotation", before.RotationDegrees, after.RotationDegrees, state => state.RotationDegrees,
            (state, value) => state.RotationDegrees = value, -180, 180);
        Adjust("visual.anchor_x", before.AnchorX, after.AnchorX, state => state.AnchorX,
            (state, value) => state.AnchorX = value, 0, 1);
        Adjust("visual.anchor_y", before.AnchorY, after.AnchorY, state => state.AnchorY,
            (state, value) => state.AnchorY = value, 0, 1);
        clip.Visual.FlipX ^= before.FlipX != after.FlipX;
        clip.Visual.FlipY ^= before.FlipY != after.FlipY;
    }
}
