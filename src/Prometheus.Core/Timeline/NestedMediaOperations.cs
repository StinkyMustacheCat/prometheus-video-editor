using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Prometheus.Core.Model;
using Prometheus.Core.Primitives;

namespace Prometheus.Core.Timeline;

public static class NestedMediaOperations
{
    private const double Epsilon = 1.0 / 6000;
    private const int MaxDepth = 16;

    public static bool IsNested(Clip? clip) => clip?.NestedContent != null;

    public static Clip? Companion(Sequence sequence, Clip clip) => sequence.LinkGroups
        .Where(g => g.ClipIds.Contains(clip.Id))
        .SelectMany(g => g.ClipIds)
        .Where(id => id != clip.Id)
        .Select(id => sequence.Clips.FirstOrDefault(c => c.Id == id))
        .FirstOrDefault(c => c?.AssetId == clip.AssetId && IsNested(c));

    public static string Create(Project project, IEnumerable<string> clipIds, string name = "Medio anidado")
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var ids = EditGroupOperations.Expand(sequence, clipIds).ToHashSet(StringComparer.Ordinal);
        var children = sequence.Clips.Where(c => ids.Contains(c.Id)).ToList();
        if (children.Count == 0) throw new InvalidOperationException("Selecciona medios para anidar.");
        var tracks = children.Select(c => sequence.Tracks.FirstOrDefault(t => t.Id == c.TrackId)
            ?? throw new InvalidOperationException("Un medio seleccionado perdió su pista.")).DistinctBy(t => t.Id).ToList();
        if (tracks.Any(t => t.Locked)) throw new InvalidOperationException("Desbloquea las pistas seleccionadas.");
        if (!tracks.Any(t => t.Kind == "video")) throw new InvalidOperationException("El medio anidado necesita un componente visual.");

        var start = children.Min(c => c.TimelineStart.Seconds);
        var end = children.Max(c => c.EndSeconds);
        var selectedLinks = sequence.LinkGroups.Where(g => g.ClipIds.All(ids.Contains)).Select(Copy).ToList();
        var selectedGroups = sequence.EditGroups.Where(g => g.ClipIds.All(ids.Contains)).Select(Copy).ToList();
        var content = new NestedContent
        {
            Tracks = tracks.Select(Copy).ToList(),
            Clips = children.Select(c => { var copy = Copy(c); copy.TimelineStart = Rational.Time(c.TimelineStart.Seconds - start); return copy; }).ToList(),
            LinkGroups = selectedLinks,
            EditGroups = selectedGroups
        };
        var childMap = children.ToDictionary(c => c.Id, c => c.Id);
        AudioTransitionOperations.CopyBetween(sequence.AudioTransitions, content.AudioTransitions, childMap);
        var title = string.IsNullOrWhiteSpace(name) ? "Medio anidado" : name.Trim();
        var assetId = Guid.NewGuid().ToString("N");
        var videoStream = new StreamInfo { Id = Guid.NewGuid().ToString("N"), Kind = "video", Codec = "nested", Duration = Rational.Time(end - start), Width = sequence.Width, Height = sequence.Height };
        var asset = new Asset
        {
            Id = assetId, Name = title, Source = new MediaSource { Kind = "generated", Uri = "nested:" + assetId },
            ContainerFormat = "nested", RecommendedDuration = Rational.Time(end - start), Streams = new List<StreamInfo> { videoStream }
        };

        sequence.Clips.RemoveAll(c => ids.Contains(c.Id));
        sequence.LinkGroups.RemoveAll(g => g.ClipIds.Any(ids.Contains));
        sequence.EditGroups.RemoveAll(g => g.ClipIds.Any(ids.Contains));
        var videoTrack = FreeTrack(sequence, "video", start, end);
        var video = new Clip { Name = title, AssetId = assetId, StreamId = videoStream.Id, TrackId = videoTrack.Id,
            TimelineStart = Rational.Time(start), Duration = Rational.Time(end - start), NestedContent = content, NestedRole = "video" };
        sequence.Clips.Add(video);

