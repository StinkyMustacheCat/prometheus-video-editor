using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

public static class WebMediaNaming
{
    public static string InitialName(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        return extension is ".mp4" or ".webm" or ".mov" or ".mp3" or ".m4a" or ".wav" or ".png" or ".jpg" or ".jpeg" or ".webp"
            ? DisplayName(FromDirectUrl(uri)) : Platform(uri) + " · enlace";
    }

    public static WebMediaMetadata FromYtDlp(JsonElement json, Uri uri)
    {
        var date = Read(json, "upload_date") ?? Read(json, "release_date");
        string? published = null;
        if (date != null && DateOnly.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed)) published = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        else if (json.TryGetProperty("timestamp", out var stamp) &&
                 stamp.ValueKind == JsonValueKind.Number && stamp.TryGetInt64(out var seconds))
        {
            try { published = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
            catch (ArgumentOutOfRangeException) { }
        }
        return new WebMediaMetadata
        {
            OriginalTitle = Read(json, "title"),
            CreatorName = Read(json, "creator"),
            AccountName = Read(json, "uploader") ?? Read(json, "channel"),
            AccountHandle = Read(json, "uploader_id"),
            AccountUrl = Read(json, "uploader_url") ?? Read(json, "channel_url"),
            PublishedAt = published,
            Platform = Platform(uri),
            SourceId = Read(json, "id")
        };
    }

    public static WebMediaMetadata FromDirectUrl(Uri uri, string? filename = null) => new()
    {
        OriginalTitle = CleanFilename(filename) ?? CleanFilename(Uri.UnescapeDataString(uri.AbsolutePath)),
        Platform = Platform(uri)
    };

    public static string DisplayName(WebMediaMetadata metadata)
    {
        var id = metadata.SourceId;
        var title = metadata.OriginalTitle?.Trim();
        if (!string.IsNullOrWhiteSpace(title) && !string.Equals(title, id, StringComparison.OrdinalIgnoreCase)) return title;
        var account = metadata.AccountHandle?.Trim();
        if (account != null && account.TrimStart('@').Length > 0 &&
            account.TrimStart('@').All(char.IsDigit)) account = null;
        if (!string.IsNullOrWhiteSpace(account)) account = account.StartsWith('@') ? account : "@" + account;
        else account = metadata.AccountName?.Trim();
        var source = !string.IsNullOrWhiteSpace(account) ? account : metadata.Platform ?? "Medio web";
        var suffix = metadata.PublishedAt ?? id;
        return string.IsNullOrWhiteSpace(suffix) ? source : source + " · " + suffix;
    }

    private static string Platform(Uri uri)
    {
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        if (host == "instagram.com") return "Instagram";
        if (host == "tiktok.com") return "TikTok";
        if (host is "twitter.com" or "x.com") return "X";
        if (host is "youtube.com" or "youtu.be") return "YouTube";
        if (host is "facebook.com" or "fb.watch") return "Facebook";
        return host;
    }

    private static string? Read(JsonElement json, string key) =>
        json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

    private static string? CleanFilename(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var name = Path.GetFileNameWithoutExtension(value.Trim('"', '\'', '/'));
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
