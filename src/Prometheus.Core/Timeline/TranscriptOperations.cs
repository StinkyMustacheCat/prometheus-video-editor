using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Timeline;

public sealed record TranscriptCaption(double Start, double End, string Text, IReadOnlyList<CaptionWord>? Words = null);

public static class TranscriptOperations
{
    public static double ClampCaptionSourceIn(Clip clip, double desired)
    {
        if (desired < 0 && clip.CaptionWords != null)
            clip.CaptionWords = clip.CaptionWords.Select(w => w with { Start = w.Start - desired, End = w.End - desired }).ToList();
        return Math.Max(0, desired);
    }
    private static readonly Regex WordlineRow = new(@"^(\d+\.\d{3}) (\d+\.\d{3}) (.+)$", RegexOptions.Compiled);
    private static readonly Regex ClockRow = new(@"(\d{1,2}:\d{2}(?::\d{2})?[,.]\d{3})\s*-->\s*(\d{1,2}:\d{2}(?::\d{2})?[,.]\d{3})", RegexOptions.Compiled);

    public static string? AudioSourceId(Sequence sequence, Clip caption) =>
        caption.TranscriptId is { } id
            ? caption.SubtitleAudioClipId ?? sequence.Transcriptions.FirstOrDefault(document => document.Id == id)?.SourceClipId
            : null;

    public static bool IsSource(TranscriptDocument document, Clip clip) =>
        document.SourceClipId is { } id &&
        (clip.Id == id || clip.AudioSplitAncestors?.Contains(id) == true);

    private static void SetSourceTiming(TranscriptDocument document, Clip source)
    {
        if (document.SourceTimeOrigin != null) return;
        document.SourceTimeOrigin = source.SourceIn.Seconds;
        document.SourceTimeRate = source.Speed.Seconds;
    }

    private static double SourceTime(Clip clip, double timeline) =>
        clip.SourceIn.Seconds + (timeline - clip.TimelineStart.Seconds) * clip.Speed.Seconds;

    private static double TimelineTime(Clip clip, double source) =>
        clip.TimelineStart.Seconds + (source - clip.SourceIn.Seconds) / clip.Speed.Seconds;

    private static double SourceEnd(Clip clip) => SourceTime(clip, clip.EndSeconds);

