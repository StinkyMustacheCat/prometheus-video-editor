using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Services;

internal sealed record SilenceSettings(double ThresholdDb, double MinimumSeconds,
    double KeepBefore, double KeepAfter);

internal static class SilenceAnalysisService
{
    private static readonly Regex Boundary = new(@"silence_(start|end):\s*(-?\d+(?:\.\d+)?(?:e[+-]?\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<IReadOnlyList<SilenceCut>> AnalyzeAsync(string path, int stream, Clip clip, double fps,
        SilenceSettings settings, CancellationToken cancellation)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("El archivo de audio no está disponible.");
        var speed = clip.Speed.Seconds;
        var duration = clip.Duration.Seconds * speed;
        var start = clip.TimelineStart.Seconds;
        var info = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
        };
        var filter = $"atrim=duration={Number(duration)},asetpts=PTS-STARTPTS," +
            $"silencedetect=noise={Number(settings.ThresholdDb)}dB:d={Number(settings.MinimumSeconds * speed)}";
        foreach (var arg in new[] { "-hide_banner", "-nostdin", "-nostats", "-ss", Number(clip.SourceIn.Seconds),
            "-t", Number(duration), "-i", path, "-map", $"0:{stream}", "-vn", "-sn", "-dn",
            "-af", filter, "-f", "null", "-" })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("No se pudo iniciar el análisis.");
        using var registration = cancellation.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        var read = process.StandardError.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation);
        var output = await read;
        if (process.ExitCode != 0)
            throw new InvalidOperationException("No se pudo decodificar la pista de audio seleccionada.");

        var cuts = new List<SilenceCut>();
        double? silenceStart = null;
        foreach (Match match in Boundary.Matches(output))
        {
            var position = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (match.Groups[1].Value == "start") silenceStart = position;
            else if (silenceStart is { } from)
            {
                AddCut(from, position);
                silenceStart = null;
            }
        }
        if (silenceStart is { } tail) AddCut(tail, duration);
        return cuts;

        void AddCut(double rawStart, double rawEnd)
        {
            var from = Math.Clamp(rawStart / speed, 0, clip.Duration.Seconds);
            var to = Math.Clamp(rawEnd / speed, 0, clip.Duration.Seconds);
            if (to - from + 1e-6 < settings.MinimumSeconds) return;
            var cutIn = Math.Ceiling((start + from + settings.KeepAfter) * fps - 1e-7) / fps;
            var cutOut = Math.Floor((start + to - settings.KeepBefore) * fps + 1e-7) / fps;
            if ((cutOut - cutIn) * fps >= 1 - 1e-7)
                cuts.Add(new SilenceCut(cutIn, cutOut));
        }
    }

    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
