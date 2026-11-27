using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Prometheus.Client.Services;

/// <summary>Owns the filter file until the decoder has finished or failed to start.</summary>
internal sealed class AudioMixCommand : IDisposable
{
    private string? _filterPath;
    public ProcessStartInfo StartInfo { get; }

    internal AudioMixCommand(ProcessStartInfo start, string? temporaryDirectory = null)
    {
        StartInfo = start;
        // Leave small commands unchanged. Count quoting conservatively, including
        // backslashes that Windows may need to escape at the end of an argument.
        if (CommandLengthUpperBound(start) < 24_000) return;
        var index = start.ArgumentList.IndexOf("-filter_complex");
        if (index < 0 || index + 1 >= start.ArgumentList.Count)
            throw new InvalidOperationException("Faltan las instrucciones de mezcla de audio.");
        var graph = start.ArgumentList[index + 1];
        var folder = temporaryDirectory ?? Path.Combine(
            Path.GetPathRoot(AppContext.BaseDirectory) ?? Path.GetTempPath(), "Prometheus", "AudioMixScripts");
        Directory.CreateDirectory(folder);
        _filterPath = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".ffgraph");
        try
        {
            File.WriteAllText(_filterPath, graph, new UTF8Encoding(false));
            start.ArgumentList[index] = "-/filter_complex";
            start.ArgumentList[index + 1] = _filterPath;
            if (OperatingSystem.IsWindows() && CommandLengthUpperBound(start) >= 32_767)
                throw new InvalidOperationException("La mezcla contiene demasiadas rutas de audio para iniciarse en Windows. " +
                    "Exporta un intervalo más corto o usa rutas de medios más cortas.");
        }
        catch { Dispose(); throw; }
    }

    internal static long CommandLengthUpperBound(ProcessStartInfo start)
    {
        long length = start.FileName.Length + 3;
        foreach (var argument in start.ArgumentList)
        {
            length += argument.Length + 3;
            foreach (var character in argument)
                if (character is '\\' or '"') length++;
        }
        return length;
    }

    public void Dispose()
    {
        var path = Interlocked.Exchange(ref _filterPath, null);
        if (path == null) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