    /// <summary>Project linked captions onto the surviving audio regions in the same undoable edit.</summary>
    public static void FollowAudioEdits(Project before, Project after)
    {
        const double tolerance = 0.0001;
        var removedAssets = new HashSet<string>();
        foreach (var sequence in after.Sequences.Where(item => item.Transcriptions.Any(doc => doc.SourceClipId != null)))
        {
            var previous = before.Sequences.FirstOrDefault(item => item.Id == sequence.Id);
            if (previous == null) continue;
            var oldClips = previous.Clips.ToDictionary(clip => clip.Id);
            var captions = sequence.Clips.Where(clip => clip.TranscriptId != null).ToLookup(clip => clip.TranscriptId);
            var lockedTracks = sequence.Tracks.Where(track => track.Locked).Select(track => track.Id).ToHashSet();
            var removed = new HashSet<string>();
            var adjusted = new HashSet<string>();
            var touchedTracks = new HashSet<string>();

            foreach (var document in sequence.Transcriptions.Where(doc => doc.SourceClipId != null))
            {
                var oldSources = previous.Clips.Where(clip => IsSource(document, clip)).ToArray();
                if (oldSources.Length == 0) continue;
                var fragments = sequence.Clips.Where(clip => IsSource(document, clip))
                    .OrderBy(clip => clip.TimelineStart.Seconds).ToArray();
                var piecesBySource = fragments.ToLookup(clip => oldClips.ContainsKey(clip.Id)
                    ? clip.Id : clip.AudioSplitAncestors?.LastOrDefault(oldClips.ContainsKey));
                var cuesBySource = captions[document.Id].Where(cue => oldClips.TryGetValue(cue.Id, out var oldCue) &&
                    oldCue.TranscriptId == document.Id).ToLookup(cue => AudioSourceId(previous, oldClips[cue.Id]));
                foreach (var oldSource in oldSources)
                {
                    var pieces = piecesBySource[oldSource.Id].ToArray();
                    var unchangedRegion = pieces.Length == 1 && pieces[0].Id == oldSource.Id &&
                        pieces[0].SourceIn == oldSource.SourceIn && pieces[0].Speed == oldSource.Speed &&
                        pieces[0].Duration == oldSource.Duration;
                    if (unchangedRegion && pieces[0].TimelineStart == oldSource.TimelineStart) continue;
                    SetSourceTiming(document, oldSources.FirstOrDefault(c => c.Id == document.SourceClipId) ?? oldSources[0]);

                    foreach (var cue in cuesBySource[oldSource.Id])
                    {
                        var oldCue = oldClips[cue.Id];
                        if (lockedTracks.Contains(cue.TrackId))
                            throw new InvalidOperationException("Desbloquea la pista de subtítulos para editar su audio vinculado.");
                        if (unchangedRegion)
                        {
                            var start = oldCue.TimelineStart.Seconds +
                                pieces[0].TimelineStart.Seconds - oldSource.TimelineStart.Seconds;
                            if (start < -tolerance)
                                throw new InvalidOperationException("El movimiento dejaría subtítulos vinculados antes del inicio del montaje.");
                            cue.TimelineStart = Rational.Time(Math.Max(0, start));
                            MarkAdjusted(cue);
                            continue;
                        }

                        var sourceStart = SourceTime(oldSource, oldCue.TimelineStart.Seconds);
                        var sourceEnd = SourceTime(oldSource, oldCue.EndSeconds);
                        var first = true;
                        foreach (var piece in pieces)
                        {
                            var start = Math.Max(sourceStart, piece.SourceIn.Seconds);
                            var end = Math.Min(sourceEnd, SourceEnd(piece));
                            var duration = (end - start) / piece.Speed.Seconds;
                            if (duration + tolerance < 1 / Math.Max(1, sequence.Fps.Seconds)) continue;
                            var segment = first ? cue : CollisionEngine.CopyClip(oldCue);
                            segment.SubtitleAudioClipId = piece.Id;
                            segment.TimelineStart = Rational.Time(TimelineTime(piece, start));
                            segment.Duration = Rational.Time(duration);
                            segment.Speed = Rational.Time(oldCue.Speed.Seconds * piece.Speed.Seconds / oldSource.Speed.Seconds);
                            var offset = (start - sourceStart) / oldSource.Speed.Seconds;
                            segment.SourceIn = Rational.Time(oldCue.SourceIn.Seconds + offset * oldCue.Speed.Seconds);
                            if (offset > tolerance) KeyframeAnimation.ShiftForLeftTrim(segment, offset);
                            if (!first) sequence.Clips.Add(segment);
                            MarkAdjusted(segment);
                            first = false;
                        }
                        if (first)
                        {
                            removed.Add(cue.Id);
                            removedAssets.Add(cue.AssetId);
                        }
                    }
                }

                if (fragments.Length == 0)
                {
                    document.SourceClipId = null;
                }
                else if (document.TimelineStart != fragments[0].TimelineStart.Seconds)
                {
                    document.TimelineStart = fragments[0].TimelineStart.Seconds;
                }
            }

            sequence.Clips.RemoveAll(clip => removed.Contains(clip.Id));
            foreach (var track in sequence.Clips.Where(clip => touchedTracks.Contains(clip.TrackId)).GroupBy(clip => clip.TrackId))
            {
                var latestEnd = double.NegativeInfinity;
                var latestAdjustedEnd = double.NegativeInfinity;
                foreach (var clip in track.OrderBy(clip => clip.TimelineStart.Seconds))
                {
                    var isAdjusted = adjusted.Contains(clip.Id);
                    if (clip.TimelineStart.Seconds + tolerance < (isAdjusted ? latestEnd : latestAdjustedEnd))
                        throw new InvalidOperationException("La edición superpondría subtítulos vinculados con otras cápsulas.");
                    latestEnd = Math.Max(latestEnd, clip.EndSeconds);
                    if (isAdjusted) latestAdjustedEnd = Math.Max(latestAdjustedEnd, clip.EndSeconds);
                }
            }

            void MarkAdjusted(Clip cue)
            {
                adjusted.Add(cue.Id);
                touchedTracks.Add(cue.TrackId);
            }
        }
        RemoveUnusedCaptionAssets(after, removedAssets);
    }

