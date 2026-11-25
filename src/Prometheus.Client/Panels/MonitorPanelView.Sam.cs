using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class MonitorPanelView
{
    private Sam2Bridge? _samBridge;
    private CancellationTokenSource? _samDelay;
    private readonly SemaphoreSlim _samInteractionGate = new(1, 1);
    private WriteableBitmap? _samMaskBitmap;
    private string? _samPreparedKey;
    private int _samPreparedWidth;
    private int _samPreparedHeight;
    private long _samRequest;
    private Sam2MaskFrame? _samPendingMask;
    private ShapeMask? _pendingShapeMask;
    private string? _samPendingKey;
    private double _samPendingSourceSeconds;
    private string? _samPendingClipId;
    private sealed class SamSelectionRegion(double x, double y, Sam2MaskFrame mask)
    {
        public double X { get; } = x;
        public double Y { get; } = y;
        public Sam2MaskFrame Mask { get; set; } = mask;
        public List<(double X, double Y)> NegativePoints { get; } = new();
    }
    private readonly List<SamSelectionRegion> _samRegions = new();
    private string? _samRegionsKey;
    private CancellationTokenSource? _samTrackingCancellation;
    private bool SamToolEnabled => ProjectController.Instance?.SamToolEnabled == true;

    private void SetupSamSelection()
    {
        if (ProjectController.Instance is { } controller)
            controller.SamToolChanged += OnSamToolChanged;
        ViewportHost.AddHandler(PointerPressedEvent, OnSamMaskPressed, RoutingStrategies.Tunnel, true);
        SamApplyButton.Click += (_, _) => ApplySamMask();
        SamRemoveButton.Click += (_, _) => RemoveSamMask();
        SamTrackButton.Click += (_, _) => StartSamTracking();
        SamCancelTrackButton.Click += (_, _) => _samTrackingCancellation?.Cancel();
        SamRemoveTrackButton.Click += (_, _) => RemoveSamTrack();
    }

    private void OnSamToolChanged()
    {
        ClearSamMaskOverlay();
        CancelSamPreparation();
        ++_samRequest;
        if (!SamToolEnabled || !_attached)
        {
            if (_attached && ShapeToolEnabled) RefreshShapeOutline();
            return;
        }
        if (!Sam2Bridge.IsAvailable)
        {
            ProjectController.Instance!.SamToolEnabled = false;
            ProjectController.Instance.ReportStatus("No se encontró el motor local SAM 2.1");
            return;
        }
        _samBridge ??= new Sam2Bridge();
        _ = WarmSamAsync(_samBridge);
        ScheduleSamPreparation();
        ProjectController.Instance?.ReportStatus("SAM: clic selecciona · Mayús+clic añade · Alt+clic resta · clic derecho abre acciones");
    }

    private void CancelSamPreparation()
    {
        var previous = _samDelay;
        _samDelay = null;
        previous?.Cancel();
        // The awaiting task owns Dispose; a cancelled source must never remain in _samDelay.
    }

    private async Task WarmSamAsync(Sam2Bridge bridge)
    {
        try { await bridge.WarmupAsync(); }
        catch (Exception ex)
        {
            if (_attached && SamToolEnabled)
                ProjectController.Instance?.ReportStatus("SAM: " + ex.Message);
        }
    }

    private (NestedRenderLayer Layer, GpuVideoSurface Gpu, string Key)? VisibleSamSource()
    {
        var controller = ProjectController.Instance;
        var project = controller?.CurrentProject;
        var clipId = controller?.SelectedClipId;
        if (project == null || clipId == null || _compositeIncomplete ||
            Math.Abs(_displayedTime - _requestedTime) > 0.05) return null;
        var layer = SubtitleRenderPlan.At(project, _displayedTime, "video")
            .FirstOrDefault(item => item.Clip.Id == clipId);
        var gpu = _nestedLayers.LastOrDefault(item => item.ClipId == clipId && !item.Failed)?.Gpu;
        if (layer == null || gpu == null) return null;
        var key = project.Id + ":" + project.Revision + ":" + clipId + ":" +
            Math.Round(layer.SourceSeconds, 3).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        return (layer, gpu, key);
    }

    private void ScheduleSamPreparation()
    {
        CancelSamPreparation();
        if (!_attached || !SamToolEnabled || _transport?.IsPlaying == true) return;
        var source = VisibleSamSource();
        if (source == null || source.Value.Key == _samPreparedKey) return;
        var delay = new CancellationTokenSource();
        _samDelay = delay;
        var request = ++_samRequest;
        _ = PrepareSamAfterPauseAsync(request, delay);
    }

    private async Task PrepareSamAfterPauseAsync(long request, CancellationTokenSource delay)
    {
        var cancellation = delay.Token;
        try
        {
            await Task.Delay(220, cancellation);
            if (request != _samRequest || cancellation.IsCancellationRequested) return;
            var source = VisibleSamSource();
            if (source == null || _samBridge == null) return;
            await _samInteractionGate.WaitAsync(cancellation);
            try
            {
                if (source.Value.Key == _samPreparedKey || cancellation.IsCancellationRequested) return;
                var frame = source.Value.Gpu.CaptureAnalysisFrame();
                await _samBridge.PrepareAsync(frame.Width, frame.Height, frame.Bgra);
                if (request != _samRequest || cancellation.IsCancellationRequested ||
                    !SamToolEnabled) return;
                _samPreparedKey = source.Value.Key;
                _samPreparedWidth = frame.Width;
                _samPreparedHeight = frame.Height;
            }
            finally { _samInteractionGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_attached && SamToolEnabled)
                ProjectController.Instance?.ReportStatus("SAM: " + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_samDelay, delay)) _samDelay = null;
            delay.Dispose();
        }
    }

    private void OnSamMaskPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!SamToolEnabled) return;
        var pressed = e.GetCurrentPoint(ViewportHost).Properties;
        if (!pressed.IsLeftButtonPressed && !pressed.IsRightButtonPressed) return;
        e.Handled = true;
        if (_transport?.IsPlaying == true)
        {
            ProjectController.Instance?.ReportStatus("Pausa el montaje para seleccionar con SAM");
            return;
        }
        var source = VisibleSamSource();
        if (source == null)
        {
            ProjectController.Instance?.ReportStatus("Selecciona un clip de vídeo y espera a que aparezca su fotograma");
            return;
        }
        if (pressed.IsRightButtonPressed) return;
        var state = DynamicMotionEffect.Evaluate(source.Value.Layer.Clip,
            source.Value.Layer.LocalTimelineSeconds);
        var rect = PreviewGeometry.PlaceLayer(SequenceFrame.Width, SequenceFrame.Height,
            source.Value.Gpu.Width, source.Value.Gpu.Height, state);
        var point = e.GetPosition(SequenceFrame);
        var mapped = PreviewGeometry.MapDisplayPointToSource(rect, state, point.X, point.Y);
        if (mapped == null)
        {
            ProjectController.Instance?.ReportStatus("Haz clic dentro del clip seleccionado");
            return;
        }
        CancelSamPreparation();
        ++_samRequest;
        var negative = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !negative;
        _ = SelectSamMaskAsync(source.Value.Key, mapped.Value.X, mapped.Value.Y,
            rect, additive, negative);
    }

    private async Task SelectSamMaskAsync(string key, double normalizedX, double normalizedY,
        PreviewRectangle rect, bool additive, bool negative)
    {
        if (_samBridge == null) return;
        try
        {
            await _samInteractionGate.WaitAsync();
            try
            {
                var source = VisibleSamSource();
                if (source == null || source.Value.Key != key) return;
                if (_samPreparedKey != key)
                {
                    ProjectController.Instance?.ReportStatus("SAM: preparando el fotograma…");
                    var frame = source.Value.Gpu.CaptureAnalysisFrame();
                    await _samBridge.PrepareAsync(frame.Width, frame.Height, frame.Bgra);
                    _samPreparedKey = key;
                    _samPreparedWidth = frame.Width;
                    _samPreparedHeight = frame.Height;
                }
                if (_samRegionsKey != key)
                {
                    _samRegions.Clear();
                    _samRegionsKey = key;
                }
                if (!additive && !negative) _samRegions.Clear();
                if (negative && _samRegions.Count == 0)
                {
                    ProjectController.Instance?.ReportStatus("SAM: selecciona primero el objeto que quieres pulir");
                    return;
                }
                Sam2MaskFrame mask;
                if (negative)
                {
                    var px = Math.Clamp((int)(normalizedX * _samPreparedWidth), 0, _samPreparedWidth - 1);
                    var py = Math.Clamp((int)(normalizedY * _samPreparedHeight), 0, _samPreparedHeight - 1);
                    var region = _samRegions.LastOrDefault(item =>
                        item.Mask.Alpha[py * item.Mask.Width + px] > 127) ?? _samRegions[^1];
                    region.NegativePoints.Add((normalizedX, normalizedY));
                    var prompts = new[] { (region.X * _samPreparedWidth,
                        region.Y * _samPreparedHeight, true) }
                        .Concat(region.NegativePoints.Select(p =>
                            (p.X * _samPreparedWidth, p.Y * _samPreparedHeight, false))).ToArray();
                    region.Mask = await _samBridge.PredictAsync(prompts);
                }
                else
                {
                    mask = await _samBridge.PredictAsync([
                        (normalizedX * _samPreparedWidth, normalizedY * _samPreparedHeight, true)]);
                    _samRegions.Add(new SamSelectionRegion(normalizedX, normalizedY, mask));
                }
                var union = new byte[checked(_samPreparedWidth * _samPreparedHeight)];
                foreach (var region in _samRegions)
                    for (var i = 0; i < union.Length; i++)
                        if (region.Mask.Alpha[i] > union[i]) union[i] = region.Mask.Alpha[i];
                mask = new Sam2MaskFrame(_samPreparedWidth, _samPreparedHeight, union,
                    _samRegions.Max(region => region.Mask.Score));
                if (VisibleSamSource()?.Key != key) return;
                if (!SamToolEnabled) return;
                ShowSamMask(mask, rect, key, source.Value.Layer);
                ProjectController.Instance?.ReportStatus(
                    "SAM: selección lista · pulsa Aplicar máscara para guardarla en este fotograma");
            }
            finally { _samInteractionGate.Release(); }
        }
        catch (Exception ex) { ProjectController.Instance?.ReportStatus("SAM: " + ex.Message); }
    }

    private void ShowSamMask(Sam2MaskFrame mask, PreviewRectangle rect, string key,
        NestedRenderLayer layer)
    {
        var bitmap = new WriteableBitmap(new PixelSize(mask.Width, mask.Height),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var row = new byte[mask.Width * 4];
        using (var locked = bitmap.Lock())
        {
            for (var y = 0; y < mask.Height; y++)
            {
                for (var x = 0; x < mask.Width; x++)
                {
                    var alpha = mask.Alpha[y * mask.Width + x] * 92 / 255;
                    var at = x * 4;
                    row[at] = 0;
                    row[at + 1] = (byte)(alpha * 156 / 255);
                    row[at + 2] = (byte)alpha;
                    row[at + 3] = (byte)alpha;
                }
                Marshal.Copy(row, 0, IntPtr.Add(locked.Address, y * locked.RowBytes), row.Length);
            }
        }
        SamMaskOverlay.Source = bitmap;
        _samMaskBitmap?.Dispose();
        _samMaskBitmap = bitmap;
        SamMaskOverlay.Width = rect.Width;
        SamMaskOverlay.Height = rect.Height;
        Canvas.SetLeft(SamMaskOverlay, rect.X);
        Canvas.SetTop(SamMaskOverlay, rect.Y);
        SamMaskCanvas.IsVisible = true;
        _samPendingMask = mask;
        _samPendingKey = key;
        _samPendingClipId = layer.Clip.Id;
        _samPendingSourceSeconds = layer.SourceSeconds;
        SamApplyButton.IsVisible = true;
        SamApplyButton.SetValue(ToolTip.TipProperty,
            StillImageRules.IsStill(ProjectController.Instance?.CurrentProject, layer.Clip)
                ? "Guardar la máscara para toda la duración de esta imagen fija."
                : "Guardar la máscara de este fotograma en el proyecto y la exportación.");
    }

    private IReadOnlyList<MenuItem>? SamMaskMenuItems()
    {
        var source = VisibleSamSource();
        if (_transport?.IsPlaying == true || source == null) return null;
        var (layer, _, key) = source.Value;
        var controller = ProjectController.Instance;
        var sequence = controller?.CurrentProject.ActiveSequence;
        var clip = sequence?.Clips.FirstOrDefault(item => item.Id == layer.Clip.Id);
        if (controller == null || clip == null || layer.Track.Locked) return null;
        var saved = ClipMaskStack.Read(clip).Where(item => item.Kind == "sam").ToArray();
        var mask = saved.FirstOrDefault(item => item.Id == controller.SelectedMaskId) ?? saved.LastOrDefault();
        var hasPending = _samPendingMask != null && _samPendingClipId == clip.Id && _samPendingKey == key;
        if (!hasPending && mask == null)
        {
            controller.ReportStatus("SAM: selecciona un objeto para asignar su máscara.");
            return null;
        }

        MenuItem Target(string label, string target)
        {
            var item = new MenuItem { Header = mask?.Target == target ? "✓ " + label : label };
            item.Click += (_, _) =>
            {
                if (hasPending) { ApplySamMask(target); return; }
                controller.UndoRedo.Commit(project =>
                {
                    var active = project.ActiveSequence;
                    var current = active?.Clips.FirstOrDefault(value => value.Id == clip.Id);
                    if (current == null || active!.Tracks.Any(track => track.Id == current.TrackId && track.Locked))
                        return;
                    var currentMask = ClipMaskStack.Edit(current).FirstOrDefault(value => value.Id == mask!.Id);
                    if (currentMask != null) currentMask.Target = target;
                }, "Asignar máscara SAM");
                controller.SelectMask(mask!.Id);
            };
            return item;
        }

        var targets = new List<MenuItem> { Target("Sin asignar", SamMaskTarget.Unassigned),
            Target("Opacidad", SamMaskTarget.Opacity) };
        targets.AddRange(clip.Effects.Where(SamMaskTarget.IsCompatible).Select(effect =>
            Target(EffectCatalog.Available.FirstOrDefault(item => item.Id == effect.EffectId)?.Name ?? effect.EffectId,
                SamMaskTarget.ForEffect(effect))));
        var actions = new List<MenuItem> { new() { Header = "Asignar máscara a", ItemsSource = targets } };
        var seed = mask == null ? null : SamMaskKeyframe.At(new Clip { SamMasks = mask.SamMasks },
            layer.SourceSeconds, sequence!.Fps.Seconds);
        if ((hasPending || seed != null) && !StillImageRules.IsStill(controller.CurrentProject, clip) &&
            _samTrackingCancellation == null)
        {
            var canTrack = Sam2VideoBridge.IsAvailable && controller.CurrentProjectPath != null;
            var follow = new MenuItem { Header = "Seguir objeto", IsEnabled = canTrack };
            if (!canTrack) ToolTip.SetTip(follow, controller.CurrentProjectPath == null
                ? "Guarda el proyecto para seguir el movimiento"
                : "El motor local SAM 2.1 no está disponible");
            follow.Click += (_, _) =>
            {
                if (hasPending && !ApplySamMask()) return;
                if (!hasPending) controller.SelectMask(mask!.Id);
                StartSamTracking();
            };
            actions.Add(follow);
        }
        return actions;
    }

    private bool ApplySamMask(string? target = null)
    {
        var controller = ProjectController.Instance;
        var source = VisibleSamSource();
        if (controller == null || source == null || _samPendingMask == null ||
            source.Value.Key != _samPendingKey || source.Value.Layer.Clip.Id != _samPendingClipId)
        {
            ClearSamMaskOverlay();
            controller?.ReportStatus("SAM: vuelve a seleccionar el fotograma antes de aplicar");
            return false;
        }
        var mask = SamMaskKeyframe.Create(_samPendingSourceSeconds,
            _samPendingMask.Width, _samPendingMask.Height, _samPendingMask.Alpha,
            _samRegions.Select(region => region.Mask.Alpha));
        var shape = _pendingShapeMask;
        var editingShapeLayerId = _editingShapeLayerId;
        var shapeSourceSeconds = ShapeSourceSeconds(source.Value.Layer);
        var clipId = _samPendingClipId!;
        var fps = controller.CurrentProject.ActiveSequence?.Fps.Seconds ?? 60;
        var still = StillImageRules.IsStill(controller.CurrentProject, source.Value.Layer.Clip);
        if (source.Value.Layer.Track.Locked || !controller.CurrentProject.ActiveSequence!.Clips.Any(c => c.Id == clipId))
        {
            controller.ReportStatus(source.Value.Layer.Track.Locked ? "SAM: desbloquea la pista para editar la máscara" :
                "SAM: la máscara de clips anidados aún no se puede guardar");
            return false;
        }
        ClearSamMaskOverlay();
        string? selectedLayerId = null;
        controller.UndoRedo.Commit(project =>
        {
            var clip = project.ActiveSequence!.Clips.First(c => c.Id == clipId);
            var layers = ClipMaskStack.Edit(clip);
            if (shape != null)
            {
                var existing = layers.FirstOrDefault(item => item.Id == editingShapeLayerId &&
                    item.Kind == "shape");
                if (existing != null)
                {
                    if (existing.ShapeKeyframes.Count > 0)
                        ShapeMaskAnimation.Upsert(existing, shapeSourceSeconds, fps, shape);
                    else existing.Shape = shape;
                    selectedLayerId = existing.Id;
                }
                else
                {
                    var layer = new ClipMaskLayer { Kind = "shape", Shape = shape,
                        Name = shape.Kind switch { "ellipse" => "Elipse", "polygon" => "Pluma",
                            _ => "Rectángulo" } + " " + (layers.Count + 1),
                        Target = layers.FirstOrDefault()?.Target ?? clip.SamMaskTarget };
                    layers.Add(layer);
                    selectedLayerId = layer.Id;
                }
                return;
            }
            var selected = layers.FirstOrDefault(item => item.Id == controller.SelectedMaskId &&
                item.Kind == "sam");
            if (selected == null)
            {
                selected = new ClipMaskLayer { Kind = "sam", Name = "SAM " + (layers.Count + 1),
                    Target = layers.FirstOrDefault()?.Target ?? clip.SamMaskTarget };
                layers.Add(selected);
            }
            if (target != null) selected.Target = target;
            selected.SamMasks.RemoveAll(existing => still ||
                Math.Abs(existing.SourceSeconds - mask.SourceSeconds) <=
                0.5 / Math.Max(1, fps) + 0.0001);
            selected.SamMasks.Add(mask);
            selectedLayerId = selected.Id;
        }, "Aplicar máscara SAM");
        controller.SelectMask(selectedLayerId);
        controller.ReportStatus(shape != null ? "Máscara de forma aplicada" :
            still ? "SAM: máscara aplicada a toda la imagen" :
            "SAM: máscara guardada en el proyecto para este fotograma");
        return true;
    }

    private void UpdateSamRemoveButton()
    {
        var source = VisibleSamSource();
        var controller = ProjectController.Instance;
        var clip = source == null ? null : controller?.CurrentProject.ActiveSequence?.Clips
            .FirstOrDefault(item => item.Id == source.Value.Layer.Clip.Id);
        var layer = clip == null ? null : (ClipMaskStack.Read(clip)
            .FirstOrDefault(item => item.Id == controller?.SelectedMaskId) ??
            ClipMaskStack.Read(clip).LastOrDefault());
        var still = clip != null && StillImageRules.IsStill(controller!.CurrentProject, clip);
        var proxy = layer == null ? null : new Clip { SamMasks = layer.SamMasks };
        var manual = proxy == null || source == null ? null : still
            ? proxy.SamMasks.LastOrDefault() : SamMaskKeyframe.At(proxy,
                source.Value.Layer.SourceSeconds,
                controller!.CurrentProject.ActiveSequence!.Fps.Seconds);
        var shape = layer?.Shape;
        SamRemoveButton.IsVisible = manual != null || shape != null;
        SamRemoveButton.SetValue(ToolTip.TipProperty, shape != null
            ? "Quitar la máscara de forma de todo el clip. Se puede deshacer."
            : still ? "Quitar la máscara de toda la imagen fija. Se puede deshacer."
            : "Quitar la máscara de este fotograma. Se puede deshacer.");
        SamTrackButton.IsVisible = !still && shape == null && manual != null &&
            _samTrackingCancellation == null;
        var path = controller?.CurrentProject.Assets.FirstOrDefault(asset =>
            asset.Id == clip?.AssetId)?.EffectiveLocator?.Path;
        SamTrackButton.Content = clip != null &&
            layer?.SamMaskTracks.LastOrDefault() is { } existing &&
            SamMaskTrackValidity.Problem(clip, existing, path) == null
            ? "Corregir tramo" : "Seguir movimiento";
        SamCancelTrackButton.IsVisible = _samTrackingCancellation != null;
        SamRemoveTrackButton.IsVisible = shape == null && layer?.SamMaskTracks.Count > 0 &&
            _samTrackingCancellation == null;
        RefreshShapeOutline();
    }

    private void StartSamTracking()
    {
        var controller = ProjectController.Instance;
        var source = VisibleSamSource();
        if (_samTrackingCancellation != null || controller == null || source == null) return;
        if (StillImageRules.IsStill(controller.CurrentProject, source.Value.Layer.Clip))
        {
            controller.ReportStatus("La máscara de esta imagen fija se aplica a todo el clip");
            return;
        }
        if (!Sam2VideoBridge.IsAvailable)
        {
            controller.ReportStatus("No se encontró el motor de seguimiento SAM 2.1");
            return;
        }
        if (controller.CurrentProjectPath is not { } projectPath)
        {
            controller.ReportStatus("Guarda el proyecto antes de seguir el movimiento con SAM");
            return;
        }
        var clip = controller.CurrentProject.ActiveSequence?.Clips
            .FirstOrDefault(item => item.Id == source.Value.Layer.Clip.Id);
        var layer = clip == null ? null : (ClipMaskStack.Read(clip)
            .FirstOrDefault(item => item.Id == controller.SelectedMaskId) ??
            ClipMaskStack.Read(clip).LastOrDefault(item => item.Kind == "sam"));
        if (layer != null) controller.SelectMask(layer.Id);
        var seed = layer == null ? null : SamMaskKeyframe.At(
            new Clip { SamMasks = layer.SamMasks },
            source.Value.Layer.SourceSeconds,
            controller.CurrentProject.ActiveSequence!.Fps.Seconds);
        var assetPath = clip == null ? null : controller.CurrentProject.Assets
            .FirstOrDefault(item => item.Id == clip.AssetId)?.EffectiveLocator?.Path;
        if (clip == null || layer?.Kind != "sam" || seed == null ||
            string.IsNullOrWhiteSpace(assetPath) ||
            !System.IO.File.Exists(assetPath))
        {
            controller.ReportStatus("Aplica primero una máscara sobre el clip de vídeo seleccionado");
            return;
        }
        projectPath = controller.ResourceProjectPath ?? projectPath;
        var snapshot = ProjectStore.Clone(controller.CurrentProject);
        var snapshotClip = snapshot.ActiveSequence!.Clips.First(item => item.Id == clip.Id);
        var snapshotLayer = ClipMaskStack.Read(snapshotClip)
            .First(item => item.Id == layer.Id);
        snapshotClip.SamMasks = snapshotLayer.SamMasks;
        snapshotClip.SamMaskTracks = snapshotLayer.SamMaskTracks;
        snapshotClip.ShapeMask = null;
        var cancellation = new CancellationTokenSource();
        _samTrackingCancellation = cancellation;
        UpdateSamRemoveButton();
        controller.ReportStatus("SAM: iniciando seguimiento en segundo plano…");
        _ = TrackSamMotionAsync(snapshot, clip.Id, layer.Id, assetPath, projectPath,
            source.Value.Layer.SourceSeconds, seed, cancellation);
    }

    private async Task TrackSamMotionAsync(Project snapshot, string clipId, string maskLayerId,
        string assetPath,
        string projectPath, double sourceSeconds, SamMaskKeyframe seed,
        CancellationTokenSource cancellation)
    {
        var progress = new Progress<(int Done, int Total)>(value =>
        {
            if (!cancellation.IsCancellationRequested &&
                ProjectController.Instance?.CurrentProject.Id == snapshot.Id)
                ProjectController.Instance.ReportStatus(
                    $"SAM: seguimiento {value.Done}/{value.Total} fotogramas");
        });
        try
        {
            var existing = snapshot.ActiveSequence?.Clips.FirstOrDefault(item => item.Id == clipId)
                ?.SamMaskTracks.LastOrDefault();
            var repairing = existing != null &&
                SamTrackingService.CanRepair(snapshot.ActiveSequence!.Clips.First(item => item.Id == clipId),
                    existing, sourceSeconds) &&
                SamMaskTrackValidity.Problem(snapshot.ActiveSequence.Clips.First(item => item.Id == clipId),
                    existing, assetPath) == null &&
                System.IO.File.Exists(SamMaskTrackStore.PathFor(projectPath, existing.Id));
            var track = await Task.Run(() => repairing
                ? SamTrackingService.Repair(snapshot, clipId, assetPath, projectPath,
                    sourceSeconds, seed, existing!, progress, cancellation.Token)
                : SamTrackingService.Track(snapshot, clipId, assetPath, projectPath,
                    sourceSeconds, seed, progress, cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
            var controller = ProjectController.Instance;
            var oldClip = snapshot.ActiveSequence?.Clips.FirstOrDefault(item => item.Id == clipId);
            var currentClip = controller?.CurrentProject.ActiveSequence?.Clips
                .FirstOrDefault(item => item.Id == clipId);
            if (!_attached || controller == null || controller.CurrentProject.Id != snapshot.Id ||
                controller.CurrentProject.ActiveSequenceId != snapshot.ActiveSequenceId ||
                currentClip == null || oldClip == null || currentClip.AssetId != oldClip.AssetId ||
                currentClip.SourceIn != oldClip.SourceIn || currentClip.Duration != oldClip.Duration ||
                currentClip.Speed != oldClip.Speed)
                throw new InvalidOperationException("El clip cambió durante SAM. Inicia de nuevo el seguimiento.");
            if (!ClipMaskStack.Read(currentClip).Any(layer => layer.Id == maskLayerId &&
                layer.Kind == "sam"))
                throw new InvalidOperationException("La máscara seleccionada cambió durante SAM.");
            if (controller.CurrentProjectPath is not { } currentPath)
                throw new InvalidOperationException("Guarda el proyecto antes de aplicar el seguimiento.");
            currentPath = controller.ResourceProjectPath ?? currentPath;
            SamMaskTrackStore.CopyOne(projectPath, currentPath, track.Id);
            controller.UndoRedo.Commit(project =>
            {
                var target = project.ActiveSequence!.Clips.First(item => item.Id == clipId);
                var layer = ClipMaskStack.Edit(target).First(item => item.Id == maskLayerId);
                layer.SamMaskTracks.Clear();
                layer.SamMaskTracks.Add(track);
            }, "Seguir máscara SAM");
            controller.ReportStatus(repairing
                ? "SAM: tramo corregido · seguimiento conservado"
                : $"SAM: seguimiento listo · {track.FrameCount} fotogramas");
        }
        catch (OperationCanceledException)
        {
            ProjectController.Instance?.ReportStatus("SAM: seguimiento cancelado");
        }
        catch (Exception ex)
        {
            ProjectController.Instance?.ReportStatus("SAM: " + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_samTrackingCancellation, cancellation))
                _samTrackingCancellation = null;
            cancellation.Dispose();
            if (_attached) UpdateSamRemoveButton();
        }
    }

    private void RemoveSamTrack()
    {
        var controller = ProjectController.Instance;
        var source = VisibleSamSource();
        if (controller == null || source == null) return;
        var clipId = source.Value.Layer.Clip.Id;
        var selectedId = controller.SelectedMaskId ?? ClipMaskStack.Read(source.Value.Layer.Clip)
            .LastOrDefault(layer => layer.Kind == "sam")?.Id;
        if (selectedId == null) return;
        controller.UndoRedo.Commit(project =>
            ClipMaskStack.Edit(project.ActiveSequence!.Clips.First(clip => clip.Id == clipId))
                .First(layer => layer.Id == selectedId).SamMaskTracks.Clear(),
            "Quitar seguimiento SAM");
        controller.ReportStatus("SAM: seguimiento quitado del clip");
    }

    private void RemoveSamMask()
    {
        var controller = ProjectController.Instance;
        var source = VisibleSamSource();
        if (controller == null || source == null) return;
        var clipId = source.Value.Layer.Clip.Id;
        var seconds = source.Value.Layer.SourceSeconds;
        var fps = controller.CurrentProject.ActiveSequence?.Fps.Seconds ?? 60;
        var still = StillImageRules.IsStill(controller.CurrentProject, source.Value.Layer.Clip);
        var selectedId = controller.SelectedMaskId ?? ClipMaskStack.Read(source.Value.Layer.Clip)
            .LastOrDefault()?.Id;
        var selected = ClipMaskStack.Read(source.Value.Layer.Clip)
            .FirstOrDefault(layer => layer.Id == selectedId);
        if (selected == null) return;
        var hasShape = selected.Kind == "shape";
        if (!hasShape && !(still ? selected.SamMasks.Count > 0 :
            SamMaskKeyframe.At(new Clip { SamMasks = selected.SamMasks }, seconds, fps) != null)) return;
        controller.UndoRedo.Commit(project =>
        {
            var clip = project.ActiveSequence!.Clips.First(c => c.Id == clipId);
            var layers = ClipMaskStack.Edit(clip);
            var layer = layers.First(item => item.Id == selectedId);
            if (layer.Kind == "shape") { layers.Remove(layer); return; }
            layer.SamMasks.RemoveAll(mask => still || Math.Abs(mask.SourceSeconds - seconds) <=
                0.5 / Math.Max(1, fps) + 0.0001);
            if (layer.SamMasks.Count == 0 && layer.SamMaskTracks.Count == 0)
                layers.Remove(layer);
        }, "Quitar máscara SAM");
        controller.ReportStatus(hasShape ? "Máscara de forma quitada del clip" :
            still ? "SAM: máscara quitada de la imagen" :
            "SAM: máscara quitada de este fotograma");
    }

    private void ClearSamMaskOverlay()
    {
        if (SamMaskCanvas == null) return;
        SamMaskCanvas.IsVisible = false;
        SamMaskOverlay.Source = null;
        _samMaskBitmap?.Dispose();
        _samMaskBitmap = null;
        _samPendingMask = null;
        _pendingShapeMask = null;
        _samPendingKey = null;
        _samPendingClipId = null;
        _samRegions.Clear();
        _samRegionsKey = null;
        SamApplyButton.IsVisible = false;
        ClearShapeDraft();
    }

    private void StopSamSelection()
    {
        _samTrackingCancellation?.Cancel();
        CancelSamPreparation();
        ++_samRequest;
        ClearSamMaskOverlay();
        var bridge = _samBridge;
        _samBridge = null;
        _samPreparedKey = null;
        _samPreparedWidth = _samPreparedHeight = 0;
        if (bridge != null) _ = bridge.DisposeAsync();
    }
}
