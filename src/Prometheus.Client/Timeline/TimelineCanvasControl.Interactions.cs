using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Timeline;

public partial class TimelineCanvasControl
{
    public Func<double, bool>? ExternalPointClick { get; set; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_sequence == null)
            return;

        var point = e.GetCurrentPoint(this);
        var pos = point.Position;
        if (point.Properties.IsLeftButtonPressed && ExternalPointClick?.Invoke(PixelsToTime(pos.X)) == true)
        {
            e.Handled = true;
            return;
        }
        if (point.Properties.IsMiddleButtonPressed)
        {
            _isMiddlePanning = true;
            _pointerStartPos = pos;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (e.ClickCount > 1) _repeatedClickGeneration++;
        double clickTime = PixelsToTime(pos.X);
        bool leftPressed = point.Properties.IsLeftButtonPressed;
        bool rightPressed = point.Properties.IsRightButtonPressed;
        var tool = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? TimelineTool.Selection : EffectiveTool;

        if (leftPressed && tool == TimelineTool.Selection && e.ClickCount == 2 && e.KeyModifiers == KeyModifiers.None &&
            HitTestClip(pos) is { NestedContent: not null } doubleClicked)
        {
            NestedClipDoubleClicked?.Invoke(doubleClicked.Id);
            e.Handled = true;
            return;
        }

        // 2. Herramienta Zoom (Z): Clic Izquierdo = Ampliar, Clic Derecho = Reducir
        if (tool == TimelineTool.Zoom)
        {
            if (rightPressed)
            {
                ZoomAt(pos.X, 0.80);
                e.Handled = true;
                return;
            }
            if (leftPressed)
            {
                _isZoomSelecting = true;
                _zoomStartX = _zoomEndX = pos.X;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        if (rightPressed)
        {
            ShowRightMenu(pos, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            e.Handled = true;
            return;
        }

        if (leftPressed && tool == TimelineTool.Selection && TransitionAt(pos) is { } transition)
        {
            Focus();
            TransitionSelected?.Invoke(transition);
            e.Handled = true;
            return;
        }
        if (leftPressed) TransitionSelected?.Invoke(null);

        if (leftPressed && tool == TimelineTool.Text)
        {
            Focus();
            PlayheadSeconds = clickTime;
            e.Handled = true;
            return;
        }

        if (leftPressed && tool == TimelineTool.Sam)
        {
            var target = HitTestClip(pos);
            if (target != null && _sequence.Tracks.Any(track => track.Id == target.TrackId &&
                track.Kind == "video"))
            {
                FocusedClipId = target.Id;
                FocusedClipChanged?.Invoke();
                _selection.SelectClip(target.Id);
            }
            PlayheadSeconds = clickTime;
            e.Handled = true;
            return;
        }

        if (leftPressed)
        {
            Focus();
            _selectionBeforeGesture = _selection.SelectedClipIds.ToArray();
            _gapBeforeGesture = _selection.CurrentGap;
            _pointerStartPos = pos;
            _dragStartTime = clickTime;
            _editGestureMoved = false;
            _gesturePointer = e.Pointer;
            e.Pointer.Capture(this);

            // Comprobar si se hace clic sobre un tirador semicircular de Roll Edit
            var rollPair = HitTestRollPair(pos);
            if (rollPair != null && tool == TimelineTool.Selection)
            {
                _isRolling = true;
                _activeRollLeftId = rollPair.Value.Left.Id;
                _activeRollRightId = rollPair.Value.Right.Id;
                _rollOriginalCutTime = rollPair.Value.Left.EndSeconds;
                _previewRollCut = _rollOriginalCutTime;
                e.Handled = true;
                return;
            }

            // Herramienta Cuchilla (C): Iniciar posible corte por área o división simple
            if (tool == TimelineTool.Razor)
            {
                _isRangeCutting = true;
                _rangeCutStart = clickTime;
                _rangeCutEnd = clickTime;
                e.Handled = true;
                return;
            }

            // Herramienta Deslizamiento (Slip)
            if (tool == TimelineTool.Slip)
            {
                var targetClip = HitTestClip(pos);
                if (targetClip != null && CanEditClips(new[] { targetClip.Id }))
                {
                    _isSlippingClip = true;
                    _activeDragClipId = targetClip.Id;
                    _slipOriginalSourceIn = targetClip.SourceIn.Seconds;
                    _previewSlipSourceIn = _slipOriginalSourceIn;
                }
                e.Handled = true;
                return;
            }

            // Herramienta Selección Direccional Dinámica (A): Definir ancla de vector (NO selecciona en clic inicial)
            if (tool == TimelineTool.SelectForward || tool == TimelineTool.SelectBackward)
            {
                _isDirectionalSelecting = true;
                _directionalAnchorPos = pos;
                _directionalAnchorTime = clickTime;
                _directionalAnchorTrackId = GetTrackAtY(pos.Y)?.Id;
                _directionalMode = tool == TimelineTool.SelectBackward ? "backward" : "forward";
                _directionalAdditive = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                if (!_directionalAdditive) _selection.Clear();
                _directionalAxis = null;
                _directionalMoved = false;
                _directionalPreviewIds.Clear();
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // Herramienta Mano (H)
            if (tool == TimelineTool.Hand)
            {
                _isHandPanning = true;
                e.Handled = true;
                return;
            }

            // Un clip siempre tiene prioridad sobre un hueco de la misma pista.
            var hitClip = HitTestClip(pos);
            var hitGap = hitClip == null && !e.KeyModifiers.HasFlag(KeyModifiers.Control) ? HitTestAnyGap(pos) : null;
            if (hitGap != null)
            {
                FocusedClipId = null;
                _pendingGapClick = new SelectedGap { TrackId = hitGap.TrackId,
                    StartSeconds = hitGap.StartSeconds, EndSeconds = hitGap.EndSeconds };
                e.Handled = true;
                return;
            }

            // Comprobar clic en Clip
            if (hitClip != null)
            {
                FocusedClipId = hitClip.Id;
                FocusedClipChanged?.Invoke();
                bool isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                bool isAlt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

                // El grupo editorial se selecciona como unidad; Alt conserva la
                // separación temporal de vínculos para duplicaciones individuales.
                var expandLinks = !isAlt || _sequence?.EditGroups.Any(g => g.ClipIds.Contains(hitClip.Id)) == true;
                var movingClipIds = _sequence == null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { hitClip.Id }
                    : EditGroupOperations.Expand(_sequence, new[] { hitClip.Id }, includeLinks: expandLinks)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (isCtrl)
                {
                    var current = _selection.SelectedClipIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (movingClipIds.All(current.Contains)) current.ExceptWith(movingClipIds);
                    else current.UnionWith(movingClipIds);
                    _selection.SelectClips(current);
                }
                else if (isShift)
                {
                    _selection.SelectClips(movingClipIds, addToSelection: true);
                }
                else if (!movingClipIds.All(id => _selection.IsSelected(id)))
                {
                    _selection.SelectClips(movingClipIds);
                }
                if (isCtrl && !_selection.IsSelected(hitClip.Id))
                {
                    e.Handled = true;
                    return;
                }

                // El candado protege la edición, pero permite seleccionar e inspeccionar.
                // También cubre vínculos y selecciones mixtas antes de iniciar un arrastre.
                var editIds = new HashSet<string>(_selection.SelectedClipIds, StringComparer.OrdinalIgnoreCase);
                editIds.Add(hitClip.Id);
                if (_sequence != null)
                    editIds.UnionWith(EditGroupOperations.Expand(_sequence, editIds, includeLinks: expandLinks));
                if (!CanEditClips(editIds))
                {
                    e.Handled = true;
                    return;
                }

                // Hover y pulsación comparten exactamente la misma zona de recorte.
                var trimEdge = tool == TimelineTool.Selection ? HitTestTrimEdge(pos) : (null, 0);
                if (trimEdge.Item1?.Id == hitClip.Id && trimEdge.Item2 == 1)
                {
                    _isTrimmingLeft = true;
                    _activeDragClipId = hitClip.Id;
                    _activeDragOriginalStart = hitClip.TimelineStart.Seconds;
                    _activeDragOriginalDuration = hitClip.Duration.Seconds;
                    _previewTrimStart = hitClip.TimelineStart.Seconds;
                    _previewTrimDuration = hitClip.Duration.Seconds;
                    _previewTrimSourceIn = hitClip.SourceIn.Seconds;
                }
                else if (trimEdge.Item1?.Id == hitClip.Id && trimEdge.Item2 == 2)
                {
                    _isTrimmingRight = true;
                    _activeDragClipId = hitClip.Id;
                    _activeDragOriginalStart = hitClip.TimelineStart.Seconds;
                    _activeDragOriginalDuration = hitClip.Duration.Seconds;
                    _previewTrimStart = hitClip.TimelineStart.Seconds;
                    _previewTrimDuration = hitClip.Duration.Seconds;
                    _previewTrimSourceIn = hitClip.SourceIn.Seconds;
                }
                else
                {
                    _isDraggingClip = true;
                    _activeDragClipId = hitClip.Id;
                    _isAltDuplicating = isAlt;
                    // A click can end without a PointerMoved event. Keep its source
                    // track as the valid drop target so release does not cancel the
                    // gesture and restore the previous selection.
                    _dragTargetTrackId = hitClip.TrackId;
                    _autoCreateTrackKind = null;
                    _dragDeltaSeconds = 0;

                    // Mover todos los clips seleccionados y sus vinculados
                    var allMoving = new HashSet<string>(_selection.SelectedClipIds, StringComparer.OrdinalIgnoreCase);
                    allMoving.Add(hitClip.Id);

                    if (_sequence != null)
                        allMoving.UnionWith(EditGroupOperations.Expand(_sequence, allMoving, includeLinks: expandLinks));

                    _draggingClipOriginalStarts.Clear();
                    _draggingClipOriginalTracks.Clear();

                    if (_sequence != null)
                    {
                        foreach (var id in allMoving)
                        {
                            var c = _sequence.Clips.Find(x => x.Id == id);
                            if (c != null)
                            {
                                _draggingClipOriginalStarts[id] = c.TimelineStart.Seconds;
                                _draggingClipOriginalTracks[id] = c.TrackId;
                            }
                        }
                    }
                }

                e.Handled = true;
                return;
            }

            // Clic en espacio vacío: Marquee selection
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                return;
            }
            // NOTA: El lector (playhead) solo se reposiciona desde la regla superior
            _selection.Clear();
            _isMarqueeSelecting = true;
            _marqueeRect = new Rect(pos.X, pos.Y, 0, 0);

            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_sequence == null)
            return;

        var pos = e.GetPosition(this);
        if (_pendingGapClick != null)
        {
            if (Math.Abs(pos.X - _pointerStartPos.X) < 4 && Math.Abs(pos.Y - _pointerStartPos.Y) < 4) return;
            _pendingGapClick = null;
            _selection.Clear();
            _isMarqueeSelecting = true;
            _marqueeRect = new Rect(_pointerStartPos.X, _pointerStartPos.Y, 0, 0);
        }
        if (_isZoomSelecting)
        {
            _zoomEndX = Math.Clamp(pos.X, 0, Bounds.Width);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (_isMiddlePanning)
        {
            HorizontalOffsetSeconds = Math.Max(0, HorizontalOffsetSeconds - (pos.X - _pointerStartPos.X) / ZoomPixelsPerSecond);
            var vertical = pos.Y - _pointerStartPos.Y;
            if (_pointerStartPos.Y < DividerY) VideoScrollOffset += vertical;
            else AudioScrollOffset -= vertical;
            _pointerStartPos = pos;
            e.Handled = true;
            return;
        }
        var previousTrimId = _hoverTrimClipId;
        var previousTrimSide = _hoverTrimSide;
        double currentTime = PixelsToTime(pos.X);
        if (HasActiveEditGesture && !_editGestureMoved)
        {
            var displacement = pos - _pointerStartPos;
            if (displacement.X * displacement.X + displacement.Y * displacement.Y < 16) return;
            _editGestureMoved = true;
        }
        _hoverPointerX = pos.X;
        PointerHoveredXChanged?.Invoke(_hoverPointerX);

        // Comprobación de hover sobre tiradores de Roll Edit
        var rollHover = HitTestRollPair(pos);
        if (rollHover != null)
        {
            _hoverRollLeftId = rollHover.Value.Left.Id;
            _hoverRollRightId = rollHover.Value.Right.Id;
            Cursor = new Cursor(StandardCursorType.SizeWestEast);
        }
        else
        {
            _hoverRollLeftId = null;
            _hoverRollRightId = null;
        }

        // Comprobación de hover sobre bordes de clip para glow interno
        var edgeHit = EffectiveTool == TimelineTool.Selection ? HitTestTrimEdge(pos) : (null, 0);
        if (edgeHit.Clip != null)
        {
            _hoverTrimClipId = edgeHit.Clip.Id;
            _hoverTrimSide = edgeHit.Side;
            Cursor = new Cursor(StandardCursorType.SizeWestEast);
        }
        else if (rollHover != null)
        {
            Cursor = new Cursor(StandardCursorType.SizeWestEast);
        }
        else
        {
            _hoverTrimClipId = null;
            _hoverTrimSide = 0;
            UpdateCursor();
        }
        if (previousTrimId != _hoverTrimClipId || previousTrimSide != _hoverTrimSide) InvalidateVisual();

        if (_isRolling && !string.IsNullOrEmpty(_activeRollLeftId) && !string.IsNullOrEmpty(_activeRollRightId))
        {
            _previewRollCut = ClampRollCut(currentTime);
            InvalidateVisual();
            return;
        }

        if (_isRangeCutting)
        {
            _rangeCutEnd = currentTime;
            InvalidateVisual();
            return;
        }

        if (_isDirectionalSelecting)
        {
            _pointerStartPos = pos;
            double dx = pos.X - _directionalAnchorPos.X;
            double dy = pos.Y - _directionalAnchorPos.Y;
            var absX = Math.Abs(dx);
            var absY = Math.Abs(dy);
            if (_directionalAxis == null && Math.Max(absX, absY) >= 12)
                _directionalAxis = absX >= absY ? "horizontal" : "vertical";
            else if (_directionalAxis == "horizontal" && absY > absX * 1.25 + 4)
                _directionalAxis = "vertical";
            else if (_directionalAxis == "vertical" && absX > absY * 1.25 + 4)
                _directionalAxis = "horizontal";
            if (_directionalAxis != null)
            {
                _directionalMoved = true;
                _directionalMode = _directionalAxis == "horizontal"
                    ? (dx >= 0 ? "forward" : "backward")
                    : (dy >= 0 ? "down" : "up");
                _directionalPreviewIds.Clear();
                foreach (var id in DirectionalClipIds()) _directionalPreviewIds.Add(id);
            }
            InvalidateVisual();
            return;
        }

        if (EffectiveTool == TimelineTool.Razor)
        {
            _pointerStartPos = pos;
            InvalidateVisual();
            return;
        }

        if (_isHandPanning)
        {
            double deltaPixels = pos.X - _pointerStartPos.X;
            HorizontalOffsetSeconds = Math.Max(0, _horizontalOffsetSeconds - (deltaPixels / _zoomPixelsPerSecond));
            double deltaY = pos.Y - _pointerStartPos.Y;
            if (_pointerStartPos.Y < DividerY) VideoScrollOffset += deltaY;
            else AudioScrollOffset -= deltaY;
            _pointerStartPos = pos;
            return;
        }

        if (_isMarqueeSelecting)
        {
            double minX = Math.Min(_pointerStartPos.X, pos.X);
            double minY = Math.Min(_pointerStartPos.Y, pos.Y);
            double width = Math.Abs(pos.X - _pointerStartPos.X);
            double height = Math.Abs(pos.Y - _pointerStartPos.Y);

            _marqueeRect = new Rect(minX, minY, width, height);

            var intersectingClips = GetClipsIntersectingRect(_marqueeRect);
            _selection.SelectClips(intersectingClips.Select(c => c.Id));

            InvalidateVisual();
            return;
        }

        if (_isSlippingClip && !string.IsNullOrEmpty(_activeDragClipId))
        {
            var clip = _sequence.Clips.Find(c => c.Id == _activeDragClipId);
            if (clip != null)
            {
                double deltaTime = currentTime - _dragStartTime;
                double maxSourceIn = clip.AvailableSourceDurationSeconds.HasValue
                    ? Math.Max(0, clip.AvailableSourceDurationSeconds.Value - clip.Duration.Seconds * Math.Max(0, clip.Speed.Seconds))
                    : double.MaxValue;
                _previewSlipSourceIn = Math.Clamp(_slipOriginalSourceIn - deltaTime, 0, maxSourceIn);
                InvalidateVisual();
            }
            return;
        }

        if (_isDraggingClip && _draggingClipOriginalStarts.Count > 0)
        {
            if (!CanEditClips(_draggingClipOriginalStarts.Keys)) { CancelGesture(); return; }
            if (!Bounds.Contains(pos))
            {
                _dragDeltaSeconds = 0;
                _dragTargetTrackId = _autoCreateTrackKind = null;
                _currentSnap = default;
                InvalidateVisual();
                return;
            }
            double deltaTime = currentTime - _dragStartTime;
            bool isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

            // Shift bloquea el eje temporal X
            if (isShift) deltaTime = 0;

            // Snapping magnético evaluando cabeza y cola (adelante y atrás)
            if (_isSnappingEnabled && !isShift && !string.IsNullOrEmpty(_activeDragClipId))
            {
                double anchorStart = _draggingClipOriginalStarts.TryGetValue(_activeDragClipId, out var s) ? s : 0;
                var anchorClip = _sequence.Clips.Find(c => c.Id == _activeDragClipId);
                double anchorDuration = anchorClip?.Duration.Seconds ?? 1.0;
                double proposedStart = Math.Max(0, anchorStart + deltaTime);

                _currentSnap = SnappingEngine.SnapClip(
                    proposedStart,
                    anchorDuration,
                    _sequence,
                    _playheadSeconds,
                    _zoomPixelsPerSecond,
                    excludedClipIds: _draggingClipOriginalStarts.Keys,
                    includeClip: SnapFilter(GetTrackAtY(pos.Y)?.Id ?? anchorClip?.TrackId));

                if (_currentSnap.IsSnapped)
                {
                    deltaTime = _currentSnap.SnappedTime - anchorStart;
                }
            }
            else
            {
                _currentSnap = default;
            }

            _dragDeltaSeconds = Math.Max(deltaTime, -_draggingClipOriginalStarts.Values.Min());

            // Detección de pista de destino y posible auto-creación de pista
            if (!string.IsNullOrEmpty(_activeDragClipId))
            {
                var anchorClip = _sequence.Clips.Find(c => c.Id == _activeDragClipId);
                var currentTrack = anchorClip != null ? _sequence.Tracks.Find(t => t.Id == anchorClip.TrackId) : null;

                if (anchorClip != null && currentTrack != null)
                {
                    var targetTrack = GetTrackAtY(pos.Y);
                    var newTrackKind = NewTrackDropKind(pos.Y);
                    // La pista existente siempre gana. Crear requiere soltar en el vacío real,
                    // separado del borde por un margen para evitar pistas accidentales.
                    if (targetTrack != null && string.Equals(targetTrack.Kind, currentTrack.Kind, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(targetTrack.Role, currentTrack.Role, StringComparison.OrdinalIgnoreCase) && !targetTrack.Locked)
                    {
                        _dragTargetTrackId = targetTrack.Id;
                        _autoCreateTrackKind = null;
                    }
                    else if (currentTrack.Kind == "video" && currentTrack.Role != "subtitle" && newTrackKind == "video")
                    {
                        _autoCreateTrackKind = "video";
                        _newTrackPointerY = pos.Y;
                        _dragTargetTrackId = null;
                    }
                    else if (currentTrack.Kind == "audio" && newTrackKind == "audio")
                    {
                        _autoCreateTrackKind = "audio";
                        _newTrackPointerY = pos.Y;
                        _dragTargetTrackId = null;
                    }
                    else
                    {
                        _autoCreateTrackKind = null;
                        _dragTargetTrackId = null;
                    }
                }
            }

            if (_dragTargetTrackId != null && _activeDragClipId != null &&
                !ClipMoveTargets.TryResolve(_sequence, _draggingClipOriginalStarts.Keys, _activeDragClipId,
                    _dragTargetTrackId, out _))
                _dragTargetTrackId = null;

            // NOTA CRÍTICA: NO mutar _sequence.Clips en caliente durante el drag.
            // Se mantiene el estado inmaculado para un Undo/Redo atómico perfecto (Cero superposiciones).
            InvalidateVisual();
            return;
        }

        if (_isTrimmingRight && !string.IsNullOrEmpty(_activeDragClipId))
        {
            var clip = _sequence.Clips.Find(c => c.Id == _activeDragClipId);
            if (clip != null && CanEditClips(EditGroupOperations.AlignedTrimTargets(_sequence, clip.Id, false)))
            {
                double targetEnd = Math.Max(clip.TimelineStart.Seconds + Math.Min(clip.Duration.Seconds, 1 / Math.Max(1, _sequence.Fps.Seconds)), currentTime);

                if (_isSnappingEnabled)
                {
                    _currentSnap = SnappingEngine.Snap(
                        targetEnd,
                        _sequence,
                        _playheadSeconds,
                        _zoomPixelsPerSecond,
                        excludedClipIds: new[] { clip.Id },
                        includeClip: SnapFilter(clip.TrackId));

                    if (_currentSnap.IsSnapped)
                        targetEnd = _currentSnap.SnappedTime;
                }

                var plan = ClipTrimOperations.PlanEdge(_project!, clip.Id, false, targetEnd);
                var clampedEnd = clip.EndSeconds + plan.Delta;
                if (Math.Abs(clampedEnd - targetEnd) > 1e-7) _currentSnap = default;
                _previewTrimStart = clip.TimelineStart.Seconds;
                _previewTrimDuration = clip.Duration.Seconds + plan.Delta;
                _previewTrimSourceIn = clip.SourceIn.Seconds;
                InvalidateVisual();
            }
            return;
        }

        if (_isTrimmingLeft && !string.IsNullOrEmpty(_activeDragClipId))
        {
            var clip = _sequence.Clips.Find(c => c.Id == _activeDragClipId);
            if (clip != null && CanEditClips(EditGroupOperations.AlignedTrimTargets(_sequence, clip.Id, true)))
            {
                double originalEnd = _activeDragOriginalStart + _activeDragOriginalDuration;
                double targetStart = Math.Clamp(currentTime, 0, originalEnd - Math.Min(clip.Duration.Seconds, 1 / Math.Max(1, _sequence.Fps.Seconds)));

                if (_isSnappingEnabled)
                {
                    _currentSnap = SnappingEngine.Snap(
                        targetStart,
                        _sequence,
                        _playheadSeconds,
                        _zoomPixelsPerSecond,
                        excludedClipIds: new[] { clip.Id },
                        includeClip: SnapFilter(clip.TrackId));

                    if (_currentSnap.IsSnapped)
                        targetStart = _currentSnap.SnappedTime;
                }

                var plan = ClipTrimOperations.PlanEdge(_project!, clip.Id, true, targetStart);
                var clampedStart = clip.TimelineStart.Seconds + plan.Delta;
                if (Math.Abs(clampedStart - targetStart) > 1e-7) _currentSnap = default;
                _previewTrimStart = clampedStart;
                _previewTrimDuration = clip.Duration.Seconds - plan.Delta;
                _previewTrimSourceIn = StillImageRules.IsStill(_project, clip) ? 0 :
                    Math.Max(0, clip.SourceIn.Seconds + plan.Delta * clip.Speed.Seconds);

                InvalidateVisual();
            }
            return;
        }

        UpdateCursor();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isZoomSelecting && e.InitialPressMouseButton == MouseButton.Left)
        {
            _isZoomSelecting = false;
            var endX = Math.Clamp(e.GetPosition(this).X, 0, Bounds.Width);
            e.Pointer.Capture(null);
            if (Math.Abs(endX - _zoomStartX) < 6)
                ZoomAt(_zoomStartX, e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 0.8 : 1.25);
            else
                FitTimeRange(PixelsToTime(Math.Min(_zoomStartX, endX)), PixelsToTime(Math.Max(_zoomStartX, endX)));
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (_isMiddlePanning && e.InitialPressMouseButton == MouseButton.Middle)
        {
            _isMiddlePanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            e.Handled = true;
            return;
        }
        if (_isSlippingClip && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(null);
        _gesturePointer = null;
        if (_pendingGapClick is { } pendingGap)
        {
            _pendingGapClick = null;
            _selection.ToggleGap(pendingGap.TrackId, pendingGap.StartSeconds, pendingGap.EndSeconds);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_isRolling)
        {
            string lId = _activeRollLeftId ?? "";
            string rId = _activeRollRightId ?? "";
            double cut = _previewRollCut ?? _rollOriginalCutTime;

            _isRolling = false;
            _activeRollLeftId = null;
            _activeRollRightId = null;
            _previewRollCut = null;

            if (Math.Abs(cut - _rollOriginalCutTime) >= 1.0 / 120.0)
                CommitRequested?.Invoke(p => RollEditOperations.Apply(p, p.ActiveSequence!, lId, rId, cut), "Edición de rodillo (Roll Edit)");

            InvalidateVisual();
            return;
        }

        if (_isRangeCutting)
        {
            double a = _rangeCutStart;
            double b = _rangeCutEnd;
            _isRangeCutting = false;

            if (Math.Abs(b - a) >= 1.0 / 60.0)
            {
                CommitRequested?.Invoke(p =>
                {
                    CollisionEngine.RangeTrim(p.ActiveSequence!, a, b);
                }, "Recorte de área con cuchilla");
            }
            else
            {
                // Clic simple: dividir clip en la posición
                var hit = HitTestClip(e.GetPosition(this));
                if (hit != null)
                {
                    CommitRequested?.Invoke(p =>
                    {
                        CollisionEngine.SplitLinkedClips(p.ActiveSequence!, hit.Id, a);
                    }, "Corte simple con cuchilla");
                }
            }

            InvalidateVisual();
            return;
        }

        if (_isDirectionalSelecting)
        {
            _isDirectionalSelecting = false;
            if (_directionalMoved) CommitDirectionalSelection();
            _directionalPreviewIds.Clear();
            _directionalAxis = null;
            _directionalMoved = false;
            _directionalAdditive = false;
            InvalidateVisual();
            return;
        }

        if (_isDraggingClip && _draggingClipOriginalStarts.Count > 0)
        {
            if (!Bounds.Contains(e.GetPosition(this))) { CancelGesture(); return; }
            if (!CanEditClips(_draggingClipOriginalStarts.Keys)) { CancelGesture(); return; }
            double finalDelta = _dragDeltaSeconds;
            var movingIds = _draggingClipOriginalStarts.Keys.ToList();
            bool wasAlt = _isAltDuplicating;
            string? targetTrackId = _dragTargetTrackId;
            string? autoTrackKind = _autoCreateTrackKind;
            var newTrackVideoArea = autoTrackKind == "video" ? VideoAreaHeightForNewTrack(_newTrackPointerY) : (double?)null;
            if (targetTrackId == null && autoTrackKind == null)
            {
                CancelGesture();
                return;
            }

            _isDraggingClip = false;
            _isAltDuplicating = false;
            _currentSnap = default;
            _dragDeltaSeconds = 0;
            _dragTargetTrackId = null;
            _autoCreateTrackKind = null;

            var anchorId = _activeDragClipId;
            var originalTrack = anchorId != null && _draggingClipOriginalTracks.TryGetValue(anchorId, out var sourceTrack) ? sourceTrack : null;
            if (Math.Abs(finalDelta) < 1.0 / 120.0 && (targetTrackId == null || targetTrackId == originalTrack) && autoTrackKind == null)
            {
                _draggingClipOriginalStarts.Clear();
                _draggingClipOriginalTracks.Clear();
                if (anchorId != null && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
                    !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                    ClipSelectionCompleted?.Invoke(anchorId);
                if (anchorId != null && _selectionBeforeGesture.Contains(anchorId) &&
                    !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
                    !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                {
                    var generation = ++_repeatedClickGeneration;
                    DispatcherTimer.RunOnce(() =>
                    {
                        if (generation == _repeatedClickGeneration)
                            SelectedClipClickedAgain?.Invoke(anchorId);
                    }, TimeSpan.FromMilliseconds(550));
                }
                InvalidateVisual();
                return;
            }

            CommitRequested?.Invoke(p =>
            {
                var seq = p.ActiveSequence!;

                // 1. Auto-crear pista si se soltó fuera de límites
                if (autoTrackKind != null)
                {
                    var newTrack = CollisionEngine.AddTrack(seq, autoTrackKind);
                    targetTrackId = newTrack.Id;
                    if (newTrackVideoArea.HasValue) seq.VideoAreaHeight = newTrackVideoArea.Value;
                }

                if (anchorId == null || targetTrackId == null ||
                    !ClipMoveTargets.TryResolve(seq, movingIds, anchorId, targetTrackId, out var targets))
                    throw new InvalidOperationException("No hay pistas compatibles para todos los medios seleccionados");

                if (wasAlt)
                {
                    // Duplicación limpia de clips con Alt (sin alterar originales)
                    var newClips = CollisionEngine.DuplicateClips(seq, movingIds, finalDelta);
                    var originals = seq.Clips.Where(c => movingIds.Contains(c.Id)).ToArray();
                    foreach (var pair in originals.Zip(newClips)) pair.Second.TrackId = targets[pair.First.Id];
                    // Sobrescribir solo tras validar que el puntero terminó en una pista real.
                    CollisionEngine.Overwrite(seq, newClips.Select(c => c.Id));
                }
                else
                {
                    // Mover clips aplicando el delta definitivo
                    foreach (var kvp in _draggingClipOriginalStarts)
                    {
                        var clip = seq.Clips.Find(c => c.Id == kvp.Key);
                        if (clip != null)
                        {
                            clip.TimelineStart = Rational.Time(Math.Max(0, kvp.Value + finalDelta));
                            clip.TrackId = targets[clip.Id];
                        }
                    }
                    var overwriting = movingIds.ToHashSet();
                    overwriting.ExceptWith(seq.Clips.Where(clip => TranscriptOperations.AudioSourceId(seq, clip) is { } audioId &&
                        movingIds.Contains(audioId)).Select(clip => clip.Id));
                    CollisionEngine.Overwrite(seq, overwriting);
                }
            }, wasAlt ? "Duplicar medios con Alt" : "Mover y sobrescribir medios");

            _draggingClipOriginalStarts.Clear();
            _draggingClipOriginalTracks.Clear();
            InvalidateVisual();
            return;
        }

        if (_isTrimmingLeft || _isTrimmingRight)
        {
            bool trimLeft = _isTrimmingLeft;
            string trimmedId = _activeDragClipId ?? "";
            double? start = _previewTrimStart;
            double? duration = _previewTrimDuration;
            double? sourceIn = _previewTrimSourceIn;
            _isTrimmingLeft = false;
            _isTrimmingRight = false;
            _activeDragClipId = null;
            _currentSnap = default;
            _previewTrimStart = null;
            _previewTrimDuration = null;
            _previewTrimSourceIn = null;

            var originalClip = _sequence?.Clips.Find(c => c.Id == trimmedId);
            var trimTargets = _sequence == null ? Array.Empty<string>() : EditGroupOperations.AlignedTrimTargets(_sequence, trimmedId, trimLeft).ToArray();
            if (originalClip != null && CanEditClips(trimTargets) && start.HasValue && duration.HasValue &&
                (Math.Abs(originalClip.TimelineStart.Seconds - start.Value) >= 1.0 / 48000 || Math.Abs(originalClip.Duration.Seconds - duration.Value) >= 1.0 / 48000))
                CommitRequested?.Invoke(p => ClipTrimOperations.ApplyEdge(p, trimmedId, trimLeft,
                    trimLeft ? start.Value : start.Value + duration.Value), "Recorte de bordes de clip (Trim)");

            InvalidateVisual();
            return;
        }

        if (_isSlippingClip)
        {
            var id = _activeDragClipId;
            var sourceIn = _previewSlipSourceIn;
            _isSlippingClip = false;
            _activeDragClipId = null;
            _previewSlipSourceIn = null;
            if (id != null && CanEditClips(new[] { id }) && sourceIn.HasValue && Math.Abs(sourceIn.Value - _slipOriginalSourceIn) >= 1.0 / 120.0)
                CommitRequested?.Invoke(p => p.ActiveSequence!.Clips.Find(c => c.Id == id)!.SourceIn = Rational.Time(sourceIn.Value), "Deslizamiento de contenido (Slip)");
            InvalidateVisual();
            return;
        }

        if (_isMarqueeSelecting)
        {
            _isMarqueeSelecting = false;
            _marqueeRect = default;
            InvalidateVisual();
            return;
        }

        if (_isHandPanning)
        {
            _isHandPanning = false;
        }
    }

    private void ShowRightMenu(Point pos, bool controlHeld)
    {
        if (TransitionAt(pos) is { } transition)
        {
            TransitionMenuRequested?.Invoke(transition);
            return;
        }
        var track = GetTrackAtY(pos.Y);
        var clip = HitTestClip(pos);
        SelectedGap? gap = null;
        if (clip == null && track != null)
        {
            var hit = HitTestAnyGap(pos);
            if (hit != null && !controlHeld)
            {
                gap = new SelectedGap { TrackId = hit.TrackId, StartSeconds = hit.StartSeconds, EndSeconds = hit.EndSeconds };
                _selection.SelectGap(gap.TrackId, gap.StartSeconds, gap.EndSeconds);
            }
        }
        if (clip != null && !_selection.IsSelected(clip.Id)) _selection.SelectClip(clip.Id);
        TimelineMenuRequested?.Invoke(clip, track, PixelsToTime(pos.X), gap);
    }

    private Func<Clip, bool> SnapFilter(string? trackId) => SnapScope switch
    {
        1 => clip => string.Equals(clip.TrackId, trackId, StringComparison.OrdinalIgnoreCase),
        2 => _ => false,
        _ => _ => true
    };

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverPointerX = null;
        PointerHoveredXChanged?.Invoke(null);
        _hoverTrimClipId = null;
        _hoverTrimSide = 0;
        _hoverRollLeftId = null;
        _hoverRollRightId = null;
        if (_isDraggingClip)
        {
            _dragDeltaSeconds = 0;
            _dragTargetTrackId = _autoCreateTrackKind = null;
            _currentSnap = default;
        }
        UpdateCursor();
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        // La liberación normal también pierde captura. Se comprueba después del evento
        // para no cancelar un recorte justo antes de que OnPointerReleased lo confirme.
        Dispatcher.UIThread.Post(() => CancelGesture());
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var pos = e.GetPosition(this);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            double factor = e.Delta.Y > 0 ? 1.25 : 0.80;
            ZoomAt(pos.X, factor);
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (pos.Y < DividerY)
                VideoScrollOffset += e.Delta.Y * 36;
            else
                AudioScrollOffset -= e.Delta.Y * 36;
            e.Handled = true;
        }
        else
        {
            double scrollDeltaSeconds = (e.Delta.Y * -30.0) / _zoomPixelsPerSecond;
            HorizontalOffsetSeconds = Math.Max(0, _horizontalOffsetSeconds + scrollDeltaSeconds);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && CancelGesture()) { e.Handled = true; return; }
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
        {
            _isCtrlHeld = true;
            UpdateCursor();
            InvalidateVisual();
        }
    }

    public bool CancelGesture()
    {
        var active = _isDraggingClip || _isTrimmingLeft || _isTrimmingRight || _isSlippingClip || _isRolling ||
            _isRangeCutting || _isDirectionalSelecting || _isMarqueeSelecting || _isHandPanning || _isMiddlePanning || _isZoomSelecting || _pendingGapClick != null;
        if (!active) return false;
        _gesturePointer?.Capture(null);
        _gesturePointer = null;
        _isDraggingClip = _isTrimmingLeft = _isTrimmingRight = _isSlippingClip = false;
        _isRolling = _isRangeCutting = _isDirectionalSelecting = _isMarqueeSelecting = _isHandPanning = _isMiddlePanning = _isZoomSelecting = false;
        _pendingGapClick = null;
        _isAltDuplicating = false;
        _repeatedClickGeneration++;
        _directionalPreviewIds.Clear();
        _directionalAxis = null;
        _directionalMoved = false;
        _directionalAdditive = false;
        _activeDragClipId = _activeRollLeftId = _activeRollRightId = null;
        _previewRollCut = _previewTrimStart = _previewTrimDuration = _previewTrimSourceIn = _previewSlipSourceIn = null;
        _draggingClipOriginalStarts.Clear();
        _draggingClipOriginalTracks.Clear();
        _dragDeltaSeconds = 0;
        _dragTargetTrackId = _autoCreateTrackKind = null;
        _currentSnap = default;
        _marqueeRect = default;
        if (_gapBeforeGesture != null) _selection.SelectGap(_gapBeforeGesture.TrackId, _gapBeforeGesture.StartSeconds, _gapBeforeGesture.EndSeconds);
        else _selection.SelectClips(_selectionBeforeGesture);
        InvalidateVisual();
        return true;
    }

    private double ClampRollCut(double requested)
    {
        var left = _sequence?.Clips.Find(c => c.Id == _activeRollLeftId);
        var right = _sequence?.Clips.Find(c => c.Id == _activeRollRightId);
        if (left == null || right == null) return _rollOriginalCutTime;
        var fps = Math.Max(1, _sequence?.Fps.Seconds ?? 60);
        var delta = Math.Round((requested - _rollOriginalCutTime) * fps) / fps;
        return _project == null ? _rollOriginalCutTime : RollEditOperations.Prepare(_project, _sequence!, left.Id, right.Id,
            _rollOriginalCutTime + delta)?.CutSeconds ?? _rollOriginalCutTime;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
        {
            _isCtrlHeld = false;
            UpdateCursor();
            InvalidateVisual();
        }
    }

    public void ZoomAt(double pivotX, double factor)
    {
        double timeAtPivot = PixelsToTime(pivotX);
        ZoomPixelsPerSecond = _zoomPixelsPerSecond * factor;
        HorizontalOffsetSeconds = Math.Max(0, timeAtPivot - (pivotX / _zoomPixelsPerSecond));
    }

    public void FitTimeRange(double start, double end)
    {
        var span = Math.Max(1.0, end - start);
        ZoomPixelsPerSecond = Math.Clamp(Math.Max(100, Bounds.Width - 40) / span, 0.1, 1000);
        HorizontalOffsetSeconds = Math.Max(0, start - 20 / ZoomPixelsPerSecond);
    }

    public void EnsurePlayheadVisible()
    {
        var width = Bounds.Width;
        if (width <= 0) return;
        var x = TimeToPixels(_playheadSeconds);
        if (x >= 0 && x <= width - 24) return;
        HorizontalOffsetSeconds = Math.Max(0, _playheadSeconds - width * 0.2 / _zoomPixelsPerSecond);
    }

    public void RippleDeleteSelectedGap()
    {
        if (_selection.CurrentGap != null && _sequence != null)
        {
            var gap = _selection.CurrentGap;
            _selection.Clear();

            CommitRequested?.Invoke(p =>
            {
                CollisionEngine.RippleDeleteGap(p.ActiveSequence!, gap.TrackId, gap.StartSeconds, gap.EndSeconds);
            }, "Cerrar espacio vacío (Ripple Delete Gap)");

            InvalidateVisual();
        }
    }

    public void DeleteSelectedClips()
    {
        if (_selection.SelectedClipIds.Count > 0 && _sequence != null)
        {
            var idsToDelete = _selection.SelectedClipIds.ToList();
            if (!CanEditClips(idsToDelete)) return;
            _selection.Clear();

            CommitRequested?.Invoke(p =>
            {
                p.ActiveSequence!.Clips.RemoveAll(c => idsToDelete.Contains(c.Id));
                foreach (var lg in p.ActiveSequence.LinkGroups.ToList())
                {
                    lg.ClipIds.RemoveAll(idsToDelete.Contains);
                    if (lg.ClipIds.Count < 2)
                        p.ActiveSequence.LinkGroups.Remove(lg);
                }
            }, "Eliminar clips seleccionados");

            InvalidateVisual();
        }
    }

    private void CommitDirectionalSelection()
    {
        if (_sequence == null) return;
        _selection.SelectClips(DirectionalClipIds(), _directionalAdditive);
    }

    private bool CanEditClips(IEnumerable<string> clipIds)
    {
        if (_sequence == null) return false;
        var ids = clipIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return !_sequence.Clips.Any(c => ids.Contains(c.Id) &&
            _sequence.Tracks.Any(t => t.Id == c.TrackId && t.Locked));
    }

    private List<string> DirectionalClipIds()
    {
        if (_sequence == null) return new List<string>();

        var videoTracks = _sequence.Tracks.Where(t => t.Kind == "video").OrderByDescending(t => t.Order).ToList();
        var audioTracks = _sequence.Tracks.Where(t => t.Kind == "audio").OrderBy(t => t.Order).ToList();
        var allTracks = videoTracks.Concat(audioTracks).ToList();

        if (string.IsNullOrEmpty(_directionalAnchorTrackId))
        {
            var nearest = allTracks.OrderBy(t => Math.Abs(GetTrackY(t.Id, videoTracks, audioTracks) + (TrackHeight(t) / 2) - _directionalAnchorPos.Y)).FirstOrDefault();
            _directionalAnchorTrackId = nearest?.Id;
        }

        return DirectionalSelectionEngine.Select(_sequence, _directionalMode, _directionalAnchorTime,
            allTracks.Select(t => t.Id).ToArray(), _directionalAnchorTrackId);
    }

    private (Clip? Clip, int Side) HitTestTrimEdge(Point pos)
    {
        if (_sequence == null) return (null, 0);
        var clip = HitTestClip(pos);
        if (clip == null || _sequence.Tracks.Any(t => t.Id == clip.TrackId && t.Locked)) return (null, 0);
        double left = TimeToPixels(clip.TimelineStart.Seconds);
        double right = TimeToPixels(clip.EndSeconds);
        double width = Math.Max(4, right - left);
        double handleWidth = Math.Min(12, width / 2);
        double leftDistance = pos.X - left;
        double rightDistance = right - pos.X;
        if (leftDistance <= handleWidth && leftDistance <= rightDistance) return (clip, 1);
        if (rightDistance <= handleWidth) return (clip, 2);
        return (null, 0);
    }

    private CollisionEngine.TimelineGapInfo? HitTestAnyGap(Point pos)
    {
        if (_sequence == null) return null;
        var track = GetTrackAtY(pos.Y);
        if (track == null || track.Locked) return null;

        double clickTime = PixelsToTime(pos.X);
        var gaps = CollisionEngine.DetectGaps(_sequence, track.Id);
        foreach (var gap in gaps)
        {
            if (clickTime >= gap.StartSeconds && clickTime <= gap.EndSeconds)
            {
                return gap;
            }
        }

        return null;
    }

    private (Clip Left, Clip Right)? HitTestRollPair(Point pos)
    {
        var track = GetTrackAtY(pos.Y);
        if (_sequence == null || track == null || track.Locked) return null;
        var bottomY = TrackTop(track.Id) + TrackBaseHeight(track) - 2;
        if (pos.Y < bottomY - 12 || pos.Y > bottomY + 4) return null;

        var near = PixelsToTime(pos.X);
        var tolerance = 10 / _zoomPixelsPerSecond;
        var trackClips = _sequence.Clips.Where(c => c.TrackId == track.Id &&
            c.TimelineStart.Seconds <= near + tolerance + 0.001 && c.EndSeconds >= near - tolerance)
            .OrderBy(c => c.TimelineStart.Seconds).ToArray();
        for (var index = 0; index < trackClips.Length - 1; index++)
        {
            var left = trackClips[index];
            var right = trackClips[index + 1];
            if (CanShowRoll(left, right) && Math.Abs(left.EndSeconds - right.TimelineStart.Seconds) <= 0.001 &&
                Math.Abs(pos.X - TimeToPixels(left.EndSeconds)) <= 10)
                return (left, right);
        }
        return null;
    }

    private Clip? HitTestClip(Point pos)
    {
        if (_sequence == null)
            return null;

        var videoTracks = _sequence.Tracks.Where(t => t.Kind == "video").OrderByDescending(t => t.Order).ToList();
        var audioTracks = _sequence.Tracks.Where(t => t.Kind == "audio").OrderBy(t => t.Order).ToList();

        foreach (var clip in _sequence.Clips)
        {
            double trackY = GetTrackY(clip.TrackId, videoTracks, audioTracks);
            // La cabecera de una pista puede quedar fuera del visor mientras parte
            // del clip sigue visible. Esa parte debe conservar el hit test.

            double clipX = TimeToPixels(clip.TimelineStart.Seconds);
            double clipW = clip.Duration.Seconds * _zoomPixelsPerSecond;

            var track = _sequence.Tracks.FirstOrDefault(t => t.Id == clip.TrackId);
            if (track == null || !TrackViewport(track.Kind).Contains(pos)) continue;
            var clipRect = new Rect(clipX, trackY + 2, Math.Max(4, clipW), TrackBaseHeight(track) - 4);
            if (clipRect.Contains(pos))
            {
                return clip;
            }
        }

        return null;
    }

    private MicroGapInfo? HitTestMicroGap(Point pos)
    {
        if (_sequence == null)
            return null;

        var gaps = CollisionEngine.DetectMicroGaps(_sequence);
        var videoTracks = _sequence.Tracks.Where(t => t.Kind == "video").OrderByDescending(t => t.Order).ToList();
        var audioTracks = _sequence.Tracks.Where(t => t.Kind == "audio").OrderBy(t => t.Order).ToList();

        foreach (var gap in gaps)
        {
            double trackY = GetTrackY(gap.TrackId, videoTracks, audioTracks);

            double startX = TimeToPixels(gap.StartSeconds);
            double endX = TimeToPixels(gap.EndSeconds);
            double width = Math.Max(8, endX - startX);

            var track = _sequence.Tracks.FirstOrDefault(t => t.Id == gap.TrackId);
            if (track == null || !TrackViewport(track.Kind).Contains(pos)) continue;
            var gapRect = new Rect(startX - 2, trackY, width + 4, TrackBaseHeight(track));
            if (gapRect.Contains(pos))
            {
                return gap;
            }
        }

        return null;
    }

    private List<Clip> GetClipsIntersectingRect(Rect rect)
    {
        var result = new List<Clip>();
        if (_sequence == null)
            return result;

        var videoTracks = _sequence.Tracks.Where(t => t.Kind == "video").OrderByDescending(t => t.Order).ToList();
        var audioTracks = _sequence.Tracks.Where(t => t.Kind == "audio").OrderBy(t => t.Order).ToList();

        foreach (var clip in _sequence.Clips)
        {
            double trackY = GetTrackY(clip.TrackId, videoTracks, audioTracks);
            var track = _sequence.Tracks.FirstOrDefault(t => t.Id == clip.TrackId);
            if (track == null || !rect.Intersects(TrackViewport(track.Kind))) continue;

            double clipX = TimeToPixels(clip.TimelineStart.Seconds);
            double clipW = clip.Duration.Seconds * _zoomPixelsPerSecond;
            var clipRect = new Rect(clipX, trackY + 2, Math.Max(4, clipW), TrackBaseHeight(track) - 4);

            if (rect.Intersects(clipRect) && clipRect.Intersects(TrackViewport(track.Kind)))
            {
                result.Add(clip);
            }
        }

        return result;
    }
}
