using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Timeline;

public partial class TimelineCanvasControl
{
    private TimelineDrawingData? _drawingData;
    internal int DrawingRevision { get; private set; }
    private TimelineDrawingData GetDrawingData() => _drawingData ??= new(_sequence!, _project);
}

internal sealed class TimelineDrawingData
{
    private readonly Dictionary<string, TrackClips> _tracks;
    private readonly Dictionary<string, Asset> _assets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EditGroup> _groups = new(StringComparer.Ordinal);
    public IReadOnlyList<MicroGapInfo> MicroGaps { get; }

    public TimelineDrawingData(Sequence sequence, Project? project)
    {
        _tracks = sequence.Clips.GroupBy(clip => clip.TrackId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(track => track.Key, track => new TrackClips(track.ToArray()), StringComparer.OrdinalIgnoreCase);
        foreach (var asset in project?.Assets ?? []) _assets.TryAdd(asset.Id, asset);
        foreach (var group in sequence.EditGroups)
            foreach (var clipId in group.ClipIds)
                _groups.TryAdd(clipId, group);
        MicroGaps = CollisionEngine.DetectMicroGaps(sequence);
    }

    public IEnumerable<Clip> OnTrack(string id) => _tracks.TryGetValue(id, out var track) ? track.Original : [];
    public IReadOnlyList<Clip> VisibleClips(string id, double start, double end) =>
        _tracks.TryGetValue(id, out var track) ? track.Intersecting(start, end) : [];
    public Asset? AssetFor(string id) => _assets.GetValueOrDefault(id);
    public EditGroup? GroupFor(string id) => _groups.GetValueOrDefault(id);

    private sealed class TrackClips
    {
        public Clip[] Original { get; }
        private readonly (Clip Clip, int Order)[] _ordered;
        private readonly double[] _ends;

        public TrackClips(Clip[] clips)
        {
            Original = clips;
            _ordered = clips.Select((clip, order) => (Clip: clip, Order: order))
                .OrderBy(item => item.Clip.TimelineStart.Seconds).ToArray();
            _ends = new double[_ordered.Length];
            var furthest = double.NegativeInfinity;
            for (var i = 0; i < _ordered.Length; i++)
                _ends[i] = furthest = Math.Max(furthest, _ordered[i].Clip.EndSeconds);
        }

        public IReadOnlyList<Clip> Intersecting(double start, double end)
        {
            var low = 0;
            var high = _ends.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (_ends[middle] < start) low = middle + 1;
                else high = middle;
            }

            var matches = new List<(Clip Clip, int Order)>();
            var ordered = true;
            for (var i = low; i < _ordered.Length; i++)
            {
                var item = _ordered[i];
                if (item.Clip.TimelineStart.Seconds > end) break;
                if (item.Clip.EndSeconds < start) continue;
                if (matches.Count > 0 && item.Order < matches[^1].Order) ordered = false;
                matches.Add(item);
            }
            if (!ordered) matches.Sort((a, b) => a.Order.CompareTo(b.Order));
            return matches.Select(item => item.Clip).ToArray();
        }
    }
}
