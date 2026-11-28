using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;

namespace Prometheus.Client.Services;

/// <summary>Tracks a clip in short GPU-decoded windows without temporary JPEG frames.</summary>
public static class SamTrackingService
{
    private const int WindowStep = 47; // 48 frames including the overlap/seed frame.

    private static string CheckpointMode(string operation, string backend) =>
        backend == "cuda" ? operation : operation + ":" + backend;

    public static bool CanRepair(Clip clip, SamMaskTrack previous, double anchorSourceSeconds) =>
        previous.FrameCount >= 2 && previous.FramesPerSecond > 0 &&
        previous.FrameAt(anchorSourceSeconds).HasValue &&
        Math.Abs(clip.SourceIn.Seconds - previous.StartSourceSeconds) <=
            0.5 / previous.FramesPerSecond &&
        clip.SourceIn.Seconds + clip.Duration.Seconds * clip.Speed.Seconds <=
            previous.StartSourceSeconds + previous.FrameCount / previous.FramesPerSecond +
            0.5 / previous.FramesPerSecond;

    public static SamMaskTrack Track(Project project, string clipId, string sourcePath,
        string projectPath, double anchorSourceSeconds, SamMaskKeyframe seed,
        IProgress<(int Done, int Total)>? progress, CancellationToken cancellation, Action? loadingModel = null)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var clip = sequence.Clips.FirstOrDefault(item => item.Id == clipId) ??
            throw new InvalidOperationException("El clip SAM ya no existe.");
        var asset = project.Assets.FirstOrDefault(item => item.Id == clip.AssetId);
        var sourceFps = asset?.Streams.FirstOrDefault(stream => stream.Id == clip.StreamId)?.NominalFps.Seconds
            ?? asset?.Streams.FirstOrDefault(stream => stream.Kind == "video")?.NominalFps.Seconds
            ?? sequence.Fps.Seconds;
        var start = clip.SourceIn.Seconds;
        var duration = clip.Duration.Seconds * clip.Speed.Seconds;
        if (!double.IsFinite(duration) || duration <= 0 || duration > 360)
            throw new InvalidOperationException("SAM requiere un clip de entre dos fotogramas y seis minutos; divide el clip si es más largo.");
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Falta el vídeo para SAM.", sourcePath);
        cancellation.ThrowIfCancellationRequested();

        // Older projects may have saved the importer default instead of the media's frame rate.
        using var video = new GpuVideoSurface(sourcePath,
            initialSeconds: Math.Clamp(anchorSourceSeconds, start, start + duration), exactDecode: true);
        sourceFps = video.SourceFrameRate ?? sourceFps;
        var fps = Math.Clamp(double.IsFinite(sourceFps) && sourceFps > 0 ? sourceFps : 30, 1, 60);
        var count = (int)Math.Ceiling(duration * fps - 0.000001);
        if (count < 2 || count > 21_600)
            throw new InvalidOperationException("SAM requiere un clip de entre dos fotogramas y seis minutos; divide el clip si es más largo.");
        var anchor = Math.Clamp((int)Math.Round((anchorSourceSeconds - start) * fps), 0, count - 1);
        var masks = new SamMaskKeyframe?[count];
        var completed = new bool[count];
        var done = 0;
        using var worker = new Sam2VideoBridge();
        loadingModel?.Invoke();
        worker.Prepare(cancellation);
        progress?.Report((0, count));
        byte[][] initialSeeds = seed.DecodeObjectAlphas().ToArray();
        var checkpoint = new SamTrackingCheckpoint(projectPath, project, clip,
            sourcePath, seed, fps, count, anchor, CheckpointMode("track", worker.Backend));

