using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Prometheus.Client.Services;

internal static class RuntimeDependencies
{
    public static string RuntimeRoot
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("PROMETHEUS_RUNTIME_ROOT");
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
            return Path.Combine(AppContext.BaseDirectory, "Runtime");
        }
    }
    public static string SamRoot => Environment.GetEnvironmentVariable("PROMETHEUS_SAM2_ROOT") ?? Path.Combine(RuntimeRoot, "sam2");
    public static string WhisperRoot => Environment.GetEnvironmentVariable("PROMETHEUS_WHISPER_ROOT") ?? Path.Combine(RuntimeRoot, "whisper");
    public static string WhisperPython => Environment.GetEnvironmentVariable("PROMETHEUS_WHISPER_PYTHON") ?? Path.Combine(WhisperRoot, "python", "python.exe");
    public static string SamPython => Environment.GetEnvironmentVariable("PROMETHEUS_SAM2_PYTHON") ?? WhisperPython;

    public static void PreparePython(ProcessStartInfo info)
    {
        var libraries = Path.Combine(Path.GetDirectoryName(info.FileName)!, "Lib", "site-packages", "torch", "lib");
        if (Directory.Exists(libraries))
            info.Environment["PATH"] = libraries + Path.PathSeparator +
                (info.Environment.TryGetValue("PATH", out var existing) ? existing : "");
    }

    public static object Describe()
    {
        var samScript = Environment.GetEnvironmentVariable("PROMETHEUS_SAM2_SCRIPT") ??
            Path.Combine(AppContext.BaseDirectory, "Runtime", "sam2_bridge.py");
        var trackScript = Environment.GetEnvironmentVariable("PROMETHEUS_SAM2_TRACK_SCRIPT") ??
            Path.Combine(AppContext.BaseDirectory, "Runtime", "sam2_track.py");
        var whisperScript = Environment.GetEnvironmentVariable("PROMETHEUS_WHISPER_SCRIPT") ??
            Path.Combine(AppContext.BaseDirectory, "Runtime", "whisper_bridge.py");
        object Engine(string python, string script, string model, string? repo = null) => new
        {
            python,
            script,
            model,
            repository = repo,
            missing = new[] { python, script, model }.Where(path => !File.Exists(path))
                .Concat(repo != null && !Directory.Exists(repo) ? new[] { repo } : []).ToArray()
        };
        return new
        {
            runtime_root = RuntimeRoot,
            sam = Engine(SamPython, samScript, Path.Combine(SamRoot, "sam2.1_hiera_small.pt"), Path.Combine(SamRoot, "repo")),
            sam_tracking = Engine(SamPython, trackScript, Path.Combine(SamRoot, "sam2.1_hiera_small.pt"), Path.Combine(SamRoot, "repo")),
            whisper = Engine(WhisperPython, whisperScript, Path.Combine(WhisperRoot, "model", "model.bin")),
            ffmpeg = LocalMediaService.Tool("ffmpeg"),
            ffprobe = LocalMediaService.Tool("ffprobe"),
            note = "La presencia de archivos no comprueba el controlador GPU ni la carga del modelo."
        };
    }
}
