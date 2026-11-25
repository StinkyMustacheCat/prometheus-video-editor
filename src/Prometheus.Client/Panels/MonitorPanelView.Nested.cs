using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class MonitorPanelView
{
    private sealed class NestedLayer : IDisposable
    {
        public required string Path { get; init; }
        public required string ClipId { get; init; }
        public required int PreviewEdge { get; init; }
        public bool FrameLocked { get; init; }
        public required DateTime SourceWriteTimeUtc { get; init; }
        public required Control Host { get; init; }
        public required int SourceIndex { get; set; }
        public GpuVideoSurface? Gpu { get; init; }
        public MgtLiveGpuSurface? Mgt { get; set; }
        public CompositionDrawingSurface? Drawing { get; init; }
        public CompositionSurfaceVisual? Visual { get; init; }
        public bool Failed { get; set; }
        public DateTime? NotReadySince { get; set; }

        public void Fit(ClipVisualState state)
        {
            var surface = Host.Parent as Control;
            if (surface == null) return;
            var areaW = surface.Bounds.Width;
            var areaH = surface.Bounds.Height;
            var sourceW = Gpu?.Width ?? Mgt?.Width ?? 1;
            var sourceH = Gpu?.Height ?? Mgt?.Height ?? 1;
            if (areaW <= 0 || areaH <= 0 || sourceW <= 0 || sourceH <= 0) return;
            var rect = PreviewGeometry.PlaceLayer(areaW, areaH, sourceW, sourceH, state);
            var width = rect.Width;
            var height = rect.Height;
            var left = rect.X;
            var top = rect.Y;
            Host.Opacity = Math.Clamp(state.Opacity, 0, 1);
            if (Visual != null)
            {
                var anchorX = state.FlipX ? 1 - state.AnchorX : state.AnchorX;
                var anchorY = state.FlipY ? 1 - state.AnchorY : state.AnchorY;
                var centerToAnchorX = (anchorX - 0.5) * width;
                var centerToAnchorY = (anchorY - 0.5) * height;
                var rotated = PreviewGeometry.RotateDelta(centerToAnchorX, centerToAnchorY,
                    state.RotationDegrees);
                Visual.Size = new(width, height);
                Visual.Offset = new Vector3D(left + centerToAnchorX - rotated.X,
                    top + centerToAnchorY - rotated.Y, 0);
                Visual.CenterPoint = new Vector3D(width / 2, height / 2, 0);
                Visual.Scale = new Vector3D(state.FlipX ? -1 : 1, state.FlipY ? 1 : -1, 1);
                Visual.RotationAngle = (float)(state.RotationDegrees * Math.PI / 180);
            }
        }

        public void Dispose()
        {
            ElementComposition.SetElementChildVisual(Host, null);
            Gpu?.Dispose();
            Mgt?.Dispose();
            Drawing?.Dispose();
        }
    }

    private readonly List<NestedLayer> _nestedLayers = new();
    private readonly List<(string ClipId, string Path, int Edge, DateTime SourceWriteTimeUtc,
        MgtLiveGpuSurface Surface)> _idleMgtScenes = new();
    private readonly Dictionary<string, DateTime> _sourceRetryAfter = new(StringComparer.Ordinal);
    private string? _nestedSignature;

    private void RetainMgtScene(NestedLayer layer)
    {
        if (layer.Mgt == null || layer.Failed) return;
        var surface = layer.Mgt;
        layer.Mgt = null;
        surface.Detach();
        _idleMgtScenes.RemoveAll(item =>
        {
            if (item.ClipId != layer.ClipId) return false;
            item.Surface.Dispose();
            return true;
        });
        _idleMgtScenes.Add((layer.ClipId, layer.Path, layer.PreviewEdge,
            layer.SourceWriteTimeUtc, surface));
        while (_idleMgtScenes.Count > 2)
        {
            _idleMgtScenes[0].Surface.Dispose();
            _idleMgtScenes.RemoveAt(0);
        }
    }

    private MgtLiveGpuSurface? TakeMgtScene(string clipId, string path, int edge,
        DateTime sourceWriteTimeUtc)
    {
        var index = _idleMgtScenes.FindIndex(item => item.ClipId == clipId &&
            item.Path == path && item.Edge == edge);
        if (index < 0) return null;
        var surface = _idleMgtScenes[index].Surface;
        var current = _idleMgtScenes[index].SourceWriteTimeUtc == sourceWriteTimeUtc;
        _idleMgtScenes.RemoveAt(index);
        if (current) return surface;
        surface.Dispose();
        return null;
    }

    private static void ConfigureLayerEffects(GpuVideoSurface gpu, Clip clip, double time,
        double sourceSeconds, Project project)
    {
        GpuEffectProfile.Configure(gpu, project, clip, time, sourceSeconds,
            ProjectController.Instance?.ResourceProjectPath);
    }

    private async Task<bool> ShowNestedLayersAsync(Project project, IReadOnlyList<NestedRenderLayer> layers)
    {
        var valid = layers.Select(layer => (Layer: layer, Path: TextMediaService.Resolve(project, layer.Clip)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Path) && File.Exists(x.Path)).ToList();
        if (valid.Count != layers.Count && layers.Any(layer => layer.Transition != null))
        {
            ProjectController.Instance?.ReportPlaybackFailure(project.Id, "transition", "Falta un medio de vídeo para la transición.");
            return false;
        }
        var edge = PreviewLongEdge;
        var renderLayers = valid.Select(item => item.Layer).ToArray();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        // Publish a complete GPU composition, even when its layer topology changes.
        // Independent Avalonia surfaces can otherwise reveal an incomplete lower layer.
        var signature = project.Id + ":" + edge + ":" + string.Join("|", valid.Select(x =>
            x.Layer.Clip.Id + ":" + UsesFrameLockedPreview(project, x.Layer) + ":" + x.Path + ":" + File.GetLastWriteTimeUtc(x.Path!).Ticks +
            ":" + string.Join(".", x.Layer.OrderPath)));
        if (valid.Any(item => _sourceRetryAfter.TryGetValue(item.Layer.Clip.Id, out var retryAt) && DateTime.UtcNow < retryAt))
        {
            return false;
        }
        if (_nestedSignature != signature)
        {
            // Start every missing input for this position together. A late source should
            // not keep the subsequent sources from opening.
            if (_transport?.IsPlaying == true && valid.Count <= MaxPreparedLayers)
            {
                var requested = valid.Select(item =>
                {
                    var mode = UsesFrameLockedPreview(project, item.Layer);
                    return (item.Layer, Path: item.Path!, FrameLocked: mode,
                        Key: LayerKey(item.Layer.Clip.Id, item.Path!, edge, mode));
                }).ToArray();
                var required = requested.Select(item => item.Key).ToHashSet();
                var waiting = false;
                foreach (var item in requested)
                {
                    if (_nestedLayers.Any(entry => !entry.Failed && LayerKey(entry) == item.Key) ||
                        _preparedLayers.Any(entry => !entry.Failed && LayerKey(entry) == item.Key)) continue;
                    StartPrewarm(project, item.Layer, item.Path, edge, item.FrameLocked, required);
                    waiting |= _warmingLayers.ContainsKey(item.Key);
                }
                if (waiting)
                {
                    return false;
                }
            }

            var pending = new Task<NestedLayer>[valid.Count];
            var created = new List<Task<NestedLayer>>();
            var usedPrepared = new List<NestedLayer>();
            var reused = new HashSet<NestedLayer>();
            try
            {
                for (var sourceIndex = 0; sourceIndex < valid.Count; sourceIndex++)
                {
                    var (layer, path) = valid[sourceIndex];
                    var frameLocked = UsesFrameLockedPreview(project, layer);
                    var existing = _nestedLayers.FirstOrDefault(item => item.ClipId == layer.Clip.Id &&
                        item.Path == path && item.PreviewEdge == edge && item.FrameLocked == frameLocked &&
                        item.SourceWriteTimeUtc == File.GetLastWriteTimeUtc(path!) &&
                        !item.Failed && !reused.Contains(item));
                    if (existing != null)
                    {
                        reused.Add(existing);
                        pending[sourceIndex] = Task.FromResult(existing);
                        continue;
                    }

                    var key = LayerKey(layer.Clip.Id, path!, edge, frameLocked);
                    var prepared = _preparedLayers.FirstOrDefault(item => LayerKey(item) == key &&
                        item.SourceWriteTimeUtc == File.GetLastWriteTimeUtc(path!) && !item.Failed && !reused.Contains(item));
                    if (prepared != null)
                    {
                        reused.Add(prepared);
                        pending[sourceIndex] = Task.FromResult(prepared);
                        usedPrepared.Add(prepared);
                        continue;
                    }
                    var creation = CreateLayerAsync(project, layer, path!, edge, sourceIndex, frameLocked, false, CancellationToken.None);
                    pending[sourceIndex] = creation;
                    created.Add(creation);
                }
                var next = await Task.WhenAll(pending);
                foreach (var entry in next)
                    _sourceRetryAfter.Remove(entry.ClipId);
                if (!IsCurrentScene(project, renderLayers))
                {
                    foreach (var task in created.Where(task => task.IsCompletedSuccessfully))
                    {
                        task.Result.Dispose();
                        NestedLayerHost.Children.Remove(task.Result.Host);
                    }
                    return false;
                }
                foreach (var entry in usedPrepared) _preparedLayers.Remove(entry);
                foreach (var old in _nestedLayers.Where(item => !next.Contains(item)))
                {
                    if (File.Exists(old.Path) && File.GetLastWriteTimeUtc(old.Path) == old.SourceWriteTimeUtc)
                        RetainMgtScene(old);
                    old.Dispose();
                    NestedLayerHost.Children.Remove(old.Host);
                }
                _nestedLayers.Clear();
                _nestedLayers.AddRange(next);
                _nestedSignature = signature;
                for (var index = 0; index < _nestedLayers.Count; index++)
                {
                    _nestedLayers[index].SourceIndex = index;
                    _nestedLayers[index].Host.ZIndex = index;
                }
            }
            catch (Exception ex)
            {
                foreach (var item in valid)
                    _sourceRetryAfter[item.Layer.Clip.Id] = DateTime.UtcNow.AddSeconds(1);
                foreach (var task in created.Where(task => task.IsCompletedSuccessfully))
                {
                    var entry = task.Result;
                    entry.Dispose();
                    NestedLayerHost.Children.Remove(entry.Host);
                }
                if (!IsCurrentScene(project, renderLayers)) return false;
                var failedIndex = Array.FindIndex(pending, task => task?.IsFaulted == true);
                var failed = failedIndex >= 0 ? valid[failedIndex].Layer : null;
                ProjectController.Instance?.ReportPlaybackFailure(project.Id, failed?.Clip.Id ?? "layer", ex.Message,
                    failed?.Containers.FirstOrDefault()?.Id ?? failed?.Clip.Id);
                return false;
            }
        }
        var results = new Task<bool>[_nestedLayers.Count];
        for (var index = 0; index < _nestedLayers.Count; index++)
        {
            var entry = _nestedLayers[index];
            var layer = valid[index].Layer;
            entry.Fit(DynamicMotionEffect.Evaluate(layer.Clip, layer.LocalTimelineSeconds));
            results[index] = PresentEntryAsync(entry, layer);
        }
        var allReady = (await Task.WhenAll(results)).All(ready => ready);
        if (allReady && !IsCurrentScene(project, renderLayers)) return false;
        if (allReady)
        {
            try { await RenderGroupAsync(project, renderLayers); }
            catch (Exception ex)
            {
                allReady = false;
                ReleaseGroupLayer();
                ProjectController.Instance?.ReportPlaybackFailure(project.Id, "gpu", "Composición GPU · " + ex.Message);
            }
        }
        _previewLoad.Observe(_transport?.IsPlaying == true && PreviewLongEdge == 0 &&
            renderLayers.Length >= 3, allReady, System.Diagnostics.Stopwatch.GetElapsedTime(started), PreviewFps);
        SetImage(null, valid.Count == 0 ? "Sin imagen en esta posición" : null);
        return allReady;

        async Task<bool> PresentEntryAsync(NestedLayer entry, NestedRenderLayer layer)
        {
            if ((entry.Gpu != null || entry.Mgt != null) && !entry.Failed)
            {
                try
                {
                    if (entry.Gpu != null) ConfigureLayerEffects(entry.Gpu, layer.Clip,
                        layer.LocalTimelineSeconds, layer.SourceSeconds, project);
                    if (entry.Mgt != null) entry.Mgt.SubtitleScene = SubtitleRenderPlan.Request(project, layer.Clip);
                    var ready = await PresentLayerAsync(entry, layer, updateVisual: false);
                    if (ready) entry.NotReadySince = null;
                    else
                    {
                        entry.NotReadySince ??= DateTime.UtcNow;
                        if (DateTime.UtcNow - entry.NotReadySince > TimeSpan.FromSeconds(4))
                            throw new TimeoutException("El lector dejó de entregar fotogramas; se volverá a preparar el medio.");
                    }
                    return ready;
                }
                catch (Exception ex)
                {
                    entry.Failed = true;
                    _sourceRetryAfter[entry.ClipId] = DateTime.UtcNow.AddSeconds(1);
                    _nestedSignature = null;
                    if (entry.Visual != null) entry.Visual.Opacity = 0;
                    if (IsCurrentScene(project, renderLayers))
                        ProjectController.Instance?.ReportPlaybackFailure(project.Id, layer.Clip.Id, ex.Message,
                            layer.Containers.FirstOrDefault()?.Id ?? layer.Clip.Id);
                }
                return false;
            }
            return false;
        }
    }

    private bool IsCurrentScene(Project project, IReadOnlyList<NestedRenderLayer> layers)
    {
        if (!_attached || !ReferenceEquals(ProjectController.Instance?.CurrentProject, project)) return false;
        var latest = SubtitleRenderPlan.At(project, _requestedTime, "video");
        return latest.Count == layers.Count && latest.Zip(layers).All(pair =>
            pair.First.Clip.Id == pair.Second.Clip.Id && pair.First.OrderPath.SequenceEqual(pair.Second.OrderPath) &&
            pair.First.Transition?.Window.Transition.Id == pair.Second.Transition?.Window.Transition.Id);
    }

    private async Task<NestedLayer> CreateLayerAsync(Project project, NestedRenderLayer layer, string path, int edge,
        int sourceIndex, bool frameLocked, bool warmup, CancellationToken cancellation)
    {
        GpuVideoSurface? gpu = null;
        MgtLiveGpuSurface? mgt = null;
        CompositionDrawingSurface? drawing = null;
        var host = new Border { Background = Brushes.Transparent, IsHitTestVisible = false };
        NestedLayerHost.Children.Add(host);
        try
        {
            var sourceWriteTime = File.GetLastWriteTimeUtc(path);
            var asset = SubtitleRenderPlan.Asset(project, layer.Clip) ?? throw new InvalidOperationException("Falta la fuente visual.");
            if (asset.Source.Kind == "mgt")
                mgt = TakeMgtScene(layer.Clip.Id, path, edge, File.GetLastWriteTimeUtc(path)) ??
                    await MgtLiveGpuSurface.StartAsync(asset, edge, cancellation, path);
            else gpu = await Task.Run(() => new GpuVideoSurface(path, IsStill(path), edge, layer.SourceSeconds, exactDecode: frameLocked), cancellation);
            cancellation.ThrowIfCancellationRequested();
            gpu?.PauseForWarmup();
            var element = ElementComposition.GetElementVisual(host);
            for (var attempt = 0; element == null && attempt < 20; attempt++)
            {
                await Task.Delay(50, cancellation);
                element = ElementComposition.GetElementVisual(host);
            }
            var compositor = element?.Compositor ?? throw new InvalidOperationException("No hay compositor para una capa.");
            drawing = compositor.CreateDrawingSurface();
            var visual = compositor.CreateSurfaceVisual();
            visual.Surface = drawing;
            visual.Opacity = 0;
            ElementComposition.SetElementChildVisual(host, visual);
            if (gpu != null) await gpu.AttachAsync(compositor, drawing);
            else await mgt!.AttachAsync(compositor, drawing);
            if (mgt != null) mgt.SubtitleScene = SubtitleRenderPlan.Request(project, layer.Clip);
            var entry = new NestedLayer { Path = path, ClipId = layer.Clip.Id, PreviewEdge = edge, FrameLocked = frameLocked,
                SourceWriteTimeUtc = sourceWriteTime,
                Host = host, SourceIndex = sourceIndex, Gpu = gpu, Mgt = mgt, Drawing = drawing, Visual = visual };
            entry.Fit(DynamicMotionEffect.Evaluate(layer.Clip, layer.LocalTimelineSeconds));
            if (gpu != null) ConfigureLayerEffects(gpu, layer.Clip, layer.LocalTimelineSeconds,
                layer.SourceSeconds, project);
            var ready = false;
            for (var attempt = 0; attempt < 100 && !ready; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                ready = await PresentLayerAsync(entry, layer, updateVisual: false, cancellation, initial: true);
                if (!ready) await Task.Delay(40, cancellation);
            }
            if (!ready) throw new InvalidOperationException("El primer fotograma aún no está listo.");
            if (File.GetLastWriteTimeUtc(path) != sourceWriteTime)
                throw new IOException("El archivo cambió durante la preparación del fotograma.");
            if (warmup) gpu?.PauseForWarmup();
            return entry;
        }
        catch
        {
            ElementComposition.SetElementChildVisual(host, null);
            gpu?.Dispose();
            mgt?.Dispose();
            drawing?.Dispose();
            NestedLayerHost.Children.Remove(host);
            throw;
        }
    }

    private static bool UsesFrameLockedPreview(Project project, NestedRenderLayer layer)
    {
        // Use one source clock before, during and after the transition so playback
        // does not have to replace a decoder at either end of the transition.
        var clipId = layer.Containers.Count == 0 ? layer.Clip.Id : layer.Containers[0].Id;
        return project.ActiveSequence?.VideoTransitions.Any(transition => transition.Mgt != null &&
            (transition.LeftClipId == clipId || transition.RightClipId == clipId)) == true;
    }

    private static bool IsMgt(Project project, Clip clip) =>
        SubtitleRenderPlan.Asset(project, clip)?.Source.Kind == "mgt";

    private async Task<bool> PresentLayerAsync(NestedLayer entry, NestedRenderLayer layer, bool updateVisual,
        CancellationToken cancellation = default, bool initial = false)
    {
        if (entry.Mgt != null) return await entry.Mgt.PresentAsync(layer.SourceSeconds,
            layer.Clip.MgtOverrides, exact: initial || _transport?.IsPlaying != true, cancellation: cancellation, updateVisual: updateVisual);
        return entry.Gpu != null && await entry.Gpu.PresentAsync(layer.SourceSeconds,
            !initial && _transport?.IsPlaying == true,
            0.5 / Math.Max(1, ProjectController.Instance?.CurrentProject.ActiveSequence?.Fps.Seconds ?? 60),
            updateVisual, cancellation);
    }

    private static bool IsStill(string path) => new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" }
        .Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    private void ReleaseNestedLayers()
    {
        ResetPrewarm();
        ReleaseActiveLayers();
        foreach (var scene in _idleMgtScenes) scene.Surface.Dispose();
        _idleMgtScenes.Clear();
        _sourceRetryAfter.Clear();
        NestedLayerHost.Children.Clear();
        _layerProjectId = null;
    }

    private void ReleaseActiveLayers(bool retainMgt = false)
    {
        ReleaseGroupLayer();
        foreach (var layer in _nestedLayers)
        {
            if (retainMgt) RetainMgtScene(layer);
            layer.Dispose();
            NestedLayerHost.Children.Remove(layer.Host);
        }
        _nestedLayers.Clear();
        _nestedSignature = null;
    }
}
