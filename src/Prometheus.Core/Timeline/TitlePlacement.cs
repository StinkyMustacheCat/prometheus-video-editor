using System;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Timeline;

public readonly record struct TitleBox(double CenterX, double CenterY, double Width, double? Height = null);

public static class TitlePlacement
{
    public static Clip Add(Project project, double atSeconds, TitleBox box)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var fps = sequence.Fps.Seconds > 0 ? sequence.Fps : new Rational(60, 1);
        // Use the frame containing the playhead. Rounding up can start the title after the displayed frame.
        var frame = (long)Math.Floor(Math.Max(0, atSeconds) * fps.Seconds + 1e-8);
        var start = new Rational(frame * fps.D, fps.N);
        const double duration = 5;
        var asset = GeneratedText.CreateAsset("Nuevo texto", "title", sequence.Width, sequence.Height);
        var text = asset.TextOverlay!;
        text.PositionX = box.CenterX;
        text.PositionY = box.CenterY;
        text.BoxWidth = Math.Clamp(box.Width, 0.02, 1);
        if (box.Height is { } height)
        {
            var scale = Math.Min(sequence.Width, sequence.Height) / 1080d;
            var sizeFromHeight = height * sequence.Height * 0.75 / scale;
            // Allow about six em for the initial "Nuevo texto" label at the default font.
            var sizeFromWidth = text.WrapWidth * sequence.Width / (6 * scale);
            text.FontSize = Math.Clamp(Math.Round(Math.Min(sizeFromHeight, sizeFromWidth)), 8, 400);
        }
        text.Validate();
        var occupied = sequence.Clips.Where(clip => clip.TimelineStart.Seconds < start.Seconds + duration &&
            clip.EndSeconds > start.Seconds).Select(clip => clip.TrackId).ToHashSet();
        var videoTracks = sequence.Tracks.Where(track => track.Kind == "video" &&
            track.Role != "subtitle" && track.Enabled).ToArray();
        var topOccupied = videoTracks.Where(track => occupied.Contains(track.Id))
            .Select(track => track.Order).DefaultIfEmpty(int.MinValue).Max();
        var track = videoTracks.Where(track => !track.Locked && track.Order > topOccupied)
            .MaxBy(track => track.Order) ?? CollisionEngine.AddTrack(sequence, "video");
        var clip = new Clip { Name = asset.Name, TrackId = track.Id, AssetId = asset.Id,
            StreamId = asset.Streams[0].Id, TimelineStart = start, Duration = Rational.Time(duration) };
        project.Assets.Add(asset);
        sequence.Clips.Add(clip);
        return clip;
    }
}
