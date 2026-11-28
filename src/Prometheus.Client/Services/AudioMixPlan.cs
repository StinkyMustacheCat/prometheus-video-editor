using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Prometheus.Core.Model;
using Prometheus.Core.Timeline;

namespace Prometheus.Client.Services;

public sealed record AudioMixInput(string Path, int StreamOrdinal, double SourceStart, double SourceDuration,
    double TimelineDelay, double Speed, double GainDb, double Balance, string RootTrackId, string EffectChain,
    string? ParentGroupId = null, AudioMixEnvelope Envelope = default);

public sealed record AudioMixGroup(string Id, string? ParentId, string EffectChain,
    double VisibleEndSeconds, int Depth, AudioMixEnvelope Envelope = default);

public readonly record struct AudioMixRamp(double Start, double End)
{
    public double ProgressAt(double time) => Math.Clamp((time - Start) / (End - Start), 0, 1);
}

public readonly record struct AudioMixEnvelope(AudioMixRamp? FadeIn, AudioMixRamp? FadeOut)
{
    public double GainAt(double time) => (FadeIn?.ProgressAt(time) ?? 1) * (1 - (FadeOut?.ProgressAt(time) ?? 0));
}

public sealed class AudioMixPlan
{
    private const int SampleRate = 48000;

    private readonly record struct ClipFades(AudioTransitionSpan? FadeIn, AudioTransitionSpan? FadeOut)
    {
        public AudioMixEnvelope At(double bias, double rate)
        {
            AudioMixRamp? OnTimeline(AudioTransitionSpan? fade) => fade == null ? null :
                new AudioMixRamp((fade.Start - bias) / rate, (fade.End - bias) / rate);
            return new(OnTimeline(FadeIn), OnTimeline(FadeOut));
        }
    }

    public double StartSeconds { get; }
    public double DurationSeconds { get; }
    public IReadOnlyList<AudioMixInput> Inputs { get; }
    public IReadOnlyList<AudioMixGroup> Groups { get; }
    public double MasterGainDb { get; }

    private AudioMixPlan(double start, double duration, double masterGainDb,
        List<AudioMixInput> inputs, List<AudioMixGroup> groups)
    {
        StartSeconds = start;
        DurationSeconds = duration;
        MasterGainDb = masterGainDb;
        Inputs = inputs;
        Groups = groups;
    }

    public static AudioMixPlan Create(Project project, double start) => Create(project, start, null, null);

    internal static AudioMixPlan Create(Project project, double start, double? end,
        Func<Asset?, StreamInfo?, Clip, double, double, (string? Path, int Ordinal)>? resolveInput)
    {
        var seq = project.ActiveSequence;
        if (seq == null) return new AudioMixPlan(start, 0, 0, new List<AudioMixInput>(), new List<AudioMixGroup>());
        var duration = Math.Max(0, (end ?? seq.DurationSeconds) - start);
        var inputs = new List<AudioMixInput>();
        var groups = new List<AudioMixGroup>();
        var solo = seq.Tracks.Any(t => t.Kind == "audio" && t.Enabled && t.Solo);
        var fades = ResolveFades(seq.Clips, seq.AudioTransitions);
        foreach (var track in seq.Tracks.Where(t => t.Kind == "audio" && t.Enabled && (!solo || t.Solo)))
        {
            var chain = AudioEffectFilters.Build(track.Effects);
            var trackGroup = chain.Length == 0 ? null : "track:" + track.Id;
            var groupIndex = groups.Count;
            if (trackGroup != null)
                groups.Add(new AudioMixGroup(trackGroup, null, chain,
                    end ?? seq.DurationSeconds, -1));
            var inputCount = inputs.Count;
            foreach (var clip in seq.Clips.Where(c => c.Enabled && c.TrackId == track.Id && c.Speed.Seconds > .001))
                Collect(project, clip, track, track.Id, start, 0, 1, 0,
                    end ?? seq.DurationSeconds, 0, 0, 0, inputs, groups, trackGroup,
                    fades?.GetValueOrDefault(clip.Id) ?? default, resolveInput);
            if (inputCount == inputs.Count) groups.RemoveRange(groupIndex, groups.Count - groupIndex);
        }
        return new AudioMixPlan(start, duration, seq.MasterGainDb, inputs, groups);
    }

