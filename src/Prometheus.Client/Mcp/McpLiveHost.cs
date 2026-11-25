using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Prometheus.Client.Services;
using Prometheus.Client.Panels;
using System.Linq;

namespace Prometheus.Client.Mcp;

internal sealed class McpLiveHost : IDisposable
{
    public static string PipeName => Environment.GetEnvironmentVariable("PROMETHEUS_MCP_PIPE_NAME")
        ?? "PrometheusV32-Mcp-" + Environment.UserName;
    private readonly ProjectController _controller;
    private readonly McpChangeService _changes;
    private readonly McpExportService _export;
    private readonly McpMediaWorkService _mediaWork;
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<Guid> _sessions = new();
    private Task? _loop;
    public event Action<JsonElement, object>? ReviewRequested;
    public event Action<string>? LogMessage;

    public McpLiveHost(ProjectController controller)
    {
        _controller = controller;
        _changes = new McpChangeService(controller);
        _export = new McpExportService(controller);
        _mediaWork = new McpMediaWorkService(controller);
    }

    public void Start() => _loop ??= Task.Run(ListenAsync);

    public async Task StopAsync()
    {
        _mediaWork.CancelAll();
        _stop.Cancel();
        if (_loop != null) await _loop;
        _stop.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(_stop.Token);
                if (line == null) continue;
                object response;
                try
                {
                    using var request = JsonDocument.Parse(line);
                    var tool = McpJson.String(request.RootElement, "tool") ?? "";
                    var client = await Dispatcher.UIThread.InvokeAsync(() => RegisterClient(request.RootElement));
                    var args = request.RootElement.TryGetProperty("arguments", out var a) ? a : default;
                    var prepared = tool is "evaluate_changelist" or "apply_changelist"
                        ? await McpChangeService.PrepareAssetsAsync(args) : null;
                    if (tool == "client_connected") response = new { connected = true };
                    else if (tool == "capture_frame")
                    {
                        var capture = await Dispatcher.UIThread.InvokeAsync<Task<object>>(() =>
                            ProjectHistory.WithOrigin(client, () => CaptureAsync(args)));
                        response = await capture;
                    }
                    else if (tool == "project_file")
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                        deadline.CancelAfter(TimeSpan.FromSeconds(40));
                        var operation = await Dispatcher.UIThread.InvokeAsync<Task<object>>(() =>
                            ProjectHistory.WithOrigin(client, () => McpProjectFileService.ExecuteAsync(_controller, args, deadline.Token)));
                        response = await operation;
                    }
                    else if (tool == "export_sequence")
                    {
                        var operation = await Dispatcher.UIThread.InvokeAsync<Task<object>>(() =>
                            ProjectHistory.WithOrigin(client, () => _export.ExecuteAsync(args, _stop.Token)));
                        response = await operation;
                        await Dispatcher.UIThread.InvokeAsync(() => LogMessage?.Invoke(tool + " · correcto"));
                    }
                    else response = await Dispatcher.UIThread.InvokeAsync(() =>
                        ProjectHistory.WithOrigin(client, () => Handle(tool, args, prepared)));
                }
                catch (Exception ex) { response = new { error = ex.Message }; }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception ex) { Console.Error.WriteLine("MCP pipe: " + ex.Message); }
        }
    }

    private string RegisterClient(JsonElement packet)
    {
        var reported = McpJson.String(packet, "clientName");
        if (string.IsNullOrWhiteSpace(reported)) return "MCP";
        var name = new string(reported.Where(character => !char.IsControl(character)).Take(80).ToArray()).Trim();
        if (name.Length == 0) name = "Cliente MCP";
        if (Guid.TryParseExact(McpJson.String(packet, "session"), "N", out var session) && _sessions.Add(session))
        {
            ProjectHistory.WithOrigin("MCP · " + name, () =>
            {
                _controller.ReportNotice("mcp:" + session, "Conexión MCP", name + " se conectó.");
                return true;
            });
        }
        return "MCP · " + name;
    }

    private object Handle(string tool, JsonElement args,
        System.Collections.Generic.IReadOnlyDictionary<string, Prometheus.Core.Model.Asset>? prepared)
    {
        try
        {
            var result = tool switch
            {
                "get_state" => McpReadService.GetState(_controller, args),
                "inspect_clip" => McpReadService.InspectClip(_controller, args),
                "get_catalog" => Catalog(args),
                "evaluate_changelist" => _changes.Evaluate(args, prepared),
                "apply_changelist" => McpJson.String(args, "mode") == "review"
                    ? Review(args, prepared) : _changes.Apply(args, prepared),
                "editor_action" => McpEditorService.Action(_controller, args),
                "editor_history" => McpEditorService.History(_controller, args),
                "media_analysis" => _mediaWork.Execute(args),
                _ => throw new ArgumentException("Herramienta no disponible: " + tool)
            };
            LogMessage?.Invoke(tool + " · correcto");
            return result;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke(tool + " · " + ex.Message);
            throw;
        }
    }

    private object Review(JsonElement args,
        System.Collections.Generic.IReadOnlyDictionary<string, Prometheus.Core.Model.Asset>? prepared)
    {
        var evaluation = _changes.Evaluate(args, prepared);
        ReviewRequested?.Invoke(args.Clone(), evaluation);
        return new { mode = "review", evaluation, project_unchanged = true };
    }

    public async Task<object> EvaluateLocalAsync(JsonElement args)
    {
        var prepared = await McpChangeService.PrepareAssetsAsync(args);
        return await Dispatcher.UIThread.InvokeAsync(() => _changes.Evaluate(args, prepared));
    }
    public object ApplyLocal(JsonElement args) => _changes.Apply(args);

    private Task<object> CaptureAsync(JsonElement args)
    {
        var monitor = PanelRegistry.GetExistingPanelContent("monitor") as MonitorPanelView;
        if (monitor == null || !monitor.IsVisible)
            throw new InvalidOperationException("Abre el panel Previsualización para capturar un fotograma");
        var mode = McpJson.String(args, "mode") ?? "save_png";
        var at = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("at_seconds", out var time)
            ? time.GetDouble() : (double?)null;
        return monitor.CaptureForMcpAsync(mode, at, McpJson.String(args, "output_path"));
    }

    private object Catalog(JsonElement args)
    {
        var kind = McpJson.String(args, "kind") ?? "all";
        var filter = McpJson.String(args, "filter")?.Trim();
        bool Matches(string value) => string.IsNullOrWhiteSpace(filter) ||
            value.Contains(filter, StringComparison.OrdinalIgnoreCase);
        var effects = EffectCatalog.Available.Where(e => e.Kind != "transitions" &&
            (Matches(e.Id) || Matches(e.Name) || Matches(e.Family))).Select(e =>
        {
            var clip = new Prometheus.Core.Model.Clip();
            EffectApplicationService.ApplyToClip(clip, [e.Id]);
            return new
            {
                id = e.Id, name = e.Name, kind = e.Kind, family = e.Family, mcp_apply_supported = true,
                needs_resource_path = e.Id == Prometheus.Core.Model.GpuLutEffect.Id,
                parameters = clip.Effects.FirstOrDefault(instance => instance.EffectId == e.Id)?.Parameters ??
                    new Dictionary<string, Prometheus.Core.Model.EffectValue>()
            };
        }).ToArray();
        var transitions = EffectCatalog.Available.Where(e => e.Kind == "transitions")
            .Concat(MgtTransitionCatalog.All(_controller.CurrentProject).Select(MgtTransitionCatalog.Entry))
            .DistinctBy(e => e.Id).Where(e => Matches(e.Id) || Matches(e.Name))
            .Select(e => new { id = e.Id, name = e.Name,
                mcp_apply_supported = Prometheus.Core.Timeline.VideoTransitionOperations.Supports(e.Id) ||
                    Prometheus.Core.Timeline.AudioTransitionOperations.Supports(e.Id) ||
                    MgtTransitionCatalog.Find(_controller.CurrentProject, e.Id) != null,
                parameters = MgtTransitionCatalog.Find(_controller.CurrentProject, e.Id)?.Parameters }).ToArray();
        var constants = new { canvas_fit_modes = new[] { "fit", "width", "height", "fill", "stretch" },
            blend_modes = Prometheus.Core.Model.ClipBlendModes.All.Select(mode => new { mode.Id, mode.Name }).ToArray(),
            curve_interpolations = Prometheus.Core.Model.KeyframeAnimation.Interpolations,
            effect_scopes = new[] { "clip", "selection", "track" },
            clip_placements = new[] { "empty", "overwrite", "insert" },
            visual_keyframes = Prometheus.Core.Model.KeyframeAnimation.VisualTargets.Keys.ToArray() };
        var actions = McpChangeService.Actions.Where(a => Matches(a.Op)).Select(a =>
            new { op = a.Op, required = a.Required, optional = a.Optional }).ToArray();
        var editorActions = McpEditorService.ActionIds.Where(Matches).ToArray();
        return kind switch
        {
            "effects" => new { effects },
            "actions" => new { actions },
            "editor_actions" => new { editor_actions = editorActions },
            "transitions" => new { transitions, available = true, mcp_apply_supported = true },
            "constants" => constants,
            "dependencies" => RuntimeDependencies.Describe(),
            "all" => new { effects, transitions, constants, actions, editor_actions = editorActions,
                supported_tools = new[] { "get_state", "inspect_clip", "get_catalog",
                    "evaluate_changelist", "apply_changelist", "editor_action", "editor_history",
                    "capture_frame", "project_file", "export_sequence", "media_analysis" },
                dependencies = RuntimeDependencies.Describe(),
                analysis = new { whisper = WhisperTranscriptionService.IsAvailable, sam_seed = Sam2Bridge.IsAvailable,
                    sam_track = Sam2VideoBridge.IsAvailable, silence = true } },
            _ => throw new ArgumentException("Catálogo no disponible: " + kind)
        };
    }

    public void Dispose()
    {
        _mediaWork.CancelAll();
        _stop.Cancel();
        _stop.Dispose();
    }
}
