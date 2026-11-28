using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class MonitorPanelView
{
    private const int MaxPreparedLayers = 6;
    private readonly List<NestedLayer> _preparedLayers = new();
    private readonly Dictionary<string, CancellationTokenSource> _warmingLayers = new();
    private readonly Dictionary<string, DateTime> _warmupFailures = new();
    private string? _layerProjectId;
    private int _prewarmEdge = -1;
    private double _lastPrewarmTime = double.NaN;
    private double _lastPrewarmScan = double.NaN;
    private double _measuredWarmupSeconds = 1.15;

    private static string LayerKey(string clipId, string path, int edge, bool frameLocked) =>
        clipId + "|" + path + "|" + edge + "|" + frameLocked;
    private static string LayerKey(NestedLayer layer) => LayerKey(layer.ClipId, layer.Path, layer.PreviewEdge, layer.FrameLocked);

    private void ResetPrewarm()
    {
        foreach (var token in _warmingLayers.Values) token.Cancel();
        _warmingLayers.Clear();
        foreach (var layer in _preparedLayers)
        {
            layer.Dispose();
            NestedLayerHost.Children.Remove(layer.Host);
        }
        _preparedLayers.Clear();
        _warmupFailures.Clear();
        _lastPrewarmTime = _lastPrewarmScan = double.NaN;
    }

    private void SyncPrewarmPosition(Project? project, double time)
    {
        if (project?.ActiveSequence == null || _transport?.IsPlaying != true)
        {
            if (!double.IsNaN(_lastPrewarmTime) || _warmingLayers.Count > 0 || _preparedLayers.Count > 0)
                ResetPrewarm();
            return;
        }
        var edge = PreviewLongEdge;
        if (_prewarmEdge != edge ||
            !double.IsNaN(_lastPrewarmTime) && (time < _lastPrewarmTime - 0.1 || time > _lastPrewarmTime + 0.75))
        {
            ResetPrewarm();
            _dynamicPreview.ResetEvidence();
        }
        _prewarmEdge = edge;
        _lastPrewarmTime = time;
    }

    private void UpdatePrewarm(Project project, double time, IReadOnlyList<NestedRenderLayer> active)
    {
        if (!_attached || project.ActiveSequence == null || _transport?.IsPlaying != true ||
            !ReferenceEquals(ProjectController.Instance?.CurrentProject, project)) return;
        var edge = PreviewLongEdge;
        if (time >= _lastPrewarmScan && time - _lastPrewarmScan < 0.12) return;
        _lastPrewarmScan = time;

        var lead = Math.Clamp((_measuredWarmupSeconds + 0.35) * _transport.PreviewSpeed, 2.25, 4.5);
        var horizon = time + lead;
        var frameSeconds = 1.0 / Math.Max(1, project.ActiveSequence.Fps.Seconds);
        var times = NestedRenderPlan.UpcomingStarts(project, time, horizon, "video", includeSubtitles: !SubtitleRenderPlan.Enabled(project))
            .Select(start => Math.Min(horizon, start + Math.Min(0.001, frameSeconds / 4)))
            .ToList();
        times.Add(horizon);

        var candidates = times.SelectMany(future => SubtitleRenderPlan.At(project, future, "video"))
            .DistinctBy(layer => (layer.Clip.Id, UsesFrameLockedPreview(project, layer)))
            .Select(layer => (Layer: layer, Path: TextMediaService.Resolve(project, layer.Clip), FrameLocked: UsesFrameLockedPreview(project, layer)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path)).ToList();
        var needed = candidates.Select(item => LayerKey(item.Layer.Clip.Id, item.Path!, edge, item.FrameLocked)).ToHashSet();
        foreach (var layer in active)
        {
            var path = TextMediaService.Resolve(project, layer.Clip);
            if (!string.IsNullOrWhiteSpace(path)) needed.Add(LayerKey(layer.Clip.Id, path, edge, UsesFrameLockedPreview(project, layer)));
        }
        foreach (var expired in _warmupFailures.Where(pair => pair.Value <= DateTime.UtcNow).ToArray())
            _warmupFailures.Remove(expired.Key);
        foreach (var obsolete in _preparedLayers.Where(item => !needed.Contains(LayerKey(item))).ToArray())
        {
            obsolete.Dispose();
            NestedLayerHost.Children.Remove(obsolete.Host);
            _preparedLayers.Remove(obsolete);
        }
        foreach (var obsolete in _warmingLayers.Where(item => !needed.Contains(item.Key)).ToArray())
        {
            obsolete.Value.Cancel();
            _warmingLayers.Remove(obsolete.Key);
        }

        foreach (var item in candidates)
        {
            // Only the current position can start more than one preparation at a time.
            if (_warmingLayers.Count > 0 || _preparedLayers.Count >= MaxPreparedLayers) break;
            var key = LayerKey(item.Layer.Clip.Id, item.Path!, edge, item.FrameLocked);
            if (_nestedLayers.Any(layer => LayerKey(layer) == key) ||
                _preparedLayers.Any(layer => LayerKey(layer) == key) || _warmingLayers.ContainsKey(key) ||
                _warmupFailures.TryGetValue(key, out var until) && DateTime.UtcNow < until) continue;
            // Nonoverlapping uses can borrow the worker after the current clip leaves.
            if (IsMgt(project, item.Layer.Clip) && active.Any(current =>
                    current.Containers.Count == 0 && item.Layer.Containers.Count == 0 &&
                    current.Clip.AssetId == item.Layer.Clip.AssetId &&
                    current.Clip.EndSeconds <= item.Layer.Clip.TimelineStart.Seconds))
                continue;
            StartPrewarm(project, item.Layer, item.Path!, edge, item.FrameLocked);
        }
    }

    private void StartPrewarm(Project project, NestedRenderLayer layer, string path, int edge, bool frameLocked,
        HashSet<string>? required = null)
    {
        var projectId = project.Id;
        var key = LayerKey(layer.Clip.Id, path, edge, frameLocked);
        if (_warmingLayers.ContainsKey(key) || _preparedLayers.Any(item => LayerKey(item) == key)) return;
        if (_warmupFailures.TryGetValue(key, out var until) && DateTime.UtcNow < until) return;
        if (_preparedLayers.Count + _warmingLayers.Count >= MaxPreparedLayers)
        {
            if (required == null) return;
            var discard = _preparedLayers.LastOrDefault(item => !required.Contains(LayerKey(item)));
            if (discard != null)
            {
                discard.Dispose();
                NestedLayerHost.Children.Remove(discard.Host);
                _preparedLayers.Remove(discard);
            }
            else
            {
                var waiting = _warmingLayers.LastOrDefault(item => !required.Contains(item.Key));
                if (waiting.Value == null) return;
                waiting.Value.Cancel();
                _warmingLayers.Remove(waiting.Key);
            }
        }
        var cancellation = new CancellationTokenSource();
        _warmingLayers[key] = cancellation;
        _ = RunPrewarmAsync();

        async System.Threading.Tasks.Task RunPrewarmAsync()
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var prepared = await CreateLayerAsync(project, layer, path, edge, -1, frameLocked, true, cancellation.Token);
                _measuredWarmupSeconds = Math.Clamp(_measuredWarmupSeconds * 0.75 + watch.Elapsed.TotalSeconds * 0.25, 0.1, 4);
                if (cancellation.IsCancellationRequested || !_attached || ProjectController.Instance?.CurrentProject.Id != projectId ||
                    PreviewLongEdge != edge)
                {
                    prepared.Dispose();
                    NestedLayerHost.Children.Remove(prepared.Host);
                }
                else _preparedLayers.Add(prepared);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    _warmupFailures[key] = DateTime.UtcNow.AddSeconds(5);
                    ProjectController.Instance?.ReportStatus("Preparación GPU: " + ex.Message);
                }
            }
            finally
            {
                if (_warmingLayers.TryGetValue(key, out var current) && ReferenceEquals(current, cancellation))
                    _warmingLayers.Remove(key);
                cancellation.Dispose();
                if (_attached && _transport?.IsPlaying == true) ShowTime(_requestedTime);
            }
        }
    }
}
