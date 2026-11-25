using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Prometheus.Core.Model;

namespace Prometheus.Core.Timeline;

public static class SubtitleAnimation
{
    public static string NormalizeTemplate(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024) throw new ArgumentException("La plantilla supera 4 MB.");
        var root = JsonNode.Parse(json) as JsonObject ?? throw new ArgumentException("Plantilla MGT no válida.");
        if (root["subtitles"] is not JsonObject subtitles || subtitles["kind"]?.GetValue<string>() != "template" ||
            subtitles["cues"] is not JsonArray { Count: 0 })
            throw new ArgumentException("Elige una plantilla de subtítulos sin transcripción (subtitles.kind: template, cues vacíos).");
        subtitles["version"] ??= 1; subtitles["timing"] ??= "cue"; subtitles["tail"] ??= .25;
        using (var contract = JsonDocument.Parse(subtitles.ToJsonString()))
        {
            var data = contract.RootElement;
            if (!data.GetProperty("version").TryGetInt32(out var version) || version != 1 ||
                data.GetProperty("timing").GetString() is not ("word" or "cue" or "estimated") ||
                data.GetProperty("tail").ValueKind != JsonValueKind.Number || !data.GetProperty("tail").TryGetDouble(out var tail) ||
                !double.IsFinite(tail) || tail < 0 || tail > 5) throw new ArgumentException("Contrato de tiempos de la plantilla no válido.");
        }
        root["kind"] ??= "composition";
        root["recommended"] = new JsonObject { ["duration"] = 1, ["aspect"] = "horizontal", ["resolution"] = 1080 };
        using var document = JsonDocument.Parse(root.ToJsonString());
        MgtLiveAsset.ValidateComposition(document.RootElement);
        var normalized = root.ToJsonString();
        var defaults = new SubtitleAnimationSettings { TemplateJson = normalized };
        foreach (var parameter in MgtLiveAsset.ReadParameters(document.RootElement)) SetParameter(defaults, parameter.Id, parameter.Default);
        return normalized;
    }

    public static void Set(Sequence sequence, string? json)
    {
        var normalized = json == null ? null : NormalizeTemplate(json);
        sequence.SubtitleAnimation = normalized == null ? null : new SubtitleAnimationSettings { TemplateJson = normalized };
        if (normalized != null) SubtitleTrack.Ensure(sequence);
    }

    public static IReadOnlyList<MgtParameter> Parameters(SubtitleAnimationSettings settings)
    {
        using var document = JsonDocument.Parse(settings.TemplateJson);
        return MgtLiveAsset.ReadParameters(document.RootElement);
    }

    public static void SetParameter(SubtitleAnimationSettings settings, string id, JsonElement value)
    {
        var parameter = Parameters(settings).SingleOrDefault(p => p.Id == id) ?? throw new ArgumentException("Parámetro MGT desconocido.");
        var valid = parameter.Type switch
        {
            "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) && double.IsFinite(n) &&
                (parameter.Min == null || n >= parameter.Min) && (parameter.Max == null || n <= parameter.Max),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "select" or "image" => value.ValueKind == JsonValueKind.String && parameter.Options.Contains(value.GetString()!),
            "color" => value.ValueKind == JsonValueKind.String && Regex.IsMatch(value.GetString()!, "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$"),
            "text" or "font" => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 1000,
            _ => false
        };
        if (!valid) throw new ArgumentException("El valor no es compatible con el parámetro MGT.");
        settings.Parameters[id] = value.Clone();
    }

    public sealed record Word(string text, double start, double end);
    public sealed record Cue(string text, double start, double end, IReadOnlyList<Word>? words)
    {
        public string timing { get; init; } = "cue";
    }
    public sealed record Transcript(int version, string kind, string timing, double tail, IReadOnlyList<Cue> cues);

    public static Transcript BuildTranscript(Project project)
    {
        var sequence = project.ActiveSequence!;
        var track = SubtitleTrack.Find(sequence);
        var cues = new List<Cue>();
        var anyTimed = false;
        foreach (var clip in sequence.Clips.Where(c => c.TrackId == track?.Id && c.Enabled).OrderBy(c => c.TimelineStart.Seconds))
        {
            var text = project.Assets.FirstOrDefault(a => a.Id == clip.AssetId)?.TextOverlay?.Content;
            if (string.IsNullOrWhiteSpace(text)) continue;
            var words = WordsFor(sequence, clip, text);
            // An aligned caption trimmed into a pause has no visible words.
            // null means unproven alignment; an empty list means proven silence.
            if (words is { Count: 0 }) continue;
            var timed = words != null;
            var label = words is { Count: > 0 } ? string.Join(" ", words.Select(w => w.text)) : text;
            if (words is not { Count: > 0 })
            {
                words = new[] { new Word(text, clip.TimelineStart.Seconds, clip.EndSeconds) };
            }
            anyTimed |= timed;
            cues.Add(new Cue(label, clip.TimelineStart.Seconds, clip.EndSeconds, words) { timing = timed ? "word" : "cue" });
        }
        using var template = JsonDocument.Parse(sequence.SubtitleAnimation!.TemplateJson);
        var tail = template.RootElement.GetProperty("subtitles").TryGetProperty("tail", out var t) && t.TryGetDouble(out var seconds)
            && double.IsFinite(seconds) ? Math.Clamp(seconds, 0, 5) : .25;
        // Existing templates branch on the track mode. Keep word animation available
        // in mixed tracks; phrase-only captions still contain one whole-phrase word.
        return new(1, "composition", anyTimed ? "word" : "cue", tail, cues);
    }

    private static IReadOnlyList<Word>? WordsFor(Sequence sequence, Clip clip, string text)
    {
        var original = clip.CaptionWords;
        if (original != null && Normalize(string.Join(" ", original.Select(w => w.Text))) == Normalize(text))
            return original.Select(w => new Word(w.Text,
                clip.TimelineStart.Seconds + (w.Start - clip.SourceIn.Seconds) / clip.Speed.Seconds,
                clip.TimelineStart.Seconds + (w.End - clip.SourceIn.Seconds) / clip.Speed.Seconds))
                .Where(w => w.end > w.start && w.end > clip.TimelineStart.Seconds && w.start < clip.EndSeconds)
                .Select(w => w with { start = Math.Max(clip.TimelineStart.Seconds, w.start), end = Math.Min(clip.EndSeconds, w.end) }).ToArray();
        if (original != null) return null; // Edited text has no proven word alignment.
        var document = sequence.Transcriptions.FirstOrDefault(d => d.Id == clip.TranscriptId);
        if (document?.TimingPrecision != "word") return null;
        var source = sequence.Clips.FirstOrDefault(c => c.Id == TranscriptOperations.AudioSourceId(sequence, clip));
        var origin = document.SourceTimeOrigin ?? source?.SourceIn.Seconds ?? 0;
        var rate = document.SourceTimeOrigin != null ? document.SourceTimeRate : source?.Speed.Seconds ?? 1;
        var mapped = document.Words.Select(w => new Word(w.Text,
            source == null ? document.TimelineStart + w.Start : source.TimelineStart.Seconds + (origin + w.Start * rate - source.SourceIn.Seconds) / source.Speed.Seconds,
            source == null ? document.TimelineStart + w.End : source.TimelineStart.Seconds + (origin + w.End * rate - source.SourceIn.Seconds) / source.Speed.Seconds))
            .Where(w => w.end > w.start && w.end > clip.TimelineStart.Seconds && w.start < clip.EndSeconds).ToArray();
        if (Normalize(string.Join(" ", mapped.Select(w => w.text))) != Normalize(text)) return null;
        return mapped.Select(w => w with { start = Math.Max(w.start, clip.TimelineStart.Seconds), end = Math.Min(w.end, clip.EndSeconds) }).ToArray();
    }

    private static string Normalize(string text) => Regex.Replace(text.Trim(), @"\s+", " ");
}
