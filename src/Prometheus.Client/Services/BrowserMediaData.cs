using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Prometheus.Core.Storage;

namespace Prometheus.Client.Services;

/// <summary>Reads browser drag and clipboard formats without interpreting MGT files as media.</summary>
public static class BrowserMediaData
{
    private const int MaxBytes = 100_000_000;
    internal const string ImageUnavailable = "El navegador no entregó una imagen accesible. Prueba «Copiar imagen» o importa el archivo.";
    internal static IReadOnlyList<string> HtmlFormats { get; } = new[] { "text/html", "HTML Format" };
    internal static IReadOnlyList<string> ImageFormats { get; } = new[] { "image/png", "PNG", "image/jpeg", "JFIF", "Bitmap" };
    internal static IReadOnlyList<string> UrlFormats { get; } = new[] { "text/uri-list", "URL", "UniformResourceLocatorW", "UniformResourceLocator", "DownloadURL" };

    private static readonly Regex MediaTag = new("""<(?<kind>img|video|source)\b(?<attrs>(?:[^<>"']|"[^"]*"|'[^']*')*)>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Attribute = new("""(?<name>[^\s=/"'<>]+)(?:\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)'|(?<value>[^\s"'<>]+)))?""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex SourceUrl = new(@"(?im)^SourceURL:\s*(?<url>[^\r\n]+)", RegexOptions.Compiled);

    public static bool TryGetUrl(IDataObject data, out Uri uri)
    {
        var hasHtmlImage = false;
        foreach (var format in HtmlFormats)
        {
            var html = ReadText(SafeGet(data, format));
            if (TryGetHtmlMediaUrl(html, out uri)) return true;
            hasHtmlImage |= HasHtmlImage(html);
        }
        if (hasHtmlImage) { uri = null!; return false; }
        foreach (var format in UrlFormats.Concat(new[] { "UnicodeText", "Text" }))
            if (TryGetUrl(ReadText(SafeGet(data, format)), out uri)) return true;
        try { return TryGetUrl(data.GetText(), out uri); }
        catch { uri = null!; return false; }
    }

    internal static string? SaveDroppedImage(IDataObject data)
    {
        foreach (var format in ImageFormats)
            if (SaveImage(SafeGet(data, format)) is { } path) return path;
        foreach (var format in HtmlFormats)
            if (SaveImage(ReadInlineImage(ReadText(SafeGet(data, format)))) is { } path) return path;
        return null;
    }

    internal static bool HasHtmlImage(IDataObject data) =>
        HtmlFormats.Any(format => HasHtmlImage(ReadText(SafeGet(data, format))));

    internal static bool HasHtmlImage(string? html) => html != null &&
        html.AsSpan()[HtmlFragment(html)].Contains("<img", StringComparison.OrdinalIgnoreCase);

    internal static byte[]? ReadInlineImage(string? html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("data:image/", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var (image, raw) in MediaSources(html))
        {
            if (!image) continue;
            var source = WebUtility.HtmlDecode(raw);
            if (!source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var comma = source.IndexOf(',');
            if (comma < 0 || comma > 24 || source.Length - comma - 1 > MaxBytes * 4 / 3 + 4) continue;
            var extension = source[..comma].ToLowerInvariant() switch
            {
                "data:image/png;base64" => ".png",
                "data:image/jpeg;base64" or "data:image/jpg;base64" => ".jpg",
                "data:image/webp;base64" => ".webp",
                "data:image/gif;base64" => ".gif",
                "data:image/bmp;base64" => ".bmp",
                _ => null
            };
            if (extension == null) continue;
            try
            {
                var bytes = Convert.FromBase64String(source[(comma + 1)..]);
                if (ImageExtension(bytes) == extension) return bytes;
            }
            catch (FormatException) { }
        }
        return null;
    }

    public static bool TryGetUrl(string? value, out Uri uri)
    {
        if (TryGetHtmlMediaUrl(value, out uri)) return true;
        if (ValidUrl(value, null, out uri)) return true;
        if (!string.IsNullOrWhiteSpace(value))
        {
            foreach (var line in value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Trim().Trim('\0');
                if (candidate.StartsWith('#')) continue;
                if (ValidUrl(candidate, null, out uri)) return true;
                var separator = candidate.LastIndexOf(":http", StringComparison.OrdinalIgnoreCase);
                if (separator >= 0 && ValidUrl(candidate[(separator + 1)..], null, out uri)) return true;
            }
        }
        uri = null!;
        return false;
    }

    public static bool TryGetUrl(object? value, out Uri uri) => TryGetUrl(ReadText(value), out uri);

    internal static bool TryGetHtmlMediaUrl(string? html, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(html) || !html.Contains('<')) return false;
        var pageMatch = SourceUrl.Match(html);
        var page = pageMatch.Success ? pageMatch.Groups["url"].Value.Trim() : null;
        foreach (var (_, source) in MediaSources(html))
            if (ValidUrl(source, page, out uri)) return true;
        return false;
    }

    private static IEnumerable<(bool Image, string Source)> MediaSources(string html)
    {
        foreach (Match tag in MediaTag.Matches(html[HtmlFragment(html)]))
        {
            string? src = null, dataSrc = null;
            foreach (Match attribute in Attribute.Matches(tag.Groups["attrs"].Value))
            {
                var name = attribute.Groups["name"].Value;
                if (name.Equals("src", StringComparison.OrdinalIgnoreCase)) src ??= attribute.Groups["value"].Value;
                else if (name.Equals("data-src", StringComparison.OrdinalIgnoreCase)) dataSrc ??= attribute.Groups["value"].Value;
            }
            var image = tag.Groups["kind"].Value.Equals("img", StringComparison.OrdinalIgnoreCase);
            if (dataSrc != null) yield return (image, dataSrc.Trim());
            if (src != null) yield return (image, src.Trim());
        }
    }

    private static Range HtmlFragment(string html)
    {
        const string marker = "<!--StartFragment-->";
        var start = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return Range.All;
        start += marker.Length;
        var end = html.IndexOf("<!--EndFragment-->", start, StringComparison.OrdinalIgnoreCase);
        return end >= 0 ? start..end : Range.All;
    }

    private static bool ValidUrl(string? raw, string? page, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var value = WebUtility.HtmlDecode(raw.Trim().Trim('\0', '"', '\''));
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            if (!Uri.TryCreate(page, UriKind.Absolute, out var baseUri) || !Uri.TryCreate(baseUri, value, out parsed)) return false;
        }
        if (parsed.Scheme is not ("http" or "https") || IsMgt(parsed)) return false;
        uri = parsed;
        return true;
    }

    public static string? ReadText(object? value)
    {
        if (value is string text) return text.TrimEnd('\0');
        byte[]? bytes = value switch
        {
            byte[] raw => raw,
            Stream stream => ReadBytes(stream),
            _ => null
        };
        if (bytes == null || bytes.Length == 0) return null;
        var unicode = bytes.Length > 1 && bytes[1] == 0;
        return (unicode ? Encoding.Unicode : Encoding.UTF8).GetString(bytes).Trim('\0', '\uFEFF');
    }

    private static byte[]? ReadBytes(Stream stream)
    {
        try
        {
            if (stream.CanSeek && stream.Length > MaxBytes) return null;
            var oldPosition = stream.CanSeek ? stream.Position : 0;
            if (stream.CanSeek) stream.Position = 0;
            try
            {
                using var output = new MemoryStream();
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > MaxBytes) return null;
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
            finally { if (stream.CanSeek) stream.Position = oldPosition; }
        }
        catch { return null; }
    }

    private static object? SafeGet(IDataObject data, string format)
    {
        try { return data.Contains(format) ? data.Get(format) : null; }
        catch { return null; }
    }

    public static bool HasImage(IDataObject data) => ImageFormats.Any(data.Contains) || HasHtmlImage(data);

    public static string? SaveImage(object? value)
    {
        var bytes = value switch
        {
            byte[] raw => raw,
            Stream stream => ReadBytes(stream),
            _ => null
        };
        var extension = value is Bitmap ? ".png" : ImageExtension(bytes);
        if (extension == null) return null;
        var projectFile = ProjectController.Instance?.CurrentProjectPath;
        var directory = !string.IsNullOrWhiteSpace(projectFile)
            ? ProjectStorage.GetImportDirectory(projectFile)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prometheus", "ClipboardMedia");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "imagen-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            if (value is Bitmap bitmap) bitmap.Save(path);
            else File.WriteAllBytes(path, bytes!);
            return path;
        }
        catch { try { File.Delete(path); } catch { } return null; }
    }

    private static string? ImageExtension(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 12 || bytes.Length > MaxBytes) return null;
        if (bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71) return ".png";
        if (bytes[0] == 0xFF && bytes[1] == 0xD8) return ".jpg";
        if (Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP") return ".webp";
        if (Encoding.ASCII.GetString(bytes, 0, 3) == "GIF") return ".gif";
        if (bytes[0] == 'B' && bytes[1] == 'M') return ".bmp";
        return null;
    }

    private static bool IsMgt(Uri uri) => uri.AbsolutePath.EndsWith(".mgt", StringComparison.OrdinalIgnoreCase);
}
