using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public sealed record NestedRenderLayer(Clip Clip, Track Track, double SourceSeconds, double LocalTimelineSeconds,
    IReadOnlyList<Clip> Containers, IReadOnlyList<double> ContainerTimelineSeconds, IReadOnlyList<int> OrderPath,
    VideoTransitionFrame? Transition = null);

public static class NestedRenderPlan
{
    public static IReadOnlyList<double> UpcomingStarts(Project project, double from, double to, string kind) =>
        UpcomingStarts(project, from, to, kind, true);

    public static IReadOnlyList<double> UpcomingStarts(Project project, double from, double to, string kind, bool includeSubtitles)
    {
        var sequence = project.ActiveSequence;
        if (sequence == null || to <= from) return Array.Empty<double>();
        var starts = new List<double>();
        VisitStarts(sequence.Tracks, sequence.Clips, kind, from, to, 0, 1,
            double.NegativeInfinity, double.PositiveInfinity, starts, 0, includeSubtitles);
        if (kind == "video")
            foreach (var transition in sequence.VideoTransitions)
                if (VideoTransitionOperations.Window(sequence, transition) is { } window &&
                    window.Start > from && window.Start <= to)
                    starts.Add(window.Start);
        return starts.Distinct().OrderBy(value => value).ToArray();
    }

    private static void VisitStarts(IReadOnlyList<Track> tracks, IReadOnlyList<Clip> clips,
        string kind, double from, double to, double bias, double rate,
        double visibleStart, double visibleEnd, List<double> starts, int depth, bool includeSubtitles)
    {
        if (depth >= 16) return;
        foreach (var track in tracks.Where(t => t.Kind == kind && t.Enabled && (includeSubtitles || depth > 0 || t.Role != "subtitle")))
        foreach (var clip in clips.Where(c => c.TrackId == track.Id && c.Enabled && c.Speed.Seconds > 0))
        {
            var start = (clip.TimelineStart.Seconds - bias) / rate;
            var end = (clip.EndSeconds - bias) / rate;
            var clippedStart = Math.Max(start, visibleStart);
            var clippedEnd = Math.Min(end, visibleEnd);
            if (clippedEnd <= from || clippedStart > to || clippedStart >= clippedEnd) continue;
            if (clippedStart > from && clippedStart <= to) starts.Add(clippedStart);
            if (clip.NestedContent == null || clip.NestedRole != kind) continue;
            var speed = clip.Speed.Seconds;
            var nextBias = clip.SourceIn.Seconds + (bias - clip.TimelineStart.Seconds) * speed;
            VisitStarts(clip.NestedContent.Tracks, clip.NestedContent.Clips, kind, from, to,
                nextBias, rate * speed, clippedStart, clippedEnd, starts, depth + 1, includeSubtitles);
        }
    }

    public static IReadOnlyList<NestedRenderLayer> At(Project project, double time, string kind) => At(project, time, kind, true);

    public static IReadOnlyList<NestedRenderLayer> At(Project project, double time, string kind, bool includeSubtitles)
    {
        var sequence = project.ActiveSequence;
        if (sequence == null) return Array.Empty<NestedRenderLayer>();
        var result = new List<NestedRenderLayer>();
        var transitions = kind == "video" && sequence.VideoTransitions.Count > 0
            ? VideoTransitionOperations.FramesAt(sequence, time).ToLookup(frame => frame.Window.Left.TrackId)
            : null;
        foreach (var track in sequence.Tracks.Where(t => t.Kind == kind && t.Enabled && (includeSubtitles || t.Role != "subtitle")).OrderBy(t => t.Order))
        {
            var frame = transitions?[track.Id].FirstOrDefault();
            foreach (var clip in sequence.Clips.Where(c => c.TrackId == track.Id && c.Enabled))
            {
                var involved = frame != null && (clip.Id == frame.Window.Left.Id || clip.Id == frame.Window.Right.Id);
                if (involved)
                {
                    var useLeft = frame!.Time < frame.Window.Cut - 1e-7;
                    if (frame.Window.Transition.Kind != VideoTransitionOperations.Dissolve && frame.Window.Transition.Mgt == null &&
                        (clip.Id == frame.Window.Left.Id) != useLeft) continue;
                }
                else if (clip.TimelineStart.Seconds > time || time >= clip.EndSeconds) continue;

                var first = result.Count;
                Visit(project, clip, track, involved ? frame!.Time : time, kind, new List<Clip>(), new List<double>(),
                    new List<int> { track.Role == "subtitle" ? int.MaxValue / 2 : track.Order }, result, 0);
                if (involved)
                    for (var i = first; i < result.Count; i++) result[i] = result[i] with { Transition = frame };
            }
        }
        return result.OrderBy(x => string.Join("/", x.OrderPath.Select(n => n.ToString("D8")))).ToList();
    }


    public static IReadOnlyList<NestedRenderLayer> TransitionFallback(Project project, VideoTransitionFrame frame, Clip clip)
    {
        var layers = new List<NestedRenderLayer>();
        var lastVisible = Math.Max(clip.TimelineStart.Seconds, clip.EndSeconds - 1 / frame.Window.Fps);
        var time = Math.Clamp(frame.Time, clip.TimelineStart.Seconds, lastVisible);
        var track = project.ActiveSequence!.Tracks.First(item => item.Id == clip.TrackId);
        Visit(project, clip, track, time, "video", [], [], [track.Order], layers, 0);
        return layers;
    }

    private static void Visit(Project project, Clip clip, Track track, double time, string kind,
        List<Clip> ancestors, List<double> ancestorTimes, List<int> order,
        List<NestedRenderLayer> result, int depth)
    {
        if (depth >= 16) throw new InvalidOperationException("Demasiados niveles de medios anidados.");
        var sourceTime = clip.SourceIn.Seconds + (time - clip.TimelineStart.Seconds) * clip.Speed.Seconds;
        if (clip.NestedContent == null)
        {
            result.Add(new NestedRenderLayer(clip, track, sourceTime, time, ancestors.ToArray(),
                ancestorTimes.ToArray(), order.ToArray()));
            return;
        }
        if (clip.NestedRole != kind) return;
        var nextAncestors = new List<Clip>(ancestors) { clip };
        var nextTimes = new List<double>(ancestorTimes) { time };
        var content = clip.NestedContent;
        foreach (var lane in content.Tracks.Where(t => t.Kind == kind && t.Enabled).OrderBy(t => t.Order))
        foreach (var child in content.Clips.Where(c => c.TrackId == lane.Id && c.Enabled && c.TimelineStart.Seconds <= sourceTime && sourceTime < c.EndSeconds))
        {
            var nextOrder = new List<int>(order) { lane.Order };
            Visit(project, child, lane, sourceTime, kind, nextAncestors, nextTimes, nextOrder, result, depth + 1);
        }
    }
}
