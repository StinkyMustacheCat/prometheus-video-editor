using System;
using System.Linq;
using System.Threading.Tasks;
using Prometheus.Client.Services;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Panels;

public partial class TimelinePanelView
{
    private async Task PasteWebAsync(Uri uri)
    {
        if (_controller?.CurrentProject.ActiveSequence is not { } sequence) return;
        var projectId = _controller.CurrentProject.Id;
        var sequenceId = sequence.Id;
        var pasteAt = TimelineCanvas.PlayheadSeconds;
        var known = _controller.CurrentProject.LinkShelf.FirstOrDefault(x =>
            string.Equals(x.Source, uri.ToString(), StringComparison.OrdinalIgnoreCase));
        var existing = _controller.CurrentProject.Assets.FirstOrDefault(a => a.Source.Kind == "web" && a.Source.Uri == uri.ToString());
        var knownDuration = known?.DurationSeconds ?? existing?.RecommendedDuration?.Seconds;
        var request = await WebLinkDialog.ForPasteAsync(this, uri.ToString(), knownDuration, sequence.Fps.Seconds,
            existing == null ? null : WebDownloadSettings.Current.QualityFor(existing));
        if (request == null || _controller.CurrentProject.Id != projectId || _controller.CurrentProject.ActiveSequenceId != sequenceId) return;
        var kind = known?.MediaKind == "audio" || known?.MediaKind == null &&
            new[] { ".mp3", ".m4a", ".wav" }.Contains(System.IO.Path.GetExtension(uri.AbsolutePath).ToLowerInvariant()) ? "audio" : "video";
        var duration = request.Duration ?? Math.Max(1 / sequence.Fps.Seconds, (knownDuration ?? 5) - request.SourceIn);
        string? trackId = null;
        var createdTrack = false;
        _controller.UndoRedo.Commit(p => {
            var before = p.ActiveSequence!.Tracks.Count;
            trackId = ImportPlacement.LowestFreeTrack(p.ActiveSequence, kind, pasteAt, duration).Id;
            createdTrack = p.ActiveSequence.Tracks.Count > before;
        }, "Preparar pista para enlace");
        if (createdTrack)
        {
            if (kind == "video") TimelineCanvas.VideoScrollOffset = TimelineCanvas.VideoScrollMaximum;
            else TimelineCanvas.AudioScrollOffset = TimelineCanvas.AudioScrollMaximum;
        }
        if (trackId != null) await WebMediaService.ImportAsync(_controller, request.Url, trackId, pasteAt,
            sourceIn: request.SourceIn, selectedDuration: request.Duration, quality: request.Quality);
    }
}
