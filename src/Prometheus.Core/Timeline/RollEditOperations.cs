using System;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Timeline;

/// <summary>Shared preview/commit limits. Existing transitions retain their full duration.</summary>
public static class RollEditOperations
{
    public sealed record Plan(double CutSeconds, bool Limited);

    public static Plan? Prepare(Project project, Sequence sequence, string leftId, string rightId, double requestedCut)
    {
        var left = sequence.Clips.FirstOrDefault(c => c.Id == leftId);
        var right = sequence.Clips.FirstOrDefault(c => c.Id == rightId);
        if (!double.IsFinite(requestedCut) || left == null || right == null ||
            !VideoTransitionOperations.IsCut(left, right) || left.Speed.Seconds <= 0 || right.Speed.Seconds <= 0 ||
            !sequence.Tracks.Any(t => t.Id == left.TrackId && !t.Locked)) return null;
        var cut = left.EndSeconds;
        var frame = 1 / Math.Max(1, sequence.Fps.Seconds);
        var low = frame - left.Duration.Seconds;
        var high = right.Duration.Seconds - frame;
        var rightStill = StillImageRules.IsStill(project, right);
        if (!rightStill) low = Math.Max(low, -VideoTransitionOperations.SourceHandle(project, right, before: true));
        var stream = project.Assets.FirstOrDefault(a => a.Id == left.AssetId)?.Streams.FirstOrDefault(s => s.Id == left.StreamId);
        if (!StillImageRules.IsStill(project, left) && (left.NestedContent != null || left.AvailableSourceDurationSeconds.HasValue || stream?.Duration.Seconds > 0))
            high = Math.Min(high, VideoTransitionOperations.SourceHandle(project, left, before: false));

        var video = sequence.VideoTransitions.Where(t => VideoTransitionOperations.Window(sequence, t) is { } w &&
            w.Left.TrackId == left.TrackId && VideoTransitionOperations.MaximumFrames(project, sequence,
                t.LeftClipId, t.RightClipId, t.Kind, t.Mgt) >= t.DurationFrames).ToArray();
        double before = 0, after = 0;
        foreach (var transition in video.Where(t => t.LeftClipId == leftId && t.RightClipId == rightId))
        {
            var window = VideoTransitionOperations.Window(sequence, transition)!;
            var b = window.BeforeFrames / window.Fps;
            var a = (transition.DurationFrames - window.BeforeFrames) / window.Fps;
            before = Math.Max(before, b); after = Math.Max(after, a);
            if (transition.Kind == VideoTransitionOperations.Dissolve || transition.Mgt != null)
            {
                low = Math.Max(low, b - VideoTransitionOperations.SourceHandle(project, right, before: true));
                high = Math.Min(high, VideoTransitionOperations.SourceHandle(project, left, before: false) - a);
            }
        }
        low = Math.Max(low, before - left.Duration.Seconds);
        high = Math.Min(high, right.Duration.Seconds - after);
        foreach (var transition in video.Where(t => t.LeftClipId != leftId || t.RightClipId != rightId))
        {
            var window = VideoTransitionOperations.Window(sequence, transition)!;
            if (window.Cut < cut) low = Math.Max(low, window.End + before - cut);
            else high = Math.Min(high, window.Start - after - cut);
        }

        bool Touches(AudioTransition t) => t.ClipId == leftId || t.ClipId == rightId || t.RightClipId == leftId || t.RightClipId == rightId;
        bool Moving(AudioTransition t) => t.Kind == AudioTransitionOperations.Crossfade && t.ClipId == leftId && t.RightClipId == rightId ||
            t.Kind == AudioTransitionOperations.FadeOut && t.ClipId == leftId || t.Kind == AudioTransitionOperations.FadeIn && t.ClipId == rightId;
        var audio = sequence.AudioTransitions.Where(t => Touches(t) && AudioTransitionOperations.Span(sequence.Clips, t) != null &&
            AudioTransitionOperations.MaximumSeconds(project, sequence, t.Kind, t.ClipId, t.RightClipId) + 1e-9 >= t.DurationSeconds).ToArray();
        double audioBefore = 0, audioAfter = 0;
        foreach (var transition in audio.Where(Moving))
        {
            if (transition.Kind == AudioTransitionOperations.Crossfade)
            {
                var half = transition.DurationSeconds / 2;
                audioBefore = Math.Max(audioBefore, half); audioAfter = Math.Max(audioAfter, half);
                low = Math.Max(low, half - VideoTransitionOperations.SourceHandle(project, right, before: true));
                high = Math.Min(high, VideoTransitionOperations.SourceHandle(project, left, before: false) - half);
            }
            else if (transition.Kind == AudioTransitionOperations.FadeOut) audioBefore = Math.Max(audioBefore, transition.DurationSeconds);
            else audioAfter = Math.Max(audioAfter, transition.DurationSeconds);
        }
        low = Math.Max(low, audioBefore - left.Duration.Seconds);
        high = Math.Min(high, right.Duration.Seconds - audioAfter);
        foreach (var transition in audio.Where(t => !Moving(t)))
        {
            var span = AudioTransitionOperations.Span(sequence.Clips, transition)!;
            if (span.End <= cut + 1e-9) low = Math.Max(low, span.End + audioBefore - cut);
            else if (span.Start >= cut - 1e-9) high = Math.Min(high, span.Start - audioAfter - cut);
        }
        if (low > high + 1e-7) return new(cut, Math.Abs(requestedCut - cut) > 1e-7);
        var delta = Math.Clamp(requestedCut - cut, Math.Min(low, high), Math.Max(low, high));

        // Match the existing 48 kHz edit clock, including its rounding at fractional FPS.
        // If a boundary rounds outside a transition's valid margin, choose the safe side.
        if (!KeepsTransitions(delta))
        {
            var safe = 0d; var invalid = delta;
            for (var i = 0; i < 32; i++)
            {
                var mid = (safe + invalid) / 2;
                if (KeepsTransitions(mid)) safe = mid; else invalid = mid;
            }
            delta = safe;
        }
        return new(cut + delta, Math.Abs(cut + delta - requestedCut) > 1e-7);

        bool KeepsTransitions(double offset)
        {
            if (Math.Abs(offset) < .0001 || video.Length == 0 && audio.Length == 0) return true;
            var l = left.ShallowCopy(); var r = right.ShallowCopy();
            l.Duration = Rational.Time(Math.Max(frame, left.Duration.Seconds + offset));
            r.TimelineStart = Rational.Time(l.EndSeconds);
            r.SourceIn = Rational.Time(rightStill && right.CaptionWords == null ? 0 : Math.Max(0, right.SourceIn.Seconds + offset * right.Speed.Seconds));
            r.Duration = Rational.Time(Math.Max(frame, right.Duration.Seconds - offset));
            var candidate = new Sequence { Fps = sequence.Fps, Tracks = sequence.Tracks,
                Clips = sequence.Clips.Select(c => c.Id == leftId ? l : c.Id == rightId ? r : c).ToList(),
                VideoTransitions = sequence.VideoTransitions, AudioTransitions = sequence.AudioTransitions };
            return video.All(t => VideoTransitionOperations.MaximumFrames(project, candidate, t.LeftClipId, t.RightClipId, t.Kind, t.Mgt) >= t.DurationFrames) &&
                audio.All(t => AudioTransitionOperations.MaximumSeconds(project, candidate, t.Kind, t.ClipId, t.RightClipId) + 1e-9 >= t.DurationSeconds);
        }
    }

    public static bool Apply(Project project, Sequence sequence, string leftId, string rightId, double requestedCut)
    {
        var plan = Prepare(project, sequence, leftId, rightId, requestedCut);
        return plan != null && CollisionEngine.RollEdit(sequence, leftId, rightId, plan.CutSeconds,
            c => StillImageRules.IsStill(project, c));
    }
}
