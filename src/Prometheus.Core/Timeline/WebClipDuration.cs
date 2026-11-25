using System;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Timeline;

public static class WebClipDuration
{
    public static void Apply(Project project, string assetId, double sourceDuration, bool completed)
    {
        if (!double.IsFinite(sourceDuration) || sourceDuration <= 0) return;
        foreach (var sequence in project.Sequences)
        {
            foreach (var clip in sequence.Clips.Where(c => c.AssetId == assetId && c.WebAutoDurationSeconds.HasValue))
            {
                if (Math.Abs(clip.Duration.Seconds - clip.WebAutoDurationSeconds!.Value) > 1.0 / 48000)
                {
                    clip.WebAutoDurationSeconds = null;
                    clip.WebRequestedOutSeconds = null;
                    continue;
                }
                var next = sequence.Clips.Where(c => c.TrackId == clip.TrackId && c.Id != clip.Id && c.TimelineStart.Seconds >= clip.TimelineStart.Seconds)
                    .OrderBy(c => c.TimelineStart.Seconds).FirstOrDefault();
                var sourceEnd = clip.WebRequestedOutSeconds is > 0
                    ? Math.Min(sourceDuration, clip.WebRequestedOutSeconds.Value) : sourceDuration;
                var sourceAvailable = Math.Max(0, sourceEnd - clip.SourceIn.Seconds);
                var available = next == null ? sourceAvailable : Math.Min(sourceAvailable, next.TimelineStart.Seconds - clip.TimelineStart.Seconds);
                var duration = Math.Max(1.0 / Math.Max(1, sequence.Fps.Seconds), available);
                clip.Duration = Rational.Time(duration);
                clip.WebAutoDurationSeconds = completed ? null : duration;
                if (completed) clip.WebRequestedOutSeconds = null;
            }
        }
    }
}
