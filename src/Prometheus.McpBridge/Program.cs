using System.IO.Pipes;
using System.Text;
using System.Text.Json;

internal static class Program
{
// stdout belongs exclusively to the MCP JSON-RPC transport.
public static async Task Main()
{
    var session = Guid.NewGuid().ToString("N");
    string? clientName = null;
    while (await Console.In.ReadLineAsync() is { } line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var method = root.TryGetProperty("method", out var m) ? m.GetString() : "";
            if (method == "initialize") clientName = ReadClientName(root);
            if (method == "notifications/initialized" && clientName != null)
                _ = NotifyConnectedAsync(session, clientName);
            if (!root.TryGetProperty("id", out var id)) continue;
            object response = method switch
            {
                "initialize" => Result(id, new
                {
                    protocolVersion = root.TryGetProperty("params", out var init) &&
                        init.TryGetProperty("protocolVersion", out var requested) &&
                        requested.GetString() is "2024-11-05" or "2025-03-26" or "2025-06-18" or "2025-11-25"
                            ? requested.GetString()! : "2025-11-25",
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "prometheus-v3-2",
                        version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "3.22.2" }
                }),
                "ping" => Result(id, new { }),
                "tools/list" => Result(id, new { tools = Tools.Select(t => new
                    { name = t.name, description = t.description, inputSchema = t.inputSchema }).ToArray() }),
                "tools/call" => await CallAsync(id, root, session, clientName),
                _ => Error(id, -32601, "Método no disponible: " + method)
            };
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("MCP: " + ex.Message);
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(Error(null, -32700, "JSON-RPC inválido")));
        }
    }
}

static string ReadClientName(JsonElement request)
{
    if (!request.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object ||
        !parameters.TryGetProperty("clientInfo", out var info) || info.ValueKind != JsonValueKind.Object)
        return "Cliente MCP";
    foreach (var key in new[] { "title", "name" })
        if (info.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString()))
            return value.GetString()!.Trim();
    return "Cliente MCP";
}

static string PipeName() => Environment.GetEnvironmentVariable("PROMETHEUS_MCP_PIPE_NAME")
    ?? "PrometheusV32-Mcp-" + Environment.UserName;

static NamedPipeClientStream ConnectPipe() => new(".", PipeName(),
    PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

static async Task NotifyConnectedAsync(string session, string clientName)
{
    try
    {
        using var pipe = ConnectPipe();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await pipe.ConnectAsync(timeout.Token);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { tool = "client_connected", session, clientName }));
        await reader.ReadLineAsync(timeout.Token);
    }
    catch (Exception error) when (error is IOException or OperationCanceledException)
    {
        // Tool calls also announce the session if the editor was unavailable at initialization.
    }
}

static async Task<object> CallAsync(JsonElement id, JsonElement root, string session, string? clientName)
{
    if (!root.TryGetProperty("params", out var parameters) ||
        !parameters.TryGetProperty("name", out var nameElement))
        return Error(id, -32602, "Falta el nombre de la herramienta");
    var name = nameElement.GetString() ?? "";
    if (!Tools.Any(t => t.name == name)) return Error(id, -32602, "Herramienta desconocida: " + name);
    try
    {
        using var pipe = ConnectPipe();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await pipe.ConnectAsync(2000, timeout.Token);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var arguments = parameters.TryGetProperty("arguments", out var a) ? a : JsonSerializer.SerializeToElement(new { });
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { tool = name, arguments, session, clientName }));
        var reply = await reader.ReadLineAsync(timeout.Token)
            ?? throw new IOException("El editor cerró la conexión MCP");
        using var parsed = JsonDocument.Parse(reply);
        var value = parsed.RootElement.Clone();
        var failed = value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("error", out var error) && error.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) &&
            !value.TryGetProperty("state", out _);
        return Result(id, new { content = new[] { new { type = "text", text = value.GetRawText() } },
            isError = failed });
    }
    catch (Exception ex)
    {
        return Result(id, new { content = new[] { new { type = "text",
            text = "No se pudo comunicar con Prometheus. Canal: " + PipeName() +
                ". Puente: " + Environment.ProcessPath + ". " +
                (ex is OperationCanceledException or TimeoutException
                    ? "Se agotó el tiempo de conexión o respuesta. Abre el editor y vuelve a configurar MCP desde su carpeta actual."
                    : ex.Message) } }, isError = true });
    }
}

static object Result(JsonElement? id, object result) => new { jsonrpc = "2.0", id, result };
static object Error(JsonElement? id, int code, string message) => new
    { jsonrpc = "2.0", id, error = new { code, message } };

