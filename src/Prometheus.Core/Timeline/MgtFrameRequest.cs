using Prometheus.Core.Primitives;

namespace Prometheus.Core.Timeline;

/// <summary>
/// An exact, stateless request to render one MGT frame from a resident browser scene.
/// Preview and export use the same frame number; neither requests a pre-rendered sequence.
/// </summary>
public readonly record struct MgtFrameRequest(string AssetId, string ClipId, int Frame, int Fps,
    int Width, int Height, long Revision)
{
    public Rational Time => Rational.FromFrames(Frame, Fps);

    public static MgtFrameRequest At(string assetId, string clipId, Rational sourceTime,
        int fps, int width, int height, long revision = 0)
    {
        if (string.IsNullOrWhiteSpace(assetId) || string.IsNullOrWhiteSpace(clipId))
            throw new ArgumentException("El fotograma MGT necesita medio y clip.");
        if (fps <= 0 || width <= 0 || height <= 0 || revision < 0)
            throw new ArgumentOutOfRangeException(nameof(fps), "Solicitud MGT fuera de rango.");
        if (sourceTime < Rational.Zero)
            throw new ArgumentOutOfRangeException(nameof(sourceTime), "El tiempo MGT no puede ser negativo.");
        var frame = checked((int)Math.Round(sourceTime.Seconds * fps, MidpointRounding.AwayFromZero));
        return new MgtFrameRequest(assetId, clipId, frame, fps, width, height, revision);
    }
}
