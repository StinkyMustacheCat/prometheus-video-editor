using System.IO.Compression;
using System.Text.Json;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Model;

/// <summary>
/// Describes a live MGT source. The browser owns the scene; no rendered frames are stored here.
/// Full MGT validation remains the responsibility of the versioned MGT runtime.
/// </summary>
public static class MgtLiveAsset
{
    public static bool IsDocumentPath(string? path) => !string.IsNullOrWhiteSpace(path) &&
        (path.EndsWith(".mgt.json", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".mgtproj", StringComparison.OrdinalIgnoreCase));

    public static Asset Inspect(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("No se encontró el MGT.", path);
        if (file.Length == 0) throw new InvalidDataException("El MGT está vacío.");
        if (file.Length > 36_000_000) throw new InvalidDataException("El MGT supera 36 MB.");

        var project = path.EndsWith(".mgtproj", StringComparison.OrdinalIgnoreCase);
        if (!project && !path.EndsWith(".mgt.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selecciona un .mgt.json o .mgtproj.");

        using var document = project ? ReadProject(file.FullName) : JsonDocument.Parse(File.ReadAllText(file.FullName));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Documento MGT no válido.");

        var scene = project ? ReadProjectScene(root) : root;
        if (project) ValidateProject(root, scene, file.FullName);
        else ValidateComposition(scene);

        var duration = project ? ReadDuration(scene) : ReadRecommendedDuration(scene);
        var aspect = project ? ReadAspect(scene) : ReadRecommendedAspect(scene);
        var (width, height) = aspect switch
        {
            "vertical" => (1080, 1920),
            "square" => (1080, 1080),
            "classic" => (1440, 1080),
            _ => (1920, 1080)
        };
        var name = !project && TryString(scene, "name", out var declaredName) ? declaredName : Path.GetFileNameWithoutExtension(file.Name);
        var stream = new StreamInfo { Id = Guid.NewGuid().ToString("N"), Kind = "video", Codec = "mgt-live",
            Width = width, Height = height, PixelFormat = "bgra8-alpha", NominalFps = new Rational(60, 1),
            Duration = Rational.Time(duration) };
        var streams = new List<StreamInfo> { stream };
        if (project && ProjectAudioEntry(root) != null)
            streams.Add(new StreamInfo { Id = Guid.NewGuid().ToString("N"), Kind = "audio",
                Codec = "pcm_s16le", Ordinal = 0, SampleRate = 48000, Channels = 2,
                Duration = stream.Duration });
        var asset = new Asset
        {
            Id = Guid.NewGuid().ToString("N"), Name = name!,
            Source = new MediaSource { Kind = "mgt", Uri = file.FullName, ImportedAt = DateTime.UtcNow.ToString("O") },
            ContainerFormat = project ? "mgtproj" : "mgt.json", RecommendedDuration = stream.Duration,
            Streams = streams, MgtParameters = project ? ReadProjectParameters(root) : ReadParameters(scene)
        };
        asset.Representations.Add(new MediaRepresentation { Role = "original", Locator = new Locator(file.FullName),
            SizeBytes = file.Length, ModifiedUtc = file.LastWriteTimeUtc.ToString("O") });
        return asset;
    }

    public static void ValidateComposition(JsonElement scene)
    {
            if (!TryString(scene, "name", out _) || !scene.TryGetProperty("parameters", out var parameters) ||
                parameters.ValueKind != JsonValueKind.Array || !scene.TryGetProperty("render", out var render) ||
                render.ValueKind != JsonValueKind.String || !scene.TryGetProperty("html", out var html) ||
                html.ValueKind != JsonValueKind.String || !scene.TryGetProperty("css", out var css) ||
                css.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("La composición MGT no declara nombre, parámetros, HTML, CSS y render.");
            if (TryString(scene, "kind", out var kind) && kind != "composition")
                throw new NotSupportedException("Una transición MGT requiere otro contrato.");
            if (!scene.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var number) || number is < 1 or > 4)
                throw new InvalidDataException("Versión de composición MGT no compatible.");
        if (scene.TryGetProperty("scene3d", out var scene3d) && scene3d.ValueKind != JsonValueKind.Null)
            throw new NotSupportedException("Esta escena MGT 3D aún no es compatible con reproducción en vivo.");
        if (new[] { "videos", "gifs", "animatedSVGs", "models", "lotties" }
            .Any(key => scene.TryGetProperty(key, out var resources) && resources.ValueKind == JsonValueKind.Object &&
                resources.EnumerateObject().Any()))
            throw new NotSupportedException("Este MGT usa vídeo, GIF, SVG animado, GLB o Lottie; aún no es compatible con reproducción en vivo.");
        _ = ReadParameters(scene);
    }

    private static void ValidateProject(JsonElement root, JsonElement scene, string path)
    {
        _ = ReadDuration(scene);
        _ = ReadAspect(scene);
        if (!TryString(scene, "text", out _) || !TryString(scene, "font", out _) ||
            !TryString(scene, "color", out _) || !TryString(scene, "background", out _) ||
            !scene.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number ||
            !scene.TryGetProperty("start", out var start) || start.ValueKind != JsonValueKind.Object ||
            !scene.TryGetProperty("end", out var end) || end.ValueKind != JsonValueKind.Object ||
            Number(start, "x") == null || Number(start, "y") == null ||
            Number(end, "x") == null || Number(end, "y") == null)
            throw new InvalidDataException("La escena del proyecto MGT está incompleta.");
        var snapshot = root.GetProperty("snapshot");
        if (snapshot.TryGetProperty("effects", out var effects) &&
            effects.ValueKind == JsonValueKind.Array && effects.GetArrayLength() > 0)
            throw new NotSupportedException("Este proyecto MGT usa efectos de audio aún no disponibles en vivo.");
        if (!snapshot.TryGetProperty("tracks", out var tracks) ||
            !tracks.TryGetProperty("overlayLanes", out var lanes) || lanes.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("El proyecto MGT no contiene pistas visuales compatibles.");
        foreach (var lane in lanes.EnumerateArray())
        {
            if (!lane.TryGetProperty("clips", out var clips) || clips.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Pista visual MGT no válida.");
            foreach (var clip in clips.EnumerateArray())
            {
                if (!TryString(clip, "id", out _) || !clip.TryGetProperty("document", out var composition))
                    throw new InvalidDataException("Clip visual MGT no válido.");
                ValidateComposition(composition);
            }
        }
        if (root.TryGetProperty("resources", out var resources) && resources.ValueKind == JsonValueKind.Array)
        {
            foreach (var resource in resources.EnumerateArray())
                if (!TryString(resource, "kind", out var kind) || kind != "audio")
                    throw new NotSupportedException("Este proyecto MGT contiene vídeo incrustado aún no disponible en vivo.");
        }
        var audioEntry = ProjectAudioEntry(root);
        if (audioEntry == null) return;
        using var zip = ZipFile.OpenRead(path);
        if (zip.GetEntry(audioEntry) is not { Length: > 0 and <= 200_000_000 })
            throw new InvalidDataException("Falta el audio incrustado del proyecto MGT.");
    }

    public static string? ProjectAudioEntry(JsonElement root)
    {
        if (!root.TryGetProperty("snapshot", out var snapshot) ||
            !snapshot.TryGetProperty("audio", out var audio) ||
            !audio.TryGetProperty("audio", out var selected) ||
            !TryString(selected, "id", out var id)) return null;
        if (!root.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Faltan los recursos MGT.");
        foreach (var item in resources.EnumerateArray())
            if (TryString(item, "id", out var candidate) && candidate == id)
            {
                if (!TryString(item, "entry", out var entry) ||
                    !System.Text.RegularExpressions.Regex.IsMatch(entry!, @"^audio/[0-9]{1,3}\.wav$"))
                    throw new InvalidDataException("Entrada de audio MGT no válida.");
                return entry;
            }
        throw new InvalidDataException("Falta el audio declarado del proyecto MGT.");
    }

    private static List<MgtParameter> ReadProjectParameters(JsonElement root)
    {
        var result = new List<MgtParameter>();
        var lanes = root.GetProperty("snapshot").GetProperty("tracks").GetProperty("overlayLanes");
        foreach (var lane in lanes.EnumerateArray())
            foreach (var clip in lane.GetProperty("clips").EnumerateArray())
            {
                var clipId = clip.GetProperty("id").GetString()!;
                var name = clip.GetProperty("document").GetProperty("name").GetString()!;
                foreach (var parameter in ReadParameters(clip.GetProperty("document")))
                {
                    parameter.Id = "clip/" + clipId + "/" + parameter.Id;
                    parameter.Label = name + " · " + parameter.Label;
                    if (clip.TryGetProperty("params", out var values) && values.ValueKind == JsonValueKind.Object &&
                        values.TryGetProperty(parameter.Id[("clip/" + clipId + "/").Length..], out var saved))
                        parameter.Default = saved.Clone();
                    result.Add(parameter);
                }
            }
        return result;
    }

    public static List<MgtParameter> ReadParameters(JsonElement scene)
    {
        var result = new List<MgtParameter>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in scene.GetProperty("parameters").EnumerateArray())
        {
            if (!TryString(item, "id", out var id) || !TryString(item, "label", out var label) ||
                !TryString(item, "type", out var type) || !item.TryGetProperty("default", out var initial) ||
                !ids.Add(id!)) throw new InvalidDataException("Parámetro MGT no válido.");
            var options = item.TryGetProperty("options", out var choices) && choices.ValueKind == JsonValueKind.Array
                ? choices.EnumerateArray().Select(choice => choice.GetString() ?? "").ToList() : [];
            if (type == "image" && scene.TryGetProperty("resources", out var embedded) &&
                embedded.ValueKind == JsonValueKind.Object)
                options = ["", .. embedded.EnumerateObject().Select(resource => resource.Name)];
            if (type is not ("text" or "number" or "color" or "boolean" or "select" or "font" or "image"))
                throw new NotSupportedException("El parámetro MGT «" + label + "» aún no es compatible con reproducción en vivo.");
            result.Add(new MgtParameter { Id = id!, Label = label!, Type = type!, Default = initial.Clone(),
                Min = Number(item, "min"), Max = Number(item, "max"), Step = Number(item, "step"), Options = options });
        }
        if (result.Count > 48) throw new InvalidDataException("El MGT declara más de 48 parámetros.");
        return result;
    }

    private static double? Number(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble() : null;

    private static JsonDocument ReadProject(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("project.json") ?? throw new InvalidDataException("Falta project.json en el MGT.");
        if (entry.Length is <= 0 or > 8_000_000) throw new InvalidDataException("project.json supera el límite permitido.");
        using var reader = entry.Open();
        return JsonDocument.Parse(reader);
    }

    private static JsonElement ReadProjectScene(JsonElement root)
    {
        if (!TryString(root, "format", out var format) || format != "mgt-project" ||
            !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) ||
            number is < 1 or > 2 || !root.TryGetProperty("snapshot", out var snapshot) ||
            snapshot.ValueKind != JsonValueKind.Object || !snapshot.TryGetProperty("state", out var scene) ||
            scene.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Proyecto MGT no compatible.");
        return scene;
    }

    private static double ReadRecommendedDuration(JsonElement root)
    {
        if (!root.TryGetProperty("recommended", out var settings) || settings.ValueKind != JsonValueKind.Object ||
            !settings.TryGetProperty("duration", out var value)) return 3;
        return ValidDuration(value);
    }

    private static double ReadDuration(JsonElement root) =>
        root.TryGetProperty("duration", out var value) ? ValidDuration(value) : throw new InvalidDataException("Falta la duración del MGT.");

    private static double ValidDuration(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var duration) ||
            !double.IsFinite(duration) || duration is < 0.1 or > 3600)
            throw new InvalidDataException("Duración MGT fuera de rango.");
        return duration;
    }

    private static string ReadRecommendedAspect(JsonElement root) =>
        root.TryGetProperty("recommended", out var settings) && settings.ValueKind == JsonValueKind.Object
            ? ReadAspect(settings) : "horizontal";

    private static string ReadAspect(JsonElement root)
    {
        if (!TryString(root, "aspect", out var aspect)) return "horizontal";
        if (aspect is not ("horizontal" or "vertical" or "square" or "classic"))
            throw new InvalidDataException("Proporción MGT no compatible.");
        return aspect;
    }

    private static bool TryString(JsonElement element, string property, out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var field) ||
            field.ValueKind != JsonValueKind.String) return false;
        value = field.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }
}
