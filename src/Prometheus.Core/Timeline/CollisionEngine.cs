using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Timeline;

public class MicroGapInfo
{
    public string TrackId { get; set; } = string.Empty;
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);
}

public class ShortTailInfo
{
    public bool HasShortTail { get; set; }
    public double AvailableMediaSeconds { get; set; }
    public double MissingStartOffsetSeconds { get; set; }
    public double MissingDurationSeconds { get; set; }
}

public static class CollisionEngine
{
    public const double MicroGapThresholdSeconds = 3.0 / 60.0; // 3 fotogramas a 60 FPS (~0.05s)

    internal static Clip CopyClip(Clip original)
    {
        var copy = JsonSerializer.Deserialize<Clip>(JsonSerializer.Serialize(original))!;
        copy.Id = Guid.NewGuid().ToString("N");
        var effectIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var effect in copy.Effects)
        {
            var oldId = effect.Id;
            effect.Id = Guid.NewGuid().ToString("N");
            effectIds[oldId] = effect.Id;
        }
        KeyframeAnimation.RemapEffectInstances(copy, effectIds);
        copy.AudioSplitAncestors = null;
        return copy;
    }

    internal static Clip CopySplitPiece(Sequence sequence, Clip original)
    {
        var copy = CopyClip(original);
        foreach (var transition in sequence.VideoTransitions.Where(t => t.LeftClipId == original.Id))
            transition.LeftClipId = copy.Id;
        AudioTransitionOperations.MoveEndToRightPiece(sequence.AudioTransitions, original.Id, copy.Id);
        if (sequence.Tracks.Any(track => track.Id == original.TrackId && track.Kind == "audio"))
            copy.AudioSplitAncestors = [.. original.AudioSplitAncestors ?? [], original.Id];
        return copy;
    }

    /// <summary>
    /// Divide un clip exactamente en el tiempo indicado (Razor Cut / Rocker Gesture).
    /// </summary>
    public static (Clip? Left, Clip? Right) SplitClip(Sequence sequence, string clipId, double cutTimeSeconds)
    {
        var clip = sequence.Clips.Find(c => string.Equals(c.Id, clipId, StringComparison.OrdinalIgnoreCase));
        if (clip == null)
            return (null, null);

        var track = sequence.Tracks.Find(t => string.Equals(t.Id, clip.TrackId, StringComparison.OrdinalIgnoreCase));
        if (track != null && track.Locked)
            return (null, null);

        double start = clip.TimelineStart.Seconds;
        double end = clip.EndSeconds;

        // Si el corte está en los bordes o fuera del clip, no hay división
        var minimum = 1 / Math.Max(1, sequence.Fps.Seconds);
        if (cutTimeSeconds < start + minimum - 0.000001 || cutTimeSeconds > end - minimum + 0.000001)
            return (clip, null);

        double leftDuration = cutTimeSeconds - start;
        double rightDuration = end - cutTimeSeconds;

        var rightClip = CopySplitPiece(sequence, clip);
        rightClip.TimelineStart = Rational.Time(cutTimeSeconds);
        rightClip.Duration = Rational.Time(rightDuration);
        rightClip.SourceIn = Rational.Time(clip.SourceIn.Seconds + leftDuration * clip.Speed.Seconds);
        KeyframeAnimation.ShiftForLeftTrim(rightClip, leftDuration);

        clip.Duration = Rational.Time(leftDuration);
        sequence.Clips.Add(rightClip);

        // Actualizar link_groups si el clip pertenecía a alguno
        foreach (var lg in sequence.LinkGroups)
        {
            if (lg.ClipIds.Contains(clip.Id))
            {
                lg.ClipIds.Add(rightClip.Id);
            }
        }

        return (clip, rightClip);
    }

    public static IReadOnlyList<Clip> SplitLinkedClips(Sequence sequence, string anchorId, double cutTimeSeconds)
    {
        var anchor = sequence.Clips.Find(c => c.Id == anchorId);
        var minimum = 1 / Math.Max(1, sequence.Fps.Seconds);
        if (anchor == null || cutTimeSeconds < anchor.TimelineStart.Seconds + minimum - 0.000001 ||
            cutTimeSeconds > anchor.EndSeconds - minimum + 0.000001)
            return Array.Empty<Clip>();
        var group = sequence.LinkGroups.FirstOrDefault(g => g.ClipIds.Contains(anchorId));
        var ids = group?.ClipIds.ToArray() ?? new[] { anchorId };
        var crossing = sequence.Clips.Where(c => ids.Contains(c.Id) && cutTimeSeconds >= c.TimelineStart.Seconds + minimum - 0.000001 &&
            cutTimeSeconds <= c.EndSeconds - minimum + 0.000001).ToArray();
        if (crossing.Any(c => sequence.Tracks.Any(t => t.Id == c.TrackId && t.Locked))) return Array.Empty<Clip>();
        var rightMap = new Dictionary<string, string>();
        var rights = new List<Clip>();
        foreach (var clip in crossing)
        {
            var (_, right) = SplitClip(sequence, clip.Id, cutTimeSeconds);
            if (right == null) continue;
            rightMap[clip.Id] = right.Id;
            rights.Add(right);
            foreach (var editGroup in sequence.EditGroups.Where(g => g.ClipIds.Contains(clip.Id))) editGroup.ClipIds.Add(right.Id);
        }
        if (group != null && rights.Count > 0)
        {
            sequence.LinkGroups.Remove(group);
            var leftIds = ids.Where(id => sequence.Clips.Any(c => c.Id == id && c.TimelineStart.Seconds < cutTimeSeconds)).ToList();
            var rightIds = ids.Select(id => rightMap.TryGetValue(id, out var rightId) ? rightId :
                sequence.Clips.FirstOrDefault(c => c.Id == id && c.TimelineStart.Seconds >= cutTimeSeconds)?.Id)
                .Where(id => id != null).Select(id => id!).ToList();
            if (leftIds.Count > 1) sequence.LinkGroups.Add(new LinkGroup { ClipIds = leftIds });
            if (rightIds.Count > 1) sequence.LinkGroups.Add(new LinkGroup { ClipIds = rightIds });
        }
        return rights;
    }

    /// <summary>
    /// Ejecuta sobrescritura no destructiva dividiendo y recortando clips subyacentes.
    /// </summary>
    public static void Overwrite(Sequence sequence, IEnumerable<string> incomingClipIds)
    {
        var incomingSet = new HashSet<string>(incomingClipIds, StringComparer.OrdinalIgnoreCase);
        var incomingClips = sequence.Clips.Where(c => incomingSet.Contains(c.Id)).ToList();
        if (incomingClips.Any(c => sequence.Tracks.Any(t => t.Id == c.TrackId && t.Locked)))
            throw new InvalidOperationException("La pista de destino está bloqueada");
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rightMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var inc in incomingClips)
        {
            double incStart = inc.TimelineStart.Seconds;
            double incEnd = inc.EndSeconds;

            var overlapping = sequence.Clips
                .Where(c => !incomingSet.Contains(c.Id) &&
                            string.Equals(c.TrackId, inc.TrackId, StringComparison.OrdinalIgnoreCase) &&
                            !removed.Contains(c.Id) && c.TimelineStart.Seconds < incEnd - 0.0001 &&
                            c.EndSeconds > incStart + 0.0001)
                .ToList();

            foreach (var old in overlapping)
            {
                double oldStart = old.TimelineStart.Seconds;
                double oldEnd = old.EndSeconds;
                bool keepLeft = oldStart < incStart - 0.0001 && incStart - oldStart >= 1.0 / 60.0;
                bool keepRight = oldEnd > incEnd + 0.0001 && oldEnd - incEnd >= 1.0 / 60.0;
                if (keepLeft && keepRight)
                {
                    var right = CopySplitPiece(sequence, old);
                    right.TimelineStart = Rational.Time(incEnd);
                    right.Duration = Rational.Time(oldEnd - incEnd);
                    right.SourceIn = Rational.Time(old.SourceIn.Seconds + (incEnd - oldStart) * old.Speed.Seconds);
                    KeyframeAnimation.ShiftForLeftTrim(right, incEnd - oldStart);
                    old.Duration = Rational.Time(incStart - oldStart);
                    sequence.Clips.Add(right);
                    rightMap[old.Id] = right.Id;
                }
                else if (keepLeft)
                {
                    RemoveEdgeTransitions(sequence, old.Id, fromStart: false);
                    old.Duration = Rational.Time(incStart - oldStart);
                }
                else if (keepRight)
                {
                    RemoveEdgeTransitions(sequence, old.Id, fromStart: true);
                    old.TimelineStart = Rational.Time(incEnd);
                    old.Duration = Rational.Time(oldEnd - incEnd);
                    old.SourceIn = Rational.Time(old.SourceIn.Seconds + (incEnd - oldStart) * old.Speed.Seconds);
                    KeyframeAnimation.ShiftForLeftTrim(old, incEnd - oldStart);
                }
                else removed.Add(old.Id);
            }
        }
        sequence.Clips.RemoveAll(c => removed.Contains(c.Id));
        sequence.VideoTransitions.RemoveAll(t => removed.Contains(t.LeftClipId) || removed.Contains(t.RightClipId));
        sequence.AudioTransitions.RemoveAll(t => removed.Contains(t.ClipId) ||
            t.RightClipId != null && removed.Contains(t.RightClipId));
        foreach (var group in sequence.LinkGroups.ToArray())
        {
            var rights = group.ClipIds.Where(rightMap.ContainsKey).Select(id => rightMap[id]).ToList();
            group.ClipIds.RemoveAll(removed.Contains);
            if (group.ClipIds.Count < 2) sequence.LinkGroups.Remove(group);
            if (rights.Count > 1) sequence.LinkGroups.Add(new LinkGroup { ClipIds = rights });
        }
        foreach (var group in sequence.EditGroups.ToArray())
        {
            group.ClipIds.RemoveAll(removed.Contains);
            foreach (var id in group.ClipIds.ToArray())
                if (rightMap.TryGetValue(id, out var rightId)) group.ClipIds.Add(rightId);
            if (group.ClipIds.Count < 2) sequence.EditGroups.Remove(group);
        }
    }

    /// <summary>
    /// Elimina un intervalo de tiempo y desplaza los clips posteriores a la izquierda (Ripple Delete).
    /// </summary>
    public static void RippleDeleteRange(Sequence sequence, double startSeconds, double durationSeconds)
    {
        if (durationSeconds <= 0)
            return;

        if (!double.IsFinite(startSeconds) || !double.IsFinite(durationSeconds) || startSeconds < 0)
            throw new ArgumentException("Intervalo de desplazamiento inválido.");
        CloseInterval(sequence, sequence.Tracks.Select(track => track.Id).ToHashSet(),
            startSeconds + durationSeconds, durationSeconds);

    }

    /// <summary>
    /// Recorte por área / cuchilla de arrastre: elimina o secciona todos los clips dentro del rango [start, end] en pistas desbloqueadas.
    /// </summary>
    public static bool RangeTrim(Sequence sequence, double cutA, double cutB) =>
        RangeTrim(sequence, cutA, cutB, null);

    internal static bool RangeTrim(Sequence sequence, double cutA, double cutB, IReadOnlySet<string>? targetIds)
    {
        double a = Math.Min(cutA, cutB);
        double b = Math.Max(cutA, cutB);

        if (b - a + 1e-7 < 1.0 / Math.Max(1, sequence.Fps.Seconds))
            return false;

        var lockedTrackIds = new HashSet<string>(
            sequence.Tracks.Where(t => t.Locked).Select(t => t.Id),
            StringComparer.OrdinalIgnoreCase);

        var clipsToInspect = sequence.Clips
            .Where(c => !lockedTrackIds.Contains(c.TrackId) &&
                        (targetIds == null || targetIds.Contains(c.Id)) &&
                        c.TimelineStart.Seconds < b - 0.0001 &&
                        c.EndSeconds > a + 0.0001)
            .ToList();

        var editedIds = clipsToInspect.Select(clip => clip.Id).ToHashSet();
        clipsToInspect.RemoveAll(clip => TranscriptOperations.AudioSourceId(sequence, clip) is { } source &&
            editedIds.Contains(source));
        if (clipsToInspect.Count == 0)
            return false;

        (a, b) = EditTiming.RemovalInterval(sequence, a, b, clipsToInspect);
        if (b - a + 1e-7 < 1 / Math.Max(1, sequence.Fps.Seconds)) return false;

        var clipsToRemove = new List<Clip>();
        var clipsToAdd = new List<Clip>();
        var rightMap = new Dictionary<string, string>();

        foreach (var c in clipsToInspect)
        {
            double start = c.TimelineStart.Seconds;
            double end = c.EndSeconds;
            double ra = Math.Max(start, a);
            double rb = Math.Min(end, b);

            if (rb <= ra + 0.0001)
                continue;

            bool keepLeft = ra > start + 0.0001;
            bool keepRight = rb < end - 0.0001;

            if (!keepLeft && !keepRight)
            {
                // Clip completamente comprendido en el área de corte -> eliminar
                clipsToRemove.Add(c);
            }
            else if (!keepLeft)
            {
                // Solo se conserva la parte derecha
                double offset = rb - start;
                RemoveEdgeTransitions(sequence, c.Id, fromStart: true);
                c.TimelineStart = Rational.Time(rb);
                c.SourceIn = Rational.Time(c.SourceIn.Seconds + offset * c.Speed.Seconds);
                c.Duration = Rational.Time(end - rb);
                KeyframeAnimation.ShiftForLeftTrim(c, offset);
            }
            else if (!keepRight)
            {
                // Solo se conserva la parte izquierda
                RemoveEdgeTransitions(sequence, c.Id, fromStart: false);
                c.Duration = Rational.Time(ra - start);
            }
            else
            {
                // El corte está en el medio del clip -> se divide en dos trozos
                var right = CopySplitPiece(sequence, c);
                right.TimelineStart = Rational.Time(rb);
                right.Duration = Rational.Time(end - rb);
                right.SourceIn = Rational.Time(c.SourceIn.Seconds + (rb - start) * c.Speed.Seconds);
                KeyframeAnimation.ShiftForLeftTrim(right, rb - start);
                c.Duration = Rational.Time(ra - start);
                clipsToAdd.Add(right);
                rightMap[c.Id] = right.Id;
            }
        }

        foreach (var r in clipsToRemove)
        {
            sequence.Clips.Remove(r);
            RemoveEdgeTransitions(sequence, r.Id, fromStart: true);
            RemoveEdgeTransitions(sequence, r.Id, fromStart: false);
        }

        sequence.Clips.AddRange(clipsToAdd);

        // Actualizar grupos vinculados
        foreach (var lg in sequence.LinkGroups.ToList())
        {
            var rights = new List<string>();
            foreach (var id in lg.ClipIds)
            {
                if (rightMap.TryGetValue(id, out var rid))
                    rights.Add(rid);
            }
            lg.ClipIds.RemoveAll(id => clipsToRemove.Any(c => c.Id == id));
            if (rights.Count > 1)
            {
                sequence.LinkGroups.Add(new LinkGroup { Id = Guid.NewGuid().ToString("N"), ClipIds = rights });
            }
            if (lg.ClipIds.Count < 2)
                sequence.LinkGroups.Remove(lg);
        }

        foreach (var group in sequence.EditGroups.ToList())
        {
            group.ClipIds.RemoveAll(id => clipsToRemove.Any(c => c.Id == id));
            foreach (var id in group.ClipIds.ToArray())
                if (rightMap.TryGetValue(id, out var rightId)) group.ClipIds.Add(rightId);
            if (group.ClipIds.Count < 2) sequence.EditGroups.Remove(group);
        }

        return true;
    }

    /// <summary>
    /// Edición de rodillo (Roll Edit): ajusta el punto de unión entre dos clips contiguos simultáneamente
    /// de manera bidireccional (hacia adelante y hacia atrás).
    /// </summary>
    public static bool RollEdit(Sequence sequence, string leftClipId, string rightClipId, double newCutSeconds, Func<Clip, bool>? isStill = null)
    {
        var left = sequence.Clips.Find(c => string.Equals(c.Id, leftClipId, StringComparison.OrdinalIgnoreCase));
        var right = sequence.Clips.Find(c => string.Equals(c.Id, rightClipId, StringComparison.OrdinalIgnoreCase));

        if (left == null || right == null || !string.Equals(left.TrackId, right.TrackId, StringComparison.OrdinalIgnoreCase))
            return false;

        var track = sequence.Tracks.Find(t => string.Equals(t.Id, left.TrackId, StringComparison.OrdinalIgnoreCase));
        if (track == null || track.Locked)
            return false;

        if (!double.IsFinite(newCutSeconds) || !VideoTransitionOperations.IsCut(left, right))
            return false;

        double delta = newCutSeconds - left.EndSeconds;
        if (Math.Abs(delta) < 0.0001)
            return true;

        // Validar límites mínimos (al menos 1 fotograma a 60 FPS)
        double minDuration = 1 / Math.Max(1, sequence.Fps.Seconds);
        if (left.Duration.Seconds + delta < minDuration)
            delta = minDuration - left.Duration.Seconds;

        if (right.Duration.Seconds - delta < minDuration)
            delta = right.Duration.Seconds - minDuration;

        // Si el clip derecho tiene límite de material a la izquierda y se rueda hacia atrás (delta < 0)
        if (isStill?.Invoke(right) != true && right.AvailableSourceDurationSeconds.HasValue && right.SourceIn.Seconds + delta * right.Speed.Seconds < 0)
        {
            delta = -right.SourceIn.Seconds / right.Speed.Seconds;
        }

        // Si el clip izquierdo tiene límite de material a la derecha y se rueda hacia adelante (delta > 0)
        if (isStill?.Invoke(left) != true && left.AvailableSourceDurationSeconds.HasValue &&
            left.SourceIn.Seconds + (left.Duration.Seconds + delta) * left.Speed.Seconds > left.AvailableSourceDurationSeconds.Value)
        {
            delta = (left.AvailableSourceDurationSeconds.Value - left.SourceIn.Seconds) / left.Speed.Seconds - left.Duration.Seconds;
        }

        left.Duration = Rational.Time(Math.Max(minDuration, left.Duration.Seconds + delta));
        right.TimelineStart = Rational.Time(left.EndSeconds);
        right.SourceIn = Rational.Time(isStill?.Invoke(right) == true && right.CaptionWords == null ? 0 :
            TranscriptOperations.ClampCaptionSourceIn(right, right.SourceIn.Seconds + delta * right.Speed.Seconds));
        KeyframeAnimation.ShiftForLeftTrim(right, delta);
        right.Duration = Rational.Time(Math.Max(minDuration, right.Duration.Seconds - delta));

        return true;
    }

    /// <summary>
    /// Duplica clips preservando intactos los clips de origen y generando nombres limpios sin redundancias.
    /// </summary>
    public static List<Clip> DuplicateClips(Sequence sequence, IEnumerable<string> clipIds, double deltaTime)
    {
        var sourceClips = sequence.Clips.Where(c => clipIds.Contains(c.Id)).ToList();
        var newClips = new List<Clip>();

        foreach (var original in sourceClips)
        {
            string baseName = original.Name;
            string newName;

            var match = System.Text.RegularExpressions.Regex.Match(baseName, @"^(.*?)(\s*\(Copia(?:\s+(\d+))?\))+$");
            if (match.Success)
            {
                string root = match.Groups[1].Value.Trim();
                int num = 2;
                if (int.TryParse(match.Groups[3].Value, out int currentNum))
                    num = currentNum + 1;
                newName = $"{root} (Copia {num})";
            }
            else
            {
                newName = $"{baseName} (Copia)";
            }

            var copy = CopyClip(original);
            copy.Name = newName;
            copy.TranscriptId = null;
            copy.SubtitleAudioClipId = null;
            copy.TimelineStart = Rational.Time(Math.Max(0, original.TimelineStart.Seconds + deltaTime));

            sequence.Clips.Add(copy);
            newClips.Add(copy);
        }

        var copyMap = sourceClips.Zip(newClips).ToDictionary(pair => pair.First.Id, pair => pair.Second.Id);
        VideoTransitionOperations.CopyBetween(sequence, sequence, copyMap);
        AudioTransitionOperations.CopyBetween(sequence.AudioTransitions, sequence.AudioTransitions, copyMap);
        foreach (var group in sequence.LinkGroups.ToArray())
        {
            var members = group.ClipIds.Where(copyMap.ContainsKey).Select(id => copyMap[id]).ToList();
            if (members.Count > 1) sequence.LinkGroups.Add(new LinkGroup { ClipIds = members });
        }
        foreach (var group in sequence.EditGroups.ToArray())
        {
            var members = group.ClipIds.Where(copyMap.ContainsKey).Select(id => copyMap[id]).ToList();
            if (members.Count > 1) sequence.EditGroups.Add(new EditGroup { Number = sequence.EditGroups.Max(g => g.Number) + 1, ClipIds = members });
        }
        return newClips;
    }

    /// <summary>
    /// Detecta micro-huecos (<= 3 fotogramas) entre clips adyacentes en la misma pista.
    /// </summary>
    public static List<MicroGapInfo> DetectMicroGaps(Sequence sequence, double thresholdSeconds = MicroGapThresholdSeconds)
    {
        var result = new List<MicroGapInfo>();

        foreach (var track in sequence.Tracks)
        {
            var trackClips = sequence.Clips
                .Where(c => string.Equals(c.TrackId, track.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.TimelineStart.Seconds)
                .ToList();

            for (int i = 0; i < trackClips.Count - 1; i++)
            {
                double currentEnd = trackClips[i].EndSeconds;
                double nextStart = trackClips[i + 1].TimelineStart.Seconds;
                double gap = nextStart - currentEnd;

                if (gap > 0.0001 && gap <= thresholdSeconds + 0.0001)
                {
                    result.Add(new MicroGapInfo
                    {
                        TrackId = track.Id,
                        StartSeconds = currentEnd,
                        EndSeconds = nextStart
                    });
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Detecta si el clip excede la duración del archivo multimedia fuente (Short Tail).
    /// </summary>
    public static ShortTailInfo CheckShortTail(Clip clip, double streamDurationSeconds)
    {
        if (streamDurationSeconds <= 0)
            return new ShortTailInfo { HasShortTail = false };

        double neededSourceSeconds = clip.SourceIn.Seconds + (clip.Duration.Seconds * clip.Speed.Seconds);
        if (neededSourceSeconds > streamDurationSeconds + 0.001)
        {
            double availableMediaSeconds = Math.Max(0, streamDurationSeconds - clip.SourceIn.Seconds) / clip.Speed.Seconds;
            double missingDuration = clip.Duration.Seconds - availableMediaSeconds;

            return new ShortTailInfo
            {
                HasShortTail = true,
                AvailableMediaSeconds = availableMediaSeconds,
                MissingStartOffsetSeconds = availableMediaSeconds,
                MissingDurationSeconds = Math.Max(0, missingDuration)
            };
        }

        return new ShortTailInfo { HasShortTail = false };
    }

    /// <summary>
    /// Ajusta la velocidad del clip restringida entre 25% y 400% recalculando su duración.
    /// </summary>
    public static void SetClipSpeed(Clip clip, double speedRatio)
    {
        double clampedSpeed = Math.Clamp(speedRatio, 0.25, 4.00);
        double previousSpeed = clip.Speed.Seconds > 0 ? clip.Speed.Seconds : 1.0;

        // Duración equivalente
        double newDuration = clip.Duration.Seconds * (previousSpeed / clampedSpeed);
        var previousDuration = clip.Duration.Seconds;
        clip.Speed = new Rational((long)Math.Round(clampedSpeed * 100), 100);
        clip.Duration = Rational.Time(Math.Max(1.0 / 60.0, newDuration));
        KeyframeAnimation.ScaleForDurationChange(clip, clip.Duration.Seconds / previousDuration);
    }

    public static bool SetLinkedClipSpeed(Sequence sequence, string clipId, double requestedRatio)
    {
        ValidateSpeed(requestedRatio);
        var clips = SpeedTargets(sequence, clipId);
        var reference = clips[0];
        var desired = reference.Duration.Seconds * Math.Max(0.001, reference.Speed.Seconds) / requestedRatio;
        var maxDuration = SpeedLimit(sequence, clips);
        var duration = Math.Min(desired, maxDuration);
        ValidateSpeedDuration(sequence, duration);
        ApplyLinkedSpeed(clips, new Rational((long)Math.Round(requestedRatio * 1000), 1000), Rational.Time(duration));
        return duration < desired - 1.0 / 120;
    }

    public static double FitSpeedToNextClip(Sequence sequence, string clipId)
    {
        var clips = SpeedTargets(sequence, clipId);
        var clip = clips.First(c => c.Id == clipId);
        var next = sequence.Clips.Where(other => other.TrackId == clip.TrackId &&
            other.TimelineStart > clip.TimelineStart && !clips.Any(member => member.Id == other.Id))
            .MinBy(other => other.TimelineStart);
        if (next == null) throw new InvalidOperationException("No hay un siguiente clip en esta pista.");

        var duration = next.TimelineStart - clip.TimelineStart;
        ValidateSpeedDuration(sequence, duration.Seconds);
        if (duration.Seconds > SpeedLimit(sequence, clips) + 1e-9)
            throw new InvalidOperationException("Otro medio ocupa el espacio necesario en una pista vinculada.");
        Rational ratio;
        try { ratio = (clip.Duration * clip.Speed) / duration; }
        catch (OverflowException)
        {
            // Repeated source trims can require an irreducible fraction beyond Int64.
            // Keep the exact endpoint and retain speed to twelve decimal places.
            var speed = clip.Duration.Seconds * clip.Speed.Seconds / duration.Seconds;
            ValidateSpeed(speed);
            ratio = new Rational((long)Math.Round(speed * 1_000_000_000_000L), 1_000_000_000_000L);
        }
        ValidateSpeed(ratio.Seconds);
        ApplyLinkedSpeed(clips, ratio, duration);
        return ratio.Seconds;
    }

    private static Clip[] SpeedTargets(Sequence sequence, string clipId)
    {
        if (!sequence.Clips.Any(clip => clip.Id == clipId))
            throw new InvalidOperationException("Clip no encontrado.");
        var ids = sequence.LinkGroups.FirstOrDefault(g => g.ClipIds.Contains(clipId))?.ClipIds ?? new List<string> { clipId };
        var clips = sequence.Clips.Where(c => ids.Contains(c.Id)).ToArray();
        if (clips.Any(c => sequence.Tracks.Any(t => t.Id == c.TrackId && t.Locked)))
            throw new InvalidOperationException("Desbloquea las pistas vinculadas antes de cambiar la velocidad");
        if (clips.Any(c => Math.Abs(c.Duration.Seconds - clips[0].Duration.Seconds) > 1.0 / 120))
            throw new InvalidOperationException("Los clips vinculados tienen duraciones distintas; sepáralos para ajustar su velocidad");
        return clips;
    }

    private static double SpeedLimit(Sequence sequence, Clip[] clips)
    {
        var ids = clips.Select(c => c.Id).ToHashSet();
        return clips.Select(c => sequence.Clips.Where(other => other.TrackId == c.TrackId &&
            !ids.Contains(other.Id) && other.TimelineStart.Seconds >= c.TimelineStart.Seconds)
            .Select(other => other.TimelineStart.Seconds - c.TimelineStart.Seconds)
            .DefaultIfEmpty(double.PositiveInfinity).Min()).Min();
    }

    private static void ValidateSpeed(double ratio)
    {
        if (!double.IsFinite(ratio) || ratio < 0.25 || ratio > 4)
            throw new InvalidOperationException("La velocidad debe estar entre 25 % y 400 %");
    }

    private static void ValidateSpeedDuration(Sequence sequence, double duration)
    {
        if (duration < 1.0 / Math.Max(1, sequence.Fps.Seconds))
            throw new InvalidOperationException("No hay espacio suficiente para este cambio de velocidad");
    }

    private static void ApplyLinkedSpeed(IEnumerable<Clip> clips, Rational speed, Rational duration)
    {
        foreach (var clip in clips)
        {
            var previousDuration = clip.Duration.Seconds;
            clip.Speed = speed;
            clip.Duration = duration;
            KeyframeAnimation.ScaleForDurationChange(clip, duration.Seconds / previousDuration);
        }
    }

    /// <summary>
    /// Crea automáticamente una nueva pista (Video o Audio) con el orden y nomenclatura secuencial.
    /// </summary>
    public static Track AddTrack(Sequence sequence, string kind)
    {
        string normKind = kind.ToLowerInvariant();
        var existing = sequence.Tracks
            .Where(t => t.Kind.Equals(normKind, StringComparison.OrdinalIgnoreCase) && t.Role != "subtitle")
            .ToList();

        int nextNum = existing.Count + 1;
        int nextOrder = existing.Count > 0 ? existing.Max(t => t.Order) + 1 : 1;
        string prefix = normKind == "video" ? "V" : "A";

        var newTrack = new Track
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"{prefix}{nextNum}",
            Kind = normKind,
            Order = nextOrder,
            Enabled = true,
            Locked = false
        };

        sequence.Tracks.Add(newTrack);
        return newTrack;
    }

    /// <summary>
    /// Información sobre un espacio vacío (gap) entre clips o al inicio de pista.
    /// </summary>
    public class TimelineGapInfo
    {
        public string TrackId { get; set; } = string.Empty;
        public double StartSeconds { get; set; }
        public double EndSeconds { get; set; }
        public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);
    }

    /// <summary>
    /// Detecta todos los espacios vacíos (gaps) en una o todas las pistas desbloqueadas.
    /// </summary>
    public static List<TimelineGapInfo> DetectGaps(Sequence sequence, string? trackId = null)
    {
        var gaps = new List<TimelineGapInfo>();
        var tracks = sequence.Tracks.Where(t => !t.Locked);
        if (!string.IsNullOrEmpty(trackId))
        {
            tracks = tracks.Where(t => string.Equals(t.Id, trackId, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var track in tracks)
        {
            var clips = sequence.Clips
                .Where(c => string.Equals(c.TrackId, track.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.TimelineStart.Seconds)
                .ToList();

            if (clips.Count == 0)
                continue;

            // Gap al inicio si el primer clip no arranca en 0
            if (clips[0].TimelineStart.Seconds > 0.01)
            {
                gaps.Add(new TimelineGapInfo
                {
                    TrackId = track.Id,
                    StartSeconds = 0,
                    EndSeconds = clips[0].TimelineStart.Seconds
                });
            }

            // Gaps entre clips contiguos
            for (int i = 0; i < clips.Count - 1; i++)
            {
                double cEnd = clips[i].EndSeconds;
                double nextStart = clips[i + 1].TimelineStart.Seconds;
                if (nextStart - cEnd > 0.01)
                {
                    gaps.Add(new TimelineGapInfo
                    {
                        TrackId = track.Id,
                        StartSeconds = cEnd,
                        EndSeconds = nextStart
                    });
                }
            }
        }

        return gaps;
    }

    /// <summary>
    /// Elimina el espacio vacío especificado desplazando a la izquierda los clips posteriores (Ripple Delete).
    /// </summary>
    public static bool RippleDeleteGap(Sequence sequence, string trackId, double gapStart, double gapEnd)
    {
        const double tolerance = 0.001;
        // La selección puede haber quedado obsoleta tras editar la secuencia.
        var gap = DetectGaps(sequence, trackId).FirstOrDefault(g =>
            Math.Abs(g.StartSeconds - gapStart) < tolerance &&
            Math.Abs(g.EndSeconds - gapEnd) < tolerance);
        if (gap == null || gap.DurationSeconds <= tolerance) return false;

        return CloseInterval(sequence, new HashSet<string> { trackId }, gap.EndSeconds, gap.DurationSeconds);
    }

    internal static bool CloseInterval(Sequence sequence, IReadOnlySet<string> trackIds, double end,
        double duration, bool followCaptionsAfterEdit = false, IReadOnlySet<string>? ignoredIds = null)
    {
        const double tolerance = 0.0001;
        ignoredIds ??= new HashSet<string>();
        var moving = sequence.Clips.Where(c => !ignoredIds.Contains(c.Id) &&
            (sequence.CloseGapAllTracks || trackIds.Contains(c.TrackId)) &&
            c.TimelineStart.Seconds >= end - tolerance)
            .Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var followed = new HashSet<string>();
        {
            var liveIds = sequence.Clips.Where(clip => !ignoredIds.Contains(clip.Id)).Select(clip => clip.Id).ToHashSet();
            foreach (var cue in sequence.Clips.Where(clip => clip.TranscriptId != null))
            {
                if (TranscriptOperations.AudioSourceId(sequence, cue) is not { } source) continue;
                if (ignoredIds.Contains(source))
                {
                    followed.Add(cue.Id);
                    moving.Remove(cue.Id);
                    continue;
                }
                if (!liveIds.Contains(source)) continue;
                if (followCaptionsAfterEdit)
                {
                    followed.Add(cue.Id);
                    moving.Remove(cue.Id);
                }
                else if (moving.Contains(source)) moving.Add(cue.Id);
                else moving.Remove(cue.Id);
            }
        }
        if (moving.Count == 0) return false;

        // Los vínculos y grupos de edición se desplazan juntos. Si un miembro
        // anterior al hueco impide hacerlo, no se modifica ninguna pista.
        var groups = sequence.LinkGroups.Select(g => g.ClipIds)
            .Concat(sequence.EditGroups.Select(g => g.ClipIds)).ToArray();
        bool expanded;
        do
        {
            expanded = false;
            foreach (var group in groups.Where(g => g.Any(moving.Contains)))
                foreach (var id in group)
                    if (!ignoredIds.Contains(id)) expanded |= moving.Add(id);
        } while (expanded);

        var locked = sequence.Tracks.Where(t => t.Locked).Select(t => t.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var movingClips = sequence.Clips.Where(c => moving.Contains(c.Id)).ToArray();
        if (movingClips.Any(c => locked.Contains(c.TrackId) ||
            c.TimelineStart.Seconds < end - tolerance))
            throw new InvalidOperationException("Un medio vinculado o agrupado impide cerrar este hueco sin alterar contenido anterior");

        var newStarts = movingClips.ToDictionary(c => c.Id,
            c => c.TimelineStart.Seconds - duration,
            StringComparer.OrdinalIgnoreCase);
        foreach (var lane in sequence.Tracks)
        {
            var ordered = sequence.Clips.Where(c => c.TrackId == lane.Id && !followed.Contains(c.Id) && !ignoredIds.Contains(c.Id))
                .OrderBy(c => newStarts.GetValueOrDefault(c.Id, c.TimelineStart.Seconds)).ToArray();
            for (var i = 1; i < ordered.Length; i++)
            {
                var left = ordered[i - 1];
                var right = ordered[i];
                if (!moving.Contains(left.Id) && !moving.Contains(right.Id)) continue;
                if (newStarts.GetValueOrDefault(left.Id, left.TimelineStart.Seconds) + left.Duration.Seconds >
                    newStarts.GetValueOrDefault(right.Id, right.TimelineStart.Seconds) + tolerance)
                    throw new InvalidOperationException("Cerrar este hueco superpondría medios vinculados o agrupados en otra pista");
            }
        }

        foreach (var clip in movingClips)
            clip.TimelineStart = Rational.Time(newStarts[clip.Id]);
        return true;
    }

    private static void RemoveEdgeTransitions(Sequence sequence, string clipId, bool fromStart)
    {
        sequence.VideoTransitions.RemoveAll(t => fromStart ? t.RightClipId == clipId : t.LeftClipId == clipId);
        sequence.AudioTransitions.RemoveAll(t => fromStart
            ? t.Kind == AudioTransitionOperations.FadeIn && t.ClipId == clipId || t.RightClipId == clipId
            : t.Kind != AudioTransitionOperations.FadeIn && t.ClipId == clipId);
    }

    public static void RippleDeleteClip(Sequence sequence, string clipId)
    {
        var clip = sequence.Clips.FirstOrDefault(c => c.Id == clipId);
        if (clip == null) return;
        var linked = sequence.LinkGroups.FirstOrDefault(g => g.ClipIds.Contains(clipId));
        var deleteIds = (linked?.ClipIds ?? new List<string> { clipId }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var start = clip.TimelineStart.Seconds;
        var end = clip.EndSeconds;
        if (sequence.Clips.Any(c => deleteIds.Contains(c.Id) &&
            sequence.Tracks.Any(track => track.Id == c.TrackId && track.Locked)))
            throw new InvalidOperationException("Desbloquea las pistas vinculadas antes de eliminar el clip.");
        CloseInterval(sequence, sequence.Tracks.Select(track => track.Id).ToHashSet(),
            end, end - start, followCaptionsAfterEdit: true, ignoredIds: deleteIds);
        sequence.Clips.RemoveAll(c => deleteIds.Contains(c.Id));
        foreach (var id in deleteIds)
        {
            RemoveEdgeTransitions(sequence, id, fromStart: true);
            RemoveEdgeTransitions(sequence, id, fromStart: false);
        }
        foreach (var group in sequence.LinkGroups) group.ClipIds.RemoveAll(deleteIds.Contains);
        sequence.LinkGroups.RemoveAll(g => g.ClipIds.Count < 2);
        foreach (var group in sequence.EditGroups) group.ClipIds.RemoveAll(deleteIds.Contains);
        sequence.EditGroups.RemoveAll(g => g.ClipIds.Count < 2);
    }

    public static bool RippleTrimClip(Sequence sequence, string clipId, bool fromStart, double playhead)
    {
        var anchor = sequence.Clips.FirstOrDefault(c => c.Id == clipId);
        var minimum = 1 / Math.Max(1, sequence.Fps.Seconds);
        if (anchor == null || !double.IsFinite(playhead) ||
            playhead < anchor.TimelineStart.Seconds + minimum - 0.000001 ||
            playhead > anchor.EndSeconds - minimum + 0.000001) return false;
        var group = sequence.LinkGroups.FirstOrDefault(g => g.ClipIds.Contains(clipId));
        var ids = (group?.ClipIds ?? new List<string> { clipId }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var related = sequence.Clips.Where(c => ids.Contains(c.Id)).ToArray();
        var delta = fromStart ? playhead - anchor.TimelineStart.Seconds : playhead - anchor.EndSeconds;
        if (related.Any(c => sequence.Tracks.Any(t => t.Id == c.TrackId && t.Locked) ||
            c.Duration.Seconds + (fromStart ? -delta : delta) < minimum - 0.000001))
            throw new InvalidOperationException("Los clips vinculados están bloqueados o son demasiado cortos para este recorte");
        if (!fromStart)
            CloseInterval(sequence, related.Select(c => c.TrackId).ToHashSet(),
                anchor.EndSeconds, -delta, followCaptionsAfterEdit: true, ignoredIds: ids);
        foreach (var clip in related)
        {
            RemoveEdgeTransitions(sequence, clip.Id, fromStart);
            if (fromStart)
            {
                clip.TimelineStart = Rational.Time(clip.TimelineStart.Seconds + delta);
                clip.SourceIn = Rational.Time(clip.SourceIn.Seconds + delta * clip.Speed.Seconds);
                KeyframeAnimation.ShiftForLeftTrim(clip, delta);
            }
            clip.Duration = Rational.Time(clip.Duration.Seconds + (fromStart ? -delta : delta));
        }
        return true;
    }
}
