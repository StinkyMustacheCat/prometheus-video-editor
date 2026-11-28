using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Prometheus.Core.Model;

namespace Prometheus.Client.Services;

/// <summary>Streams mixed timeline PCM directly to the Windows audio device.</summary>
public sealed class AudioPreviewService
{
    private readonly object _gate = new();
    private Session? _session;
    public event Action<string>? Failed;

    private sealed class Session
    {
        public required double Start;
        public required double PreviewSpeed;
        public PlaybackDiagnostics.Run? Measurement;
        public readonly CancellationTokenSource Cancellation = new();
        public WaveOutEvent? Output;
        public PcmPipeProvider? Provider;
        public Task? Startup;
        public Task? Pump;
        public bool Ready;
        public bool Failed;
        public StereoLevel MasterLevel;
    }

    internal sealed class PcmPipeProvider : IWaveProvider
    {
        private readonly BufferedWaveProvider _buffered;
        private readonly Action<StereoLevel> _report;
        private Action? _firstAudio;
        private readonly Action? _underrun;
        private readonly object _positionGate = new();
        private readonly Queue<(long Start, long End)> _padding = new();
        private long _writtenBytes, _playedPadding;
        private volatile bool _completed;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public PcmPipeProvider(Action<StereoLevel> report, Action? firstAudio = null, Action? underrun = null)
        {
            _report = report;
            _firstAudio = firstAudio;
            _underrun = underrun;
            _buffered = new BufferedWaveProvider(WaveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                ReadFully = false
            };
        }
        public int BufferedBytes => _buffered.BufferedBytes;
        public int FreeBytes => _buffered.BufferLength - _buffered.BufferedBytes;
        public void Add(byte[] buffer, int count) => _buffered.AddSamples(buffer, 0, count);
        public void Complete() => _completed = true;
        public long MediaBytesAt(long deviceBytes)
        {
            lock (_positionGate)
            {
                while (_padding.TryPeek(out var gap) && gap.End <= deviceBytes)
                {
                    _playedPadding += gap.End - gap.Start;
                    _padding.Dequeue();
                }
                var currentPadding = _padding.TryPeek(out var pending) ? Math.Max(0, deviceBytes - pending.Start) : 0;
                return Math.Max(0, Math.Min(deviceBytes, _writtenBytes) - _playedPadding - currentPadding);
            }
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            var read = _buffered.Read(buffer, offset, count);
            var submitted = _completed ? read : count;
            lock (_positionGate)
            {
                if (submitted > read) _padding.Enqueue((_writtenBytes + read, _writtenBytes + submitted));
                _writtenBytes += submitted;
            }
            if (submitted > read)
            {
                Array.Clear(buffer, offset + read, submitted - read);
                _underrun?.Invoke();
            }
            if (read >= 8) Interlocked.Exchange(ref _firstAudio, null)?.Invoke();
            double left = 0, right = 0;
            for (var i = offset; i + 7 < offset + read; i += 8)
            {
                left = Math.Max(left, Math.Abs(BitConverter.ToSingle(buffer, i)));
                right = Math.Max(right, Math.Abs(BitConverter.ToSingle(buffer, i + 4)));
            }
            _report(new StereoLevel(left, right));
            return submitted;
        }
    }

    public bool IsStarting
    {
        get { lock (_gate) return _session is { Ready: false, Failed: false }; }
    }

    public bool HasEnded
    {
        get { lock (_gate) return _session is { Ready: true, Failed: false, Output.PlaybackState: PlaybackState.Stopped }; }
    }

    public double? PlaybackSeconds
    {
        get
        {
            lock (_gate)
            {
                var session = _session;
                if (session is not { Ready: true, Failed: false } || session.Output == null) return null;
                try { return session.Start + session.Provider!.MediaBytesAt(session.Output.GetPosition()) / 384000.0 * session.PreviewSpeed; }
                catch { return null; }
            }
        }
    }

    public StereoLevel MasterLevel
    {
        get { lock (_gate) return _session?.MasterLevel ?? default; }
    }

    public void Stop()
    {
        Session? old;
        lock (_gate) { old = _session; _session = null; }
        if (old == null) return;
        var stopping = old.Cancellation.CancelAsync();
        try { old.Output?.Stop(); } catch { }
        _ = Task.Run(async () => {
            try { await stopping; } catch { }
            if (old.Startup != null) try { await old.Startup; } catch { }
            if (old.Pump != null) try { await old.Pump; } catch { }
            DisposeSession(old);
        });
    }

    public void PlayAt(Project project, double time, double previewSpeed = 1) => PlayAt(project, time, previewSpeed, null);

    internal void PlayAt(Project project, double time, double previewSpeed, PlaybackDiagnostics.Run? diagnostics)
    {
        Stop();
        var sequence = project.ActiveSequence;
        if (sequence == null || sequence.DurationSeconds <= time) return;
        var audibleTracks = sequence.Tracks.Where(track => track.Kind == "audio" && track.Enabled).Select(track => track.Id).ToHashSet();
        if (!sequence.Clips.Any(clip => clip.Enabled && audibleTracks.Contains(clip.TrackId) && clip.EndSeconds > time)) return;
        var session = new Session { Start = time, PreviewSpeed = previewSpeed, Measurement = diagnostics };
        lock (_gate)
        {
            _session = session;
            session.Startup = Task.Run(() => StartAsync(session, project, sequence));
        }
    }

