using Prometheus.Core.Model;

namespace Prometheus.Core.Undo;

// Navigation and track layout are persisted with the project, but are not edits.
internal sealed class ProjectViewState
{
    private sealed record TrackView(int Height, bool Compact, bool Names);
    private sealed record SequenceView(double? Divider, bool Subtitles, double Playhead, double Offset,
        double Zoom, Dictionary<string, TrackView> Tracks);
    private readonly string _active;
    private readonly Dictionary<string, SequenceView> _sequences;

    public ProjectViewState(Project project)
    {
        _active = project.ActiveSequenceId;
        _sequences = project.Sequences.ToDictionary(s => s.Id, s => new SequenceView(s.VideoAreaHeight,
            s.SubtitleLaneExpanded, s.ViewPlayheadSeconds, s.ViewOffsetSeconds, s.ViewZoomPixelsPerSecond,
            s.Tracks.ToDictionary(t => t.Id, t => new TrackView(t.PresentationHeight, t.Compact, t.ShowNames))));
    }

    public void Apply(Project project)
    {
        if (project.Sequences.Any(s => s.Id == _active)) project.ActiveSequenceId = _active;
        foreach (var sequence in project.Sequences)
        {
            if (!_sequences.TryGetValue(sequence.Id, out var view)) continue;
            sequence.VideoAreaHeight = view.Divider;
            sequence.SubtitleLaneExpanded = view.Subtitles;
            sequence.ViewPlayheadSeconds = view.Playhead;
            sequence.ViewOffsetSeconds = view.Offset;
            sequence.ViewZoomPixelsPerSecond = view.Zoom;
            foreach (var track in sequence.Tracks)
                if (view.Tracks.TryGetValue(track.Id, out var saved))
                {
                    track.PresentationHeight = saved.Height;
                    track.Compact = saved.Compact;
                    track.ShowNames = saved.Names;
                }
        }
    }

    public bool Matches(Project project)
    {
        if (_active != project.ActiveSequenceId) return false;
        foreach (var sequence in project.Sequences)
        {
            if (!_sequences.TryGetValue(sequence.Id, out var view)) continue;
            if (sequence.VideoAreaHeight != view.Divider || sequence.SubtitleLaneExpanded != view.Subtitles ||
                sequence.ViewPlayheadSeconds != view.Playhead || sequence.ViewOffsetSeconds != view.Offset ||
                sequence.ViewZoomPixelsPerSecond != view.Zoom) return false;
            foreach (var track in sequence.Tracks)
                if (view.Tracks.TryGetValue(track.Id, out var saved) &&
                    (track.PresentationHeight != saved.Height || track.Compact != saved.Compact || track.ShowNames != saved.Names))
                    return false;
        }
        return true;
    }
}
