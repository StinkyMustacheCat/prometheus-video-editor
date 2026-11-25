using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Serialization;

namespace Prometheus.Core.Model;

/// <summary>A SAM mask tied to one source frame. Tracking can add more frames later.</summary>
public sealed class SamMaskKeyframe
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("source_seconds")]
    public double SourceSeconds { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("alpha_deflate")]
    public byte[] AlphaDeflate { get; set; } = [];

    [JsonPropertyName("object_alphas_deflate")]
    public List<byte[]> ObjectAlphasDeflate { get; set; } = new();

    public static SamMaskKeyframe Create(double sourceSeconds, int width, int height, byte[] alpha,
        IEnumerable<byte[]>? objectAlphas = null)
    {
        if (!double.IsFinite(sourceSeconds) || width < 1 || height < 1 ||
            (long)width * height > 8_000_000 || alpha.Length != width * height)
            throw new ArgumentException("La máscara SAM tiene dimensiones inválidas.");
        return new SamMaskKeyframe { SourceSeconds = sourceSeconds, Width = width,
            Height = height, AlphaDeflate = Compress(alpha),
            ObjectAlphasDeflate = objectAlphas?.Select(mask => mask.Length == alpha.Length
                ? Compress(mask) : throw new ArgumentException("Una selección SAM tiene dimensiones distintas."))
                .ToList() ?? new() };
    }

    private static byte[] Compress(byte[] alpha)
    {
        using var output = new MemoryStream();
        using (var compressor = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
            compressor.Write(alpha);
        return output.ToArray();
    }

    public byte[] DecodeAlpha()
    {
        if (Width < 1 || Height < 1 || (long)Width * Height > 8_000_000)
            throw new InvalidDataException("La máscara SAM tiene dimensiones inválidas.");
        return Decode(AlphaDeflate);
    }

    public IReadOnlyList<byte[]> DecodeObjectAlphas() => ObjectAlphasDeflate.Count == 0
        ? [DecodeAlpha()] : ObjectAlphasDeflate.Select(Decode).ToArray();

    private byte[] Decode(byte[] compressed)
    {
        var alpha = new byte[checked(Width * Height)];
        using var input = new MemoryStream(compressed);
        using var decompressor = new DeflateStream(input, CompressionMode.Decompress);
        decompressor.ReadExactly(alpha);
        if (decompressor.ReadByte() != -1)
            throw new InvalidDataException("La máscara SAM tiene datos adicionales.");
        return alpha;
    }

    public static SamMaskKeyframe? At(Clip clip, double sourceSeconds, double fps) =>
        clip.SamMasks?.Where(mask => Math.Abs(mask.SourceSeconds - sourceSeconds) <=
                0.5 / Math.Max(1, fps) + 0.0001)
            .MinBy(mask => Math.Abs(mask.SourceSeconds - sourceSeconds));
}
