using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Prometheus.Client.Services;

internal enum ProcessingFunction { Graphics, H264, Hevc, SamImage, SamTrack, Whisper }
internal sealed record ProcessingPreference(string Backend = "auto", string? AdapterKey = null);
internal sealed class ProcessingSettings
{
    public int Schema { get; set; } = 1;
    public int CpuThreads { get; set; }
    public Dictionary<string, ProcessingPreference> Functions { get; set; } = new();
}
internal sealed record ProcessingInstall(bool CudaSam, bool Dml, bool WebGpu, bool OnnxCpu, bool TorchCpu, bool Whisper);
internal sealed record ProcessingChoice(ProcessingFunction Function, string Backend, ProcessingAdapter? Adapter,
    int CpuThreads, bool Automatic, string Origin, string Reason)
{
    public string Signature => $"{Function}|{Backend}|{Adapter?.Key}|{Adapter?.Luid}|{Adapter?.Driver}|{CpuThreads}|{Automatic}|{Origin}";
    public string Label => Backend switch {
        "directml" when Function == ProcessingFunction.SamTrack => "DirectML + CPU",
        "webgpu" when Function == ProcessingFunction.SamTrack => "WebGPU + CPU",
        "cuda" => "NVIDIA CUDA", "directml" => "DirectML", "webgpu" => "WebGPU",
        "onnx-cpu" => "ONNX CPU", "torch-cpu" => "PyTorch CPU", "cpu" => "CPU",
        "nvidia" => "NVIDIA Media Foundation / NVENC", "amf" => "AMD AMF", "d3d11" => "Direct3D 11 / CEF", _ => Backend };
}
internal sealed record ProcessingExecution(string Backend, string? Device, string Status, string? Reason, DateTimeOffset Time);

internal static class ProcessingService
{
    private static readonly object Gate = new();
    private static Task<ProcessingHardwareSnapshot>? _inventory;
    private static ProcessingSettings? _settings;
    private static long? _compositionObserved;
    private static bool _resultsLoaded, _savingResults;
    private static readonly Dictionary<ProcessingFunction, ProcessingExecution> Executions = new();
    internal static string SettingsPath => Environment.GetEnvironmentVariable("PROMETHEUS_PROCESSING_SETTINGS_PATH") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prometheus", "processing-v1.json");
    internal static event Action? Changed;

    internal static Task<ProcessingHardwareSnapshot> InitializeAsync(bool refresh = false)
    {
        lock (Gate)
        {
            if (refresh || _inventory == null) _inventory = Task.Run(ProcessingHardware.Capture);
            return _inventory;
        }
    }
    internal static ProcessingHardwareSnapshot Hardware
    {
        get { var snapshot = InitializeAsync().GetAwaiter().GetResult(); lock (Gate) return _compositionObserved.HasValue ? snapshot with { CompositionLuid = _compositionObserved } : snapshot; }
    }
    internal static void ObserveComposition(long luid) { lock (Gate) _compositionObserved = luid; }
    internal static ProcessingExecution? LastExecution(ProcessingFunction function)
    { LoadResults(); lock (Gate) return Executions.GetValueOrDefault(function); }