        if (tracks.Any(t => t.Kind == "audio"))
        {
            var audioStream = new StreamInfo { Id = Guid.NewGuid().ToString("N"), Ordinal = 1, Kind = "audio", Codec = "nested", Duration = Rational.Time(end - start), SampleRate = sequence.MixSampleRate, Channels = sequence.MixChannels };
            asset.Streams.Add(audioStream);
            var audioTrack = FreeTrack(sequence, "audio", start, end);
            var audio = new Clip { Name = title + " · audio", AssetId = assetId, StreamId = audioStream.Id, TrackId = audioTrack.Id,
                TimelineStart = Rational.Time(start), Duration = Rational.Time(end - start), NestedContent = Copy(content), NestedRole = "audio" };
            sequence.Clips.Add(audio);
            sequence.LinkGroups.Add(new LinkGroup { ClipIds = new List<string> { video.Id, audio.Id } });
        }
        project.Assets.Add(asset);
        Validate(project, video);
        return video.Id;
    }

    public static void Validate(Project project, Clip outer, int depth = 0, HashSet<string>? ancestors = null)
    {
        if (outer.NestedContent == null) return;
        if (depth >= MaxDepth) throw new InvalidOperationException("Demasiados niveles de medios anidados.");
        ancestors ??= new HashSet<string>(StringComparer.Ordinal);
        if (!ancestors.Add(outer.AssetId)) throw new InvalidOperationException("Referencia circular en medios anidados.");
        var content = outer.NestedContent;
        if (content.Clips.Count == 0 || !content.Tracks.Any(t => t.Kind == "video" && content.Clips.Any(c => c.TrackId == t.Id)))
            throw new InvalidOperationException("El medio anidado necesita al menos un componente visual.");
        if (content.Tracks.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != content.Tracks.Count ||
            content.Clips.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != content.Clips.Count)
            throw new InvalidOperationException("Identificadores internos duplicados.");
        var childIds = content.Clips.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        if (content.LinkGroups.Any(g => g.ClipIds.Count < 2 || g.ClipIds.Any(id => !childIds.Contains(id))) ||
            content.EditGroups.Any(g => g.ClipIds.Count < 2 || g.ClipIds.Any(id => !childIds.Contains(id))))
            throw new InvalidOperationException("Vínculo o grupo interno inválido.");
        foreach (var child in content.Clips)
        {
            var track = content.Tracks.FirstOrDefault(t => t.Id == child.TrackId);
            var asset = project.Assets.FirstOrDefault(a => a.Id == child.AssetId);
            var stream = asset?.Streams.FirstOrDefault(s => s.Id == child.StreamId);
            if (track == null || stream == null || child.AssetId == outer.AssetId || (stream.Kind == "audio") != (track.Kind == "audio") ||
                child.TimelineStart.Seconds < 0 || child.Duration.Seconds < 1.0 / 60 - Epsilon || child.SourceIn.Seconds < 0 || child.Speed.Seconds <= 0)
                throw new InvalidOperationException("Componente interno inválido.");
            if (child.NestedContent != null) Validate(project, child, depth + 1, ancestors);
        }
        ancestors.Remove(outer.AssetId);
    }

    public static void UpdateChild(Project project, string outerId, string childId, double start, double duration, string trackId)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var outer = sequence.Clips.FirstOrDefault(c => c.Id == outerId) ?? throw new InvalidOperationException("Medio anidado ausente.");
        var owner = outer.NestedRole == "audio" ? Companion(sequence, outer) ?? outer : outer;
        if (!UpdateIn(project, owner.NestedContent ?? throw new InvalidOperationException("No es un medio anidado."), childId, start, duration, trackId, 0))
            throw new InvalidOperationException("Componente interno ausente.");
        var pair = Companion(sequence, owner);
        FitContentBounds(owner, sequence.Clips, pair?.Id, pair == null ? owner.NestedRole : null);
        var asset = project.Assets.First(a => a.Id == owner.AssetId);
        asset.RecommendedDuration = Rational.Time(owner.NestedContent!.Clips.Max(c => c.EndSeconds));
        foreach (var stream in asset.Streams) stream.Duration = asset.RecommendedDuration.Value;
        SyncCompanion(sequence, owner);
        Validate(project, owner);
        PruneUnusedNestedAssets(project);
    }

    private static bool UpdateIn(Project project, NestedContent content, string childId, double start, double duration, string trackId, int depth)
    {
        if (depth >= MaxDepth) throw new InvalidOperationException("Demasiados niveles de medios anidados.");
        var child = content.Clips.FirstOrDefault(c => c.Id == childId);
        if (child == null)
        {
            foreach (var nested in content.Clips.Where(IsNested))
            {
                if (!UpdateIn(project, nested.NestedContent!, childId, start, duration, trackId, depth + 1)) continue;
                FitContentBounds(nested, content.Clips);
                var asset = project.Assets.First(a => a.Id == nested.AssetId);
                asset.RecommendedDuration = Rational.Time(nested.NestedContent!.Clips.Max(c => c.EndSeconds));
                foreach (var nestedStream in asset.Streams) nestedStream.Duration = asset.RecommendedDuration.Value;
                SyncCompanion(content, nested);
                return true;
            }
            return false;
        }
        var oldTrack = content.Tracks.First(t => t.Id == child.TrackId);
        var target = content.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (target == null || target.Kind != oldTrack.Kind || target.Locked)
            throw new InvalidOperationException("La minipista de destino no admite el componente.");
        if (start < 0 || duration < 1.0 / 60 - Epsilon || start + duration > 48 * 3600)
            throw new InvalidOperationException("Rango interno inválido.");
        var leftTrim = Math.Abs(start + duration - child.EndSeconds) < Epsilon && Math.Abs(start - child.TimelineStart.Seconds) > Epsilon;
        var sourceIn = child.SourceIn.Seconds + (leftTrim ? (start - child.TimelineStart.Seconds) * child.Speed.Seconds : 0);
        var stream = project.Assets.First(a => a.Id == child.AssetId).Streams.First(s => s.Id == child.StreamId);
        if (stream.Kind == "image") sourceIn = 0;
        if (sourceIn < -Epsilon || stream.Kind != "image" && stream.Duration.Seconds > Epsilon &&
            sourceIn + duration * child.Speed.Seconds > stream.Duration.Seconds + Epsilon)
            throw new InvalidOperationException("El recorte excede la fuente.");
        if (content.Clips.Any(c => c.Id != child.Id && c.TrackId == trackId && c.TimelineStart.Seconds < start + duration - Epsilon && c.EndSeconds > start + Epsilon))
            throw new InvalidOperationException("El componente chocaría con otro medio en la minipista.");
        var previousStart = child.TimelineStart.Seconds;
        child.TrackId = trackId;
        child.TimelineStart = Rational.Time(start);
        if (leftTrim) KeyframeAnimation.ShiftForLeftTrim(child, start - previousStart);
        child.Duration = Rational.Time(duration);
        child.SourceIn = Rational.Time(Math.Max(0, sourceIn));
        return true;
    }

    public static void RemoveChild(Project project, string outerId, string childId)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var outer = sequence.Clips.FirstOrDefault(c => c.Id == outerId) ?? throw new InvalidOperationException("Medio anidado ausente.");
        var owner = outer.NestedRole == "audio" ? Companion(sequence, outer) ?? outer : outer;
        if (!RemoveIn(owner.NestedContent ?? throw new InvalidOperationException("No es un medio anidado."), childId, 0))
            throw new InvalidOperationException("Componente interno ausente.");
        if (owner.NestedRole == "audio" && Companion(sequence, owner) == null &&
            !owner.NestedContent!.Clips.Any(c => owner.NestedContent.Tracks.Any(t => t.Id == c.TrackId && t.Kind == "audio")))
        {
            sequence.Clips.Remove(owner);
            sequence.LinkGroups.RemoveAll(g => g.ClipIds.Contains(owner.Id));
            sequence.EditGroups.RemoveAll(g => g.ClipIds.Contains(owner.Id));
            PruneUnusedNestedAssets(project);
            return;
        }
        var pair = Companion(sequence, owner);
        FitContentBounds(owner, sequence.Clips, pair?.Id, pair == null ? owner.NestedRole : null);
        var asset = project.Assets.First(a => a.Id == owner.AssetId);
        asset.RecommendedDuration = Rational.Time(owner.NestedContent!.Clips.Max(c => c.EndSeconds));
        foreach (var stream in asset.Streams) stream.Duration = asset.RecommendedDuration.Value;
        SyncCompanion(sequence, owner);
        Validate(project, owner);
        PruneUnusedNestedAssets(project);
    }

    public static (Clip Child, Track Track)? FindChild(Project project, string outerId, string childId)
    {
        var sequence = project.ActiveSequence;
        var outer = sequence?.Clips.FirstOrDefault(c => c.Id == outerId);
        var visual = outer?.NestedRole == "audio" ? Companion(sequence!, outer) ?? outer : outer;
        return visual?.NestedContent == null ? null : FindIn(visual.NestedContent, childId, 0);
    }

    private static (Clip Child, Track Track)? FindIn(NestedContent content, string childId, int depth)
    {
        if (depth >= MaxDepth) return null;
        var child = content.Clips.FirstOrDefault(c => c.Id == childId);
        if (child != null) return (child, content.Tracks.First(t => t.Id == child.TrackId));
        foreach (var nested in content.Clips.Where(IsNested))
        {
            var found = FindIn(nested.NestedContent!, childId, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    public static void SetChildProperties(Project project, string outerId, string childId,
        string name, bool enabled, double gainDb, double balance)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var outer = sequence.Clips.FirstOrDefault(c => c.Id == outerId) ?? throw new InvalidOperationException("Medio anidado ausente.");
        var visual = outer.NestedRole == "audio" ? Companion(sequence, outer) ?? outer : outer;
        if (visual.NestedContent == null || gainDb < -60 || gainDb > 12 || balance < -1 || balance > 1)
            throw new InvalidOperationException("Propiedades internas inválidas.");
        if (!SetPropertiesIn(visual.NestedContent, childId, name, enabled, gainDb, balance, 0))
            throw new InvalidOperationException("Componente interno ausente.");
        SyncCompanion(sequence, visual);
    }

    private static bool SetPropertiesIn(NestedContent content, string childId, string name,
        bool enabled, double gainDb, double balance, int depth)
    {
        if (depth >= MaxDepth) throw new InvalidOperationException("Demasiados niveles de medios anidados.");
        var child = content.Clips.FirstOrDefault(c => c.Id == childId);
        if (child != null)
        {
            child.Name = string.IsNullOrWhiteSpace(name) ? child.Name : name.Trim();
            child.Enabled = enabled;
            child.GainDb = gainDb;
            child.Balance = balance;
            return true;
        }
        foreach (var nested in content.Clips.Where(IsNested))
        {
            if (!SetPropertiesIn(nested.NestedContent!, childId, name, enabled, gainDb, balance, depth + 1)) continue;
            SyncCompanion(content, nested);
            return true;
        }
        return false;
    }

    private static bool RemoveIn(NestedContent content, string childId, int depth)
    {
        if (depth >= MaxDepth) throw new InvalidOperationException("Demasiados niveles de medios anidados.");
        var child = content.Clips.FirstOrDefault(c => c.Id == childId);
        if (child == null)
        {
            foreach (var nested in content.Clips.Where(IsNested))
            {
                if (!RemoveIn(nested.NestedContent!, childId, depth + 1)) continue;
                FitContentBounds(nested, content.Clips);
                SyncCompanion(content, nested);
                return true;
            }
            return false;
        }
        var ids = new HashSet<string> { child.Id };
        if (child.NestedContent != null)
        {
            var pair = content.LinkGroups.Where(g => g.ClipIds.Contains(child.Id)).SelectMany(g => g.ClipIds)
                .Select(id => content.Clips.FirstOrDefault(c => c.Id == id))
                .FirstOrDefault(c => c?.AssetId == child.AssetId && c.Id != child.Id && c.NestedContent != null);
            if (pair != null) ids.Add(pair.Id);
        }
        if (!content.Clips.Any(c => !ids.Contains(c.Id) && content.Tracks.Any(t => t.Id == c.TrackId && t.Kind == "video")))
            throw new InvalidOperationException("El medio anidado necesita un componente visual.");
        content.Clips.RemoveAll(c => ids.Contains(c.Id));
        content.LinkGroups.RemoveAll(g => g.ClipIds.Any(ids.Contains));
        content.EditGroups.RemoveAll(g => g.ClipIds.Any(ids.Contains));
        return true;
    }

    public static IReadOnlyList<string> Unnest(Project project, string clipId)
    {
        var sequence = project.ActiveSequence ?? throw new InvalidOperationException("No hay montaje activo.");
        var clip = sequence.Clips.FirstOrDefault(c => c.Id == clipId) ?? throw new InvalidOperationException("Medio anidado ausente.");
        var visual = clip.NestedRole == "audio" ? Companion(sequence, clip) ?? clip : clip;
        var content = visual.NestedContent ?? throw new InvalidOperationException("No es un medio anidado.");
        var audio = Companion(sequence, visual);
        var roleFilter = audio == null ? clip.NestedRole : null;
        var outerIds = new HashSet<string> { visual.Id };
        if (audio != null) outerIds.Add(audio.Id);
        if (sequence.Tracks.Any(t => outerIds.Any(id => sequence.Clips.Any(c => c.Id == id && c.TrackId == t.Id)) && t.Locked))
            throw new InvalidOperationException("Desbloquea las pistas del contenedor.");
        if (sequence.EditGroups.Any(g => g.ClipIds.Any(outerIds.Contains)))
            throw new InvalidOperationException("Desagrupa el medio anidado antes de desanidarlo.");
        var visible = content.Clips.Where(child => roleFilter == null || content.Tracks.Any(t => t.Id == child.TrackId && t.Kind == roleFilter))
            .Select<Clip, (string OriginalId, string OriginalTrackId, Clip Clip)?>(child =>
        {
            var sourceStart = (child.TimelineStart.Seconds - visual.SourceIn.Seconds) / visual.Speed.Seconds;
            var sourceEnd = (child.EndSeconds - visual.SourceIn.Seconds) / visual.Speed.Seconds;
            var from = Math.Max(0, sourceStart);
            var to = Math.Min(visual.Duration.Seconds, sourceEnd);
            if (to - from < 1 / Math.Max(1, sequence.Fps.Seconds) - Epsilon) return null;
            var copy = CollisionEngine.CopyClip(child);
            var trimmed = visual.SourceIn.Seconds + from * visual.Speed.Seconds - child.TimelineStart.Seconds;
            KeyframeAnimation.ShiftForLeftTrim(copy, trimmed);
            KeyframeAnimation.ScaleForDurationChange(copy, 1 / visual.Speed.Seconds);
            copy.TimelineStart = Rational.Time(visual.TimelineStart.Seconds + from);
            copy.Duration = Rational.Time(to - from);
            copy.SourceIn = Rational.Time(child.SourceIn.Seconds + (visual.SourceIn.Seconds + from * visual.Speed.Seconds - child.TimelineStart.Seconds) * child.Speed.Seconds);
            copy.Speed = ComposeSpeed(child.Speed, visual.Speed, copy.Duration);
            return (OriginalId: child.Id, OriginalTrackId: child.TrackId, Clip: copy);
        }).Where(item => item != null).Select(item => item!.Value).ToList();

        sequence.Clips.RemoveAll(c => outerIds.Contains(c.Id));
        sequence.LinkGroups.RemoveAll(g => g.ClipIds.Any(outerIds.Contains));

        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        var lastOrder = new Dictionary<string, int> { ["audio"] = -1, ["video"] = -1 };
        foreach (var sourceTrack in content.Tracks.OrderBy(t => t.Kind).ThenBy(t => t.Order))
        {
            var members = visible.Where(x => x.OriginalTrackId == sourceTrack.Id).Select(x => x.Clip).ToList();
            if (members.Count == 0) continue;
            var sourceNeutral = sourceTrack.Enabled && !sourceTrack.Locked && sourceTrack.Effects.Count == 0 &&
                Math.Abs(sourceTrack.GainDb) < Epsilon && Math.Abs(sourceTrack.Balance) < Epsilon;
            var available = sequence.Tracks.Where(t => sourceNeutral && t.Kind == sourceTrack.Kind && t.Order > lastOrder[sourceTrack.Kind] && t.Enabled && !t.Locked &&
                t.Effects.Count == 0 && Math.Abs(t.GainDb) < Epsilon && Math.Abs(t.Balance) < Epsilon)
                .OrderBy(t => t.Id == sourceTrack.Id ? 0 : 1).ThenBy(t => t.Order)
                .FirstOrDefault(t => members.All(child => !sequence.Clips.Any(other => other.TrackId == t.Id && other.TimelineStart.Seconds < child.EndSeconds - Epsilon && other.EndSeconds > child.TimelineStart.Seconds + Epsilon)));
            var target = available ?? Copy(sourceTrack);
            if (available == null)
            {
                target.Id = Guid.NewGuid().ToString("N");
                foreach (var effect in target.Effects) effect.Id = Guid.NewGuid().ToString("N");
                target.Order = Math.Max(lastOrder[target.Kind], sequence.Tracks.Where(t => t.Kind == target.Kind).Select(t => t.Order).DefaultIfEmpty(-1).Max()) + 1;
                target.Name = target.Kind == "video" ? "V" + (target.Order + 1) : "A" + (target.Order + 1);
                target.Locked = false;
                sequence.Tracks.Add(target);
            }
            mapping[sourceTrack.Id] = target.Id;
            lastOrder[sourceTrack.Kind] = target.Order;
            foreach (var child in members) child.TrackId = target.Id;
            sequence.Clips.AddRange(members);
        }
        var childIds = visible.ToDictionary(x => x.OriginalId, x => x.Clip.Id);
        var audioContent = audio?.NestedContent ?? content;
        bool KeepsEdge(string id, bool fromStart)
        {
            var original = audioContent.Clips.FirstOrDefault(child => child.Id == id);
            var revealed = visible.FirstOrDefault(child => child.OriginalId == id).Clip;
            if (original == null || revealed == null) return false;
            var originalEdge = original.SourceIn.Seconds +
                (fromStart ? 0 : original.Duration.Seconds * original.Speed.Seconds);
            var revealedEdge = revealed.SourceIn.Seconds +
                (fromStart ? 0 : revealed.Duration.Seconds * revealed.Speed.Seconds);
            return Math.Abs(originalEdge - revealedEdge) < Epsilon;
        }
        var preservedFades = audioContent.AudioTransitions.Where(transition =>
            transition.Kind == AudioTransitionOperations.FadeIn ? KeepsEdge(transition.ClipId, true) :
            transition.Kind == AudioTransitionOperations.FadeOut ? KeepsEdge(transition.ClipId, false) :
            KeepsEdge(transition.ClipId, false) && transition.RightClipId is { } right && KeepsEdge(right, true));
        AudioTransitionOperations.CopyBetween(preservedFades,
            sequence.AudioTransitions, childIds, visual.Speed.Seconds);
        foreach (var group in content.LinkGroups)
        {
            var members = group.ClipIds.Where(childIds.ContainsKey).Select(id => childIds[id]).ToList();
            if (members.Count > 1) sequence.LinkGroups.Add(new LinkGroup { ClipIds = members });
        }
        foreach (var group in content.EditGroups)
        {
            var members = group.ClipIds.Where(childIds.ContainsKey).Select(id => childIds[id]).ToList();
            if (members.Count > 1)
            {
                var used = sequence.EditGroups.Select(g => g.Number).ToHashSet();
                var number = group.Number;
                if (number < 1 || used.Contains(number))
                {
                    number = 1;
                    while (used.Contains(number)) number++;
                }
                sequence.EditGroups.Add(new EditGroup { Number = number, ClipIds = members });
            }
        }
        PruneUnusedNestedAssets(project);
        return visible.Select(x => x.Clip.Id).ToList();
    }

    private static Rational ComposeSpeed(Rational child, Rational parent, Rational duration)
    {
        try { return child * parent; }
        catch (OverflowException)
        {
            // Only the unrepresentable product is approximated. Check its exact
            // accumulated source error against one 48 kHz sample over this clip.
            var numerator = (System.Numerics.BigInteger)child.N * parent.N;
            var denominator = (System.Numerics.BigInteger)child.D * parent.D;
            if (numerator <= 0 || denominator <= 0)
                throw new InvalidOperationException("La velocidad combinada no es válida.");
            long precision = 1_000_000_000_000;
            var rounded = (numerator * precision + denominator / 2) / denominator;
            while (rounded > long.MaxValue && precision > 1)
            {
                precision /= 10;
                rounded = (numerator * precision + denominator / 2) / denominator;
            }
            if (rounded <= 0 || rounded > long.MaxValue)
                throw new InvalidOperationException("La velocidad combinada excede el rango representable.");
            var result = new Rational((long)rounded, precision);
            var error = System.Numerics.BigInteger.Abs(numerator * result.D - result.N * denominator);
            if (error * duration.N * 48000 > denominator * result.D * duration.D)
                throw new InvalidOperationException("No se puede desanidar conservando la precisión de una muestra de audio.");
            return result;
        }
    }

    private static void SyncCompanion(Sequence sequence, Clip visual)
    {
        var companion = Companion(sequence, visual);
        if (companion == null) return;
        if (visual.NestedContent?.Clips.Any(c => visual.NestedContent.Tracks.Any(t => t.Id == c.TrackId && t.Kind == "audio")) != true)
        {
            sequence.Clips.Remove(companion);
            sequence.LinkGroups.RemoveAll(g => g.ClipIds.Contains(companion.Id));
            return;
        }
        companion.NestedContent = Copy(visual.NestedContent!);
        FitContentBounds(companion, sequence.Clips, visual.Id);
    }

    private static void SyncCompanion(NestedContent content, Clip visual)
    {
        var companion = content.LinkGroups.Where(g => g.ClipIds.Contains(visual.Id)).SelectMany(g => g.ClipIds)
            .Where(id => id != visual.Id).Select(id => content.Clips.FirstOrDefault(c => c.Id == id))
            .FirstOrDefault(c => c?.AssetId == visual.AssetId && c.NestedRole == "audio");
        if (companion == null) return;
        if (visual.NestedContent?.Clips.Any(c => visual.NestedContent.Tracks.Any(t => t.Id == c.TrackId && t.Kind == "audio")) != true)
        {
            content.Clips.Remove(companion);
            content.LinkGroups.RemoveAll(g => g.ClipIds.Contains(companion.Id));
        }
        else
        {
            companion.NestedContent = Copy(visual.NestedContent!);
            FitContentBounds(companion, content.Clips, visual.Id);
        }
    }

    private static void FitContentBounds(Clip container, IReadOnlyList<Clip> siblings, string? companionId = null, string? role = null)
    {
        var content = container.NestedContent ?? throw new InvalidOperationException("No es un medio anidado.");
        var children = content.Clips.Where(c => role == null || content.Tracks.Any(t => t.Id == c.TrackId && t.Kind == role)).ToList();
        if (children.Count == 0) return;
        var first = children.Min(c => c.TimelineStart.Seconds);
        var last = children.Max(c => c.EndSeconds);
        var speed = container.Speed.Seconds;
        var newStart = container.TimelineStart.Seconds + (first - container.SourceIn.Seconds) / speed;
        var newDuration = Math.Max(1.0 / 60, (last - first) / speed);
        var newEnd = newStart + newDuration;
        if (newStart < -Epsilon) throw new InvalidOperationException("No hay tiempo antes del medio anidado para mostrar su primer componente.");
        if ((newStart < container.TimelineStart.Seconds - Epsilon || newEnd > container.EndSeconds + Epsilon) &&
            siblings.Any(c => c.Id != container.Id && c.Id != companionId && c.TrackId == container.TrackId &&
                c.TimelineStart.Seconds < newEnd - Epsilon && c.EndSeconds > newStart + Epsilon))
            throw new InvalidOperationException("No hay espacio para ajustar el medio anidado a sus componentes.");
        var oldStart = container.TimelineStart.Seconds;
        container.TimelineStart = Rational.Time(Math.Max(0, newStart));
        KeyframeAnimation.ShiftForLeftTrim(container, container.TimelineStart.Seconds - oldStart);
        container.SourceIn = Rational.Time(first);
        container.Duration = Rational.Time(newDuration);
    }

    private static void PruneUnusedNestedAssets(Project project)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        void Visit(Clip clip)
        {
            used.Add(clip.AssetId);
            if (clip.NestedContent != null) foreach (var child in clip.NestedContent.Clips) Visit(child);
        }
        foreach (var sequence in project.Sequences)
        foreach (var clip in sequence.Clips) Visit(clip);
        project.Assets.RemoveAll(a => a.Source.Uri.StartsWith("nested:", StringComparison.Ordinal) && !used.Contains(a.Id));
    }

    public sealed record AssetOccurrence(string SequenceId, string SequenceName, string RootClipId,
        double RootStartSeconds, bool InsideNested);

    public static IReadOnlyList<AssetOccurrence> FindAssetOccurrences(Project project, string assetId)
    {
        var found = new List<AssetOccurrence>();
        foreach (var sequence in project.Sequences)
        foreach (var root in sequence.Clips)
        {
            if (root.AssetId == assetId)
                found.Add(new AssetOccurrence(sequence.Id, sequence.Name, root.Id,
                    root.TimelineStart.Seconds, false));
            if (root.NestedContent == null || root.NestedRole == "audio" && Companion(sequence, root) != null)
                continue;
            void Visit(NestedContent content)
            {
                foreach (var child in content.Clips)
                {
                    if (child.NestedRole == "audio" && content.LinkGroups.Any(group =>
                        group.ClipIds.Contains(child.Id) && group.ClipIds.Any(id => content.Clips.Any(other =>
                            other.Id == id && other.AssetId == child.AssetId && other.NestedRole == "video")))) continue;
                    if (child.AssetId == assetId)
                        found.Add(new AssetOccurrence(sequence.Id, sequence.Name, root.Id,
                            root.TimelineStart.Seconds, true));
                    if (child.NestedContent != null) Visit(child.NestedContent);
                }
            }
            Visit(root.NestedContent);
        }
        return found;
    }

    public static void RemoveAssetEverywhere(Project project, string assetId)
    {
        static void CleanGroups(List<LinkGroup> links, List<EditGroup> groups, HashSet<string> removed)
        {
            foreach (var group in links) group.ClipIds.RemoveAll(removed.Contains);
            links.RemoveAll(group => group.ClipIds.Count < 2);
            foreach (var group in groups) group.ClipIds.RemoveAll(removed.Contains);
            groups.RemoveAll(group => group.ClipIds.Count < 2);
        }

        bool RemoveFromContent(NestedContent content)
        {
            var removed = new HashSet<string>(StringComparer.Ordinal);
            var changed = false;
            foreach (var child in content.Clips.ToArray())
            {
                if (removed.Contains(child.Id)) continue;
                var companion = content.LinkGroups.Where(group => group.ClipIds.Contains(child.Id))
                    .SelectMany(group => group.ClipIds)
                    .Select(id => content.Clips.FirstOrDefault(other => other.Id == id &&
                        other.Id != child.Id && other.AssetId == child.AssetId && other.NestedContent != null))
                    .FirstOrDefault(other => other != null);
                if (child.AssetId == assetId)
                {
                    var isVideo = content.Tracks.Any(track => track.Id == child.TrackId && track.Kind == "video");
                    var otherVideo = content.Clips.Any(other => other.Id != child.Id &&
                        other.Id != companion?.Id && !removed.Contains(other.Id) &&
                        content.Tracks.Any(track => track.Id == other.TrackId && track.Kind == "video"));
                    if (isVideo && !otherVideo && child.NestedRole != "audio")
                    {
                        var reserve = PlaceholderService.CreateAsset(false, child.Duration.Seconds);
                        project.Assets.Add(reserve);
                        child.AssetId = reserve.Id;
                        child.StreamId = reserve.Streams[0].Id;
                        child.Name = reserve.Name;
                        child.SourceIn = Rational.Zero;
                        child.Speed = Rational.One;
                        child.Visual = new ClipVisualState();
                        child.Effects.Clear();
                        child.KeyframeTracks.Clear();
                        child.NestedContent = null;
                        child.NestedRole = null;
                        changed = true;
                        continue;
                    }
                    removed.Add(child.Id);
                    if (companion != null) removed.Add(companion.Id);
                    changed = true;
                    continue;
                }
                if (child.NestedContent == null || child.NestedRole == "audio" && companion != null) continue;
                if (!RemoveFromContent(child.NestedContent)) continue;
                changed = true;
                var hasVisual = child.NestedContent.Clips.Any(clip => child.NestedContent.Tracks.Any(track =>
                    track.Id == clip.TrackId && track.Kind == "video"));
                if (!hasVisual)
                {
                    removed.Add(child.Id);
                    if (companion != null) removed.Add(companion.Id);
                }
                else
                {
                    FitContentBounds(child, content.Clips, companion?.Id);
                    SyncCompanion(content, child);
                }
            }
            if (removed.Count > 0)
            {
                content.Clips.RemoveAll(clip => removed.Contains(clip.Id));
                CleanGroups(content.LinkGroups, content.EditGroups, removed);
            }
            return changed;
        }

        foreach (var sequence in project.Sequences)
        {
            var removed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in sequence.Clips.ToArray())
            {
                if (removed.Contains(root.Id)) continue;
                var companion = Companion(sequence, root);
                if (root.AssetId == assetId)
                {
                    removed.Add(root.Id);
                    if (companion != null) removed.Add(companion.Id);
                    continue;
                }
                if (root.NestedContent == null || root.NestedRole == "audio" && companion != null) continue;
                if (!RemoveFromContent(root.NestedContent)) continue;
                var kind = root.NestedRole == "audio" ? "audio" : "video";
                if (!root.NestedContent.Clips.Any(clip => root.NestedContent.Tracks.Any(track =>
                    track.Id == clip.TrackId && track.Kind == kind)))
                {
                    removed.Add(root.Id);
                    if (companion != null) removed.Add(companion.Id);
                }
                else
                {
                    FitContentBounds(root, sequence.Clips, companion?.Id, companion == null ? kind : null);
                    SyncCompanion(sequence, root);
                }
            }
            sequence.Clips.RemoveAll(clip => removed.Contains(clip.Id));
            CleanGroups(sequence.LinkGroups, sequence.EditGroups, removed);
        }
        project.Assets.RemoveAll(asset => asset.Id == assetId);
        PruneUnusedNestedAssets(project);
    }

    private static Track FreeTrack(Sequence sequence, string kind, double start, double end)
    {
        var available = sequence.Tracks.Where(t => t.Kind == kind && t.Role != "subtitle" && !t.Locked && t.Enabled)
            .OrderBy(t => t.Order)
            .FirstOrDefault(t => !sequence.Clips.Any(c => c.TrackId == t.Id && c.TimelineStart.Seconds < end - Epsilon && c.EndSeconds > start + Epsilon));
        if (available != null) return available;
        // Order 0 is V1 (lowest video layer) and A1 (highest audio row).
        foreach (var existing in sequence.Tracks.Where(t => t.Kind == kind && t.Role != "subtitle"))
        {
            var prefix = kind == "video" ? "V" : "A";
            if (existing.Name == prefix + (existing.Order + 1)) existing.Name = prefix + (existing.Order + 2);
            existing.Order++;
        }
        var track = new Track { Kind = kind, Order = 0, Name = kind == "video" ? "V1" : "A1" };
        sequence.Tracks.Add(track);
        return track;
    }

    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