    private static void Collect(Project project, Clip clip, Track track, string rootTrackId, double start,
        double parentBias, double parentRate, double visibleStart, double visibleEnd,
        double parentGain, double parentBalance, int depth, List<AudioMixInput> inputs,
        List<AudioMixGroup> groups, string? parentGroupId, ClipFades fades,
        Func<Asset?, StreamInfo?, Clip, double, double, (string? Path, int Ordinal)>? resolveInput)
    {
        if (depth >= 16 || !clip.Enabled || !track.Enabled || parentRate <= 0 || clip.Speed.Seconds <= 0) return;
        var localStart = fades.FadeIn is { Right: not null } incoming ? incoming.Start : clip.TimelineStart.Seconds;
        var localEnd = fades.FadeOut is { Right: not null } outgoing ? outgoing.End : clip.EndSeconds;
        var from = Math.Max(visibleStart, (localStart - parentBias) / parentRate);
        var to = Math.Min(visibleEnd, (localEnd - parentBias) / parentRate);
        if (to <= Math.Max(from, start)) return;
        var envelope = fades.At(parentBias, parentRate);
        var bias = clip.SourceIn.Seconds + (parentBias - clip.TimelineStart.Seconds) * clip.Speed.Seconds;
        var rate = parentRate * clip.Speed.Seconds;
        var gain = parentGain + track.GainDb + clip.GainDb;
        var balance = Math.Clamp(parentBalance + track.Balance + clip.Balance, -1, 1);
        if (clip.NestedContent is { } content)
        {
            if (clip.NestedRole != "audio") return;
            var groupIndex = groups.Count;
            var groupId = parentGroupId == null ? clip.Id : parentGroupId + "/" + clip.Id;
            groups.Add(new AudioMixGroup(groupId, parentGroupId,
                AudioEffectFilters.Build(clip.Effects), to, depth * 2, envelope));
            var inputCount = inputs.Count;
            var internalSolo = content.Tracks.Any(t => t.Kind == "audio" && t.Enabled && t.Solo);
            var childFades = ResolveFades(content.Clips, content.AudioTransitions);
            foreach (var lane in content.Tracks.Where(t => t.Kind == "audio" && t.Enabled && (!internalSolo || t.Solo)))
            {
                var chain = AudioEffectFilters.Build(lane.Effects);
                var laneGroup = chain.Length == 0 ? groupId : groupId + "/track:" + lane.Id;
                var laneGroupIndex = groups.Count;
                if (laneGroup != groupId)
                    groups.Add(new AudioMixGroup(laneGroup, groupId, chain, to, depth * 2 + 1));
                var laneInputs = inputs.Count;
                foreach (var child in content.Clips.Where(c => c.TrackId == lane.Id))
                    Collect(project, child, lane, rootTrackId, start, bias, rate, from, to, gain, balance,
                        depth + 1, inputs, groups, laneGroup, childFades?.GetValueOrDefault(child.Id) ?? default, resolveInput);
                if (inputs.Count == laneInputs) groups.RemoveRange(laneGroupIndex, groups.Count - laneGroupIndex);
            }
            if (inputs.Count == inputCount) groups.RemoveRange(groupIndex, groups.Count - groupIndex);
            return;
        }
        var asset = project.Assets.FirstOrDefault(a => a.Id == clip.AssetId);
        var stream = asset?.Streams.FirstOrDefault(s => s.Id == clip.StreamId && s.Kind == "audio");
        var trimAt = Math.Max(start, from);
        var (path, ordinal) = resolveInput != null ? resolveInput(asset, stream, clip, trimAt, to) :
            (MgtProjectAudio.Resolve(asset) ?? asset?.EffectiveLocator?.Path, stream?.Ordinal ?? 0);
        if (stream == null || string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        inputs.Add(new AudioMixInput(path, ordinal,
            Math.Max(0, bias + trimAt * rate), (to - trimAt) * rate,
            Math.Max(0, trimAt - start), rate, gain, balance, rootTrackId,
            AudioEffectFilters.Build(clip.Effects), parentGroupId, envelope));
    }

    private static Dictionary<string, ClipFades>? ResolveFades(IReadOnlyList<Clip> clips,
        IReadOnlyList<AudioTransition> transitions)
    {
        if (transitions.Count == 0) return null;
        var result = new Dictionary<string, ClipFades>();
        foreach (var transition in transitions)
        {
            if (AudioTransitionOperations.Span(clips, transition) is not { } span ||
                !span.Clip.Enabled || span.Right?.Enabled == false) continue;
            var primary = result.GetValueOrDefault(span.Clip.Id);
            result[span.Clip.Id] = transition.Kind == AudioTransitionOperations.FadeIn
                ? primary with { FadeIn = span } : primary with { FadeOut = span };
            if (span.Right is { } right)
                result[right.Id] = result.GetValueOrDefault(right.Id) with { FadeIn = span };
        }
        return result;
    }

    internal AudioMixCommand PrepareProcess(double previewSpeed = 1, string? waveOutput = null, bool floatingPoint = false) =>
        new(BuildProcess(previewSpeed, waveOutput, floatingPoint));

    internal AudioMixCommand PreparePreviewWindow(AudioPreviewWindows.Window window) =>
        new(BuildProcess(1, null, true, window));

    public ProcessStartInfo BuildProcess(double previewSpeed = 1, string? waveOutput = null, bool floatingPoint = false) =>
        BuildProcess(previewSpeed, waveOutput, floatingPoint, null);

    private ProcessStartInfo BuildProcess(double previewSpeed, string? waveOutput, bool floatingPoint,
        AudioPreviewWindows.Window? previewWindow)
    {
        previewSpeed = Math.Clamp(previewSpeed, 0.5, 3);
        var info = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        void Add(params string[] args) { foreach (var arg in args) info.ArgumentList.Add(arg); }
        Add("-nostdin", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo");
        foreach (var input in Inputs)
            Add("-ss", N(input.SourceStart), "-i", input.Path);
        var parts = new List<string> { $"[0:a]atrim=duration={N(DurationSeconds)},asetpts=PTS-STARTPTS[base]" };
        for (var i = 0; i < Inputs.Count; i++)
        {
            var input = Inputs[i];
            var tempo = input.Speed == 1 && (input.Envelope != default || previewWindow.HasValue) ? "" : TempoFilters(input.Speed);
            var rampFilters = EnvelopeFilters(input.Envelope, StartSeconds + input.TimelineDelay);
            var limit = $",atrim=end_sample={Math.Max(1, (long)Math.Round(input.SourceDuration / input.Speed * SampleRate))}";
            var left = input.Balance > 0 ? 1 - input.Balance : 1;
            var right = input.Balance < 0 ? 1 + input.Balance : 1;
            var delaySamples = (long)Math.Round(input.TimelineDelay * 48000);
            parts.Add($"[{i + 1}:{input.StreamOrdinal}]atrim=duration={N(input.SourceDuration)},asetpts=PTS-STARTPTS," +
                tempo + $"aresample=48000,aformat=channel_layouts=stereo{input.EffectChain}{limit},volume={N(Math.Clamp(input.GainDb, -120, 24))}dB{rampFilters}," +
                $"pan=stereo|c0={N(left)}*c0|c1={N(right)}*c1,adelay={delaySamples}S|{delaySamples}S[a{i}]");
        }
        var groupLabels = new Dictionary<string, string>();
        foreach (var group in Groups.OrderByDescending(item => item.Depth))
        {
            var members = Enumerable.Range(0, Inputs.Count)
                .Where(i => Inputs[i].ParentGroupId == group.Id).Select(i => $"[a{i}]")
                .Concat(Groups.Where(child => child.ParentId == group.Id && groupLabels.ContainsKey(child.Id))
                    .Select(child => groupLabels[child.Id])).ToArray();
            if (members.Length == 0) continue;
            var output = $"[g{groupLabels.Count}]";
            var mix = members.Length == 1 ? "anull" :
                $"amix=inputs={members.Length}:duration=longest:dropout_transition=0:normalize=0";
            parts.Add(string.Concat(members) + mix + group.EffectChain +
                $",atrim=duration={N(Math.Max(0, group.VisibleEndSeconds - StartSeconds))}" +
                EnvelopeFilters(group.Envelope, StartSeconds) + output);
            groupLabels[group.Id] = output;
        }
        var rootInputs = Enumerable.Range(0, Inputs.Count).Where(i => Inputs[i].ParentGroupId == null)
            .Select(i => $"[a{i}]").Concat(Groups.Where(group => group.ParentId == null &&
                groupLabels.ContainsKey(group.Id)).Select(group => groupLabels[group.Id])).ToArray();
        var labels = "[base]" + string.Concat(rootInputs);
        var previewTempo = Math.Abs(previewSpeed - 1) < 0.001 ? "" : "," + TempoFilters(previewSpeed).TrimEnd(',');
        var windowTrim = previewWindow is { } window
            ? $",atrim=start_sample={window.DiscardSamples}:end_sample={window.DiscardSamples + window.Samples},asetpts=PTS-STARTPTS" : "";
        parts.Add($"{labels}amix=inputs={rootInputs.Length + 1}:duration=first:dropout_transition=0:normalize=0," +
            $"volume={N(Math.Clamp(MasterGainDb, -120, 24))}dB{previewTempo}{windowTrim}[mix]");
        var outputDuration = previewWindow is { } sliced ? sliced.Samples / (double)SampleRate : DurationSeconds / previewSpeed;
        Add("-filter_complex", string.Join(';', parts), "-map", "[mix]", "-t", N(outputDuration),
            "-f", waveOutput == null ? floatingPoint ? "f32le" : "s16le" : "wav",
            "-acodec", floatingPoint ? "pcm_f32le" : "pcm_s16le", "-ac", "2", "-ar", "48000");
        if (waveOutput == null) Add("pipe:1");
        else
        {
            info.RedirectStandardOutput = false;
            Add("-rf64", "auto", "-y", waveOutput);
        }
        return info;
    }

    private static string EnvelopeFilters(AudioMixEnvelope envelope, double origin)
    {
        if (envelope == default) return "";
        var fades = new List<(string Type, long Start, long End)>(2);
        long Samples(double time) => (long)Math.Round(time * SampleRate);
        var first = Samples(origin);
        if (envelope.FadeIn is { } up && Samples(up.End) > first)
            fades.Add(("in", Samples(up.Start) - first, Samples(up.End) - first));
        if (envelope.FadeOut is { } down)
        {
            if (Samples(down.End) <= first) return ",volume=0";
            fades.Add(("out", Samples(down.Start) - first, Samples(down.End) - first));
        }
        if (fades.Count == 0) return "";

        // afade uses sample timestamps. Offset them when seeking inside a ramp
        // so the envelope resumes at the cut instead of restarting.
        var prefix = Math.Max(0, -fades.Min(fade => fade.Start));
        var filters = prefix > 0 ? $",asetpts=PTS-STARTPTS+{prefix}/SR/TB" : "";
        foreach (var fade in fades)
            filters += $",afade=t={fade.Type}:ss={fade.Start + prefix}:ns={Math.Max(1, fade.End - fade.Start)}:curve=tri";
        return prefix > 0 ? filters + ",asetpts=PTS-STARTPTS" : filters;
    }

    private static string TempoFilters(double speed)
    {
        var filters = new List<string>();
        while (speed > 2) { filters.Add("atempo=2,"); speed /= 2; }
        while (speed < 0.5) { filters.Add("atempo=0.5,"); speed /= 0.5; }
        filters.Add($"atempo={N(speed)},");
        return string.Concat(filters);
    }

    private static string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
