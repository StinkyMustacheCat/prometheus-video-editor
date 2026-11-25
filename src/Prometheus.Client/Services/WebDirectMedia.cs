using System;
using System.IO;

namespace Prometheus.Client.Services;

public static class WebDirectMedia
{
    public static string? ExtensionForContentType(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/pjpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "image/bmp" or "image/x-ms-bmp" => ".bmp",
        "image/tiff" => ".tiff",
        "image/avif" => ".avif",
        "video/mp4" => ".mp4",
        "video/webm" => ".webm",
        "video/quicktime" => ".mov",
        "audio/mpeg" => ".mp3",
        "audio/mp4" or "audio/x-m4a" => ".m4a",
        "audio/wav" or "audio/x-wav" => ".wav",
        _ => null
    };

    public static string? ImageExtension(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (header.Length >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff) return ".jpg";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        if (header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8))) return ".gif";
        if (header.Length >= 2 && header[..2].SequenceEqual("BM"u8)) return ".bmp";
        if (header.Length >= 4 && (header[..4].SequenceEqual(new byte[] { 73, 73, 42, 0 }) ||
            header[..4].SequenceEqual(new byte[] { 77, 77, 0, 42 }))) return ".tiff";
        if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8) &&
            (header[8..12].SequenceEqual("avif"u8) || header[8..12].SequenceEqual("avis"u8))) return ".avif";
        return null;
    }

    public static string? ImageExtension(string file)
    {
        using var input = File.OpenRead(file);
        Span<byte> header = stackalloc byte[32];
        return ImageExtension(header[..input.Read(header)]);
    }
}
