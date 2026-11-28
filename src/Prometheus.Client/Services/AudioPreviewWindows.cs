using System;
using System.Collections.Generic;
using System.Linq;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

internal static class AudioPreviewWindows
{
    internal const int SampleRate = 48_000;
    private const int FirstLength = 4 * SampleRate;
    private const int NextLength = 12 * SampleRate;
    private const int ResampleMargin = SampleRate / 4;

    internal readonly record struct Window(double RenderStart, double RenderEnd, int DiscardSamples, int Samples);

    internal static bool CanSplit(Sequence sequence, double previewSpeed)
    {
        if (previewSpeed != 1) return false;
        var solo = sequence.Tracks.Any(track => track.Kind == "audio" && track.Enabled && track.Solo);
        var tracks = sequence.Tracks.Where(track => track.Kind == "audio" && track.Enabled && (!solo || track.Solo)).ToArray();
        if (tracks.Any(track => AudioEffectFilters.Build(track.Effects).Length != 0)) return false;
        var ids = tracks.Select(track => track.Id).ToHashSet(StringComparer.Ordinal);
        // IIR filters, look-ahead dynamics and time-stretch require uninterrupted state.
        return sequence.Clips.Where(clip => clip.Enabled && ids.Contains(clip.TrackId)).All(clip =>
            clip.NestedContent == null && clip.Speed.Seconds == 1 && AudioEffectFilters.Build(clip.Effects).Length == 0);
    }

    internal static IEnumerable<Window> At(double start, double end)
    {
        var total = (long)Math.Round(Math.Max(0, end - start) * SampleRate);
        for (long offset = 0; offset < total;)
        {
            var length = (int)Math.Min(total - offset, offset == 0 ? FirstLength : NextLength);
            var preroll = offset == 0 ? 0 : ResampleMargin;
            // Allow the resampler to see samples on both sides of the boundary.
            var renderThrough = Math.Min(total, offset + length + ResampleMargin);
            yield return new(start + (offset - preroll) / (double)SampleRate,
                start + renderThrough / (double)SampleRate, preroll, length);
            offset += length;
        }
    }
}
