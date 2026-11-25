using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public sealed record AudioTransitionSpan(AudioTransition Transition, Clip Clip, Clip? Right,
    double Start, double End);

public static class AudioTransitionOperations
{
    public const string FadeIn = "audio-fade-in";
    public const string FadeOut = "audio-fade-out";
    public const string Crossfade = "audio-crossfade";
    private const double Epsilon = 1e-7;
    private const double MinimumSeconds = 1d / 48000;

    public static bool Supports(string kind) => kind is FadeIn or FadeOut or Crossfade;

    public static string Name(string kind) => kind switch
    {
        FadeIn => "Fundido de entrada",
        FadeOut => "Fundido de salida",
        _ => "Fundido cruzado de audio"
    };

    public static AudioTransitionSpan? Span(IReadOnlyList<Clip> clips, AudioTransition transition)
    {
        if (!Supports(transition.Kind) || !double.IsFinite(transition.DurationSeconds) ||
            transition.DurationSeconds < MinimumSeconds - Epsilon) return null;
        var clip = clips.FirstOrDefault(c => c.Id == transition.ClipId);
        if (clip == null) return null;
        if (transition.Kind == FadeIn)
            return new(transition, clip, null, clip.TimelineStart.Seconds, clip.TimelineStart.Seconds + transition.DurationSeconds);
        if (transition.Kind == FadeOut)
            return new(transition, clip, null, clip.EndSeconds - transition.DurationSeconds, clip.EndSeconds);
        var right = clips.FirstOrDefault(c => c.Id == transition.RightClipId);
        if (right == null || !VideoTransitionOperations.IsCut(clip, right)) return null;
        var half = transition.DurationSeconds / 2;
        return new(transition, clip, right, right.TimelineStart.Seconds - half, right.TimelineStart.Seconds + half);
    }

    public static double MaximumSeconds(Project project, Sequence sequence, string kind, string clipId, string? rightId = null) =>
        MaximumSeconds(project, sequence.Clips, sequence.Tracks, sequence.AudioTransitions,
            new AudioTransition { Kind = kind, ClipId = clipId, RightClipId = rightId });

    private static double MaximumSeconds(Project project, IReadOnlyList<Clip> clips, IReadOnlyList<Track> tracks,
        IEnumerable<AudioTransition> others, AudioTransition candidate)
    {
        if (!Supports(candidate.Kind)) return 0;
        var clip = clips.FirstOrDefault(c => c.Id == candidate.ClipId);
        if (clip == null || !tracks.Any(t => t.Id == clip.TrackId && t.Kind == "audio")) return 0;
        var crossed = candidate.Kind == Crossfade;
        var right = crossed ? clips.FirstOrDefault(c => c.Id == candidate.RightClipId) : null;
        if (crossed && (right == null || !VideoTransitionOperations.IsCut(clip, right)) ||
            !crossed && candidate.RightClipId != null) return 0;

        var pivot = candidate.Kind == FadeIn ? clip.TimelineStart.Seconds : right?.TimelineStart.Seconds ?? clip.EndSeconds;
        var before = Math.Min(clip.Duration.Seconds, pivot);
        var after = clip.Duration.Seconds;
        if (right != null)
        {
            before = Math.Min(before, VideoTransitionOperations.SourceHandle(project, right, before: true));
            after = Math.Min(right.Duration.Seconds, VideoTransitionOperations.SourceHandle(project, clip, before: false));
        }
        foreach (var other in others)
        {
            if (SharesEdge(other, candidate) || Span(clips, other) is not { } span) continue;
            if (span.Clip.Id != clip.Id && span.Right?.Id != clip.Id &&
                (right == null || span.Clip.Id != right.Id && span.Right?.Id != right.Id)) continue;
            if (span.Start < pivot && candidate.Kind != FadeIn) before = Math.Min(before, pivot - span.End);
            if (span.End > pivot && candidate.Kind != FadeOut) after = Math.Min(after, span.Start - pivot);
        }
        var maximum = crossed ? 2 * Math.Min(before, after) : candidate.Kind == FadeIn ? after : before;
        return double.IsFinite(maximum) ? Math.Max(0, maximum) : 0;
    }