    private async Task StartAsync(Session session, Project project, Sequence sequence)
    {
        var cancellation = session.Cancellation.Token;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var windowed = AudioPreviewWindows.CanSplit(sequence, session.PreviewSpeed);
            var continuous = windowed ? null : AudioMixPlan.Create(project, session.Start);
            if (continuous?.Inputs.Count == 0)
            {
                lock (_gate) if (ReferenceEquals(_session, session)) session.Failed = true;
                return;
            }
            var provider = session.Provider = new PcmPipeProvider(level => {
                lock (_gate) if (ReferenceEquals(_session, session)) session.MasterLevel = level;
            }, session.Measurement is { } measurement ? measurement.AudioReady : null,
                session.Measurement == null ? null : () => session.Measurement.Count("audio_underruns"));
            session.Pump = PumpAsync(session, provider, project, sequence.DurationSeconds, continuous);
            var initialBytes = windowed ? 38_400 : 96_000;
            var deadline = Stopwatch.StartNew();
            while (provider.BufferedBytes < initialBytes && !session.Pump.IsCompleted)
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(10))
                    throw new TimeoutException("El dispositivo de audio no recibió muestras de la mezcla.");
                await Task.Delay(10, cancellation).ConfigureAwait(false);
            }
            cancellation.ThrowIfCancellationRequested();
            if (session.Failed) return;
            var output = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };
            session.Output = output;
            output.Init(provider);
            lock (_gate)
            {
                if (!ReferenceEquals(_session, session) || session.Cancellation.IsCancellationRequested || session.Failed) return;
                output.Play();
                session.Ready = true;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { ReportFailure(session, ex); }
    }

    private async Task PumpAsync(Session session, PcmPipeProvider provider, Project project, double end, AudioMixPlan? continuous)
    {
        var cancellation = session.Cancellation.Token;
        var chunk = new byte[16_384];
        try
        {
            if (continuous != null)
            {
                session.Measurement?.Count("audio_continuous");
                using var command = continuous.PrepareProcess(session.PreviewSpeed, floatingPoint: true);
                await AppendAsync(command, null).ConfigureAwait(false);
            }
            else foreach (var window in AudioPreviewWindows.At(session.Start, end))
            {
                cancellation.ThrowIfCancellationRequested();
                var plan = AudioMixPlan.Create(project, window.RenderStart, window.RenderEnd, null);
                using var command = plan.PreparePreviewWindow(window);
                session.Measurement?.Count("audio_windows");
                // The next decoder starts with the previous window's tail still in the same device buffer.
                await AppendAsync(command, window.Samples * 8).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { ReportFailure(session, error); }
        finally { provider.Complete(); }

        async Task AppendAsync(AudioMixCommand command, int? expectedBytes)
        {
            cancellation.ThrowIfCancellationRequested();
            using var decoder = Process.Start(command.StartInfo) ?? throw new InvalidOperationException("FFmpeg no abrió el audio");
            using var stop = cancellation.Register(() => ExternalProcess.Kill(decoder));
            var diagnostics = ExternalProcess.ReadDiagnosticsAsync(decoder.StandardError);
            var stream = decoder.StandardOutput.BaseStream;
            var carry = 0;
            long total = 0;
            try
            {
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (provider.FreeBytes < chunk.Length)
                    {
                        await Task.Delay(8, cancellation).ConfigureAwait(false);
                        continue;
                    }
                    var read = await stream.ReadAsync(chunk.AsMemory(carry), cancellation).ConfigureAwait(false);
                    if (read == 0) break;
                    var aligned = (read + carry) & ~7;
                    if (aligned > 0) provider.Add(chunk, aligned);
                    total += aligned;
                    carry = read + carry - aligned;
                    if (carry > 0) Array.Copy(chunk, aligned, chunk, 0, carry);
                }
                await decoder.WaitForExitAsync(cancellation).ConfigureAwait(false);
                var detail = await diagnostics.ConfigureAwait(false);
                if (decoder.ExitCode != 0) throw new IOException("No se pudo reproducir el audio: " + detail.Trim());
                if (expectedBytes.HasValue && total != expectedBytes.Value)
                    throw new IOException("El tramo de audio no entregó el número esperado de muestras.");
            }
            finally
            {
                ExternalProcess.Kill(decoder);
                try { await decoder.WaitForExitAsync().ConfigureAwait(false); } catch (InvalidOperationException) { }
            }
        }
    }

    private void ReportFailure(Session session, Exception error)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_session, session) || session.Cancellation.IsCancellationRequested || session.Failed) return;
            session.Failed = true;
        }
        try { session.Output?.Stop(); } catch { }
        _ = session.Cancellation.CancelAsync();
        Failed?.Invoke(error.Message);
    }

    private static void DisposeSession(Session session)
    {
        try { session.Output?.Dispose(); } catch { }
        session.Cancellation.Dispose();
    }
}
