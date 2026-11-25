using System.Linq;
using System.IO;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public static class StillImageRules
{
    public static bool IsStill(Project? project, Clip clip)
    {
        var asset = project?.Assets.FirstOrDefault(item => item.Id == clip.AssetId);
        if (asset == null) return false;
        if (asset.Streams.Any(stream => stream.Kind == "image" &&
            (stream.Id == clip.StreamId || string.IsNullOrEmpty(clip.StreamId)))) return true;
        var extension = Path.GetExtension(asset.EffectiveLocator?.Path)?.ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";
    }
}