static readonly (string name, string description, object inputSchema)[] Tools =
[
    ("get_state", "Consulta el estado compacto del proyecto abierto en Prometheus Avalonia.",
        Schema(new { scope = new { type = "string", @enum = new[] { "summary", "project", "media_pool", "sequence", "selection", "io", "tracks", "links", "transcriptions", "processing" } },
            track_ids = new { type = "array", items = new { type = "string" } },
            folder_id = new { type = new[] { "string", "null" }, description = "En media_pool: omitir para todos, null para Sin carpeta, o ID de carpeta." },
            document_id = new { type = "string" }, offset = new { type = "integer" }, limit = new { type = "integer" } })),
    ("inspect_clip", "Inspecciona un clip y su contenido anidado, si existe.",
        Schema(new { clip_id = new { type = "string" }, compact = new { type = "boolean" } })),
    ("get_catalog", "Consulta las acciones disponibles en esta versión del editor.",
        Schema(new { kind = new { type = "string" }, filter = new { type = "string" } })),
    ("evaluate_changelist", "Valida un lote sin modificar el proyecto y devuelve un plan.",
        Schema(new { actions = new { type = "array", items = new { type = "object" } }, expected_revision = new { type = "integer" } }, ["actions"])),
    ("apply_changelist", "Confirma un plan evaluado en un único paso de Undo.",
        Schema(new { plan_id = new { type = "string" }, actions = new { type = "array", items = new { type = "object" } },
            expected_revision = new { type = "integer" }, mode = new { type = "string" } })),
    ("editor_action", "Controla transporte, lector y selección sin crear un paso de Undo.",
        Schema(new { id = new { type = "string" }, seconds = new { type = "number" },
            frames = new { type = "integer" }, clip_id = new { type = "string" } }, ["id"])),
    ("editor_history", "Deshace o rehace el último cambio del proyecto.",
        Schema(new { operation = new { type = "string", @enum = new[] { "undo", "redo" } } }, ["operation"])),
    ("capture_frame", "Captura el fotograma compuesto del monitor como PNG o medio editable.",
        Schema(new { mode = new { type = "string", @enum = new[] { "save_png", "add_to_media", "insert", "overlay" } },
            at_seconds = new { type = "number" }, output_path = new { type = "string" } })),
    ("project_file", "Crea, abre, guarda o recupera un proyecto por ruta. Consulta las copias disponibles con recoveries.",
        Schema(new { operation = new { type = "string", @enum = new[] { "new", "open", "close", "save", "recoveries", "recover" } },
            file_path = new { type = "string" }, name = new { type = "string" },
            discard_unsaved = new { type = "boolean" } }, ["operation"])),
    ("media_analysis", "Inicia, consulta o cancela trabajos de Whisper, SAM y detección de silencios.",
        Schema(new { operation = new { type = "string", @enum = new[] { "start", "status", "cancel" } },
            kind = new { type = "string", @enum = new[] { "whisper", "sam_seed", "sam_track", "silence" } },
            job_id = new { type = "string" }, clip_id = new { type = "string" }, sequence_id = new { type = "string" },
            expected_revision = new { type = "integer" }, at_seconds = new { type = "number" }, mask_id = new { type = "string" },
            repair = new { type = "boolean" }, name = new { type = "string" }, caption_mode = new { type = "string", @enum = new[] { "phrase", "word" } },
            apply_captions = new { type = "boolean" }, apply = new { type = "boolean" }, ripple = new { type = "boolean" },
            preset = new { type = "string", @enum = new[] { "soft", "balanced", "aggressive" } },
            points = new { type = "array", items = Schema(new { x = new { type = "number", minimum = 0, maximum = 1 },
                y = new { type = "number", minimum = 0, maximum = 1 }, positive = new { type = "boolean" } }, ["x", "y"]) }
        }, ["operation"])),
    ("export_sequence", "Prepara, inicia, consulta o cancela una exportación MP4, MOV o WAV del montaje activo.",
        Schema(new { operation = new { type = "string", @enum = new[] { "preflight", "start", "resume", "status", "cancel" } },
            output_path = new { type = "string" }, job_id = new { type = "string" },
            expected_revision = new { type = "integer" }, overwrite = new { type = "boolean" },
            format = new { type = "string", @enum = new[] { "mp4", "hevc", "prores422", "prores4444", "wav" } },
            encoder = new { type = "string", @enum = new[] { "auto", "gpu", "cpu" } },
            range = new { type = "string", @enum = new[] { "all", "marked" } },
            allow_offline = new { type = "boolean" }, recoverable = new { type = "boolean" }, video_bitrate_mbps = new { type = "integer" },
            audio_bitrate_kbps = new { type = "integer" },
            width = new { type = "integer", minimum = 2, maximum = 8192, description = "Ancho de salida. Indicar también height; conserva la proporción del montaje." },
            height = new { type = "integer", minimum = 2, maximum = 8192, description = "Alto de salida nativa; omitir ambos para usar el lienzo del montaje." } }))
];

static object Schema(object properties, string[]? required = null) =>
    new { type = "object", properties, required = required ?? [], additionalProperties = false };
}
