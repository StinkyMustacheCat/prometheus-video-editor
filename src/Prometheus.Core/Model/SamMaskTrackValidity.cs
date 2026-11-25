using System;
using System.IO;

namespace Prometheus.Core.Model;

public static class SamMaskTrackValidity
{
    public static string? Problem(Clip clip, SamMaskTrack track, string? sourcePath, bool checkSourceIdentity = true)
    {
        if (track.FrameCount < 1 || track.FramesPerSecond <= 0 ||
            !double.IsFinite(track.FramesPerSecond))
            return "El seguimiento SAM está dañado.";
        var tolerance = 0.5 / track.FramesPerSecond + 0.0001;
        if (clip.SourceIn.Seconds < track.StartSourceSeconds - tolerance ||
            clip.SourceIn.Seconds + clip.Duration.Seconds * clip.Speed.Seconds >
            track.StartSourceSeconds + track.FrameCount / track.FramesPerSecond + tolerance)
            return "El seguimiento SAM no cubre todo el recorte. Vuelve a seguir el clip.";
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return "Falta el vídeo usado por el seguimiento SAM.";
        if (checkSourceIdentity && track.SourceFileLength > 0 && track.SourceFileWriteUtcTicks > 0)
        {
            var file = new FileInfo(sourcePath);
            if (file.Length != track.SourceFileLength ||
                file.LastWriteTimeUtc.Ticks != track.SourceFileWriteUtcTicks)
                return "El vídeo cambió desde que se calculó la máscara SAM. Vuelve a seguir el clip.";
        }
        return null;
    }
}
