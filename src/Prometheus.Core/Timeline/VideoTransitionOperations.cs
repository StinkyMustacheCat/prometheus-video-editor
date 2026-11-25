using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public sealed record VideoTransitionWindow(VideoTransition Transition, Clip Left, Clip Right, double Fps,
    double Cut, double Start, double End)
{
    public int BeforeFrames => Transition.DurationFrames / 2 + Transition.DurationFrames % 2;
}

public sealed record VideoTransitionFrame(VideoTransitionWindow Window, double Time,
    double LeftWeight, double RightWeight, double SolidWeight);

public static class VideoTransitionOperations
{
    public const string Dissolve = "dissolve";
    public const string DipBlack = "dip-black";
    public const string DipWhite = "dip-white";
    private const double AdjacencyTolerance = 0.001;
    private const double Epsilon = 1e-7;

    public static bool Supports(string kind) => kind is Dissolve or DipBlack or DipWhite;
    public static int MinimumFrames(string kind) => kind == Dissolve ? 2 : 3;

    public static string Name(string kind) => kind switch
    {
        Dissolve => "Disolvencia cruzada",
        DipBlack => "Fundido por negro",
        DipWhite => "Fundido por blanco",
        _ => kind
    };

    public static int MinimumFrames(string kind, double fps, MgtGpuTransition? mgt) =>
        mgt == null ? MinimumFrames(kind) : Math.Max(2, (int)Math.Ceiling(mgt.Duration.Min * fps - Epsilon));

    public static string Name(VideoTransition transition) => transition.Mgt?.Name ?? Name(transition.Kind);

    public static bool IsCut(Clip left, Clip right) => left.Id != right.Id && left.TrackId == right.TrackId &&
        Math.Abs(left.EndSeconds - right.TimelineStart.Seconds) <= AdjacencyTolerance;

    public static VideoTransitionWindow? Window(Sequence sequence, VideoTransition transition)
    {
        if (!TryPair(sequence, transition.LeftClipId, transition.RightClipId, out var left, out var right) ||
            !Supports(transition.Kind) && transition.Mgt?.Id != transition.Kind ||
            transition.DurationFrames < MinimumFrames(transition.Kind, sequence.Fps.Seconds, transition.Mgt)) return null;
        var fps = Math.Max(1, sequence.Fps.Seconds);
        var before = Before(transition.DurationFrames);
        var cut = right.TimelineStart.Seconds;
        return new(transition, left, right, fps, cut, cut - before / fps,
            cut + (transition.DurationFrames - before) / fps);
    }

    public static int MaximumFrames(Project project, Sequence sequence, string leftId, string rightId, string kind,
        MgtGpuTransition? mgt = null) =>
        MaximumFrames(project, sequence, leftId, rightId, kind, sequence.VideoTransitions, mgt);

    private static int MaximumFrames(Project project, Sequence sequence, string leftId, string rightId,
        string kind, IEnumerable<VideoTransition> others, MgtGpuTransition? mgt)
    {
        if ((!Supports(kind) && mgt?.Id != kind) || !TryPair(sequence, leftId, rightId, out var left, out var right)) return 0;
        if (PlaceholderService.IsPlaceholder(project.Assets.FirstOrDefault(a => a.Id == left.AssetId)) ||
            PlaceholderService.IsPlaceholder(project.Assets.FirstOrDefault(a => a.Id == right.AssetId))) return 0;
        var fps = Math.Max(1, sequence.Fps.Seconds);
        var cut = right.TimelineStart.Seconds;
        var beforeRoom = Math.Min(left.Duration.Seconds, cut);
        var afterRoom = right.Duration.Seconds;
        if (kind == Dissolve || mgt != null)
        {
            beforeRoom = Math.Min(beforeRoom, SourceHandle(project, right, before: true));
            afterRoom = Math.Min(afterRoom, SourceHandle(project, left, before: false));
        }
        foreach (var item in others)
        {
            if (item.LeftClipId == leftId && item.RightClipId == rightId) continue;
            if (Window(sequence, item) is not { } other || other.Left.TrackId != left.TrackId) continue;
            if (other.Cut < cut) beforeRoom = Math.Min(beforeRoom, cut - other.End);
            else afterRoom = Math.Min(afterRoom, other.Start - cut);
        }
        var before = Math.Min(int.MaxValue / 4, Math.Floor(beforeRoom * fps + Epsilon));
        var after = Math.Min(int.MaxValue / 4, Math.Floor(afterRoom * fps + Epsilon));
        if (before < 1 || after < 1) return 0;
        var available = Math.Min(before * 2, after * 2 + 1);
        if (mgt != null) available = Math.Min(available, Math.Floor(mgt.Duration.Max * fps + Epsilon));
        return (int)available;
    }

