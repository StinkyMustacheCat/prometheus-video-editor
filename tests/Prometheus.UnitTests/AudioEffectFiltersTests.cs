using System;
using System.Diagnostics;
using System.Linq;
using Prometheus.Client.Services;
using Prometheus.Core.Model;

namespace Prometheus.UnitTests;

public class AudioEffectFiltersTests
{
    [Fact]
    public void EveryLegacyAudioFilterRunsOnBundledFfmpeg()
    {
        foreach (var definition in AudioEffects.All)
        {
            var clip = new Clip();
            AudioEffects.Add(clip, definition.Id);
            var chain = AudioEffectFilters.Build(clip.Effects).TrimStart(',');
            var start = new ProcessStartInfo(LocalMediaService.Tool("ffmpeg"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
                         "sine=frequency=440:duration=0.25", "-af", chain, "-f", "null", "-" })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(15000), $"FFmpeg no terminó: {definition.Id}");
            Assert.True(process.ExitCode == 0, $"{definition.Id}: {error}");
        }
    }

    [Fact]
    public void DisabledEffectsAreSkippedAndOrderIsPreserved()
    {
        var clip = new Clip();
        AudioEffects.Add(clip, AudioEffects.LowPassId);
        AudioEffects.Add(clip, AudioEffects.HighPassId);
        var chain = AudioEffectFilters.Build(clip.Effects);
        Assert.True(chain.IndexOf("lowpass", StringComparison.Ordinal) <
                    chain.IndexOf("highpass", StringComparison.Ordinal));
        clip.Effects.Single(e => e.EffectId == AudioEffects.LowPassId).Enabled = false;
        Assert.DoesNotContain("lowpass", AudioEffectFilters.Build(clip.Effects));
    }
}
