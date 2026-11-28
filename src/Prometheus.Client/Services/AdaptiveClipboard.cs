using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Prometheus.Client.Services;

public sealed record ClipboardMedia(string[] Files, string? Text, string? Error = null);

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
        foreach (var format in BrowserMediaData.ImageFormats)
            if (BrowserMediaData.SaveImage(await clipboard.GetDataAsync(format)) is { } path)
                return new ClipboardMedia(new[] { path }, null);

        Uri? linkedMedia = null;
        var hasHtmlImage = false;
        foreach (var format in BrowserMediaData.HtmlFormats)
        {
            var html = BrowserMediaData.ReadText(await clipboard.GetDataAsync(format));
            if (BrowserMediaData.SaveImage(BrowserMediaData.ReadInlineImage(html)) is { } path)
                return new ClipboardMedia(new[] { path }, null);
            if (linkedMedia == null && BrowserMediaData.TryGetHtmlMediaUrl(html, out var uri)) linkedMedia = uri;
            hasHtmlImage |= BrowserMediaData.HasHtmlImage(html);
        }
        if (linkedMedia != null) return new ClipboardMedia(Array.Empty<string>(), linkedMedia.ToString());
        if (hasHtmlImage) return new ClipboardMedia(Array.Empty<string>(), null, BrowserMediaData.ImageUnavailable);

        foreach (var format in BrowserMediaData.UrlFormats)
            if (BrowserMediaData.TryGetUrl(await clipboard.GetDataAsync(format), out var url))
                return new ClipboardMedia(Array.Empty<string>(), url.ToString());
        var text = (await clipboard.GetTextAsync())?.Trim();
        return new ClipboardMedia(Array.Empty<string>(),
            text?.StartsWith('{') != true && BrowserMediaData.TryGetUrl(text, out var parsed) ? parsed.ToString() : text);
    }
}
