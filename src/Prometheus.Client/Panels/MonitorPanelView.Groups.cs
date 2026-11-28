using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class MonitorPanelView
{
    private sealed class GroupLayer : IDisposable
    {
        public required Border Host { get; init; }
        public required CompositionDrawingSurface Drawing { get; init; }
        public required CompositionSurfaceVisual Visual { get; init; }
        public required GpuGroupSurface Gpu { get; init; }

        public void Fit(double width, double height)
        {
            Visual.Size = new(width, height);
            Visual.Offset = new Vector3D(0, 0, 0);
            Visual.CenterPoint = new Vector3D(width / 2, height / 2, 0);
            Visual.Scale = new Vector3D(1, -1, 1);
        }

        public void Dispose()
        {
            ElementComposition.SetElementChildVisual(Host, null);
            Gpu.Dispose();
            Drawing.Dispose();
        }
    }

    private GroupLayer? _groupLayer;
    private readonly PreviewLoadBudget _previewLoad = new();
    private readonly DynamicPreviewBudget _dynamicPreview = new();
    private (int Width, int Height) _lastReportedResolution;

    private void ResetDynamicPreview()
    {
        _previewRevision++;
        _dynamicPreview.Reset();
    }

    private void ObserveDynamicPreview(long renderStarted, double sequenceFps)
    {
        var previous = _dynamicPreview.Level;
        if (_groupLayer == null) return;
        _dynamicPreview.Observe(_previewClock.Elapsed, System.Diagnostics.Stopwatch.GetElapsedTime(renderStarted),
            _groupLayer.Gpu.LastCompositionWork, Math.Min(PreviewFps, sequenceFps));
        if (previous != _dynamicPreview.Level)
            _transport?.Diagnostics.Current?.Count(previous < _dynamicPreview.Level ? "dynamic_lowered" : "dynamic_raised");
    }

    private void UpdateDynamicResolutionLabel()
    {
        if (!UsesDynamicPreview || _groupLayer == null)
        {
            DynamicResolutionLabel.IsVisible = false;
            return;
        }
        var size = (_groupLayer.Gpu.Width, _groupLayer.Gpu.Height);
        if (size != _lastReportedResolution)
        {
            _lastReportedResolution = size;
            DynamicResolutionLabel.Text = $"Dinámica · {size.Width}×{size.Height}";
        }
        DynamicResolutionLabel.IsVisible = true;
    }

    private async Task RenderGroupAsync(Project project, IReadOnlyList<NestedRenderLayer> layers)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var longEdge = Math.Max(sequence.Width, sequence.Height);
        var edge = PreviewLongEdge;
        if (UsesDynamicPreview && _transport?.IsPlaying == true)
            edge = _dynamicPreview.OutputLongEdge(longEdge);
        else if (PreviewUserSettings.QualityIndex == PreviewUserSettings.HighQualityIndex &&
                 _previewLoad.Reduce && _transport?.IsPlaying == true && layers.Count >= 3)
        {
            var rasterScale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            var screenEdge = Math.Max(NestedLayerHost.Bounds.Width, NestedLayerHost.Bounds.Height) * rasterScale;
            edge = (int)Math.Clamp(Math.Ceiling(screenEdge / 64) * 64, 640, 1280);
        }
        var scale = edge > 0 && edge < longEdge ? (double)edge / longEdge : 1;
        var width = Math.Max(1, (int)Math.Round(sequence.Width * scale));
        var height = Math.Max(1, (int)Math.Round(sequence.Height * scale));
        var previous = _groupLayer;
        var replacement = previous == null || previous.Gpu.Width != width || previous.Gpu.Height != height;
        var group = previous;
        if (replacement)
        {
            var host = new Border { Background = Brushes.Transparent, IsHitTestVisible = false, ZIndex = 1000 };
            NestedLayerHost.Children.Add(host);
            GpuGroupSurface? gpu = null;
            CompositionDrawingSurface? drawing = null;
            try
            {
                var element = ElementComposition.GetElementVisual(host);
                for (var attempt = 0; element == null && attempt < 20; attempt++)
                {
                    await Task.Delay(50);
                    element = ElementComposition.GetElementVisual(host);
                }
                var compositor = element?.Compositor ?? throw new InvalidOperationException("No hay compositor para el grupo.");
                gpu = new GpuGroupSurface(width, height)
                { TransitionDesignWidth = sequence.Width, TransitionDesignHeight = sequence.Height };
                drawing = compositor.CreateDrawingSurface();
                var visual = compositor.CreateSurfaceVisual();
                visual.Surface = drawing;
                visual.Opacity = 0;
                ElementComposition.SetElementChildVisual(host, visual);
                await gpu.AttachAsync(compositor, drawing);
                group = new GroupLayer { Host = host, Drawing = drawing, Visual = visual, Gpu = gpu };
            }
            catch
            {
                ElementComposition.SetElementChildVisual(host, null);
                gpu?.Dispose(); drawing?.Dispose(); NestedLayerHost.Children.Remove(host);
                throw;
            }
        }
        var current = group!;
        current.Gpu.TransitionDesignWidth = sequence.Width;
        current.Gpu.TransitionDesignHeight = sequence.Height;
        var tree = NestedRenderTree.Build(layers, sequence);
        current.Gpu.MasksForClip = (clip, time) => GpuEffectProfile.ResolveMaskGroups(project, clip,
            KeyframeAnimation.EvaluateEffects(clip, time), time,
            clip.SourceIn.Seconds + Math.Max(0, time - clip.TimelineStart.Seconds) * clip.Speed.Seconds,
            ProjectController.Instance?.ResourceProjectPath);
        current.Gpu.LutPathForClip = clip => GpuLutEffect.ResolvePath(project, clip,
            ProjectController.Instance?.ResourceProjectPath);
        current.Gpu.VisualOverrideForClip = clip => _gizmoPointer != null &&
            clip.Id == _gizmoClipId ? _gizmoPreviewState : null;
        var frames = new Dictionary<string, GpuFrameReference>(StringComparer.Ordinal);
        foreach (var layer in _nestedLayers)
            if (layer.Gpu?.TryGetCurrentFrame(out var frame) == true ||
                layer.Mgt?.TryGetCurrentFrame(out frame) == true)
                frames.TryAdd(layer.ClipId, frame);
        try
        {
            await current.Gpu.RenderAsync(tree, clipId => frames.TryGetValue(clipId, out var frame) ? frame : null);
        }
        catch
        {
            if (replacement)
            {
                current.Dispose();
                NestedLayerHost.Children.Remove(current.Host);
            }
            throw;
        }
        current.Fit(NestedLayerHost.Bounds.Width, NestedLayerHost.Bounds.Height);
        current.Visual.Opacity = 1;
        if (replacement)
        {
            ReleaseGroupLayer();
            _groupLayer = current;
        }
        UpdateDynamicResolutionLabel();
        foreach (var layer in _nestedLayers) if (layer.Visual != null) layer.Visual.Opacity = 0;
    }

    private void ReleaseGroupLayer()
    {
        if (_groupLayer == null) return;
        var group = _groupLayer;
        _groupLayer = null;
        group.Dispose();
        NestedLayerHost.Children.Remove(group.Host);
        UpdateDynamicResolutionLabel();
    }

    private bool _gizmoGroupRendering;
    private bool _gizmoGroupPending;

    private async Task RefreshGizmoGroupAsync()
    {
        if (_gizmoGroupRendering) { _gizmoGroupPending = true; return; }
        if (_groupLayer == null) return;
        if (_busy) { _gizmoGroupPending = true; return; }
        _gizmoGroupRendering = true;
        try
        {
            do
            {
                _gizmoGroupPending = false;
                var project = ProjectController.Instance?.CurrentProject;
                if (project == null || _gizmoPointer == null) break;
                var layers = SubtitleRenderPlan.At(project, _requestedTime, "video");
                await RenderGroupAsync(project, layers);
            }
            while (_gizmoGroupPending);
        }
        catch (Exception ex) { ProjectController.Instance?.ReportStatus("Transformación: " + ex.Message); }
        finally
        {
            _gizmoGroupRendering = false;
            if (_releaseWhenIdle)
            {
                _releaseWhenIdle = false;
                ReleaseNestedLayers();
                if (_attached) QueueRefresh();
            }
            if (_gizmoPointer == null || _refreshAgain)
            {
                _refreshAgain = false;
                QueueRefresh();
            }
        }
    }
}
