using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;
using Prometheus.Core.Timeline;
using Vortice.Direct3D11;
using SkiaSharp;

namespace Prometheus.Client.Services;

/// <summary>Renders full-resolution sequence frames into a shared D3D11 texture.</summary>
public sealed class GpuExportFrameRenderer : IDisposable
{
    private readonly Project _project;
    private readonly GpuGroupSurface _compositor;
    private readonly Dictionary<string, (string Path, GpuVideoSurface Surface)> _sources = new();
    private readonly Dictionary<string, (string Path, MgtLiveGpuSurface Surface)> _mgtSources = new();
    private readonly string? _projectPath;
    private readonly ExportMediaPlan? _media;

    public int Width => _compositor.Width;
    public int Height => _compositor.Height;
    public IntPtr SharedTextureHandle => _compositor.SharedHandle;

    public void WithOutputTexture(Action<ID3D11Device, ID3D11DeviceContext, ID3D11Texture2D> consume) =>
        _compositor.WithOutputTexture(consume);

    public GpuExportFrameRenderer(Project project, string? projectPath) : this(project, projectPath, null) { }

    public GpuExportFrameRenderer(Project project, string? projectPath, int width, int height)
        : this(project, projectPath, null, width, height) { }

    internal uint AdapterVendorId => _compositor.Adapter.VendorId;

    internal void CopyFrameBgra(byte[] pixels) => _compositor.CopyFrameBgra(pixels);

    internal SKBitmap Readback() => _compositor.ReadbackBitmap();

    internal GpuExportFrameRenderer(Project project, string? projectPath, ExportMediaPlan? media,
        int? outputWidth = null, int? outputHeight = null)
    {
        _media = media;
        _project = project;
        _projectPath = projectPath;
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var output = ExportPresentation.OutputSequence(sequence, new(false, 12, 192, Width: outputWidth, Height: outputHeight));
        _compositor = new GpuGroupSurface(output.Width, output.Height)
        {
            TransitionDesignWidth = sequence.Width,
            TransitionDesignHeight = sequence.Height,
            MasksForClip = (clip, time) => GpuEffectProfile.ResolveMaskGroups(project, clip,
                KeyframeAnimation.EvaluateEffects(clip, time), time,
                clip.SourceIn.Seconds + Math.Max(0, time - clip.TimelineStart.Seconds) * clip.Speed.Seconds,
                projectPath),
            LutPathForClip = clip => GpuLutEffect.ResolvePath(project, clip, projectPath)
        };
    }

