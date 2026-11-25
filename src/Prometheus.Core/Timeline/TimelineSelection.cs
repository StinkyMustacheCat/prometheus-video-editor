using System;
using System.Collections.Generic;

namespace Prometheus.Core.Timeline;

public class SelectedGap
{
    public string TrackId { get; set; } = string.Empty;
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);
}

public class TimelineSelection
{
    private readonly HashSet<string> _selectedClipIds = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> SelectedClipIds => _selectedClipIds;
    public SelectedGap? CurrentGap { get; private set; }

    public bool HasSelection => _selectedClipIds.Count > 0 || CurrentGap != null;
    public bool HasGapSelection => CurrentGap != null;

    public event Action? SelectionChanged;

    public void SelectClip(string clipId, bool addToSelection = false)
    {
        CurrentGap = null;
        if (!addToSelection)
        {
            _selectedClipIds.Clear();
        }
        _selectedClipIds.Add(clipId);
        SelectionChanged?.Invoke();
    }

    public void SelectClips(IEnumerable<string> clipIds, bool addToSelection = false)
    {
        CurrentGap = null;
        if (!addToSelection)
        {
            _selectedClipIds.Clear();
        }
        foreach (var id in clipIds)
        {
            _selectedClipIds.Add(id);
        }
        SelectionChanged?.Invoke();
    }

    public void ToggleClip(string clipId)
    {
        CurrentGap = null;
        if (!_selectedClipIds.Remove(clipId))
        {
            _selectedClipIds.Add(clipId);
        }
        SelectionChanged?.Invoke();
    }

    public void SelectGap(string trackId, double start, double end)
    {
        _selectedClipIds.Clear();
        CurrentGap = new SelectedGap
        {
            TrackId = trackId,
            StartSeconds = start,
            EndSeconds = end
        };
        SelectionChanged?.Invoke();
    }

    public void ToggleGap(string trackId, double start, double end)
    {
        if (CurrentGap != null && string.Equals(CurrentGap.TrackId, trackId, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(CurrentGap.StartSeconds - start) < 1.0 / 48000 && Math.Abs(CurrentGap.EndSeconds - end) < 1.0 / 48000)
            Clear();
        else SelectGap(trackId, start, end);
    }

    public void Clear()
    {
        if (_selectedClipIds.Count > 0 || CurrentGap != null)
        {
            _selectedClipIds.Clear();
            CurrentGap = null;
            SelectionChanged?.Invoke();
        }
    }

    public bool IsSelected(string clipId) => _selectedClipIds.Contains(clipId);
}
