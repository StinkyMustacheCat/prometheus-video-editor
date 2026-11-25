using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Prometheus.Client.Services;
using Prometheus.Core.Model;

namespace Prometheus.Client.Panels;

public partial class MediaPoolPanelView
{
    private readonly Dictionary<string, Bitmap> _thumbnails = new();
    private readonly ConcurrentDictionary<string, byte> _thumbnailJobs = new();
    private readonly ConcurrentDictionary<string, byte> _thumbnailFailed = new();

    private Control BuildMediaListRow(Asset asset, string title, string? state, double width)
    {
        var expanded = width >= 650;
        var type = TypeOf(asset);
        var origin = asset.Source.Kind == "web"
            ? asset.WebMetadata?.AccountHandle ?? asset.WebMetadata?.AccountName ?? asset.WebMetadata?.CreatorName ?? "Web"
            : asset.Source.Kind == "mgt" ? "Composición MGT"
            : asset.Source.Kind == "local" ? "Archivo local" : "Generado";
        if (_selectedFolder == null && asset.FolderId is { } folderId &&
            _controller?.CurrentProject.MediaFolders.FirstOrDefault(f => f.Id == folderId) is { } folder)
            origin += " · " + folder.Name;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(expanded ? "*,125,75,72" : "*,80"),
            Margin = new Avalonia.Thickness(6, 4) };
        var nameArea = new Grid { ColumnDefinitions = new ColumnDefinitions("3,52,*") };
        nameArea.Children.Add(new Border { Width = 3, Background = new SolidColorBrush(MediaLabelPalette.ForAsset(asset)) });
        var image = Thumbnail(asset);
        Control thumbnail = image != null
            ? new Image { Source = image, Width = 49, Height = 43, Stretch = Stretch.UniformToFill }
            : new Border { Width = 49, Height = 43, Background = new SolidColorBrush(Color.Parse("#1B1B1B")),
                Child = new TextBlock { Text = type == "Audio" ? "A" : type == "Imagen" ? "I" : type == "MGT" ? "M" : "V",
                    Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center } };
        Grid.SetColumn(thumbnail, 1); nameArea.Children.Add(thumbnail);
        var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(9, 0, 6, 0) };
        labels.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis });
        labels.Children.Add(new TextBlock { Text = state ?? (expanded ? origin : origin + " · " + type),
            FontSize = 10, Foreground = state == null ? Brushes.Gray : Brushes.LightGray,
            TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(labels, 2); nameArea.Children.Add(labels);
        row.Children.Add(nameArea);
        if (expanded)
        {
            var source = new TextBlock { Text = origin, FontSize = 10, Foreground = Brushes.LightGray,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(source, 1); row.Children.Add(source);
            var kind = new TextBlock { Text = type, FontSize = 10, Foreground = Brushes.LightGray,
                VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(kind, 3); row.Children.Add(kind);
        }
        var duration = asset.RecommendedDuration?.Seconds;
        var time = new TextBlock { Text = duration is > 0 ? TimeSpan.FromSeconds(duration.Value).ToString(@"hh\:mm\:ss") : "—",
            FontSize = 10, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(time, expanded ? 2 : 1); row.Children.Add(time);
        return row;
    }

    private Control BuildMediaCard(Asset asset, string text)
    {
        var details = _viewMode == 0;
        var imageWidth = details ? Math.Min(80, _viewSizes[0] * 1.4) : Math.Max(70, _viewSizes[1] - 14);
        var imageHeight = details ? Math.Max(32, _viewSizes[0] - 12) : Math.Max(65, _viewSizes[1] * 0.52);
        var panel = new StackPanel { Orientation = details ? Orientation.Horizontal : Orientation.Vertical, Spacing = 5 };
        var image = Thumbnail(asset);
        if (image != null) panel.Children.Add(new Image { Source = image, Width = imageWidth, Height = imageHeight, Stretch = Stretch.UniformToFill });
        else panel.Children.Add(new Border { Width = imageWidth, Height = imageHeight, Background = new SolidColorBrush(Color.Parse("#171717")), Child = new TextBlock { Text = asset.TextOverlay != null ? "T" : asset.Source.Kind == "web" ? "WEB" : asset.Source.Kind == "generated" ? "RESERVA" : "▶", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        var labels = new StackPanel { Spacing = 2 };
        labels.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = details ? 280 : Math.Max(70, _viewSizes[1] - 14), FontSize = 10 });
        if (_selectedFolder == null && asset.FolderId is { } folderId &&
            _controller?.CurrentProject.MediaFolders.FirstOrDefault(f => f.Id == folderId) is { } folder)
        {
            var badge = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            badge.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#FF8A00")), Width = 5, Height = 8 });
            badge.Children.Add(new TextBlock { Text = folder.Name, Foreground = Brushes.LightGray, FontSize = 9, TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = details ? 260 : Math.Max(55, _viewSizes[1] - 25) });
            labels.Children.Add(badge);
        }
        panel.Children.Add(labels);
        return panel;
    }

    private Bitmap? Thumbnail(Asset asset)
    {
        if (asset.Source.Kind == "mgt") return null;
        var path = asset.EffectiveLocator?.Path;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        if (asset.Streams.Count > 0 && asset.Streams.TrueForAll(s => s.Kind == "audio")) return null;
        var key = asset.Id + ":" + path + ":" + File.GetLastWriteTimeUtc(path).Ticks;
        if (_thumbnails.TryGetValue(key, out var cached)) return cached;
        if (!_thumbnailFailed.ContainsKey(key) && _thumbnailJobs.TryAdd(key, 0)) _ = GenerateThumbnailAsync(path, key);
        return null;
    }

    private void PruneThumbnailCache(int start, int end)
    {
        if (_thumbnails.Count <= 120) return;
        var visibleIds = _visibleAssets.Skip(start).Take(Math.Max(0, end - start)).Select(a => a.Id).ToHashSet();
        foreach (var key in _thumbnails.Keys.ToArray())
        {
            if (_thumbnails.Count <= 96) break;
            var id = key.Split(':', 2)[0];
            if (visibleIds.Contains(id)) continue;
            _thumbnails[key].Dispose();
            _thumbnails.Remove(key);
        }
    }

    private async Task GenerateThumbnailAsync(string source, string key)
    {
        try
        {
            var extension = Path.GetExtension(source).ToLowerInvariant();
            var imageFile = extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tif" or ".tiff" or ".avif";
            var target = source;
            if (!imageFile)
            {
                var folder = Path.Combine(Path.GetTempPath(), "Prometheus", "Thumbnails");
                Directory.CreateDirectory(folder);
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
                target = Path.Combine(folder, hash + ".jpg");
                if (!File.Exists(target))
                {
                    using var process = new Process { StartInfo = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true } };
                    foreach (var arg in new[] { "-v", "error", "-y", "-ss", "0.2", "-i", source, "-frames:v", "1", "-vf", "scale=160:-1", target }) process.StartInfo.ArgumentList.Add(arg);
                    process.Start();
                    var error = process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    await error;
                    if (process.ExitCode != 0 || !File.Exists(target)) throw new IOException("No se pudo generar la miniatura");
                }
            }
            var bitmap = await Task.Run(() => { using var stream = File.OpenRead(target); return Bitmap.DecodeToWidth(stream, 160); });
            await Dispatcher.UIThread.InvokeAsync(() => {
                if (_controller?.CurrentProject.Assets.Any(a => key.StartsWith(a.Id + ":", StringComparison.Ordinal)) == true)
                {
                    _thumbnails[key] = bitmap;
                    RenderVisibleAssets(true);
                }
                else bitmap.Dispose();
            });
        }
        catch { _thumbnailFailed.TryAdd(key, 0); }
        finally { _thumbnailJobs.TryRemove(key, out _); }
    }
}