    public async Task RenderAsync(double time, CancellationToken cancellation = default)
    {
        var original = SubtitleRenderPlan.At(_project, time, "video");
        var layers = new List<NestedRenderLayer>();
        var paths = new Dictionary<string, string>();
        foreach (var entry in original)
            if (Prepare(entry)) layers.Add(entry);

        foreach (var group in original.Where(layer => layer.Transition != null).GroupBy(layer => layer.Transition!))
        {
            if (group.All(layer => paths.ContainsKey(layer.Clip.Id))) continue;
            var frame = group.Key;
            var survivors = layers.Where(layer => layer.Transition == frame).ToArray();
            if (survivors.Select(layer => layer.Containers.FirstOrDefault()?.Id ?? layer.Clip.Id).Distinct().Count() > 1)
                continue;
            if (survivors.Length == 0 && group.Select(layer => layer.Containers.FirstOrDefault()?.Id ?? layer.Clip.Id).Distinct().Count() == 1)
            {
                var missing = group.First().Containers.FirstOrDefault()?.Id ?? group.First().Clip.Id;
                var other = missing == frame.Window.Left.Id ? frame.Window.Right : frame.Window.Left;
                var fallback = NestedRenderPlan.TransitionFallback(_project, frame, other);
                var first = layers.FindIndex(layer => layer.OrderPath[0] > group.First().OrderPath[0]);
                var additions = fallback.Where(Prepare);
                layers.InsertRange(first < 0 ? layers.Count : first, additions);
            }
            for (var i = 0; i < layers.Count; i++)
                if (layers[i].Transition == frame) layers[i] = layers[i] with { Transition = null };
        }

        bool Prepare(NestedRenderLayer layer)
        {
            var asset = SubtitleRenderPlan.Asset(_project, layer.Clip);
            var path = _media == null || SubtitleRenderPlan.IsScene(layer.Clip) ? TextMediaService.Resolve(_project, asset, Width, Height) : _media.VideoPath(asset, Width, Height);
            if (string.IsNullOrWhiteSpace(path) && _media != null) return false;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException($"Falta «{layer.Clip.Name}» · {ExportMediaPlan.Timecode(time)}.", path);
            paths[layer.Clip.Id] = path;
            return true;
        }
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            cancellation.ThrowIfCancellationRequested();
            var asset = SubtitleRenderPlan.Asset(_project, layer.Clip);
            var path = paths[layer.Clip.Id];
            active.Add(layer.Clip.Id);
            var proxyPath = _media?.UsesProxy(asset) == true ? path : null;
            try
            {
                if (asset?.Source.Kind == "mgt")
                {
                    if (!_mgtSources.TryGetValue(layer.Clip.Id, out var mgt) || mgt.Path != path)
                    {
                        mgt.Surface?.Dispose();
                        mgt = (path, await MgtLiveGpuSurface.StartAsync(asset, 0, cancellation, path));
                        _mgtSources[layer.Clip.Id] = mgt;
                    }
                    mgt.Surface.SubtitleScene = SubtitleRenderPlan.Request(_project, layer.Clip);
                    await mgt.Surface.RenderOffscreenAsync(layer.SourceSeconds, layer.Clip.MgtOverrides, cancellation);
                    continue;
                }
                if (!layer.Clip.MaskLayersInitialized && layer.Clip.ShapeMask == null &&
                    !StillImageRules.IsStill(_project, layer.Clip) &&
                    layer.Clip.SamMaskTracks.LastOrDefault() is { } samTrack &&
                    SamMaskTrackValidity.Problem(layer.Clip, samTrack, path, checkSourceIdentity: proxyPath == null) is { } problem)
                    throw new InvalidOperationException(problem);
                if (!StillImageRules.IsStill(_project, layer.Clip))
                    foreach (var maskLayer in layer.Clip.MaskLayers.Where(item => item.Enabled))
                        if (maskLayer.SamMaskTracks.LastOrDefault() is { } layerTrack &&
                            SamMaskTrackValidity.Problem(layer.Clip, layerTrack, path, checkSourceIdentity: proxyPath == null) is { } layerProblem)
                            throw new InvalidOperationException(maskLayer.Name + ": " + layerProblem);
                if (!_sources.TryGetValue(layer.Clip.Id, out var source) || source.Path != path)
                {
                    if (source.Surface != null) source.Surface.Dispose();
                    source = (path, new GpuVideoSurface(path, IsStill(path),
                        initialSeconds: layer.SourceSeconds, exactDecode: true));
                    _sources[layer.Clip.Id] = source;
                }
                GpuEffectProfile.Configure(source.Surface, _project, layer.Clip,
                    layer.LocalTimelineSeconds, layer.SourceSeconds, _projectPath, proxyPath);
                var ready = false;
                for (var attempt = 0; attempt < 150; attempt++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (source.Surface.RenderOffscreen(layer.SourceSeconds,
                        0.5 / Math.Max(1, _project.ActiveSequence!.Fps.Seconds), cancellation)) { ready = true; break; }
                    await Task.Delay(40, cancellation);
                }
                if (!ready) throw new InvalidOperationException("Un fotograma no estuvo listo después de 6 segundos.");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                throw new InvalidOperationException($"«{layer.Clip.Name}» · {ExportMediaPlan.Timecode(time)} · {error.Message}", error);
            }
        }

        _compositor.RenderOffscreen(NestedRenderTree.Build(layers, _project.ActiveSequence), clipId =>
        {
            if (_mgtSources.TryGetValue(clipId, out var mgt) &&
                mgt.Surface.TryGetCurrentFrame(out var mgtFrame)) return mgtFrame;
            if (_sources.TryGetValue(clipId, out var source) &&
                source.Surface.TryGetCurrentFrame(out var frame)) return frame;
            return null;
        });
        foreach (var id in _sources.Keys.Where(id => !active.Contains(id)).ToArray())
        {
            _sources[id].Surface.Dispose();
            _sources.Remove(id);
        }
        foreach (var id in _mgtSources.Keys.Where(id => !active.Contains(id)).ToArray())
        {
            _mgtSources[id].Surface.Dispose();
            _mgtSources.Remove(id);
        }
    }

    private static bool IsStill(string path) =>
        new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" }
            .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        foreach (var source in _sources.Values) source.Surface.Dispose();
        _sources.Clear();
        foreach (var source in _mgtSources.Values) source.Surface.Dispose();
        _mgtSources.Clear();
        _compositor.Dispose();
    }
}
