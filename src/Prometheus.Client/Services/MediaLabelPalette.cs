using System;
using System.Linq;
using Avalonia.Media;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Services;

public static class MediaLabelPalette
{
    public sealed record Option(string Id, string Name, string Hex);
    public static readonly Option[] Options =
    {
        new("coral", "Coral", "#E2A08F"), new("orange", "Naranja", "#E9B06C"),
        new("yellow", "Amarillo", "#E7D284"), new("green", "Verde", "#ABD497"),
        new("cyan", "Cian", "#93CFD2"), new("blue", "Azul", "#9ABBE0"),
        new("violet", "Violeta", "#B9A9D9"), new("pink", "Rosa", "#D6A3C3"),
        new("gray", "Gris", "#B7B8B9")
    };

    public static bool IsValid(string? id) => id == null || Options.Any(x => x.Id == id);

    public static Color ForAsset(Asset asset) => PendingWebColor(asset) ?? ForId(asset.LabelId) ??
        Default(asset, asset.Streams.All(s => s.Kind == "audio") ? "audio" : "video");

    public static Color ForClip(Clip clip, Asset? asset, string trackKind) =>
        (asset == null ? null : PendingWebColor(asset)) ?? ForId(clip.LabelId) ??
        (asset == null ? Color.Parse("#E9B06C") : ForId(asset.LabelId) ?? Default(asset, trackKind));

    private static Color? PendingWebColor(Asset asset)
    {
        if (asset.Source.Kind != "web" || !WebMediaService.IsPending(asset)) return null;
        return WebMediaService.GetState(asset.Id)?.Stage == "failed"
            ? Color.Parse("#C96E6E") : Color.Parse("#92969B");
    }

    private static Color? ForId(string? id)
    {
        var hex = Options.FirstOrDefault(x => x.Id == id)?.Hex;
        return hex == null ? null : Color.Parse(hex);
    }

    private static Color Default(Asset asset, string trackKind)
    {
        if (PlaceholderService.IsPlaceholder(asset)) return Color.Parse("#B7B8B9");
        if (asset.Source.Kind == "web") return Color.Parse("#E09E52");
        if (asset.Source.Kind == "mgt") return Color.Parse("#AE91D5");
        if (asset.TextOverlay?.Kind == "title") return Color.Parse("#E6A8C9");
        return trackKind == "audio" ? Color.Parse("#93CFD2") : Color.Parse("#E9B06C");
    }
}
