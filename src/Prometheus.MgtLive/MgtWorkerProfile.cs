namespace Prometheus.MgtLive;

/// <summary>Owns only a marked, exclusively leased worker directory.</summary>
internal sealed class MgtWorkerProfile : IDisposable
{
    private const string Marker = "prometheus-mgt-profile-v1";
    private readonly FileStream _lease;
    public string Path { get; }

    public MgtWorkerProfile(string root)
    {
        Directory.CreateDirectory(root);
        foreach (var candidate in Directory.EnumerateDirectories(root))
            TryRemove(candidate);
        Path = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        _lease = new FileStream(System.IO.Path.Combine(Path, Marker),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
    }

    private static void TryRemove(string candidate)
    {
        if (!Guid.TryParseExact(System.IO.Path.GetFileName(candidate), "N", out _)) return;
        var marker = System.IO.Path.Combine(candidate, Marker);
        if (!File.Exists(marker)) return;
        try
        {
            // A running worker retains this lease and prevents both cleanup and rename.
            using (new FileStream(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Directory.Delete(candidate, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _lease.Dispose();
        TryRemove(Path);
    }
}
