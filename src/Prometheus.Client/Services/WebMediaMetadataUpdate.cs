using System;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Services;

public static class WebMediaMetadataUpdate
{
    public static void Apply(Project project, string assetId, Uri uri, WebMediaMetadata metadata, double? duration)
    {
        var target = project.Assets.First(a => a.Id == assetId);
        var displayName = WebMediaNaming.DisplayName(metadata);
        var previousAutomaticName = target.WebAutoName;
        if (previousAutomaticName == null &&
            (target.Name == uri.Host || target.Name == WebMediaNaming.InitialName(uri) ||
             target.Name.StartsWith(assetId + "-", StringComparison.OrdinalIgnoreCase)))
            previousAutomaticName = target.Name;
        target.WebMetadata = metadata;
        if (previousAutomaticName != null && target.Name == previousAutomaticName)
            target.Name = displayName;
        target.WebAutoName = displayName;
        var reference = project.LinkShelf.FirstOrDefault(x =>
            string.Equals(x.Source, uri.ToString(), StringComparison.OrdinalIgnoreCase));
        if (reference != null)
        {
            reference.WebMetadata = metadata;
            reference.Name = displayName;
            reference.MetadataStatus = "ready";
            reference.MetadataError = null;
            if (duration is > 0) reference.DurationSeconds = duration;
        }
        foreach (var sequence in project.Sequences)
        foreach (var clip in sequence.Clips.Where(c => c.AssetId == assetId &&
                     (c.Name == "Preparando medio…" || previousAutomaticName != null && c.Name == previousAutomaticName)))
            clip.Name = displayName;
        if (duration is > 0)
        {
            target.RecommendedDuration = Rational.Time(duration.Value);
            WebClipDuration.Apply(project, assetId, duration.Value, false);
        }
    }
}