        void RunDirection(bool reverse)
        {
            var edge = anchor;
            var seeds = initialSeeds;
            var seedWidth = seed.Width;
            var seedHeight = seed.Height;
            while (reverse ? edge > 0 : edge < count - 1)
            {
                cancellation.ThrowIfCancellationRequested();
                var low = reverse ? Math.Max(0, edge - WindowStep) : edge;
                var high = reverse ? edge : Math.Min(count - 1, edge + WindowStep);
                if (checkpoint.TryLoad(low, high, reverse, out var saved))
                {
                    for (var local = 0; local < saved!.Frames.Count; local++)
                    {
                        var index = low + local;
                        masks[index] = saved.Frames[local];
                        if (completed[index]) continue;
                        completed[index] = true;
                        done++;
                    }
                    seeds = saved.Boundary.DecodeObjectAlphas().ToArray();
                    seedWidth = saved.Boundary.Width;
                    seedHeight = saved.Boundary.Height;
                    edge = reverse ? low : high;
                    progress?.Report((done, count));
                    continue;
                }
                var frames = new List<SamVideoFrame>(high - low + 1);
                for (var index = low; index <= high; index++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var sourceSeconds = start + index / fps;
                    ConfigureFrame(video, project, clip, sourceSeconds, projectPath);
                    var ready = false;
                    for (var attempt = 0; attempt < 150; attempt++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (video.RenderOffscreen(sourceSeconds, 0.5 / fps, cancellation)) { ready = true; break; }
                        Thread.Sleep(40);
                    }
                    if (!ready) throw new InvalidOperationException("Un fotograma SAM no estuvo listo después de 6 segundos.");
                    var captured = video.CaptureAnalysisFrame();
                    frames.Add(new SamVideoFrame(captured.Width, captured.Height, captured.Bgra));
                }
                var scaledSeeds = seeds.Select(mask => ScaleMask(mask, seedWidth, seedHeight,
                    frames[0].Width, frames[0].Height)).ToArray();
                var reported = done;
                var tracked = worker.TrackWindow(frames, scaledSeeds, reverse, cancellation, local =>
                {
                    if (!completed[low + local]) progress?.Report((++reported, count));
                });
                for (var local = 0; local < tracked.Count; local++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var index = low + local;
                    var frame = tracked[local];
                    masks[index] = SamMaskKeyframe.Create(start + index / fps,
                        frame.Width, frame.Height, frame.UnionAlpha());
                    if (completed[index]) continue;
                    completed[index] = true;
                    done++;
                }
                var boundary = tracked[reverse ? 0 : tracked.Count - 1];
                checkpoint.Save(new SamTrackingCheckpoint.Chunk
                {
                    Low = low, High = high, Reverse = reverse,
                    Frames = Enumerable.Range(low, high - low + 1)
                        .Select(index => masks[index]!).ToList(),
                    Boundary = SamMaskKeyframe.Create(start + (reverse ? low : high) / fps,
                        boundary.Width, boundary.Height, boundary.UnionAlpha(),
                        boundary.ObjectAlphas)
                });
                seeds = boundary.ObjectAlphas;
                seedWidth = frames[0].Width;
                seedHeight = frames[0].Height;
                edge = reverse ? low : high;
                progress?.Report((done, count));
            }
        }

