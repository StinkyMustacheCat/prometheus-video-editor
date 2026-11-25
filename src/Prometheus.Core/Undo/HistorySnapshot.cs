using System.IO;
using System.IO.Compression;
using System.Text;

namespace Prometheus.Core.Undo;

/// <summary>Lossless state for Undo. It never stores rendered frames.</summary>
internal sealed record HistorySnapshot(byte[] Data, bool Compressed)
{
    public static HistorySnapshot Pack(string json) => Pack(Encoding.UTF8.GetBytes(json));

    public static HistorySnapshot Pack(byte[] bytes)
    {
        if (bytes.Length < 16384) return new(bytes, false);
        using var output = new MemoryStream();
        using (var compressor = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
            compressor.Write(bytes);
        return output.Length < bytes.Length ? new(output.ToArray(), true) : new(bytes, false);
    }

    public byte[] Unpack()
    {
        if (!Compressed) return Data;
        using var input = new MemoryStream(Data, writable: false);
        using var decompressor = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }
}