    private static void RemoveUnusedCaptionAssets(Project project, HashSet<string> candidates)
    {
        if (candidates.Count == 0) return;
        var unused = new HashSet<string>(candidates);
        foreach (var clip in project.Sequences.SelectMany(sequence => sequence.Clips)) KeepReferenced(clip);
        project.Assets.RemoveAll(asset => unused.Contains(asset.Id));

        void KeepReferenced(Clip clip)
        {
            unused.Remove(clip.AssetId);
            if (clip.NestedContent is { } nested)
                foreach (var child in nested.Clips) KeepReferenced(child);
        }
    }

    public static string ToWordline(TranscriptDocument document)
    {
        document.Validate();
        if (document.TimingPrecision != "word")
            throw new InvalidOperationException("Este documento solo tiene tiempos por frase.");
        var output = new StringBuilder("UNIDAD: segundos (precisión de milisegundos)\nFORMATO: inicio fin contenido\n\n");
        foreach (var word in document.Words)
            output.Append(word.Start.ToString("0.000", CultureInfo.InvariantCulture)).Append(' ')
                .Append(word.End.ToString("0.000", CultureInfo.InvariantCulture)).Append(' ')
                .Append(word.Text.Replace('\n', ' ')).Append('\n');
        return output.ToString();
    }

    public static TranscriptDocument AlignToClip(TranscriptDocument source, Clip clip)
    {
        source.Validate();
        var begin = clip.SourceIn.Seconds;
        var speed = clip.Speed.Seconds;
        if (speed <= 0 || clip.Duration.Seconds <= 0)
            throw new ArgumentException("El clip tiene un tiempo o velocidad inválidos.");
        var limit = begin + clip.Duration.Seconds * speed;
        var mapped = new TranscriptDocument { Name = source.Name, Language = source.Language,
            TimingPrecision = source.TimingPrecision, CaptionMode = source.CaptionMode,
            SourceClipId = clip.Id, TimelineStart = clip.TimelineStart.Seconds,
            SourceTimeOrigin = begin, SourceTimeRate = speed };
        foreach (var word in source.Words.Where(word => word.End > begin && word.Start < limit))
        {
            var start = Math.Max(0, (Math.Max(begin, word.Start) - begin) / speed);
            var end = Math.Min(clip.Duration.Seconds, (Math.Min(limit, word.End) - begin) / speed);
            if (end > start) mapped.Words.Add(new TranscriptWord { Start = start,
                End = end, Text = word.Text });
        }
        mapped.Validate();
        return mapped;
    }