        if (anchor < count - 1) RunDirection(reverse: false);
        if (anchor > 0) RunDirection(reverse: true);
        if (!completed[anchor]) { completed[anchor] = true; done++; }
        var first = masks.FirstOrDefault(mask => mask != null) ?? seed;
        var exactSeed = ScaleMask(seed.DecodeAlpha(), seed.Width, seed.Height, first.Width, first.Height);
        masks[anchor] = SamMaskKeyframe.Create(start + anchor / fps, first.Width, first.Height, exactSeed);
        progress?.Report((done, count));
        if (masks.Any(mask => mask == null))
            throw new InvalidDataException("SAM no devolvió todos los fotogramas.");
        var track = new SamMaskTrack { StartSourceSeconds = start, FramesPerSecond = fps,
            FrameCount = count, Width = first.Width, Height = first.Height,
            SourceFileLength = new FileInfo(sourcePath).Length,
            SourceFileWriteUtcTicks = File.GetLastWriteTimeUtc(sourcePath).Ticks };
        SamMaskTrackStore.Write(projectPath, track, masks.Select(mask => mask!).ToArray(), cancellation);
        checkpoint.Complete();
        return track;
    }

    /// <summary>Propagates one new anchor until the old track agrees again or another manual anchor is reached.</summary>
    public static SamMaskTrack Repair(Project project, string clipId, string sourcePath,
        string projectPath, double anchorSourceSeconds, SamMaskKeyframe seed,
        SamMaskTrack previous, IProgress<(int Done, int Total)>? progress,
        CancellationToken cancellation, Action? loadingModel = null)
    {
        var clip = project.ActiveSequence?.Clips.FirstOrDefault(item => item.Id == clipId) ??
            throw new InvalidOperationException("El clip SAM ya no existe.");
        var count = previous.FrameCount;
        var fps = previous.FramesPerSecond;
        if (!CanRepair(clip, previous, anchorSourceSeconds) ||
            SamMaskTrackValidity.Problem(clip, previous, sourcePath) != null)
            throw new InvalidOperationException("El seguimiento anterior no cubre este clip; inicia un seguimiento nuevo.");
        var anchor = previous.FrameAt(anchorSourceSeconds) ??
            throw new InvalidOperationException("La corrección está fuera del seguimiento SAM.");
        var manual = clip.SamMasks.Select(mask => previous.FrameAt(mask.SourceSeconds))
            .Where(index => index.HasValue).Select(index => index!.Value).Distinct().ToArray();
        var leftLimit = manual.Where(index => index < anchor).DefaultIfEmpty(0).Max();
        var rightLimit = manual.Where(index => index > anchor).DefaultIfEmpty(count - 1).Min();
        var changed = new Dictionary<int, SamMaskKeyframe>();
        var done = 0;
        var total = rightLimit - leftLimit + 1;
        cancellation.ThrowIfCancellationRequested();
        using var video = new GpuVideoSurface(sourcePath,
            initialSeconds: previous.StartSourceSeconds + anchor / fps, exactDecode: true);
        using var worker = new Sam2VideoBridge();
        loadingModel?.Invoke();
        worker.Prepare(cancellation);
        progress?.Report((0, total));
        var initialSeeds = seed.DecodeObjectAlphas().ToArray();
        var checkpoint = new SamTrackingCheckpoint(projectPath, project, clip,
            sourcePath, seed, fps, count, anchor, CheckpointMode("repair:" + previous.Id, worker.Backend));

        void Propagate(bool reverse)
        {
            var edge = anchor;
            var seeds = initialSeeds;
            var seedWidth = seed.Width;
            var seedHeight = seed.Height;
            var limit = reverse ? leftLimit : rightLimit;
            while (reverse ? edge > limit : edge < limit)
            {
                cancellation.ThrowIfCancellationRequested();
                var low = reverse ? Math.Max(limit, edge - WindowStep) : edge;
                var high = reverse ? edge : Math.Min(limit, edge + WindowStep);
                if (!checkpoint.TryLoad(low, high, reverse, out var saved))
                {
                    var frames = new List<SamVideoFrame>(high - low + 1);
                    for (var index = low; index <= high; index++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var sourceSeconds = previous.StartSourceSeconds + index / fps;
                        ConfigureFrame(video, project, clip, sourceSeconds, projectPath);
                        var ready = false;
                        for (var attempt = 0; attempt < 150; attempt++)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (video.RenderOffscreen(sourceSeconds, 0.5 / fps, cancellation))
                            { ready = true; break; }
                            Thread.Sleep(40);
                        }
                        if (!ready) throw new InvalidOperationException("Un fotograma SAM no estuvo listo después de 6 segundos.");
                        var captured = video.CaptureAnalysisFrame();
                        frames.Add(new SamVideoFrame(captured.Width, captured.Height, captured.Bgra));
                    }
                    var scaledSeeds = seeds.Select(mask => ScaleMask(mask, seedWidth, seedHeight,
                        frames[0].Width, frames[0].Height)).ToArray();
                    var reported = done;
                    var tracked = worker.TrackWindow(frames, scaledSeeds, reverse, cancellation, local =>
                    {
                        var index = low + local;
                        if (index != anchor && (index != limit || !manual.Contains(limit)) &&
                            !changed.ContainsKey(index)) progress?.Report((++reported, total));
                    });
                    var boundary = tracked[reverse ? 0 : tracked.Count - 1];
                    saved = new SamTrackingCheckpoint.Chunk
                    {
                        Low = low, High = high, Reverse = reverse,
                        Frames = tracked.Select((frame, local) => SamMaskKeyframe.Create(
                            previous.StartSourceSeconds + (low + local) / fps,
                            previous.Width, previous.Height,
                            ScaleMask(frame.UnionAlpha(), frame.Width, frame.Height,
                                previous.Width, previous.Height))).ToList(),
                        Boundary = SamMaskKeyframe.Create(
                            previous.StartSourceSeconds + (reverse ? low : high) / fps,
                            boundary.Width, boundary.Height, boundary.UnionAlpha(),
                            boundary.ObjectAlphas)
                    };
                    checkpoint.Save(saved);
                }
                for (var local = 0; local < saved!.Frames.Count; local++)
                {
                    var index = low + local;
                    if (index == anchor || index == limit && manual.Contains(limit)) continue;
                    if (!changed.ContainsKey(index)) done++;
                    changed[index] = saved.Frames[local];
                }
                var nextEdge = reverse ? low : high;
                seeds = saved.Boundary.DecodeObjectAlphas().ToArray();
                seedWidth = saved.Boundary.Width;
                seedHeight = saved.Boundary.Height;
                edge = nextEdge;
                progress?.Report((done, total));
                if (edge == limit || manual.Contains(edge)) break;
                var oldAlpha = SamMaskTrackStore.Read(projectPath, previous, edge).DecodeAlpha();
                if (MaskAgreement(changed[edge].DecodeAlpha(), oldAlpha) >= 0.95) break;
            }
        }

        if (anchor < rightLimit) Propagate(reverse: false);
        if (anchor > leftLimit) Propagate(reverse: true);
        changed[anchor] = SamMaskKeyframe.Create(
            previous.StartSourceSeconds + anchor / fps, previous.Width, previous.Height,
            ScaleMask(seed.DecodeAlpha(), seed.Width, seed.Height,
                previous.Width, previous.Height));
        done++;
        progress?.Report((done, total));
        var replacement = new SamMaskTrack { StartSourceSeconds = previous.StartSourceSeconds,
            FramesPerSecond = fps, FrameCount = count,
            Width = previous.Width, Height = previous.Height,
            SourceFileLength = previous.SourceFileLength,
            SourceFileWriteUtcTicks = previous.SourceFileWriteUtcTicks };
        SamMaskTrackStore.WritePatched(projectPath, previous, replacement, changed, cancellation);
        checkpoint.Complete();
        return replacement;
    }

    private static double MaskAgreement(byte[] a, byte[] b)
    {
        long union = 0, intersection = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var aa = a[i] > 127;
            var bb = b[i] > 127;
            if (aa || bb) union++;
            if (aa && bb) intersection++;
        }
        return union == 0 ? 1 : (double)intersection / union;
    }

    private static byte[] ScaleMask(byte[] input, int sourceWidth, int sourceHeight,
        int targetWidth, int targetHeight)
    {
        if (sourceWidth == targetWidth && sourceHeight == targetHeight) return input;
        var output = new byte[checked(targetWidth * targetHeight)];
        for (var y = 0; y < targetHeight; y++)
            for (var x = 0; x < targetWidth; x++)
                output[y * targetWidth + x] = input[Math.Min(sourceHeight - 1,
                    y * sourceHeight / targetHeight) * sourceWidth +
                    Math.Min(sourceWidth - 1, x * sourceWidth / targetWidth)];
        return output;
    }

    private static void ConfigureFrame(GpuVideoSurface gpu, Project project, Clip clip,
        double sourceSeconds, string projectPath)
    {
        var timelineSeconds = clip.TimelineStart.Seconds +
            (sourceSeconds - clip.SourceIn.Seconds) / clip.Speed.Seconds;
        var evaluated = KeyframeAnimation.EvaluateEffects(clip, timelineSeconds);
        gpu.ColorSettings = BasicColorEffect.Read(evaluated);
        gpu.ExposureSettings = ExposureEffect.Read(evaluated);
        gpu.LevelsSettings = LevelsEffect.Read(evaluated);
        gpu.MonochromeEnabled = MonochromeEffect.IsEnabled(evaluated);
        gpu.FinishingSettings = ColorFinishingEffect.Read(evaluated);
        gpu.GeometrySettings = ImageGeometryEffect.Read(evaluated);
        gpu.FilterSettings = GpuFilterEffect.Read(evaluated);
        gpu.DetailSettings = GpuDetailEffect.Read(evaluated);
        gpu.LutPath = GpuLutEffect.ResolvePath(project, evaluated, projectPath);
        gpu.LutTetrahedral = GpuLutEffect.IsTetrahedral(evaluated);
    }
}