    public static VideoTransition Apply(Project project, string leftId, string rightId,
        string kind, int? durationFrames = null, MgtGpuTransition? mgt = null)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        if (!TryPair(sequence, leftId, rightId, out var left, out _))
            throw new InvalidOperationException("La transición necesita un corte entre dos clips de la misma pista de video.");
        if (sequence.Tracks.Any(track => track.Id == left.TrackId && track.Locked))
            throw new InvalidOperationException("Desbloquea la pista para editar la transición.");
        mgt?.Validate();
        var min = MinimumFrames(kind, sequence.Fps.Seconds, mgt);
        var max = MaximumFrames(project, sequence, leftId, rightId, kind, mgt);
        if (max < min)
            throw new InvalidOperationException(mgt != null
                ? "La transición MGT necesita margen de video antes y después del corte."
                : kind == Dissolve
                ? "La disolvencia necesita fotogramas disponibles después del clip saliente y antes del entrante, además de espacio entre transiciones. Prueba un fundido por negro o blanco."
                : "No hay espacio suficiente alrededor del corte para este fundido.");
        var transition = sequence.VideoTransitions.FirstOrDefault(t => t.LeftClipId == leftId && t.RightClipId == rightId);
        var defaultFrames = (int)Math.Round(sequence.Fps.Seconds * (mgt?.Recommended.Duration ?? 0.5));
        var requested = durationFrames ?? (transition?.Kind == kind ? transition.DurationFrames : defaultFrames);
        if (transition == null)
        {
            transition = new VideoTransition { LeftClipId = leftId, RightClipId = rightId };
            sequence.VideoTransitions.Add(transition);
        }
        if (transition.Mgt?.Id != mgt?.Id) transition.MgtOverrides.Clear();
        transition.Kind = kind;
        transition.Mgt = mgt;
        transition.DurationFrames = Math.Clamp(requested, min, max);
        return transition;
    }

    public static IEnumerable<VideoTransitionFrame> FramesAt(Sequence sequence, double time)
    {
        foreach (var transition in sequence.VideoTransitions)
        {
            if (Window(sequence, transition) is not { } window ||
                time < window.Start - Epsilon || time >= window.End - Epsilon ||
                !window.Left.Enabled || !window.Right.Enabled) continue;
            var index = Math.Clamp((int)Math.Floor((time - window.Start) * window.Fps + Epsilon), 0, transition.DurationFrames - 1);
            var at = window.Cut + (index - window.BeforeFrames) / window.Fps;
            if (transition.Kind == Dissolve || transition.Mgt != null)
            {
                var right = (double)index / (transition.DurationFrames - 1);
                yield return new(window, at, 1 - right, right, 0);
            }
            else if (index < window.BeforeFrames)
            {
                var left = 1 - (double)index / Math.Max(1, window.BeforeFrames - 1);
                yield return new(window, at, left, 0, 1 - left);
            }
            else
            {
                var after = transition.DurationFrames - window.BeforeFrames;
                var right = after == 1 ? 1 : (double)(index - window.BeforeFrames) / (after - 1);
                yield return new(window, at, 0, right, 1 - right);
            }
        }
    }

    public static void CopyBetween(Sequence source, Sequence destination, IReadOnlyDictionary<string, string> clips)
    {
        var rate = Math.Max(1, destination.Fps.Seconds) / Math.Max(1, source.Fps.Seconds);
        foreach (var transition in source.VideoTransitions.ToArray())
        {
            if (!clips.TryGetValue(transition.LeftClipId, out var left) ||
                !clips.TryGetValue(transition.RightClipId, out var right)) continue;
            destination.VideoTransitions.Add(new VideoTransition { LeftClipId = left, RightClipId = right,
                Kind = transition.Kind, Mgt = transition.Mgt,
                MgtOverrides = new Dictionary<string, System.Text.Json.JsonElement>(transition.MgtOverrides),
                DurationFrames = Math.Max(MinimumFrames(transition.Kind, destination.Fps.Seconds, transition.Mgt),
                    (int)Math.Min(int.MaxValue / 2, Math.Round(transition.DurationFrames * rate))) });
        }
    }

    public static void Reconcile(Project project, Sequence sequence)
    {
        if (sequence.VideoTransitions.Count == 0) return;
        var valid = new List<VideoTransition>();
        foreach (var transition in sequence.VideoTransitions.OrderBy(t => Window(sequence, t)?.Cut ?? double.MaxValue))
        {
            if (valid.Any(t => t.LeftClipId == transition.LeftClipId && t.RightClipId == transition.RightClipId)) continue;
            var minimum = MinimumFrames(transition.Kind, sequence.Fps.Seconds, transition.Mgt);
            var maximum = MaximumFrames(project, sequence, transition.LeftClipId, transition.RightClipId, transition.Kind, valid, transition.Mgt);
            if (transition.DurationFrames < minimum || maximum < minimum) continue;
            transition.DurationFrames = Math.Min(transition.DurationFrames, maximum);
            valid.Add(transition);
        }
        sequence.VideoTransitions = valid;
    }

    private static int Before(int frames) => frames / 2 + frames % 2;

    private static bool TryPair(Sequence sequence, string leftId, string rightId, out Clip left, out Clip right)
    {
        left = sequence.Clips.FirstOrDefault(c => c.Id == leftId)!;
        right = sequence.Clips.FirstOrDefault(c => c.Id == rightId)!;
        if (left == null || right == null || !IsCut(left, right)) return false;
        var trackId = left.TrackId;
        return sequence.Tracks.Any(t => t.Id == trackId && t.Kind == "video" && t.Role != "subtitle");
    }

    internal static double SourceHandle(Project project, Clip clip, bool before)
    {
        if (StillImageRules.IsStill(project, clip)) return double.PositiveInfinity;
        var speed = clip.Speed.Seconds;
        if (speed <= 0) return 0;
        if (before)
        {
            var first = clip.NestedContent is { } nested
                ? nested.Clips.Where(c => nested.Tracks.Any(t => t.Id == c.TrackId && t.Kind == clip.NestedRole))
                    .Select(c => c.TimelineStart.Seconds).DefaultIfEmpty(0).Min()
                : 0;
            return Math.Max(0, (clip.SourceIn.Seconds - first) / speed);
        }
        var asset = project.Assets.FirstOrDefault(a => a.Id == clip.AssetId);
        var stream = asset?.Streams.FirstOrDefault(s => s.Id == clip.StreamId);
        var end = clip.NestedContent is { } content
            ? content.Clips.Where(c => content.Tracks.Any(t => t.Id == c.TrackId && t.Kind == clip.NestedRole))
                .Select(c => c.EndSeconds).DefaultIfEmpty(0).Max()
            : clip.AvailableSourceDurationSeconds ?? stream?.Duration.Seconds ?? 0;
        return Math.Max(0, (end - clip.SourceIn.Seconds) / speed - clip.Duration.Seconds);
    }
}
