using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Prometheus.Client.Services;

public sealed record ClipboardMedia(string[] Files, string? Text);

public static class AdaptiveClipboard
{
    public static async Task<ClipboardMedia> ReadAsync(Control owner)
    {
        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard == null) return new ClipboardMedia(Array.Empty<string>(), null);
        var data = await clipboard.GetDataAsync(DataFormats.Files);
        var files = (data as IEnumerable<IStorageItem>)?.Where(x => x.Path.IsFile).Select(x => x.Path.LocalPath).Where(File.Exists).ToArray()
            ?? (data as IEnumerable<string>)?.Where(File.Exists).ToArray()
            ?? Array.Empty<string>();
        if (files.Length > 0) return new ClipboardMedia(files, null);
        foreach (var format in new[] { "text/html", "HTML Format" })
            if (BrowserMediaData.TryGetUrl(await clipboard.GetDataAsync(format), out var mediaUrl))
                return new ClipboardMedia(Array.Empty<string>(), mediaUrl.ToString());
        foreach (var format in new[] { "Bitmap", "image/png", "PNG", "image/jpeg", "JFIF" })
        {
            var path = BrowserMediaData.SaveImage(await clipboard.GetDataAsync(format));
            if (path != null) return new ClipboardMedia(new[] { path }, null);
        }
        foreach (var format in new[] { "text/uri-list", "URL", "UniformResourceLocatorW", "UniformResourceLocator", "DownloadURL" })
            if (BrowserMediaData.TryGetUrl(await clipboard.GetDataAsync(format), out var url))
                return new ClipboardMedia(Array.Empty<string>(), url.ToString());
        var text = (await clipboard.GetTextAsync())?.Trim();
        return new ClipboardMedia(Array.Empty<string>(), BrowserMediaData.TryGetUrl(text, out var parsed) ? parsed.ToString() : text);
    }
}
