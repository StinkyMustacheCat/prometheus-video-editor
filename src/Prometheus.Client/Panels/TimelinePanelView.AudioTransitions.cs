using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView
{
    private (Clip Clip, Clip? Right, double Time)? FindAudioTransitionTarget(Point point, string kind)
    {
        var track = TimelineCanvas.TrackAtY(point.Y);
        if (track is not { Kind: "audio", Locked: false }) return null;
        if (kind == AudioTransitionOperations.Crossfade)
            return FindMediaCut(track, TimelineCanvas.PixelsToTime(point.X), "audio") is { } cut
                ? (cut.Left, cut.Right, cut.Right.TimelineStart.Seconds) : null;
        var clip = TimelineCanvas.ClipAt(point);
        return clip?.TrackId == track.Id
            ? (clip, null, kind == AudioTransitionOperations.FadeIn ? clip.TimelineStart.Seconds : clip.EndSeconds)
            : null;
    }

    private void ApplyAudioTransition(string clip, string? right, string kind)
    {
        string? id = null;
        if (CommitTimelineEdit(project => id = AudioTransitionOperations.Apply(project, kind, clip, right).Id,
                "Aplicar fundido de audio") && id != null)
            SelectTransition(id);
    }

    private IEnumerable<MenuItem> AudioTransitionMenuChoices(Clip clip, Track track, double time)
    {
        yield return Menu(AudioTransitionOperations.Name(AudioTransitionOperations.FadeIn),
            () => ApplyAudioTransition(clip.Id, null, AudioTransitionOperations.FadeIn));
        yield return Menu(AudioTransitionOperations.Name(AudioTransitionOperations.FadeOut),
            () => ApplyAudioTransition(clip.Id, null, AudioTransitionOperations.FadeOut));
        if (FindMediaCut(track, time, "audio") is { } cut)
            yield return Menu(AudioTransitionOperations.Name(AudioTransitionOperations.Crossfade),
                () => ApplyAudioTransition(cut.Left.Id, cut.Right.Id, AudioTransitionOperations.Crossfade));
    }

    private bool ShowAudioTransitionMenu(string id)
    {
        if (_controller?.CurrentProject.ActiveSequence is not { } sequence ||
            sequence.AudioTransitions.FirstOrDefault(t => t.Id == id) is not { } transition ||
            AudioTransitionOperations.Span(sequence.Clips, transition) is not { } span) return false;
        SelectTransition(id);
        TimelineCanvas.Focus();
        var item = Menu("Quitar fundido", () => RemoveTransition(id));
        item.IsEnabled = sequence.Tracks.Any(t => t.Id == span.Clip.TrackId && !t.Locked);
        ShowMenu(TimelineCanvas, new[] { item });
        return true;
    }
}
