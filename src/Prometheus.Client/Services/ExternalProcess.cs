using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus.Client.Services;

internal static class ExternalProcess
{
    internal sealed record Result(int ExitCode, string Output, string Error);

    public static async Task<Result> RunAsync(ProcessStartInfo start, TimeSpan timeout,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start) ?? throw new IOException("No se pudo iniciar " + start.FileName);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(timeout);
        using var stop = deadline.Token.Register(() => Kill(process));
        var output = ReadOutputAsync(process);
        var error = ReadDiagnosticsAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            var text = await output.WaitAsync(deadline.Token).ConfigureAwait(false);
            var details = await error.WaitAsync(deadline.Token).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested) throw new TimeoutException("El proceso externo superó el tiempo de espera: " + start.FileName);
            return new(process.ExitCode, text, details);
        }
        catch
        {
            Kill(process);
            await process.WaitForExitAsync().ConfigureAwait(false);
            try { await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
            cancellation.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested) throw new TimeoutException("El proceso externo superó el tiempo de espera: " + start.FileName);
            throw;
        }
    }

    private static async Task<string> ReadOutputAsync(Process process)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            int count;
            while ((count = await process.StandardOutput.ReadAsync(buffer).ConfigureAwait(false)) != 0)
            {
                if (text.Length + count > 16 * 1024 * 1024)
                    throw new InvalidDataException("La respuesta del proceso externo es demasiado grande.");
                text.Append(buffer, 0, count);
            }
            return text.ToString();
        }
        catch { Kill(process); throw; }
    }

    public static async Task<string> ReadDiagnosticsAsync(StreamReader reader)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            text.Append(buffer, 0, count);
            if (text.Length > 16384) text.Remove(0, text.Length - 16384);
        }
        return text.ToString();
    }

    public static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
