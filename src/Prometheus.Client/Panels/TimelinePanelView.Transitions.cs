using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Prometheus.Core.Model;
using Prometheus.Client.Services;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView
{
    private (Clip Left, Clip Right)? FindMediaCut(Track? track, double time, string kind)
    {
        if (track == null || track.Kind != kind || track.Locked || track.Role == "subtitle" ||
            _controller?.CurrentProject.ActiveSequence is not { } sequence) return null;
        var clips = sequence.Clips.Where(clip => clip.TrackId == track.Id).OrderBy(clip => clip.TimelineStart.Seconds).ToArray();
        var bestDistance = 16 / TimelineCanvas.ZoomPixelsPerSecond;
        (Clip Left, Clip Right)? result = null;
        for (var i = 1; i < clips.Length; i++)
        {
            var left = clips[i - 1];
            var right = clips[i];
            var distance = Math.Abs(right.TimelineStart.Seconds - time);
            if (distance > bestDistance || !VideoTransitionOperations.IsCut(left, right)) continue;
            bestDistance = distance;
            result = (left, right);
        }
        return result;
    }

    private IEnumerable<MenuItem> TransitionMenuChoices(string left, string right)
    {
        foreach (var kind in new[] { VideoTransitionOperations.Dissolve, VideoTransitionOperations.DipBlack, VideoTransitionOperations.DipWhite })
            yield return Menu(VideoTransitionOperations.Name(kind), () => ApplyVideoTransition(left, right, kind));
        var templates = MgtTransitionCatalog.All(_controller?.CurrentProject).ToArray();
        if (templates.Length > 0)
            yield return Submenu("MGT", templates.Select(t => Menu(t.Name, () => ApplyVideoTransition(left, right, t.Id))));
    }

    private void ApplyVideoTransition(string left, string right, string kind)
    {
        string? id = null;
        if (CommitTimelineEdit(project => id = VideoTransitionOperations.Apply(project, left, right, kind,
                mgt: MgtTransitionCatalog.Find(project, kind)).Id, "Aplicar transición") && id != null)
            SelectTransition(id);
    }

    private void SelectTransition(string? id)
    {
        if (_controller == null) return;
        if (id == null)
        {
            if (_controller.SelectedTransitionId != null) _controller.SetSelectedClip(null);
            return;
        }
        TimelineCanvas.Selection.Clear();
        _controller.SetSelectedTransition(id);
    }

    private void RefreshTransitionSelection()
    {
        TimelineCanvas.SelectedTransitionId = _controller?.SelectedTransitionId;
    }

    private void ShowTransitionMenu(string id)
    {
        if (ShowAudioTransitionMenu(id)) return;
        if (_controller?.CurrentProject.ActiveSequence is not { } sequence ||
            sequence.VideoTransitions.FirstOrDefault(t => t.Id == id) is not { } transition ||
            VideoTransitionOperations.Window(sequence, transition) is not { } window) return;
        SelectTransition(id);
        TimelineCanvas.Focus();
        var mutable = sequence.Tracks.Any(t => t.Id == window.Left.TrackId && !t.Locked);
        var replace = Submenu("Cambiar transición", TransitionMenuChoices(transition.LeftClipId, transition.RightClipId));
        replace.IsEnabled = mutable;
        var remove = Menu("Quitar transición", () => RemoveTransition(id));
        remove.IsEnabled = mutable;
        ShowMenu(TimelineCanvas, new[] { replace, remove });
    }

    private void RemoveTransition(string id)
    {
        CommitTimelineEdit(project =>
        {
            var sequence = project.ActiveSequence!;
            if (sequence.AudioTransitions.FirstOrDefault(t => t.Id == id) is { } audio &&
                AudioTransitionOperations.Span(sequence.Clips, audio) is { } span)
            {
                if (sequence.Tracks.Any(t => t.Id == span.Clip.TrackId && t.Locked))
                    throw new InvalidOperationException("Desbloquea la pista para quitar el fundido.");
                sequence.AudioTransitions.Remove(audio);
                return;
            }
            var transition = sequence.VideoTransitions.FirstOrDefault(t => t.Id == id);
            if (transition == null) return;
            var window = VideoTransitionOperations.Window(sequence, transition);
            if (window != null && sequence.Tracks.Any(t => t.Id == window.Left.TrackId && t.Locked))
                throw new InvalidOperationException("Desbloquea la pista para quitar la transición.");
            sequence.VideoTransitions.Remove(transition);
        }, "Quitar transición");
    }
}