    public static AudioTransition Apply(Project project, string kind, string clipId, string? rightId = null,
        double? duration = null)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var clip = sequence.Clips.FirstOrDefault(c => c.Id == clipId);
        if (sequence.Tracks.Any(t => t.Id == clip?.TrackId && t.Locked))
            throw new InvalidOperationException("Desbloquea la pista para ajustar el fundido.");

        var maximum = MaximumSeconds(project, sequence, kind, clipId, rightId);
        if (maximum < MinimumSeconds)
            throw new InvalidOperationException(kind == Crossfade
                ? "El fundido cruzado necesita dos clips contiguos en la misma pista y audio disponible alrededor del corte."
                : "No hay espacio suficiente para el fundido en ese clip de audio.");
        var existing = sequence.AudioTransitions.FirstOrDefault(t => t.Kind == kind && t.ClipId == clipId && t.RightClipId == rightId);
        var wanted = duration ?? existing?.DurationSeconds ?? 0.5;
        if (!double.IsFinite(wanted)) throw new ArgumentException("La duración del fundido debe ser válida.");
        var transition = existing ?? new AudioTransition { Kind = kind, ClipId = clipId, RightClipId = rightId };
        transition.DurationSeconds = Math.Clamp(wanted, MinimumSeconds, maximum);
        sequence.AudioTransitions.RemoveAll(other => other.Id != transition.Id && SharesEdge(other, transition));
        if (existing == null) sequence.AudioTransitions.Add(transition);
        return transition;
    }

    private static string? InEdge(AudioTransition t) => t.Kind == FadeIn ? t.ClipId : t.RightClipId;
    private static string? OutEdge(AudioTransition t) => t.Kind == FadeIn ? null : t.ClipId;
    private static bool SharesEdge(AudioTransition a, AudioTransition b) =>
        InEdge(a) is { } incoming && incoming == InEdge(b) ||
        OutEdge(a) is { } outgoing && outgoing == OutEdge(b);

    public static void MoveEndToRightPiece(IEnumerable<AudioTransition> transitions, string originalId, string rightId)
    {
        foreach (var transition in transitions.Where(t => OutEdge(t) == originalId))
            transition.ClipId = rightId;
    }

    public static void CopyBetween(IEnumerable<AudioTransition> source, List<AudioTransition> destination,
        IReadOnlyDictionary<string, string> clips, double speed = 1)
    {
        foreach (var transition in source.ToArray())
        {
            if (!clips.TryGetValue(transition.ClipId, out var clip)) continue;
            string? right = null;
            if (transition.RightClipId is { } id && !clips.TryGetValue(id, out right)) continue;
            destination.Add(new AudioTransition { Kind = transition.Kind, ClipId = clip,
                RightClipId = right, DurationSeconds = transition.DurationSeconds / speed });
        }
    }

    public static void Reconcile(Project project, IReadOnlyList<Clip> clips, IReadOnlyList<Track> tracks,
        List<AudioTransition> transitions)
    {
        if (transitions.Count > 0)
        {
            var valid = new List<AudioTransition>();
            foreach (var transition in transitions)
            {
                if (valid.Any(t => SharesEdge(t, transition)) || Span(clips, transition) == null) continue;
                var maximum = MaximumSeconds(project, clips, tracks, valid, transition);
                if (maximum < MinimumSeconds) continue;
                transition.DurationSeconds = Math.Min(transition.DurationSeconds, maximum);
                valid.Add(transition);
            }
            transitions.Clear();
            transitions.AddRange(valid);
        }
        foreach (var clip in clips)
            if (clip.NestedContent is { } content)
                Reconcile(project, content.Clips, content.Tracks, content.AudioTransitions);
    }
}