    internal static ProcessingSettings GetSettings()
    {
        lock (Gate)
        {
            if (_settings == null)
            {
                try { _settings = JsonSerializer.Deserialize<ProcessingSettings>(File.ReadAllText(SettingsPath)); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
                _settings ??= new();
                if (_settings.Schema != 1) _settings = new();
                _settings.Functions ??= new();
                _settings.CpuThreads = Math.Clamp(_settings.CpuThreads, 0, 64);
            }
            return JsonSerializer.Deserialize<ProcessingSettings>(JsonSerializer.Serialize(_settings))!;
        }
    }

    internal static void Save(ProcessingSettings value)
    {
        if (value.CpuThreads is < 0 or > 64) throw new ArgumentException("El número de hilos debe estar entre 0 (automático) y 64.");
        foreach (var pair in value.Functions)
            if (!Enum.TryParse<ProcessingFunction>(pair.Key, out _) || !KnownBackends.Contains(pair.Value.Backend))
                throw new ArgumentException("Hay una opción de procesamiento desconocida.");
        var serialized = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, serialized); File.Move(temporary, SettingsPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        lock (Gate) _settings = JsonSerializer.Deserialize<ProcessingSettings>(serialized)!;
        Changed?.Invoke();
    }

    private static readonly HashSet<string> KnownBackends = new(StringComparer.Ordinal) {
        "auto", "cuda", "directml", "webgpu", "onnx-cpu", "torch-cpu", "cpu", "nvidia", "amf", "d3d11" };

    internal static ProcessingInstall Installed()
    {
        var root = RuntimeDependencies.SamRoot;
        var graphs = Path.Combine(root, "onnx");
        var python = File.Exists(RuntimeDependencies.SamPython);
        var temporal = python && File.Exists(Path.Combine(root, "sam2.1_hiera_small.pt")) && Directory.Exists(Path.Combine(root, "repo"));
        var image = python && File.Exists(Path.Combine(graphs, "vision_encoder.onnx")) && File.Exists(Path.Combine(graphs, "vision_encoder.onnx_data"));
        var prompt = File.Exists(Path.Combine(graphs, "prompt_encoder_mask_decoder.onnx")) && File.Exists(Path.Combine(graphs, "prompt_encoder_mask_decoder.onnx_data"));
        var dml = image && prompt && Directory.Exists(Path.Combine(graphs, "ort-directml", "onnxruntime"));
        var web = image && prompt && Directory.Exists(Path.Combine(graphs, "ort-webgpu", "onnxruntime"));
        var torch = Path.Combine(Path.GetDirectoryName(RuntimeDependencies.SamPython)!, "Lib", "site-packages", "torch", "lib", "torch_cuda.dll");
        return new(temporal && File.Exists(torch), dml, web, dml || web,
            temporal && File.Exists(Path.Combine(root, "track-cpu", "torch", "__init__.py")),
            File.Exists(RuntimeDependencies.WhisperPython) && File.Exists(Path.Combine(RuntimeDependencies.WhisperRoot, "model", "model.bin")));
    }

    internal static string? ExternalBackend(ProcessingFunction function) => Environment.GetEnvironmentVariable(function switch {
        ProcessingFunction.SamImage => "PROMETHEUS_SAM2_BACKEND",
        ProcessingFunction.SamTrack => "PROMETHEUS_SAM2_TRACK_BACKEND",
        ProcessingFunction.Whisper => "PROMETHEUS_WHISPER_BACKEND",
        ProcessingFunction.H264 or ProcessingFunction.Hevc => "PROMETHEUS_EXPORT_BACKEND", _ => "PROMETHEUS_GRAPHICS_BACKEND" });

    internal static ProcessingChoice Resolve(ProcessingFunction function, string? jobBackend = null) =>
        Evaluate(function, Hardware, Installed(), GetSettings(), ExternalBackend(function), jobBackend);

    // Pure selection logic, also used with simulated hardware in regression tests.
    internal static ProcessingChoice Evaluate(ProcessingFunction function, ProcessingHardwareSnapshot hardware,
        ProcessingInstall installed, ProcessingSettings settings, string? external = null, string? jobBackend = null)
    {
        var preference = settings.Functions.GetValueOrDefault(function.ToString()) ?? new();
        var requested = preference.Backend;
        var origin = "Ajuste local";
        if (!string.IsNullOrWhiteSpace(jobBackend) && jobBackend != "auto") { requested = jobBackend; origin = "Este trabajo"; }
        if (!string.IsNullOrWhiteSpace(external) && external != "auto") { requested = external.ToLowerInvariant(); origin = "Configuración externa"; }
        if (!KnownBackends.Contains(requested)) throw new InvalidOperationException("Proveedor desconocido: " + requested);
        var automatic = requested == "auto";
        var candidates = hardware.Adapters.Where(a => !a.Software && a.D3D11).ToArray();
        var cpuRequested = requested is "cpu" or "onnx-cpu" or "torch-cpu";
        var adapter = preference.AdapterKey == null || cpuRequested ? null : candidates.FirstOrDefault(a => a.Key == preference.AdapterKey);
        if (!cpuRequested && preference.AdapterKey != null && adapter == null)
            throw new InvalidOperationException("La GPU elegida ya no está disponible. Selecciona otra GPU o restaura Automático en Procesamiento.");
        var threads = settings.CpuThreads;
        if (threads == 0 && hardware.InstalledRamBytes > 0 && hardware.InstalledRamBytes <= 8UL * 1024 * 1024 * 1024 && candidates.Any(a => a.Amd && a.UnifiedMemory == true)) threads = 4;
        if (int.TryParse(Environment.GetEnvironmentVariable("PROMETHEUS_CPU_THREADS"), out var explicitThreads) && explicitThreads is >= 0 and <= 64) threads = explicitThreads;
        if (function == ProcessingFunction.Graphics)
        {
            adapter = hardware.Composition;
            if (adapter == null || !adapter.D3D11) throw new InvalidOperationException("No se pudo comprobar el dispositivo Direct3D 11 del monitor.");
            return new(function, "d3d11", adapter, threads, true, "Dispositivo del compositor", "Se conserva el adaptador efectivo compartido con CEF.");
        }
        if (function is ProcessingFunction.H264 or ProcessingFunction.Hevc)
        {
            adapter = hardware.Composition;
            if (automatic) requested = adapter?.Nvidia == true ? "nvidia" : adapter?.Amd == true ? "amf" : "cpu";
            if (requested == "nvidia" && adapter?.Nvidia != true || requested == "amf" && adapter?.Amd != true)
                throw new InvalidOperationException("Ese codificador no corresponde al adaptador del compositor.");
            if (requested is not ("nvidia" or "amf" or "cpu")) throw new InvalidOperationException("Codificador no válido: " + requested);
        }
        else
        {
            adapter ??= candidates.FirstOrDefault(a => a.Nvidia) ??
                (hardware.Composition is { Software: false, D3D11: true } compositor ? compositor : null) ?? candidates.FirstOrDefault();
            if (automatic)
            {
                requested = function switch {
                    ProcessingFunction.Whisper => installed.Whisper && adapter?.Nvidia == true ? "cuda" : "cpu",
                    ProcessingFunction.SamImage => installed.CudaSam && adapter?.Nvidia == true ? "cuda" :
                        installed.Dml && adapter != null ? "directml" : installed.WebGpu && adapter != null ? "webgpu" : "onnx-cpu",
                    _ => installed.CudaSam && adapter?.Nvidia == true ? "cuda" :
                        installed.Dml && installed.TorchCpu && adapter != null ? "directml" :
                        installed.WebGpu && installed.TorchCpu && adapter != null ? "webgpu" : "torch-cpu" };
            }
            var supported = function switch {
                ProcessingFunction.Whisper => installed.Whisper && (requested == "cpu" || requested == "cuda" && adapter?.Nvidia == true),
                ProcessingFunction.SamImage => requested switch {
                    "cuda" => installed.CudaSam && adapter?.Nvidia == true, "directml" => installed.Dml && adapter != null,
                    "webgpu" => installed.WebGpu && adapter != null, "onnx-cpu" => installed.OnnxCpu, _ => false },
                _ => requested switch {
                    "cuda" => installed.CudaSam && adapter?.Nvidia == true, "directml" => installed.Dml && installed.TorchCpu && adapter != null,
                    "webgpu" => installed.WebGpu && installed.TorchCpu && adapter != null, "torch-cpu" => installed.TorchCpu, _ => false } };
            if (!supported) throw new InvalidOperationException("No está disponible " + requested + " para esta función. Comprueba el runtime local y la GPU.");
        }
        var reason = automatic ? "Ruta compatible preferida para este equipo; se comprueba al utilizarla." : "Elección explícita: " + origin + ".";
        return new(function, requested, requested is "cpu" or "onnx-cpu" or "torch-cpu" ? null : adapter, threads,
            automatic, automatic ? "Automático" : origin, reason);
    }

    internal static ProcessingChoice[] Candidates(ProcessingFunction function)
    {
        var first = Resolve(function);
        if (!first.Automatic) return [first];
        var backends = function switch {
            ProcessingFunction.SamImage => new[] { "cuda", "directml", "webgpu", "onnx-cpu" },
            ProcessingFunction.SamTrack => new[] { "cuda", "directml", "webgpu", "torch-cpu" },
            ProcessingFunction.Whisper => new[] { "cuda", "cpu" }, _ => new[] { first.Backend } };
        var choices = new List<ProcessingChoice> { first };
        foreach (var backend in backends.Where(b => b != first.Backend))
            try { choices.Add(Evaluate(function, Hardware, Installed(), GetSettings(), backend) with { Automatic = true, Origin = "Respaldo automático" }); }
            catch (InvalidOperationException) { }
        return choices.ToArray();
    }

    internal static void Configure(ProcessStartInfo start, ProcessingChoice choice)
    {
        start.Environment[choice.Function switch {
            ProcessingFunction.SamImage => "PROMETHEUS_SAM2_BACKEND", ProcessingFunction.SamTrack => "PROMETHEUS_SAM2_TRACK_BACKEND", _ => "PROMETHEUS_WHISPER_BACKEND" }] = choice.Backend;
        start.Environment["PROMETHEUS_CPU_THREADS"] = choice.CpuThreads.ToString();
        start.Environment["PROMETHEUS_PROCESSING_DML_DEVICE"] = (choice.Adapter?.Index ?? 0).ToString();
        if (choice.Adapter != null)
        {
            start.Environment["PROMETHEUS_PROCESSING_ADAPTER_LUID"] = choice.Adapter.Luid.ToString();
            start.Environment["PROMETHEUS_PROCESSING_VENDOR"] = choice.Adapter.VendorId.ToString();
            start.Environment["PROMETHEUS_PROCESSING_DEVICE"] = choice.Adapter.DeviceId.ToString();
        }
        var torch = choice.Function == ProcessingFunction.SamTrack && choice.Backend != "cuda"
            ? Path.Combine(RuntimeDependencies.SamRoot, "track-cpu", "torch", "lib")
            : Path.Combine(Path.GetDirectoryName(start.FileName)!, "Lib", "site-packages", "torch", "lib");
        if (Directory.Exists(torch)) start.Environment["PATH"] = torch + Path.PathSeparator +
            (start.Environment.TryGetValue("PATH", out var existingPath) ? existingPath : "");
    }

    internal static void Record(ProcessingChoice choice, string? actual = null, string? reason = null, bool failed = false)
    {
        LoadResults();
        lock (Gate) Executions[choice.Function] = new(actual ?? choice.Label, choice.Adapter?.Name,
            failed ? "No disponible" : "Comprobado", reason, DateTimeOffset.UtcNow);
        lock (Gate)
            if (!_savingResults)
            {
                _savingResults = true;
                _ = Task.Run(() =>
                {
                    try
                    {
                        Dictionary<ProcessingFunction, ProcessingExecution> copy;
                        lock (Gate) copy = new(Executions);
                        var path = Path.ChangeExtension(SettingsPath, ".results.json");
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        var temporary = path + ".tmp";
                        try { File.WriteAllText(temporary, JsonSerializer.Serialize(new VerifiedProfile(ProfileSignature(), copy))); File.Move(temporary, path, true); }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                    finally { lock (Gate) _savingResults = false; }
                });
            }
        Changed?.Invoke();
    }

    private sealed record VerifiedProfile(string Signature, Dictionary<ProcessingFunction, ProcessingExecution> Results);
    private static string ProfileSignature()
    {
        var hardware = Hardware;
        var text = typeof(ProcessingService).Assembly.GetName().Version + "|" + hardware.Cpu + "|" + hardware.InstalledRamBytes + "|" +
            string.Join(';', hardware.Adapters.Select(a => a.Key + ":" + a.Driver));
        foreach (var path in new[] { Path.Combine(RuntimeDependencies.SamRoot, "sam2.1_hiera_small.pt"),
            Path.Combine(RuntimeDependencies.SamRoot, "onnx", "vision_encoder.onnx"),
            Path.Combine(RuntimeDependencies.WhisperRoot, "model", "model.bin"),
            Path.Combine(AppContext.BaseDirectory, "Runtime", "processing_runtime.py") })
        {
            var file = new FileInfo(path);
            text += "|" + path + ":" + (file.Exists ? file.Length + ":" + file.LastWriteTimeUtc.Ticks : "missing");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
    private static void LoadResults()
    {
        lock (Gate)
        {
            if (_resultsLoaded) return;
            _resultsLoaded = true;
            try
            {
                var profile = JsonSerializer.Deserialize<VerifiedProfile>(File.ReadAllText(Path.ChangeExtension(SettingsPath, ".results.json")));
                if (profile?.Results != null && profile.Signature == ProfileSignature())
                    foreach (var pair in profile.Results) Executions.TryAdd(pair.Key, pair.Value);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        }
    }

    internal static object Describe()
    {
        LoadResults();
        var hardware = Hardware;
        return new {
            version = typeof(ProcessingService).Assembly.GetName().Version?.ToString(3),
            hardware,
            installed_components = Installed(),
            external_overrides = new[] { "PROMETHEUS_SAM2_BACKEND", "PROMETHEUS_SAM2_TRACK_BACKEND", "PROMETHEUS_WHISPER_BACKEND",
                "PROMETHEUS_EXPORT_BACKEND", "PROMETHEUS_CPU_THREADS", "PROMETHEUS_SAM2_SCRIPT", "PROMETHEUS_SAM2_TRACK_SCRIPT",
                "PROMETHEUS_WHISPER_SCRIPT" }.Where(name => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))).ToArray(),
            functions = Enum.GetValues<ProcessingFunction>().Select(function => {
                ProcessingExecution? execution;
                lock (Gate) Executions.TryGetValue(function, out execution);
                try { var choice = Resolve(function); return (object)new { function = function.ToString(), selected = choice.Label,
                    device = choice.Adapter?.Name, choice.Origin, choice.Reason, choice.CpuThreads,
                    status = execution?.Status ?? "Pendiente de comprobar", last_execution = execution }; }
                catch (Exception error) { return new { function = function.ToString(), status = "No disponible", reason = error.Message, last_execution = execution }; }
            }).ToArray(),
            note = "Inventario ligero: consultar este informe no carga SAM ni Whisper. La memoria compartida no es memoria adicional." };
    }
}
