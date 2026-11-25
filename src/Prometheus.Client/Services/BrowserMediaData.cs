using System;
using System.IO;
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
    private static readonly Regex MediaTag = new(@"<(?:img|video|source)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex SourceUrl = new(@"(?im)^SourceURL:\s*(?<url>[^\r\n]+)", RegexOptions.Compiled);

    public static bool TryGetUrl(IDataObject data, out Uri uri)
    {
        if (TryGetHtmlMediaUrl(data, out uri)) return true;
        foreach (var format in new[] { "text/uri-list", "URL", "UniformResourceLocatorW", "UniformResourceLocator", "DownloadURL", "UnicodeText", "Text" })
            if (TryGetUrl(ReadText(SafeGet(data, format)), out uri)) return true;
        try { return TryGetUrl(data.GetText(), out uri); }
        catch { uri = null!; return false; }
    }

    public static bool TryGetHtmlMediaUrl(IDataObject data, out Uri uri)
    {
        foreach (var format in new[] { "text/html", "HTML Format" })
            if (TryGetHtmlMediaUrl(ReadText(SafeGet(data, format)), out uri)) return true;
        uri = null!;
        return false;
    }

    public static object? GetImageData(IDataObject data) => SafeGet(data, "Bitmap") ?? SafeGet(data, "image/png")
        ?? SafeGet(data, "PNG") ?? SafeGet(data, "image/jpeg") ?? SafeGet(data, "JFIF");

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

    private static bool TryGetHtmlMediaUrl(string? html, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(html)) return false;
        var pageMatch = SourceUrl.Match(html);
        var page = pageMatch.Success ? pageMatch.Groups["url"].Value.Trim() : null;
        foreach (Match tag in MediaTag.Matches(html))
        {
            foreach (var attribute in new[] { "data-src", "src" })
            {
                var match = Regex.Match(tag.Value, @"(?<![\w-])" + attribute + @"\s*=\s*(?:[""'](?<url>[^""']+)[""']|(?<url>[^\s>]+))", RegexOptions.IgnoreCase);
                if (match.Success && ValidUrl(match.Groups["url"].Value, page, out uri)) return true;
            }
        }
        return false;
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
            var oldPosition = stream.CanSeek ? stream.Position : 0;
            if (stream.CanSeek) stream.Position = 0;
            using var output = new MemoryStream();
            stream.CopyTo(output);
            if (stream.CanSeek) stream.Position = oldPosition;
            return output.Length <= 100_000_000 ? output.ToArray() : null;
        }
        catch { return null; }
    }

    private static object? SafeGet(IDataObject data, string format)
    {
        try { return data.Contains(format) ? data.Get(format) : null; }
        catch { return null; }
    }

    public static bool HasImage(IDataObject data) => data.Contains("Bitmap") || data.Contains("image/png") || data.Contains("PNG") || data.Contains("image/jpeg") || data.Contains("JFIF");

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
        if (bytes == null || bytes.Length < 12 || bytes.Length > 100_000_000) return null;
        if (bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71) return ".png";
        if (bytes[0] == 0xFF && bytes[1] == 0xD8) return ".jpg";
        if (Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP") return ".webp";
        if (Encoding.ASCII.GetString(bytes, 0, 3) == "GIF") return ".gif";
        if (bytes[0] == 'B' && bytes[1] == 'M') return ".bmp";
        return null;
    }

    private static bool IsMgt(Uri uri) => uri.AbsolutePath.EndsWith(".mgt", StringComparison.OrdinalIgnoreCase);
}