    public static TranscriptDocument Parse(string content, string name, double at)
    {
        var document = new TranscriptDocument { Name = name, TimelineStart = at };
        var lines = (content ?? "").TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length >= 3 && lines[0] == "UNIDAD: segundos (precisión de milisegundos)" &&
            lines[1] == "FORMATO: inicio fin contenido" && lines[2] == "")
        {
            foreach (var line in lines.Skip(3).Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                var match = WordlineRow.Match(line);
                if (!match.Success) throw new FormatException("Línea Wordline inválida: " + line);
                var value = match.Groups[3].Value;
                if (Regex.IsMatch(value, @"^\[(?:SIN TEXTO|PAUSA|VOZ ININTELIGIBLE|POSIBLE VOZ|SONIDO SIN IDENTIFICAR) \d+\.\d{3}\]$")) continue;
                document.Words.Add(new TranscriptWord { Start = Number(match.Groups[1].Value),
                    End = Number(match.Groups[2].Value), Text = value });
            }
        }
        else
        {
            document.TimingPrecision = "cue";
            var blocks = Regex.Split(string.Join("\n", lines).Trim(), @"\n\s*\n");
            foreach (var block in blocks)
            {
                var rows = block.Split('\n');
                var timeIndex = Array.FindIndex(rows, row => row.Contains("-->", StringComparison.Ordinal));
                if (timeIndex < 0) continue;
                var match = ClockRow.Match(rows[timeIndex]);
                if (!match.Success) throw new FormatException("Tiempo SRT/VTT inválido.");
                var value = Regex.Replace(string.Join(" ", rows.Skip(timeIndex + 1)), @"<[^>]+>", "").Trim();
                if (value.Length == 0) continue;
                document.Words.Add(new TranscriptWord { Start = Clock(match.Groups[1].Value),
                    End = Clock(match.Groups[2].Value), Text = value });
            }
        }
        document.Validate();
        return document;
    }

    public static IReadOnlyList<TranscriptCaption> Captions(TranscriptDocument document, double start)
    {
        document.Validate();
        if (document.CaptionMode == "word" && document.TimingPrecision == "word")
            return document.Words.Select(word => new TranscriptCaption(start + word.Start,
                start + word.End, word.Text, new[] { new CaptionWord(word.Id, word.Text, 0, word.End - word.Start) })).ToArray();
        var result = new List<TranscriptCaption>();
        var group = new List<TranscriptWord>();
        void Flush()
        {
            if (group.Count == 0) return;
            result.Add(new TranscriptCaption(start + group[0].Start,
                start + group.Max(word => word.End),
                string.Join(" ", group.Select(word => word.Text)), document.TimingPrecision != "word" ? null : group.Select(word =>
                    new CaptionWord(word.Id, word.Text, word.Start - group[0].Start, word.End - group[0].Start)).ToArray()));
            group.Clear();
        }
        foreach (var word in document.Words)
        {
            if (group.Count > 0 && (document.TimingPrecision == "cue" ||
                word.Start - group[^1].End > 0.7 || word.End - group[0].Start > 6 ||
                group.Sum(item => item.Text.Length + 1) + word.Text.Length > 78)) Flush();
            group.Add(word);
            if (Regex.IsMatch(word.Text, @"[.!?]$")) Flush();
        }
        Flush();
        return result;
    }

    public static double? TimelinePositionForWord(Sequence sequence, TranscriptDocument document, TranscriptWord word)
    {
        if (document.SourceClipId == null) return document.TimelineStart + word.Start;
        var sources = sequence.Clips.Where(c => IsSource(document, c)).ToArray();
        var original = sources.FirstOrDefault(c => c.Id == document.SourceClipId);
        if (document.SourceTimeOrigin == null && original == null) return null;
        var origin = document.SourceTimeOrigin ?? original!.SourceIn.Seconds;
        var rate = document.SourceTimeOrigin == null ? original!.Speed.Seconds : document.SourceTimeRate;
        var start = origin + word.Start * rate;
        var end = origin + word.End * rate;
        var piece = sources.Where(c => c.SourceIn.Seconds < end && SourceEnd(c) > start)
            .MinBy(c => c.TimelineStart.Seconds);
        return piece == null ? null : TimelineTime(piece, Math.Max(start, piece.SourceIn.Seconds));
    }

    private static IEnumerable<(TranscriptCaption Caption, string? SourceId)> CaptionsForSequence(
        TranscriptDocument document, Clip[] sources)
    {
        if (document.SourceClipId == null)
            return Captions(document, document.TimelineStart).Select(c => (c, (string?)null));
        if (sources.Length == 0) return [];
        SetSourceTiming(document, sources.FirstOrDefault(c => c.Id == document.SourceClipId) ?? sources[0]);
        document.Validate();
        var captions = new List<(TranscriptCaption Caption, string? SourceId)>();
        var origin = document.SourceTimeOrigin!.Value;
        var rate = document.SourceTimeRate;
        foreach (var source in sources)
        {
            var mapped = new TranscriptDocument { CaptionMode = document.CaptionMode, TimingPrecision = document.TimingPrecision };
            var end = SourceEnd(source);
            foreach (var word in document.Words)
            {
                var wordStart = origin + word.Start * rate;
                if (wordStart >= end) break;
                var begin = Math.Max(wordStart, source.SourceIn.Seconds);
                var limit = Math.Min(origin + word.End * rate, end);
                if (limit <= begin) continue;
                mapped.Words.Add(new TranscriptWord { Id = word.Id, Text = word.Text,
                    Start = (begin - source.SourceIn.Seconds) / source.Speed.Seconds,
                    End = (limit - source.SourceIn.Seconds) / source.Speed.Seconds });
            }
            if (mapped.Words.Count > 0)
                captions.AddRange(Captions(mapped, source.TimelineStart.Seconds).Select(c => (c, (string?)source.Id)));
        }
        return captions.OrderBy(item => item.Caption.Start);
    }

    public static IReadOnlyList<string> Apply(Project project, string documentId)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var document = sequence.Transcriptions.Single(item => item.Id == documentId);
        var sources = sequence.Clips.Where(clip => IsSource(document, clip))
            .OrderBy(clip => clip.TimelineStart.Seconds).ToArray();
        var fps = Math.Max(1, sequence.Fps.Seconds);
        var frame = 1 / fps;
        var planned = new List<(TranscriptCaption Caption, string? SourceId)>();
        var previousEnd = -1d;
        foreach (var (caption, sourceId) in CaptionsForSequence(document, sources))
        {
            var at = Math.Max(0, Math.Round(caption.Start * fps) / fps);
            at = Math.Max(at, previousEnd);
            var end = Math.Max(at + frame, Math.Round(caption.End * fps) / fps);
            if (sourceId != null)
            {
                var source = sources.Single(clip => clip.Id == sourceId);
                at = Math.Max(at, source.TimelineStart.Seconds);
                end = Math.Min(end, source.EndSeconds);
                if (end - at + 0.0001 < frame) continue;
            }
            var shift = caption.Start - at;
            planned.Add((new TranscriptCaption(at, end, caption.Text, caption.Words?.Select(word =>
                word with { Start = word.Start + shift, End = word.End + shift }).ToArray()), sourceId));
            previousEnd = end;
        }
        var subtitle = SubtitleTrack.Find(sequence);
        var old = sequence.Clips.Where(clip => clip.TranscriptId == documentId).ToList();
        if (subtitle != null && subtitle.Locked)
            throw new InvalidOperationException("La pista de subtítulos está bloqueada.");
        Clip[] manual = subtitle == null ? [] : sequence.Clips.Where(clip => clip.TrackId == subtitle.Id &&
            clip.TranscriptId != documentId).ToArray();
        if (planned.Any(item => manual.Any(clip => clip.TimelineStart.Seconds < item.Caption.End - 0.000001 &&
            clip.EndSeconds > item.Caption.Start + 0.000001)))
            throw new InvalidOperationException("Hay subtítulos manuales en ese intervalo. Muévelos antes de actualizar.");
        sequence.Clips.RemoveAll(clip => clip.TranscriptId == documentId);
        RemoveUnusedCaptionAssets(project, old.Select(clip => clip.AssetId).ToHashSet());
        var ids = new List<string>();
        foreach (var (caption, sourceId) in planned)
        {
            var cue = SubtitleTrack.Add(project, caption.Text, caption.Start, caption.End - caption.Start);
            cue.TranscriptId = documentId;
            cue.CaptionWords = caption.Words?.ToList();
            cue.SubtitleAudioClipId = sourceId;
            ids.Add(cue.Id);
        }
        sequence.SubtitleLaneExpanded = true;
        if (sources.Length > 0) document.TimelineStart = sources[0].TimelineStart.Seconds;
        return ids;
    }

    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    private static double Clock(string value)
    {
        var parts = value.Replace(',', '.').Split(':');
        if (parts.Length is < 2 or > 3) throw new FormatException("Tiempo inválido.");
        var hours = parts.Length == 3 ? int.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
        var minutes = int.Parse(parts[^2], CultureInfo.InvariantCulture);
        return hours * 3600 + minutes * 60 + Number(parts[^1]);
    }
}
